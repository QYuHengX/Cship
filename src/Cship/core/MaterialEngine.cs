using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cship.Ui.Themes;

namespace Cship.Core;

/// <summary>
/// 材质引擎（步骤 06；**2026-10-06 口径重写为"图层堆叠"**）。
///
/// **材质 = 系统合成器在窗口背后画的模糊（DWM）+ 本进程自绘的若干静态图层**
/// （板底色 / 噪点 / 液态玻璃的边缘高光与内阴影 / 发丝描边）。全进程不再有任何抓屏、位图或模糊着色器。
///
/// **为什么换掉抓屏式模拟通道**：旧实现把"窗口背后长什么样"用 GDI 抓进进程
/// （每窗一份整屏 1920×1080 位图 + BlurEffect，单次抓屏 20~35ms 且阻塞 DWM 合成），
/// 换来的是"内容永远是上一帧"的延迟与实机可感的卡顿；而"让系统把自己从抓屏里摘掉"这条路
/// 对本项目走不通（<c>WDA_EXCLUDEFROMCAPTURE</c> 对 <c>AllowsTransparency</c> 分层窗实测
/// 返回 FALSE / err=8），只能靠"自身矩形空洞"绕开自拍，复杂度与代价都高。
/// 现在改为**向系统要**：<c>SetWindowCompositionAttribute(ACCENT_ENABLE_BLURBEHIND)</c>
/// 由 DWM 实时合成，本进程零抓屏、零位图、零着色器，**延迟为 0（玻璃后面就是真实桌面）**。
///
/// **能力边界（本机 Win10 19045 实测，夹具 <c>build/_nattest</c> 与 <c>_nattest2</c>，见 STEP_LOG）**：
///   · 该 API 对本项目窗型（<c>AllowsTransparency</c> 分层窗）**有效**，可随时清除、无残留；
///   · 模糊**覆盖整个窗口矩形**，不尊重逐像素 alpha（全透明处照样被糊）；
///   · **形状裁不住**：`SetWindowRgn`（显示后设 / Show 前设 / 配 SWP_FRAMECHANGED 三种都试过）对模糊无效，
///     `DwmEnableBlurBehindWindow(hRgnBlur)` 在 Win10 已不出模糊（hr=S_OK 但画面无变化），
///     `DWMWA_WINDOW_CORNER_PREFERENCE` 本机返回 E_INVALIDARG（Win11 才有）。
///     故圆角外那圈模糊无法从系统侧消除，只能由 <see cref="Cship.Ui.Components.MaterialBackground"/>
///     的**四角羽化补丁**在视觉上磨掉（见该类注释）；
///   · 因此只有"整个窗口矩形都该是玻璃"的窗口才谈得上用它。
///
/// **当前启用状态**：**收纳板启用**（`BoardWindow` 里 `Material.UseNativeBlur = true`）。
/// "玻璃感"与"完美圆角"在当前 Windows 上不可兼得（圆角外必然露出一圈被糊的桌面），
/// 用户在这两者之间来回过一次，最终选玻璃感 + 四角羽化补丁磨棱角；
/// 关掉只需把那一行改成 false，四角立刻回到逐像素干净（补丁随之静默）。
/// </summary>
public static class MaterialEngine
{
    // **档位 id 与显示名的对应（2026-10-06 用户改名）**：
    //   none         → 显示名"无"（主题纯色板）
    //   acrylic      → "亚克力"
    //   blur         → **"柔化"**（原名"模糊"）
    //   liquidGlass  → **"瞎眼防弹玻璃"**（原名"液态玻璃"）
    // **id 一律不变**：它是 settings.json 与导出配置里的持久化值，改名只动 i18n 的 material.* 显示名。
    // 注释里提到"液态玻璃/模糊"时指的都是这套配方本身，与显示名无关。

    /// <summary>"无"材质（2026-10-06 口径二次修订）：**一块单纯的主题纯色板**——
    /// 只铺主题界面底色（<c>Color.Bg</c>，跟随"收纳板背景不透明度"），
    /// 不加噪点、不加高光内阴影、不加边缘渐深、不加液态玻璃装饰，也不开系统模糊。
    /// （首版误做成"什么都不画"，实机表现为"收纳板没有背景板"，按用户反馈改正。）</summary>
    public const string None = "none";
    public const string Acrylic = "acrylic";
    public const string Blur = "blur";
    public const string LiquidGlass = "liquidGlass";

    /// <summary>是否为"无材质"档。</summary>
    public static bool IsNone(string? material)
        => string.Equals(material, None, StringComparison.OrdinalIgnoreCase);

    // ---- 材质配方（00 §9 三档 + "无"）----

    /// <summary>亚克力：α≈70%、轻噪点。</summary>
    static readonly Color AcrylicLightTop = Color.FromRgb(0xFF, 0xFF, 0xFF);
    static readonly Color AcrylicLightBottom = Color.FromRgb(0xF0, 0xF4, 0xF8);
    static readonly Color AcrylicDarkTop = Color.FromRgb(0x06, 0x06, 0x06);
    static readonly Color AcrylicDarkBottom = Color.FromRgb(0x3B, 0x3B, 0x3B);
    const byte AcrylicA1 = 0xFC, AcrylicA2 = 0xEF, AcrylicADark = 0xE6;

    /// <summary>模糊：α≈50%、无噪点。</summary>
    static readonly Color BlurLight = Color.FromRgb(0xF9, 0xF9, 0xF9);
    static readonly Color BlurDark = Color.FromRgb(0x25, 0x25, 0x30);
    const byte BlurA = 0x99;

    /// <summary>液态玻璃主体（2026-10-06 重写）：三段式半透明渐变，上亮下暗、带一点冷调。</summary>
    static readonly Color LiquidLightTop = Color.FromRgb(0xFF, 0xFF, 0xFF);
    static readonly Color LiquidLightMid = Color.FromRgb(0xF6, 0xFA, 0xFF);
    static readonly Color LiquidLightBottom = Color.FromRgb(0xE3, 0xEC, 0xF8);
    static readonly Color LiquidDarkTop = Color.FromRgb(0x2A, 0x2A, 0x38);
    static readonly Color LiquidDarkMid = Color.FromRgb(0x1E, 0x1E, 0x2A);
    static readonly Color LiquidDarkBottom = Color.FromRgb(0x14, 0x14, 0x1E);
    const byte LiquidA1 = 0xF2, LiquidAMid = 0xE8, LiquidA2 = 0xD6;
    const byte LiquidADark1 = 0xE6, LiquidADarkMid = 0xD9, LiquidADark2 = 0xCC;

    /// <summary>材质档 → 噪点不透明度（亚克力 0.04 / 模糊 0 / 液态玻璃 0.035 / 无 0）。</summary>
    public static double NoiseOpacity(string material) => material switch
    {
        None => 0.0,
        Blur => 0.0,
        LiquidGlass => 0.035,
        _ => 0.04,
    };

    /// <summary>液态玻璃才有边缘高光/内阴影/边缘渐深这套"玻璃厚度"装饰；"无"什么都没有。</summary>
    public static bool HasGlassDepth(string material)
        => string.Equals(material, LiquidGlass, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 板底在"上/下"两端各自的等效纯色（含个人不透明度折算）。
    /// **用途**：四角羽化补丁只有几像素宽，该处的板底渐变近似常量，取所在端点的停靠色即可对齐观感。
    /// </summary>
    public static (Color Top, Color Bottom) FillEdgeColors(string material, bool dark, double opacityPercent)
    {
        double op = Math.Clamp(opacityPercent, 0, 100) / 100.0;
        if (IsNone(material))
        {
            // "无"档 = 主题纯色板：两端同色（Color.Bg）
            var bg = ThemeEngine.GetColor("Color.Bg");
            var solid = Scale(bg, bg.A, op);
            return (solid, solid);
        }
        if (string.Equals(material, Blur, StringComparison.OrdinalIgnoreCase))
        {
            var c = dark ? BlurDark : BlurLight;
            var solid = Scale(c, BlurA, op);
            return (solid, solid);
        }
        if (string.Equals(material, LiquidGlass, StringComparison.OrdinalIgnoreCase))
        {
            return dark
                ? (Scale(LiquidDarkTop, LiquidADark1, op), Scale(LiquidDarkBottom, LiquidADark2, op))
                : (Scale(LiquidLightTop, LiquidA1, op), Scale(LiquidLightBottom, LiquidA2, op));
        }
        return dark
            ? (Scale(AcrylicDarkTop, AcrylicADark, op), Scale(AcrylicDarkBottom, AcrylicADark, op))
            : (Scale(AcrylicLightTop, AcrylicA1, op), Scale(AcrylicLightBottom, AcrylicA2, op));
    }

    /// <summary>
    /// 材质底画刷（含个人不透明度折算）。收纳板/设置窗板底、悬浮窗三板与各级菜单都走这里，
    /// 保证"材质切换时各处视觉一致变化"。液态玻璃为三段式渐变，其余为两端渐变/纯色。
    /// </summary>
    public static Brush Fill(string material, bool dark, double opacityPercent)
    {
        double op = Math.Clamp(opacityPercent, 0, 100) / 100.0;
        if (IsNone(material))
        {
            var (top, _) = FillEdgeColors(material, dark, opacityPercent);
            return Solid(top);
        }
        if (string.Equals(material, Blur, StringComparison.OrdinalIgnoreCase))
        {
            var (top, _) = FillEdgeColors(material, dark, opacityPercent);
            return Solid(top);
        }
        if (string.Equals(material, LiquidGlass, StringComparison.OrdinalIgnoreCase))
        {
            return dark
                ? Gradient3(Scale(LiquidDarkTop, LiquidADark1, op), Scale(LiquidDarkMid, LiquidADarkMid, op), Scale(LiquidDarkBottom, LiquidADark2, op))
                : Gradient3(Scale(LiquidLightTop, LiquidA1, op), Scale(LiquidLightMid, LiquidAMid, op), Scale(LiquidLightBottom, LiquidA2, op));
        }
        var (a, b) = FillEdgeColors(material, dark, opacityPercent);
        return Gradient(a, b);
    }

    /// <summary>
    /// 液态玻璃的"玻璃厚度"装饰层配方（2026-10-06 重写）。把一层厚玻璃拆成可独立叠加的几层，
    /// 由 <see cref="Cship.Ui.Components.MaterialBackground"/> 与 <see cref="Thumbnail"/> 共用同一套值，
    /// 保证"设置页预览 = 实际观感"。
    /// </summary>
    /// <param name="Rim">内缘高光（1.5px 内描边）：上缘最亮、两侧转柔、下缘再catch一道亮——
    /// 这是"玻璃有厚度"的主要线索。</param>
    /// <param name="Specular">顶部镜面高光带：上缘一道亮白，向下 55% 内淡出。</param>
    /// <param name="Vignette">四周边缘渐深：中心透明、贴边微暗，把视线收进板内。</param>
    /// <param name="InnerShadow">底部内阴影：贴下缘的暗带，压出"玻璃坐在桌面上"的厚度。</param>
    public sealed record GlassRecipe(Brush Rim, Brush Specular, Brush Vignette, Brush InnerShadow);

    public static GlassRecipe LiquidGlassRecipe(bool dark)
    {
        // 内缘高光：0=上缘 1=下缘。上缘最亮（受光），两侧迅速转柔，下缘再起一道（玻璃底面的反光）
        var rim = dark
            ? GradientStops((Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), 0.0), (Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), 0.34),
                (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.72), (Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF), 1.0))
            : GradientStops((Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), 0.0), (Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), 0.34),
                (Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0.72), (Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), 1.0));

        var specular = dark
            ? GradientStops((Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.0), (Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF), 0.22),
                (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0))
            : GradientStops((Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), 0.0), (Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), 0.22),
                (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55), (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));

        var vignette = dark
            ? RadialStops((Color.FromArgb(0x00, 0, 0, 0), 0.0), (Color.FromArgb(0x00, 0, 0, 0), 0.5), (Color.FromArgb(0x38, 0, 0, 0), 1.0))
            : RadialStops((Color.FromArgb(0x00, 0, 0, 0), 0.0), (Color.FromArgb(0x00, 0, 0, 0), 0.5), (Color.FromArgb(0x1F, 0x0B, 0x14, 0x20), 1.0));

        var inner = dark
            ? GradientStops((Color.FromArgb(0x4D, 0, 0, 0), 1.0), (Color.FromArgb(0x00, 0, 0, 0), 0.72), (Color.FromArgb(0x00, 0, 0, 0), 0.0))
            : GradientStops((Color.FromArgb(0x24, 0, 0, 0), 1.0), (Color.FromArgb(0x00, 0, 0, 0), 0.72), (Color.FromArgb(0x00, 0, 0, 0), 0.0));

        return new GlassRecipe(rim, specular, vignette, inner);
    }

    /// <summary>板底发丝描边（00 §9）。</summary>
    public static Brush PlateBorder(bool dark) => ThemeEngine.Brush("Brush.PlateBorder");

    static Color Scale(Color c, byte baseAlpha, double opacity)
        => Color.FromArgb((byte)Math.Clamp(Math.Round(baseAlpha * opacity), 0, 255), c.R, c.G, c.B);

    static Brush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Brush Gradient(Color top, Color bottom)
    {
        var b = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        b.Freeze();
        return b;
    }

    static Brush Gradient3(Color top, Color mid, Color bottom)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(top, 0.0));
        b.GradientStops.Add(new GradientStop(mid, 0.45));
        b.GradientStops.Add(new GradientStop(bottom, 1.0));
        b.Freeze();
        return b;
    }

    static Brush GradientStops(params (Color Color, double Offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    static Brush RadialStops(params (Color Color, double Offset)[] stops)
    {
        var b = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.78,
            RadiusY = 0.78,
        };
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }

    // ---- 系统合成器模糊（真·图层堆叠；§1 口径 2026-10-06）----

    static readonly HashSet<IntPtr> BlurOn = new();
    static bool? _blurLogged;

    /// <summary>
    /// 开/关窗口背后的**系统模糊**（DWM 实时合成，本进程零开销）。幂等；返回是否处于目标态。
    /// <paramref name="force"/> 用于窗口 Hide→Show 后兜底重下发（系统若在隐藏期丢弃了强调策略）。
    /// 失败（老系统 / API 被移除）只记日志并返回 false —— 调用方保持纯静态材质层，**绝不黑底**。
    /// </summary>
    public static bool SetNativeBlur(Window window, bool on, bool force = false)
    {
        IntPtr hwnd;
        try { hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        catch { return false; }
        if (hwnd == IntPtr.Zero) return false;

        lock (BlurOn)
        {
            bool current = BlurOn.Contains(hwnd);
            if (current == on && !force) return on;
            if (!ApplyAccent(hwnd, on)) return false;
            if (on) BlurOn.Add(hwnd); else BlurOn.Remove(hwnd);
            return on;
        }
    }

    /// <summary>
    /// 收尾：撤掉模糊**并**清账本。必须真撤而不是只清账本——若窗口还活着（元素被移出视觉树后又回来），
    /// 只清账本会让"当前状态=关"这个记账与"模糊实际还开着"不一致，此后切到"无"档就不会再下发关闭。
    /// 窗口正在销毁时句柄可能已失效，失败静默（强调策略随 HWND 一起消失）。
    /// </summary>
    public static void ClearNativeBlur(Window window)
    {
        IntPtr hwnd;
        try { hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        catch { return; }
        if (hwnd == IntPtr.Zero) return;
        lock (BlurOn)
        {
            if (!BlurOn.Contains(hwnd)) return;
            ApplyAccent(hwnd, false);
            BlurOn.Remove(hwnd);
        }
    }

    static bool ApplyAccent(IntPtr hwnd, bool on)
    {
        var policy = new SystemBridge.AccentPolicy
        {
            AccentState = on ? SystemBridge.ACCENT_ENABLE_BLURBEHIND : SystemBridge.ACCENT_DISABLED,
            AccentFlags = 2, // 四边都画：否则模糊只落在边框带内
            GradientColor = 0, // 底色由本进程的 Tint 层给（主题/材质/不透明度都在那里），DWM 只出模糊
            AnimationId = 0,
        };
        int size = Marshal.SizeOf<SystemBridge.AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new SystemBridge.WindowCompositionAttributeData
            {
                Attribute = SystemBridge.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            int hr = SystemBridge.SetWindowCompositionAttribute(hwnd, ref data);
            if (hr == 0)
            {
                if (_blurLogged != false)
                {
                    _blurLogged = false;
                    Logger.Warn("材质系统模糊：SetWindowCompositionAttribute 调用失败（本系统不支持）→ 退回纯静态材质层");
                }
                return false;
            }
            if (_blurLogged != true)
            {
                _blurLogged = true;
                Logger.Info($"材质系统模糊：已启用（DWM 实时合成，本进程零抓屏零位图；窗口 0x{hwnd.ToInt64():X}）");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"材质系统模糊下发异常（退回纯静态材质层）：{ex.Message}");
            return false;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // ---- 64×64 噪点纹理（一次生成全进程共享）----

    static ImageBrush? _noise;

    /// <summary>程序生成 64×64 灰度噪点平铺画刷（固定种子，各材质档观感一致）。</summary>
    public static ImageBrush Noise()
    {
        if (_noise != null) return _noise;
        const int n = 64;
        var bmp = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[n * n * 4];
        var rnd = new Random(20260828);
        for (int i = 0; i < n * n; i++)
        {
            byte gray = (byte)rnd.Next(256);
            pixels[4 * i + 0] = gray;
            pixels[4 * i + 1] = gray;
            pixels[4 * i + 2] = gray;
            pixels[4 * i + 3] = 255;
        }
        bmp.WritePixels(new Int32Rect(0, 0, n, n), pixels, 4 * n, 0);
        bmp.Freeze();
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, n, n),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        _noise = brush;
        return brush;
    }

    // ---- 纹理离屏缓存（§1.4：≤12 张 / ≤64MB，LRU）----

    /// <summary>
    /// 纹理离屏缓存（材质预览缩略图）。键含材质+主题+尺寸档，满员按最久未用淘汰；
    /// 另设总字节上限，超出即整批清最旧。**只存设置页那几张预览图**（一次性离屏渲染），
    /// 不参与任何窗口绘制。
    /// </summary>
    public static class Textures
    {
        const int MaxEntries = 12;
        const long MaxBytes = 64L * 1024 * 1024;

        sealed class Entry { public required BitmapSource Bmp; public long Bytes; public long Access; }
        static readonly object Gate = new();
        static readonly Dictionary<string, Entry> Map = new(StringComparer.Ordinal);

        public static BitmapSource? Get(string key)
        {
            lock (Gate)
            {
                if (!Map.TryGetValue(key, out var e)) return null;
                e.Access = Stopwatch.GetTimestamp();
                return e.Bmp;
            }
        }

        public static void Put(string key, BitmapSource bmp)
        {
            long bytes = (long)bmp.PixelWidth * bmp.PixelHeight * 4;
            lock (Gate)
            {
                Map[key] = new Entry { Bmp = bmp, Bytes = bytes, Access = Stopwatch.GetTimestamp() };
                long total = 0;
                foreach (var e in Map.Values) total += e.Bytes;
                while (Map.Count > MaxEntries || total > MaxBytes)
                {
                    string? victim = null;
                    long oldest = long.MaxValue;
                    foreach (var kv in Map)
                        if (kv.Value.Access < oldest) { oldest = kv.Value.Access; victim = kv.Key; }
                    if (victim == null) break;
                    total -= Map[victim].Bytes;
                    Map.Remove(victim);
                }
            }
        }

        /// <summary>当前条目数 / 估算字节（诊断与验收用）。</summary>
        public static (int Count, long Bytes) Stats
        {
            get
            {
                lock (Gate)
                {
                    long total = 0;
                    foreach (var e in Map.Values) total += e.Bytes;
                    return (Map.Count, total);
                }
            }
        }
    }

    // ---- 材质预览缩略图（§1.6；走 Textures 缓存，不新增解码路径）----

    /// <summary>
    /// 离屏渲染 120×76 材质缩略图（设置页材质卡）：底色 + 噪点 + 液态玻璃的内缘高光/顶部镜面/边缘渐深/底部内阴影
    /// + 发丝描边。与 <see cref="Cship.Ui.Components.MaterialBackground"/> 共用同一套配方，**预览即实际**。
    /// </summary>
    public static BitmapSource Thumbnail(string material, bool dark, int w = 120, int h = 76)
    {
        string key = $"thumb|{material}|{(dark ? 1 : 0)}|{w}x{h}";
        var hit = Textures.Get(key);
        if (hit != null) return hit;

        var visual = new DrawingVisual();
        var rect = new Rect(0, 0, w, h);
        double radius = 10;
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(Fill(material, dark, 100), null, rect, radius, radius);
            double noise = NoiseOpacity(material);
            if (noise > 0)
            {
                dc.PushOpacity(noise);
                dc.DrawRoundedRectangle(Noise(), null, rect, radius, radius); // 噪点按材质档透明度叠一层
                dc.Pop();
            }
            if (HasGlassDepth(material))
            {
                var recipe = LiquidGlassRecipe(dark);
                dc.DrawRoundedRectangle(recipe.Vignette, null, rect, radius, radius);
                dc.DrawRoundedRectangle(recipe.Specular, null, rect, radius, radius);
                dc.DrawRoundedRectangle(recipe.InnerShadow, null, rect, radius, radius);
                dc.DrawRoundedRectangle(null, new Pen(recipe.Rim, 1.5),
                    new Rect(0.75, 0.75, w - 1.5, h - 1.5), radius, radius);
            }
            dc.DrawRoundedRectangle(null, new Pen(PlateBorder(dark), 1), new Rect(0.5, 0.5, w - 1, h - 1), radius, radius);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        Textures.Put(key, rtb);
        return rtb;
    }
}
