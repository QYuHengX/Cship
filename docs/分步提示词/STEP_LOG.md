# STEP_LOG — Cship 施工记录

> 各步骤完成记录按序追加。任何 Agent 开工前必读：00 总览 + 自己的步骤文件 + 本文件全部前序记录。
> **冲突规则**见 00 §1 执行协议第 5 条（条款时效与优先级）；与原始设计文档冲突或原文未写的，以各记录中的【补充决策】为准。
> 【编号说明】步骤 01/01b 的补充决策为 1–20；步骤 02 完成记录的局部编号 1–10 系早期遗留（与 01 重号），其后各批次自 21 起续编；引用时以"内容 + 上下文"为准。
> **本文件已于 2026-10-05（批次十二）压缩**：只保留仍有效的信息——补充决策、给后续步骤的接口提示、关键根因与教训、每批次范围一句话、当前口径。过程叙述（改动文件清单、验证细节、测试残留、留人工验证项、逐项验收清单）已按"对当前项目已无用"删除，共移除 130 个子节。
> 压缩前全文备份：`build\_STEP_LOG.before-compact.md`（确认无需回查后可删）。
> 【跳号说明】原文存在历史跳号（32 / 40 / 85–93 / 104 / 128–130 / 180 未使用），压缩前后一致，**不补号、不重编号**（保证既有引用有效）。

---

## 步骤01 完成记录（2026-08-28）

- **范围**：项目骨架、后台常驻（托盘）与悬浮窗（步骤文件 01 全部任务），未越界实现 02+。

### 补充决策

1. settings/state 用扁平点号键 JSON，键名与 00 §8 逐字一致，按表序输出便于核对。
2. `dock.pos` 存物理像素 `[x,y]`，与 `Screen.WorkingArea` 同坐标系。
3. 关闭 ImplicitUsings、全文件显式 using（WPF+WinForms 类型大量歧义）。
4. `NoWarn=WFAC010`（WinForms 专属提示；PerMonitorV2 必须留 app.manifest），其余警告零容忍。
5. `JsonElement.DeepEquals` 是 .NET 9 API，.NET 8 自行实现 ValueKind+GetRawText 比较。
6. 首启即落盘 settings.json 默认值（不能等第一次改设置才写盘）。
7. ThemeResolver 为临时实现（auto=6:00–19:00 白天；system 读 AppsUseLightTheme），06 步 ThemeEngine 接管。
8. 托盘图标句柄经 `SystemBridge.DestroyIcon` 显式销毁防泄漏；强杀会遗留幽灵图标（悬停即消失）。
9. 重启顺序：`SingleInstance.Release()`→`Process.Start`→Shutdown；启动失败则重新 TryAcquire 并继续运行。
10. `build/` 本步仅建目录；主程序 exe 图标未设置，08 步随 makeicon.ps1 统一补。
11. 悬浮窗窗口尺寸=板边长+32px（预留 2×16px 错位行程）。
12. Topmost 被抢焦点失效问题留 02 步处理（已挂账）。

### 给后续步骤的接口提示

- `ShortcutRouter.Register(handler, EscLayer)/EscPressed()` 就绪，02/04/05 接入。
- `TrayController` 四动作事件化；`PreferencesRequested` 目前无人订阅（05 接设置窗）。
- `SquarePlate` 材质参数收敛在组件内，06 步 MaterialEngine 只替换绘制来源。
- `FloatingDock.Dissolve()` 的"600ms 复现"占位在 02 步替换为：消散→打开 BoardWindow→关闭后倒放复现。
- `GlassWindow` 可作 BoardWindow/SettingsWindow/玻璃菜单窗基类；`ApplyRoundedClip` 备用于圆角裁剪。
- 开发运行：`dotnet run --project src\Cship`；配置在输出目录 `dependencies\config\`。

---

## 步骤01 修订记录（2026-08-28 · 用户反馈）

- **反馈**：单击消散"消失速度过快"。要求：中层板停留原地，上/下板向错开方向移动并缩小，三层统一提高透明度至完全透明并消失；放缓透明度变化。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/components/SquarePlate.xaml(.cs)` | 加 `BaseMargin=24`、窗口预留改 `Chrome=72`；上/下板预置中心缩放 `ScaleTransform`；新增 `PlayDissolve/PlayRevive/FreezeOffset/ResetPlateVisuals/FinalizeRest`，消散/复现编排收敛进组件 |
| `ui/FloatingDock.xaml.cs` | 窗口边长=板边长+Chrome；消散参数 `DissolveMoveMs/DissolveFadeMs`（终值 250/250）；夹边按可见三板包络计算；首启顶留白换算 |

新消散编排：单击→中层板停在原地（不动不缩）；上层板向左上、下层板向右下各外扩 24px 并缩至 0.5（250ms easeInOut）；三板同步 250ms 淡出至完全透明→Hide→600ms 后反向复现（复现结束落回静置错位 6）。

### 修订补充决策

13. **消散节奏（二次修订后）＝分离 250ms + 淡出 250ms（同 duration 同起点）**：动画结束瞬间透明度恰好到 100%，无"板已停住仍在渐隐"的尾巴。演进：初版 120ms → 300+450ms → 250+250ms。**均覆盖 00 §7"悬浮窗消散 120ms"**，后续引用以本记录为准。
14. `dock.pos` 语义=**窗口原点**（非可见板左上角）；窗口四周含 72px 动画行程边距，夹边/首启按可见包络换算。
15. 决策 11（"窗口尺寸=板边长+32px"）由本修订取代（现为板边长+72px），夹边口径同步更新为可见包络。

---

## 文档同步记录（2026-08-28 · 补丁 01b 开工时）

- **起因**：原始设计文档新增【L60】"悬浮窗优先级 低/中/高"，规格已同步进：00（§6 术语 `DockZOrder`、§7 优先级行、§8 `advanced.dockPriority`）、01（4.1 表述 + 新增 4.8 三档规格 + 验收项）、05（高级页项 + 验收项）、07（§8 全屏前台窗口检测 + 验收项）、01b（本补丁规格文件）。

---

## 步骤01b 完成记录（2026-08-28）

- **范围**：悬浮窗 z 序优先级骨架（补丁 01b 全部任务）。未做设置 UI（05）、未做真全屏检测（07）、未改 00 键名/默认值。

### 补充决策

16. **low 档选"钩子拦截 WM_WINDOWPOSCHANGING"而非周期 SetWindowPos(HWND_BOTTOM)**：零 CPU 常驻、z 序变化时序精确；钩子经 `HwndSource.AddHook`（托管子类化）。
17. **GlassWindow 置顶策略采用模板方法 `ApplyTopmostStrategy()`**：基类构造保持 `Topmost=false`，`OnSourceInitialized` 末尾调用虚钩子（句柄已建且尚未 Show）。
18. **medium 档=行为暂同 high + 启动时占位探测一次**（`IsForegroundFullScreen` + `OnForegroundFullScreenChanged` 各调一次留日志，证明调用路径可达）；07 步接通后由其驱动隐藏或退层。
19. **ApplyPriority 顺序约定**：先 `RemoveHook` 摘旧 low 钩子 → `Topmost` 落定 → 再 `AddHook` 挂新钩子。
20. **low 档自动化测试的点位纪律**：low 模式下悬浮窗被普通窗口盖住是正确行为，测试先把悬浮窗放到命中测试全空闲的桌面区（`WindowFromPoint` 命中 SysListView32 才算空），否则事件被上层窗口吃掉造成假失败。

### ⚠️ 给步骤 02 的显式提示

1. **BoardWindow 继承 GlassWindow 后必须自设 `Topmost=true`**——基类已不再提供置顶；BoardWindow 置顶策略不受 `advanced.dockPriority` 影响（01 4.8 末段）。
2. "收纳板打开期间临时取消悬浮窗 Topmost"的挂账落点：02 打开 BoardWindow 时降级、关闭时恢复，建议与 `advanced.dockPriority` 联动（high 档才临时降级）。
3. `FloatingDock.ApplyPriority(DockZOrder)` 为 public，05 步设置 UI 直接调用；`DockZOrder` 在 `Cship.Ui` 命名空间。

---

## 步骤02 完成记录（2026-08-28）

- **范围**：步骤文件 02 全部任务（三层结构/锚边定位/可调长宽、桌面枚举与图标缓存、IconItem 全套交互、GlassPopup 自绘菜单、垃圾桶拖删、拖动换位、悬浮窗联动开合、Esc 路由）。未做设置界面（05）、分类页（04）、材质引擎内部（06）、动画预设注册表（03）。

### 补充决策（步骤02 局部编号 1–10）

1. **图标提取换道 IShellItemImageFactory**（原指引 SHGetFileInfo+SHIL_JUMBO 逐级回退）：本机 `SHGetImageList` 恒 E_NOINTERFACE，ImageList 通道不可用。新通道零引用、天然解析 .lnk/文件夹图标、直接拿 256px。附带给 app.manifest 补 Common-Controls v6 依赖。
2. **iconOrder 与排序互斥**：改排序（或"自动排列"）即清空 iconOrder 回自动布局；有记录项占固定单元格（越界回退补位），其余按当前排序填空位。`lockIconDrag=true` 禁一切拖动（含拖删）。
3. **>300 项取舍=分批加载而非虚拟化**：自定义 IconGridPanel 需按 iconOrder 精确摆位；分批插入（64/批、Background 优先级、代次校验）已满足"不冻结 UI"。
4. **`board.size.<direction>` 存 DIP**（逻辑像素），与 `dock.pos` 存物理像素不同口径：尺寸跟随 DPI 缩放更自然。
5. **文件夹指示灯=进程路径前缀匹配**（原文 L58）：任一进程主模块位于该文件夹下即点亮。
6. **"固定到开始屏幕"未做隐藏逻辑**：Win10/11 下 pintohome verb 恒可用，失败仅记日志。
7. **卸载/异常兜底**：垃圾桶删除放后台线程，成功立即差量移除；删除失败（用户取消 UAC 等）watcher 刷新自动恢复。
8. **收纳板期间 Topmost 策略**：BoardWindow 恒置顶；悬浮窗 high/medium 档在收纳板打开期间临时 Topmost=false、关闭恢复；low 档不动。
9. **重命名非法名提示用系统 MessageBox、快捷方式输入框为简化小窗**：临时交互件，05 步可统一替换。
10. **渲染通道备注**：材质仍为静态亚克力占位，06 步 MaterialEngine 接管后 BoardWindow 只需换 MaterialBackground 内部实现。

### 给后续步骤的接口提示

- `BoardWindow.GearClicked` 事件（05 接设置窗）；`OpenBoard/ToggleBoard` 在 App.xaml.cs。
- `BoardAnimController.Open/Close(board,dock,onDone)` 签名即 03 步预设注册表调用面；消散/复现复用 SquarePlate.PlayDissolve/PlayRevive。
- `MaterialBackground`（材质参数+SetTheme）为 06 步唯一替换点；IconItem 主题色收敛在 `ApplySettings`。
- `FileOps.PinToStart/ShowProperties` 走 `SystemBridge.ShellExecuteVerb`，07 步可复用。
- `DesktopScanner.RefreshRequested` 已在 UI 线程触发；`state.json` 现含 `iconOrder`/`board.size.*`。

---

## 步骤02 修订记录（2026-08-28 · 用户截图反馈批次）

- **反馈**：①取消收纳板顶部"标题栏"预留带；②齿轮与红点垂直对齐；③图标观感尺寸不一（过小/顶格溢出）；④图标默认大小改小；⑤鼠标经过的图标完整播放跳动；⑥取消选中蓝框；⑦滚动条现代化；⑧整体风格参考 modern-gradient。

### 修订补充决策

21. **"取消标题栏"落法**：去掉 44px 顶部预留带（图标区顶起 10px），齿轮/红点悬浮于网格右上角。〔**注：该布局后被批次三/二恢复为 34px 标题栏带（见决策 28）；本决策仅存历史。**〕
22. **图标观感归一化在提取层做**（alpha 包围盒裁边 + 8% 内边距）而非显示层拉伸；缓存键升 v2。
23. **hover 完整播放**：进入必播满上行（200ms），离开时上行未完则排队回程；回程中再次进入从当前值起跳。
24. **modern-gradient 本步只落"占位版"**（渐变底+发丝描边），完整配方归 06 步。

---

## 步骤02 修订记录（2026-08-29 · 用户截图反馈批次二）

- **反馈**（9 项）：①默认位置/大小按截图；②收纳板优先级跟随悬浮窗；③文件夹图标用素材\文件夹.webp；④关闭钮 hover 放大带过渡；⑤关闭钮更右上、齿轮其正下方；⑥标题栏略微加宽+材质区分+交界不生硬；⑦跳动的图标可覆盖标题栏；⑧默认不显示文件名；⑨Steam 的 .url 快捷方式图标空白修复。

### 根因（⑨）

探针实测 `IShellItemImageFactory(ICONONLY)` 对 .url/.txt 会 S_OK 返回**通用空白页图标**（像素统计完全一致），坏图标被当成功缓存。`SHDefExtractIcon` 与 `SHGetFileInfo` 均能拿真图标。

### 补充决策

25. **收纳板默认 1334×806 来自截图实测**（1920×1080@100%）；位置零新参数，锚边规则下自然落位；默认值对四方向统一生效。
26. **通用图标误缓存是空白图标的根因**：识别方案=惰性构建"系统默认文档图标"32×32 基准（假扩展名 + SHGFI_USEFILEATTRIBUTES）+ 降采样逐像素比对（平均差<3 判同图）；.url 优先走 IconFile 直取。缓存盐升 v3。
27. **WebP 由 Pillow 预转 PNG 入 resources\icons\**：WPF/.NET 8 无内置 WebP 解码，引库违反零依赖；转制时做与系统图标一致的裁边+8% 归一化。文件夹类项（含 .lnk→文件夹）统一用该资源。
28. **标题栏带=网格层之下的 34px 渐隐罩层**而非遮挡层：图标/指示灯天然不被遮挡，Scroller 顶边距 40 保证首行起始于带之下；带材质为主题色轻罩+下缘渐变（亮 #14→00、暗 #2E→00）。
29. **"跃出收纳版边界"=窗口根层幽灵（BounceHost）**：窗口内容无法绘到 HWND 之外；实现为弹跳位图在窗口根层播放（ScrollViewer 不裁剪），首行图标可覆盖标题栏带直至窗口顶边。原图标临时压暗用零时长动画而非本地值。
30. **弹跳编排整体从 IconItem 迁至 BoardWindow**（IconItem.PlayBounce 删除，参数原样保留在 PlayBounceUnclipped）；调用面=BoardWindow.PlayBounceUnclipped。
31. **showLabels=false 的文件名可见性**：悬停气泡与设置页开关均可找回名称，属用户要求的默认态。

### 给后续步骤的接口提示

- `BoardWindow.ApplyBoardPriority()`：切 `advanced.dockPriority` 时悬浮窗与收纳板各自经 SettingsChanged 联动，无需额外调用。
- 弹跳进 03 预设体系入口=`BoardWindow.PlayBounceUnclipped`。
- 06 步 MaterialEngine 接管时一并接管 TitleBand 配色（当前在 BoardWindow.ApplyTheme 内联）。
- `SystemBridge.ExtractIconPng` 通道次序与基准识别收敛在 SystemBridge，07 步加固只需外层包。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次三）

- **反馈**（14 项 + 2 额外）：①滚轮滚动加过渡；②双击无法打开；③指示灯被标题栏遮挡；④标题栏区分不明显；⑤滚动条到顶与设置按钮距离过大；⑥右键独特菜单（任务抛弃）；⑦子菜单改定时制（母项悬停 300ms 展开、离开 100ms 收起、同时仅一个）；⑧菜单宽度收紧至文字距边界 1.5 汉字；⑨无法斜向调尺寸；⑩关闭/设置下方设计阴影（共用）；额外1：弹出方向锚边紧贴**屏幕边缘**（非任务栏）、锚边不可调、锚边侧过渡阴影、center 四边不贴屏可拖动；额外2：默认屏幕横向居中、按五方向记忆。追加：⑪打开冷却 1.5s；⑫拖动时原位不显示原图标、无效位置松开回弹；⑬右键"移除"；⑭拖文件到收纳板=收纳。

### 补充决策

33. **锚边贴"屏幕完整边界"而非工作区**：收纳板可覆盖任务栏（Topmost 档）；max 尺寸同步放宽到屏幕完整边界。
34. **位置记忆语义**：`board.pos.<direction>` 记录窗口左上角（物理像素）；锚定轴每次强制回贴屏幕边缘（记忆仅对自由轴生效）；center 全自由；未调整过→默认屏幕居中——**位置与悬浮窗完全解耦**（原"水平对齐悬浮窗中心"废弃）。
35. **"移除"按文件名记录**（与 iconOrder 同口径）：同名新文件也会被隐藏。
36. **拖拽收纳=复制到桌面**（非移动、不删源文件）；"刷新不移除"天然满足。
37. **打开冷却在弹跳开始即计入**；按完整路径区分不同文件。
38. **GlassPopup 子菜单 StaysOpen=true + 显式放置**：定时器全链路单实例由静态字段+锁保证。
39. **平滑滚动用 CompositionTarget.Rendering 帧插值**而非 ScrollViewer 动画（无可动画的偏移属性）。

### 给后续步骤的接口提示

- `BoardWindow.OnBlankDragMove*` 三件套仅 center 模式生效。
- 05 步方向 UI 写 `advanced.popupDirection`（center 合法值），BoardWindow 的 SettingsChanged 已处理重锚边/阴影/热区。
- `FileOps.CopyIntoDesktop` 返回成功数；07 步可加失败汇总提示。
- `BoardModel.Removed` 为内存权威名单，来源 `state.removedItems`。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次四）

- **反馈**：①外部文件拖入后板不新增图标；②右键菜单点空白无法关闭、关板也关不掉；③指针悬浮子菜单仍会关闭；④菜单四角深色脏边；额外：上下左右锚边两角改直角+整体留几像素在屏幕外；默认弹出动画改为从屏幕外丝滑滑出。

### 根因与修复

| # | 根因 | 修复 |
|---|---|---|
| ② | 批次三子菜单 StaysOpen=true 破坏父菜单"点击外部关闭"；弹出层不随宿主隐藏自动关闭 | `ComponentDispatcher.ThreadFilterMessage` 过滤器：鼠标按下落在全部弹出层矩形之外→CloseAll；CloseBoard/Closed→GlassPopup.CloseAll |
| ③ | 子菜单内"普通行"走了"悬停→收起现存子菜单"逻辑 | BuildRow 增加 inSubmenu 标记：子菜单内行 MouseEnter 只取消收起计时 |
| ④ | DropShadowEffect 模糊越界部分被弹出层 HWND 裁掉 | Child 包一层四周 16px 留白 Grid 给阴影出血，阴影完整、圆角干净 |
| ① | 实测功能是通的（旧进程菜单卡死干扰） | 保留诊断日志；用户用新版重试 |
| 额外 | — | DirectionCorners 锚边两角直角（四处同步裁剪）；PinAnchorEdge 整体留 6px 在屏幕外；默认开合改从锚边屏幕外平移滑入 350ms + 淡入、倒放滑出 250ms（center 保留缩放） |

### 补充决策

41. **弹出层关闭策略改为显式过滤**（ThreadFilterMessage）而非依赖 StaysOpen 自动关：子菜单必须 StaysOpen=true 才不抢捕获，代价是自动关失效——过滤器统一判定，天然支持"点关板按钮先关菜单再执行动作"。
42. **菜单投影出血**：Child 外包 Margin 留白并补偿放置偏移（本版 16px）。
43. **锚边直角+出屏 6px**：让板与屏幕边缘视觉融合；ClampIntoScreen 对锚定轴豁免；滑出动画距离=板长+12px。
44. 拖拽收纳"刷新无效"未在现行版本复现（全链路实测通过），归因于卡死菜单的旧会话。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次五）

- **反馈**：①右键菜单单击选项后直接消失、不执行；②子菜单压在母菜单上；③为菜单关闭绘制动画；④标题栏下边界画一条不太明显的线。

### 根因定位（先插桩后修复）

| # | 根因 | 修复 |
|---|---|---|
| ① | 两层缺陷叠加：(a) 外部点击过滤器检查 `popup.Child is Border`——批次四已给 Child 包阴影留白 Grid，永不匹配；(b) 即使经 `popup.Tag` 取 Border，**lParam 是"接收消息窗口"的客户区坐标**，与屏幕坐标直接比较恒判外部 | (a) Build 时 `popup.Tag=border`；(b) `lParam` 经 `ClientToScreen(msg.hwnd)` 转屏幕坐标再比较（新增 P/Invoke 入 SystemBridge） |
| ② | `-ShadowPad` 双重补偿：WPF Popup 定位已自动排除子元素 Margin，又手动补偿 16px→整块菜单系统性偏左/上 16px | 移除 Show 与子菜单放置两处 `-ShadowPad`；子菜单锚点改按母菜单 Border 右缘+8px；宽高改实测尺寸 |
| ③ | — | 新增 `ClosePopup(animate)`：淡出+锚点微缩 0.96，130ms easeIn，完成后才 IsOpen=false；关闭期间禁命中 |
| ④ | — | `TitleBandLine`：1px Border 置于带底，亮 `#1A1B1B1B`/暗 `#28FFFFFF`，ApplyTheme 装配 |

### 补充决策

45. **WPF Popup 定位已排除子元素 Margin**：给弹出层 Child 包留白做阴影出血，都**不要**再补偿放置偏移。
46. **ThreadFilterMessage 的 lParam 是客户区坐标**：必须 `ClientToScreen(msg.hwnd)` 转换后再比较。
47. **菜单关闭动画=淡出+锚点微缩 0.96，130ms easeIn**（出现 120ms 对称）；Chain 先清空、过滤器先摘除，关闭中层禁命中。
48. **子菜单放置"先构建、后实测、再放置"**：Build 后 Measure 取实际宽高，右侧放不下（余量<4px）整体翻转，锚点=母菜单 Border 右缘外 8px。
49. **标题栏下界淡线配色**：亮 `#1A1B1B1B`/暗 `#28FFFFFF`，随 ApplyTheme 切换；06 步接管 TitleBand 时一并接管（同 28 条）。

### 给后续步骤的接口提示

- `GlassPopup.CloseAll(bool animate=true)`：05/06 步刷新菜单外观直接关开即可。
- 06 步接管 TitleBand 时同步接管 `TitleBandLine`。
- 03 步菜单类动画参数收敛在 GlassPopup.Build/ClosePopup。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次六）

- **反馈**（5 主 + 5 额外）：①弹出方向应依悬浮窗位置（贴右→右滑出等，居中→"果冻"），现状只从下边滑出；②收纳板无法横移/竖移；③不显示快捷方式扩展名；④设置按钮移回关闭按钮左边；⑤拖桌面文件到板=无效，非桌面文件拖入=仅"快捷方式"、插入落点腾位；⑥缩放中行列冻结+边缘渐变条、结束后动画过渡；⑦图标拖出板外=移除、拖动时隐藏文件名、消失后补位动画；⑧语义：移除=取消显示；删除=取消显示+删源；拖垃圾桶=取消显示+删源。

### 补充决策

50. **方向 auto=几何推断而非新动画**：贴边阈值 110 物理像素；距四边均 >110→center。方向在**每次开板时**求值。旧 `up` 一次性迁移的依据：方向设置 UI 尚未存在，存量 `up` 必为旧默认残留。
51. **横移/竖移口径**：上下滑出仅横移、左右仅竖移、center 自由；拖动中锚定轴由 PinAnchorEdge 持续回贴（保持 6px 出屏），松手 ClampIntoScreen+落盘。自由轴越界仍按屏幕完整边界钳回。
52. **虚拟项（仅收纳板"快捷方式"）**：外部文件拖入不复制源文件，只在 `state.virtualItems` 登记；图标走既有提取缓存；打开/定位/属性/固定均指向源路径；**移除/拖出板外=从清单彻底删除**（不进 removedItems）；删除（垃圾桶）=回收站删源+清清单；改名=仅改显示名。重名自动 "(2)" 递增。
53. **落点插入=线性格号重排**：拖放点换算 row-major 线性格号，插入到首个"格号 ≥ 落点"的项之前（其后整体后移一格），全量写回 iconOrder。
54. **缩放冻结=冻结网格视口宽**（DragStarted 记住 Scroller.ViewportWidth）；边缘渐变条 26px（边缘 22% 黑→透明），四边独立、锚定边隐藏、圆角随方向，仅缩放期间显示。
55. **拖出板外=移除**的判定：释放点在收纳板窗口矩形之外；垃圾桶优先级更高；拖影在松开处 200ms 淡出。拖动期间整项隐藏（含文件名）。

### 给后续步骤的接口提示

- 05 步方向 UI 直接写 `advanced.popupDirection`（六选，auto 默认）。
- 06/03 步接管图标过渡动画节奏：参数收敛在 `IconGridPanel.Approach/Epsilon`。
- 07 步多屏注意：EffectiveDirection 的屏幕边界与 dock 中心都取 `Screens.SelectedScreen()`。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次七）

- **反馈**：①移除/删除后留空位、不补位；②改为仅标题栏区域拖动才能移动；③新增左键点击板外收起；④外部文件拖入要**实时腾位**（悬停>100ms 后腾位、空位随悬停迁移、取消/失败恢复、落座入位），取消绿膜/复制角标；⑤首次打开收纳板卡顿。

### 根因与实现

| # | 根因 | 实现 |
|---|---|---|
| ① | 落点重排只在拖入插入时做，移除/删除/刷新走旧 fill 布局，空位永久留存 | **布局模型改为线性序列紧凑式**：Relayout 按当前视觉顺序紧凑重排，任何增删后空位自动补齐；iconOrder 只存可见项；拖动换位改"移动插入" |
| ② | 空白拖动放开到了全板身 | OnBlankDragMove 增加 `Y>34` 守卫——仅标题栏带内可拖动 |
| ③ | — | `Deactivated`→CloseBoard；主动让焦场景经 `SuppressOutsideClose()` 排除；CloseBoard 加 `_closeStarted` 重入护栏 |
| ④ | 批次六是 Drop 落地后才插入重排 | **DragOver 预览空位**：悬停格号变化重启 100ms 计时器，到期留洞、后方顺移（不写 iconOrder）；悬停迁移→洞迁移；取消→回流；Drop→插入洞位。删绿膜；DragOver 效果 Copy→Move |
| ⑤ | 首扫+容器生成+图标解码全挤在 350ms 开板动画里 | ①首载一律分批（16/批 Background）；②`LoadIcon` 加 `DecodePixelWidth=192`；帧差渐进收敛无整段冻结 |

### 补充决策

56. **布局模型定为线性序列紧凑式**：序列=当前视觉顺序，任何增删后紧凑重排不留空位；iconOrder 语义变为"当前布局快照"（只含可见项，变化才落盘）。拖动到某格=移动插入而非交换（旧"交换"废弃）。"自动排列"=清 iconOrder 回排序序。
57. **板外收起=失焦判定而非全局鼠标钩子**〔**注：批次八已改为中键 + WH_MOUSE_LL，见决策 60**〕：统一 `SuppressOutsideClose(ms)` 窗口期（2.5s，启动应用 3s）。
58. **拖入腾位预览不持久化**：预览只改 Row/Col 不写 iconOrder，"取消即恢复"零成本；Drop 时真项直接插进洞位。
59. **首开卡顿治理取舍**：分批 16/批 + 解码 192px 两刀摊薄峰值；未采用"启动即预构造"（批次八改为消散期预构造，见决策 62）。

---

## 步骤02 修订记录（2026-08-29 · 用户反馈批次八）

- **反馈**：①首开卡顿缓解不多；②左键板外收起导致无法完成拖入（拖动一起步资源管理器夺焦，板被关）——改中键；③希望拖出到板外的图标也能渲染。

### 根因与实现

| # | 根因 | 实现 |
|---|---|---|
| ① | 首开才构造：首扫+容器生成+图标解码挤在开板动画里 | **消散期并行预构造**：`DissolveStarted` 事件→App 在消散 ~500ms 内构造 BoardWindow；开板动画加**闸门**（等首扫完成+IconPanel 就绪+首载分批灌完，1.5s 兜底）；指示灯进程枚举挪后台 |
| ② | 批次七用 Deactivated 判板外点击——OLE 拖入起步即失焦→板被关 | 改 **WH_MOUSE_LL 全局低级鼠标钩子**：中键落点在板矩形外→收起；钩子随 OpenFor 安装/CloseBoard·Closed 卸载；移除左键失焦收起 |
| ③ | 拖影是 Root 内 Border，分层窗口画不出 HWND | 拖影搬进**独立 Popup 弹出层**（AllowsTransparency+StaysOpen+IsHitTestVisible=false），跟随光标、拖出板外全程可见；回弹改动画 Popup 偏移 |

### 补充决策

60. **板外收起最终形态=中键 + WH_MOUSE_LL**：失焦方案与 OLE 拖入天然互斥；低级钩子按物理坐标判定、不吞消息（恒 CallNextHookEx）、随板装卸。收起动作 Dispatcher.BeginInvoke 回 UI 线程。
61. **拖出拖影=独立 Popup 弹出层**：Popup 自带顶层 HWND 且天然 NOACTIVATE/不抢捕获；位置用 HorizontalOffset/VerticalOffset 驱动，回弹动画直接动画这两个 DP。
62. **首开卡顿最终形态=闸门式取舍**：闸门条件=首扫完成 && IconPanel 就绪 && 分批灌完（LastBatchTarget），1.5s 兜底；**闸门轮询必须用 DispatcherTimer，不得在 Loaded 优先级自排队**（会饿死 Background 分批）。
63. **诊断纪律**：本机单实例应用——自动化测试前必须确认无旧实例驻留，否则脚本实际驱动旧二进制。

---

## 步骤02 修订记录（2026-08-30 · 用户反馈批次九）

- **反馈**：批次八修复首开卡顿后，第二、三次打开又出现严重卡顿。

### 根因

闸门就绪条件 `_populateDrained` 是**一次性闩锁**：首开通过闸门时被消耗，而全代码只有 `RescanAsync` 会再置 true。第二及以后开板若桌面无变化，无人补位→每 25ms 轮询干等 1.5s 兜底（期间 Root.Opacity=0 完全不可见）。批次八"第二次 0.42s"是测试期 watcher 重扫恰好补位闩锁的假阳性。**"图标重新渲染"不是机制**：重开时容器与 frozen 位图全程复用。

未修复版实测（连开 3 次）：闸门等待 553ms / 1510ms / 1506ms。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/BoardWindow.xaml.cs` | `OpenFor` 非首载开板即刻补满 `_populateDrained`（新增 `_batchPending` 区分"没有待灌批次"与"首载分批仍在灌"）；新增闸门诊断日志 |

### 补充决策

64. **闸门闩锁语义修正**：`_populateDrained` 改为由 `_batchPending` 守卫的可重置条件——闸门真正语义是"**没有进行中的容器灌入**"；非首载开板即刻放行。批次八"第二次 0.42s"为测试期 watcher 重扫补位造成的假阳性。
65. **测试纪律补充**：桌面 Lively Wallpaper 像素持续变化——可见性检测改为**与板开后的参考帧比对相似度**，动态壁纸下依然可靠。

### 给后续步骤的接口提示

- 闸门诊断日志 `开板动画启动：闸门等待 Nms` 常驻（Info 级），07 步性能排查可直接读它定位开板延迟。

---

## 步骤02 修订记录（2026-08-30 · 用户反馈批次十）

- **反馈**：①拖动文件时文件名隐藏是跳跃式，要过渡；②外部拖入腾位有误（悬停同区域后方反复腾位/补位循环）；③总程序关闭/开启时悬浮窗要有出入场过渡；④收纳板中任何文件的消失与出现都要过渡。

### 根因与实现

| # | 根因 | 实现 |
|---|---|---|
| ① | 起拖隐藏原图标用零时长动画（瞬隐） | StartGhost 改 150ms 淡出；RemoveGhost 改 150ms 淡入（替换语义，淡出未完即松手从当前值续播） |
| ② | 拖入腾位悬停格号判定对格边界零容错，光标微抖→格号跳变→反复腾位/补位 | 新增 `CellIndexAtHyst`：14px 迟滞带（约格宽 20%），迟滞带内保持原格；Drop 落点仍用精确格号 |
| ③ | — | `FloatingDock` 新增 `PlayIntro()`（启动入场=三板自消散态聚合复现）与 `PlayExit(onDone)`（退出离场=消散编排后回调）；App 启动后调 `PlayIntro`；托盘退出/重启统一走 `App.ExitWithFade`（板开着先 `CloseBoard(revive:false)` 滑出），动画结束才 Shutdown/放锁拉起新进程 |
| ④ | 移除/删除/刷新均为直接改集合→容器瞬消；新容器瞬现 | 右键"移除"先 `PlayDeleteFade`（200ms）再 `FinishRemoveFromBoard`；watcher 刷新前对"将消失项"预演淡出并等 220ms；新项入场 `PlayAppear`（淡入+自 0.85 微放大 150ms，挂 DataContextChanged） |

### 补充决策

66. **拖入腾位格号判定引入迟滞带**：`CellIndexAtHyst` 仅服务 DragOver 预览（Drop 仍精确）；迟滞方向=必须深入新格 14px 才切换，边界抖动回到原格侧视为仍在原格。跨多格移动不受迟滞限制。
67. **消失动画三条编排统一为"先淡出、播完再动模型"**：右键移除=PlayDeleteFade→FinishRemoveFromBoard；拖出板外=拖影 200ms 淡出→RemoveFromBoard(false)；watcher 消失=对账前预演淡出+等 220ms。
68. **新项入场挂 DataContextChanged 而非 Items.CollectionChanged**：Reconcile 复用容器实例（不重建），DataContext 换绑=真"新项出现"（首载分批/虚拟项拖入/watcher 新文件三路全覆盖）。
69. **程序出入场复用消散/复现编排**：PlayIntro=PlayRevive、PlayExit=PlayDissolve（不走 `Dissolve()` 以免触发 DissolveStarted/Completed 误开收纳板）；退出编排收口在 `App.ExitWithFade`；重启路径单实例锁在动画结束、新进程拉起前一刻才 Release，防 250ms 窗口期内新实例误判退出。

### 给后续步骤的接口提示

- `App.ExitWithFade(Action? after)`：07 步新增退出触发源统一走它；`FloatingDock.PlayIntro/PlayExit` 为 public。
- `IconItem.PlayAppear/PlayDeleteFade` 即图标出入场动画唯一调用面。
- `CloseBoard(revive:false)` 语义=关板不复现悬浮窗（退出专用）。

---

## 步骤02 修订记录（2026-08-30 · 用户反馈批次十一）

- **反馈**：①第一次打开收纳板所有图标叠在第一行第一列，重排才正常；②腾位仍有误（只有拖到目标区域极边缘才正常）；③托盘右键"关闭"要=关闭程序。

### 根因与实现

| # | 根因 | 实现 |
|---|---|---|
| ① | 消散期预构造后，首扫分批回调里 `Relayout()` 因 `IconPanel=null`（模板未应用）全部早退，行列分配从未执行；`_populateDrained` 却照常置位→Show 后闸门立刻放行→容器 Row/Col=-1→全部落 (0,0) 叠住 | 闸门放行处（IconPanel 必就绪）补跑 `Relayout()`，并新增 `IconGridPanel.ResetLayout()` 清落位记忆（图标直接落位而非从 (0,0) 飞入） |
| ② | **OLE 拖动经过板时 DragLeave 高频误报、DragOver 大量缺席**（探针实锤 Leave 时 WindowFromPoint 仍是本窗口）；批次十迟滞带只治格号抖动，真实循环是"DragLeave→CancelDropPreview 补位→下次 Enter/Over 再腾位" | ①`DragEnter` 兼作 `DragOver`；②**板内 DragLeave 忽略**，只有光标物理位置真出板矩形才取消腾位 |
| ③ | 托盘"关闭"链路依赖离场动画回调必达；回调丢失则进程不退 | `ExitWithFade` 加**兜底强退**：1.5s 内动画回调未完成→强制落盘并 `Environment.Exit(0)` |

### ②的定位过程（教训）

- 用户日志铁证：同一格 14 以 ~250ms 反复"腾位预览"——唯一解释是 `_dropHole` 被反复清空（DragLeave 反复触发）。
- Win32 探针：误报 Leave 时 OLE 目标仍是板自己 HWND——WPF/OLE 对分层窗口命中变化的 Enter/Leave 风暴。
- OLE 拖拽测试必须真实按下在"文件行"上（PIL 截图定位→SendInput 按住→分步移动→悬停微抖→松手）。

### 补充决策

70. **首载布局必须"面板就绪后补跑"**：布局前提（IconPanel 非空）与数据灌完在预构造场景下顺序颠倒，二者之积才是"布局就绪"；闸门放行点是两者首次同时成立的时刻。
71. **OLE 拖放"取消"以光标物理位置为准，不信 DragLeave**：分层窗口 Enter/Leave 高频震荡，Leave 只能当噪声；Over 缺席由 Enter 兼任补偿。
72. **退出链路必须有与动画解耦的兜底**：凡"动画回调后再执行关键动作"的编排都配超时强退兜底（本版 1.5s→Flush+Environment.Exit）——动画回调在 WPF 里不是可靠信号。

### 给后续步骤的接口提示

- `IconGridPanel.ResetLayout()`：凡"容器可能在未分配格时生成"的场景（03 开板、04 分类页宿主）记得在布局补跑后调用。
- SystemBridge 的 OLE 探针三件套为诊断保留，07 步可复用。

---

## 步骤02 修订记录（续 · 2026-08-30 · 托盘"关闭"无效根因实锤与修复）

- **反馈**：托盘"关闭"无法关闭总程序。羽实测修复版后确认**完美解决**。

### 根因（诊断插桩抓到完整堆栈）

批次十把托盘退出接线写成 `_tray.ExitRequested += () => Dispatcher.Invoke(ExitWithFade);`——**裸方法组 `ExitWithFade` 带可选参数 `(Action? after = null)`**，C# 12 方法组自然类型把它推断为"要求 1 个 Action 参数"的委托，运行时 `Dispatcher.Invoke(Delegate, object[])` 走 `DynamicInvoke(空参数数组)` → **`TargetParameterCountException`** → 被全局异常钩子吞掉 → `ExitWithFade` 从未执行 → 点击"关闭"静默无效。

完整堆栈：`ToolStripMenuItem.OnClick → App.OnStartup lambda → Dispatcher.Invoke(Delegate, object[]) → DynamicInvoke → TargetParameterCountException`。**此前"每 ~3 秒一炸"= 羽反复点击托盘"关闭"的节奏**；"重启"一直有效是因为它写的是显式 lambda。

### 补充决策

73. **禁止 `Dispatcher.Invoke/BeginInvoke(裸方法组)`**：方法组自然类型遇上带可选参数/重载的方法会推断出"带参委托"，`Invoke(Delegate, object[])` 的 DynamicInvoke 在参数数不匹配时抛 `TargetParameterCountException`，被全局异常钩子吞掉后**动作静默失效**。铁律：一律显式 lambda 或 `new Action(...)`。`DispatcherUnhandledException` 里补打 `Environment.StackTrace` 是定位此类异常的唯一手段，保留为常驻诊断。
74. **"周期性异常"先怀疑用户在反复重试**：本例"每 3 秒一炸"就是人手重复点击无效按钮的节奏，不要急着找定时器。

## 步骤03 完成记录（2026-08-30）

- **范围**：步骤文件 03 全部任务（AnimationClock/Anim 基础设施、AnimTuning 常量唯一来源、BoardAnimPresets 5 套开合预设注册表、ThemeFade 工具、悬浮窗动画复查、02 步散落动画数值收编）。未做设置界面（05）、未动分类页（04）、未改 02 交互逻辑。

### 局限记录（步骤文件 1.1 要求）

`Timeline.DesiredFrameRate` 只是**期望帧率**：实际渲染受显示器 vsync 与 WPF 渲染线程限制——60Hz 屏上各档无差别封顶 vsync，高刷屏才能拉开档位差。档位变化日志留作排查依据。

### 补充决策

75. **expand 预设=00 §7 现行口径**（锚边从屏幕外滑入 350ms+淡入，关闭倒放 250ms；center=自中心缩放+淡入），**不采用** 03 步骤文件表内"自悬浮窗区域 scale 0.1→1"旧描述；差异化诉求由其他预设覆盖。
76. **BackEase Amplitude=1.5 而非 ≈0.35**：a=0.35 峰值仅 1.003（与"过冲 ≤1.08"矛盾）；a=1.5 峰值恰 1.08。
77. **不设 DockVanish=120 常量**：AnimTuning 以 `DockDissolve=250` 为唯一口径。
78. **预设切换"关闭状态下生效"=关板沿用开板预设**（`_openPresetId` 快照）；`Current()` 每次开板读键，05 步写键即生效。
79. **动画期输入遮罩=透明 Border + Preview 隧道事件置 Handled**（ZIndex 10000、挂/完即摘）。
80. **开合动画互斥=关板请求排队** + 闸门 `_closeStarted` 守卫（防两套动画互清 Root 属性）。
81. **Anim.Run 复位语义**：完成回调 `BeginAnimation(prop, null)` + 基值落定终值（等效 FillBehavior=Stop+手动复位，无回跳闪帧），并释放动画引用防泄漏；缓动实例全项目共享（静态冻结）。
82. **slideEdge 在 center 方向退化为"自下方上滑 48px+淡入"**（关闭倒放）。
83. **BounceArcsMs 语义=每弧总时长，apex 在弧中点**，总时长 220+200+180+160=760ms（与 00 "约 900ms"偏差是按 03 更具体的数值落地；回调只动 AnimTuning）。
84. **三个交互计时器字面量保留原位**（腾位 100ms/闸门轮询 25ms/位置防抖 500ms）：属交互协议节奏，非动画时长，不进 AnimTuning。

### 给后续步骤的接口提示

- **05 步**：预设下拉枚举 `BoardAnimPresets.All`（`I18n.Tr(preset.I18nName)`）；写 `personal.boardAnim`/`general.fps` 即生效，无需重启。
- **06 步**：主题过渡入口 `ThemeFade.Transition`；BoardWindow 已接入 `ApplyThemeWithFade`，ThemeEngine 接管时替换其内部 `ApplyTheme()` 调用。
- **04 步**：分类页开合动画可复用 BoardAnimPresets 或 Anim.Run。
- **07 步**：排查帧率先看 `动画帧率档 = N fps` 日志；`AnimTuning` 集中全部动画时长。

---

## 步骤04 完成记录（2026-09-25 · 新版顶部按钮行方案）

- **范围**：04 步骤文件（2026-09-25 改版）全部任务（顶部横向分类按钮行 CategoryTabBar、默认 全部+三内置、溢出折叠"更多"下拉、右键移除/重命名、拖入加入、板内视图切换共用主网格、`state.categories` 持久化）。**旧版（边缘按钮+侧开面板，2026-08-30）已整体回退**：CategoryPanel/CategoryEdgeButton 删除、`AnimTuning.Category*` 八常量清理、`panel.size.*` 键作废、EscLayer.CategoryPanel 移除；旧完成记录已从本文件删除。未做设置界面（05）、材质引擎（06）。
- **编号说明**：本记录补充决策自 94 起续编（旧步骤04记录的 94–101 随回退删除，编号不复用旧义）。

### 补充决策

94. **分类视图=主网格过滤**（非新面板）：`BoardModel.ViewFilter` 与显示开关叠加；可见集合经 Reconcile 差量切换，图标复用容器、缓动过渡天然获得（原文 L23"共用现有代码"的最短路径）。
95. **过滤视图内 Row/Col 仅显示值**：iconOrder 是全局顺序唯一权威，过滤视图紧凑重排不写盘；回到"全部"时从 iconOrder 还原；外部拖入在过滤视图一律追加全局尾。
96. **内置项"移除"=catShow* 子开关关闭**：与设置页同机制，重开即找回，数据不删；重命名仅存显示名覆盖（`name` ≠ id 才落盘）。
97. **"全部"常驻**：不持久化、右键置灰、不接收拖入（拖到"全部"=正常网格落点）。
98. **外部文件拖到分类按钮=登记虚拟项+加入成员**：外部图标在板上尚不存在，先入 `virtualItems` 再入分类，保证"分类页可见"；桌面已有文件仅加成员。
99. **折叠算法**：按钮宽 Measure(inf) 实测（Collapsed 态 Measure 恒 0，"更多"占宽用 FormattedText 直算）；每次重算从 TabVisible 全量恢复再折叠（增量可见集统计会把上次的折叠态误判为无溢出）。
100. **分类页不占 Esc 层**：EscLayer.CategoryPanel 删除；ShortcutRouter 现为 设置窗 > 收纳板。

### 给后续步骤的接口提示

- **05 步**：`advanced.categoryDisabled`/`catShow*` 写键即生效（Set* 已修广播）；"显示X分类项"子开关开=找回被移除的内置项。
- **06 步**：按钮配色/选中态收敛在 `CategoryTabButton.SetSelected`/`CategoryTabBar.ApplyTheme`；"更多"下拉复用 GlassPopup（材质接管自动跟随）。
- **07 步**：顶栏折叠重算挂在 bar.SizeChanged，多屏/DPI 变化无需额外接线。

---

## 前端 UI 批量修复 · 清单一（2026-09-25 · 用户 16 项任务拆批 1/3）

| # | 任务 | 落点 |
|---|---|---|
| 1 | 悬浮窗错开/图标跳起动画可中断接续 | `Anim.Run` from=null 语义改为"快照当前有效值→摘旧动画→基值落定→从快照起播"；`IconItem` hover 删除"上行未完排队回程"机制（旧机制强制播完整段=割裂根源） |
| 2 | 右键菜单宽度收窄 | GlassPopup：菜单内边距 6→4、行左右内距 14→3、勾选列 20→16、箭头列 14→12（文字距边界≈7px≈半个汉字）；子菜单翻转定位的母菜单左缘补偿 7→5 同步 |
| 4 | 收纳板边缘遮挡 | `AnchorOutsetPx` 6→1（出屏部分叠住首列图标=遮挡根因）；材质描边本就 1px 不动 |
| 5 | 标题栏移除 | BoardWindow.xaml 删 TitleBand/TitleBandLine；ApplyTheme/ApplyDirectionCorners 去引用；首行图标顶=34px（Scroller 顶距 36→28、GridHost 顶距 16→0）；顶部 34px 仍为拖动移动热区 |
| 9 | 按钮阴影取消+关闭钮重做 | Chrome/TrashBtn 的 DropShadowEffect 删除；关闭钮 hover 改"黑叉浮现+红点提亮"（CloseX Path + 非冻结画刷 ColorAnimation，替代旧 1.35× 放大） |
| 11 | 全部|+ 按钮 | CategoryTabBar：全部 与首分类按钮间插入 1×30px 灰白细竖杠与"+"按钮（透明灰白圆角底）；点击=CreateCustom+落盘+重建+立即进入重命名（命名冲突自动序号）；折叠算法计入二者固定占宽（恒显示不折叠） |

### 补充决策

101. **Anim.Run 接续语义全局生效**：所有 from=null 的动画二次触发均从当前值续播（含悬浮窗错开、图标跳起、hoverReveal、齿轮、气泡）；显式传 from 的编排（消散/复现等）不受影响，Completed 回调语义不变。
102. **新增分类入口**："+"=原文任务11 专属入口（原文 L9"本版 UI 无创建入口"作废）；新增即编辑，Esc/点外部取消后分类保留默认名"新分类N"。
103. **任务4 的"边框挡图标"按实测根因处置**：方向锚边出屏 6px 叠住首列图标（34_left_dir.png 实拍左缘图标被裁）；材质层描边本为 1px 不需要改。如用户所指另有其物（如缩放热区），清单三验收时追问。

## 清单一 · 修订 A（2026-09-25 深夜 · 用户复反馈）

| # | 问题 | 修复 |
|---|---|---|
| A1 | 短停留即离开 → 上行半途折返"瞬间回归"割裂（悬浮窗/齿轮/图标跳动） | 新增 `Anim.HoverMomentum`（接续+惯性并存）：上行未播完就离开=排队，播完自动回程；回程中再进入=从当前值接续上行。应用到悬浮窗错开、齿轮（旋转+亮度）、图标跳动；PlayBreathing/Dissolve/PlayExit 接管属性前 Reset 防闩死。批次十二 A 曾误删 IconItem 的同款排队（2026-08-28 用户反馈的原始机制），本次推广为通用类恢复 |
| A2 | 关闭钮黑叉偏大且不居中 | 8×8→4.5×4.5 几何（6px 对角视觉），改在 13×13（与红点同框）坐标系内画 X：`M4.25,4.25 L8.75,8.75 M8.75,4.25 L4.25,8.75`，Width/Height=13 强制同心，笔帽偏移对称化 |

## 清单一 · 修订 B（2026-09-25 · 用户裁决）

- **hover 动画规则改为"完整播放"**：`Anim.HoverMomentum`（接续+惯性并存）整体替换为 `Anim.HoverCycle`——触发后强行播放完整动画，播放中进/离触发一律忽略；播完核对指针实际位置补账（升完指针不在→补回程；降完指针回来→再升起），任何时序不折返不滞空。应用于悬浮窗错开、齿轮、图标跳动三处。`Anim.Run` 的接续语义保留（仍服务于非 hover 场景）。
- 决策 104：**接续语义只保留在 Anim.Run 内部**，hover 交互以"完整播放+补账"为准——用户实测后裁决接续/惯性混搭仍有个案割裂，规则越简单越好。
- 连拍实测：进入 100ms 即离开，离开后运动 ≈300ms+（残程升完+补账回程），无瞬间回归。
- 新会话提示词已落盘：`分步提示词/清单二提示词.md`（任务 3/6/10/7/13）、`分步提示词/清单三提示词.md`（任务 8/12/14/15/16），含全局规约与验收清单，全文发给新会话即可执行。

---

---

## 前端 UI 批量修复 · 清单二（2026-09-26 凌晨 · 用户 16 项任务拆批 2/3）

| # | 任务 | 落点 |
|---|---|---|
| 3 | 悬浮窗右键菜单 + 托盘"显示" | FloatingDock：`MouseRightButtonUp`（Idle/可见守卫）弹 GlassPopup（锚点 Plate、位置=光标）：隐藏=`HideByUser()`（`_userHidden=true`+Hide+`_state=Hidden`，暴露 `IsUserHidden`/`HiddenStateChanged`）；结束程序=`ExitRequested` 事件→App `ExitWithFade()`（显式无参 lambda 包 Dispatcher.Invoke，批次十一教训守约）。TrayController 新增"显示"项（默认禁用，`SetShowEnabled`）；App 接线：`ShowDockRequested→dock.Revive()+禁用自身`、`HiddenStateChanged→SetShowEnabled(IsUserHidden)`；Revive/PlayIntro 内 `ClearUserHidden()` 同步回落 |
| 6 | 垃圾桶改造 | TrashBtn 从右下角 46×46 移入顶栏 Chrome 行（齿轮左、22×22 容器+16px Viewbox 图标）；Path 拆盖/身两块，盖挂 `TrashLidShift`(Translate)+`TrashLidTilt`(Rotate，RenderTransformOrigin=0,1 铰链)；悬停盖升 2px（HoverCycle 完整播放）；`SetTrashHighlight(true)`：盖升 3px+转 −14°（侧开铰链感）、光晕+危险色保留、**TrashScale 1.15 缩放删除**；单击 `explorer.exe shell:RecycleBinFolder`（UseShellExecute）开回收站；ApplyTheme 同步盖/身两块配色；CatTabBar 右保护区 86→110 |
| 10 | 调尺寸阴影改模糊 | XAML 删 ResizeShadeTop/Bottom/Left/Right 四条渐变与 `UpdateResizeShades`；Begin/EndResizeChrome 改 `Root.Effect=BlurEffect`（Radius 0↔6，AnimTuning.ResizeBlur=180ms，EaseInOutQuad）；End 动画回 0 后 onDone 摘 Effect（含"期间又开一轮"的 ReferenceEquals 防误摘） |
| 7 | Tab 开关收纳板 | 新建 `core/KeyboardHotkey.cs`（WH_KEYBOARD_LL，参照 WH_MOUSE_LL 写法：委托持引用防 GC、UI 线程安装/卸载随 App 生命周期）；SystemBridge 补键盘钩子 P/Invoke（WM_KEYDOWN/WM_SYSKEYDOWN/VK_TAB/VK_MENU/GetAsyncKeyState/KBDLLHOOKSTRUCT）；App 决策 `OnTabPressed`：TextBox 聚焦放行→Alt 按住=吞掉并 `ForceToggleBoard`（开板置顶 `_forceTopmostNextOpen`→OpenBoard 落 Topmost=true；关板 onDone 里 `ApplyBoardPriority()` 回落）→无 Alt 时 `WindowFromPoint+GetWindowThreadProcessId+HWND 比对`命中悬浮窗/板才吞掉并走 ToggleBoard 同链路，否则放行。焦点虚线框：IconItem Root/GridHost/GearBtn/CloseBtn/TrashBtn/ScrollBar Thumb 加 `Focusable="False"` |
| 13 | 首开等待渲染+三点加载 | BoardWindow 暴露 `IsFirstLoadReady` + `FirstLoadReady`（**只发一次**；分批灌完回调/空桌面 LastBatchTarget==0/扫描异常 ForceFirstLoadReady 三路触发）；FloatingDock 加载态：XAML 两侧各 3 竖排 6px 圆点（列内 BeginTime 相位错开 140ms，0.2↔1.0 循环闪烁），`OpenGate` 闸门（Dissolve 前询问，未就绪→EnterLoading，悬停禁用、拖动保留）；App `OpenGate=IsBoardFirstLoadReady`（就地构造板）、`FirstLoadReady→dock.OnFirstLoadReady()`：停循环→左右外移 10px+fade 200ms（AnimTuning.LoadingScatter）→Dissolve→DissolveCompleted→OpenBoard（闸门即刻通过）；非首载路径不变 |

### 补充决策

105. **首载就绪与开板闸门闩锁解耦**：自检实测发现 `IsFirstLoadReady` 若直接用 `_populateDrained` 会被 TryBeginOpenAnimation 的一次性消耗（置回 false）卡死——第二次开板永远"未就绪"、悬浮窗永久加载态（zoom_q2_08 截图实锤圆点常驻）。新增不复位的 `_firstLoadPopulated` 事实标志承载"首载灌完"语义，`_populateDrained` 继续服务开板动画闸门。
106. **空桌面兜底**：`ReconcileBatched` 在 LastBatchTarget==0 时不排任何批次、回调永不到达——空桌面首载会在分批启动后立即自检置就绪；扫描异常走 ForceFirstLoadReady 防加载态永久卡住。
107. **Alt+Tab 吞键备注（用户已裁决）**：键盘钩子对 Alt 按住时的 Tab（WM_SYSKEYDOWN）一律吞掉，系统任务切换器不再触发；实现上 Alt+Tab 强制开合与托盘/单击同链路（板未显→消散开板+置顶提升，板显→关板+ApplyBoardPriority 回落）。
108. **加载态交互口径**：Loading 期间悬停错开禁用（OnMouseEnter/Leave guard）、拖动/按住仍响应、点击不误触未就绪开板链路（Dissolve 对 `_loading` 早退）——就绪后自动续链开板，无需用户再点。

## 清单二 · 修订 A（2026-09-26 · 用户复反馈三处）

| # | 反馈 | 修复 |
|---|---|---|
| A1 | 加载小圆点不明显 | 提速+提对比：`AnimTuning.LoadingPulse` 400→240ms（单次闪烁半程）、`LoadingPulsePhase` 140→80ms（相位错开更密），透明度振幅 0.2→0.12 起步（0.12↔1.0 明暗差更大），圆点填充 #96888888→#B4888888。实测 zoom_m2_01：两侧圆点清晰可见、明暗相位错开 |
| A2 | Tab 开合去掉"鼠标悬停"条件 | OnTabPressed 无 Alt 分支改为：板显→关板；否则悬浮窗可见（显示在最上层）→开板；两者都不可见→放行。删除 WindowFromPoint+HWND 比对的 PointerOverDockOrBoard。实测指针钉在屏幕左上角 (60,60) 也能 Tab 开/关板（m2_03/m2_04） |
| A3 | Alt+Tab 条件放宽为"程序正在运行" | 原实现已满足（钩子随进程安装，无任何额外前置条件），仅更新注释口径；强制开合对右键隐藏态/加载态/消散态的行为不变 |

## 清单二 · 修订 B（2026-09-26 · 用户复反馈三处）

| # | 反馈 | 修复 |
|---|---|---|
| B1 | 隐藏悬浮窗后 Alt+Tab 不生效 | 根因=按键序：钩子原先只认"Alt 已按住时按下 Tab"；用户"先 Tab 后 Alt"（或几乎同时）时 Tab 先到、那一刻 Alt 还没按下，被当普通 Tab 放行。KeyboardHotkey 改为双序触发：**每次 Tab 按下都进决策**（决策回调内区分 Alt/无 Alt）+ **Alt 按下时若 Tab 正被按住也进决策**（此时 GetAsyncKeyState(VK_MENU) 必为按下态→走强制开合分支）。⚠ 修订 B 初版曾把条件误写成"两键同按才触发"，普通 Tab 整体失效——隔离实验（对照 Tab 无响应）自查抓出后改正。ForceToggleBoard 入口加状态诊断日志（板显/悬浮窗显/右键隐藏），后续再出问题日志可直接定位。实测：隐藏态"先 Tab 后 Alt"→ 强制开板置顶 ✓（日志 00:54:38 组合触发 右键隐藏=True）；四探针（普通Tab开/关、Alt先手关/开）全部命中 |
| B2 | 右键菜单改悬浮窗上方横向小按钮 | FloatingDock 不再用 GlassPopup（纵向菜单）：自建横向按钮条 Popup——Placement=Top 悬浮窗上方水平居中（间隙 6px），胶囊小按钮（隐藏/结束程序，主题跟随亮暗、hover 提亮），出现=自下方 5px 上浮+淡入（MenuPop 120ms），Esc 可关 |
| B3 | 隐藏消失无过渡 | HideByUser 复用三板消散编排（PlayDissolve，DockDissolve 250ms）→ 播完 Hide+置 `_userHidden`+通知托盘；过渡期 `_state=Dissolving` 悬停/点击守卫自然拦截误触。截图抓拍到三板分离淡出中间帧 |
| B4 | 菜单显示时单击其余区域可关闭 | Popup 用 StaysOpen=false：WPF 原生捕获——单击菜单外任意区域即关闭（该次点击被弹层吞掉，不会误触消散/托盘），Esc 亦可关 |

## 清单二 · 修订 C（2026-09-26 · 用户复反馈：快捷菜单位置与尺寸）

- **位置**：原 `VerticalOffset=-6` 挂在窗口顶缘——三板可见顶缘其实在窗口顶 + `SquarePlate.BaseMargin`（24px 是消散/悬停动画行程区），菜单等于悬空在板顶上方 ~30px。改为 `VerticalOffset = BaseMargin - 4`，菜单底边贴板顶上方 4px（像素实测间隙 9px，含圆角观感），水平仍居中。
- **尺寸**：整体缩一档——按钮字号 12→11、按钮内距 12,4→9,3、容器 Padding 4→3、圆角 10→8、阴影 BlurRadius 12→10，上浮动画行程 5→4px。
- 构建零警告；截图 zoom_c2_01 确认贴合效果；程序保持运行交用户验收。

---

---

## 前端 UI 批量修复 · 清单三（2026-09-26 · 用户 16 项任务拆批 3/3 · 最后一批）

| # | 任务 | 落点 |
|---|---|---|
| 8 | 分类项按钮右键/重命名修复 | `CategoryTabBar`：ShowTabMenu 对"全部"**直接静默返回**（置灰菜单与 cat.all.locked 提示一并删除，i18n 键保留弃用）；BeginEdit 改造——进入前锁 `Width=ActualWidth`（不足取 DesiredSize）、结束 `Width=NaN` 还原自适应；EditBox 本体 Foreground/CaretBrush/SelectionBrush 全透明仅作输入捕获、不再 SelectAll（光标落尾），可见文本=覆盖层镜像 TextBlock 随 TextChanged 同步 + 文字右侧 1×14px 细竖杠（Opacity 1↔0 AutoReverse+Forever，`AnimTuning.CaretBlink=500ms`）；新增 `CategoryTabBar.IsEditing/IsEditHit/EndEditing()`，BoardWindow 挂 `Root.PreviewMouseLeftButtonDown`：命中点不在编辑按钮内→EndEditing（提交，与失焦同口径）；未选中常态/hover 底色统一低透明度灰（亮 #149E9E9E、暗 #22FFFFFF，IdleBrush），DragBrush 保留 |
| 12 | 刷新=重新从桌面拉取（逐个果冻渲染） | BoardWindow 新增 `RescanRebuildAsync()`（右键"刷新"专用，与 watcher 差量 RescanAsync、首载分批 ReconcileBatched 三轨并存）：后台 Scan+虚拟项合成 → **渲染前算好最终顺序**（iconOrder R,C 升序在先，无记录按当前排序配置缀后，`BoardModel.DesiredSequence()` 提供过滤+排序基序）→ UI 线程逐个 `Items.Add` 间隔 `AnimTuning.JellyRebuildStepMs=20ms`；果冻入场=IconItem 新增 `PlayJellyAppear()`（Squash X 0.7→1.08→1 / Y 0.7→0.92→1 关键帧+淡入，`AnimTuning.JellyAppear=260ms`），经 **BoardItem.JellyPending 一次性标志**（普通属性，不进 PropertyChanged/持久化）在 DataContextChanged 消费；过滤视图重建只渲染视图内项、收尾 ApplyCategoryFilter 自愈；防重入 `_scanBusy`+`_rebuildRendering` |
| 14 | 图标拖到分类按钮的归类语义 | OnGridRelease：过滤视图拖到**其他**分类=AddToCategory(目标)+LeaveCurrentCategory（新 helper：图标 PlayDeleteFade 播完→RemoveMember→MembersChanged→Reconcile，板/全局不受影响）；拖**回当前分类自身**=ReturnGhostToOrigin 回弹；过滤视图拖**出板外**=仅 RemoveMember（RemoveFromBoard 的 removedItems 全局移除语义不再误用）；"全部"视图行为不变；RemoveMember 改 public |
| 15 | 外部/桌面文件拖入恢复+"全部"=并集 | OnDropFiles 删除"拖入忽略"分支：桌面文件拖到空白=**移动到落点格**（MoveItemToCell 同内部拖拽，多文件 hole+added 连排保留）；过滤视图=AppendGlobalOrder 挪全局尾；已进 removedItems 的（不在陈列源）维持忽略；拖到分类按钮=加入该分类（保留）；外部（非桌面）文件 AddVirtualItem 机制不变。并集复核：虚拟项先入主网格再入分类、桌面文件本就在"全部"——天然满足 |
| 16 | steam/商店快捷方式归为"软件" | `DesktopScanner.Classify`：.lnk 目标以 `shell:` 开头 / 含 `AppsFolder` / `@{` 前缀 / 位于 `\WindowsApps\` 下 → **Apps**（新增 IsStoreLikeTarget）；.url 读文本 `URL=` 行，协议非 http(s)（steam:// 等）→ **Apps**，网页书签仍 Files（新增 ReadUrlTarget）；BuildFromPath 同路径生效（虚拟项受益）；图标缓存键 sha1(path+mtime) 不受影响，BoardItem.UpdateFrom 合入新 Type 无需迁移 |

### 补充决策

109. **"全部"右键=完全静默**：ShowTabMenu 对 Id=="all" 直接 return——连置灰菜单行与文字提示都不弹（用户明确"连文字提示也不要"）；`cat.all.locked` 键保留在 i18n 供回滚。
110. **重命名编辑="透明捕获+镜像呈现"**：可见文本与光标全部移出 TextBox（其自身全透明），选区高亮天然消失——比"调 SelectionBrush 颜色"更彻底（淡蓝底经实测确系选区高亮，方案按提示词预告执行，无需再确认）；按钮宽度用锁 Width 而非缩 MinWidth，结束必须清 Width 还原自适应。
111. **点编辑按钮外退出=提交（End(true)）**：提示词给出"取消或提交二选一"，取提交——与既有 LostFocus→End(true) 同口径、不丢用户已输入内容（Esc 仍是取消路径）。
112. **果冻入场用临时标志而非新事件**：BoardItem.JellyPending 普通自动属性，DataContextChanged 消费即清——同"换绑=新项出现"的既有管线（决策 68），不污染 PropertyChanged/持久化。
113. **重建式刷新不动 iconOrder 语义**：顺序计算只读 iconOrder（R,C 升序）+排序配置兜底，落盘仍由 Relayout/CompactSequence 负责；过滤视图重建期间只渲染视图内项，结束时 IsFiltered→ApplyCategoryFilter 自愈（期间切换分类也不脏）。
114. **过滤视图"移出"先播消失动画再动数据**：复用批次十"先淡出、播完再动模型"编排（PlayDeleteFade 200ms→RemoveMember→Reconcile 补位）。
115. **拖入桌面的文件不再忽略**：cship_20260925.log 实证用户拖的就是桌面文件；移入落点格与内部拖拽同语义；仅 removedItems 名单内（不在陈列源）维持无效操作——防拖入"找回"被移除项。
116. **DragLeave 探针日志标签纠偏**：原打印 `板内={outside}` 实为"是否板外"值（本次测试期误导排查），改 `板外={outside}`。
117. **协议链 .url 判定只认 URL= 行**：steam 建的 .url 无 TargetPath 可解析，读文本首条 URL= 行；非 http(s) 即 Apps（含 ms-windows-store: 等任意协议，按提示词口径）。

## 清单三 · 修订 A（2026-09-26 · 用户复反馈 3+4 处）

| # | 反馈 | 修复 |
|---|---|---|
| 1 | 刷新重建时图标逐个渲染但**全部叠在第一行第一列**，完成后才散开 | 根因：重建循环只 Add 不分配格，IconGridPanel 对 Row/Col=-1 的容器一律按 (0,0) 落位（"首次出现直接落位"规则），渲染完 Relayout 才集体弹开。改为**渲染前按填充序分配单元格**（左上原点行主序：第 1 个 (0,0)、第 2 个 (0,1)…，列数按当前视口现算），每个容器生成即落在自己的格位；全部渲染完成后 Relayout 按最终顺序（iconOrder）缓动排序——与用户描述"逐个安置到 1,1 / 2,1……完成后再排序"一致 |
| 2 | 刷新会重置"全部"中被移除的图标（桌面仍存在者）；拖入分类会在"全部"+该分类复活 | 复现定位：右键移除+刷新的过滤链实测无恙（渲染 38=44 源-6 移除，iconOrder 键集合逐一核对无移除项）。但实测复现出**真入口**：桌面拖放会携带桌面**多选**的全部文件（含被移除项），OnDropFiles 的移动/追加/入分类三路对已移除项无防线——MoveItemToCell 把移除项插进序列污染 iconOrder、AddToCategory 让移除项登记为分类成员。修复=全入口防线：①OnDropFiles 桌面分支对 `_model.Removed` 命中者直接忽略；②MoveItemToCell/AppendGlobalOrder 早退；③CompactSequence 不把移除名单内的名字写进 iconOrder；④AddToCategory 拒绝已移除项；⑤刷新重建日志增加"移除名单 N 项"便于观测 |
| 3 | 微软商店、Minecraft Launcher 被归为文件 | 根因：UWP 快捷方式的 TargetPath 经 COM 解析为**空**（任务16 的 lnk 目标判定全部落空）。新增 `LnkLooksLikeStore`：对 TargetPath 为空的 .lnk 做**字节级扫描**（UTF-16LE+ASCII 双解码找 AppsFolder/_8wekyb3d8bbwe/!App/WindowsApps/shell:/@{ 特征，AUMID 藏在 IDList）。实测两枚均含特征（Microsoft.WindowsStore_8wekyb3d8bbwe!App / WindowsApps）→ 归入"软件" |
| 4.1 | 最新建的分类被选中时文字黑色带白描边 | 根因：重命名的**镜像 TextBlock 残留**——编辑结束后可见文本层未清空，残留黑字叠在选中白字上形成"黑字白描边"假象（仅编辑过的按钮出现）。修复：End() 清空镜像文本；BeginEdit 时镜像前景色同步标签色（选中白/主题色） |
| 4.2 | 选中蓝色底改透明深黑 + 新分类按钮出现/选中颜色变化要过渡 | SetSelected 选中态改 `#59000000` 透明深黑（"更多"折叠选中提示同步）；底色/文字色改 ColorAnimation 过渡（非冻结画刷，CategorySwitch=150ms）；新分类按钮出现=淡入+自 0.85 微放大（Micro/EaseOutCubic） |
| 4.3 | 悬停分类按钮：底色透明度过渡拉高+微微放大 | 悬停色=同色相透明度拉高一档（亮 #2E9E9E9E、暗 #3CFFFFFF），RenderTransform 挂 ScaleTransform 缓动至 1.05（Micro），离开回落；选中/拖入高亮状态不受悬停覆盖 |
| 4.4 | 垃圾桶悬停：开盖做过渡 + 过渡出现灰色圆角方形底色，拖入图标底色深灰 | TrashBtn 内新增 TrashPlate 圆角 Border（CornerRadius 6）：悬停=主题灰过渡浮现+盖侧向开盖过渡（新增 TrashLidHoverTilt=8°，与上提并行，HoverCycle 完整播放）；SetTrashHighlight(hot)=底板颜色过渡转深灰（#59000000），拖离按指针位置回落悬停灰或淡出（回调复核防误隐藏） |

## 清单三 · 修订 B（2026-09-26 下午 · 用户复反馈两处）

| # | 反馈 | 处置 |
|---|---|---|
| B-确认 | 拖入与"刷新"都无法复活已移除的图标（桌面两个 txt、"全部"只显一个） | **确认修订 A 问题 2 防线生效**：桌面上的「新建文本文档.txt」仍在移除名单里被正确隐藏、「小鲸鱼的悄悄话.txt」正常显示——符合"移除不找回"语义，非缺陷 |
| B-1 | 指针停在图标**最底边**时：跳起→指针"离开"→落下→又碰到→无限振荡 | 根因：跳起动画的 TransformGroup 挂在 IconItem 根元素上，命中区随视觉一起上移 6px——指针在底缘 6px 内时进入/离开事件交替触发。修复：XAML 引入内层 JumpHost 承载跳起/挤压 Transform，**Root 命中区固定不动**。连拍 4 帧实测：指针钉在底缘，图标稳定保持抬起、无振荡（build/q1_strips2.png）；点击/拖拽命中链路不变 |

### 补充决策

118. **hover 动画的命中区必须与视觉解耦**：凡"元素被指针触发的动画会移动元素自身"的场景，Transform 挂内层视觉元素、命中判定留在外层固定容器——否则边界指针必然振荡。本次为 IconItem 落地；分类按钮的悬停放大是自中心向外扩（边缘只会更"包住"指针），天然无此问题。

## 清单三 · 修订 C（2026-09-26 下午 · 用户纠正移除语义）

- **用户纠正**：修订 A 对问题 2 的理解反了——"移除"=从当前视图隐藏（而非永久黑名单），**重新拖入与"刷新"本来就应该能复活它**，刷新复活只对"全部"分类项和桌面文件生效。修订 A 问题 2 的全入口拦截防线方向错误，本轮整体撤除并改为复活逻辑。

| # | 改动 | 说明 |
|---|---|---|
| 1 | `BoardModel.RemoveName` 不再从 `_source` 删除 | 源集合恒为桌面全量——这是复活的基石；移除仅加名单+Reconcile 摘出可见集合。附带修正：移除不再经 OnSourceChanged 清掉分类成员（"不影响 a/b"） |
| 2 | 新增 `BoardModel.RestoreName` | 移出名单 + Reconcile 放行回可见集合 |
| 3 | 拖入复活 | OnDropFiles 桌面分支：命移除名单 → `ReviveRemovedItem`（名单移除+持久化+RestoreName+日志），随后照常加入分类/移动落点。实测：ZCode.lnk 移除后拖入 → "拖入复活（移除项重新上架）" → 回到"全部"并移动到落点格 |
| 4 | 刷新复活 | RescanRebuildAsync 扫描后**复位移除名单**（SetRemovedItems 空 + SetRemoved 空 + 日志"刷新复位移除名单：N 项在全部重新上架"）。removedItems 天然只含桌面文件名（虚拟项移除即彻底删除不进名单）→"只对桌面文件生效"自动成立；分类成员不受刷新影响 |
| 5 | watcher 差量刷新（RescanAsync）**不复活** | 只有用户点"刷新"才复位名单——后台自动刷新不应悄悄撤销移除 |
| 6 | 撤除修订 A 的拦截防线 | MoveItemToCell / AppendGlobalOrder / AddToCategory / CompactSequence 四处 `_removed` 早退全部移除（复活路径需要它们放行；移除项经 ReviveRemovedItem 后本就已可见） |

## 步骤05 完成记录（2026-09-26）

- **范围**：步骤文件 05 全部任务（SettingsWindow 仿 mac 设置窗、7 件自绘控件库、5 个分页全部设置项 UI+绑定、配置导出导入、语言/字体/刷新率/主题全局热应用、悬浮窗自定义三层消费、隐藏桌面图标）。渲染内核（MaterialEngine/ThemeEngine）未做（06 步），材质/主题按占位样式跟随。未改 00 键名，未越界改分步提示词。
- **编号说明**：本记录补充决策自 119 起续编。

### 补充决策

119. 主题"自动"时段口径统一为 06:00~18:00 亮（00 §9 与步骤 05 一致；步骤 01 决策 7 的 19:00 临时值作废），每分钟检查在 ThemeAuto（UI 线程 DispatcherTimer），仅 auto 模式且折算翻转时广播 DarkChanged，各窗口自行走 ThemeFade。
120. 隐藏桌面图标的 0x7402 是切换不是置位：SystemBridge.DesktopIconsHidden 跟踪程序视角状态，同状态调用 no-op；程序退出按设置值恢复显示（OnExit）。
121. 主题翻转=设置窗整页重建（控件配色为冻结画刷，重建最可靠；折叠态/滚动位丢失可接受）；页面控件文字创建时赋初值，RefreshText 仅服务语言热切换。
122. 开发态自启动值=构建输出 apphost 绝对路径+--autostart（Environment.ProcessPath；发布态自然变为 Cship.exe，无需特判）。
123. 配置导入的热应用分工：settings 键经 Set 广播各自生效；state.categories/iconOrder 整段写入后由 BoardWindow.ApplyImportedConfig 重载（iconOrder+CatTabBar.ReloadCategories+LoadCustomBg+Relayout）。
124. 画布类自绘控件（ToggleSwitch/SliderX）继承 Canvas 并必须 Background=Transparent（Canvas 默认无背景不参与命中测试；子元素 Border 必须显式 Width/Height，Canvas 不约束子元素尺寸——两处实测踩坑）。
125. CropPicker 输出=原图像素坐标（显示区等比缩放换算），与 BoardWindow.LoadCustomBg 的 CroppedBitmap 口径一致；取消框选保留旧背景。
126. 动画预览为"同源参数缩略编排"：BoardAnimPresets 的 Open/Close 签名绑定 BoardWindow 无法复用，预览按 AnimTuning 同一时长/缓动独立实现（五套预设），播完停 600ms 自动关窗。
127. 设置窗"关闭=仅隐藏"且单例常驻（App._settings 复用），Esc 与红点同走 Hide；Present 时 ReloadFromSettings 防外部（导入）改值后显示陈旧。

### 给后续步骤的接口提示

- 06 步：SettingsPalette 为设置窗配色唯一占位点；主题五选的 transparent 折算在 ThemeResolver（现走系统亮暗占位），ThemeEngine 接管时替换；材质三卡预览图为静态占位（PersonalizePage.PlaceholderFill）。
- 07 步：SetDesktopIconsVisible 已在 SystemBridge（Progman/WorkerW 双路径），加固只需外包重试；--autostart 链路已在 App（LaunchedWithAutostart）；屏幕折叠项的"型号名"增强点=DisplayPage.RebuildScreenOptions。
- 08 步：版本号单一来源=csproj Version（AboutPage 自动读取）。

---

---

## 内存占用优化（2026-09-27 · 用户反馈"运行时内存 100MB+"）

- **约束**：不破坏最终效果与后续开发适配度（06 材质引擎/07 系统集成/08 打包的接缝全部保留）。
- **性质**：非功能性改动；全部动画节奏/交互/观感零变化。

### 说明与口径

- 128. **口径**：任务管理器"内存"列≈私有工作集，与 WS 同口径。优化前常驻 100MB+；优化后静置 ~15-38MB、开板峰值 ~72MB、关板回落 ~32MB。
- 129. **Private（提交字节）****：（提交字节）首开板后 ~165MB 属 .NET/GC 正常保留行为，非泄漏、任务管理器默认列不显示，未做 GC.Collect 强回收（避免交互期卡顿）。
- 130. **IconBitmapCache 复用面**： 缓存值均 Freeze 后共享，06 步 MaterialEngine/分类页缩略图等需要位图处直接取用即可；DecodePixelWidth 口径=显示尺寸×2（图标）/屏幕宽（背景）/256（层图）。

## 前端 UI 批量修复 · 10 项 bug 批次（2026-10-01/02 · 用户 10 项任务）

- **范围**：右键排序失效、主题改名、指示灯改版、设置窗 2/3+动画+开关重绘、折叠列表覆盖式展开、高级页去滚动条、遮罩重做、两枚小图标归一、设置窗优先级跟随、悬浮窗自定义重置。渲染内核（06）未越界。
- **编号说明**：本记录补充决策自 131 起续编。

### 补充决策

131. **排序/自动排列与"线性序列紧凑布局"的兼容**：批次七的 Relayout 语义=无 iconOrder 记录的项保留旧格位、VisualSequence 按格位升序——这与"清空 iconOrder 回排序序"冲突（清空后立刻按旧位置重快照）。修复口径：**清空 iconOrder 的动作必须同时打回全部格位**（ResetCellsToUnassigned），让未分配项按模型集合顺序（=过滤+排序结果）缀后进布局。
132. **遮罩形状判定在显示层做、随路径缓存**：64px 缩样四角+四边采样足够区分"满铺方形/磨圆/圆形 vs 异形剪影"；结果挂 `IconBitmapCache` 同路径键（不落盘、随进程），遮罩开关关闭时零开销。
133. **图标"大底小内容"二次裁切的三层判定**（满铺门 94% + 边缘均匀底 + 内容 ≤72%）+ **两遍底色圈**（外圈失败换内圈）+ **镂空边框第三遍**（内圈近全透→按不透明主体裁、扫描区内缩 1/16）：四层各自独立失败安全，任何一层不满足即维持原 alpha 包围盒结果，正常图标零影响。
134. **缓存盐 v7**：归一化算法四轮演进，盐随算法语义升级（v4/v5/v6/v7），旧代缓存文件留在 iconcache 不清理（体积小，08 步打包时可顺带清）。
135. **FoldingList 弹出卡用 Popup 而非占位展开**：覆盖式语义天然匹配 Popup（独立 HWND、StaysOpen=false 的捕获关闭语义）；代价是"点另一个折叠头需两次"（第一下被捕获吞掉）——与 macOS 弹层交互一致，接受。
136. **设置窗尺寸 600×413 的页面适配=双列 masonry 而非缩字体**：高级/个性化两页双列（`TwoColumn`），全局/显示单列放得下；行标签与节标题语义重复的一律去行标签防 207px 列挤压。
137. **设置窗 Topmost 策略并入 `advanced.dockPriority`**（原 2026-09-26"恒置顶"作废）：low=不置顶、medium/high=置顶，与收纳板同链路（SettingsChanged 即时翻转）；关闭=仅隐藏、重复打开=置前的语义不变。

### 给后续步骤的接口提示

- 06 步 ThemeEngine 接管时：指示灯配色（IconItem.ApplySettings 两行）与遮罩色（UpdateMaskPlate）一并换 Token；设置窗出入场动画如需统一编排可挪 AnimTuning 既有常量。
- 图标归一化若再迭代：`UniformBgContentCrop` 三遍结构独立可关（注释掉对应 attempt 即回退），盐再升 v8。
- 测试基建 `build/cuahelper.py` 可复用：按 PID 窗口枚举/区域截图/点击，后续验收脚本建议基于它而非裸坐标。

程序保持运行交羽验收～10 项全部落地！

---

---

## 前端 UI 批量修复 · 10 项 bug 批次二（2026-10-02 · 用户 10 项任务）

- **范围**：方形图标磨圆、设置窗恒压板上方、设置页滚动、指示灯重做、打开动画重绘为三点、设置控件去蓝化、高级页图标、双击开关语义、折叠项文字主题色、hoverReveal 圆形化。渲染内核（06）未越界。
- **编号说明**：本记录补充决策自 138 起续编。

### 补充决策

138. **满铺方形图标的"磨圆"在显示层做**（IconImage.Clip 圆角矩形+5% 内缩），不动提取缓存——形状分级结果随路径缓存，方形=Square、磨圆/圆形=Regular（两者都免遮罩板）、其余=Irregular（走遮罩）。
139. **指示灯必须挂在动画宿主之外**：凡"图标动画不应带动"的元素（指示灯/打开三点）一律放 Root 直接子级底部预留条；`IndicatorStrip=8` 是 IconItem 与 BoardWindow.Relayout 的共享常量，两处口径锁定。
140. **"文件正在使用或打开"两路判定**：占用=独占打开失败（后台线程批量检测）；打开=经板打开后 10s 保持（编辑器加载后不持锁是常态，不能让灯闪一下就灭）。
141. **打开动画与打开动作解耦**：三点先播、`FileOps.Open` 立即执行（不等动画）；合并时机=IsRunning 转亮（IconItem 监听）或 2.5s 兜底，打开失败走 AbortOpening 淡出——动画永不阻塞打开。
142. **设置窗层序策略=恒置顶+HWND 校正**：与板同为 topmost 层时激活次序不可控，`RaiseAbove` 以 SetWindowPos 插到板句柄正上方（NOACTIVATE 不抢焦点），开板与板激活两向触发。
143. **XamlReader 解析的样式字符串必须自带头部命名空间**（xmlns 与 xmlns:x 都要显式），且适合"与既有 XAML 逐字一致"的样式搬运场景；FrameworkElementFactory 写 Track.Thumb 这类非 DP 属性反而不可行。
144. **Segmented/Latch/按钮类"强调色填充"一律配 `OnAccent` 前景**：强调色亮暗翻转（黑灰↔灰白）后，固定白字必有一边不可读。

### 给后续步骤的接口提示

- 06 步 ThemeEngine 接管时：指示灯/三点配色（IconItem.ApplySettings/PlayOpening）与 SettingsPalette.Accent/OnAccent 一并换 Token。
- 打开动画参数在 AnimTuning.OpenDots*/DotsMerge；弹跳相关 Bounce* 常量与 Anim.BounceJump/BounceSqueeze 已无调用方（08 步打包前可评估清理）。
- SmoothScroller 可复用于任何 ScrollViewer（悬浮窗/弹层如需平滑滚轮）。

程序保持运行（pid 7196）交羽验收～10 项全部落地！

---

---

## 批次二 · 修订 A（2026-10-02 · 用户澄清任务 3 语义：舒展密度而非硬塞）

- **澄清**：任务 3 的本意不是"加滚动条"本身——各设置页此前为"全部项一口气塞进 600×413"把行距/内边距/控件内边距整体压缩。要求=恢复合理行高与间距，**放不下的子项被窗口裁剪、滚动查看**（窗口尺寸不变）。

### 改动（全部为间距放宽，无逻辑变化）

| 文件 | 改动 |
|---|---|
| `ui/pages/SettingsPageBase.cs` | Section 内边距 (12,8,12,9)→(14,10,14,12)、卡间距 6→8、节标题 12.5→13/下距 5→8；RowRaw 行上下 margin 0→5（行呼吸空间）、分隔线间距 2→3；PageBody (14,6,14,8)→(16,10,16,12)；Caption 下距 4→6 |
| `ui/components/LatchButton.cs` | padding (8,4)→(10,5)、右距 4→6 |
| `ui/components/Segmented.cs` | 段 padding (8,4)→(10,5)、容器 padding 3→4 |
| `ui/components/SliderX.cs` | MinHeight 30→34 |
| `ui/components/FoldingList.cs` | 头部/选项行 padding (8,4)/(9,5)→(10,6) |
| `ui/pages/AdvancedPage.cs` | 导出/导入按钮 padding (10,4)→(12,6) |

### 补充决策

145. **设置页密度口径=RowRaw 统一供行距**（上下 5px+分隔线 3px），控件只管自身内边距；"页面是否恰好放得下"不再是布局目标——溢出即裁剪+滚动（ScrollWrap），与任务 3 的滚动条/滚轮闭环。

---

---

## 批次二 · 修订 B（2026-10-02 · 用户复反馈：高级/个性化每项独占一行）

- **反馈**：高级与个性化两页右侧某些项堆在一行（内容过滤三闩锁、分类页三子开关、导出/导入、选择/清除图片），要求每个项占一行，并调高行宽。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/pages/AdvancedPage.cs` | **双列 masonry 整体撤除，整页单列**：内容过滤"软件/文件/文件夹"三闩锁各自一行（带行标签，新增 BuildLatchRow）；分类页三子开关各自一行；导出配置/导入配置各自一行；悬浮窗优先级行标签恢复；顺序=收纳板→图标与交互→分类页→悬浮窗优先级→配置 |
| `ui/pages/PersonalizePage.cs` | 双列撤除、整页单列：顺序=收纳板材质→弹出方式→自定义背景图（选择图片/清除图片各自一行）→悬浮窗自定义（重置独占一行） |

### 补充决策

146. **高级/个性化页废弃 TwoColumn**：单列全宽行是"每个项占一行+行宽调高"的直接落法；TwoColumn 帮手保留在 SettingsPageBase（后续宽窗可用），设置窗当前不再使用。

---

---

## 前端 UI 批量修复 · 4 项批次三（2026-10-03 · 用户 4 项任务）

- **范围**：全局页主题卡适配宽度、设置窗开/关动画重绘（径向渐显）、收纳板拖出屏回弹动画、弹出判定范围滑块。
- **编号说明**：本记录补充决策自 147 起续编。

### 补充决策

147. **设置窗径向渐显用 OpacityMask 而非缩放**："由中间向四周透明度逐渐降低"直接映射为 RelativeToBoundingBox 径向刷 + RadiusX/Y 动画；内圈 stop 0.55 保证中心区域先完整出现；开窗完成即摘遮罩（无常驻渲染开销）；关闭复用同一遮罩逆放——天然"与打开相逆"。
148. **弹出判定范围语义**：p%=两侧边缘判定带合计占屏幕高/宽比例（横纵各自归一化），中央 (100−p)% 内=居中弹出；25% ⇒ 悬浮窗处于屏幕中央 3/4 区域才判居中（与用户示例一致）；0=恒居中、100=几乎恒贴边；方向仍在每次开板时求值，改滑块无需重启。
149. **拖动松手回弹必须考虑动画与拖动赋值的冲突**：Left/Top 动画持有期间直接赋值无效——下次拖动按下时先 BeginAnimation(null) 清场；落盘延到回弹 onDone（防中途位置入盘）。

### 给后续步骤的接口提示

- 回弹动画=AnimateClampIntoScreen（含距离折算时长），07 步多屏/混合 DPI 的钳制若改口径只需同步此函数；
- dockEdgeRange 若要 00 §8 文档同步：键序在 advanced.popupDirection 之后、默认 "25"。

---

---

## 前端 UI 批量修复 · 11 项批次四（2026-10-03 · 用户 11 项任务，步骤 05 前后端为主）

- **范围**：三点打开动画改为循环到目标开启、折叠卡头部点击改关闭动画、语言切换过渡、显示页滑块 0.1 精度、文件名行数展开卡、悬停气泡重绘、窗口优先级语义重分、Tab/Tab+Alt 决策重做（含真全屏检测实装）、悬浮窗自定义三根因修复、开关去果冻+跟随主题开关、暗夜板底纯色黑。
- **编号说明**：本记录补充决策自 150 起续编。

### 补充决策

150. **悬浮窗 z 序与"窗口优先级"解耦**：悬浮窗恒走 Low（HWND_BOTTOM 桌面级），`advanced.dockPriority`（更名"窗口优先级"）只管收纳板+设置窗；设置窗跟随该档 + RaiseAbove 保证在板上方。
151. **FoldingList 关闭动画与"点外不反应"**：Popup 改 StaysOpen=true 后，关闭路径全部收敛到 CloseAnimated（淡出+上收，完成后 IsOpen=false 并复位透明度）；展开卡静态互斥防多卡同开。
152. **display.labelLines 行高口径**：IconItem.LabelSpace（静态）为 IconItem.Root.Height 与 BoardWindow.Relayout 的唯一来源——one=19px、two/full=35px；full 模式不裁剪文字（超出格位自然溢出显示）。
153. **色盘修复=视觉树两处叠加缺陷**：Canvas 不约束子元素尺寸（渐变层塌 0×0）+ RebuildFill 不装回 Child（第二次调用后整块消失）。教训：**"构造时手动补装"掩盖"方法内不装回"的缺陷，第二次调用才爆**——拆装视觉树的方法必须自洽（拆了自己装回去）。
154. **排障纪律**：改代码后若现象不变，先核对运行中二进制是否包含改动（本轮探针补丁转义损坏连累两轮构建失败跑了旧 exe）；独立最小复现探针（反射进私有类+落盘转储）比在主程序里盲改快得多。
155. **Tab 决策表**（OnTabPressed）：TextBox 聚焦放行 > 设置窗存在先关设置窗 > 全屏前台（板开=两键关+恢复档；板关=仅 Tab+Alt 开并临时置顶）> 常态两键开关板 > 都不可见放行（Alt 兜底强制开）。
156. **IsForegroundFullScreen 判定**：前台窗口 rect 完整覆盖所选屏（最大化窗口 rect=工作区天然不满足）+ 排除自身进程/桌面壳/DWM cloaked；真实现提前从 07 步落进 SystemBridge，07 步只需复核多屏。

---

## 收纳板彻底隐藏修复（2026-10-04 · 步骤 05 后端缺陷）

- **现象**：测试中收纳板完全不可见，每次开板（点悬浮窗/Tab）动画日志照常打印但板不出现。
- **根因**：`display.labelLines` 切到 "full" 后，`IconItem.ConfigureNameText` 的 full 分支执行 `NameText.MaxHeight = double.NaN`——WPF 的 MaxHeight 不接受 NaN（只接受非负有限值或 PositiveInfinity），每次 ApplySettings/IconItem 构造都抛 ArgumentException，经 XAML 加载路径升级为 XamlParseException，板内容加载中断 → 窗口永不显示。日志实锤：cship_20261004.log 全天 20+ 次 `“NaN”不是属性“MaxHeight”的有效值`（含 IconItem 构造、RefreshContainers、LoadBoardSize 三条链路，全部同一根因）。
- **次生缺陷**：DisplayPage.ToggleLabelLinesPopup 关闭路径只清 `_labelLinesPopup`，`_labelLinesSlider` 仍非空 → 重开时惰性构造被跳过，line 226 `popup.Child` NRE（日志 10:40 四次 NullReferenceException）。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/components/IconItem.xaml.cs` | full 分支 `MaxHeight = double.PositiveInfinity`（不限高语义），附注释说明 NaN 不被接受 |
| `ui/pages/DisplayPage.cs` | ToggleLabelLinesPopup 关闭路径同步清空 `_labelLinesSlider`/`_labelLinesZone`，重开走完整重建 |

### 补充决策

157. **WPF MaxHeight/MaxWidth 不接受 NaN**："不限高"必须用 `double.PositiveInfinity`；NaN 只属于 Width/Height（Auto）。同类陷阱：凡把 NaN 当"自动/不限"写入 Min/Max 系属性的写法都会在第一次赋值即抛。
158. **惰性构造+关闭即弃的控件要成对清理**：关闭路径置空容器引用时，必须同步置空其子控件字段，否则"子控件非空 → 跳过重建 → 容器为空"三段组合成重开 NRE。

---

## 清单01 · 设置窗口：展开卡同窗化 / 点外关闭 / 导航重播 / 圆角脏块 / 动画放缓（2026-10-04）

任务来源：分步提示词\BUG清单01（用户 17 项任务拆 4 批之第 1 批；02/03/04 清单留待后续会话执行）。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/animations/AnimTuning.cs` | ⑥ SettingsWindowIn 220→400、SettingsWindowOut 150→300（用户要求放缓开合）；新增 SliderStepSnap=130 |
| `ui/components/ExpanderOverlay.cs` | **新增**：展开卡承载层静态服务——Attach 全窗口 Canvas 进设置窗内容根；TryPlace 把卡按锚点坐标摆进层内并 clamp（四周 16px 阴影余量，装不下时收缩卡内 ScrollViewer 的 MaxHeight）；RegisterOpen/CloseAll 登记/收口；有卡打开时层背景转 Transparent 参与命中，点击卡外任意区域=CloseAll（⑤任务12） |
| `ui/components/FoldingList.cs` | **③（任务13）Popup 全删**：选项卡改经 ExpanderOverlay 渲染进设置窗自身可视化树（同窗、绝不跃出）；互斥改由 CloseAll 天然成立（静态 _openLists 删除）；关闭动画后从承载层摘除卡；重开前先摘旧挂载（防关闭动画未完又展开的竞态）；承载层不可用=拒绝展开+日志（防跳出同窗约束） |
| `ui/pages/DisplayPage.cs` | **③** labelLines 档位卡同款改造：Popup → ExpanderOverlay；点外关闭由承载层统一收口（原 StaysOpen=true 语义作废） |
| `ui/components/SliderX.cs` | **③** 档位过渡：Step≥1 的滑轨，Value 变化时滑块/填充以 SliderStepSnap 接续滑向新档（Anim.Run from=null 语义，连续拖动逐帧换目标不跳变）；动画只动视觉 transform，设置回写仍即时（连续滑轨 Step<1 与布局落位不受影响） |
| `ui/SettingsWindow.cs` | **①（任务5）** 窗口=视觉板 600×413+四周阴影落脚余量（W/H→648/467，板 Margin 24/24/24/30）——DropShadowEffect(Blur24+Depth5) 被窗口边界硬切是四角"脏块"根因，余量给足后阴影完整落进窗口；**②（任务11）** SelectNav 二次点击已选中项直接 return（不重建右列、不重播出现动画）；换页前 ExpanderOverlay.CloseAll（旧页展开卡随页消亡）；隐藏前 CloseAll（防残影）；Closed 时 Detach |

### 补充决策

159. **Popup=独立 HWND，与"同窗渲染"约束天然冲突**：凡是设置窗内"覆盖式浮层"，一律走 ExpanderOverlay（窗口内 Canvas 承载+背景命中点外关闭+clamp 不跃出），Popup 只保留给确需溢出窗口边界的场景。
160. **阴影必须预留落脚余量**： AllowsTransparency 窗口里带 DropShadowEffect 的圆角板，若窗口=内容尺寸，阴影被窗口边界硬切出"脏块"——外扩窗口、给板加 Margin（底边=Blur+Depth）是标准解；展开卡同理（TryPlace 的 shadowPad）。
161. **档位滑块过渡的边界**：动画只作用于视觉 transform（_thumbShift/_fillShift），Value 回写与动画解耦；Step<1 的连续滑轨不做过渡（拖动实时性优先）。

---

## 清单01 衍生修复 ×2（2026-10-04 · 用户实机反馈）

1. **再点展开展开卡的按钮=重新展开 → 改为关闭**：根因是点外关闭挂在 MouseLeftButtonDown——
   再点展开按钮时 Down 被承载层吃掉并关卡，但 **Up 漏给下层控件**（展开按钮/折叠头部都是
   MouseLeftButtonUp 触发），于是 Up 又把刚关的卡重开。修订：承载层成对消费 Down+Up，
   **Up 才触发关闭**；Up 时按原始命中目标判定（OriginalSource==承载层=点卡外才关；
   命中卡内=只吞事件不关——不能按 Down 判定，labelLines 滑块等自捕获 Down 的控件，
   其 Up 不标 Handled 会继续冒泡上来）。
2. **展开卡滚动条去除**：FoldingList 的 ScrollViewer VerticalScrollBarVisibility Auto→Hidden
   （滚动条不渲染，卡被 clamp 压缩时也不出现滑块/凹槽；滚轮滚动保留）。

### 补充决策

162. **浮层点击必须 Down+Up 成对消费**：同窗浮层的点外关闭若只吃 Down，Up 会落到下层
   Up 触发型控件上形成"关了又开/误触"二次动作；判定"点在哪"以 Up 的 OriginalSource 为准，
   因为卡内控件（滑块等）捕获 Down 后其 Up 仍会冒泡到承载层。

---

## 设置窗开合动画定档 750ms · 勘误与闭合可见性修复（2026-10-04 · 用户反馈"打开放缓了，但闭合不变"）

1. **勘误（重要）**：前两条记录中 `SettingsWindowOut` 的改动**实际未生效**——220→400 批次和 400→750 批次
   都只改了 In 的值与 Out 的**注释**，Out 的值行一直是 150。本批次真正落定：`SettingsWindowIn = 750`、
   `SettingsWindowOut = 750`（rg 验证值行）。
2. **闭合"看起来没变"的第二个根因（径向遮罩死区）**：遮罩内圈全亮区（0.55×R）在 R≥0.909 时完整盖住全窗，
   闭合用 EaseInQuad 前段慢走 → 前 ~430ms 无任何可见变化，可见塌缩挤在最后 ~320ms，观感与 150ms 无异。
   修复：闭合缓动 EaseInQuad→EaseOutQuad（半径变化提前，死区压到 ~130ms，750ms 全程可见收拢；
   开场仍是 EaseOutCubic 中心向外径向渐显）。

### 补充决策

163. **改常量必须核到值行**：连续两轮"注释声称已改、值没动"（编辑器把常量行留在 old_string 外）。
     纪律：改 AnimTuning 等常量后，`rg "常量名 = "` 核对值行，再对照运行时行为；"注释说改了"不算数。
164. **遮罩/渐变类动画存在"几何死区"**：评估观感时长要按"有效变化段"算，不能只看 Duration——
     EaseInQuad+起始即全覆盖的收拢动画，可见段会被压缩到尾部。反向（展开）因末段持续变亮无此问题。

---

## 设置窗闭合回调 300ms（2026-10-04 · 用户定档）

- 用户实机感受 750ms 闭合偏慢：`SettingsWindowOut` 750→300（`SettingsWindowIn` 保持 750 不变，rg 已核值行）。
- 闭合缓动保留 EaseOutQuad（决策164 的遮罩死区修复）：300ms 下可见塌缩段 ≈260ms，几乎全程可见，不回退。
- 构建 0 警告 0 错误，重启验证开/关路径正常。

### 补充决策

165. **观感定档以用户实机感受为最终裁决**：技术上的"全程可见"（164）与用户偏好时长是两个维度——
     750ms 全程可见但偏慢，最终 300ms+EaseOutQuad 是"死区修复"与"时长偏好"的合并落点。

---

## 修复：闭合动画进行中重开设置窗 → 窗口僵死打不开（2026-10-04 · 用户实机反馈）

- **现象**：设置窗 300ms 闭合过程中再次点齿轮，窗口打不开且此后永远打不开（板正常）。
- **根因（ Present 与 HideAnimated 的竞态，旧缺陷，300ms 放大了暴露面）**：Present 仅 `_hideGen++`
  打断 Hide 回调，但**半径动画不受代次管控**——会继续播完把遮罩半径归 0；窗口停留成
  "IsVisible=true 但遮罩全透明"的僵死态。此后 Present 恒走 `IsVisible→跳过 PlayOpen/Show` 分支，
  永远恢复不了。150ms 时代竞态窗口太窄未被察觉，300ms 放大后用户实机踩中。
- **修复**：`Present` 开头复位视觉态——若 `_plate.OpacityMask != null`（闭合动画在场），
  先停掉 RadiusX/RadiusY 动画并摘遮罩，再复位 Opacity；已可见时窗口立即完整显现，
  不可见时照常 PlayOpen+Show。
- **验证**：开设置→红点关闭→100ms 内重开齿轮，循环两轮：窗口每次都完整恢复且可交互（截图）；
  日志 0 异常；构建 0 警告 0 错误。重启（PID 15468）已顺带清掉用户卡死的僵死实例。

### 补充决策

166. **代次守卫只打断回调、不打断动画本身**：Anim.Run 的 Completed 里 resetOnDone 会把基值落定为
     终值（半径=0），无论外层业务是否"取消"——打断一个进行中的动画必须显式 BeginAnimation(null)
     并复位受影响属性，只 bump 代次会留下半途视觉态。凡是"动画驱动窗口可见性"的窗口，
     打开路径必须先复位关闭路径的所有视觉残留。

---

## 清单02 · 背景 45% 灰 / 板底与背景图不透明度 / 悬浮窗自定义重做 / 贴边延伸 10px（2026-10-04）

任务来源：分步提示词\BUG清单02（5 任务）。构建 0 警告 0 错误 + 实机验证通过 + 设置已还原用户原值。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/components/MaterialBackground.xaml.cs` | **任务1** 暗色板底 #E6000000 纯黑 → RGB(115,115,115)（0.45×255）@αE6；**任务7** 板底画刷改运行时构建：`SetBoardOpacity(0~100)` 把亮渐变 α(FC/EF) 与暗纯色 α(E6) 乘不透明度，`SetTheme` 存 `_dark` 后统一 `ApplyFill()` |
| `ui/pages/DisplayPage.cs` | **任务7** 显示页"图标不透明度"下新增"收纳板背景不透明度"行（`personal.boardOpacity`，0~100、Step0.1、% 单位） |
| `ui/pages/PersonalizePage.cs` | **任务8** 删三层 ColorPicker 行与 colors 读写/重置分支；自定义行重排=选择图片→缩略图→层标注紧随其右→行尾不透明度滑块（`personal.dockCustom.opacity` 三值数组）；"选择图片"按钮 VerticalAlignment=Center 修正拉伸偏高/文字不居中；**任务9** 背景图行重排=缩略图最左→选择/清除按钮，新增"背景图不透明度"滑块（`personal.boardBgOpacity`）；缩略图装载抽公共 `LoadThumb` |
| `ui/components/ColorPicker.cs` | **任务8** 整文件删除（再无他用） |
| `ui/FloatingDock.xaml.cs` | **任务8** ApplyDockCustom 删 colors 分支；层图 ImageBrush.Opacity=各自不透明度；设置监听 colors→opacity |
| `core/SettingsStore.cs` | 登记新键 personal.boardOpacity / personal.boardBgOpacity=100、personal.dockCustom.opacity=[100,100,100]；移除 colors 默认键（残留键无害） |
| `ui/BoardWindow.xaml(.cs)` | **任务9** 背景图 Opacity 应用+实时联动；1px 溢出修复：Root/CustomBgHost/CustomBgImage `UseLayoutRounding` + CustomBgHost `ClipToBounds`（圆角轮廓裁剪仍由 ApplyRoundedClip 负责）；**任务7** ApplyTheme/设置联动接 Material.SetBoardOpacity；**任务17** 见下 |
| `ui/BoardWindow.xaml.cs`（任务17） | `AnchorOutsetPx` 1→10：窗口矩形沿锚边向屏外延伸 10 物理px（`LoadBoardSize`/`ClampSizeToWorkarea`/`ApplyResize` 统一"窗口=可见+延伸"口径，`SaveSizeNow` 落盘扣回延伸=可见尺寸）；`ApplyAnchorExtension()` 给 Root 加"延伸量−1px 搭接保险"的内边距把内容平移回原视觉位置；Reposition 开头重入（DPI 随屏折算） |

### 补充决策

167. **窗口矩形延伸必须配"内容反向内边距"且尺寸口径全链路统一**：延伸只改窗口矩形，可见内容靠 Root.Margin=(延伸量−1px 搭接)平移回位（保留 1px 出屏搭接防 DIP 取整露缝）；Width/Height 从此含延伸，落盘（SaveSizeNow）扣回、加载/缩放/夹屏加回，四处口径必须同批改齐，否则出现 10px/次的尺寸漂移。
168. **延伸量按物理像素定、折 DIP 应用**：出屏量与 Root 内边距都用 `AnchorOutsetPx / 当前屏 DPI` 折算，保证任意缩放下"窗口出屏量=内容平移量"逐像素对齐（Reposition 开头重入以跟随 DPI 变化）。
169. **板底画刷从静态冻结改为按主题+不透明度运行时构建**：MaterialBackground 的亮渐变/暗纯色 alpha 口径（FC/EF/E6）保留为基准，乘 boardOpacity 后重建画刷；与背景图 personal.boardBgOpacity（只作用于 L3 Image）互不干涉。

---

## 暗色板底改"上深下浅"垂直黑色渐变（2026-10-04 · 用户定稿，取代清单02任务1 的 45% 灰纯色）

- 参考图 `前端问题预览图\6.png`（15×3 色带，色值 #060606 → #3B3B3B 线性）：`MaterialBackground.ApplyFill`
  暗色分支由 RGB(115,115,115) 纯色改为垂直 LinearGradientBrush（顶 #060606 → 底 #3B3B3B，α 仍按 E6×boardOpacity）。
- 实机验证：暗主题板右缘空白列自上而下采样 #070707 → #353636 单调线性变浅，与理论值吻合；亮色主题与
  boardOpacity/背景图不透明度链路不受影响；构建 0 警告 0 错误；主题已还原 light。

---

## 清单03 · 悬停气泡 / 行数展开齿轮 / 满铺图标判定 / hoverReveal 连坐 / 打开动画（2026-10-04）

任务来源：分步提示词\BUG清单03_图标_气泡_打开动画.md（任务 2/3/4/14/15；完成后该清单已删除）。
构建 0 警告 0 错误 + 实机验证通过（SendInput+截图）+ 设置已还原用户原值（iconStyle/dockPriority）。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/components/IconItem.xaml` | **任务2a** 气泡去掉径向渐变 OpacityMask（整体边缘虚化退役），CornerRadius 2→6=清晰圆角长方形半透明底；**任务15** DotsHost（三点宿主）删除 |
| `ui/components/IconItem.xaml.cs` | **任务2b** 气泡残留根因修复：OnMouseLeave 原 `Opacity > 0` 才淡出——淡入 BeginAnimation 后未走出第一帧时 Opacity 读 0，淡出被跳过而淡入继续播满 → 短停留后气泡常驻。改为 `_bubbleShowing` 状态位驱动，移出无条件取消淡入压回 0，onDone 兜底落定终态；**任务2a** 新增 `UpdateBubbleEdgeMask()`：悬停显示前按气泡相对板窗边缘的距离，仅对最近贴边侧装线性渐隐遮罩（带宽 12=板圆角口径，不贴边=全程清晰）；**任务4** AnalyzeShape 增加满铺统计（不透明包围盒 ≥95% 双向 + 填充率 ≥0.72 → 归入 Square）——Chrome/Xbox 满铺圆、Store 圆角方等"图案自带满铺底"统一内缩 5%+磨圆 24%，带透明留白的小圆/小方与异形剪影（企鹅等）不受影响；**任务15** 三点打开动画（PlayOpening/MergeDots/AbortOpening/RestoreIndicator）整体退役，新增 `PlayOpenPop()`=Squash 1→1.12→1 短缩放（AnimTuning.OpenPopMs/OpenPopPeak 入表） |
| `ui/BoardWindow.xaml.cs` | **任务15** OpenItem 重写：打开成功且开关开启 → `icon.PlayOpenPop()`（打开与动画并行，启动耗时远大于 220ms 动画）；三点循环闪烁链路删除 |
| `ui/pages/DisplayPage.cs` | **任务3** 行数展开按钮 chevron→齿轮：几何/13px 大小/hover 慢转 15°/GearSpin/微亮口径照搬 BoardWindow 板头齿轮；居中根因修复=旧 chevron 走默认布局（Stretch 对齐+几何原尺寸）漂出按钮，齿轮显式 Stretch=Uniform+双向 Center；展开态齿轮保持转动位（`_labelLinesOpen`），收起复位 |
| `ui/animations/AnimTuning.cs` | **任务15** Bounce*（Heights/ArcsMs/Squeeze*）与 OpenDots*/DotsMerge 死常量删除；新增 OpenPopMs=220 / OpenPopPeak=1.12 |
| `ui/animations/Anim.cs` | **任务15** BounceJump/BounceSqueeze 死代码删除（00 §7 表的 bounce 注释按约不回头改） |
| `i18n zh-CN/en-US.json` | **任务15** set.bounceOpen："弹跳打开动画"→"打开动画"/"Open animation"；**任务14** set.iconStyle："图标样式"→"显示方式"/"Display mode"（口径与清单验收行文一致） |

### 补充决策

170. **"读值做守卫"对尚未走出第一帧的动画不可靠**：BeginAnimation 后 GetValue 返回动画起点值（0），
     `Opacity > 0` 之类守卫会漏掉"淡入刚开始就要打断"的场景；打断/收尾类逻辑应使用显式状态位
     （本例 `_bubbleShowing`）驱动，终态在 onDone 兜底落定。
171. **打开动画语义重定义为"纯反馈"**：启用=短缩放弹一下（与启动并行，不再阻塞打开、不再有
     "三点循环到目标开启"的等待态）；禁用=完全无动画。旧弹跳（Bounce*）与三点（OpenDots*/DotsMerge）
     全链路退役，仅存 00 §7 表的注释留档。
172. **满铺判定以"包围盒近满幅 + 填充率"为口径**：圆=π/4≈0.785，阈值取 0.72（消抗锯齿余量）；
     空心/镂空异形（OBS 齿轮圈等）填充率低于阈值保持原分级，避免把异形误内缩。

---

## 清单03 同日两笔用户修订（2026-10-04 下午）

1. **打开动画方向反转**：用户裁定"先向中心快速缩小再复原"——PlayOpenPop 关键帧改为
   1→0.8→1（谷值 AnimTuning.OpenPopScaleMin=0.8，缩段占 40%）；JumpHost 加
   RenderTransformOrigin=0.5,0.5 使缩放以图标中心为锚（Jump 平移不受影响，删除/入场
   动画随之改为居中锚，观感更正）。实测抓拍：点击后 60ms 图标明显小于邻格、随后复原、窗口打开。
2. **任务4 满铺图标"参考仿制"方案试做后整体撤销**：曾按参考样张实现 TileIconFactory 重组
   （环带取主色画圆角瓷砖占 78% + 图案等比缩至 30% 居中，MC 黑框类整图缩放保边框），
   原型预览达成样张观感；**用户随即裁决"恢复原状，不要这个图标设计"**——当日撤销：
   TileIconFactory.cs 删除，AnalyzeShape/UpdateMaskPlate/LoadIcon 恢复 2026-10-02 口径
   （Square=四角实测，内缩 5%+磨圆 24%，无满铺圆/圆角方扩展、无重组）。教训：纯视觉类
   "参考图完全一致"需求先出原型预览给用户过目再动产线代码。

---

## 清单04 · 分段切换过渡动画 / 贴边弹跳闪烁核验（2026-10-04）

任务来源：分步提示词\BUG清单04_过渡动画_遗留核验.md（任务 10/16；完成后该清单已删除）。
构建 0 警告 0 错误；实机 SendInput+截图（滑块滑动中间帧已抓拍确认），**最终视觉观感验收由用户执行**；
测试改动的设置已还原原值（display.iconStyle→original、personal.dockCustom.mode→custom），最终实例日志 0 异常。

### 改动

| 文件 | 改动 |
|---|---|
| `ui/components/Segmented.cs` | **任务10** 选中底重构为**独立滑块**：结构改 Grid（滑块垫底 + 段面板透明），切换时滑块 Width + TranslateTransform.X 以 Micro/EaseInOutQuad 滑动接续（Anim.Run from=null 语义，连点不跳变）；段文字前景色改**非冻结克隆刷** + ColorAnimation 交叉淡变（同 Micro 节奏）；初始/换语言/换主题瞬时落位——段尺寸未就绪时 `_thumbPending` 记账，Loaded/Host SizeChanged/RefreshTexts 布局补跑后落位 |
| `ui/pages/PersonalizePage.cs` | **任务10** 悬浮窗自定义 预设/自定义联动面板切换改"旧面板淡出(EaseInCubic) → 收起换面板 → 新面板淡入(EaseOutCubic)"，单段 SettingsPageSwitch=160ms 与右列分页同节奏；`ApplyDockModePanels(animate)` 默认 false——初始载入/重置/设置回读仍瞬时落位 |
| `ui/BoardWindow.xaml.cs` | **任务16** 新增 `WCur/HCur`（Width/Height，NaN 兜底 Actual）；PinAnchorEdge / Reposition / ClampIntoScreen / AnimateClampIntoScreen / ApplyResize 的像素折算从 ActualWidth/ActualHeight 改为刚写入的目标尺寸（根因见决策 174） |

### 补充决策

173. **Anim.Run(from=null) 要求属性基值必须是有效数值**：对 Width/Height 这类默认 NaN(auto) 的布局属性做
     from=null 动画会构造 From=NaN 的 DoubleAnimation，BeginAnimation 即抛 ArgumentException；
     若异常被全局钩子吞掉则表现为"动画静默失效"——给这类属性上动画前先确保数值基值（构造时给初值，
     或先直接赋值再 BeginAnimation）。排查此类问题看日志 `[ERROR] UI 线程未处理异常`。
174. **写入 Width/Height 后立刻读 ActualWidth/ActualHeight 拿到的是上一帧旧值**（布局异步）：
     "刚写入的目标尺寸"场景（回贴锚边、钳位、居中）应直接读 Width/Height（NaN 兜底 Actual），
     或显式 UpdateLayout 后再读 Actual。锚定边缘的逐帧弹跳/落位偏差多源于此。

---

## 前端 UI 批量修复 · 8 项批次五（2026-10-05 · 用户 8 项，步骤 05 前后端为主）

任务来源：用户 8 项 bug 清单（步骤 05 设置界面相关为主，少量其他）。用户明确"完成修复后仅做代码逻辑验证、
视觉验收由用户执行"。构建 0 警告 0 错误；实机 SendInput + 逐像素测量验证（本轮脚本 `build/diag_*.py` +
截图 `d*/e*/f*/g*/h*/k*/m*/n*/p*` 系列）。测试期改动的设置与 state 已逐键还原。

### 补充决策

175. **`Unloaded` 清空状态字段必须配 `Loaded` 补挂**：`Unloaded += DetachItem()` 只应对"订阅解绑"负责，
     而 `_item` 同时是"当前项"状态位——容器被 ItemsControl 回收重挂（Move/排序）时 DataContext 不变、
     `DataContextChanged` 不再触发，`_item` 会永久停在 null（图标视觉正常、交互全哑）。凡"字段兼作
     订阅句柄与状态"的控件，重挂路径必须能从 DataContext 重新装配（本例 `Loaded → SyncItem`）。
176. **过滤视图的换位语义=成员槽位重排，而非改写过滤序**：iconOrder 是唯一权威（04 §7.1）——把新成员
     顺序放回这批成员在全局序里原本占用的槽位，非成员项相对顺序与槽位一律不动；视图内紧凑排列由
     `FilteredSequence`（按全局 (R,C) 过滤）自然给出，用户回"全部"时也能看到这次换位的落点。
177. **打开动画的合并时机必须覆盖"检测不到运行态"的项**：文件靠"刚经板打开过"标记、文件夹靠同标记
     （资源管理器不产生路径前缀进程，`AnyProcessUnder` 永远不亮）——否则三点循环永不收口；
     反方向"已运行项"要立即合并（IsRunning 不会再有 turn-on 事件）。
178. **主题/语言快照过渡的宿主必须包含"随主题换装的全部视觉层"**：设置窗只截板内内容根时，板底色
     （主题切换的主视觉）在快照之外瞬变，观感仍是瞬间换主题——快照宿主改为整块视觉板容器后才成立。
     判据：apply() 里被改写的视觉属性，凡不在快照内的都会瞬变。
179. **背景图与板底"描边层"几何对齐**：板底 `Border` 的 1px 描边把画刷画在描边以内（内缩 1px、半径−1），
     boardOpacity=0 时板底不可见只剩描边，背景图若贴满整块板就会把描边带占满（观感"图片超出板缘"）。
     背景图按同量内缩 + 半径−1 后与板底内层几何一致，板缘一圈交回描边本色。
181. **"即时反馈"与"等待态"分属两套动画，可并存**：短缩弹一下（Squash 1→0.8→1，220ms，点击瞬间即完）
     是反馈；三点循环（指示灯条，相位错开闪到 IsRunning 转亮）是等待态——两者动画对象不重叠
     （图标宿主 vs 指示灯条），由 `OpenItem` 一并触发、互不打断；判据：凡动画作用属性集合不相交、
     生命周期各自独立（一个单次一个循环），就不必二选一。
182. **Popup 的窗口优先级不会随宿主实时变化**：WPF 只在 Popup 创建时按宿主窗口置顶态落定一次，
     宿主切档后弹出层仍保持旧档（low 档下菜单依旧浮在最上层）。弹出层打开时按
     `Window.GetWindow(anchor).Topmost` 主动同步一次 HWND 置顶态（`SetWindowPos` 置顶带开关）。

## 前端 UI 批量修复 · 4 项批次六（2026-10-05 · 用户 4 项，步骤 05 前后端为主）

任务来源：用户 4 项 bug（步骤 05 前后端为主，少量其他）。构建 0 警告 0 错误；实机 SendInput 自动化验证
（脚本 `build/diag_batch6.py` 三档：`layout` 侦察 / `tip` 单点 / `full` 全量；轮询 CPU 代价另测
`build/diag_batch6_cpu.py`；证据截图 `b6_*` / `v_*` 系列）。测试用**自建探针项**（桌面
`0cship_probe_app.lnk`→记事本 / `0cship_probe_dir` / `0cship_probe_src`）避免动用户文件，收尾全部删除、
settings.json 与 state.json 逐字节还原。

### 补充决策

183. **WPF `Measure` 对 `Collapsed` 元素恒返 DesiredSize=0×0**（实测探针 `build/probe_collapsed_measure.ps1`：
     collapsed 0×0 / hidden 154.8×21.2 / visible 同）。凡"先量尺寸再摆放、末了才置 Visible"的定位代码，
     首帧必然按 0 尺寸算：居中用的 left 落到目标水平中心、上沿只剩间距 → 观感=浮层贴在目标右上角，
     要等下一次命中/布局触发的重算才复位。判据：量算前把元素转 `Hidden`（不渲染但正常参与量算）
     取真尺寸，同一帧内定位完再转 `Visible`，无中间态。
184. **"打开完成"与"关闭"的检测粒度可以分档，不必折中成一个间隔**：合并等待与熄灭等待都只取决于
     "下一拍何时到"，拍频越密 CPU 越贵。按态势选档——刚打开项目 300ms（4s 窗口，覆盖程序起速）、
     有运行项 700ms（熄灭 <1s）、全场空闲 2s（原值不动）。实测代价 +0.5pt 单核
     （空闲 1.69% → 活跃 2.21%），故保留 700ms。
185. **"刚经板打开过"的保持时长是双职责常量**：既给"检测不到运行态"的文件/文件夹提供三点合并时机，
     又是它们指示灯的点亮依据（资源管理器开文件夹不产生路径前缀进程）。缩短它（10s→4s）必须以
     "合并时机由更密的冲刺轮询兜住"为前提——冲刺首拍 0.3s 内即命中，故 4s 只影响"亮多久"，
     不影响"能不能合并"。
186. **单个 Windows 对话框无法同时选中文件与文件夹**：`FOS_PICKFOLDERS` 与普通"打开"互斥；
     旧式 `SHBrowseForFolder + BIF_BROWSEINCLUDEFILES` 实测只列文件夹、不列文件
     （`build/probe_browse.ps1` 截图佐证）。故"新建快捷方式"拆两行——文件走系统"打开"
     （筛选=所有文件，文件夹可逐层进入，校验关闭以便直接填文件夹路径同样成立）、文件夹走系统
     "选择文件夹"。判据：需求写"文件与文件夹都要能选"时只能拆入口，别赌一个对话框。
187. **"移入文件夹"的冲突策略=永不覆盖**：与拖拽收纳同口径走 `UniqueName` 自动 "(2)" 递增；
     自嵌套（目标位于源目录之内）与"目标即自身"直接拒绝返回 null 并回滚图标显示。数据安全优先于
     "和资源管理器一模一样"。
188. **拖动落点的新语义必须在"释放捕获之前"快照**：`GridHost.ReleaseMouseCapture()` 会同步触发
     `LostMouseCapture → EndDrag`（本轮 EndDrag 新增清描边），释放分支再读状态已被清空——
     与既有 `overTrash` 同一处理：进分支前先取快照。
189. **判定圆心取"格位原点"而非可视中心**：拖动中指针掠过目标会触发该图标的 hover 上跳（渲染变换挂在
     IconItem 内部的 JumpHost 上），若用 `IconVisual.TranslatePoint` 取中心，判定圈会跟着抖；
     IconItem 自身原点不含该变换，故圆心 = 原点 + (宽/2, TopPad+宽/2) 恒定。

## 前端 UI 批量修复 · 3 项批次七（2026-10-05 · 用户 3 项，步骤 05 前后端为主）

任务来源：用户 3 项 bug——①设置页"收纳板弹出方向"展开卡中的文字发虚；②按下 Alt 键时左列出现虚线框；
③在"高级"中新增"快捷键"区，内两项可对 Tab 与 Tab+Alt 的快捷键改键。构建 0 警告 0 错误；
实机自动化验证（键盘 SendInput + 逐像素量算 + 窗口枚举）12 项全 PASS，日志 0 ERROR / 0 WARN。

### 补充决策

190. **WPF `Effect` 与文字必须分层**：带 `DropShadowEffect` 的元素会把**整棵子树**先渲进一张中间位图再参与
     合成——文字落进位图即丢亚像素灰阶，元素又落在分数坐标时还会被整体重采样。实测同一张"收纳板弹出方向"
     展开卡：卡外同级文字（12.5px）中间灰占比 3.2%、暗核/中间灰 1.3~2.0，卡内文字（12px）中间灰占比高达
     **26.8%**、比值仅 **0.14**、最暗值 37~62（页面文字为 27＝纯黑）。修法＝把 Effect 挪到"只含纯色板底"的
     同尺寸底板上、文字所在的板不带任何 Effect，阴影观感零差异。判据：凡"容器带 Effect 且容器里有文字或
     1px 细线"，一律拆层。
191. **被低级键盘钩子吞掉的键不会进系统输入队列，因而不会更新 async 键态**：`GetAsyncKeyState` 读的是
     "进过系统队列"的键。快捷键录入若靠它读修饰键，会得到"按了 Ctrl 再按 K → 无修饰的 K"（本批前两版实测
     分别记成 `LeftCtrl`、`K`）。故录入态改由钩子自己给修饰键记账（按下加、抬起销，进入录入前先用
     `GetAsyncKeyState` 播种已按住者），且**按下/抬起成对吞掉**；"录入开始前就已按住的键"其抬起必须放行，
     否则系统会以为该键一直按着。判据：凡"钩子要吞键"又"要读全局键态"的场景，键态必须自持。
192. **全局快捷键匹配必须"修饰键精确匹配"**：旧实现"Tab 按下即触发"、决策里再区分有无 Alt，副作用是
     Ctrl+Tab / Shift+Tab 也被吞掉并开板。改为两个键位各自带修饰键集合、多按修饰键即不命中后，Ctrl/Shift+Tab
     正常交给系统（实测 Shift+Tab 前后板态不变）；"先 Tab 后 Alt"的旧双触发也一并消失（正常的 Alt 先 Tab 后
     序仍由"Tab 按下时 Alt 已按住"命中，实测默认 Alt+Tab 开合不受影响）。
193. **低级钩子给的 vkCode 是左右分体码**：只判 0x10/0x11/0x12 会把"先按 Ctrl"当成主键 `LeftCtrl`（0xA2）。
     `IsModifierOnly` 与录入态归一化都必须同时覆盖 0xA0~0xA5 与 0x5B/0x5C。
194. **设置窗按 Alt 的虚线框＝`ScrollViewer`（Control）的主题默认 `FocusVisualStyle`**：实测 Alt 前后差分
     包围盒 (25,25)-(174,435) 恰为左列 navScroll 的矩形。`Border` 等非 Control 无默认焦点视觉（弹出菜单里
     `Focusable=true` 的 Border 不受影响），故只需给设置窗内三处 ScrollViewer 置 `null`。
195. **两个快捷键不允许相同**：同键位无法判定语义，"静默取一"在用户眼里就是"改了没反应"；故整条修改作废
     （框内回退原值）并常驻红字提示，直到下次成功修改才隐藏。
196. **新增两个设置键在 00 §8 表外**：`advanced.hotkeyToggle`（默认 `"Tab"`）/ `advanced.hotkeyForce`
     （默认 `"Alt+Tab"`）。步骤文件禁改，故仅在此登记；配置导出/导入沿用既有 ConfigPort（未知键忽略），
     旧配置文件不含这两键时按缺键补默认自动升为 Tab / Alt+Tab。

## 收纳板界面修复 · 4 项批次八（2026-10-05 · 用户 4 项，步骤 02 收纳板为主）

任务来源：用户在检验中发现 4 项——①滚动条那一侧有一条"不透明背景"（很粗，压住末列图标又白吃宽度）；
②下/左/右边框偏厚，希望变成极细或去掉，让图标一直铺到最边缘才被挤走；③上下左右中五个方向的最小可调
尺寸都缩到原来的 1/3；④分类项放不下时的"更多"按钮改成磨圆三角形、带展开动画、动态跟在最右边分类项右侧。
**本轮按用户明确要求不做自动化视觉验证**，只做构建 + 日志冒烟，观感验收交用户。

### 补充决策

197. **滚动条那侧的"实心带"= 为滚动条预留的列宽 + 图标区外侧留白**：默认 `ScrollViewer` 模板把垂直滚动条
     放进独立一列（`Auto` = 滚动条宽 8px），再叠上 `Scroller.Margin` 的 10px 右边距，右侧就出现一条约 18px
     的实心带——它既压住末列图标，又白吃 18px 内容宽度。改 **Overlay**（滚动条与内容同格、右对齐浮在上面）
     后一像素宽度都不占。判据：凡"滚动条附近出现与内容无关的实心带"，先怀疑被预留了列宽，而不是去改颜色。
198. **图标区四周的留白就是用户感知里的"边框"**：板底 `BorderThickness=1` 已是发丝级（`MaterialBackground.PlateStroke`），
     真正"厚"的是图标区到板缘之间那段空白（左右各 10px、下 12px）。缩到 1px 后图标一直铺到板缘，
     只有真正越界才被窗口裁掉——正合"使图标在最边缘才会被挤走"。
199. **最小尺寸必须三处同改**：XAML 的 `MinWidth/MinHeight`（WPF 的窗口下限，不改会把代码设的小尺寸顶回去）、
     `ClampSizeToWorkarea`、`ApplyResize`。且三者都是"窗口矩形口径"，锚边贴屏延伸量要另加。原 480×320 →
     160×107（480/3=160、320/3≈106.7 向上取整），集中为 `MinBoardW/MinBoardH` 常量，别再散字面量。
200. **要让行末按钮"跟在最后一个可见项后面"，就得把它挂进那个 StackPanel**：`Visibility=Collapsed` 的子元素
     不参与布局，所以把"更多"挂到 `_tabsHost` 末位，它天然就落在最后一个**可见**分类项右侧，且随折叠结果
     自动移动。代价：`RebuildTabs` 里的 `Children.Clear()` 会把它一起清掉，必须记得重建后补挂回末位。
201. **"更多"按钮占宽可退化为常量**：内容从"更多 ▾"文字换成三角形后不随语言/文字变宽，于是旧实现那套
     "用 FormattedText 直算（因为 Collapsed 元素 Measure 恒返 0）"的绕行可以退休，直接给常量并注明推导。
202. **磨圆三角形的最省事画法**：`Path` 的 `Fill` 与 `Stroke` 取同一支画刷 + `StrokeLineJoin=Round`
     （端点圆帽同设），顶点即被描边圆化——不必手工拼圆弧几何。
203. **`GlassPopup.Show` 增加可选 `onClosed`**：弹出层的关闭收口在 `Popup.Closed`，而按钮侧的"展开态视觉"
     （三角形翻转）需要跟着回落；加一个可选回调比在调用方轮询/挂全局事件更直白，且旧调用点零改动。

## "更多"展开卡二次展开修复 · 批次八追加（2026-10-05 · 用户补报）

任务来源：用户回报——"磨圆三角形"的展开卡**收回后会二次展开**；要求交互与板内右键空白菜单一致：
单击板内其他区域即收回、卡已展开时再次点击三角形即视为收回。**本轮仍不做自动化视觉验证**，
只做功能性自检（枚举进程窗口数判断菜单开合，不看画面）。

### 根因（时序：按下收回 / 抬起展开）

`GlassPopup` 的"点外收回"由 **线程消息过滤器在鼠标【按下】** 时执行，而"更多"按钮的动作挂在
**【抬起】**（`MouseLeftButtonUp`）。于是同一次点击被拆成两个动作：按下 → 过滤器判定点击落在
弹出层矩形之外 → `CloseAll()` 把卡收走；抬起 → 按钮又 `GlassPopup.Show(...)` → **卡被重新弹出**。
实测弹出层矩形上边界 = 按钮底边 + 18（`ActualHeight + DropdownGap`），再算上阴影余量 16 之后仍在
按钮下沿之下，所以点按钮的按下一定被判为"点外部"——即用户 100% 能撞上这个 bug。

### 补充决策

204. **"点外收回"发生在按下、"按钮动作"发生在抬起——两者必须配对成一次点击**：过滤器在按下的
     `CloseAll` 与按钮在抬起的 `Show` 会让同一次点击"先收后弹"。修法不是挪动任何一边的时机，而是让
     被收回的锚点**在抬起时承认这次点击已经用掉**（`JustClosedByOutsideClick`）。判据：凡"关闭由底层
     消息过滤器触发、打开由控件事件触发"的组合，一律要有一条"同一次点击只算一次动作"的凭据。
205. **凭据必须按锚点区分，不能是全进程一个 bool**：否则"板内右键菜单开着时点三角形"会被误吞
     （用户下次点到三角形会没反应）。故登记的是"收回的是谁的菜单"，锚点只认自己那一张。
206. **未消费的凭据要留住过滤器**：点外收起后链已空，若顺手摘掉消息过滤器，下一次点击的按下就没人
     来清凭据，抬起时会拿着过期凭据把"打开"误判成"收回"。故链空时**先判凭据、后摘过滤器**。
207. **关闭回调要么带代次、要么别做视觉回落**：上一张卡的关闭动画（130ms）回调可能在本轮新卡已打开
     之后才到，会把刚翻上去的三角形又转回朝下。给回调带上"展开代次"，回调里比一次代次即可丢弃迟到者。

## 06 前置 · 性能与内存加固（2026-10-05 · 批次九）

- **范围**：步骤 06（材质引擎，性能敏感度最高的一步）开工前，对项目整体做一次"性能 + 内存泄漏"专项加固。
  **不改任何交互行为与视觉**，只做等价的实现替换（缓存、退订、节流、释放、护栏）。
- **未触碰**：`产品设计简要说明.txt`、`分步提示词/*.md`（00 §1.3 禁止改步骤文件）。故 06/07/08 的
  性能/内存增补约束以「给后续步骤的接口提示」形式记在本节末，可直接抄进对应步骤文件。

### 关键机理（为什么这些是真问题）

- **静态事件钉窗口**：`ThemeAuto.DarkChanged` / `I18n.LanguageChanged` 是 static event，lambda 或方法组订阅后
  不退订 → 事件源持有订阅者 → 整棵可视树无法回收。BoardWindow/CategoryTabBar 都是"宿主不 Close 所以现在
  看不出来"，一旦将来支持重建窗口即刻变成真泄漏 + 事件重复触发。
- **进程枚举的异常吞吐**：`Process.MainModule` 对系统/提权进程必抛；指示灯轮询是 0.3~2s 的常驻循环，
  原实现每拍全量重扫 = 每拍上百次首次异常 + 句柄 churn。
- **图标缓存整表清空**：条目数 > 128 时每次新增都清空全表 → 屏上所有图标重新解码（解码风暴），
  恰好在"分类页/多解码宽共存"时最严重。
- **`StateStore` 无防抖**：`SaveSizeNow` 一次写两个键 = 两轮全文档 JSON 重建 + 两次 `File.Replace`（UI 线程）。
- **`CropPickerWindow` 整幅解码**：选背景图这一下是单点内存峰值最高的操作。

### 补充决策（续 207）

208. **图标缓存淘汰=LRU 清最旧 32 条**，不再整表清空。屏上可见图标不会被误伤；`Get` 也会刷新访问时间。
209. **`ThemeResolver.IsSystemDark` 缓存 5s + `SystemEvents.UserPreferenceChanged` 即时失效**。
     06 步 ThemeEngine 接管 system 模式时**必须**改为经 `InvalidateSystemTheme()` 失效（或复用该缓存），
     否则"系统切深色即时跟随"会出现最多 5s 延迟。
210. **进程枚举按 PID 缓存（TTL 30s）**：代价是 PID 复用/进程瞬时重启时指示灯可能沿用旧路径最多 30s。
     实测可接受；若 07 步改为 Toolhelp32/`QueryFullProcessImageName` 原生枚举，应整体替换本缓存。
211. **文件占用探测 6s 节流**：语义降级为"最近 6s 内探测到的占用态"。被外部程序打开的文件 mtime 不变，
     所以不能用 mtime 变化做失效依据，只能限频；6s 是"灯亮晚了"与"磁盘 IO 频率"的折中。
212. **`StateStore` 防抖 200ms**：退出路径（`App.OnExit` / 强退兜底 / `ExitWithFade`）已有显式 `Flush`，
     掉电/杀进程最多丢 200ms 内的状态变更（此前是 0）。
213. **日志 5MB/天轮转**：超出后当天主文件改名为 `cship_yyyyMMdd.1.log`（覆盖旧轮转件），主文件从 0 重开。
     按天清理的 `cship_*.log` 通配会连带清掉轮转件。07 §6 的"5MB/天"口径即此实现。
214. **watcher 错误不再停用监听**：原实现 `Error` 里 `EnableRaisingEvents=false`，缓冲一溢出该目录就永久失明；
     现改为触发一次防抖全量重扫（07 §5 要求），监听保持有效。
215. **`CategoryTabBar.Detach()` / `SettingsPageBase.Detach()`** 是"消费方显式解绑"约定：静态事件的订阅方
     必须在宿主销毁时主动退订，不能只依赖 `Unloaded`（未完成首次布局即被换掉时 `Unloaded` 不触发）。
216. **打开三点 12s 超时**：文档类程序常检测不到 `IsRunning` 转亮，原来三点会无限闪烁占渲染动画槽位；
     超时后按"打开失败"收尾（与 `AbortOpening` 同路径）。
217. **框选器解码封顶 1440px**，crop 输出乘 `1/_decodeScale` 换回原图坐标，与 `BoardWindow.DecodeBackground`
     的 `ScaleCropRect` 同口径。
218. **`_shapeCache` 上限 256（满员整清）**：形状判定是纯路径派生、重算只是 64px 像素扫描，整清代价可忽略。

### 给后续步骤的接口提示（06/07/08 增补约束；步骤文件不可改，此处留档）

> 用户本轮要求"对 06/07/08 做性能与内存优化"。按 00 §1.3 未直接改步骤文件，以下条款可直接抄入对应文件。

**06（材质引擎）**
- 模拟通道抓屏：`CopyFromScreen` 全屏抓取必须**按"窗口几何并集"裁剪**，且只在 `DispatcherPriority.Render`
  执行；拖动/动画期间用冻结帧，禁止每帧抓。≥2 个可见玻璃窗口共享同一次全屏抓取（步骤文件已写，此处强调）。
- 纹理离屏缓存（`RenderTargetBitmap`）**必须有容量上限 + LRU**：借 `IconBitmapCache` 的淘汰口径，键为
  `(material, radius, theme, 尺寸档)`；否则拖尺寸滑块会无界增长（每档一张全窗尺寸位图）。
- `BlurEffect` 实例数上限：Effect 会让元素走中间位图合成，全板/全窗各挂一个是当前最大的隐式开销；
  06 落地时统计"同时存在的 Effect 个数"，与 02 步的缩放期模糊（`_resizeBlur`）互斥复用。
- `ThemeFade.Transition` 的 `RenderTargetBitmap` 快照：动画回调里**必须置空引用**（本批已确认无泄漏，
  但 06 会增加主题切换频率，需保持"用完即弃、不缓存"）。
- 背景图/材质预览缩略图统一走 `IconBitmapCache`（已支持任意解码宽），不要新增第二条解码路径。
- `ThemeResolver` 的 system 分支缓存见决策 209。

**07（系统集成与健壮性）**
- §4 线程化 + 本批已提前落地：`FileOps.RunningProcesses` PID 缓存、`StateStore` 防抖、watcher 64KB/重扫。
  07 若要改原生进程枚举，请整体替换 `RunningProcesses` 并删掉 PID 缓存。
- **仍未做的热点，建议 07 收口**（本批刻意未动，因为会改交互时序）：
  1. `BoardWindow.AddVirtualItem` / `CreateShortcutByPick` 在 UI 线程做 COM 解析 + 图标提取 + 写盘 →
     应挪进 `Task.Run` 后回 Dispatcher 挂模型（拖入一批文件时会卡 UI）。
  2. `UpdateReveal` 每次 `MouseMove` 对全部图标 `FindDescendant` 递归 + `TranslatePoint` → 建议维护
     `List<IconItem>` 缓存（容器生成/销毁时增删），本批只做了 `SetReveal` 端短路。
  3. 缩放拖动每个 `DragDelta` 全量 `Relayout` → 建议 30ms 节流或延迟到 `DragCompleted`。
  4. 设置滑块（`display.iconSize/rowSpacing/colSpacing`）逐帧广播 → 全板 `RefreshContainers` + `Relayout`；
     建议在 `BoardWindow.OnSettingChanged` 侧对这几个 key 做 16ms 合并。
- 性能目标复测口径：常驻内存 <250MB（本批实测：首开峰值 208MB / 稳定 81MB）、空闲 CPU≈0。

**08（打包验收）**
- 单文件 self-contained 的 `EnableCompressionInSingleFile=true` 会让**启动解压**变慢，冒烟要量"双击到托盘可见"
  的实际耗时（目标 <3s）；若超，先关压缩再评估体积。
- 回归表增加"内存与 CPU"行：开板/关板各测一次 WorkingSet，空闲 10 分钟看是否单调增长。

## 项目体积优化（2026-10-05 · 批次十）

- **原则**（用户口径）：优化项目体积，**不得破坏后开发难度与后开发兼容**。据此只清理
  "可再生 / 无引用 / 一次性过程产物"，源码、依赖资源、用户数据、开发工具脚本一律保留。
- **成果**：`636 MB → 7.6 MB`（其中约 3.4MB 是清理后重新 `dotnet build` 生成的 bin/obj，
  **纯源码 + 资产 + 文档 ≈ 4.3 MB**）。降幅 98.8%。

### 清理明细

| 对象 | 体积 | 判定依据 | 处理 |
|---|---|---|---|
| `build\*.png`（914 张） | **610 MB** | 历史验证截图/连拍/侦察拼图；结论已固化在本文件各批次记录里，不参与后续开发 | 删除 |
| `src\...\config\assets\*.png`（6 张） | **11 MB** | settings 里 `personal.boardBg = null`、`dockCustom.images` 全空 → **零引用**的测试残留副本 | 删除 |
| `src\...\config\iconcache\*.png`（237 张） | **6.8 MB** | 缓存键含盐值版本（当前 v7），旧盐值/已删桌面项的条目永不命中 | 清空（下次启动重建为 43 项 / 1.3MB） |
| `build\probe_picker\`（含 bin/obj/exe） | 0.8 MB | 一次性诊断探针项目 | 删除 |
| `build\__pycache__\` | 16 KB | Python 字节码缓存 | 删除 |
| 根 `nul`、空目录 `cship\` | ~1 KB | Git Bash 重定向误产生的垃圾文件 / 残留空目录 | 删除 |
| `state.json~RFab63.TMP` | 3.6 KB | `File.Replace` 崩溃残留的挂起临时件 | 删除 |

**明确保留**（后续开发要用）：`build\*.py|*.ps1|*.txt|*.json`（62 个测试/诊断脚本，151KB）、
`build\backup_batch6\`（388KB，非 git 仓库下的源码回滚保险）、`src\**` 全部源码、
`src\Cship\dependencies\resources\`（i18n + folder.png）、`前端问题预览图\`、`34_left_dir.png`
（STEP_LOG:1014 引用的实拍证据）、`素材\`、全部文档与 `settings.json`/`state.json`。

### 代码级护栏（防复发，`core/StorageMaintenance.cs` 新增）

本轮体积膨胀有两个**结构性根因**，只手工清理会再犯，故加进代码：

1. **iconcache 无淘汰**：盐值升级 + 桌面项增删改名都会留下永不命中的孤儿（实测 237 个里 194 个是孤儿）。
   → 启动时后台检查，超过 `600 个 / 32MB` 才动手：先删"30 天未写入"的，仍超限再按写入时间淘汰一半。
2. **assets 副本无回收**：选图即复制副本到 `config\assets\`，但清空/更换设置时**不删旧副本**
   （实测 6 张 11MB 全部无引用）。→ 启动时后台比对 `personal.boardBg.image` 与
   `personal.dockCustom.images` 的引用集合，删除无引用且**创建满 10 分钟**的图片文件
   （宽限期保护"刚复制、设置还没写入"的中间态）。

另加 `.gitignore`（约束"哪些内容不该进版本/发行目录"；本仓库当前非 git 仓库，规则同时充当
"什么可以安全清理"的清单）。

### 补充决策（续 218）

219. **体积优化边界=可再生/无引用**：删除前的判定标准是"能否由程序或构建重新生成"与"是否被
     settings/代码引用"。凡不满足两条之一者（用户数据、源码、工具脚本、证据图）一律保留。
220. **`assets` 副本回收采用"引用比对 + 10 分钟宽限期"**：不引入"历史图片版本"概念——
     产品设计未要求保留换图历史；若将来要"最近 N 张可选"，需先把 assets 纳入设置 UI 再放开清理。
221. **`iconcache` 阈值取 600 个 / 32MB**（而非按桌面项数）：正常桌面数百项时不触发，
     避免"清一半 → 重建 → 再清"的抖动；只有真正积累孤儿/旧盐值文件时才动手。
222. **`34_left_dir.png` 保留原处**：它是 STEP_LOG:1014 的证据引用，1.4MB 换取证据链完整值得。

### 给 08 的补充（发布体积）

- 本批清掉的是**开发目录**体积；**发布体积**（`dist\` 单文件 self-contained）由 08 步负责，
  建议在 08 增加：`<SatelliteResourceLanguages>en</SatelliteResourceLanguages>`（去掉非英文附属资源
  程序集，约省数 MB；WPF 中文界面文案走 `dependencies\resources\i18n`，不受影响）、
  `EnableCompressionInSingleFile=true`（已在 08 规划），以及"两架构体积 + 首启耗时"摘要入本文件。

---

## 06/07/08 步骤文件重写（2026-10-05 · 批次十一）

- **授权**：用户明确裁决——"**项目当前与未来的兼容性与新诉求高于一切**，01 的要求已经很旧了，06/07/08 要改"。
  据此 06/07/08 三份步骤文件按**当前代码实际状态 + 未来兼容性**重写（00 §1.3 的"步骤文件只读"
  经用户明确授权开例外，协议已同步更新）。
- **依据**：STEP_LOG 批次一~十全部修订 + 对现有代码接口的实地核对（`MaterialBackground` / `SquarePlate` /
  `GlassWindow` / `ThemeFade` / `ThemeResolver` / `Autostart` / `SystemBridge` / 各 sys 集成点）。

### 未改但已知过时、建议后续同步的 00/01 条款

以下**未动**（不属于 06/07/08 重写范围，且不影响施工正确性），列出供后续按需同步：

- 00 §7「首个位置」"上留 16px"：现状口径是"**可见板顶**距工作区顶 16px"（含义一致，措辞略粗）。
- 00 §7「拖动边界」"所选屏 `Screen.WorkingArea`"：现状按**可见三板包络**夹回（窗口含 72px 动画行程边距）。
- 01 §4.2 悬浮窗材质描述仍是骨架期占位版（已被 05 的自定义三层 + 本步的材质引擎取代）。
- 01 §4.4 消散时序仍写 120ms（已在 00 §7 动画表与决策 13 作废，01 正文未回填）。
- 01 §4.8 末段"BoardWindow 等其他派生类不受 dockPriority 影响"——**与现状相反**（该键现管收纳板/设置窗）。
- 01 §5 Esc 路由"分类页 > 收纳板 > 设置窗"——现状为"设置窗 > 收纳板"。
- 01b 全篇：骨架期补丁，其 medium 档设计已随悬浮窗改桌面级而失效；保留作历史记录。

### 补充决策（续 222）

223. **06/07/08 的「基线与时效」节效力最高**：与 00/01 冲突时让位给该节；00/01 中已被 STEP_LOG
     批次修订的内容以 STEP_LOG 为准（用户裁决：兼容性与新诉求 > 骨架期规格）。
224. **07 §8 全屏检测用途 = 方案 B（用户 2026-10-05 裁决，已落地）**：置顶的收纳板/设置窗在所选屏出现真全屏前台时自动退层、退出后按 `dockPriority` 恢复；medium 档遗留死代码（`FloatingDock.ProbeForegroundFullScreen` / `OnForegroundFullScreenChanged`）已删除。实现与实测见批次十三。
     （`FloatingDock.ProbeForegroundFullScreen`）；B=置顶的收纳板/设置窗在全屏前台时自动退层。
     施工前与用户确认；未确认按 A 执行并记录。
225. **06 性能预算取"实测可复现"的数值**：首帧 ≤40ms（中端核显）、抓屏 0 次（拖动/动画期）、
     `BlurEffect` ≤2、纹理缓存 ≤12 张/64MB、内存增量 ≤30MB、空闲 CPU≈0。超限即视为本步未过。
226. **08 的体积与启动耗时冲突时以启动耗时为先**（用户体感 > 磁盘占用），两组数值都要记录。
227. **发布参数 `SatelliteResourceLanguages=en` 是"体积换系统对话框文案"的取舍**：应用自身 UI 文案走
     `dependencies\resources\i18n` 不受影响，但 `MessageBox` 等系统对话框按钮会变英文；若在意可去掉该参数。

---

## 项目理念确立 + 成品文件树规格 + STEP_LOG 压缩（2026-10-05 · 批次十二）

- **用户确立的七条项目理念**（已写入 00 §0 作为**最高准则**）：**用户体验（最重要）**、动画美学 UI、性能、兼容、后开发、拓展、体积。冲突裁决：任一维度与用户体验冲突 → 用户体验优先；其余维度之间的取舍必须记入本文件并说明理由。
- **成品文件树规格（用户明确要求）**：`dock++\cship\` **自包含**——内含 `Cship.exe`（64 位）与 `Cship-x86.exe`（32 位）两个单文件主程序 + `dependencies\`（resources 与 config，两架构**共用一份**）；**不得硬编码绝对路径**；整个 `cship\` 目录移动到任意位置（含中文/空格/跨分区/改名）都能正常运行。
  - 已落地：`core/Paths.cs`（依赖根 = exe 旁 `dependencies\`，自包含语义写入注释）；00 §5.2 交付布局改写、§5.3 路径铁律同步、§5.1 补交付目录说明、§4 发布策略行改写；08 §2 组装布局（x86 产物重命名 `Cship-x86.exe`、resources 只放一份、config 不预置）、§3 移植性（以整个 `cship\` 目录为单位 + 改名测试）、§6 兼容性验收（布局 / 自包含可移动 / WOW64 共享 config）、§7 交付说明；`.gitignore` 增 `/cship/`。
  - 过程记录：曾按用户笔误 `dcship`（与 `cship` 平级）实现过**候选链路径解析**；用户澄清后已**回滚为单一的「exe 旁 dependencies」**（`Paths.DepsDir()` 无候选链）——最简且天然自包含。
- **STEP_LOG 压缩**：2669 行 → 1301 行（-51%）。
  - **保留**：全部「补充决策」条目（编号与原文，1–227 + 步骤02 局部 1–10）、全部「给后续步骤的接口提示」、关键根因与教训、每批次标题与范围一句话、最新三批次（九/十/十一）全文。
  - **删除 130 个子节**：「测试残留与交代」「留人工验证项」「改动文件」表格、「验证」过程细节（截图文件名 / 连拍 / 逐像素步骤）、「测试期缺陷 / 事项」叙述、「逐项验收清单」。
  - 跳号（32/40/85–93/104/128–130/180）为原文既有，**未补号、未重编号**（保证 06/07/08 对决策号的引用有效）。

### 补充决策（续 227）

228. **项目理念七维度写入 00 §0 为最高准则**：用户体验是最终判据；其余维度（动画美学 UI / 性能 / 兼容 / 后开发 / 拓展 / 体积）冲突时须把取舍理由记入本文件。
229. **交付布局定为「`cship\` 自包含」**：32/64 位两个 exe 同目录、`dependencies\` 在 exe 旁（候选链方案已回滚）。整个目录可任意移动 / 改名；**两架构共用一份 config** 是本布局的核心收益。
230. **STEP_LOG 只记「仍有效的信息」**：过程叙述（改动清单 / 验证细节 / 测试残留 / 留人工项）不再累积——对后续 Agent 无价值且拖慢必读流程；有效信息 = 决策、接口、教训、当前口径。
231. **决策编号不重排**：历史跳号保留（引用稳定性优先于编号美观）；新增决策一律续编最大号 +1。

### 给后续步骤的接口提示

- 06/07/08 的「基线与时效」节已是各自最高依据（决策 223）；本文件其余节为背景与决策库。
- 08 步产出目录 = **仓库根 `cship\`（自包含）**，不是 `dist\`；脚本内部可先暂存 `build\_publish\`。
- `Paths.DepsDir()` **无候选链**：若将来真要「exe 与 dependencies 分离」，需连同 00 §5.3 一并改，不可只改代码。

---

## 07 §8 方案 B 落地：全屏前台退让（2026-10-05 · 批次十三）

- **用户裁决**：07 §8 全屏检测采用**方案 B**——置顶中的收纳板 / 设置窗在所选屏出现真全屏前台时**自动退层**，退出后按 `advanced.dockPriority` 恢复。
- **改动文件 4 个**：
  - `App.xaml.cs`：新增 `StartFullScreenWatch()`（1s `DispatcherTimer`，Background 优先级）+ `OnFullScreenTick()` + `ApplyRetreat()`；`OnStartup` 启动、`OnExit` 停止。
  - `ui/BoardWindow.xaml.cs`：新增 `SetFullScreenRetreat(bool)`（退层 / 回调 `ApplyBoardPriority()` 恢复）。
  - `ui/SettingsWindow.cs`：新增 `ReapplyPriority()` + `SetFullScreenRetreat(bool)`。
  - `ui/FloatingDock.xaml.cs`：删除 medium 档占位 API（`OnForegroundFullScreenChanged` / `ProbeForegroundFullScreen`）及其调用——随“悬浮窗恒桌面级”一并作废。
- **防抖动设计（关键）**：tick 的“是否值得检测”判定必须用**优先级档是否要求置顶**这个稳定事实，**不得用 `Topmost`**——退层后 `Topmost` 已是 false，用它判定会立刻走复位分支，与退层互相触发形成每秒抖动。
- **开销控制**：仅当“优先级档要求置顶 **且** 至少一个窗口可见”时才做 P/Invoke；当前默认 low 档 → 零检测开销（空转只判两个布尔）。
- **实测（端到端）**：临时把 `dockPriority` 置 `high` → 启动 → Tab 开板（`Topmost=True`）→ 弹一个覆盖全屏的黑窗 → 日志出现“全屏前台：收纳板临时退层（Topmost=false）” → 关闭黑窗 → “全屏退出：收纳板恢复优先级档”（`Topmost=True`）。全程 **0 ERROR / 0 WARN**，**无抖动**（各只有一次切换）。测试后 `settings.json` 的 `dockPriority` 已还原为 `low`，`state.json` 的 43 项 `iconOrder` 完好。
- 构建 0 警告 0 错误。

### 补充决策（续 231）

232. **全屏退让只管“置顶窗口”，恢复一律回调优先级落定方法**：不写死 `Topmost=true`——low 档下退让本身即 no-op，恢复也不得把窗口顶起来。判定条件同样以“优先级档”为准而非 `Topmost`（防抖动，见上）。
233. **作废逻辑必须真删**：medium 档占位 API 一旦其用途被新机制（App 层统一退让）取代，就随用途一起删除，不留“将来可能用”的空壳——空壳会让后续 Agent 误以为那条链路还活着。

---

## 07 §8-B 修正：强制开板会话挂起全屏退让（2026-10-05 · 批次十四）

- **用户报障（实机）**：`advanced.dockPriority=high` 下，全屏 Minecraft 前台按 Alt+Tab（`advanced.hotkeyForce`）→ 收纳板出现后**立即消失**。
- **根因（日志实锤，非推测）**：两套机制互搏——任务7 的 Alt+Tab 强制开板把板提到 `Topmost=true`，1s 轮询的全屏退让监视（07 §8-B）紧接着检测到“游戏仍是真全屏前台”，把同一个 `Topmost` 抹成 false，板遂被满屏游戏盖住。日志逐次复现：`21:55:03.599 收纳板 Alt+Tab 强制开板：Topmost=true` → `21:55:04.055 全屏前台：收纳板临时退层（Topmost=false）`（相隔 456ms），用户每次 Alt+Tab 都成对出现。
  - 附带确认：全屏游戏在前台时**不会**被收纳板的 `Activate()` 抢走前台（否则 `IsForegroundFullScreen` 会因“自身进程”返回 false 而放过）——所以这不是激活时机问题，是退让逻辑缺了“用户显式指令优先”的例外。
- **修法（会话标志，最小改动）**：`App` 增 `_boardForceTopmost`——强制开板时置位、板隐藏时清除（`EnsureBoard` 挂 `IsVisibleChanged`）；`OnFullScreenTick` 增 `forceTopmostSession`，会话期间**挂起退让判定**（并入“low 档 / 窗口全关”那条早退分支，仍保留 `_fullScreenRetreat` 兜底复位）。
- **改动文件 1 个**：`App.xaml.cs`（字段 + `OpenBoard` 置位 + `EnsureBoard` 清除订阅 + `OnFullScreenTick` 挂起分支 + `OnHotkeyPressed`/`ForceToggleBoard` 注释口径）。**未改** `BoardWindow` / `SettingsWindow`——退让状态机本身没错，错的是“什么时候该判定”。
- **有意保留的边界**：托盘/单击开板在全屏前台**仍会被退让**（方案 B 原意：置顶窗口不压满屏内容）；只有“用户按了强制键”这一显式指令才例外，且板关闭即会话结束、不跨会话残留。
- 构建 0 警告 0 错误（`dotnet build -c Debug`）。
- **留人工验证项**：端到端复现需要真全屏前台窗口 + 真实 Alt+Tab，由用户在 Minecraft 全屏下复测——预期日志只有“强制开板：Topmost=true（…退让监视挂起至板关闭…）”，全程无“全屏前台：收纳板临时退层”。
- 文档同步：07 §8 增“强制置顶会话例外”子条、验收清单增该回归项；07 §8 仍为该行为的最高依据。

### 补充决策（续 233）

234. **显式用户指令优先于自动退让**：全屏退让监视只约束“由优先级档自动置顶”的窗口；用户经强制键（`hotkeyForce`）主动置顶的窗口在**该次开板会话内**不受退让约束，会话随板隐藏结束。判据是会话标志而非 `Topmost`（防抖动约束同 232）。

---

## 配置导入导出适配设置项生态 + 主题预览卡重绘（2026-10-05 · 批次十五）

### A. 配置导入/导出适配当前设置项生态

- **用户要求**：“让导入/导出配置功能适配现在的设置项生态”。
- **根因 ①（硬伤，实锤）**：**state 键名错位——配置的“收纳板布局”那一半从未生效**。`ConfigPort` 读写
  `state.categories` / `state.iconOrder`，而 state.json 里的键是**裸名** `categories` / `iconOrder`
  （`StateStore` 各存取方法默认键 + 00 §8 state 表均为裸名）。后果：导出时 `GetRawValue("state.categories")`
  返回 Undefined → **静默跳过**（导出文件里从来没有这两行，05 §4 的格式示例一直是空头承诺）；
  导入时写进 `state.categories` 这个**没有任何调用方读取的假键**（`BoardWindow.ApplyImportedConfig` 读的是裸键）
  → 分类/图标顺序的导入导出全程无效，只有 settings 那一半在真正工作。
- **根因 ②（易碎）**：可导入键集合写死为 `SettingsStore.Defaults.Keys`，而导出写的是
  `Snapshot()`（settings.json 全部实际键）→ 集合不对称：表外键（已删功能的遗留键 `personal.dockCustom.colors`、
  后续步骤新增的键）“**导得出去、导不回来**”，摘要恒报“忽略 N 项”；步骤 06 的主题/材质新键一落地就会踩同一个坑。
- **修法 3 处**（`core/ConfigPort.cs` 重写）：
  1. **键名映射**：`FileKey(裸键)` / `StoreKey(文件键)` 两个方向各一处，文件里保留 05 §4 的 `state.` 前缀写法，
     存储层一律裸键；`ConfigStateKeys` 扩为 `categories / iconOrder / removedItems / virtualItems`
     （收纳板“用户可带走”的全部内容；`dock.pos`/`board.size.*`/`board.pos.*`/`firstRunDone` 刻意排除——机器相关）。
  2. **键集合与导出对称**：可导入键 = §8 默认表 ∪ **当前 settings.json 实际持有的键** ∪ 上述 state 键；
     每次解析现算（导入是低频人工操作，缓存反而会在新键写入后过期）。旧文件里表外的历史键照旧可导回。
  3. **可诊断**：`ParseResult` 增加 `IgnoredKeys`；摘要弹窗多一行“被忽略的键：…（最多 6 个，超出以‘等 N 个’收尾）”
     （新增 i18n `cfg.ignoredKeys` 中英各一），日志同步打印——版本不匹配时一眼看出是哪些键被丢。
- **热应用补齐**：`BoardWindow.ApplyImportedConfig()` 增重载 `_virtualItems` / `_model.SetRemoved(removedItems)`，
  并追加一次 `RescanAsync()`（快捷方式属于**源集合**内容，必须重扫才纳入；`RescanAsync` 不复位移除名单——
  复位是“刷新”的语义，见 `RescanRebuildAsync`，导入进来的名单得以保留）。
- **实测（离屏夹具，不打扰运行中的程序）**：`build/_uitest`（临时夹具，不入交付）跑 `config` 模式——
  导出内容含 settings 全键 + 四行 `state.*`（`state.categories` 这行**首次真正出现**）；
  **往返“应用 51 项 / 忽略 0 项”**（旧实现下自己导出的文件必报忽略）；`Apply` 后 `GetIconOrder().Count==2`、
  `removedItems` 含 `c.txt`、`virtualItems` 1 项、`categories` 1 项（**全部落在裸键上**），
  且 state.json 里没有 `state.iconOrder` 这种假键；负例（未知键 / `state.dock.pos` / 非法值 / 垃圾行）
  = 忽略 4 / 应用 0 且键名可读。`== ALL PASS ==`。

### B. 主题四选预览卡重绘（预览图 + 选中效果）

- **用户要求**：“主题的四个选项有预览图，重绘预览图与选中效果”，抽象口径**“简约明了”**。
- **旧实现的三个问题**：① 四张卡各画各的图标（日 / 月 / 日月 / 四色 Win 徽标），语言不统一、也不表达“界面长什么样”；
  ② 选中效果只有描边换色，且**描边 1→2px 会让卡内内容位移 1px**（Border 子元素被边框挤进 1px）；
  ③ 卡名恒为次要色，选中与否在文字层无差别。
- **重绘**（`ui/pages/GlobalPage.cs`，纯几何、无位图素材，像素尺寸固定保 1px 描边锐利）：
  - 统一语言 = **圆角画布 + 一枚“迷你界面”缩略图**（50×32 窗口 = 1px 框 + 标题条 + 内容），先回答“界面会变什么样”：
    白皙/暗夜 = 纯亮/纯暗界面（两条内容线）；自动 / 跟随系统 = **左亮右暗硬分界**（表达“会随条件切换”），
    分别配**日月**（按时段，太阳在亮半边、月牙在暗半边）与**四格徽标**（按系统，左列深色压在亮半边、右列浅色压在暗半边）。
    分屏一律用**硬分界渐变**（0.5 处双停靠）——比两块矩形拼接少掉接缝与圆角缺口，且天然贴合圆角。
  - 暗色一侧刻意拉开“画布/窗口/标题条/内容线”四级明度（首版黑底黑窗看不清结构，出图后回调）。
  - 选中效果三重线索：**恒 2px 描边只换颜色**（零位移）+ 右上角**勾选徽标**（强调色圆底 + 对色勾，取 `OnAccent`）
    + 卡名转正文色 **SemiBold**；未选中为分隔线描边 + 次要色常规字。
- **实测（离屏出图 + 点击自检）**：`build/_uitest`（临时夹具：`Program.cs` + `run.ps1`，`run.ps1 all` 一键重跑三节自检）
  的 `render` / `click` 模式——4 种选中态各出一张 2x PNG（浅色页与深色页都确认过：选中环/勾选徽标/卡名加重清晰、
  四张卡彼此可辨、切换前后卡内内容**零位移**）；点击第 3 张 → `theme.mode=auto` 且可见徽标唯一。
  证据图留在 `build/theme_previews/`（theme_light/dark/auto/system + click_before/after，可随时重跑覆盖）。`== ALL PASS ==`。
- 构建 0 警告 0 错误（`dotnet build -c Debug`）。
- **真机自检（部分完成，如实记录）**：重建后重启真机——Alt+Tab 强制开板正常（日志新文案“（…全屏退让监视挂起至板关闭）”已生效）、
  板 1000×721 陈列 43 项正常、**本次会话日志 0 ERROR / 0 WARN**。但“在真设置窗里截主题卡”这一步没做成：
  脚本按 `w-33` 估算的齿轮点落空（两次点击都没打开设置窗），且期间全屏 Minecraft 重新拿回前台，
  合成鼠标点击可能落到游戏里 —— **立即停止鼠标驱动，改用全局热键收板还原桌面**。
  故本批次的视觉证据以**离屏夹具的 2x 出图**（渲染的就是真实 `GlobalPage` 类与真实调色板；页面自身不画底，
  夹具补了同色底以还原深/浅页观感）+ 点击自检为准；未做的真机项只有“真设置窗内的观感目视”。
  教训：**驱动鼠标前先确认前台窗口不是别人的全屏程序**；点击热区必须由截图量出，不能按公式估算。
- 文档同步：05 §3.1 主题行改为“4 选 1”并补预览卡/选中效果口径（同时标注原提示词的“透明”档未落地，
  06 若恢复需补 `ThemeResolver` 折算与预览卡）、05 §4 配置格式/范围/映射/对称集合全部重写、05 验收清单相应两条改写；
  新增 i18n 键 `cfg.ignoredKeys`（中英）。

### 补充决策（续 234）

235. **配置文件用前缀、存储层用裸键**：对外格式保持 `state.<裸键名>`（05 §4 既有口径，人读得懂），
    读写 state.json 一律裸键，映射只允许在 `ConfigPort` 内做——**任何"拿文件键名直接读写存储"的写法都属于本批次修掉的 bug 模式**。
    纳入配置的 state 键固定为“用户可带走”的四项（分类/格序/移除名单/板内快捷方式），位置尺寸与心跳标记永不入配置。
236. **导入导出的键集合必须对称且随生态自增**：可导入键集合由“默认表 ∪ 当前实际持有的键”现算，禁止再写死一份白名单——
    白名单会随功能增删而过期，表现为“自己导出的文件被自己忽略”。被忽略的键必须**点名展示**，不能只报数量。
237. **预览卡=统一语言 + 单一差异点**：四张卡共用同一构图（画布 + 迷你界面），模式差异收敛到一处（界面明暗 / 分屏 + 一枚小徽标），
    选中态用”恒粗细描边换色 + 勾选徽标 + 文字加重”三重线索而**不用改变尺寸**（任何靠改边框粗细的选中态都会让内容位移 1px）。
    暗色侧的四级明度（画布/面/标题条/内容线）是硬要求：黑底黑窗的缩略图等于没画。

---

## 步骤06 完成记录（2026-10-05 · 材质引擎 / ThemeEngine / 托盘自绘菜单）

- **范围**：步骤 06 全部任务（MaterialEngine 通道链与回退、五档 ThemeEngine token 换肤、托盘菜单自绘化、
  悬浮窗三层自定义材质来源接管、收纳板 L3 完整版与解码缓存、材质预览图）+ 用户附加三项
  （CShip.png 作程序图标、关于页文案改”可爱的 Windows 桌面收纳工具”、**当前设置落为默认配置**）。
- **未做**：07（多屏/健壮性）、08（打包）；未改 00/01/07 步骤文件（06 文件本身为本步依据，未改）。

### 交付物（新增 / 重写）

| 文件 | 说明 |
|---|---|
| `core/MaterialEngine.cs` | **新增**：通道注册链、能力探测、模拟通道抓屏（自拍预防 + 同帧共享）、BlurEffect 共享池、64×64 噪声、纹理 LRU、材质预览缩略图、材质配方（三档 + 透明档） |
| `ui/themes/ThemeEngine.cs` + `Tokens.xaml` / `Light.xaml` / `Dark.xaml` / `Transparent.xaml` | **新增**：token 键集与三套配色字典、App 级字典装配、五档模式解析、配色注册表（新增配色=加文件+注册一行）、系统偏好即时跟随钩子 |
| `ui/components/MaterialBackground.xaml(.cs)` | **重写**：模拟通道玻璃层（截屏→模糊→主题底色→噪点→液态玻璃高光/内阴影/边缘加深→发丝描边）；既有 API 语义不变，另加 `SetTransparent` 与抓屏时机控制 |
| `core/TrayController.cs` | **重写菜单链路**：去 WinForms `ContextMenuStrip`，改 `MenuRequested` + `Request(TrayAction)`（复用既有四事件，不新增动作链路）；图标改 app.ico |
| `ui/components/GlassPopup.cs` | 新增 `ShowAtScreen`（托盘菜单绝对定位=光标左上方 + 夹屏 + 置顶取焦点接 Esc）；配色改 token + 材质底 |
| `ui/BoardWindow.xaml(.cs)` | L2/L3 **z 序修正**（背景图压到材质之上）+ 其上 10% 主题色遮罩；材质/透明档接入；开合/拖动/缩放 Freeze 与落定抓屏；背景图解码缓存 |
| `ui/SettingsWindow.cs` | 板底改 `MaterialBackground`（模拟通道），材质/透明档跟随 |
| `ui/FloatingDock.xaml.cs` / `ui/components/SquarePlate.xaml.cs` | 三板材质来源改 `MaterialEngine.Fill`；噪点随材质档；补 auto/system 即时跟随订阅 |
| `ui/pages/SettingsPalette.cs` | 退化为 token 薄封装（token 未就绪回退旧字面量，保证任何时序不出透明控件） |
| `ui/pages/PersonalizePage.cs` / `GlobalPage.cs` / `AboutPage.cs` / `ui/components/IconItem.xaml.cs` | 材质预览卡改真实缩略图 / 主题卡加”透明”档 / Logo 改产品图标 / 透明档文件名加重投影 |
| `core/SettingsStore.cs` | 默认表按”当前设置”逐键落定 + 全部缺键兜底字面量同步 |
| `core/SystemBridge.cs` | 新增 `DwmSetWindowAttribute` / `SetForegroundWindow` / `GetDpiForWindow` / `ScaleAt` |
| `Cship.csproj` + `app.ico` + `resources\icons\app.ico|app.png` | 程序图标（素材 CShip.png 转制，多尺寸） |
| `build/makeicon.py` / `build/sync_defaults.py` | 图标生成 / 默认值同步脚本 |

### 实测（本机 Windows 10 19045 · 1920×1080@100% · 中端核显）

| 指标 | 结果 |
|---|---|
| 单次抓屏 + 上屏 | **24.2 / 28.0 / 28.5 / 29.0 / 29.7 / 34.9 ms**（窗口 1010×475，全部 ≤40ms 达标） |
| 拖动期间抓屏 | **0 次**（实测：拖动前累计 2 → 拖动后 3，仅”落定后”那一次；拖动全程 40 步鼠标移动无抓屏） |
| 开合动画期间抓屏 | 0 次（动画期 Freeze，动画结束回调里抓一次） |
| 同时存在的 BlurEffect | **1 档**（按材质半径登记冻结共享实例；与 02 步缩放期模糊合计 ≤2） |
| 纹理离屏缓存 | 上限 12 张 / 64MB + LRU；实测材质缩略图 3 张（键含材质/主题/透明/尺寸） |
| 常驻内存（WS） | 启动含预热 115.3MB → 空闲 65s（含 60s 工作集整理）**11.9MB**；开板瞬时峰值见启动段 |
| 空闲 CPU | **0.12% 单核**（65s 采样 0.08s CPU 时间；含 1s 全屏退让轮询与分钟级主题检查的低档早退） |
| 原生增强判定 | `CurrentBuild=19045 < 22000` 提前跳过；且本项目玻璃窗一律为分层窗口（AllowsTransparency 为圆角/三层悬浮窗所需），与 DWM backdrop 互斥 → **全窗口走模拟通道**，日志留痕（”不适用”而非”失败”，无黑底） |

### 补充决策（续 237）

238. **模拟通道抓屏的自拍预防选”Opacity=0 + Render 优先级让渲染”**（步骤文件 §1.3 给的二选一）：
     不动窗口位置/z 序，最不易被察觉；抓完在同一 `finally` 里恢复。**教训：`window.Opacity = 0` 之后
     到恢复之间不得有会抛异常的语句**——`Dispatcher.Invoke(..., Render)` 也必须落在 try 内，
     否则异常一旦逃逸，窗口会永久停在”全透明但可见”的僵死态。
239. **材质档在三处的口径分工**：`MaterialEngine.Fill`（底画刷，含个人不透明度与透明档）供
     悬浮窗三板/各级菜单/设置窗；`MaterialBackground`（含实时截屏）只给收纳板与设置窗；
     **小窗（菜单/三板）不抓屏**，用静态材质底（§1.5 的”小窗用静态缓存纹理”）。
240. **背景图（L3）必须压在材质（L2）之上**：原 XAML 里 CustomBgHost 在 MaterialBackground 之前
     （z 序更低），而板底近不透明 → 自定义背景图实际不可见（用户当前 `boardBg=null` 故长期未被发现）。
     本步改为”材质在下、图片居中”，并在图片之上叠 10% 主题色遮罩（§5），`BgShade` 与图片同几何内缩。
241. **托盘菜单用”光标位置”而非托盘图标矩形**：NotifyIcon 不暴露 hWnd/uID，`Shell_NotifyIconGetRect`
     无从调用；右键本就发生在图标上，光标即图标所在处（§3 允许的兜底）。定位=光标左上方 + 夹回该屏，
     偏移按该点所在屏 DPI 折算（Absolute 定位以虚拟屏左上角为原点）。
242. **托盘菜单保留”显示”项**（仅悬浮窗处于右键隐藏态时列出）：步骤文件写”四项”，但清单二·任务3 的
     “隐藏后从托盘复现”能力若一并砍掉就是功能回退；故五项（四项常驻 + 条件性”显示”）并把动作
     统一收口到 `TrayController.Request(TrayAction)`（既有四事件，不新增链路，也消除 CS0067 警告）。
243. **I18n 静态订阅改”按需构建即热切换”**：托盘菜单改为每次弹出按当前语言构建后，原
     `I18n.LanguageChanged += RefreshTexts` 的静态订阅已无意义 → 直接删除（等价迁移，不留未退订订阅）。
244. **透明档=暗底浅字 + 表面近全透**：`ThemeResolver.IsDark()` 对 transparent 返回 true（全部既有配色路径
     自动切浅色文字/图标），板底 α 由 `MaterialEngine` 的透明档给到 ≈8%（0x14），装饰减到最轻；
     **设置窗面板保一定不透明度**（否则杂色桌面上设置项不可读）——“α≈8%”只落在”背景板”即收纳板。
245. **设置窗也走材质引擎**（模拟通道实时截屏）：板底改由 `MaterialBackground` 绘制，窗口原有的
     `_plate` 底色/描边置空避免二次上色；材质/透明档随设置与主题即时切换。
246. **主题”透明”档恢复**（原文 L28；批次十五记录其未落地）：主题卡从 4 张扩到 5 张，透明卡用
     **棋盘格画布 + 发丝描边迷你窗**表达”透出下层”；`ThemeResolver`/`ThemeEngine` 五档解析。
247. **”当前设置落为默认配置”（用户附加需求）口径**：`SettingsStore.DefaultRaw` 逐键按当时 settings.json 取值
     重写（theme.mode=light / autostart=true / fps=120 / dockSize=58.7 / iconSize=67.1 / rowSpacing=0 /
     colSpacing=10.9 / labelLines=two / hoverLabels=true / iconMask=true / dockEdgeRange≈40.24 /
     dockPriority=low / dockFollowTheme=false / boardAnim=slideEdge / boardOpacity=91 /
     dockCustom.images=[“”,””,””]），并用 `build/sync_defaults.py` 把散落在各处的**缺键兜底字面量**
     一并同步（消除”默认表一套、代码兜底一套”的双口径）。删除 settings.json 或全新机器首启即得到当前外观。
248. **图标四处同源**：素材 `CShip.png` → `app.ico`（16/24/32/48/64/128/256，非方形源图补透明边成正方形）→
     `ApplicationIcon`（exe 文件图标 / 任务栏 / 任务管理器）与 `resources\icons\app.ico`（托盘 NotifyIcon），
     另出 `app.png`（512）供设置-关于页 Logo；托盘加载失败时依次回退”exe 关联图标 → 自绘三层方板”。

### 给后续步骤的接口提示

- **07**：`MaterialEngine.Freeze/Unfreeze(Window)` 是”交互期零抓屏”的唯一闸门——07 若新增拖动/缩放类交互
  （如多屏拖板、DPI 变更重排）需在同一处配对调用，否则会出现”拖动中反复抓屏”。
  `MaterialEngine.CaptureCount` / `Textures.Stats` 可直接读来做性能回归断言。
- **07**：全屏退让（§8-B）与材质无耦合；但若 07 引入”窗口重建”（如换屏重建板），重建后必须重新
  `Material.RefreshSnapshot()`（新 HWND 无上一帧）。
- **08**：`app.ico` 已入 `src\Cship\`（`ApplicationIcon` 源）与 `dependencies\resources\icons\`（托盘用），
  两处都需随发布目录带走；`build/makeicon.py` 可在换图时一键重生成。
- **08**：主题字典是 `Page`（编译为 BAML）+ pack URI 加载，单文件 self-contained 发布下不需要额外处理；
  若将来把 themes 移出程序集（真外置），改 `ThemeEngine.Load` 一处即可。
- **任意后续**：新增配色 = 加一个 `xxx.xaml`（同名键）+ 在 `ThemeEngine.PaletteFile` 注册一行；
  新增材质档 = 在 `MaterialEngine` 的 `Fill/BlurRadius/NoiseOpacity/HasGlassDepth` 各加一支，不动调用方。

### 留人工验证项（自动化未覆盖，如实记录）

1. **托盘图标右键**：本机 Cship 托盘图标在”隐藏图标”浮层内，合成点击无法打开该浮层（Win10 溢出面板
   不响应 SendInput 的 ^ 点击），故菜单本体改用临时入口 `--traymenu-test`（跑完已删）验证：
   自绘玻璃菜单四项正确弹出、位置贴光标左上方不出屏、透明档下为半透明玻璃底 + 浅色文字。
   **”右键托盘图标 → 菜单弹出 → 失焦/Esc 关闭”这一真实链路请羽实机点一次确认。**
2. **三档材质在设置页的预览卡观感**（真实缩略图）与**透明档下悬停文件名的可读性**：需目视，留用户验收。
3. **系统主题即时跟随**（`system` 模式下切 Windows 深色）：代码走 `UserPreferenceChanged → InvalidateSystemTheme
   → ThemeAuto.EvaluateOnUi`，本机未实机切换系统主题验证（会改变用户桌面设置，未擅自执行）。

---

## 材质取景修订：改尺寸不再拉伸/不再闪烁 · 设置窗材质 1:1（2026-10-06 · 步骤 06 后的用户实机反馈）

- **用户报障**（三句，同一病根）：① 收纳板**调整尺寸时背景被"固定成调整前那张"并被拉伸缩放**；
  ② **调整完毕后闪一下再渲染**；③ 设置窗"**一打开就固定了背景**"，都不是实时渲染材质效果。
- **取证**：DevTools 探针（`build/probe_resize.py` / `probe_resize2.py`）——旧版松手后连拍 64 帧中出现
  **连续 2 帧收纳板完全透明**（整窗连图标一起消失，纯壁纸），即"闪一下"；日志同一时刻两行
  `材质抓屏`（12.452 / 12.552，各 ~32ms）。

### 根因（三条，代码与实机证据一致）

| # | 根因 | 表现 |
|---|---|---|
| 1 | **取景按"窗口矩形"裁剪**：`CaptureBehind(window)` 取 `window.PointToScreen(0,0)` + `ActualWidth/Height`，而材质层在板里被 Root 的锚边内边距、在设置窗里被板 `Margin(24,24,24,30)` 内缩，同一张图被 `Stretch="Fill"` 塞进更小的元素 | 系统性错位 + 等比缩放（设置窗最重：内容偏 24px、放大约 1.08 倍，"背景像是贴上去的假图"） |
| 2 | **冻结期沿用旧位图 + `Stretch=Fill`**：拖动/缩放期间 `MaterialEngine.Freeze` 挡住抓屏，旧位图被硬拉成新尺寸 | "调整时对背景进行拉伸缩放"（实测 1010→760 时横向压到 0.75） |
| 3 | **抓屏时机在窗口可见期**：自拍预防把本窗 `Opacity=0` 再 `Dispatcher.Invoke(Render)` 让渲染让出，那一帧**真的会合成上屏**（抓屏 ~30ms ≈ 2 帧） | "调整完毕后闪一下再渲染"；设置窗是 Show 后 180ms 去抖抓一次且此后永不刷新 → "打开就固定了背景" |

### 新口径（一句话）

**窗口不可见时抓一次整屏存为"后方帧"，此后所有尺寸/位置变化都在这张帧上按元素的"实际屏幕矩形"1:1 重裁**
——零抓屏、零闪烁，画面与真实桌面逐像素对齐（"玻璃贴住桌面"而不是"贴住窗口"）。

### 改动文件

| 文件 | 改动 |
|---|---|
| `core/MaterialEngine.cs` | `CaptureBehind(window)` → **`CaptureScreen()`**（抓整屏并返回，自拍预防改由调用方以"不可见时机"保证）+ `CaptureScreenWithSelfHidden(window)`（兜底，会隐帧）+ **`TryCrop(frame, 屏幕物理矩形)`**（1:1 裁剪，返回越界偏移）；`Freeze/Unfreeze/IsFrozen` 保留 |
| `ui/components/MaterialBackground.xaml.cs` | 新增 **`CaptureFrame()`**（宿主在 Show 之前调用）/ **`SyncToFrame()`**（按自身屏幕矩形 1:1 重裁，零抓屏）/ `DropFrame()`；`SizeChanged` 与宿主 `LocationChanged` 均实时重裁；`RefreshSnapshot()` 保留旧语义（抓新帧+重裁，可见时兜底会隐帧一次） |
| `ui/BoardWindow.xaml.cs` | `OpenFor` 在 `Show()` **之前** `Material.CaptureFrame()`；`OnOpenAnimDone` 与 `SaveSizeNow` 的 `RefreshSnapshot()` → **`SyncToFrame()`**（去闪烁） |
| `ui/SettingsWindow.cs` | `Present` 首次 Show 之前 `_material.CaptureFrame()`（替换"Show 后 180ms 去抖抓屏"） |

### 实测（本机 1920×1080@100%、`personal.material=blur`、`personal.boardOpacity=0`＝板底全透，材质即整块画面）

| 项 | 结果 |
|---|---|
| 抓屏次数 | **开板 1 次 / 设置窗 1 次**；改尺寸、移动、开合动画期间 **0 次**（日志仅一行 `材质后方帧：1920×1080 耗时 30~31ms`） |
| 闪烁 | 新版松手后连拍 49 帧**无任何透明帧**（帧间最大差异仅来自 180ms 缩放模糊收尾）；设置窗开窗连拍 32 帧"背景可见占比"100%→15.9% **单调下降无回跳** |
| 拉伸 | 同几何"拖拽中 vs 落定后"画面差异：旧 **均值 38.05**（横向压缩）→ 新 **8.32**（只剩缩放模糊） |
| 对齐（决定性） | 临时把 `Blur` 半径置 0 做逐像素判定（`build/probe_pixelalign.py`）：图标间纯材质竖缝在 **偏移 (0,0)±1 处近等率 88%**，错位即崩到基线；差异图上材质区全黑（=与开板前桌面逐像素相同），只有图标本身是亮的 |
| 设置窗对齐 | 板顶横带水平互相关：旧版最优偏移**跑到搜索边界 dx≥45**（错位+缩放），新版**落在 dx=0** |
| 构建 | `dotnet build -c Debug` **0 警告 0 错误**；本轮会话日志 **0 ERROR / 0 WARN** |

### 补充决策（续 248）

249. **抓屏时机铁律=本进程玻璃窗"不可见"时**：可见期抓屏只能靠 `Opacity=0` 隐帧让渲染让出，
    而那一帧确实会上屏（抓屏 ~30ms ≈ 2 帧）→ 必现闪烁。故收纳板在 `Show()` **之前**抓帧、
    设置窗在首次 `Show()` 之前抓帧；`CaptureFrame()` 内对"本窗已可见"直接拒抓并记 WARN，防止后人误用。
250. **取景几何按"元素"算，不按"窗口"算**：材质层在板里受 Root 锚边内边距、在设置窗里受板 Margin 内缩，
    用窗口矩形取景必然错位 + 缩放。判据：**凡"绘制层比宿主窗口小一圈"的自绘背景，取景矩形一律取该元素自身的屏幕矩形**。
251. **后方帧按窗口各存一份**（`MaterialBackground._frame`，不是全局静态）：设置窗弹出时收纳板正可见，
    它抓到的帧天然含收纳板——若两窗共用一帧，收纳板几何变化时会照出自己（递归玻璃）。
    代价是每窗一份 1920×1080 位图（≈8MB），实测两窗同开 WS 96.7MB，仍在预算内。
252. **几何变化一律"重裁留存帧"，不重抓**：帧是全屏的，窗口怎么变都能从同一帧里裁出正确的那块，
    所以拖动/缩放/移动全程零抓屏也不会拉伸；`CroppedBitmap` 的构造是零拷贝包装，逐帧重建代价可忽略
    （用 `_lastCrop` 矩形比对挡掉"几何没变"的重复重建）。
253. **`Freeze/Unfreeze` 语义收窄为"防抓屏"**：它不再承担"防拉伸"职责（防拉伸已由 252 的几何重裁解决），
    07 步新增交互照旧配对调用即可，漏调也不会再出现拉伸，只可能多一次抓屏。
254. **材质"实时"的边界＝几何实时、内容为开窗时那一帧**：桌面在玻璃后面是静止的（板在最上层），
    故"内容冻结 + 几何逐像素跟随"与真实实时取景观感等价；换来的是零抓屏开销与零闪烁。
    若将来要跟动态壁纸（本机 Wallpaper Engine 实测为静态帧），再在 `SyncToFrame` 之外挂低频
    `CaptureFrame`（不可见时机不成立，需先评估隐帧代价）。

### 给后续步骤的接口提示

- **07**：`Material.CaptureFrame()` 只能在窗口不可见时调；若新增"换屏重建板"这类需要新画面的路径，
  请先 `Material.DropFrame()`，再趁不可见时机 `CaptureFrame()`。`RefreshSnapshot()` 保留旧语义
  （"抓一次并铺上"，可见时会隐帧一次）仅作兜底，不要在新交互路径里用。
- **07**：多屏/混合 DPI 下 `TryCrop` 用 `SystemParameters.VirtualScreenLeft/Top` 与 `PointToScreen` 同坐标系，
  口径未变（本轮未动），换屏后建议实测一次越界偏移。
- **08**：`MaterialEngine.CaptureCount` 仍可直接读来做性能回归断言；本轮的验收口径是
  "**开板 1 次、交互期 0 次**"，比步骤 06 的"拖动期 0 次"更严。

### 留人工验证项（自动化未覆盖，如实记录）

1. **视觉观感**：缩放过程中材质是否"贴住桌面"（不再拉伸）、设置窗打开/拖动观感、三档材质下的观感——交羽目视验收。
2. **拖动窗口移动材质跟随**：合成鼠标驱动的 `DragMove()`（设置窗标题栏）与收纳板标题带拖动在本机均未驱动成功
   （`cuahelper.click/move` 走的 `mouse_event` 进不了 WPF 的 DragMove 模态循环），故 `LocationChanged → SyncToFrame`
   这条通路只有代码与"尺寸通路同源"的推断，未做机器实测——请羽拖一次看背景是否随窗口"钉在桌面上"。
3. 本轮测试用截图/脚本留在 `build/`：`probe_resize*.py` / `probe_align.py` / `probe_pixelalign.py` /
   `probe_setalign.py` 与证据图 `matfix_r1_burst_sheet.png`（旧版闪烁）/`matfix_r2_burst_sheet.png`（新版无闪烁）/
   `matfix_al_diff.png`（对齐差异图）/`matfix_set_cmp.png`；过程截图已清理，`state.json` 的
   `board.size.down` 已还原为探测前的 `[1000,470]`。

---

## 材质取景第三次修订 + 主题"透明"档删除 + 高级页"恢复默认"（2026-10-06 · 步骤 06 后的用户实机反馈）

- **范围**：用户三句报障（① 去掉全局设置里的"透明"主题及相关代码；② 高级页配置区上方加"恢复默认"按钮，恢复全部设置项但**不动分类项**；
  ③ 收纳板开板时"记住"背景图，移动窗口只对记住的背景实时渲染，下层窗口视图一变就与真实背景不一致，设置窗同样，且移动中背景闪烁、卡顿）
  + 用户追加两条实测（④ 模糊材质失效；⑤ 收纳板打开后鼠标每秒卡顿一次）。未做 07/08。

### 一、根因（全部实测定位，不是推断）

| # | 现象 | 根因 | 证据 |
|---|---|---|---|
| 1 | 模糊材质失效（材质层什么都不画） | 整屏帧（1920×1080）直接挂在 `Grid` 下时，WPF 会给"超出父级可用尺寸"的子元素一个**布局裁剪**（按子元素自身坐标系裁到父级大小 840×528）；再叠上取景用的负向 `RenderTransform(-931,-233)`，整块内容被裁掉又移出可视区 → 材质与模糊一起消失 | `build\_captest` 五组对照 + 板内 10 组同构对照（小图/大图/大图+位移/小图+effect/大图+位移+effect/条纹同构…）；诊断日志显示元素尺寸、裁剪矩形、偏移、可见性、effect 全对，却"什么都没画" |
| 2 | （同因第二处）`Snapshot.Width/Height` 从未被赋值 | 初值 NaN（Auto），而 `Math.Abs(NaN - w) > 0.01` **恒为 false** → 尺寸永远停在 Auto | 诊断日志 `图 NaN×NaN` |
| 3 | 鼠标每秒卡顿 | 可见期每 1.2s 一次整屏 GDI 抓屏会阻塞 DWM 合成 | 空闲 8s 内抓屏十余次、CPU 312ms；改变更驱动后空闲 10s 仅 47ms |
| 4 | 刷新偶尔永久停摆（此后桌面再变也不刷新） | 后台抓屏完成后的 `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)` 在 UI 忙时被饿死（实测丢过一次）→ `_refreshing` 永远为真 | 日志 `材质刷新跳过：上一轮刷新未回收（累计 1 次）` 之后不再有任何刷新 |
| 5 | "只对记住的背景实时渲染" | 帧只在开板前抓一次，此后永不更新（旧口径决策 254 的边界） | 代码即证 |
| 6 | 移动时闪烁/卡顿 | 每移动一步都重建 `CroppedBitmap` + 换 `Image.Source`（重传纹理）+ 重算模糊 | 旧实现代码路径 |

**另一条硬结论（能力探测，供后续复用）**：`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 在本机（Win10 19045）对**非分层窗**有效
（实测"与隐藏本窗的地面真值差异 = 0.0"，且"排除→立即抓→恢复"零延迟、无副作用），但对本项目窗型 **`AllowsTransparency` 分层窗直接返回 FALSE（`GetLastError=8`）**。
即"让系统把自己从抓屏里摘掉"这条路对本项目走不通，只能用"自身矩形空洞回填"。夹具留在 `build\_captest`（探针一/二/三）。

### 二、改法（新口径）

1. **帧 = 整屏一张（每窗一份）+ 自身矩形空洞**：整屏抓取时跳过本进程全部可见玻璃窗的矩形，这些矩形保留帧内旧内容
   （= 该处"最后一次可观测"的画面，物理上不可观测的区域本来就无从获取）。由此**抓屏不必隐藏自身** → 零隐帧、零闪烁，
   也不再受"必须在本窗不可见时抓"的时机约束。
2. **取景 = 渲染变换**：本层只是整屏帧上的一扇取景窗，移动/缩放**只改一个 `TranslateTransform`**（不触发布局、不重建位图、不重算模糊）。
   **整屏帧必须住在 `Canvas` 里**（见根因 1）；`Snapshot.Width/Height` 用 `SetIfChanged` 兜住 NaN（见根因 2）。
3. **刷新 = 桌面变更驱动**：700ms 算一次"可见顶层窗口 (hwnd, 矩形) 集合哈希"（**与 z 序无关**——把 z 序算进去会因前台激活噪声退化成"每 tick 都抓"，
   实测空闲 8s 抓了十几次），指纹变了才抓一次；`Freeze/Unfreeze` 语义收窄为"交互期不刷新"，**窗口拖动不再冻结**
   （拖动期正需要"刚被盖住前那次观测"保持新鲜）。
4. **主题"透明"档整体删除**：字典 `Transparent.xaml`、`ThemeEngine.IsTransparent` 与调色板注册、`ThemeResolver.IsTransparent`、
   `MaterialEngine` 全链路 transparent 参数（`Fill/NoiseOpacity/HasGlassDepth/Thumbnail/Apply/IChannel`）、`MaterialBackground.SetTransparent`、
   `SquarePlate` 双参 `SetTheme`、`FloatingDock`/`GlassPopup`/`IconItem`/`PersonalizePage`/`BoardWindow`/`SettingsWindow` 各调用点、
   第五张预览卡与棋盘格画布、i18n 键 `set.theme.transparent`（中英）；历史值 `transparent` 在 `SettingsStore` 构造时一次性迁移为 `dark`。
5. **"恢复默认"按钮**：高级页**配置卡上方**新增"恢复默认设置"卡（危险色按钮 + 二次确认）；
   `SettingsStore.ResetToDefaults()` 只写 settings.json 默认表的键；跨窗口/系统副作用（开机自启动、隐藏桌面图标、语言）
   **收口到 `App` 的 `SettingsChanged` 监听**（顺带修掉"导入配置改到这三项时系统侧根本不动"的老问题）；
   `AdvancedPage.DefaultsRestored` 让设置窗回读当前页控件值。

### 三、实测（本机 1920×1080@100%，`personal.material=acrylic`）

| 项 | 结果 |
|---|---|
| 玻璃模糊 | 条纹信号窗下"玻璃边界梯度 / 原始边界梯度" = **0.04**（修前 ≈1.0 = 材质层什么都没画）；板内图标锐利、背景被抹平（证据图 `build5_board.png`） |
| 取景对齐 | 中位剖面互相关最佳偏移：**静止 -1px、拖动中 0px**（`build\probe_align2.py`） |
| 拖动期抓屏 | **0 次**（40 步 ×10px 全程） |
| 拖动每步净 CPU | **2.0ms**（只剩 WPF 重绘分层窗；无位图/纹理/模糊工作） |
| 空闲抓屏 | **0 次**；开板空闲 10s CPU **47ms**（≈0.15% 单核，含 1s 全屏退让轮询与 700ms 指纹） |
| 变更驱动 | 记事本出现 → 1 次抓屏（~20ms）；关闭 → 1 次 |
| 恢复默认（端到端） | 点按钮 → 二次确认 → `personal.boardOpacity 0→91`、`advanced.lockIconDrag true→false`（回默认值）；`state.json` 逐字节未变 |
| 恢复默认（语义夹具 `build\_mattest` reset） | 4 项写回默认；`state.categories / iconOrder / board.pos` 未动；表外残留键 `personal.dockCustom.colors` 保留 |
| 主题迁移（`_mattest` migrate） | 历史 `theme.mode=transparent` → `dark`，其余键保留 |
| 构建 | `dotnet build -c Debug` **0 警告 0 错误**；本轮会话日志 0 ERROR / 0 WARN |

### 补充决策（续 254）

255. **"自拍预防"只能靠自身矩形空洞，不能靠系统 API**：`WDA_EXCLUDEFROMCAPTURE` 对 `AllowsTransparency` 分层窗无效（实测 FALSE/err=8），
     而本项目的圆角/三层悬浮窗必须分层、改不得。空洞语义 = "跳过本进程全部可见玻璃窗矩形、保留帧内旧内容"，
     即"最后一次可观测"，**不重抓、不递归**（洞区永不从新抓的画面里回读，故不可能出现玻璃照玻璃的反馈环）。
256. **整屏帧必须住在 `Canvas` 里**：WPF 对"超出父级可用尺寸"的子元素施加**布局裁剪**（子元素自身坐标系、裁到父级大小），
     再叠负向取景平移会把内容整体移出可视区 → 表现为"材质与模糊一起消失"。`Canvas` 测量时可用尺寸为无穷、不产生布局裁剪。
     最小复现：`Grid(80×40) > Image(1920×1080, Stretch=None, Translate(-500,-500))` → 什么都不画；宿主换 `Canvas` 即正常。
257. **取景用 `RenderTransform`，不用"每步重裁"**：帧是整屏的，窗口怎么动都能从同一张帧里取景，
     移动一步只改一个变换（不触发布局、不重建位图、不重算模糊）。旧实现每步 `CroppedBitmap` + 换 `Source` + 重算模糊 = 用户反馈的"闪烁 + 卡顿"。
     **显式尺寸不能省**：`Width/Height` 默认 NaN，`Math.Abs(NaN-x) > eps` 恒 false，必须用 `double.IsNaN` 兜住（否则元素停在 Auto 尺寸）。
258. **抓屏只由"桌面窗口视图变化"驱动**：指纹 = 可见顶层窗口的 (hwnd, 矩形) **集合**哈希，**与 z 序无关**。
     周期抓屏会阻塞 DWM 合成（用户实机感受="鼠标每秒卡一下"），故 700ms 只花 ~0.3ms 算指纹，变了才抓。
     指纹不含"窗口内部纯内容变化"（视频/滚动）：那类变化玻璃保持"最后一次可观测"语义（重模糊下不可辨，真全屏另有 §8-B 退让）。
259. **`Freeze/Unfreeze` 语义收窄为"交互期不刷新"**：它不再承担"防拉伸/防抓屏"的兜底职责（防拉伸已由 257 的变换取景解决），
     07 若新增交互照旧配对调用即可；窗口拖动路径**不冻结**（否则"刚被盖住那块"会一直停在旧观测上）。
260. **"恢复默认"只动 settings.json**：默认表逐键写回；state.json（分类/格序/移除名单/板内快捷方式）与窗口位置尺寸一律不碰
     （用户明确要求"不会动分类项"；位置尺寸是"这台机器的当前状态"，属托盘"复位"职责）；表外历史残留键保留原值（不替用户丢数据）。
261. **跨窗口/系统副作用收口在 `App` 的 `SettingsChanged`**：`general.autostart`（注册表）、`general.hideDesktopIcons`（桌面图标）、
     `general.language`（I18n 热切换）三项原先只在设置页控件自己的事件里生效，"导入配置 / 恢复默认"改到它们时系统侧不动 —— 现在任何写入口同样生效。
     副作用在 UI 线程执行（`RunOnUi`），语言项与当前一致时不重载词典。

### 给后续步骤的接口提示

- **07**：`MaterialEngine.CaptureFrame()` 仍建议在窗口不可见时调（最干净），但**已允许可见期调用**（本窗矩形会被跳过）。
     多屏/换屏：`FrameMatchesScreen` 会在尺寸不符时自动重建帧；`TryCrop` 已删除，取景偏移用 `VirtualScreenOrigin()` 与 `PointToScreen` 同坐标系。
     `MaterialEngine.CaptureCount` 仍可做性能回归断言；本步的验收口径是"**几何变化期 0 次 + 空闲期 0 次（仅桌面窗口视图变化各 1 次）**"。
- **08**：材质不再有透明档相关的配置/资源；`Transparent.xaml` 已删，发布目录无需带走。
- **任意后续**：新增主题档 = 加字典 + `ThemeEngine.PaletteFile` 注册一行 + `GlobalPage.ThemeModes` 加一项（不要再引入 `MaterialEngine` 的参数链）。

### 留人工验证项（自动化未覆盖，如实记录）

1. **视觉观感**：三档材质在收纳板/设置窗/各级菜单下的一致性；移动窗口时玻璃是否"贴住桌面"（不再闪烁/错位）；设置窗拖动时的观感。
2. **全局设置页主题卡**：从 5 张回到 4 张后的排版观感（等分星号列已按 4 张重排），以及"透明"档消失后无空位。
3. **高级页"恢复默认设置"卡**：危险色按钮 + 说明文字的观感与位置（配置卡之上）。
4. 本轮测试用夹具/证据留在 `build/`：`_captest`（抓屏排除能力探针）、`_solidwin`（可控信号窗）、`_mattest`（真实控件模糊/恢复默认/迁移自检）、
   `probe_blur.py` / `probe_align2.py` / `probe_drag.py` / `probe_mat.py` 与证据图 `b5_board.png` / `adv_page.png` 等（均不入交付）。

---

## 材质改"图层堆叠"（系统合成器模糊）+ 设置窗注释清理 + 收纳板材质"无"档（2026-10-06 · 步骤 06 后的用户实机反馈）

- **范围**：用户四项要求（① 去掉设置窗各设置项下的说明性注释、去掉关于页"关于 Cship"；② 材质系统从"抓屏→计算→贴图"
  改为"图层堆叠"式的低开销零延迟方案，做不到的删掉、做得到的保留；③ 收纳板材质新增"无"档＝不启用任何材质；
  ④ 导入导出配置适配本轮改动）。**未做 07/08**（用户明确"步骤 07 还不执行"）。

### 一、能力探测（先定论再动手；夹具 `build/_nattest`，数据即结论）

判据：条纹信号窗（周期 40px 洋红/黑）上叠一枚**与本项目同款**的分层玻璃窗（`AllowsTransparency=true`，
内含两块 `#33FFFFFF` 半透明"板"与中间一条全透明缝），逐状态截图量横向最大梯度。
参考上限：未模糊的条纹相邻像素色差 = **255**。

| 状态 | 板内梯度 | 缝隙梯度 | 结论 |
|---|---|---|---|
| 无 accent（基线） | 136.0 | 170.0 | 条纹原样透出 |
| `ACCENT_ENABLE_BLURBEHIND`(3) | **8.7** | 10.7 | **系统模糊生效**（板内、缝隙都被糊） |
| `ACCENT_ENABLE_ACRYLICBLURBEHIND`(4) | 4.7 | 4.7（均值 85→**192**） | 也生效，但带强染色（DWM 的 GradientColor 洗白整窗） |
| 回到 `ACCENT_DISABLED`(0) | 136.0 | 170.0 | **完全可逆、无残留** |
| BLURBEHIND + `SetWindowRgn`(内缩 40px 矩形) | 区域内 8.7 | **区域外 10.7** | **窗口区域裁不住系统模糊** |

四条硬结论：① 该 API 对**本项目窗型（分层窗）有效**，且可随时清除；② 模糊**覆盖整个窗口矩形、不尊重逐像素 alpha**
（全透明处照样被糊）；③ `SetWindowRgn` 裁不住它；④ `AccentFlags` 取 0 或 2 结果相同（用 2）。
于是**适用判据 = "整个窗口矩形都该是玻璃"**——只有收纳板满足。

### 二、改法（新口径）

**材质 = 系统合成器在窗口背后画的模糊（DWM）+ 本进程自绘的静态图层**（板底色 / 噪点 / 液态玻璃高光内阴影 / 发丝描边）。
全进程不再有任何抓屏、位图或模糊着色器。

1. **删掉整条抓屏链路**（`MaterialEngine` 与 `MaterialBackground` 各删约 400 行）：通道注册链与能力探测
   （`IChannel`/`Chain`/`Register`/`Init`/`Apply`/`LastChannel`/`NativeBackdropChannel`/`SimulatedChannel`，
   其中 native-backdrop 因"与分层窗互斥"从未生效过）、整屏帧与空洞回填（`Frame`/`CreateFrame`/`GrabIntoFrame`/
   `GrabScreenBytes`/`ReleaseScreenBytes`/`ApplyToFrame`/`ClipHoles`）、桌面指纹与变更检测
   （`DesktopFingerprint`/`FingerprintProc`）、冻结闸门（`Freeze`/`Unfreeze`/`IsFrozen`）、玻璃窗登记
   （`RegisterGlassWindow`/`UnregisterGlassWindow`/`VisibleGlassRects`）、`BlurFor`/`BlurPool`/`BlurRadius`、
   `CaptureCount`/`LastGrabMs`/`VirtualScreenOrigin`；`MaterialBackground` 侧删 `Snapshot`/`FrameHost`/
   `CaptureFrame`/`SyncToFrame`/`DropFrame`/`RefreshFrame`/`RefreshSnapshot`/`_offset`/`_loop`/看门狗；
   `SystemBridge` 删随之无用的 `EnumWindows`/`EnumWindowsProc`/`DwmSetWindowAttribute`。
   `App` 的 `MaterialEngine.Init()` 调用一并删除。
2. **新增系统模糊开关**：`MaterialEngine.SetNativeBlur(Window, bool, force)`（+ `ClearNativeBlur` 收尾），
   `SystemBridge` 补 `SetWindowCompositionAttribute` 与 `ACCENT_POLICY` 结构体。失败只记 WARN 并退回静态层，**绝不黑底**。
3. **`MaterialBackground.UseNativeBlur`（默认 false）是唯一开关**：收纳板置真（它的材质层是 `Root` 的直接子元素、
   正好铺满窗口矩形）；**设置窗保持 false**——它的板比窗口小一圈（`PlateMargin` 留给投影），开了会把模糊漫到板外。
4. **"无"档**（`MaterialEngine.None`）：不铺底色、不加噪点、不做装饰、**去掉发丝描边**、**不开系统模糊**；
   预览缩略图画棋盘格（＝透明）。设置窗把"无"**折算为亚克力**（一整页文字控件没有板底就没有可读性，同决策 244）。

### 三、实测（本机 Win10 19045 · 1920×1080@100%）

| 项 | 结果 |
|---|---|
| 构建 | `dotnet build -c Debug` **0 警告 0 错误** |
| 启动 + 开板（`personal.material=acrylic`） | 日志 `材质系统模糊：已启用（DWM 实时合成，本进程零抓屏零位图；窗口 0x2044A）`；**0 ERROR / 0 WARN** |
| 抓屏类日志 | `材质实时刷新` / `材质后方帧` / `材质变更检测` / `材质整屏抓取` / `材质刷新跳过` / `BlurEffect` **全部 0 条**（旧口径下开板必有一次 20~35ms 抓屏） |
| 启动 + 开板（`personal.material=none`） | **没有**"材质系统模糊"行（按预期未开模糊）；**0 ERROR / 0 WARN** |
| 几何/动画期开销 | 0（窗口移动、缩放、开合动画对材质**无事可做**——模糊由 DWM 跟随窗口实时合成） |

### 补充决策（续 261）

262. **系统模糊的适用判据 = "整个窗口矩形都该是玻璃"**：模糊无视逐像素 alpha 且裁不住（实测），
     故收纳板（材质层铺满窗口）可用；悬浮窗三板（板间有空隙）、设置窗（板比窗口小一圈）、各级菜单一律用纯静态层。
     这不是妥协——板底本身 90%+ 不透明（亚克力 0xFC/0xEF × `boardOpacity`），背后画面只贡献几个百分点，
     静态层与"模糊层"的观感差本就很小。
263. **"无"档语义 = 一个像素都不画**：底色、噪点、装饰、发丝描边、系统模糊全部关掉，收纳板只剩图标与自定义背景图（若已设）。
     设置窗例外（折算亚克力），理由同决策 244：那是可读性问题，不是观感问题。
264. **设置项注释的清理规则**（用户"等"字的落点）：`set.hideIcons.hint` / `set.catShow.hint` / `set.hotkey.hint` /
     `set.reset.hint` / `set.material.hint` / `set.dockCustom.wip` 六条说明性 `Caption` 全去，
     **错误态提示保留**（`set.hotkey.dup` 冲突提示）；自定义背景图下原"未设置（使用默认材质背景）"状态行也去掉
     （状态改由缩略图表达）；关于页去掉"关于 Cship"标题与"预留扩展区"占位卡。
     i18n 中随之无引用的 10 个键（含 `set.boardBg.none` / `set.about` / `set.about.reserve*`）中英双语一并删除。
     `SettingsPageBase` 补无标题的 `Section()` 重载（卡片底与内边距保留）。
265. **配置导入加"枚举值白名单"**（`ConfigPort.EnumValues`）：材质（含新档 `none`）、悬浮窗自定义模式、主题
     （已删"透明"档）四键，值不在白名单即按"非法值"跳过并计数——比热应用一个谁都不认识的档更好排查。
     键集合仍自动适配（§键集合与导出对称），本表只管值域。
266. **已知取舍：模糊会在收纳板四角露出**。圆角（半径 12）处内容被裁掉、而系统模糊铺满窗口矩形且裁不住，
     故四角各有约 12×12px 的"被糊桌面"（其余 90%+ 板面照旧被板底盖住）。若实机观感不接受，
     回退代价极小：把 `BoardWindow` 构造函数里的 `Material.UseNativeBlur = true` 去掉即可（退回纯静态板）。

### 给后续步骤的接口提示（**07 必读，旧提示已作废**）

- **07**：上一轮给 07 的接口提示（`MaterialEngine.Freeze/Unfreeze(Window)`、`IsFrozen`、`CaptureCount`、
  `LastGrabMs`、`Material.CaptureFrame()`/`SyncToFrame()`/`DropFrame()`/`RefreshSnapshot()`、`FrameMatchesScreen`、
  `VirtualScreenOrigin`、`TryCrop`）**全部已删除**，不要再按它们接入。多屏/换屏对材质**无需任何动作**
  （DWM 跟随窗口实时合成；不再有"换屏要重建帧"的概念）。若新增"换屏重建板"这类窗口重建路径，
  重建后**不要**手动重下发模糊（新 HWND 上 `IsVisibleChanged` 会兜底），但若确需强制，用
  `MaterialEngine.SetNativeBlur(window, true, force: true)`。
- **07**：性能回归断言改用日志口径（"开板后 0 条抓屏类日志"），`CaptureCount` 这个读数不存在了。
- **08**：无新增资源文件；`MaterialEngine.Thumbnail` 仍供设置页材质卡使用（"无"档为棋盘格）。
- **任意后续**：新增材质档 = `MaterialEngine` 的 `Fill`/`NoiseOpacity`/`HasGlassDepth` 各加一支
  （**不再有 `BlurRadius`**，系统模糊半径由 DWM 决定、不可配）+ `PersonalizePage` 加一张卡 + i18n 加 `material.*` 键
  + `ConfigPort.EnumValues["personal.material"]` 补值。

### 留人工验证项（自动化未覆盖，如实记录）

1. **收纳板玻璃观感**：与旧版对比是否"更真、更跟手"（移动/缩放/开合期间玻璃是否始终是当前桌面）；
   以及**四角是否能看到被糊的桌面小块**（决策 266 的已知取舍）——不接受就说一声，去掉 `UseNativeBlur` 即回退。
2. **"无"档实机观感**：收纳板只剩图标（+ 自定义背景图）时是否可用、图标是否还看得清（可配合"图标遮罩"开关）。
3. **设置窗与悬浮窗三板**：本轮它们改为纯静态材质层（本来背后画面只贡献几个百分点），请确认观感无退化。
4. **设置页清理后的排版**：六个说明性注释去掉后卡片是否留白过多、关于页去掉标题后的观感。
5. 本轮夹具留在 `build/_nattest`（能力探测，含 `nattest_out.txt` 与四张对照截图）；
   上一轮的 `_mattest` 面向已删除的"整屏帧 + 渲染变换"口径，**已不再适用**（保留作历史证据，不入交付）。

---

## 磨棱角（四角羽化补丁）+ 液态玻璃重写 + "无"档改主题纯色板（2026-10-06 · 用户三条追加要求）

- **范围**：用户三条——① 在"图层堆叠"基础上**磨去圆角外的棱角**（能就尝试）；② **重写液态玻璃材质**；
  ③ 材质"无"档**保留背景板**（单纯的主题纯色板，不加额外效果）。**未做 07/08**。

### 一、磨棱角：先把"能不能"穷举干净（夹具 `build/_nattest2`，五组配置）

判据：条纹底窗上，窗口**圆角外**采样点显示"原始条纹"（梯度≈170）= 该处没有窗口/没有模糊。

| 配置 | 圆角外结果 | 结论 |
|---|---|---|
| 无区域 + 无模糊（基线） | 原始条纹 | — |
| `SetWindowRgn`(圆角 r=40) + 无模糊 | 与基线**完全相同** | 区域对分层窗**无可观测效果** |
| `SetWindowRgn` + `SWP_FRAMECHANGED` | 与基线完全相同 | 同上 |
| 半透明板 + 模糊 + 圆角区域 | 梯度 10.7（被糊） | 区域裁不住系统模糊 |
| Show **之前**设区域 + 模糊 | 梯度 10.7（被糊） | 时机也不是原因 |
| `DwmEnableBlurBehindWindow(hRgnBlur=圆角区域)` | hr=**S_OK** 但中心梯度 136 = **没有模糊** | Win10 上这条老路已不出模糊 |
| `DWMWA_WINDOW_CORNER_PREFERENCE=ROUND` | hr=**0x80070057**(E_INVALIDARG) | Win11 才有，本机不支持 |

**硬结论：系统模糊的形状从系统侧裁不住**（四条路全断）。故改用**视觉侧**磨：
在圆角外那圈**月牙**上补一层板底色，用**径向渐变**从"圆弧处=板内强度"渐隐到"角落=0"
（`MaterialBackground.RebuildCornerVeil`，四角各一枚 `Path`，统一用左上角月牙模板 + 镜像变换摆位）。
月牙最宽处 = r(1−√2/2) ≈ 0.29r（r=12 → 3.5px），于是"硬边"变成约 5px 的平滑过渡。

### 二、另外三处改动

1. **液态玻璃重写**（`MaterialEngine.LiquidGlassRecipe`）：把一层厚玻璃拆成**四层可独立叠加**的配方，
   与 `Thumbnail` **同源**（预览即实际）：
   - `Rim` 内缘高光（1.5px 内描边）：上缘最亮 → 两侧转柔 → 下缘再catch一道亮（玻璃有厚度的主线索）；
   - `Specular` 顶部镜面高光带（55% 内淡出）；
   - `Vignette` 四周边缘渐深（径向，中心透明）；
   - `InnerShadow` 底部内阴影。
   主体底色由两端渐变改**三段式**（上亮 / 中过渡 / 下暗，带一点冷调），噪点 0.06 → 0.035。
2. **"无"档口径二次修订**：首版做成"什么都不画"，实机表现＝**收纳板没有背景板**（用户报障）。
   现改为**一块单纯的主题纯色板**：铺 `Color.Bg`（浅色 #F9F9F9 / 暗色 #1F1F1F），跟随
   "收纳板背景不透明度"，不加噪点/高光/内阴影/边缘渐深，也不开系统模糊。
   设置窗随之取消"把'无'折算成亚克力"的临时措施（纯色板本身就够可读）。
3. **修掉上一轮引入的回归：板底只能画一层**。旧实现的"板底 + 截屏层 + 同色叠层"是**夹心**结构，
   模糊（截屏）夹在两层同色底之间，贡献 α=10%；改成系统模糊后模糊跑到窗口背后，两层同色底一叠，
   合成不透明度变成 1−(1−0.9)² ≈ **99%**，模糊只剩 0.8% ＝**等于看不见**（材质重写等于白做）。
   现在只保留 `Plate.Background` 一层，模糊可见度回到与旧版一致。

### 三、实测（本机 Win10 19045 · 1920×1080@100%）

| 项 | 结果 |
|---|---|
| 构建 | `dotnet build -c Debug` **0 警告 0 错误** |
| 四角羽化补丁（数值核对，非观感） | 四角沿对角线由外向内取样**单调升向板底**（左上 201→210→218→216→218→**241**；右上 90→…→221；左下 138→…→229；右下 60→…→217）——宽度约 5px 的过渡，远超 AA 的 1px，硬棱角消失 |
| 模糊可见度（条纹信号窗周期 200px 垫在板后） | 板底横剖面 R 通道**跟着条纹起伏 25~29**（= 板底 90% 不透明，背后画面占 10%，与旧夹心结构一致）；修双层叠底前该起伏只有 **1.2** |
| 液态玻璃 | 顶边竖剖面 y=0→127(描边) / **y=1..2→253(内缘高光)** / y=3..4→245(镜面) / y≥5→236(主体)——四层配方确实在画 |
| "无"档 | 板底 (247,244,249)≈`Color.Bg`，板外桌面 (233,201,248) → **实心纯色板**（不是透明）；该档**系统模糊 0 条日志** |
| 稳定性 | 三轮重启（none / acrylic / liquidGlass）**0 ERROR / 0 WARN** |
| 设置页 | 材质卡 4 张（无 / 亚克力 / 模糊 / 液态玻璃）正常渲染，"无"卡＝纯色板缩略图 |

### 补充决策（续 266）

267. **系统模糊的形状裁不住，只能视觉侧磨**：`SetWindowRgn`（显示后 / Show 前 / 配 FRAMECHANGED）、
     `DwmEnableBlurBehindWindow(hRgnBlur)`、`DWMWA_WINDOW_CORNER_PREFERENCE` 四条路全部实测不通
     （数据见上表）。四角羽化补丁是"能在图层堆叠基础上做到的最接近磨平"的做法：
     硬边→约 5px 平滑过渡。**它只在系统模糊真的开着时才画**（模糊没开时圆角外本就是干净桌面，
     补上去反而多出一圈色），故由 `SetNativeBlur` 的返回值驱动，而不是由配置驱动。
268. **板底只能画一层**（根因见上）：这是"夹心结构在换成系统模糊后失效"的直接后果，
     也是本轮最容易漏掉的一处——不看数值根本发现不了（板面看起来一样，只是玻璃没了）。
     **教训**：把"背景来源"从进程内换成进程外（DWM）时，必须重算**叠加次序与不透明度合成**，
     不能只换绘制来源。
269. **液态玻璃拆成四层配方并与缩略图同源**：新增材质档的"装饰层"一律走
     `MaterialEngine.*Recipe(dark)` 返回冻结画刷，`MaterialBackground` 与 `Thumbnail` 都从它取——
     避免"预览与实际两套值"（步骤 06 初版就有这个毛病：预览是画出来的，实际是另一套常量）。
270. **"无"档＝主题纯色板**（二次修订）：`Color.Bg` + 个人不透明度，**跟随板底不透明度滑块**，
     不做特例；不铺噪点/高光/内阴影/边缘渐深，不开系统模糊。设置窗不再折算（纯色板可读）。

### 给后续步骤的接口提示

- **07/08**：材质接口未再变动（`SetNativeBlur`/`ClearNativeBlur`/`MaterialBackground.UseNativeBlur` 同上一轮）。
  新增：`MaterialEngine.FillEdgeColors` / `LiquidGlassRecipe` / `GlassRecipe`（新增材质档时同步补这三处 + `Thumbnail`）。
- **任意后续**：改圆角半径无需改补丁代码（`SetCorners` 会重建）；**方向圆角**（锚边两角直角）时
  直角那侧不生成补丁（半径 <1.5px 直接跳过）。

### 留人工验证项（自动化未覆盖，如实记录）

1. **四角观感**：现在圆角外是一圈约 5px 的柔和过渡（板底色→桌面）。请确认"方形模糊残留"是否已消失、
   过渡是否自然（截图证据：`build/_corners_sheet.png` 四角拼图、`build/_board_none_zoom.png`、
   `build/_board_liquid_zoom.png`）。
2. **液态玻璃观感**：内缘高光 / 顶部镜面 / 边缘渐深 / 底部内阴影四层叠加后的"厚玻璃感"是否满意
   （设置页材质卡与收纳板都已同步）。
3. **"无"档**：是否就是想要的"单纯主题纯色板"（当前是 `Color.Bg`，浅色 #F9F9F9 / 暗色 #1F1F1F，
   跟随"收纳板背景不透明度"）。若想要更实心，把该滑块拉到 100 即可。
4. 本轮夹具/证据留在 `build/`：`_nattest2`（棱角裁剪能力探测，含 `nattest2_out.txt`）、
   `_live_blur_check.py`（模糊可见度）、`_corner_check.py`（四角数值核对）、`_mat_switch.py`（逐档取样）。

---

## 材质档改名（模糊→柔化 / 液态玻璃→瞎眼防弹玻璃）（2026-10-06 · 用户要求）

- **改动**：`material.blur` 显示名"模糊" → **"柔化"**；`material.liquidGlass` 显示名"液态玻璃" → **"瞎眼防弹玻璃"**（中英双语同步）。
- **内部 id 一律不动**：`blur` / `liquidGlass` 是 settings.json 与导出配置里的**持久化值**，
  改名只动 i18n 的 `material.*` 显示名（见 `MaterialEngine` 顶部新增的"档位 id ↔ 显示名"对照注释）。
- **排版适配**：材质卡 62 → 76px 宽、卡名 10.5 → 10 号并允许折行（"瞎眼防弹玻璃"6 字原会顶到 62px 边界）；
  4 张卡合计 336px，仍在设置页右列（约 386px）内；"悬浮窗自定义 → 底面材质"的分段控件按内容自适应，无需改。

### 补充决策（续 270）

271. **改名只动显示层，不动持久化 id**：材质档 id 出现在 settings.json、导出的配置文件与
     `ConfigPort.EnumValues` 白名单里，动 id 会让老配置失效/被当成非法值跳过。故"改档名"= 只改 i18n。
272. **为什么设置窗的四个角是完美裁切、收纳板不是**（用户提问，记录成对照）：
     设置窗的材质层**没有开系统模糊**（`MaterialBackground.UseNativeBlur` 保持 false）——
     它的板比窗口小一圈（`PlateMargin` 24/24/24/30 留给投影），开模糊会让模糊漫到板外那圈投影区上；
     而**没有模糊就没有"圆角外露出被糊桌面"这件事**，圆角外本来就是干净桌面（分层窗的透明区），
     于是四角天然完美。收纳板的残留**完全来自系统模糊**（模糊铺满窗口矩形、形状裁不住，四条路实测都不通），
     故只有收纳板需要四角羽化补丁。**换句话说："玻璃"与"完美圆角"在当前 Windows 上是一对取舍**，
     要完美圆角就得关掉 `BoardWindow` 的 `Material.UseNativeBlur`（一行），代价是失去"透出桌面"。

---

## 圆角优先：收纳板也关掉系统模糊（2026-10-06 · 用户裁决）

- **用户裁决**：在"玻璃感"与"完美圆角"之间选**圆角**——"以圆角为优先级，拿掉所谓玻璃感"。
- **改动**：`BoardWindow` 构造里的 `Material.UseNativeBlur` 由 `true` 改 **`false`**（收纳板回到纯静态材质层）。
  这是**一行开关**，材质配方本身（板底/噪点/内缘高光/顶部镜面/边缘渐深/底部内阴影/发丝描边）全部保留，
  只是背后不再叠一层 DWM 实时模糊。
- **能力保留不启用**：`MaterialEngine.SetNativeBlur` / `ClearNativeBlur`、`SystemBridge` 的
  `SetWindowCompositionAttribute`、`MaterialBackground.UseNativeBlur` 与**四角羽化补丁**
  （`RebuildCornerVeil`，只在模糊真开着时才画）全部留着——换回玻璃感＝把那一个布尔量改回 `true`，
  羽化补丁会自动接管。`MaterialEngine` / `MaterialBackground` 的类注释已改写为"当前全项目无窗口启用"。

### 实测（本机 Win10 19045 · 1920×1080@100%）

**受控验证**（夹具 `build/_corner_controlled.py`）：用**静态条纹信号窗**垫在收纳板后面
（避免板后窗口内容变动造成假差值；需临时把 `dockPriority` 调到 high，测完还原），
再"关板截图 / 开板截图"逐像素比。**在会开模糊的亚克力档下测**（否则"无"档本来就不开模糊，测不出开关生效）。

| 采样区 | 开板前后最大差 | 结论 |
|---|---|---|
| 圆角外**月牙区**（到圆角圆心距离 > 半径） | 左上 2 ／ 右上 **0** ／ 左下 36 ／ 右下 2 | 圆角外**一个像素都没画** ⇒ 完美裁切（36 是掩码边界上的抗锯齿像素） |
| 板内 40×40（对照） | **255** | 板色确实盖住了条纹，说明这次比较有效 |
| 四角 16×16 实际像素 | 月牙区读到的就是条纹本色（洋红 255,0,255 / 黑 0,0,0） | 直接看到"板没碰那里"，不依赖掩码 |

会话日志：**系统模糊启用 0 条**、WARN/ERROR 0 条——开关确实生效（改前同一测法月牙区差值是 47~100，即"被糊的桌面"）。

### 补充决策（续 272）

273. **"玻璃感"与"完美圆角"在当前 Windows 上不可兼得，用户选圆角**：系统模糊铺满窗口矩形且形状裁不住
     （四条路实测见 267），圆角外必然露出一圈被糊的桌面。羽化补丁能把硬边磨成柔和过渡，
     但补丁本身也要在圆角外**画板底色**——严格说那仍不是"什么都没画"。故用户选"干净"：
     关掉收纳板的系统模糊，四角回到逐像素干净的桌面。
     **代价**：板底（90% 不透明）后面透出的是**清晰**桌面而不是模糊桌面；三档材质的"玻璃"观感由此消失，
     只剩底色 + 装饰。**回退**：`BoardWindow` 里 `Material.UseNativeBlur = true`（一行）。
274. **四角羽化补丁与系统模糊同生共死**：它由 `SetNativeBlur` 的**返回值**（实际是否生效）驱动，
     不是由配置驱动——模糊关着时圆角外本就是干净桌面，补上去反而多出一圈色。故本轮关掉模糊后
     补丁自动静默，无需删代码、也不会留下残留。

### 追加（同日 · 用户改主意）

275. **改回"玻璃感"**：用户看过两个方向后决定恢复——`BoardWindow` 里 `Material.UseNativeBlur` 改回 `true`，
     收纳板重新启用系统模糊（零抓屏零位图零延迟），圆角外那圈由四角羽化补丁按径向渐隐磨掉。
     关掉仍是同一行改 `false`（四角回到逐像素干净、补丁静默）。
     **两个方向都验证过**：开启时圆角外月牙区 47~100（被糊桌面，由补丁羽化）、关掉后 0~2（逐像素干净）。

---

## 步骤07 完成记录（2026-10-06 · 系统集成 / 多屏 / 健壮性）

- **范围**：步骤文件 07 全部任务（自启动、隐藏桌面图标健壮化、多屏与屏幕选择、后台任务线程化、
  文件监视加固复核、稳定性与运维、细节补全）+ 已提前落地项（批次九）的复测。**未做 08（打包）**；
  未改 00/01/06/08 步骤文件（07 文件本身为本步依据，未改）。
- **前置核对结论**：步骤文件 §0 列的批次九落地项**全部实测复现**（PID 缓存、6s 节流、200ms 防抖、
  watcher 64KB + Error 不停监听、日志轮转、静态事件退订、12s 超时、缓存上限），本步只在其上加固。

### 交付物（新增 / 修改）

| 文件 | 改动 |
|---|---|
| `core/TaskbarWatcher.cs` | **新增**：`TaskbarCreated` 钩子（`RegisterWindowMessage` 按进程缓存 + `HwndSource.AddHook`），窗口 `Closed` 自动 `RemoveHook`（自管生命周期，防 HwndSource 泄漏） |
| `core/SystemBridge.cs` | 新增 `QueryDesktopIconsVisible()`（问 SysListView32 的 `WS_VISIBLE`，返回可空）、`RecoverDesktopIconsIfNeeded()`、`TaskbarCreatedMessage()`、`EnumDisplayDevices`+`DISPLAY_DEVICE`、`EdidModel`/`ParseEdidModel`、`GetMonitorInfo`+`MONITORINFO`；`SetDesktopIconsVisible` 改为**以真实查询为准**（幂等，状态未知时不盲发切换） |
| `core/Screens.cs` | **重写**：`ScreenInfo` 记录 + 型号名（EDID）+ `All()/Enumerate()/ModelOf()/ByDeviceName()/Primary()/Exists()/WorkAreaMatches()`；`Screen.AllScreens` 结果与型号名各按 `Invalidate()` 失效缓存 |
| `core/Autostart.cs` | `SetEnabled` 返回 `bool` + **写入后回读校验**（静默失败也算失败） |
| `core/TrayController.cs` | 新增 `ShowBalloon(title,text)`；托盘文案改经 `I18n.Tr("tray.tooltip")` |
| `core/JsonKeyValueStore.cs` | 解析失败 → `LoadFailed`/`CorruptBackupPath` + 原文件复制为 `*.bak`（不静默覆盖用户数据） |
| `core/DesktopScanner.cs` | **图标提取改单消费者后台队列 + 渐次回填**（`BlockingCollection` + BelowNormal 后台线程 + `IconReady` 事件 + 在飞/失败去重）；防抖计时器 `Background → Normal` 优先级 |
| `models/BoardItem.cs` | 新增 `IconVersion`（缓存路径不变时靠版本号触发重载） |
| `ui/components/IconItem.xaml.cs` | `LoadIcon(path, version)`；`IconVersion` 变化即重载；形状判定在位图缺失时**不缓存**（否则 Unknown 被钉住，遮罩/磨圆不按真实剪影重算） |
| `ui/BoardWindow.xaml.cs` | `OnIconReady`（渐次回填）；hoverReveal **图标清单缓存**（`RevealIcons()` + `InvalidateRevealCache`）；缩放期 `RelayoutThrottled`（30ms）；布局类设置 **16ms 合并**（`ScheduleLayoutRefresh`）；`AddVirtualItem`/`CreateShortcutByPick` **后台化**；新增 `OnDisplayChanged()`/`RequestFullRefresh()`/`RequestRescan()` |
| `ui/FloatingDock.xaml.cs` | 私有 `SelectedScreen()` 删除、统一走 `Screens.SelectedScreen()`（带缓存与拔屏回退）；新增 `OnDisplayChanged()`；`Closed` 补 `SettingsChanged` 对称退订 |
| `ui/SettingsWindow.cs` | 新增 `ReloadCurrentPage()`（自启动写键失败时开关回滚、显示变化时重载屏幕列表） |
| `ui/pages/DisplayPage.cs` | 屏幕列表改 `Screens.Enumerate()`，显示"显示器N · 2560×1440 · 主屏 · P24A2G"（型号缺失只回退基础信息） |
| `App.xaml.cs` | `ApplyDesktopIconsOnStartup`（崩溃恢复 + 意图应用）、`StartHeartbeat`、`InstallExitPaths`（`SessionEnding` + `ProcessExit`）、`CleanExit`（幂等，`lastRunOk=true`）、`AttachTaskbarWatcher`/`OnTaskbarCreated`/`ReplayDesktopIconsIntent`、`StartDisplayWatch`/`HandleDisplayChanged`、`ReportStartupIssues`、自启动写键失败回滚 |
| `dependencies/resources/i18n/zh-CN.json` / `en-US.json` | 新增 `notify.*` 5 键（中英各一）；删除 5 个**零引用**死键（`cat.rename`/`new.shortcut.label`/`set.catShow`/`set.configHint`/`set.contentFilter`） |

### 一、系统集成实测（本机 Win10 19045 · 1920×1080@100% · 单显示器 · 中端独显）

**自启动（§1）**：注册表现值 = `"…\src\Cship\bin\Debug\net8.0-windows\Cship.exe" --autostart`
——**开发态实测口径确认**：`Environment.ProcessPath` 指向 `bin\…\Cship.exe`（apphost），
**不是** `dotnet run` 命令行（与 01 旧描述不同，以实测为准）。夹具把同一份 `Autostart.cs` 编进来跑：
写入值 = 夹具自身 `ProcessPath` + `--autostart`（**证明 exe 部分是运行时解析、绝不硬编码**，
故"打包态 = 发布 exe 绝对路径"只是同一表达式在另一位置的取值）、`IsEnabled()` 回读一致、
`SetEnabled(false)` 删除成功且回读为 false；测完 Run 值已还原为原开发态值。
`--autostart` 静默语义复核：启动链路只有 托盘 + 悬浮窗，**不弹收纳板**（`OnStartup` 无开板调用，实测日志无"消散/开板"行）。

**隐藏桌面图标 + 崩溃恢复（§2，端到端）**：

| 步骤 | 实测 |
|---|---|
| 意图=隐藏 → 启动 | 图标实测 `visible=False`；state `lastRunOk=false` / `hideIconsOwned=true` |
| 强杀进程（`Stop-Process -Force`，不走退出清理） | 图标仍隐藏；state 两个标记原样保留 |
| 再启动 | 日志逐条：`未正常退出`(WARN) → `检测到桌面图标处于隐藏态（上次非正常退出），强制恢复可见` → `显隐 → 显示（切换前实测=隐藏）` → `显隐 → 隐藏（切换前实测=可见）` → `启动自检：…已恢复可见` |
| 意图改=显示 → 再启动 | 强制恢复可见后不再隐藏；图标实测 `visible=True`；`hideIconsOwned=false` |

**`TaskbarCreated` 重放（§2）**：用探针**广播**该消息（不杀 explorer，零风险）→ 日志
`收到 TaskbarCreated：资源管理器已重启` → 812ms 后 `桌面重建后重放隐藏意图：意图=显示 实测=可见`（延迟重放避开 SysListView32 尚未重建的空窗）。
真实 explorer 重启路径**未做**（见留人工验证项）。

**退出路径统一（§6）**：`SessionEnding` + `ProcessExit` + `OnExit` 三路共用幂等 `CleanExit`；
夹具实测 `Environment.Exit(0)` 确实触发 `AppDomain.ProcessExit`（`ExitWithFade` 1.5s 超时兜底走的正是这条路）。
第二实例（单实例检查处早退）由 `_storesReady` 闸门挡住，不会碰存储。

**配置损坏容错（§6）**：把 `settings.json` 写成 `{ this is not valid json,,,` → 日志
`解析 …失败，相关键走默认/缺省` + `损坏的配置已备份：…settings.json.bak（随后重建默认）`；
实测 `.bak` 生成（28 字节 = 原始垃圾内容）、`settings.json` 被重建为合法 JSON。测后已还原用户原设置。

### 二、多屏与屏幕选择（§3）

- **型号名（EDID 路线，零依赖）**：本机 AMD 驱动下 `EnumDisplayDevices` 的 `DeviceString` 恒为
  `"Generic PnP Monitor"`（通用占位），故实现走 **EDID**：`EDD_GET_DEVICE_INTERFACE_NAME` 拿
  `\\?\DISPLAY#LHCFFFF#7&141258ed&6&UID260#{guid}` → 精确拼出注册表
  `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\<硬件ID>\<实例>\Device Parameters\EDID` → 解析描述符 0xFC。
  **实测真实型号 = `P24A2G`**（夹具直接调用产品代码 `SystemBridge.MonitorModel`）。
  取不到时回退基础信息（`Describe()` 不显示空占位），全程无阻塞（注册表读 + 一次 P/Invoke）。
- **热插拔分支实测**：把 `display.screen` 设成不存在的 `\\.\DISPLAY9` → 广播 `WM_DISPLAYCHANGE`
  → 日志 `所选屏已移除（…DISPLAY9）：自动切回 auto（光标所在屏）`，`settings.json` 实测变回 `auto`，
  悬浮窗复位、收纳板夹回、设置页屏幕列表重建（`ReloadCurrentPage`）。
- **缓存**：`Screens.All()`/`ModelOf()` 带缓存，`DisplaySettingsChanged` 时 `Screens.Invalidate()` 失效重取
  （批次九实测 `Screen.AllScreens` 是热点调用）。
- **混合 DPI**：**本机只有一台 1920×1080@100% 显示器，无法物理实测跨屏拖动**（如实记录）。
  静态审计结果：全项目物理/逻辑换算点**全部**走 `GlassWindow.DpiScaleX/Y`（或 `dpi.DpiScaleX/Y`）与
  `SystemBridge.ScaleAt`（按点位查 `GetDpiForWindow`）；**无一处手写 `/96` 或 `*96`**（`grep` 全量核对）。

### 三、后台任务线程化（§4，批次九 4 个遗留热点全部收口）

| 热点 | 改法 | 改前 → 改后（实测） |
|---|---|---|
| ① 拖入路径 UI 线程做 COM/图标/写盘 | `AddVirtualItem` / `CreateShortcutByPick` 挪进 `Task.Run`，回 Dispatcher 挂模型 | 拖入不再阻塞 UI（代码路径核实 + 开板/扫掠全程 0 异常） |
| ② `UpdateReveal` 每次 MouseMove 全量 `FindDescendant` | 维护 `List<IconItem>` 缓存，`Relayout` 置脏、容器数变化或不完整时重建 | hoverReveal 模式 3×60 步扫掠 0 WARN/ERROR、进程存活 |
| ③ 缩放 `DragDelta` 逐帧全量 `Relayout` | `RelayoutThrottled`（30ms），落定由 `EndResizeChrome` 补一次 | 缩放期全量重排次数降约 2/3（30ms 窗口） |
| ④ 滑块逐帧广播全板刷新 | `ScheduleLayoutRefresh`（16ms 合并） | 同一帧内多次写入只做一次全量 `RefreshContainers + Relayout` |
| （附加）图标提取 | **单消费者后台队列 + 渐次回填** | 见下 |

**图标渐次回填实测（500 枚冷图标）**：

| 指标 | 改前（同步提取） | 改后（队列 + 回填） |
|---|---|---|
| 板列出 543 项 | 3400 ms（等全部图标提完） | **772 ms**（立即出内容，图标随后补齐） |
| 500 枚图标全部落盘 | （含在上面 3400ms 内） | 2436 ms（BelowNormal 单线程串行，不挤 UI/shell） |

**大桌面一致性（§5 验收）**：桌面 43 项为基线；批量建 200 文件（236ms）→ watcher 刷新 →
`源 243 项 / 可见 243 项`；一次性删 200（120ms）→ `源 43 项 / 可见 43 项`。再加到 **500 个文件**
（`源 543 项 / 可见 543 项`）→ 全删 → 回 `源 43 项 / 可见 43 项`，**0 ERROR、进程存活、桌面文件数还原**。
（批量删除 500 时重扫曾被推迟到 8.4s——根因是防抖 `DispatcherTimer` 默认 Background 优先级在数百容器
果冻入场期间被渲染饿死；提到 Normal 后按点触发，见决策 279。）

### 四、稳定性与运维实测（§6）

| 指标 | 实测 |
|---|---|
| 启动到悬浮窗可见 | **1055 / 1106 ms**（两次；目标 <3s） |
| 启动 3s 后 WS | 114.9 ~ 116.1 MB |
| 空闲（托盘+悬浮窗静止） | 60s 后 WS **14.9 MB**（含 60s 工作集整理）、本分钟 CPU **109 ms ≈ 0.18% 单核** |
| 开板（43 项） | 峰值 202.6 MB → 稳定 197.4 MB → **关板后 40.1 MB**；开板空闲 CPU 94ms/30s ≈ 0.31% 单核 |
| 开板（243 项 / 543 项压力） | 241.3 MB / **288~310 MB（超 250MB，如实记录，见决策 280）** |
| 日志轮转/清理 | 复核通过（按天 + 5MB 轮转 + 7 天清理，`Logger` 未改） |
| 单实例 | 复核通过（第二实例早退且不碰存储，不产生第二个托盘图标） |

### 五、细节补全（§7）

- **Esc 路由**：`ShortcutRouter` 注册层级 = 设置窗 > 收纳板（分类页不占层），两窗 `Closed` 均 `Unregister`；复核通过（未改）。
- **气泡经 I18n**：新增 `TrayController.ShowBalloon`，全部 4 处调用（自启动失败 / 配置损坏 / 图标恢复 /
  屏幕移除）文案均取自 i18n，中英齐全。
- **文案自检**：新增工具 `build/i18n_audit.py`（可重复运行，退出码即结论）——实测
  **176 键两语言完全对称、89 处 `I18n.Tr("字面量")` 引用全部存在、0 个零引用死键**。
- **静态事件审计**：全项目 `+=`/`-=` 逐一配对核对。修 1 处（`FloatingDock.SettingsChanged` 缺对称退订）；
  本步新增的三处全部成对（`DisplaySettingsChanged` 在 `OnExit` 退订、`TaskbarCreated` 由 `TaskbarWatcher`
  在窗口 `Closed` `RemoveHook`、`DesktopScanner.IconReady` 在 `BoardWindow.Closed` 退订）。
  另两处既有 `UserPreferenceChanged`（`ThemeResolver` / `ThemeEngine`）为**进程级单例 + 标志位幂等 + 零捕获 lambda**，
  不构成泄漏，保留并记录。

### 补充决策（续 275）

276. **`SetDesktopIconsVisible` 以"真实状态查询"为准，自记状态只作兜底**：`WM_COMMAND 0x7402` 是
     **切换**指令，进程被杀 / explorer 重启后自记状态必然失真。改为先问 `SysListView32` 的 `WS_VISIBLE`：
     与目标一致即 no-op、不一致才发切换；**查询不到真实状态时不盲发**（未知态发切换可能把"已隐藏"变"显示"）。
     实测：`visible → 隐藏 → hidden → 显示 → visible → 再显示 = no-op`。这一步让"崩溃恢复"与
     "TaskbarCreated 重放"两条路径都天然幂等。
277. **崩溃恢复的判据是"本程序自己隐藏过"（`state.hideIconsOwned`），不是"上次是否崩溃"**：
     用 `lastRunOk` 做判据会在"上次崩溃但图标本来就没被我们动过"时误开用户**手动**关闭的桌面图标。
     `hideIconsOwned` 只在本程序成功隐藏后置位、恢复后清除，语义精确；`lastRunOk` 保留为通用崩溃信号（记日志）。
278. **心跳写的是"运行中"标记（`lastRunOk=false`），不是 true**：步骤文件原文写"每 30s 写 true"，
     但那样**判据自毁**——被强杀时磁盘上留的是最后一次 `true`，崩溃永远检测不出来（验收项
     "杀进程→再启动→图标恢复"必然失败）。正确语义：运行期间恒 false、只有走完正常退出流程才置 true。
     30s 心跳的作用是让"运行中"这一事实**耐久**（顺带把防抖中的 state 一起刷下去）。
279. **大桌面刷新延迟的根因是"防抖计时器被渲染饿死"**：`DispatcherTimer` 默认 Background 优先级，
     数百枚容器果冻入场 + 解码期间收不到 tick → 实测批量删 500 文件后重扫被推迟到 8.4s。
     提到 Normal 后按点触发（tick 只做一次事件转发，扫描仍在 `Task.Run` 里，不会卡渲染）。
     **教训**：把"后台刷新"交给低优先级计时器时，必须验证它在重渲染负载下仍能按时触发。
280. **"图标渐次回填"是图标提取的正确形态（不是把同步提取换个线程）**：`Scan()` 只算确定性缓存路径并
     立即返回，未命中项排入单消费者队列、由 BelowNormal 后台线程逐个提取写盘，完成即按名广播
     `IconReady` → UI 只重载那一枚。收益：板立即出内容（543 项 3400→772ms）、shell 调用天然串行
     （不瞬间打爆 COM/磁盘队列）、拖入路径彻底不碰 shell。代价：首次显示时图标有短暂空窗（可接受，
     正是"渐次补齐"的观感）。
281. **"图标就绪"必须用版本号而不是路径变化触发重载**：缓存路径 = `sha1(v7|path|mtime)`，图标从
     "还没提"到"提好了"**路径完全不变**，只靠 `IconPath` 比较永远不会重载。故 `BoardItem.IconVersion`
     在就绪时自增，`IconItem` 同时比较路径与版本。
282. **形状判定在位图缺失时不缓存**：`GetShapeKind` 原实现在位图取不到时把 `Unknown` 也写进缓存 →
     图标就绪后遮罩/磨圆不会按真实剪影重算（"图标就绪了但观感不对"）。改为位图非空才缓存。
283. **型号名走 EDID 注册表，不走 WMI**：步骤文件建议 `WmiMonitorID`，但 WMI 在 .NET Core+ 需
     `System.Management` 包（违反"零第三方依赖"铁律）。改用
     `EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)` → 设备接口名 → 注册表 EDID → 描述符 0xFC，
     零依赖、无超时需求、结果与 EDID 同源（本机实测 `P24A2G`，而驱动友好名只是
     `"Generic PnP Monitor"` 占位）。驱动给出真实友好名时优先用它（少一次注册表读）。
284. **`Autostart.SetEnabled` 必须"写入后回读"**：注册表写入可能被组策略/重定向**静默丢弃**，
     只 `try/catch` 会把失败当成功、开关显示"已开"而实际没有。回读不一致即返回 false →
     `App` 把设置项回滚为实际值 + 气泡告知 + 让设置页重载控件（决策落地在
     `ApplyGlobalSettingSideEffect`，带防重入标志）。
285. **543 项压力下常驻内存超 250MB 是"如实记录的已知边界"**：实测 43 项（用户实际桌面）
     峰值 202.6MB 达标；243 项 241MB；**543 项 288~310MB（超）**，且与图标冷热无关
     （冷 310 / 热 288）→ 主因是 WPF **每项可视元素成本线性叠加**（分层窗 + 软件合成），
     非图标位图（543×134²×4 ≈ 39MB）。未做优化：降解码尺寸收益不足 20MB 且会改观感
     （违背"用户体验优先"），虚拟化与 02 步决策 3"分批加载而非虚拟化"冲突（精确摆位会破）。
     **250MB 这一档的成立前提是"正常桌面规模"**；500+ 项属压力场景，已记录实测值。

### 给后续步骤的接口提示

- **08**：无新增资源文件；`TaskbarWatcher` / `Screens` / `SystemBridge` 新 API 均在程序集内，单文件发布无需处理。
  冒烟新增三项可自动断言的日志口径：`资源管理器重启监视已挂载`、`图标提取后台队列已启动`、
  `桌面图标显隐 → …（…切换前实测=…）`。发布后请复核 **打包态自启动形态**（`"<发布 exe 绝对路径>" --autostart`）
  与 `--autostart` 静默启动（不弹收纳板）。
- **任意后续**：新增"需要真实桌面图标状态"的逻辑一律用 `SystemBridge.QueryDesktopIconsVisible()`
  （可空），**不要**再新增自记布尔；新增用户可见文字一律加 i18n 键并用 `build/i18n_audit.py` 自检。
- **任意后续**：新增静态事件订阅必须成对退订（窗口 `Closed` / 显式 `Detach`），
  `TaskbarCreated` / `DisplaySettingsChanged` / `IconReady` 三处已有先例可抄。
- **性能回归**：本步的断言口径 = 启动到悬浮窗可见 <1.2s、空闲 60s WS <20MB 且 CPU <0.2% 单核、
  开板（43 项）峰值 <210MB、关板后 <50MB、543 项列出 <1s。

### 留人工验证项（自动化未覆盖，如实记录）

1. **托盘"关闭"正常退出路径**：托盘图标在本机"隐藏图标"浮层内（批次 06 已记录 Win10 溢出面板
   不响应 `SendInput`），无法脚本化点击 → `OnExit → CleanExit → lastRunOk=true` 这一条请羽点一次
   托盘"关闭"，重启后**不应**出现"检测到上次运行未正常退出"的 WARN（`ProcessExit` 机制已由夹具单独证明）。
2. **真实 explorer 重启**：本步用广播 `TaskbarCreated` 验证（等价且零风险），未真杀 `explorer.exe`；
   请羽在方便时用任务管理器重启资源管理器，确认隐藏意图自动重放。
3. **混合 DPI 双屏拖动**与**真实拔插副屏**：本机单显示器 1920×1080@100%，无法物理验证
   （代码审计：换算点全走 `DpiScaleX/Y`，无手写 96）。
4. **自启动的"注销重登"端到端**：注册表值与命令形态已实测，但"注销重登后静默常驻"需真实注销一次。
5. **543 项内存（决策 285）**：若羽认为 500+ 项场景也需要 <250MB，可讨论"降解码尺寸 / 视口外释放位图"
   等方向——当前按"用户体验优先"未动。
6. 本步测试脚本留在 `build/`（可重复运行、不入交付）：`_step7_probe.ps1`（能力探针：显示器枚举 /
   图标状态 / TaskbarCreated 与 WM_DISPLAYCHANGE 广播 / 精确点击与扫掠）、`_step7_lifecycle.ps1`
   （启动耗时 + 内存 + 强杀恢复 + 配置损坏）、`_step7_icons_e2e.ps1`（图标崩溃恢复端到端）、
   `_step7_reveal_smoke.ps1`（hoverReveal 扫掠）、`_step7_bigdesk.ps1`（大桌面 / 批量删除）、
   `_step7test/`（编译真实 `SystemBridge.cs`/`Autostart.cs` 的验证夹具）、`i18n_audit.py`（文案自检）。
   测试期对桌面图标与 `settings.json` 的所有改动均已还原（桌面文件数 35、图标可见、Run 值原样）。

## 批次十六 · 5 项用户要求（2026-10-06 · 步骤 07 之后 / 步骤 08 之前）

- **用户要求（原文五条）**：① 收纳板最左列图标离板左缘"多隔几个像素"；② 悬浮窗自定义·自定义·透明度调整
  也要能影响"预设"所给的各层透明度（**不改变原始预设**）；③ 改默认项——收纳板材质默认"无"、
  悬浮窗底面材质默认"柔化"；④ 自定义背景图片要在**主题纯色板、收纳板材质之下**；⑤ 适配配置导入导出功能。
- **范围**：只做这 5 项 + 必要文档/自检同步。**未做步骤 08（打包/移植性/最终验收）**。
- **交付形态**：改完**重建开发态并重启真机**（旧实例 PID 3920 强停 → 新实例 PID 17392），
  用户可直接做视觉验收。

### 交付物（修改）

| 文件 | 改动 |
|---|---|
| `ui/BoardWindow.xaml` | ① `Scroller.Margin` 左 1 → **6**（右/下仍 1，浮层滚动条不占宽）；④ Grid 子序改为 `CustomBgHost → BgShade → MaterialBackground → …`（**图片压最底**） |
| `ui/BoardWindow.xaml.cs` | ① 新增 `BoardPadLeft = 6`；3 处 `ActualWidth - BoardInnerPad*2` 视口兜底折算改为 `- BoardPadLeft - BoardInnerPad`；③ 材质兜底 `Acrylic → None` |
| `ui/components/SquarePlate.xaml.cs` | ② 新增 `_layerOpacity[3]` + `SetLayerOpacities(double[])`；`ApplyPlateFill` 拆出 `ApplyLayer`：层图优先，否则 `Fill(material, dark, 该层不透明度)`，噪点/内高光随层淡出；默认材质 `Acrylic → Blur` |
| `ui/FloatingDock.xaml.cs` | ② `ApplyDockCustom` **不论模式**都读 `personal.dockCustom.opacity` 并 `Plate.SetLayerOpacities`；兜底默认 → `Blur` |
| `ui/pages/PersonalizePage.cs` | ② 三层不透明度滑块从"自定义面板的行内控件"上移为**与预设/自定义面板平级的常驻行**（`BuildLayerOpacityRow`），`DockImageRow` 去滑块；③ 重置与默认 → `blur` / `none` |
| `core/SettingsStore.cs` | ③ 默认表：`personal.material = "none"`、`personal.dockCustom.material = "blur"` |
| `ui/SettingsWindow.cs` · `ui/components/GlassPopup.cs` · `ui/components/MaterialBackground.xaml.cs` | ③ `personal.material` 兜底字面量 `Acrylic → None`（默认表与代码单一口径） |
| `core/ConfigPort.cs` | ⑤ 新增 `Coerce` 值域校验：结构非法跳过并点名，数值越界夹回 |
| `dependencies/resources/i18n/{zh-CN,en-US}.json` | ② 新增 `set.dockCustom.layerOpacity`（各层不透明度 / Layer opacity） |
| `build/sync_defaults.py` | ③ 补两条材质默认值同步规则（防日后回退成双口径） |
| `build/_uitest/Program.cs` | 自检夹具新增 D（层不透明度语义）、E（个性化页常驻行）两节，A 节补本轮适配断言 |

### 一、实测（夹具 `build/_uitest`，跑在自带 `run/dependencies/config`，不碰用户态）

```
== A. 配置导入导出往返 ==   往返 51 项 / 忽略 0 项；负例 忽略 4 / 应用 0 且键名可读
[默认值] personal.material=none · personal.dockCustom.material=blur
[形状负例] 忽略 4 / 应用 0 —— personal.dockCustom.opacity、.images、personal.boardBg、personal.boardOpacity（值非法）
[区间夹回] 忽略 0 / 应用 4 —— boardOpacity 150→100、boardBgOpacity -20→0、opacity [120,50,-8]→[100,50,0]
== B/C. 主题预览卡渲染 + 点击 ==  4 张卡/4 枚徽标，点击第 3 张 → theme.mode=auto，可见徽标唯一
== D. 悬浮窗各层不透明度 ==
[预设层 α] 上=0 中=76 下=153（柔化档基准 α=153；[0%,50%,100%] 三层按比例折算）
[装饰层透明度] 上=0 中=0.5 下=1（噪点/内高光随层淡出，不留亮边）
[层图折算] ImageBrush.Opacity=0.5（调用方设的 0.5 未被二次折算成 0.25）
[原始预设] MaterialEngine.Fill(blur,100%) α=153（配方本身不被用户调整改写）
== E. 个性化页 · 各层不透明度常驻行 ==
[滑轨数] 4（背景图 1 + 各层 3）；[层滑轨] 命中 3/3
[常驻性] 与滑块平级的面板数 = 2（预设 + 自定义 → 切模式不隐藏）
[写入] 顶层滑轨 → personal.dockCustom.opacity=[42,100,100]
== ALL PASS ==
```

- **文案自检**：`build/i18n_audit.py` → **177 键两语言对称、89 处字面量引用全部存在、0 个零引用死键**。
- **构建**：`dotnet build -c Debug` **0 警告 0 错误**（XAML 与 BAML 一并重生成）。
- **真机启动**：强停旧实例 → 重建 → 启动，日志 `startup pid=17392 autostart=False`、
  `悬浮窗 z 序优先级 = Low`、`动画帧率档 = 120 fps`、`资源管理器重启监视已挂载`，**本次启动 0 ERROR**；
  唯一 WARN 是"检测到上次运行未正常退出（lastRunOk=false）"——**这是强停（TerminateProcess 不触发
  `ProcessExit`）的预期结果**，且 `state.hideIconsOwned=false`，故恢复逻辑不碰桌面图标（决策 277/278 的语义）。
  另注：本次启动的存储维护删除了 2 个无引用 assets 副本（`personal.boardBg=null`、层图全空 → 确为孤儿）。
- **未做（留视觉验收）**：① 的左缘留白、④ 的图层叠压观感、② 的三板实时变化都是**目视项**，按用户要求交由羽验收。

### 二、逐项口径

**① 最左列图标留白**：图标区内边距原本四边都收到 1px（2026-10-05 需求②"那圈留白就是厚边框"），
本轮**只把左边单独放到 6px**（右/下不动：右侧是浮层滚动条的地盘，放大右侧会让滚动条压住末列图标）。
`Relayout` 的视口宽度兜底值同步扣 6+1（正常走 `Scroller.ViewportWidth`，兜底只在模板未实例化时用）。

**② 层不透明度对预设生效（且不改原始预设）**：见决策 286。

**③ 默认项改值**：`personal.material=none`、`personal.dockCustom.material=blur`。改的是**默认表**
（`SettingsStore.DefaultRaw`）与散落各处的**兜底字面量**（`sync_defaults.py` 的两条新规则就是为它们补的），
因此"删 settings.json / 全新机器首启 / 高级页恢复默认"三条路径都得到同一外观；
**已存在的 settings.json 不受影响**（缺键才补默认）——用户当前文件里本来就是 `none` / `blur`，与本轮默认一致。

**④ 背景图压最底**：见决策 287。

**⑤ 配置导入导出适配**：见决策 288。

### 三、补充决策（续 285）

286. **"各层不透明度"是一组常驻控制，不是"自定义模式的附属"**：用户要求它对预设层同样生效，若仍留在
     自定义面板里，就变成"要调预设层的透明度得先切到自定义"——控制项与作用对象对不上。
     故三条滑轨上移为**与两个模式面板平级的常驻行**（切模式不隐藏、不重建），两模式共用同一个键。
     实现上：**预设模式把值折进 `MaterialEngine.Fill` 的 α**，而不是设 `Border.Opacity`——
     三板 `Opacity` 归消散/复现动画所有（`PlayDissolve` 从 1 淡到 0），改基值会被动画顶掉；
     自定义模式的层图仍由调用方设在 `ImageBrush.Opacity` 上（**同一组值只折算一次**，夹具 D 节守着这条）。
     装饰层（噪点、内高光）随该层同透明度——否则把某层调到 0% 时画刷透了、装饰还留一道亮边。
     **原始预设零改写**：配方常量与 `Fill` 的 100% 基准未动（夹具断言 α 仍为 153）。
287. **"图片在材质之下"＝图片是全摞最底，不是"图片居中"**：决策 240（"材质在下、图片居中"）当时是为了
     让近不透明的板底盖不住图片；本轮用户明确要求反过来。新口径下背景图要透出来靠的是**板底自身的 α**
     （`personal.boardOpacity`）——板底 100% 时图片完全不可见是**预期行为**，要看图就调低板底不透明度。
     10% 主题色遮罩仍贴在图片正上方（它修饰的对象是图片本身），随图片一起被材质盖住。
288. **导入侧对"结构型键"补形状校验，而不是照单全收**：`personal.dockCustom.opacity/images`、
     `personal.boardBg`、`boardOpacity/boardBgOpacity` 这些键的**值域是结构**，读取侧只会各自静默兜底
     （非法项按 100/空名/不裁剪处理）——照单全收会得到"摘要说导入了、实际什么都没变"的假成功。
     故 `ConfigPort.Coerce` 逐键校验：**结构非法 → 跳过并在摘要里点名**（与枚举白名单同一套"可诊断"口径，
     决策 236）；**仅数值越界 → 夹回 0~100 后放行**（越界只是手写/旧版的偏差，夹回比丢弃更符合用户预期）。
     键集合仍是"默认表 ∪ 当前实际持有的键"现算（决策 236），本轮新增/改名的键无需再动 ConfigPort。

### 四、追加修订（同日 · 用户反馈"层滑轨绑定反了"）

- **用户反馈**：*"这个透明度调节对应的层：上下两层绑定的滑轨调换位置。（因为视觉上，右下角的那一层才是上层）"*
- **问题**：设置页/存储的三个值按**界面层序** `[上,中,下]` 排，而板子的绘制序是
  `[front(左上，画在最上), mid, back(右下，画在最下)]`——原实现把两者按同序直连，于是
  "上层"滑轨实际作用在**左上角**那块板上，与用户看到的层身份相反。
- **改法**（`ui/FloatingDock.xaml.cs` 的 `ApplyDockCustom`，唯一的映射点）：交给板子前把上/下对调
  （`plateOpacity = [下,中,上]`、层图 `layers[2 - i]`）。**层图与层不透明度共用同一份对调**，
  否则会出现"图落在某一层、不透明度作用在另一层"的错配。`SquarePlate` 内部层名（front/mid/back）
  保持"绘制序"语义不动，只在类注释里写明它与界面层序相反。
- **不动的东西**：消散/复现编排（仍是 front 向左上、back 向右下错开）、`personal.dockCustom.*` 的键名与
  数组长度、"上/中/下"三行的**行序**（仍自上而下排列，只是绑定对调）。
- **数据影响**：`personal.dockCustom.images` 与 `.opacity` 里 index 0/2 的**落点板**对调了。
  用户当前 `images` 全空、`opacity` 全 100，故无观感变化；若日后有人已设过层图，重新选一次即可。

**实测（夹具 `build/_uitest` 新增 F 节，走真实 `FloatingDock` 构造路径）**：

```
== F. 层滑轨 ↔ 板 绑定 ==
设 opacity = [0,50,100]（上/中/下）后：
[α] 左上=153 中=76 右下=0     → 上(0%) 落在右下角、下(100%) 落在左上角 ✔
[层图] 上层图落在：左上=False 中=False 右下=True   → 层图与不透明度同一份对调 ✔
== ALL PASS ==（A~F 六节全绿）
```

- **夹具工具修正（踩坑记录）**：`build/_uitest` 对 `Cship.dll` 的 CopyLocal **不会**因主产物重建而刷新
  （obj 缓存），本轮因此跑出过一次"旧代码"的**假失败**（现象：改了源码、重跑仍报错）。已在 `run.ps1`
  组装阶段**强制**用 `_verify_fix` 的新产物覆盖 `run\Cship.dll`，并在脚本头写明这个坑。
  教训：**夹具报错时先核对它加载的程序集哈希**，再怀疑自己的代码。

### 五、补充决策（续 288）

289. **"上层/下层"以用户看到的层身份为准，映射只允许有一个落点**：板子的绘制序（front 画在最上、
    back 在最下）是渲染事实，用户看到的"上层"是右下角那一层——两者相反时**改映射、不改渲染**：
    对调发生在 `FloatingDock.ApplyDockCustom`（存储/界面 → 板的唯一入口），`SquarePlate` 内部保持
    "front/mid/back=绘制序"的语义不被污染。**层图与层不透明度必须共用同一份对调**——
    只对调其中一半，就会出现"图在 A 层、透明度作用在 B 层"这种最难排查的错配。

---

## 步骤08 完成记录（2026-10-06 · 打包 / 移植性 / 最终验收）

- **范围**：步骤文件 08 全部任务（程序图标、双架构发布、移植性、体积与启动性能实测、全量回归验收、兼容性验收、收尾）
  \+ **2026-10-06 用户新增的两项交付规格**：发行版落 `dock++\Releases\`（仅功能产品本身）、源码仓库落 `dock++\CShips\`（**git 仓库**）。
- **交付形态**：`Releases\`（自包含发行版）+ `CShips\`（git 源码仓库）；`dock++\` 保留为开发工作区。
- **未改**：`产品设计简要说明.txt`、00/01/02~07 步骤文件。**08 步骤文件按用户授权同步修改**（交付根改 `Releases\`、新增 §8 源码仓库、图标与压缩口径按实测更新）。

### 交付物（新增 / 修改）

| 文件 | 改动 |
|---|---|
| `build\makeicon.ps1` | **新增**：素材源图 → 多尺寸 ICO（16~128 用 DIB 帧、256 用 PNG 帧）+ 512 PNG，三处同源产出，幂等 |
| `build\build.ps1` | **新增**：双架构单文件发布 → `Releases\`；`-Compress` / `-Only` / `-KeepTemp`；先清后产；输出体积摘要 |
| `build\_step8_smoke.ps1` | **新增**：发布态冒烟（启动耗时 / 内存 / 首启结构生成 / 日志 WARN·ERROR） |
| `build\_step8_board.ps1` | **新增**：发布态开板内存（`-Warmup` 可等常驻稳定后再开板） |
| `build\_step8_port.ps1` | **新增**：移植性（中文+空格 / 跨分区 / 改名 × 首启·二次 + config 只读降级） |
| `build\_step8_shot.ps1` · `build\_iconfp.ps1` | **新增**：开板截图取证 / exe 内嵌图标指纹提取 |
| `src\Cship\app.ico` · `dependencies\resources\icons\app.ico` · `app.png` | **重新生成**（135,808 B / 同 / 116,090 B，7 帧：16/24/32/48/64/128/256） |
| `CShips\`（整仓） | **新增**：git 仓库（源码 + build 脚本与夹具 + docs + LICENSE/README/.gitignore），首次提交 `f6b302f`（124 文件 / 26,419 行） |
| `分步提示词\08_*.md` | 交付根改 `Releases\`、新增 §8 源码仓库、§1 图标口径改"素材转制"、§2 压缩默认 false + 实测表、§3/§6/§7 路径同步 |
| `Releases\` | **产出**：`Cship.exe` 139.0MB + `Cship-x86.exe` 128.9MB + `dependencies\resources\`（**不含 config**） |

### 一、程序图标（§1）

- `build\makeicon.ps1` 由 `素材\CShip.png`（1132×940，非方形 → 补透明底正方形居中缩放）生成三处同源图标：
  `src\Cship\app.ico`（csproj `ApplicationIcon`）、`dependencies\resources\icons\app.ico`（托盘）、`app.png`（512，关于页 Logo）。
- **帧格式选择**：16~128 用 DIB(BMP) 帧、256 用 PNG 帧。实测 `System.Drawing.Icon` 请求 256 时只回 128——GDI+ 对**纯 PNG 帧**的取帧不完整；混合格式下 16/24/32/48/64/128 逐档精确命中（托盘只用 ≤48 不受影响；shell 走原生解析可正确取 256）。
- **幂等**：重复运行产物字节一致（先删旧产物；ICO 在内存组装，无中间文件落盘）。
- **替换流程实测（08 验收项）**：以 `folder.png` 为源临时替换 → `build.ps1 -Only x64` → exe 内嵌图标指纹 **801.88 → 1733.21**（已变化）→ 还原源图重跑 → 指纹**回到 801.88**。流程闭环。
- **踩坑记录（PowerShell 重载解析）**：`BinaryWriter.Write($f.Bytes)` 单参数调用在 `$f.Bytes` 为 `Object[]` 时**只写出 1 字节**（首版 ICO 因此只有 125 字节），改为先 `[byte[]]` 强转再用三参重载 `Write($bytes, 0, $len)` 后正常。**教训：PowerShell 调 .NET 含数组参数的重载时必须显式转换 + 显式三参。**

### 二、发布产物与参数取舍（§2）

- 布局（`Releases\`）：`Cship.exe`(x64) + `Cship-x86.exe`(x86，重命名) + `dependencies\resources\`（两架构共用一份）；`dependencies\config\` **不预置**，由程序首启自建。
- **压缩默认改为关闭**（原文默认开）——同口径实测（43 项桌面，同一台机）：

| 配置 | exe 体积 | 双击→悬浮窗 | 常驻 60s WS | 开板稳定 WS |
|---|---|---|---|---|
| `EnableCompressionInSingleFile=true` | 63.2 MB | 1267 / 1555 ms | 245.9 MB | 214.3 MB |
| **`false`（最终采用）** | **139.3 MB** | **827 ms** | **125.8 MB** | **138.2 MB** |

  压缩换来 -76MB 体积，代价是**常驻内存 +120MB、启动慢 440~730ms**（自解压缓存常驻）。依 08 §4「体积与启动耗时不可兼得时以启动耗时为先」+ 00 §0「体积是七维度末位、与用户体验冲突时让位」→ **默认不压缩**，`-Compress` 保留给需要小体积分发的场合。
- `SatelliteResourceLanguages=en` 保留：`Releases\` 内**无任何 `*.resources.dll` 附属资源目录**；`MessageBox` 按钮文本来自 `user32.dll`（系统语言），中文环境仍是"确定/取消"，**未牺牲中文体验**。
- `DebugType=none`：发布包内**无 `.pdb`**。`PublishTrimmed=false`、`InvariantGlobalization=false` 未动。

### 三、体积 / 启动 / 内存实测（§4 · 最终产物）

| 指标 | x64（`Cship.exe`） | x86（`Cship-x86.exe`） | 目标 |
|---|---|---|---|
| exe 体积 | **139.0 MB**（145,787,220 B） | **128.9 MB**（135,199,892 B） | 记录 |
| `dependencies\` | 0.3 MB（两架构共用） | 同左 | 记录 |
| 交付目录合计 | **268.2 MB** | | |
| 发布耗时（冷 / 增量） | ~25s / 1.6~2.7s | ~21s / 1.8~3.0s | 记录 |
| **双击 → 悬浮窗可见（首启）** | **853 / 881 ms** | **863 ms** | **< 3s ✅** |
| 二次启动 | 770~804 ms | — | — |
| 启动 3s 后 WS | 117.4 MB | 154 ~ 181.6 MB | — |
| **常驻 60s WS** | **125.8 MB** | **157.4 MB** | — |
| 空闲 60s WS（工作集整理后） | **14.1 MB** | **13.2 MB** | 不单调增长 ✅ |
| **空闲 CPU** | **0.21% 单核** | **0.18% 单核** | **≈ 0 ✅** |
| **开板峰值 / 稳定 WS** | **150.5 / 138.2 MB** | **192.4 / 194.2 MB** | **< 250MB ✅** |
| 首启结构生成 | `settings.json` `state.json` `iconcache\` `assets\` `logs\` 全部自建 ✅ | 同左（**与 x64 共用同一份**）✅ | — |
| 启动期日志 | 0 WARN / 0 ERROR | 0 WARN / 0 ERROR（首启） | 无 ERROR ✅ |

- 开板口径：**常驻 60s 后再开板**（`_step8_board.ps1 -Warmup 60`）。若在启动后 4s 内直接开板，测到的是自解压/预热峰值（x64 曾测到 219MB），不代表稳态。
- x86 首启 2714ms 的旧数据来自**压缩版**；不压缩后为 863ms。

### 四、移植性测试（§3 · `build\_step8_port.ps1`）

| 位置 | 首启 | 二次运行 | config 是否重置 | ERROR |
|---|---|---|---|---|
| `C:\Temp\Test 1\测试\`（中文+空格） | 1116 / 787 ms | 781 / 774 ms | **未重置**（`settings.json` 创建时间不变） | 0 |
| `D:\PortTest\`（跨分区） | 1109 / 787 ms | 770 / 774 ms | **未重置** | 0 |
| `D:\PortTest\我的收纳工具\`（**改名**） | 1080 / 780 ms | 804 / 802 ms | **未重置** | 0 |

- **x86 与 x64 共用同一份 config**：x64 首启生成 `Releases\dependencies\config\` 后，`Cship-x86.exe` 启动**未重建**任何结构，且新日志行**追加进同一个 `config\logs\cship_YYYYMMDD.log`**（`startup pid=15436`）——两架构共享设置与图标顺序已实测确认。
- **只读/受限目录降级**：对 `dependencies\config` 施加 `icacls /deny (W)` 后启动，**进程存活、悬浮窗正常出现（840ms）、无未捕获异常、0 ERROR**。
  - **踩坑 A**：若对**整个目录**施加 `deny (W)`，Windows 会**直接拒绝从该目录启动进程**（`Start-Process` 报"拒绝访问"）——那是 OS 行为而非程序缺陷；测"写入失败不崩溃"必须只收紧 `config` 子树。
  - **踩坑 B**：`icacls` 的账户参数**必须带机器名前缀**（`$env:COMPUTERNAME\$env:USERNAME`）——只写 `YuHeng:` 会被解析成机器名 `YUHENG\`（空账户），deny **静默失效**（首轮测试因此是假通过，已修正重测）。
- **绝对路径残留自查**：`rg "D:\\" Releases\dependencies` **0 命中**；对两个 exe 做二进制扫描 `rg -a "D:\\AiAgent"` **0 命中**。

### 五、最终回归验收（§5 · 对照 `产品设计简要说明.txt`）

> 逐条填"结论 + 证据"。**已被后续批次修订的条目按 STEP_LOG 最终口径验收**。
> 证据来源缩写：**08**=本步实测、**07/06/05/04/03/02**=对应步骤完成记录。

| 条目 | 结论 | 证据 / 备注 |
|---|---|---|
| L1 免安装 exe，作品名 Cship | ✅ | 08：`Releases\` 双架构单文件，无安装程序 |
| L2 双击运行 / 依赖在 dependencies / 常驻后台 / 托盘隐藏区可见 | ✅ | 08：双击即起（853/863ms），`dependencies\` 在 exe 旁，托盘常驻 |
| L3 悬浮窗三层错开叠加；悬停丝滑拉大 | ✅ | 02/03；08 截图取证 |
| L4 单击：间距+前后距离瞬间拉大、透明度 100% 完全消失 | ✅ | 02/03；08 实测点击悬浮窗 → 收纳板出现（窗口 293,-10,1334×816） |
| L5 收纳板开=向外扩大 / 关=倒放；≥5 套预设 | ✅ | 03：`BoardAnimPresets` 共 **5 套**（展开/吐出/贴边滑入/弹跳/柔淡） |
| L6 悬停图标上跳、离开倒放 | ✅ | 02；07 `_step7_reveal_smoke.ps1` 扫掠 0 WARN/ERROR |
| L8 悬浮窗可拖动、不可出屏/进任务栏、首启顶部居中 | ✅ | 01/02；08 实测首启位置 895,-8（1920 屏居中） |
| L9 收纳板三层结构；中/下层圆角；可调长宽有 min/max | ✅ | 02；min 已改 **160×107**（2026-10-05 用户裁决） |
| L10 陈列桌面全部文件（图标+名称）；齿轮+红点右上；垃圾桶拖删 | ✅ | 02；**垃圾桶位置已由后继批次按用户要求从右下角移至右上角顶栏**（清单二·任务6）；08 截图确认右上角三控件（垃圾桶/齿轮/红点）齐全；拖删走系统回收站 |
| L11 背景板默认亚克力；多材质可换 | ✅（口径已改） | 06：材质 **4 档**（无/亚克力/柔化/瞎眼防弹玻璃）；**默认已改为"无"**（2026-10-06 用户裁决） |
| L12 下层自定义图片 | ✅ | 06：`personal.boardBg` + `config\assets\` 副本 |
| L14 空白右键：排序方式＞/刷新/新建＞ | ✅ | 02 |
| L15 图标右键：打开/所在位置/固定到开始屏幕/复制/重命名/属性 | ✅ | 02 |
| L16 两级菜单跟随全局界面颜色与收纳板材质 | ✅ | 02/06：`GlassPopup` 自绘 |
| L17 Esc 收起收纳板（路由：设置窗 > 收纳板） | ✅ | 07 复核 `ShortcutRouter` 层级，分类页不占层 |
| L18 分类页顶部按钮行、宽度自适应；默认 全部/软件/文件夹/文件 | ✅ | 04；08 截图确认按钮行与"全部"选中态 |
| L20 溢出折叠为"更多"，下拉可收；resize/改名/换语言实时重算 | ✅ | 04；批次八修正二次展开时序 |
| L21 右键分类按钮：移除/重命名；"全部"置灰；内置项可找回 | ✅ | 04；设置子开关（L49） |
| L22 图标拖到分类按钮=加入分类（复制语义） | ✅ | 04 |
| L23 分类视图与主收纳板共用交互 | ✅ | 04：共用网格/菜单/hover/指示灯/弹跳/拖删 |
| L25 设置仿 mac；左项右子项；不可调大小可移动 | ✅ | 05 |
| L28 界面颜色 五档 + 预览图 + 切换过渡 | ✅（口径已改） | 05/06：现为 **4 档**（白皙/暗夜/自动/跟随系统）——**"透明"档已删**（2026-10-06 用户裁决）；预览卡已重绘（批次十五） |
| L29 开机自启动开关（打包态写 exe 绝对路径） | ✅ | 07：`Environment.ProcessPath` 运行时解析 + `--autostart` + 写入后回读校验；端到端"注销重登"留人工 |
| L30 刷新率 60/90/120/165 折叠项 | ✅ | 05；启动日志实测"动画帧率档 = 120 fps" |
| L31 语言 简体中文/English 折叠项 | ✅ | 05；07 `i18n_audit.py` 176 键两语言对称、0 死键 |
| L32 字体预设折叠项 | ✅ | 05：**7 套**（微软雅黑/等线/宋体/黑体/楷体/Segoe UI/Consolas） |
| L33 隐藏桌面图标开关（崩溃/explorer 重启后可恢复） | ✅ | 07 端到端：杀进程→重启→强制恢复可见并气泡告知；`TaskbarCreated` 广播→812ms 后重放隐藏意图 |
| L35 悬浮窗大小滑轨 | ✅ | 05 |
| L36 图标样式 原图标 / 隐藏-hover 渐显 | ✅ | 05/07（hoverReveal 扫掠实测） |
| L37 图标不透明度滑轨 0~100 默认 100 | ✅ | 05 |
| L38 图标下方显示文件名开关 | ✅ | 05；08 截图确认名称显示 |
| L39 悬停显示文件名开关（黑底白字虚化边） | ✅ | 05 |
| L40-42 图标大小/行间距/列间距滑轨 | ✅ | 05；07 已做 16ms 合并广播（遗留热点④收口） |
| L43 屏幕选择 Auto/枚举（含型号名，失败回退） | ✅ | 07：EDID 路线实测型号 `P24A2G`；取不到回退基础信息，不阻塞 UI |
| L45 弹出方向四选 + 锚边/可调边逻辑 | ✅ | 05/02 |
| L46-48 显示软件/文件/文件夹小按钮（保持亮起） | ✅ | 05 |
| L49 禁用分类页母开关 + 三子开关 | ✅ | 05 |
| L53 图标白色圆角遮罩统一大小形状、不越界 | ✅ | 05；08 截图确认统一遮罩 |
| L54 锁定图标拖动 | ✅ | 05 |
| L55-56 双击打开软件文件/文件夹开关 | ✅ | 05 |
| L57 弹跳打开动画 | ✅ | 03/05 |
| L58 运行指示灯（绿点在图标上方几像素） | ✅ | 05；07 已后台化 + PID 增量缓存 |
| L59 悬浮窗锁定 | ✅ | 05 |
| L60-61 导出/导入配置 txt | ✅ | 05/批次十五/十六：往返 51 项；结构非法跳过并点名、数值越界夹回（决策 288） |
| L63 弹出方式折叠项（多预设） | ✅ | 03：5 套动画预设 |
| L64 材质三选 + 预览图，影响除悬浮窗外全局 | ✅（口径已改） | 06：现为 **4 档**（含"无"档），预览图已重绘 |
| L65 自定义背景图（副本存储、删源不影响、可框选范围） | ✅ | 06：`config\assets\` 内容 hash 命名 + `CropPickerWindow` |
| L66-71 悬浮窗自定义：预设（材质+三层颜色全色盘）/ 自定义（三层图片） | ✅ | 06/批次十六：三层不透明度为常驻行、对预设同样生效且不改写原始配方；层滑轨与层图共用同一份对调（决策 286/289） |
| L72 关于页占位 | ✅ | 05/06：含 Logo（`app.png`）与产品信息 |
| L73 托盘右键菜单：偏好设置/复位/关闭/重启，跟随主题材质（自绘） | ✅ | 06：自绘 `GlassPopup`，非 WinForms 菜单 |
| L74 程序图标（可替换） | ✅ | 08：`makeicon.ps1` + `assets\CShip.png`；替换流程实测闭环（指纹 801.88→1733.21→801.88） |
| L75 所有未提及 UI 跟随主题与材质 | ✅ | 06：主题字典 + 材质通道为注册式扩展点，各级菜单/弹窗统一走 `SettingsPalette` / `MaterialBackground` |
| L76 目录树：主程序框架 + dependencies 依赖配置 | ✅ | 08：`Releases\` 顶层仅 exe ×2 + `dependencies\`；开发态 `src\Cship\dependencies\resources` 由 csproj 复制到输出目录 |
| L77 无硬编码绝对路径，整目录任意位置可运行 | ✅ | 08：三路径（中文+空格/跨分区/改名）实测通过、config 不重置；`rg "D:\\"` 0 命中；`Paths.DepsDir()` 单一出口 |
| L79 所有视觉跳跃式变化均有过渡动画 | ✅ | 00 §10 铁律 8；06/07 各批次逐项落地 |

> 未单列的原文行（L7/L13/L19/L24/L26-27/L34/L44/L50-52/L62/L78）为标题行或已被上述条目覆盖（L13 圆角并入 L9、L50-52 并入 L49、L62 并入 L60-61、L78 并入 L77）。

### 六、兼容性验收（§6）

- ✅ **交付布局**：`Releases\` 顶层 = `Cship.exe` + `Cship-x86.exe` + `dependencies\`；`dependencies\` 内首启前只有 `resources\`。
- ✅ **自包含可移动**：中文+空格、跨分区、**改名**三种位置均正常运行且 **config 不重置**。
- ⬜ **纯净系统**（无 .NET 运行时的干净 VM/账户）：本机装有 .NET SDK，**无法物理验证**；self-contained 单文件已内嵌运行时，留人工。
- ✅ **WOW64**：x86 包在 64 位系统运行正常（悬浮窗/开板/图标提取/内存均正常），且与 x64 **共享同一份 config**。
- ✅ **中文 + 空格 + 跨分区路径**：全部正常。
- ⬜ **Win10 1607+ / Win11**：本机为 Win10 19045；Win11 未测（材质走系统合成器通道，Win11 由系统原生增强），留人工。
- ⬜ **混合 DPI 双屏**：本机单显示器 1920×1080@100%，**无法物理验证**；代码审计：全部换算点走 `GlassWindow.DpiScaleX/Y` 与 `SystemBridge.ScaleAt`，无手写 `/96`、`*96`（07 已全量核对）。
- ✅ **无第三方 NuGet 依赖**：`dotnet list package` 仅 SDK 自动引用的 `Microsoft.NET.ILLink.Tasks`；`Releases\` 内无第三方 dll。
- ✅ `rg "D:\\" Releases` **0 命中**；`rg "net9\.0|net10\.0" src` **0 命中**（仍锁 `net8.0-windows`）。
- ✅ **发布包无 `.pdb`、无非英文附属资源程序集**。

### 七、源码仓库 `CShips\`（§8）

- `git init` + 首次提交 `f6b302f`：**124 文件 / 26,419 行**，`git status` 干净。
- 结构：`LICENSE`(MIT) · `README.md`（顶部居中：`assets\CShip.png` + 作品名 Cship）· `.gitignore` · `Cship.sln` · `assets\`（图标源图）· `src\Cship\`（源码）· `build\`（构建脚本 + 端到端测试 + 自检工具 + C# 夹具 + `build\README.md` 索引）· `docs\`（产品设计简要说明 + 分步提示词 00~08 与 STEP_LOG）。
- 忽略规则已生效：`bin` / `obj` / `dependencies\config` / `Releases` / 测试临时目录均未入库（暂存校验 0 命中）。
- 仓库内脚本以"仓库根"为基准（`build\build.ps1` 产出到 `..\Releases\`），从 `CShips\` 根调用即可重建产品。

### 补充决策（续 289）

290. **程序图标走"素材转制"而不是"脚本自绘"**：原文要求用 `System.Drawing` 画三层错开方板，但用户已提供真实产品图
     `CShip.png`（步骤 06 附加需求即据此落地）。**观感优先**——真实素材与产品本体语义一致，且用户可随时覆盖源图替换。
     `makeicon.ps1` 的职责因此收窄为"源图 → 多尺寸 ICO 的可靠转制"（补正方形/居中/多帧/幂等），不负责绘图。
291. **发布默认不压缩（`EnableCompressionInSingleFile=false`）**：同口径实测压缩只换 -76MB 体积，代价是
     **常驻内存 +120MB、启动慢 440~730ms**（自解压缓存常驻工作集）。依 08 §4「启动耗时优先于体积」与
     00 §0「体积是七维度末位、与用户体验冲突时让位」→ 默认关闭；`-Compress` 作为显式选项保留。
     **该取舍必须随实测数字一起记录**，否则后人只看到"体积大了 76MB"会误以为回退。
292. **交付布局拆成"发行版"与"源码仓库"两个落点**（2026-10-06 用户裁决）：`dock++\Releases\` 只放**功能产品本身**
     （两个 exe + `dependencies\resources\`，无任何多余文件，`config` 由产品首启自建）；
     `dock++\CShips\` 是**源码仓库兼 git 仓库**（源码 + 构建与测试脚本 + MIT + README + 文档）。
     开发工作区 `dock++\` 不再承担交付职责——它的临时脚本、素材、截图不入库，`CShips` 只收"能重建产品/能复现验收"的东西。
293. **垃圾桶位置的回归口径以"后继用户裁决"为准**：原文 L10 写"垃圾桶在右下角"，但用户在后继批次明确要求
     **移入右上角顶栏并缩小**（清单二·任务6）。回归表按**最终口径**验收（右上角三控件），并在表内注明改动来源——
     回归验收是"对照产品最终形态"，不是"对照原始文档字面"，凡有用户明确裁决的偏差一律以裁决为准并留痕。
294. **"只读介质降级"的正确测法：只收紧 `dependencies\config` 子树**：对整个目录 `deny (W)` 时，Windows 会
     **直接拒绝从该目录启动进程**（`Start-Process` 报拒绝访问）——这是 OS 行为，测不出程序自身的降级能力；
     只 deny `config` 才能命中"写 config/写日志失败"这一条路径。另：`icacls` 的账户**必须带机器名前缀**
     （`COMPUTERNAME\USERNAME`），只写 `USERNAME:` 会被解析成机器名 `COMPUTERNAME\`（空账户）而**静默失效**。
295. **ICO 帧格式用 DIB + PNG 混合**：GDI+（`System.Drawing.Icon`）对**纯 PNG 帧**的 ICO 取帧不完整
     （实测请求 256 回退到 128）。故 16~128 写 DIB(BMP) 帧、256 写 PNG 帧——托盘（≤48）与 shell（原生解析）
     都能取到正确帧，同时 256 帧仍享受 PNG 压缩。**替换图标后必须验证 exe 内嵌图标指纹变化**，不能只看文件被覆盖。

### 给后续步骤的接口提示

- **交付根**：发行版 = `dock++\Releases\`（自包含，整体可移动/改名）；源码仓库 = `dock++\CShips\`（git）。
  重建流程：`CShips\build\makeicon.ps1`（可选）→ `CShips\build\build.ps1` → 产出到 `..\Releases\`。
- **改图标**：覆盖 `CShips\assets\CShip.png` → 重跑 `makeicon.ps1` → 重跑 `build.ps1`。三处引用（csproj / 托盘 / 关于页）自动同源。
- **要小体积分发**：`build.ps1 -Compress`，但请连同"内存 +120MB / 启动慢 440~730ms"一起告知使用者（决策 291）。
- **回归验收口径**：凡原始文档条目与后继用户裁决冲突，**以裁决为准并在表内注明来源**（决策 293）；新写回归表时沿用本节的"结论 + 证据来源缩写"格式。
- **发布态性能基线**（43 项桌面 · Win10 19045 · 1920×1080@100%）：双击→悬浮窗 **< 1s**、常驻 60s WS **< 160MB**、
  空闲 WS **< 20MB**、空闲 CPU **< 0.3% 单核**、开板稳定 WS **< 200MB**。明显劣于此值时应先查是否误开了 `-Compress`。
- **测试脚本**：`build\_step8_*.ps1` 四个（smoke / board / port / shot）可重复运行，`-Exe <路径>` 指向任意发布产物；
  它们都会**真实启动程序并操作窗口**，跑前请保存工作。

### 留人工验证项（自动化未覆盖，如实记录）

1. **托盘"关闭"的干净退出路径**：托盘图标在本机"隐藏图标"浮层内（Win10 溢出面板不响应 `SendInput`），无法脚本化点击 →
   自动化一律用强杀退出，日志因此每次都会出现一条"检测到上次运行未正常退出"的 WARN（**预期**）。
   请羽点一次托盘"关闭"，确认下次启动**无**该 WARN。
2. **纯净系统**：需要一台**没有装 .NET 运行时**的干净 VM 或干净账户，双击两个 exe 各一次。本机装有 SDK，无法模拟。
3. **Win11**：本机 Win10 19045，未测。预期材质由系统原生增强、观感一致且无黑底。
4. **混合 DPI 双屏**：本机单显示器，无法物理验证跨屏拖动/锚边/菜单定位。
5. **自启动"注销重登"端到端**：注册表值与命令形态已实测（07），但"注销重登后静默常驻"需真实注销一次。
6. **大桌面（500+ 项）发布态内存**：07 在开发态测得 543 项开板 288~310MB（超 250MB，属压力场景的已知边界，决策 285）；
   发布态未重测该规模。
7. **发布态的完整 UI 功能抽查**：本步做了开板 + 截图取证（分类行/齿轮/红点/垃圾桶/图标网格/统一遮罩均正常），
   但右键菜单、拖删、导出导入、自启动写键等交互项的证据来自开发态（02/05/07）——发布态与开发态共用同一份源码与资源，
   未单独重跑全部交互用例。

### 已知限制（§7）

1. **材质**：Win10 上系统级亚克力/DWM backdrop 对分层窗口不可用，程序走**系统合成器模糊通道**
   （`SetWindowCompositionAttribute` / `ACCENT_ENABLE_BLURBEHIND`）实现玻璃观感；Win11 上同一通道由系统原生增强。
   个别系统版本或显卡驱动下可能回退为纯色板——**观感会变，但不会黑底、不会崩**（能力探测优先于版本号）。
2. **动画帧率**：WPF 动画受垂直同步与渲染线程限制，高刷屏实际帧率可能达不到设置档位。
3. **x86 包内存上限**：32 位地址空间约 2~4GB，超大桌面下更容易吃紧。
4. **超大桌面内存**：常驻内存随桌面项数近似线性增长（WPF 每项可视元素成本）；543 项时开板 288~310MB（决策 285）。
   250MB 这一档的成立前提是"正常桌面规模"。
5. **单文件压缩的取舍**：开启后体积约减半，但**常驻内存 +120MB、启动慢 440~730ms**（自解压缓存常驻）。
   本交付默认关闭（决策 291）。
6. **混合 DPI 双屏**：实现按逐屏 DPI 换算（无手写 96），但未在双屏环境物理验证。

### 交付说明（§7）

- **怎么放**：`Releases\` 是**自包含目录**，整体复制/移动到任意位置（含中文、空格、跨分区）都能跑，也可以整个改名
  （如 `我的收纳工具\`）。不需要安装，不写系统区（自启动项除外，可选）。
- **怎么运行**：双击 `Cship.exe`（64 位）或 `Cship-x86.exe`（32 位）。首次运行会在 `dependencies\` 下自建
  `config\`（`settings.json` / `state.json` / `assets\` / `iconcache\` / `logs\`）。两个 exe **共用同一份 config**：
  换架构不丢设置、不丢图标顺序。
- **怎么换图标**：覆盖 `CShips\assets\CShip.png` → 重跑 `makeicon.ps1` → 重跑 `build.ps1`（三处图标自动同源）。
- **怎么完全清理**：删掉整个 `Releases\` 目录；若开过开机自启，再删注册表
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `Cship` 值（或先在设置里关掉自启动开关）。
  没有其他残留（不写 AppData、不装服务、不注册驱动）。
- **x86 与 x64 怎么选**：默认用 `Cship.exe`；只有需要在 32 位系统运行、或要与 32 位程序共享环境时才用 `Cship-x86.exe`。
- **交付目录体积清单**：

| 项 | 体积 |
|---|---|
| `Cship.exe`（x64 单文件，未压缩） | 139.0 MB（145,787,220 B） |
| `Cship-x86.exe`（x86 单文件，未压缩） | 128.9 MB（135,199,892 B） |
| `dependencies\resources\`（两架构共用） | 0.3 MB |
| **`Releases\` 合计** | **268.2 MB** |
| 对照：`CShips\` 源码仓库（含文档） | 约 4 MB（不含 `.git`） |
| 对照：若开启 `-Compress` | 63.2 + 57.9 + 0.3 ≈ **121.4 MB**（内存与启动代价见上） |

---

## 步骤08 修订 · 两档交付（2026-10-06 · 用户新规格：完整版 / 精简版）

- **用户要求（原文三条）**：
  1. "纯净系统打开即用，但不用包含完整 net8，需要哪个依赖保留哪个"；
  2. "最大程度压缩体积，点开任意个 exe 后跳出弹窗，从互联网获取 net8 等非本项目原创的内容，进度条完成后方可使用，预留多个下载途径"（用户："构建前项目只有 5MB，构建后 300+ 给我吓哭了"）；
  3. 源码仓库需改动的地方参考 1；1、2 分文件夹放 `Releases\` 里。
- **交付形态**：`Releases\完整版\`（离线即用）+ `Releases\精简版\`（首启联网获取运行时）。

### 一、技术事实先立住（为什么不是"把运行时删到只剩用到的"）

- **WPF 不支持运行时裁剪**：`PublishTrimmed=true` 对 WPF 是官方未支持状态（反射/标记扩展场景运行时崩溃），
  00/08 禁止；`InvariantGlobalization=true` 可省 ICU 但会破坏中文文件名排序（产品核心场景），同样禁止。
  因此"需要哪个依赖保留哪个"在**离线即用**这一档没有官方实现路径——.NET 8 的
  CoreCLR + WPF + BCL + ICU 是不可分割的整体，**压缩后的 63/58 MB 就是"纯净系统打开即用"的正路下限**。
- 极小体积的正路是**framework-dependent**：主程序只剩 ~1 MB，运行时按需获取——这正是精简版（见下）。

### 二、完整版（`Releases\完整版\` · self-contained，默认压缩）

| 项 | 数值 |
|---|---|
| `Cship.exe`（x64，压缩） | 64,707 KB（63.2 MB） |
| `Cship-x86.exe`（压缩） | 59,326 KB（57.9 MB） |
| 合计（含 resources 0.3MB） | **121.4 MB**（此前未压缩单档为 268.2 MB） |
| 双击 → 悬浮窗可见（压缩自解压） | **1755 ms**（< 3s ✅） |
| 开板稳定 WS | 334.9 MB（压缩自解压常驻开销，已知边界） |
| 启动期日志 | 0 WARN / 0 ERROR |

- **完整版默认压缩**（决策 296 取代 291 的"默认不压缩"）：两档交付后完整版的定位是"网络不可用时的离线兜底"，
  用户当前的显式诉求是体积（"吓哭了"）→ 体积优先；实测压缩代价（常驻内存 +120MB、启动慢 440~730ms，
  决策 291 的数据仍成立）以"在意内存请用精简版"的口径写进 README 与 08 文档。`-NoCompress` 保留给需要
  完整版跑满性能的场合。

### 三、精简版（`Releases\精简版\` · framework-dependent + 引导器）

**布局（初始合计 2.5 MB）**：

```
精简版\
├─ Cship.exe / Cship-x86.exe            ← 引导器（net48 WinForms，157 KB；同一 AnyCPU 程序集改名两次）
├─ Cship.app.exe / Cship.app-x86.exe    ← 主程序（framework-dependent 单文件，975 / 948 KB）
└─ dependencies\resources\              ← 预置（与完整版同一套资源）
```

**引导器（`src\CshipBootstrapper`，新增项目）**：

1. 查找顺序：本地 `dependencies\runtime\{arch}\dotnet`（此前下载）→ 系统安装位（`%ProgramFiles%\dotnet`，
   x86 看 ProgramFiles(x86)）→ 都没有才弹下载窗。找到即启动主程序并退出：本地运行时注入
   `DOTNET_ROOT_X64/X86`（.NET 6+ apphost 支持），系统运行时不注入。
2. **下载 = 两个官方 zip 叠加解压**：官方没有"桌面完整根"zip——WindowsDesktop zip 顶层只有
   `shared\Microsoft.WindowsDesktop.App`（**缺 apphost 启动必需的 host\fxr**，实测确认）；
   基础 runtime zip 提供 `host\fxr + Microsoft.NETCore.App`。两包**覆盖式**解压到同一目录
   （两包的 LICENSE.txt 同名会冲突，逐条 ExtractToFile(overwrite) 而非 ExtractToDirectory），
   构成完整 DOTNET_ROOT 布局（实测落盘 160.1 MB）。
3. **多源自动轮询**（实测三源均可直连）：`builds.dotnet.microsoft.com` · `dotnetcli.azureedge.net` ·
   `dotnetcli.blob.core.windows.net`；前源失败自动切下一个，窗体上"更换下载源"按钮可立即中断当前源。
   版本优先在线查 8.0 线元数据（releases.json 的 `latest-runtime`），失败退回内置常量 8.0.31。
4. **免管理员、免安装、零系统残留**：下载到 %TEMP%、解压到程序目录，全程 asInvoker、不写注册表；
   引导器面向 **net48**（Win10/11 自带 .NET Framework 4.8），在没有 .NET 8 的机器上也能跑起来弹窗。
5. **排障开关**：`CSHIP_FORCE_BOOTSTRAP=1` 强制跳过检测直接进下载窗（重装运行时/自动化测试用）。
6. 主程序缺失 → 错误弹窗退码 1；全部源失败 → 提示"改用完整版（离线、免下载）"。
7. 引导日志：`dependencies\runtime\bootstrapper.log`（覆盖式小日志，只记录引导阶段）。

**端到端实测（`build\_step8_lite.ps1`，含真实网络下载 68 MB）**：

| 指标 | 数值 |
|---|---|
| 双击 → 引导窗体可见 | **816 ms** |
| 在线版本查询 | 8.0.31（2026-09-08，8.0 线最新） |
| **下载 68 MB（双包）+ 覆盖式解压 + 启动主程序** | **8.9 s**（带宽相关，进度条/速度/源实时可见） |
| runtime 落盘 | 160.1 MB（`dependencies\runtime\x64\dotnet`） |
| 主程序悬浮窗 | 可见 ✅（框架依赖进程 WS 123.8 MB，**无自解压开销**） |
| 二次启动 | 引导器**直接转发**（无窗体），主程序 2.0 s 内启动 ✅ |
| 引导日志 | 逐条：进入引导 → 目标版本 → 运行时就绪 → 使用本地运行时 ✅ |

### 四、交付终态（`Releases\`）

```
Releases\
├─ 完整版\   121.4 MB   Cship.exe(63.2M) + Cship-x86.exe(57.9M) + dependencies\resources
└─ 精简版\      2.5 MB   Cship.exe(157K) + Cship-x86.exe(157K) + Cship.app.exe(975K)
                         + Cship.app-x86.exe(948K) + dependencies\resources
```

- 测试生成的 `完整版\dependencies\config\`、`精简版\dependencies\{config,runtime}\` 已清理，
  交付恢复"仅产品本身"；精简版首次运行时引导器下载运行时、主程序自建 config。
- **两档共用同一套 config 结构**（主程序是同一份源码、同一套 `Paths`），用户在两档间迁移设置无需改动。

### 五、源码仓库（`CShips\`）同步

- 新增 `src\CshipBootstrapper\`（csproj / app.manifest / App.config / Program.cs / RuntimeLocator.cs /
  KnownSources.cs / BootstrapForm.cs）。
- `build\build.ps1` 改双档（`-Mode full|lite|both`、`-NoCompress`）；新增 `build\_step8_lite.ps1`。
- `README.md` 重写"快速开始/从源码构建"，"已知限制"补精简版联网与完整版压缩两条；
  `build\README.md` 同步；`.gitignore` 增 `**/dependencies/runtime/`。

### 补充决策（续 295）

296. **两档交付取代单档（完整版默认压缩）**：用户体积诉求显式压倒此前"默认不压缩"——完整版是"离线兜底"档，
     默认 `-Compress`（121 MB），精简版承载"小体积 + 性能"定位（124 MB 内存 vs 完整版 240 MB）。
     决策 291 的实测数据（压缩使常驻内存 +120MB、启动慢 440~730ms）依然成立并写进两处文档，
     作为两档选择的判断依据；`-NoCompress` 保留。**交付目录从单层变两层：`完整版\` + `精简版\`。**
297. **"需要哪个依赖保留哪个"在离线档没有官方实现路径，如实告知而不是硬裁**：WPF 不支持 `PublishTrimmed`
     （官方未支持，运行时崩溃），`InvariantGlobalization` 被中文排序场景锁死。离线即用的正路下限 =
     压缩单文件 63/58 MB。对"极小体积"的诉求，正确响应是**换交付形态**（framework-dependent）而不是
     破坏运行时完整性——后者会让"绝不黑底、绝不崩溃"的铁律破产。
298. **引导器必须能在"最裸的机器"上跑起来**：目标机器可能没有 .NET 8，故引导器面向 net48
     （Win10/11 自带 .NET Framework 4.8，WinForms，157 KB 单文件）。自身按**文件名**区分架构语义
     （含 `x86` → 管 x86 主程序），同一 AnyCPU 程序集改名两次即得两个入口，无需分架构编译。
299. **运行时获取 = 两个官方 zip 覆盖式叠加**：官方没有"桌面完整根"zip（WindowsDesktop zip 缺 host\fxr，
     实测确认）；基础 runtime zip 提供 `host\fxr + Microsoft.NETCore.App`，桌面包提供
     `Microsoft.WindowsDesktop.App`，两包先后解压到同一目录即完整 DOTNET_ROOT 布局。
     解压必须**逐条覆盖**（两包许可文件同名，ExtractToDirectory 会因已存在文件抛异常），
     完成后整体 `Directory.Move` 就位（staging 模式防半成品），并校验 host\fxr 与 8.x 框架目录存在。
     运行时注入 `DOTNET_ROOT_X64/X86`——绿色、免管理员、零注册表。
300. **版本策略：在线查询优先，内置常量兜底**：构建日（2026-10-06）8.0 线最新为 8.0.31；
     引导器运行时查 releases.json 的 `latest-runtime`（正则提取，net48 无 System.Text.Json），
     查询失败退回内置常量。框架判定只认 8.x（主程序锁 net8.0，RollForward 只在 8.0 线内滚动）。
301. **引导器是一次性网关，不是常驻组件**：找到可用运行时即启动主程序并退出，绝不驻留；
     `CSHIP_FORCE_BOOTSTRAP=1` 是唯一的强制重装通道（排障/自动化测试用），隐藏且不干扰正常路径。

### 给后续步骤的接口提示

- **重建交付**：`CShips\build\build.ps1`（两档全出，~25s）；单档加 `-Mode full|lite`；
  完整版关压缩加 `-NoCompress`。产物永远落在 `dock++\Releases\{完整版,精简版}\`。
- **改引导器**：源码在 `src\CshipBootstrapper`；它面向 net48，**不要使用 C# 8+ 语法与 .NET 8 API**
  （当前 LangVersion=7.3）；下载源/版本常量在 `KnownSources.cs`。
- **精简版测试**：`build\_step8_lite.ps1`（真实下载 68MB，跑前确认网络）；日常冒烟用
  `CSHIP_FORCE_BOOTSTRAP=1` 强制走引导窗。
- **发新补丁版后**：仅需更新 `KnownSources.FallbackVersion`（在线查询会自动追新，常量只是离线兜底）。

### 留人工验证项（本次修订新增）

1. **真正无 .NET 8 的纯净机器上走精简版引导**：本机装有 SDK，引导链路是用 `CSHIP_FORCE_BOOTSTRAP=1`
   强制走通的（下载/解压/启动全真实），但"系统检测分支为空"的判定在纯净机上需复核一次。
2. **32 位精简版**：`Cship-x86.exe`（引导器）+ `Cship.app-x86.exe` 的 x86 全链路逻辑与 x64 完全同构，
   本机只实测了 x64 链路（x86 主程序 WOW64 已在完整版验证）。
3. **慢速/受限网络下的换源体验**：三源轮询与"更换下载源"按钮已实现，多源全挂时的提示与"改用完整版"
   引导文案待真实弱网复核。
