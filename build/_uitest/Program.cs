using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cship.Core;
using Cship.Ui.Pages;

namespace CshipCheck;

/// <summary>
/// 临时验收夹具（不入交付、不在 Cship.sln 内）：跑在**自己的** dependencies\config 里，
/// 不碰开发态/用户态的 settings.json 与 state.json。
///   config 参数 = 配置导入导出往返自检（含"导出键集合 == 可导入键集合"与负例）
///   render 参数 = 主题预览卡离屏渲染出图（4 种选中态各一张 PNG，2x 缩放便于放大看细节）
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string mode = args.Length > 0 ? args[0] : "all";

        Paths.EnsureDirs();
        Logger.Init();
        SettingsStore.Init(Paths.SettingsFile());
        StateStore.Init(Paths.StateFile());
        SettingsStore.Instance.Flush();
        I18n.Init(SettingsStore.Instance.GetString("general.language", "zh-CN"));
        Console.WriteLine($"[运行目录] {Paths.ConfigDir()}");

        int fail = 0;
        if (mode is "all" or "config") fail += ConfigRoundTrip();
        if (mode is "all" or "render") fail += RenderThemeCards();
        if (mode is "all" or "click") fail += ClickThemeCard();
        if (mode is "all" or "dock") fail += DockLayerOpacity();
        if (mode is "all" or "dockbind") fail += DockLayerBinding();
        if (mode is "all" or "pers") fail += PersonalizePageSmoke();
        if (mode is "all" or "about") fail += AboutPageSmoke();
        Console.WriteLine(fail == 0 ? "== ALL PASS ==" : $"== FAILED（{fail} 条）==");
        SettingsStore.Instance.Flush();
        StateStore.Instance.Flush();
        return fail;
    }

    // ---------------- F. 层滑轨 ↔ 板 绑定（2026-10-06 用户口径：上=右下角那层） ----------------

    /// <summary>
    /// 自检"界面层序 [上,中,下] → 板序 [front(左上), mid, back(右下)]"的对调：把三层不透明度设成
    /// [0,50,100]（上/中/下）后，**右下角那块板应最透**（上=0）、左上那块应为配方基准（下=100）。
    /// 走真实 <see cref="Cship.Ui.FloatingDock"/> 构造路径（其 ApplyDockCustom 是唯一映射点）。
    /// </summary>
    static int DockLayerBinding()
    {
        Console.WriteLine("\n==== F. 层滑轨 ↔ 板 绑定 ====");
        int fail = 0;
        var s = SettingsStore.Instance;
        s.SetString("personal.dockCustom.mode", "preset");
        s.SetString("personal.dockCustom.material", MaterialEngine.Blur);
        s.Set("personal.dockCustom.opacity", System.Text.Json.JsonSerializer.SerializeToElement(new[] { 0.0, 50.0, 100.0 }));

        var dock = new Cship.Ui.FloatingDock();
        var plate = (Cship.Ui.Components.SquarePlate)dock.FindName("Plate")!;
        var front = (System.Windows.Controls.Border)plate.FindName("PlateFront")!; // 左上（画在最上）
        var mid = (System.Windows.Controls.Border)plate.FindName("PlateMid")!;
        var back = (System.Windows.Controls.Border)plate.FindName("PlateBack")!;  // 右下（画在最下）
        int A(System.Windows.Controls.Border b) => (b.Background as SolidColorBrush)?.Color.A ?? -1;
        Console.WriteLine($"[α] 左上={A(front)} 中={A(mid)} 右下={A(back)}（上=0%→右下 0；中=50%→≈76；下=100%→左上 153）");
        if (A(back) != 0) { Console.WriteLine("[FAIL] 上层滑轨（0%）未作用在右下角那层"); fail++; }
        if (Math.Abs(A(mid) - 76.5) > 1.5) { Console.WriteLine("[FAIL] 中层滑轨未作用在中间那层"); fail++; }
        if (A(front) != 153) { Console.WriteLine("[FAIL] 下层滑轨（100%）未作用在左上那层"); fail++; }

        // 层图与层不透明度同一份对调：上层图（index 0）必须落在右下角那层
        var px = new byte[4] { 0xFF, 0x20, 0x80, 0xE0 };
        string name = "layerbind_probe.png";
        string path = System.IO.Path.Combine(Paths.AssetsDir(), name);
        using (var fs = System.IO.File.Create(path))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, px, 4)));
            enc.Save(fs);
        }
        s.SetString("personal.dockCustom.mode", "custom");            // 写键即广播 → FloatingDock.ApplyDockCustom
        s.Set("personal.dockCustom.images", System.Text.Json.JsonSerializer.SerializeToElement(new[] { name, "", "" }));
        Console.WriteLine($"[层图] 上层图落在：左上={front.Background is ImageBrush} 中={mid.Background is ImageBrush} 右下={back.Background is ImageBrush}");
        if (!(back.Background is ImageBrush)) { Console.WriteLine("[FAIL] 上层图片未落在右下角那层"); fail++; }
        if (front.Background is ImageBrush || mid.Background is ImageBrush) { Console.WriteLine("[FAIL] 上层图片落错层"); fail++; }
        try { System.IO.File.Delete(path); } catch { }
        return fail;
    }

    // ---------------- E. 个性化页"各层不透明度"常驻行（2026-10-06） ----------------

    /// <summary>
    /// 自检个性化页改版：三层不透明度滑块**不再是自定义面板的行内控件**，而是与预设/自定义
    /// 两个面板平级的常驻行（两种模式都看得见、都写同一个键），且拖动即时写入三值数组。
    /// </summary>
    static int PersonalizePageSmoke()
    {
        Console.WriteLine("\n==== E. 个性化页 · 各层不透明度常驻行 ====");
        int fail = 0;
        var page = new Cship.Ui.Pages.PersonalizePage();
        _ = new Cship.Ui.Pages.PersonalizePage(); // 主题翻转会整页重建：再建一次确认无状态残留

        var sliders = new List<Cship.Ui.Components.SliderX>();
        Collect(page, sliders);
        Console.WriteLine($"[滑轨数] {sliders.Count}（背景图不透明度 1 + 各层不透明度 3 = 4）");
        if (sliders.Count != 4) { Console.WriteLine("[FAIL] 滑轨数量不符（改版后应为 4）"); fail++; }

        var layerKeys = new[] { "set.layer.top", "set.layer.mid", "set.layer.bottom" };
        var found = new Dictionary<string, Cship.Ui.Components.SliderX>();
        foreach (var slider in sliders)
        {
            var row = System.Windows.LogicalTreeHelper.GetParent(slider) as System.Windows.Controls.Grid;
            if (row == null) continue;
            foreach (var child in row.Children)
                if (child is System.Windows.Controls.TextBlock tb)
                    foreach (var key in layerKeys)
                        if (tb.Text == I18n.Tr(key)) found[key] = slider;
        }
        Console.WriteLine($"[层滑轨] 命中 {found.Count}/3（上/中/下）");
        if (found.Count != 3) { Console.WriteLine("[FAIL] 未找到三层的常驻不透明度滑轨"); return fail + 1; }

        // 常驻性：滑块所在行与"预设面板/自定义面板"同为分节正文的直接子级（不是面板内部的行）
        var topSlider = found["set.layer.top"];
        var rowGrid = System.Windows.LogicalTreeHelper.GetParent(topSlider) as System.Windows.Controls.Grid;
        var body = System.Windows.LogicalTreeHelper.GetParent(rowGrid!) as System.Windows.Controls.StackPanel;
        int panels = 0;
        if (body != null)
            foreach (var child in body.Children)
                if (child is System.Windows.Controls.StackPanel sp && !IsAncestorOf(sp, topSlider)) panels++;
        Console.WriteLine($"[常驻性] 与滑块平级的面板数 = {panels}（预设 + 自定义 = 2，说明不随模式隐藏）");
        if (panels < 2) { Console.WriteLine("[FAIL] 不透明度行仍在模式面板内部（切到预设就看不见）"); fail++; }

        // 拖动即写三值数组（顶层设为 42%）
        SettingsStore.Instance.Set("personal.dockCustom.opacity", System.Text.Json.JsonSerializer.SerializeToElement(new[] { 100.0, 100.0, 100.0 }));
        topSlider.Value = 42;
        SettingsStore.Instance.Flush();
        string written = SettingsStore.Instance.GetRawValue("personal.dockCustom.opacity").GetRawText();
        Console.WriteLine($"[写入] 顶层滑轨 → personal.dockCustom.opacity={written}（期望 [42,100,100]）");
        if (written != "[42,100,100]") { Console.WriteLine("[FAIL] 层滑轨未写入三值数组"); fail++; }
        return fail;
    }

    static bool IsAncestorOf(System.Windows.DependencyObject ancestor, System.Windows.DependencyObject node)
    {
        for (var cur = node; cur != null; cur = System.Windows.LogicalTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, ancestor)) return true;
        return false;
    }

    static void Collect<T>(System.Windows.DependencyObject root, List<T> into)
    {
        if (root is T hit) into.Add(hit);
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
            if (child is System.Windows.DependencyObject d)
                Collect(d, into);
    }

    // ---------------- D. 悬浮窗各层不透明度（预设/自定义共用，2026-10-06） ----------------

    /// <summary>
    /// 自检"personal.dockCustom.opacity 对预设模式同样生效"：无层图时各层底色取材质配方，
    /// 按该层不透明度折算 α；装饰（噪点/内高光）随层淡出；自定义层图**不重复折算**
    /// （调用方设在 ImageBrush 上的 Opacity 必须原样保留）。
    /// </summary>
    static int DockLayerOpacity()
    {
        Console.WriteLine("\n==== D. 悬浮窗各层不透明度 ====");
        int fail = 0;
        var plate = new Cship.Ui.Components.SquarePlate { PlateSize = 56 };
        plate.SetTheme(false);              // 亮色主题：柔化档底 α = 0x99 = 153
        plate.Material = MaterialEngine.Blur;
        plate.ApplyCustomLayers(new Brush?[3]);              // 预设模式（无层图）
        plate.SetLayerOpacities(new[] { 0.0, 50.0, 100.0 }); // [上,中,下]

        var front = (System.Windows.Controls.Border)plate.FindName("PlateFront")!;
        var mid = (System.Windows.Controls.Border)plate.FindName("PlateMid")!;
        var back = (System.Windows.Controls.Border)plate.FindName("PlateBack")!;
        int A(System.Windows.Controls.Border b) => (b.Background as SolidColorBrush)?.Color.A ?? -1;
        double G(System.Windows.Controls.Border b) => ((System.Windows.Controls.Grid)b.Child).Opacity;
        Console.WriteLine($"[预设层 α] 上={A(front)} 中={A(mid)} 下={A(back)}（期望 0 / ≈76 / 153）");
        Console.WriteLine($"[装饰层透明度] 上={G(front)} 中={G(mid)} 下={G(back)}（期望 0 / 0.5 / 1）");
        if (A(front) != 0) { Console.WriteLine("[FAIL] 上层调到 0% 后底仍不透明"); fail++; }
        if (Math.Abs(A(mid) - 76.5) > 1.5) { Console.WriteLine("[FAIL] 中层 50% 未按比例折算 α"); fail++; }
        if (A(back) != 153) { Console.WriteLine("[FAIL] 下层 100% 应与配方基准 α 一致"); fail++; }
        if (G(front) != 0 || Math.Abs(G(mid) - 0.5) > 0.001 || G(back) != 1) { Console.WriteLine("[FAIL] 装饰层未随层淡出"); fail++; }

        // 层图：不重复折算（ImageBrush 自带 Opacity=0.5，SquarePlate 不得再乘一次）
        var px = new byte[4] { 0xFF, 0x20, 0x80, 0xE0 };
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, px, 4);
        var img = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill, Opacity = 0.5 };
        plate.ApplyCustomLayers(new Brush?[] { img, null, null });
        plate.SetLayerOpacities(new[] { 50.0, 50.0, 50.0 });
        double imgOpacity = ((ImageBrush)front.Background).Opacity;
        Console.WriteLine($"[层图折算] ImageBrush.Opacity={imgOpacity}（期望 0.5，不得被二次折算成 0.25）");
        if (Math.Abs(imgOpacity - 0.5) > 0.001) { Console.WriteLine("[FAIL] 层图不透明度被重复折算"); fail++; }

        // 原始预设未被改写：同一材质档在 100% 下的配方基准 α 仍是 153
        int baseAlpha = ((SolidColorBrush)MaterialEngine.Fill(MaterialEngine.Blur, false, 100)).Color.A;
        Console.WriteLine($"[原始预设] MaterialEngine.Fill(blur,100%) α={baseAlpha}（期望 153，配方本身不被用户调整改写）");
        if (baseAlpha != 153) { Console.WriteLine("[FAIL] 材质配方被改写"); fail++; }
        return fail;
    }

    // ---------------- A. 配置导入导出往返 ----------------

    static int ConfigRoundTrip()
    {
        Console.WriteLine("\n==== A. 配置导入导出往返 ====");
        int fail = 0;
        var settings = SettingsStore.Instance;
        var state = StateStore.Instance;

        // 模拟"当前设置项生态"：一个删功能留下的残留键 + 一个后续步骤（06）才会新增的键
        // ——两者都不在 00 §8 默认表里，旧实现"导得出去、导不回来"。
        settings.SetString("personal.dockCustom.colors", "#FF112233");
        settings.SetDouble("theme.engine.grain", 0.35);
        // 收纳板布局（真实裸键名，配置里写成 state.<裸键名>）
        state.SetCategories(new List<Cship.Models.CategoryModel>
        {
            new() { Id = "apps", Builtin = true, Type = "apps", Items = { "a.txt" } },
        });
        state.SetIconOrder(new Dictionary<string, (int R, int C)> { ["a.txt"] = (0, 0), ["b.lnk"] = (0, 1) });
        state.SetRemovedItems(new[] { "c.txt" });
        state.SetVirtualItems(new[] { (@"D:\somewhere\y.lnk", "Y") });
        state.SetPoint2("dock.pos", 111, 222);            // 机器相关：不得入配置
        state.SetPoint2("board.size.up", 1000, 600);      // 机器相关：不得入配置

        string file = Path.Combine(Paths.ConfigDir(), "export_check.txt");
        ConfigPort.Export(file);
        string text = File.ReadAllText(file);
        Console.WriteLine("---- 导出内容 ----");
        Console.WriteLine(text.TrimEnd());
        Console.WriteLine("---- /导出内容 ----");

        foreach (var key in new[] { "personal.dockCustom.colors=", "theme.engine.grain=" })
            if (!text.Contains(key)) { Console.WriteLine($"[FAIL] 生态内键未导出：{key.TrimEnd('=')}"); fail++; }
        foreach (var key in new[] { "state.categories=", "state.iconOrder=", "state.removedItems=", "state.virtualItems=" })
            if (!text.Contains(key)) { Console.WriteLine($"[FAIL] 收纳板布局未导出：{key.TrimEnd('=')}"); fail++; }
        foreach (var key in new[] { "state.dock.pos", "state.board.size.up", "state.firstRunDone" })
            if (text.Contains(key)) { Console.WriteLine($"[FAIL] 机器相关键不该入配置：{key}"); fail++; }

        // 往返：自己导出的文件，每一项都必须被自己接受
        var parsed = ConfigPort.Parse(text);
        Console.WriteLine($"[往返] 应用 {parsed.Apply.Count} 项 / 忽略 {parsed.Ignored} 项"
            + (parsed.IgnoredKeys.Count > 0 ? "（" + string.Join("、", parsed.IgnoredKeys) + "）" : ""));
        if (parsed.Ignored != 0) { Console.WriteLine("[FAIL] 自己导出的文件被自己忽略"); fail++; }
        if (!parsed.Apply.Exists(e => e.Key == "state.iconOrder"))
        { Console.WriteLine("[FAIL] 往返结果缺少 state.iconOrder"); fail++; }

        // 应用后必须落到真实裸键上（旧实现写的是 state.iconOrder 这个没人读的假键）
        ConfigPort.Apply(parsed.Apply);
        SettingsStore.Instance.Flush();
        StateStore.Instance.Flush();
        if (state.GetIconOrder().Count != 2) { Console.WriteLine("[FAIL] 导入未落到 iconOrder 裸键"); fail++; }
        if (!state.GetRemovedItems().Contains("c.txt")) { Console.WriteLine("[FAIL] 导入未落到 removedItems 裸键"); fail++; }
        if (state.GetVirtualItems().Count != 1) { Console.WriteLine("[FAIL] 导入未落到 virtualItems 裸键"); fail++; }
        if (state.GetCategories().Count != 1) { Console.WriteLine("[FAIL] 导入未落到 categories 裸键"); fail++; }
        if (state.GetRawValue("state.iconOrder").ValueKind != System.Text.Json.JsonValueKind.Undefined)
        { Console.WriteLine("[FAIL] state.json 被写入了假键 state.iconOrder"); fail++; }

        // 负例：未知键 / 未纳入配置的 state 键 / 非法值 / 垃圾行
        var bad = ConfigPort.Parse("unknown.key=1\nstate.dock.pos=[1,2]\ngeneral.fps={oops}\nnonsense line\n");
        Console.WriteLine($"[负例] 忽略 {bad.Ignored} 项 / 可应用 {bad.Apply.Count} 项 / 键：{string.Join("、", bad.IgnoredKeys)}");
        if (bad.Ignored != 4 || bad.Apply.Count != 0) { Console.WriteLine("[FAIL] 负例计数不符（期望 忽略 4 / 应用 0）"); fail++; }

        string summary = ConfigPort.Summary(bad, "将应用 {0} 项设置", "忽略 {0} 项（未知键或非法值）", "被忽略的键：{0}");
        Console.WriteLine("[摘要弹窗文案]\n" + summary);

        // ---- 2026-10-06 适配：默认值口径 + 结构化键的形状/区间校验 ----
        Console.WriteLine("\n---- 本轮适配自检（默认值 / 形状校验 / 区间夹回）----");
        string materialDefault = settings.GetString("personal.material", "");
        string dockMaterialDefault = settings.GetString("personal.dockCustom.material", "");
        Console.WriteLine($"[默认值] personal.material={materialDefault} · personal.dockCustom.material={dockMaterialDefault}");
        if (materialDefault != "none") { Console.WriteLine("[FAIL] 收纳板材质默认应为 none"); fail++; }
        if (dockMaterialDefault != "blur") { Console.WriteLine("[FAIL] 悬浮窗底面材质默认应为 blur"); fail++; }
        foreach (var key in new[] { "personal.material=\"none\"", "personal.dockCustom.material=\"blur\"", "personal.dockCustom.opacity=" })
            if (!text.Contains(key)) { Console.WriteLine($"[FAIL] 导出内容缺少本轮口径：{key}"); fail++; }

        // 形状非法 → 跳过并点名（旧实现会照单全收，读取侧各自静默兜底）
        var shape = ConfigPort.Parse(
            "personal.dockCustom.opacity=[\"a\",\"b\",\"c\"]\n"   // 字符串数组：非法
            + "personal.dockCustom.images=\"not-an-array\"\n"      // 非数组：非法
            + "personal.boardBg={\"nope\":1}\n"                    // 缺 image 字段：非法
            + "personal.boardOpacity=\"ninety\"\n");               // 非数字：非法
        Console.WriteLine($"[形状负例] 忽略 {shape.Ignored} 项 / 可应用 {shape.Apply.Count} 项 / 键：{string.Join("、", shape.IgnoredKeys)}");
        if (shape.Ignored != 4 || shape.Apply.Count != 0) { Console.WriteLine("[FAIL] 形状负例计数不符（期望 忽略 4 / 应用 0）"); fail++; }

        // 区间越界 → 夹回（不丢配置）；结构合法 → 放行
        var clamp = ConfigPort.Parse(
            "personal.boardOpacity=150\n"
            + "personal.boardBgOpacity=-20\n"
            + "personal.dockCustom.opacity=[120,50,-8]\n"
            + "personal.boardBg=null\n");
        Console.WriteLine($"[区间夹回] 忽略 {clamp.Ignored} 项 / 可应用 {clamp.Apply.Count} 项");
        if (clamp.Ignored != 0 || clamp.Apply.Count != 4) { Console.WriteLine("[FAIL] 区间夹回计数不符（期望 忽略 0 / 应用 4）"); fail++; }
        ConfigPort.Apply(clamp.Apply);
        SettingsStore.Instance.Flush();
        Console.WriteLine($"[夹回结果] boardOpacity={settings.GetDouble("personal.boardOpacity", -1)}"
            + $" · boardBgOpacity={settings.GetDouble("personal.boardBgOpacity", -1)}"
            + $" · opacity={settings.GetRawValue("personal.dockCustom.opacity").GetRawText()}");
        if (settings.GetDouble("personal.boardOpacity", -1) != 100) { Console.WriteLine("[FAIL] boardOpacity 未夹回 100"); fail++; }
        if (settings.GetDouble("personal.boardBgOpacity", -1) != 0) { Console.WriteLine("[FAIL] boardBgOpacity 未夹回 0"); fail++; }
        if (settings.GetRawValue("personal.dockCustom.opacity").GetRawText() != "[100,50,0]")
        { Console.WriteLine("[FAIL] 三层不透明度未夹回 [100,50,0]"); fail++; }
        return fail;
    }

    // ---------------- B. 主题预览卡离屏渲染 ----------------

    static int RenderThemeCards()
    {
        Console.WriteLine("\n==== B. 主题预览卡渲染 ====");
        string outDir = ShotDir();
        Directory.CreateDirectory(outDir);
        string[] modes = { "light", "dark", "auto", "system" };
        foreach (var mode in modes)
        {
            SettingsStore.Instance.SetString("theme.mode", mode);
            var page = new GlobalPage();
            // 页面自身不画底（底由 SettingsWindow 的视觉板提供），离屏出图时补一块同色底，
            // 否则深色主题的文字/描边落在白底上看不出真实观感
            var host = new System.Windows.Controls.Border
            {
                Background = Cship.Ui.Components.SettingsPalette.Bg(ThemeResolver.IsDark()),
                Child = page,
            };
            string path = Path.GetFullPath(Path.Combine(outDir, $"theme_{mode}.png"));
            Shot(host, 470, 250, path);
            Console.WriteLine($"[出图] {mode,-7} dark={ThemeResolver.IsDark(),-5} → {path}");
        }
        return 0;
    }

    /// <summary>出图目录：run\ 之上两级 = build\theme_previews（固定位置，清 run\ 也不丢证据）。</summary>
    static string ShotDir()
        => Path.GetFullPath(Path.Combine(Paths.AppDir(), "..", "..", "theme_previews"));

    /// <summary>离屏出图：按 DIP 布局，2x 像素密度渲染（1px 描边与勾号在放大查看时看得清）。</summary>
    static void Shot(FrameworkElement element, int widthDip, int heightDip, string path)
    {
        const int scale = 2;
        element.Measure(new Size(widthDip, heightDip));
        element.Arrange(new Rect(0, 0, widthDip, heightDip));
        element.UpdateLayout();
        var rtb = new RenderTargetBitmap(widthDip * scale, heightDip * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    // ---------------- C. 点击切换自检（选中效果搬家 + 写键） ----------------

    static int ClickThemeCard()
    {
        Console.WriteLine("\n==== C. 点击切换 ====");
        int fail = 0;
        SettingsStore.Instance.SetString("theme.mode", "light");
        var page = new GlobalPage();
        var host = new System.Windows.Controls.Border
        {
            Background = Cship.Ui.Components.SettingsPalette.Bg(ThemeResolver.IsDark()),
            Child = page,
        };
        string dir = ShotDir();
        Directory.CreateDirectory(dir);
        Shot(host, 470, 250, Path.Combine(dir, "click_before.png"));

        var cards = FindByPredicate<System.Windows.Controls.Border>(host, b => b.Height is 68);
        var badges = FindByPredicate<System.Windows.Controls.Grid>(host, g => g is { Width: 15, Height: 15 });
        Console.WriteLine($"[树] 卡片 {cards.Count} 张 / 徽标 {badges.Count} 枚");
        if (cards.Count != 4 || badges.Count != 4) { Console.WriteLine("[FAIL] 卡片或徽标数量不符"); return fail + 1; }
        if (Visible(badges) != "1") { Console.WriteLine($"[FAIL] 初始可见徽标数={Visible(badges)}（期望 1）"); fail++; }
        if (SettingsStore.Instance.GetString("theme.mode", "") != "light") { Console.WriteLine("[FAIL] 初始 mode 非 light"); fail++; }

        cards[2].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        page.UpdateLayout();
        string after = SettingsStore.Instance.GetString("theme.mode", "");
        Console.WriteLine($"[点击第3张] theme.mode={after} 可见徽标数={Visible(badges)}");
        if (after != "auto") { Console.WriteLine("[FAIL] 点击未写入 theme.mode=auto"); fail++; }
        if (Visible(badges) != "1") { Console.WriteLine("[FAIL] 选中徽标未唯一化"); fail++; }
        Shot(host, 470, 250, Path.Combine(dir, "click_after.png"));
        Console.WriteLine("[出图] click_before.png / click_after.png");
        return fail;
    }

    static string Visible(List<System.Windows.Controls.Grid> badges)
        => badges.FindAll(b => b.Visibility == Visibility.Visible).Count.ToString();

    static List<T> FindByPredicate<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(DependencyObject node)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T typed && match(typed)) { found.Add(typed); continue; }
                Walk(child);
            }
        }
        Walk(root);
        return found;
    }

    /// <summary>逻辑树遍历（离屏夹具用：无 PresentationSource 时视觉树可能未把 Content 接上）。</summary>
    static List<T> FindLogical<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(DependencyObject node)
        {
            foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(node))
            {
                if (child is not DependencyObject dep) continue;
                if (child is T typed && match(typed)) found.Add(typed);
                Walk(dep);
            }
        }
        Walk(root);
        return found;
    }

    // ---------------- G. 关于页扩展内容（2026-10-06 用户要求：四个大按钮折叠项 + 致谢段） ----------------

    /// <summary>
    /// 自检关于页扩展：四个 InfoExpander（仓库/许可证/开发者/Bilibili）+ 无标题致谢段都已在
    /// 页面树中；整页（亮/暗）与各折叠项展开内容（经反射取私有 _contentHost）离屏出图供人工核对。
    /// </summary>
    static int AboutPageSmoke()
    {
        Console.WriteLine("\n==== G. 关于页扩展内容 ====");
        int fail = 0;
        string dir = ShotDir();
        Directory.CreateDirectory(dir);

        string[] modes = { "light", "dark" };
        foreach (var mode in modes)
        {
            SettingsStore.Instance.SetString("theme.mode", mode);
            var page = new Cship.Ui.Pages.AboutPage();
            var host = new System.Windows.Controls.Border
            {
                Background = Cship.Ui.Components.SettingsPalette.Bg(ThemeResolver.IsDark()),
                Child = page,
            };

            // 视觉树在离屏（无 PresentationSource）下 ScrollViewer 模板链可能不完整，逻辑树更可靠
            var expanders = FindLogical<Cship.Ui.Components.InfoExpander>(host, _ => true);
            var allTexts = FindLogical<System.Windows.Controls.TextBlock>(host, _ => true);
            var allBorders = FindLogical<System.Windows.Controls.Border>(host, _ => true);
            Console.WriteLine($"[{mode}] 诊断：TextBlock={allTexts.Count} Border={allBorders.Count} 折叠项 {expanders.Count}/4（仓库/许可证/开发者/Bilibili）");
            if (expanders.Count != 4) { Console.WriteLine("[FAIL] 大按钮折叠项数量不符（应为 4）"); fail++; }

            // 折叠项内容经反射取出渲染（展开卡走 ExpanderOverlay，离屏无承载层，直接画内容板）
            var contentField = typeof(Cship.Ui.Components.InfoExpander).GetField("_contentHost",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int idx = 0;
            string[] names = { "repo", "license", "developer", "bilibili" };
            foreach (var exp in expanders)
            {
                if (contentField?.GetValue(exp) is System.Windows.Controls.StackPanel content &&
                    content.Children.Count > 0 &&
                    content.Children[0] is System.Windows.FrameworkElement inner)
                {
                    string path = Path.GetFullPath(Path.Combine(dir, $"about_{mode}_{names[idx++]}.png"));
                    Shot(inner, 430, 220, path);
                }
                else { Console.WriteLine("[FAIL] 折叠项内容为空（SetContent 未生效）"); fail++; }
            }

            string pagePath = Path.GetFullPath(Path.Combine(dir, $"about_{mode}_page.png"));
            Shot(host, 466, 980, pagePath);
            Console.WriteLine($"[出图] about_{mode}_page.png + 内容 {idx}/4");
            if (idx != 4) fail++;
        }
        return fail;
    }
}
