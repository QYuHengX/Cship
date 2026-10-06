# build — 构建与测试脚本

所有脚本都用 **Windows PowerShell 5.1** 或 **PowerShell 7** 运行，从**仓库根目录**调用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build\<脚本名>.ps1 [参数]
```

> 脚本文件带 UTF-8 BOM（含中文注释，PowerShell 5.1 无 BOM 时会按 ANSI 解码而报语法错）。

## 构建

| 脚本 | 作用 |
|---|---|
| `makeicon.ps1` | 由 `assets\CShip.png` 生成三处同源图标：`src\Cship\app.ico`（exe/任务栏）、`dependencies\resources\icons\app.ico`（托盘）、`app.png`（关于页 Logo）。幂等，可重复运行。替换图标 = 覆盖源图后重跑本脚本 + `build.ps1` |
| `build.ps1` | 双架构单文件发布，产出到仓库**上一级的 `Releases\`**。默认**不压缩**（压缩省 76MB 体积但常驻内存 +120MB、启动慢 45%，见脚本头实测表）；`-Compress` 可开启 |

## 自检

| 脚本 | 作用 |
|---|---|
| `i18n_audit.py` | 文案自检：中英两语言键对称、代码中 `I18n.Tr("字面量")` 引用全部存在、无零引用死键。退出码即结论 |
| `sync_defaults.py` | 默认值同步自检：`SettingsStore.DefaultRaw` 与散落各处的兜底字面量不得出现双口径 |
| `_iconfp.ps1` | 提取 exe 内嵌图标的尺寸与像素指纹（用于验证"换 ico 重打包"确实生效） |

## 端到端测试

这些脚本会**真实启动程序**并操作窗口/桌面，请先保存工作。

| 脚本 | 覆盖 |
|---|---|
| `_step7_probe.ps1` | 底层探针（被其他脚本调用）：`screens` 显示器枚举 / `icons` 桌面图标显隐状态 / `win <pid>` 可见顶层窗口数 / `rects <pid>` 窗口矩形 / `click` `sweep` 精确点击与扫掠 / `taskbar` 广播 `TaskbarCreated` / `display` 广播 `WM_DISPLAYCHANGE` |
| `_step7_lifecycle.ps1` | 整机生命周期：启动耗时、内存、强杀恢复、`TaskbarCreated` 重放、配置损坏自愈 |
| `_step7_icons_e2e.ps1` | 隐藏桌面图标的崩溃恢复端到端 |
| `_step7_reveal_smoke.ps1` | 悬停渐显（hoverReveal）扫掠 |
| `_step7_bigdesk.ps1` | 大桌面（批量建/删 100+ 文件）一致性 |
| `_step8_smoke.ps1` | **发布态**冒烟：双击→悬浮窗可见耗时、内存、首启结构生成、日志 WARN/ERROR 统计。`-Exe <路径>` 指定发布产物 |
| `_step8_board.ps1` | **发布态**开板内存：启动 →（可 `-Warmup 60` 等常驻稳定）→ 点击悬浮窗开板 → 测基线/峰值/稳定工作集 |
| `_step8_port.ps1` | 移植性：中文+空格路径 / 跨分区 / 改名目录下的首启与二次运行（config 不重置），以及 `config` 只读时的降级行为 |
| `_step8_shot.ps1` | 发布态开板截图取证 |

## 测试夹具（C#）

| 目录 | 作用 |
|---|---|
| `_uitest\` | 无 UI 的自检夹具：配置导入导出往返与非法值处理、主题预览卡、悬浮窗各层不透明度、层滑轨与板的映射。`run.ps1` 负责组装并运行 |
| `_step7test\` | 把真实的 `SystemBridge.cs` / `Autostart.cs` 编进来单独验证（显示器型号、自启动写键回读、图标显隐查询） |

> `_uitest\run.ps1` 会强制用新构建的产物覆盖夹具目录里的 `Cship.dll`——夹具对程序集的 CopyLocal **不会**因主产物重建而自动刷新，否则会跑出"旧代码"的假失败。
