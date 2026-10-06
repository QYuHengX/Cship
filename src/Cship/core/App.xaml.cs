using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Cship.Ui;
using Cship.Core;
using Cship.Ui.Components;
using Cship.Ui.Pages;
using Cship.Ui.Themes;

namespace Cship;

/// <summary>
/// 入口装配（步骤 2）：单实例 → 目录/日志/存储/文案 → 异常钩子 → 托盘 → 悬浮窗。
/// </summary>
public partial class App : Application
{
    /// <summary>--autostart 静默启动标记（步骤 2.3，07 步接入开机自启）。</summary>
    public static bool LaunchedWithAutostart { get; private set; }

    SingleInstance? _single;
    TrayController? _tray;
    FloatingDock? _dock;
    BoardWindow? _board;
    SettingsWindow? _settings;

    // ---- 07 §1/§2/§3/§6 状态 ----
    TaskbarWatcher? _taskbarWatcher;   // 资源管理器重启监视
    DispatcherTimer? _heartbeat;       // 崩溃恢复心跳（30s）
    DispatcherTimer? _taskbarReplay;   // TaskbarCreated → 延迟重放（等桌面窗口重建完）
    bool _cleanExitDone;               // 退出清理只做一次（OnExit / ProcessExit / SessionEnding 三路共用）
    bool _storesReady;                 // 存储是否已初始化（第二实例早退时 OnExit 仍会跑，须跳过清理）
    bool _displayWatchInstalled;
    bool _settingsCorrupt, _stateCorrupt;
    string? _corruptBackup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LaunchedWithAutostart = e.Args.Contains("--autostart");

        // 1) 单实例：第二实例发 activate 信号后退出（2.2）
        _single = new SingleInstance();
        if (!_single.TryAcquire())
        {
            SingleInstance.SignalExisting();
            Shutdown(0);
            return;
        }
        _single.ActivateRequested += () =>
            Dispatcher.Invoke(() => _dock?.PlayBreathing()); // 首实例收到信号播呼吸

        // 2) 目录 → 日志 → 存储 → 文案
        Paths.EnsureDirs();
        Logger.Init();
        Logger.Info($"startup pid={Environment.ProcessId} autostart={LaunchedWithAutostart} ver={typeof(App).Assembly.GetName().Version}");
        SettingsStore.Init(Paths.SettingsFile());
        StateStore.Init(Paths.StateFile());
        _storesReady = true; // 之后才允许退出清理碰存储（第二实例在单实例检查处就早退了）
        // 配置损坏容错（07 §6）：解析失败已在存储层把原文件改名 .bak，这里记下以便稍后气泡告知
        _settingsCorrupt = SettingsStore.Instance.LoadFailed;
        _stateCorrupt = StateStore.Instance.LoadFailed;
        _corruptBackup = SettingsStore.Instance.CorruptBackupPath ?? StateStore.Instance.CorruptBackupPath;
        SettingsStore.Instance.Flush(); // 首启即落盘默认值，保证 settings.json 始终存在且合法
        I18n.Init(SettingsStore.Instance.GetString("general.language", "zh-CN"));
        ThemeEngine.Init();     // 步骤 06：token 合并字典 + 配色装配 + 系统主题即时跟随钩子
        StorageMaintenance.RunAsync(); // 后台清理可再生缓存与无引用素材副本（项目体积护栏，失败静默）

        InstallExceptionHooks();

        // 3) 托盘 → 悬浮窗 → 收纳板（2.1 装配顺序；02 步接入开合链路）
        // ⚠ 全部显式无参 lambda，禁止 Dispatcher.Invoke(裸方法组)：方法带可选参数时
        // C# 12 方法组自然类型推断为"要求 N 参数"的委托，运行时 DynamicInvoke 参数计数不匹配
        // 直接炸——批次十的 Invoke(ExitWithFade) 就此让托盘"关闭"静默失效（批次十一堆栈实锤）。
        _tray = new TrayController();
        _tray.ToggleBoardRequested += () => Dispatcher.Invoke(new Action(() => ToggleBoard())); // 收纳板开着→收起，否则打开（02 步）
        // 托盘"复位"（07 §5）：悬浮窗回默认位 **+ 一次全量刷新**（桌面与列表一把拉平）
        _tray.ResetRequested += () => Dispatcher.Invoke(new Action(() =>
        {
            _dock?.ResetToDefaultPosition();
            _board?.RequestFullRefresh();
        }));
        _tray.RestartRequested += () => Dispatcher.Invoke(new Action(() => ExitWithFade(Restart))); // 离场动画后拉起新进程（批次十）
        _tray.ExitRequested += () => Dispatcher.Invoke(new Action(() => ExitWithFade()));
        // 偏好设置（步骤 05）：打开设置窗口
        _tray.PreferencesRequested += () => Dispatcher.Invoke(new Action(OpenSettings));
        _tray.SetPreferencesEnabled(true);
        // 托盘右键菜单（步骤 06 §3）：不再用 WinForms ContextMenuStrip，改 GlassPopup 自绘玻璃菜单；
        // 动作复用上面四个既有事件，不新增动作链路
        _tray.MenuRequested += p => Dispatcher.Invoke(new Action(() => ShowTrayMenu(p)));
        // 托盘"显示"（清单二·任务3）：悬浮窗右键隐藏后复现，并禁用自身
        _tray.ShowDockRequested += () => Dispatcher.Invoke(new Action(() =>
        {
            _dock?.Revive();
            _tray?.SetShowEnabled(false);
        }));

        _dock = new FloatingDock();
        _dock.DissolveStarted += () => Dispatcher.Invoke(new Action(PrefetchBoard)); // 消散 ~500ms 内并行构造（批次八首开卡顿）
        _dock.DissolveCompleted += () => Dispatcher.Invoke(new Action(OpenBoard)); // 单击消散→打开收纳板（7.1）
        _dock.HiddenStateChanged += () => Dispatcher.Invoke(new Action(() => _tray?.SetShowEnabled(_dock?.IsUserHidden == true)));
        _dock.ExitRequested += () => Dispatcher.Invoke(new Action(() => ExitWithFade())); // 悬浮窗右键"结束程序"（任务3）
        _dock.OpenGate = IsBoardFirstLoadReady; // 开板闸门（任务13）：未就绪不消散、进加载态
        _dock.Show();
        _dock.PlayIntro(); // 启动入场：三板聚合复现 250ms（2026-08-30 批次十）

        // 全局快捷键钩子（清单二·任务7；2026-10-05 需求③ 改为键位可配置：高级页"快捷键"区）
        _hotkey = new KeyboardHotkey(OnHotkeyPressed);

        // 内存优化（2026-09-27）：启动稳定后整理一次工作集——WPF 预热/JIT 的临时页不常驻
        var trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        trimTimer.Tick += (_, _) =>
        {
            trimTimer.Stop();
            SystemBridge.TrimWorkingSet();
            Logger.Info("启动后工作集整理完成");
        };
        trimTimer.Start();

        // 07 §2：桌面图标显隐（崩溃恢复 + 当前意图应用）、心跳、退出路径、资源管理器重启监视
        ApplyDesktopIconsOnStartup();
        StartHeartbeat();
        InstallExitPaths();
        AttachTaskbarWatcher();

        // 自动主题时段检查（步骤 05：auto=06:00~18:00 亮，每分钟一次）
        ThemeAuto.EnsureStarted();
        StartFullScreenWatch(); // 07 §8-B：全屏前台时置顶窗口自动退层（每秒判定）
        StartDisplayWatch();    // 07 §3：显示器热插拔 / 分辨率变化

        ReportStartupIssues(); // 启动期告知（配置损坏 / 桌面图标恢复），托盘与悬浮窗已就绪
        SettingsStore.Instance.SettingsChanged += args =>
        {
            if (args.Key == "theme.mode")
                ThemeAuto.Evaluate();
            // 快捷键设置变更：重解析缓存（钩子回调里不再做字符串解析，见 OnHotkeyPressed）
            if (args.Key is "advanced.hotkeyToggle" or "advanced.hotkeyForce")
                ReloadHotkeys();
            if (args.Key is "general.autostart" or "general.hideDesktopIcons" or "general.language")
                RunOnUi(() => ApplyGlobalSettingSideEffect(args.Key));
        };

        // 配置导入热应用（步骤 05）：收纳板重载 iconOrder/分类/背景
        AdvancedPage.ImportedConfigHotReload += () => Dispatcher.Invoke(new Action(() =>
        {
            _board?.ApplyImportedConfig();
        }));
    }

    /// <summary>在 UI 线程执行（设置变更的副作用可能从任意线程抛出）。</summary>
    void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// "需要落到系统层"的设置项副作用收口（2026-10-06）：这三项原先只在设置页控件自己的事件里
    /// 生效，于是**导入配置 / 恢复默认**改到它们时系统侧根本没动（settings.json 与实际状态不一致）。
    /// 收到这里之后，所有写入口（设置页控件 / 导入 / 恢复默认）得到同一效果。
    /// </summary>
    void ApplyGlobalSettingSideEffect(string key)
    {
        switch (key)
        {
            case "general.autostart":
                if (_suppressAutostartSideEffect) break; // 回滚写回设置时不再次进本分支
                bool wantAuto = SettingsStore.Instance.GetBool("general.autostart", true);
                if (!Autostart.SetEnabled(wantAuto))
                {
                    // 07 §1：写键失败（组策略/权限限制）→ 开关 UI 状态回滚为**实际值** + 气泡提示一次
                    bool actualAuto = Autostart.IsEnabled();
                    Logger.Warn($"自启动写入失败：期望={wantAuto} 实际={actualAuto}（设置项回滚为实际值）");
                    if (SettingsStore.Instance.GetBool("general.autostart", true) != actualAuto)
                    {
                        _suppressAutostartSideEffect = true;
                        try { SettingsStore.Instance.SetBool("general.autostart", actualAuto); }
                        finally { _suppressAutostartSideEffect = false; }
                    }
                    _settings?.ReloadCurrentPage(); // 开关控件回到实际状态（GlobalPage.ReloadFromSettings 读注册表）
                    _tray?.ShowBalloon("Cship", I18n.Tr("notify.autostartFailed"));
                }
                break;
            case "general.hideDesktopIcons":
                bool wantHideIcons = SettingsStore.Instance.GetBool("general.hideDesktopIcons", false);
                SystemBridge.SetDesktopIconsVisible(!wantHideIcons);
                SetHideIconsOwned(wantHideIcons && SystemBridge.DesktopIconsHidden);
                break;
            case "general.language":
                string lang = SettingsStore.Instance.GetString("general.language", "zh-CN");
                if (!string.Equals(lang, I18n.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                    I18n.SetLanguage(lang); // 幂等：与当前一致就不重载词典、不重播全局文案刷新
                break;
        }
    }

    /// <summary>
    /// 托盘右键玻璃菜单（步骤 06 §3）：偏好设置 / 复位 / 重启 / 关闭 四项（悬浮窗处于右键隐藏态时
    /// 额外列出"显示"——清单二·任务3 的功能不能在换自绘菜单后丢失）。文案每次弹出按当前语言构建，
    /// 故不再需要 I18n 静态订阅。定位/失焦关闭/Esc 都在 GlassPopup.ShowAtScreen。
    /// </summary>
    void ShowTrayMenu(Point physical)
    {
        var tray = _tray;
        var items = new List<PopupMenuItem>
        {
            // 动作一律经 TrayController.Request → 既有四事件（不新增动作链路）
            new() { Header = I18n.Tr("tray.preferences"), OnClick = () => tray?.Request(TrayAction.Preferences) },
        };
        if (tray is { ShowEnabled: true })
        {
            // 悬浮窗被右键隐藏时才有"显示"（清单二·任务3 的能力不得因换自绘菜单而丢失）
            items.Add(PopupMenuItem.Separator());
            items.Add(new PopupMenuItem { Header = I18n.Tr("tray.show"), OnClick = () => tray?.Request(TrayAction.Show) });
        }
        items.Add(PopupMenuItem.Separator());
        items.Add(new PopupMenuItem { Header = I18n.Tr("tray.reset"), OnClick = () => tray?.Request(TrayAction.Reset) });
        items.Add(new PopupMenuItem { Header = I18n.Tr("tray.restart"), OnClick = () => tray?.Request(TrayAction.Restart) });
        items.Add(new PopupMenuItem { Header = I18n.Tr("tray.exit"), OnClick = () => tray?.Request(TrayAction.Exit) });
        GlassPopup.ShowAtScreen(physical, items);
    }

    /// <summary>打开设置窗口（齿轮/托盘两处来源；关闭=仅隐藏，重复打开=置前并重载控件值）。</summary>
    void OpenSettings()
    {
        try
        {
            _settings ??= new SettingsWindow();
            _settings.Present(_board);
        }
        catch (Exception ex)
        {
            Logger.Error("打开设置窗口失败", ex);
        }
    }

    KeyboardHotkey? _hotkey;

    // 快捷键解析缓存（性能 · 2026-10-05）：低级键盘钩子对系统每一次按键都会回调，
    // 原实现每键都 GetString×2 + Split/ToLowerInvariant/线性查表解析两道组合。
    // 改为设置变更时预解析，回调里只剩两次结构体比较（钩子变慢会被系统静默摘除，必须保持轻量）。
    KeyChord _hotkeyToggle = KeyChord.DefaultToggle;
    KeyChord _hotkeyForce = KeyChord.DefaultForce;
    bool _hotkeysLoaded;

    void ReloadHotkeys()
    {
        _hotkeyToggle = KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyToggle", "Tab"), KeyChord.DefaultToggle);
        _hotkeyForce = KeyChord.ParseOr(SettingsStore.Instance.GetString("advanced.hotkeyForce", "Alt+Tab"), KeyChord.DefaultForce);
        _hotkeysLoaded = true;
    }

    /// <summary>开板闸门（任务13）：板未构造则就地构造（等价消散期预取），返回首载是否就绪。</summary>
    bool IsBoardFirstLoadReady()
    {
        if (_board == null)
            PrefetchBoard();
        return _board?.IsFirstLoadReady ?? false;
    }

    /// <summary>
    /// 全局快捷键决策（UI 线程，返回 true=吞掉）。键位来自设置（高级页"快捷键"区可改）：
    /// `advanced.hotkeyToggle` 默认 Tab / `advanced.hotkeyForce` 默认 Alt+Tab。
    /// 主键与修饰键**精确匹配**——多按了修饰键就不命中（Ctrl+Tab、Shift+Tab 之类照常给系统，
    /// 旧实现一律吞掉并开板）。命中后的动作语义与清单二·任务7 原口径一致：
    /// ① 板内重命名/文本框聚焦 → 放行；② 设置窗口可见 → 优先关闭设置窗口（下一次再轮到收纳板）；
    /// ③ 真全屏前台（游戏/视频等）→ 普通键失效放行，强制键开板并临时提至最高优先级；
    ///    收纳板存在期间两键都能关闭，关闭后窗口优先级恢复；**该强制置顶是一个"会话"**——
    ///    板存续期间全屏退让监视挂起（见 <see cref="_boardForceTopmost"/>），否则刚提到最高就被抹回；
    /// ④ 常态 → 两键均开关收纳板（板与悬浮窗都不可见时普通键放行，避免误吞打字键；强制键兜底开板）。
    /// </summary>
    bool OnHotkeyPressed(KeyChord chord)
    {
        if (chord.IsModifierOnly) return false; // 只按了修饰键：不放行不干预
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            return false; // 重命名/文本框：按键归输入框

        if (!_hotkeysLoaded) ReloadHotkeys(); // 首键兜底（正常启动期已由 SettingsChanged 之外的路径置位）
        var toggle = _hotkeyToggle;
        var force = _hotkeyForce;
        bool isForce;
        if (chord == force) isForce = true;
        else if (chord == toggle) isForce = false;
        else return false; // 非本程序快捷键：放行

        // 设置窗口存在：优先关闭设置窗口（两键一致），下一次按键才轮到收纳板
        if (_settings is { IsVisible: true })
        {
            Dispatcher.BeginInvoke(new Action(() => _settings?.TryHandleEsc()));
            return true;
        }

        bool boardVisible = _board is { IsVisible: true };

        // 真全屏前台（2026-10-03 实装检测）：普通键失效；板开着时两键都能关；强制键开板置顶
        if (SystemBridge.IsForegroundFullScreen(Screens.SelectedScreen()))
        {
            if (boardVisible)
            {
                Dispatcher.BeginInvoke(new Action(ForceToggleBoard)); // 关板并恢复窗口优先级档
                return true;
            }
            if (!isForce)
                return false; // 全屏界面占据屏幕时普通键失效
            Dispatcher.BeginInvoke(new Action(ForceToggleBoard)); // 强制键：开板并临时提至最高优先级
            return true;
        }

        if (!boardVisible && _dock is not { IsVisible: true })
        {
            if (isForce)
            {
                Dispatcher.BeginInvoke(new Action(ForceToggleBoard)); // 强制键兜底：悬浮窗被右键隐藏时也可强制开板
                return true;
            }
            return false; // 板与悬浮窗都不在：普通键放行（不干扰正常打字）
        }
        Dispatcher.BeginInvoke(new Action(ToggleBoard));
        return true;
    }

    /// <summary>Alt+Tab 强制开合（任务7）：板显→关板并恢复优先级档（回落）；板未显→开板并置顶。
    /// 开板一路置 <see cref="_boardForceTopmost"/>：这是用户主动要求的"临时最高优先级"会话，
    /// 期间全屏退让监视必须挂起（2026-10-05 修复：真全屏游戏中 Alt+Tab 开板被退让立刻打回
    /// Topmost=false → 板被满屏游戏盖住，观感=出现即消失）。</summary>
    void ForceToggleBoard()
    {
        Logger.Info($"Tab/Alt 组合触发：板显={_board is { IsVisible: true }} 悬浮窗显={_dock?.IsVisible} "
            + $"右键隐藏={_dock?.IsUserHidden} 加载中加载点随板走");
        if (_board is { IsVisible: true })
        {
            _board.CloseBoard(() => Dispatcher.Invoke(new Action(() => _board?.ApplyBoardPriority())));
            return;
        }
        _forceTopmostNextOpen = true;
        if (_dock is { IsUserHidden: true })
        {
            OpenBoard(); // 悬浮窗右键隐藏态：无消散链路，直接开板
        }
        else
        {
            _dock?.Dissolve(); // 与单击同链路：消散→DissolveCompleted→OpenBoard
        }
    }

    bool _forceTopmostNextOpen;

    /// <summary>
    /// "强制置顶会话"是否进行中（Alt+Tab 强制开板 → 置顶 → 板关闭）。会话期间全屏退让监视挂起：
    /// 用户按下强制键的语义就是"现在就要压在满屏内容之上"，退让监视再把它抹回 Topmost=false
    /// 等于撤销用户刚下达的指令（2026-10-05 实机 bug：全屏 Minecraft 下 Alt+Tab 板闪现即消失）。
    /// 随板隐藏清除（<see cref="EnsureBoard"/> 的 IsVisibleChanged），不跨开板会话残留。
    /// </summary>
    bool _boardForceTopmost;

    /// <summary>收纳板单例构造 + 事件订阅（预取与闸门两条路径共用，防漏订阅）。</summary>
    BoardWindow EnsureBoard()
    {
        if (_board != null) return _board;
        try
        {
            _board = new BoardWindow();
            _board.FirstLoadReady += () => Dispatcher.Invoke(new Action(() => _dock?.OnFirstLoadReady()));
            _board.GearClicked += () => Dispatcher.Invoke(new Action(OpenSettings)); // 齿轮打开设置窗（步骤 05）
            // 设置窗恒在收纳板之上（2026-10-02）：板被激活（点击）后把设置窗 HWND 校正回板上方
            _board.Activated += (_, _) => Dispatcher.Invoke(new Action(() => _settings?.RaiseAbove(_board)));
            // 强制置顶会话随板隐藏结束：板不在了就没有"临时最高优先级"可言，退让监视恢复判定
            // （板重开若走普通链路，优先级照旧由 advanced.dockPriority 决定）
            _board.IsVisibleChanged += (_, _) =>
            {
                if (!_board.IsVisible) _boardForceTopmost = false;
            };
        }
        catch (Exception ex)
        {
            Logger.Error("收纳板构造失败", ex);
            throw;
        }
        return _board;
    }

    /// <summary>消散期间预构造收纳板（构造即触发首扫，扫描在消散 ~500ms 内并行完成；
    /// 开板动画闸门随后等容器/分批就绪——2026-08-29 批次八首开卡顿）。</summary>
    void PrefetchBoard()
    {
        if (_board != null) return; // 闸门路径（任务13）可能已构造：不重复订阅
        try
        {
            EnsureBoard();
        }
        catch (Exception ex)
        {
            Logger.Error("收纳板预构造失败（开板时重试）", ex);
        }
    }

    /// <summary>悬浮窗消散完成后打开收纳板（02 步：一次消散对应一次开合）。
    /// Alt+Tab 强制开板时置顶（任务7），开板动画完成后由 CloseBoard 恢复优先级档。</summary>
    void OpenBoard()
    {
        if (_board is { IsVisible: true }) return;
        var board = EnsureBoard();
        _dock?.SetBoardSession(true);
        board.OpenFor(_dock!);
        _settings?.RaiseAbove(board); // 开板后设置窗保持在板上方（2026-10-02）
        if (_forceTopmostNextOpen)
        {
            _forceTopmostNextOpen = false;
            _boardForceTopmost = true; // 会话开始：退让监视在此期间挂起（OnFullScreenTick）
            board.Topmost = true; // 强制开板置顶（任务7）
            Logger.Info("收纳板 Alt+Tab 强制开板：Topmost=true（优先级临时提升，全屏退让监视挂起至板关闭）");
        }
    }

    /// <summary>托盘左键切换收纳板；关闭链路由 BoardWindow.CloseBoard 自行复现悬浮窗。</summary>
    void ToggleBoard()
    {
        if (_board is { IsVisible: true })
            _board.CloseBoard();
        else
            _dock?.Dissolve(); // 与单击同链路：消散→DissolveCompleted→OpenBoard
    }

    /// <summary>
    /// 退出/重启的离场过渡（2026-08-30 批次十；批次十一加固）：收纳板若开着→板滑出（不复现悬浮窗）；
    /// 否则悬浮窗播 250ms 消散离场。动画结束后才执行后续动作（Shutdown / 拉起新进程）——
    /// 单实例锁在动画期间保持持有，重启路径在新进程拉起前一刻才放锁，避免新实例误判。
    /// **兜底强退**：离场动画回调万一丢失（动画中断/时钟异常），1.5s 后强制落盘并结束进程——
    /// 保证托盘"关闭/重启"在任何状态下语义=结束进程。
    /// </summary>
    bool _exitCompleted;

    void ExitWithFade(Action? after = null)
    {
        Logger.Info("exit requested");
        _exitCompleted = false;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(1500);
            if (_exitCompleted) return;
            Logger.Warn("退出离场超时（动画回调未到达），强制结束进程");
            try
            {
                SettingsStore.Instance.Flush();
                StateStore.Instance.Flush();
            }
            catch { /* 强退路径尽力落盘 */ }
            Environment.Exit(0);
        });
        void Finish()
        {
            _exitCompleted = true;
            if (after != null) after();
            else Shutdown(0);
        }
        if (_board is { IsVisible: true })
        {
            _board.CloseBoard(() => Dispatcher.Invoke(new Action(Finish)), revive: false); // 板滑出即离场（悬浮窗本已隐藏）
            return;
        }
        _dock?.PlayExit(() => Dispatcher.Invoke(new Action(Finish)));
    }

    // ---- 全屏前台退让监视（07 §8 方案 B，2026-10-05 用户裁决）----

    DispatcherTimer? _fullScreenWatch;
    bool _fullScreenRetreat;

    /// <summary>
    /// 每秒检查所选屏是否被真全屏前台窗口占据：是则让置顶中的收纳板/设置窗退层，退出后恢复。
    /// 仅当"优先级档要求置顶 + 至少一个窗口可见"时才做 P/Invoke 检测（不置顶/都关闭时零开销）。
    /// </summary>
    void StartFullScreenWatch()
    {
        _fullScreenWatch = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _fullScreenWatch.Tick += (_, _) => OnFullScreenTick();
        _fullScreenWatch.Start();
    }

    void OnFullScreenTick()
    {
        // 判定"要不要看"必须用"优先级档是否要求置顶"这个稳定事实，不能用 Topmost——
        // 退层后 Topmost 已是 false，用它判定会立刻走"复位"分支，与退层互相触发形成每秒抖动。
        bool priorityWantsTopmost = !string.Equals(
            SettingsStore.Instance.GetString("advanced.dockPriority", "low"), "low",
            StringComparison.OrdinalIgnoreCase);
        bool anyVisible = _board is { IsVisible: true } || _settings is { IsVisible: true };
        // 强制置顶会话（Alt+Tab 开板）进行中：用户明确要求此刻压在满屏内容之上，
        // 退让判定必须让位——否则强制提上去的 Topmost 会在 ≤1s 内被抹掉（2026-10-05 修复）
        bool forceTopmostSession = _boardForceTopmost && _board is { IsVisible: true };

        if (!priorityWantsTopmost || !anyVisible || forceTopmostSession)
        {
            if (_fullScreenRetreat)
                ApplyRetreat(false); // 条件不再成立（切成 low 档 / 窗口都关了 / 进入强制会话）：立即复位
            return;
        }

        bool fullScreen;
        try { fullScreen = SystemBridge.IsForegroundFullScreen(Screens.SelectedScreen()); }
        catch { return; } // 检测失败保持现状，不误判

        if (fullScreen != _fullScreenRetreat)
            ApplyRetreat(fullScreen);
    }

    void ApplyRetreat(bool retreat)
    {
        _fullScreenRetreat = retreat;
        _board?.SetFullScreenRetreat(retreat);
        _settings?.SetFullScreenRetreat(retreat);
    }

    // ============================ 07 §1/§2/§3/§6 系统集成 ============================

    // ---- 07 §2：桌面图标显隐的崩溃恢复、心跳与重放 ----

    bool _iconsRecoveredOnStartup;
    bool _suppressAutostartSideEffect; // 自启动回滚写回设置时的防重入

    /// <summary>
    /// 启动时的桌面图标状态处理（07 §2）：
    /// ① 先把"运行中"标记置 false 并落盘——此后任何强杀都会在磁盘上留下 false（崩溃可检测）；
    /// ② 若上次运行"由本程序隐藏了图标"却没能正常恢复（<c>hideIconsOwned</c> 仍为 true），
    ///    先把图标强制恢复可见——0x7402 是**切换**指令，进程被杀后自记状态必然失真，
    ///    状态未知时盲发可能把"已隐藏"变成"显示"；恢复后自记状态与真实状态重新对齐；
    /// ③ 再按 <c>general.hideDesktopIcons</c> 应用当前意图，并同步 <c>hideIconsOwned</c>。
    /// </summary>
    void ApplyDesktopIconsOnStartup()
    {
        try
        {
            bool crashed = !StateStore.Instance.GetBool("lastRunOk", true);
            bool ownedBefore = StateStore.Instance.GetBool("hideIconsOwned", false);
            bool wantHide = SettingsStore.Instance.GetBool("general.hideDesktopIcons", false);

            StateStore.Instance.SetBool("lastRunOk", false);
            StateStore.Instance.Flush();

            if (crashed)
                Logger.Warn("检测到上次运行未正常退出（lastRunOk=false）：执行启动自检与恢复");

            bool recovered = false;
            if (ownedBefore)
            {
                recovered = SystemBridge.RecoverDesktopIconsIfNeeded();
                SetHideIconsOwned(false);
            }

            if (wantHide)
            {
                SystemBridge.SetDesktopIconsVisible(false);
                SetHideIconsOwned(true);
            }
            else
            {
                SetHideIconsOwned(false); // 显式落盘"当前不由本程序隐藏"，让 state.json 如实反映
            }

            _iconsRecoveredOnStartup = recovered;
            if (recovered)
                Logger.Info("启动自检：上次遗留的桌面图标隐藏状态已恢复可见");
        }
        catch (Exception ex)
        {
            Logger.Error("启动桌面图标自检失败", ex);
        }
    }

    /// <summary>记录"桌面图标当前由本程序隐藏"（崩溃后据此精确恢复，不误动用户手动关闭的桌面图标）。</summary>
    void SetHideIconsOwned(bool owned)
    {
        try { StateStore.Instance.SetBool("hideIconsOwned", owned); }
        catch (Exception ex) { Logger.Warn($"写入 hideIconsOwned 失败：{ex.Message}"); }
    }

    /// <summary>
    /// 崩溃恢复心跳（07 §2）：每 30s 把"运行中"标记（lastRunOk=false）落盘一次。
    /// **为什么写 false 而不是 true**：该标记是"上次是否正常退出"的判据——运行期间必须是 false，
    /// 只有走完正常退出流程才置 true。若按字面"每 30s 写 true"，进程被强杀时磁盘上留下的是
    /// 最后一次 true，崩溃就永远检测不出来（判据失效，验收项"杀进程→再启动→图标恢复"必然失败）。
    /// 心跳的作用是保证"运行中"这一事实**耐久**，并顺带把防抖中的 state 变更刷下去。
    /// </summary>
    void StartHeartbeat()
    {
        _heartbeat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _heartbeat.Tick += (_, _) =>
        {
            try
            {
                StateStore.Instance.SetBool("lastRunOk", false);
                StateStore.Instance.Flush();
            }
            catch (Exception ex)
            {
                Logger.Warn($"心跳落盘失败：{ex.Message}");
            }
        };
        _heartbeat.Start();
    }

    /// <summary>
    /// 三条退出路径统一（07 §6）：WPF <c>Application.Exit/Shutdown</c>（→ <see cref="OnExit"/>）、
    /// 进程退出（<c>AppDomain.ProcessExit</c>，覆盖 ExitWithFade 超时的 <c>Environment.Exit</c> 兜底）、
    /// 注销/关机（<c>SessionEnding</c>）。三路都走 <see cref="CleanExit"/>，用标志保证只执行一次。
    /// </summary>
    void InstallExitPaths()
    {
        SessionEnding += (_, _) =>
        {
            Logger.Info("系统注销/关机（SessionEnding）：恢复桌面图标并落盘");
            CleanExit();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanExit();
    }

    /// <summary>
    /// 正常退出清理（幂等）：恢复桌面图标 → 落盘（含 lastRunOk=true）→ 停止心跳。
    /// 三路退出共用。**杀进程/断电不会走到这里**——这正是"崩溃可检测"的前提。
    /// </summary>
    void CleanExit()
    {
        if (_cleanExitDone) return;
        _cleanExitDone = true;
        if (!_storesReady) return; // 第二实例（单实例检查处早退）没有存储：不做任何清理，也不碰用户数据
        try
        {
            _heartbeat?.Stop();
            _heartbeat = null;
            // 步骤 05 铁律：隐藏桌面图标只切换显示状态，程序退出恢复显示。
            // 仅在"确实由本程序隐藏"或"当前意图是隐藏"时恢复，绝不越权打开用户手动关闭的桌面图标。
            if (SystemBridge.DesktopIconsHidden
                || SettingsStore.Instance.GetBool("general.hideDesktopIcons", false))
                SystemBridge.SetDesktopIconsVisible(true);
            SetHideIconsOwned(false);
            StateStore.Instance.SetBool("lastRunOk", true); // 正常退出标记（下次启动据此判定是否崩溃）
            SettingsStore.Instance.Flush();
            StateStore.Instance.Flush();
        }
        catch (Exception ex)
        {
            Logger.Warn($"退出清理失败：{ex.Message}");
        }
    }

    /// <summary>挂载资源管理器重启监视（07 §2）：窗口关闭时由 TaskbarWatcher 自行 RemoveHook。</summary>
    void AttachTaskbarWatcher()
    {
        try
        {
            if (_dock == null) return;
            _taskbarWatcher = new TaskbarWatcher(_dock);
            _taskbarWatcher.TaskbarCreated += () => Dispatcher.BeginInvoke(new Action(OnTaskbarCreated));
        }
        catch (Exception ex)
        {
            Logger.Warn($"资源管理器重启监视挂载失败：{ex.Message}");
        }
    }

    /// <summary>资源管理器重启（07 §2）：合并连发广播，延迟 800ms 等桌面窗口重建完再重放。</summary>
    void OnTaskbarCreated()
    {
        if (_taskbarReplay == null)
        {
            _taskbarReplay = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(800),
            };
            _taskbarReplay.Tick += (_, _) =>
            {
                _taskbarReplay!.Stop();
                ReplayDesktopIconsIntent();
            };
        }
        _taskbarReplay.Stop();
        _taskbarReplay.Start();
    }

    /// <summary>重放隐藏意图：桌面窗口重建后图标回到可见默认态，按当前设置重新应用。</summary>
    void ReplayDesktopIconsIntent()
    {
        try
        {
            bool wantHide = SettingsStore.Instance.GetBool("general.hideDesktopIcons", false);
            var actual = SystemBridge.QueryDesktopIconsVisible();
            Logger.Info($"桌面重建后重放隐藏意图：意图={(wantHide ? "隐藏" : "显示")} "
                + $"实测={(actual.HasValue ? (actual.Value ? "可见" : "隐藏") : "未知")}");
            if (wantHide)
                SystemBridge.SetDesktopIconsVisible(false);
            else if (SystemBridge.DesktopIconsHidden)
                SystemBridge.SetDesktopIconsVisible(true);
            SetHideIconsOwned(wantHide && SystemBridge.DesktopIconsHidden);
            _board?.RequestRescan(); // 桌面重建后重新对账（差量，不重播果冻）
        }
        catch (Exception ex)
        {
            Logger.Warn($"桌面重建重放失败：{ex.Message}");
        }
    }

    // ---- 07 §3：显示器热插拔 / 分辨率变化 ----

    void StartDisplayWatch()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _displayWatchInstalled = true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"显示设置变化监视挂载失败（热插拔将不可感知）：{ex.Message}");
        }
    }

    /// <summary>SystemEvents 回调在系统线程：一律回 UI 线程（后台线程禁止直接碰 WPF 控件）。</summary>
    void OnDisplaySettingsChanged(object? sender, EventArgs e) => RunOnUi(HandleDisplayChanged);

    void HandleDisplayChanged()
    {
        try
        {
            Screens.Invalidate(); // Screen.AllScreens 是 P/Invoke 热点：按显示变化失效重取
            Logger.Info("显示设置变化：屏幕枚举缓存已失效，窗口尺寸/位置重新校验");
            string id = SettingsStore.Instance.GetString("display.screen", "auto");
            bool specified = !string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase);
            if (specified && !Screens.Exists(id))
            {
                SettingsStore.Instance.SetString("display.screen", "auto");
                Logger.Info($"所选屏已移除（{id}）：自动切回 auto（光标所在屏）");
                _startupNotices.Add(I18n.Tr("notify.screenRemoved"));
            }
            _dock?.OnDisplayChanged();     // 悬浮窗复位/夹回
            _board?.OnDisplayChanged();    // 收纳板尺寸/位置夹回
            _settings?.ReloadCurrentPage(); // 设置窗开着时重载屏幕列表（型号/分辨率已变）
            if (_startupNotices.Count > 0)
            {
                _tray?.ShowBalloon("Cship", string.Join("\n", _startupNotices));
                _startupNotices.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("处理显示设置变化失败", ex);
        }
    }

    // ---- 07 §1/§6：启动期告知（配置损坏 / 图标恢复）----

    readonly List<string> _startupNotices = new();

    /// <summary>启动期一次性告知（托盘气泡）：配置损坏已重建、上次遗留的图标隐藏已恢复。</summary>
    void ReportStartupIssues()
    {
        if (_settingsCorrupt || _stateCorrupt)
        {
            string text = I18n.Tr("notify.configCorrupt");
            if (!string.IsNullOrEmpty(_corruptBackup))
                text += "\n" + string.Format(I18n.Tr("notify.configBackup"), _corruptBackup);
            _startupNotices.Add(text);
        }
        if (_iconsRecoveredOnStartup)
            _startupNotices.Add(I18n.Tr("notify.iconsRecovered"));
        if (_startupNotices.Count > 0)
        {
            _tray?.ShowBalloon("Cship", string.Join("\n", _startupNotices));
            _startupNotices.Clear();
        }
    }

    void InstallExceptionHooks()
    {
        // 三处全局异常钩子统一写日志（2.1）
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("UI 线程未处理异常", args.Exception);
            // 批次十一诊断：TargetParameterCountException（参数不匹配的 DynamicInvoke）反复出现
            // 且堆栈只有调度层帧——补打 UI 线程完整现场栈，暴露"正在执行的委托"是谁
            try { Logger.Info("UI 线程现场：\n" + Environment.StackTrace); } catch { }
            args.Handled = true; // 常驻程序吞掉非致命 UI 异常，避免托盘残留/进程僵死
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("非 UI 线程致命异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("未观察的 Task 异常", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>重启（3.3）：先放单实例锁再拉起新进程，避免新进程被误判为第二实例。</summary>
    void Restart()
    {
        Logger.Info("restart requested");
        try
        {
            var exe = Paths.LauncherExe();   // 精简版经引导器重启（它负责注入本地运行时）
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("无法解析重启入口（Paths.LauncherExe 为空）");
            _single?.Release();
            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("重启失败，程序继续运行", ex);
            _single?.TryAcquire(); // 重新拿回单实例身份
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fullScreenWatch?.Stop(); // 全屏退让监视随进程退出停止
        _fullScreenWatch = null;
        _taskbarReplay?.Stop();   // 资源管理器重启重放计时器
        _taskbarReplay = null;
        _hotkey?.Dispose(); // Tab 键盘钩子随进程卸载（任务7）
        _hotkey = null;
        if (_displayWatchInstalled)
        {
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
            catch (Exception ex) { Logger.Warn($"卸载显示设置监视失败：{ex.Message}"); }
            _displayWatchInstalled = false;
        }
        _taskbarWatcher?.Dispose(); // 移除 TaskbarCreated 钩子（HwndSource 防泄漏）
        _taskbarWatcher = null;
        CleanExit(); // 07 §6：恢复桌面图标 + 落盘（含 lastRunOk=true），与 ProcessExit/SessionEnding 同一路径
        _tray?.Dispose();
        _tray = null;
        _single?.Dispose();
        _single = null;
        Logger.Info("exit");
        base.OnExit(e);
    }
}
