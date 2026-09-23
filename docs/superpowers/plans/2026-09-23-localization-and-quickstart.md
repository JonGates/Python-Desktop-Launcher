# Localization, Toast and Quickstart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 收紧设置页、引入不占位错误 Toast，实现无损中英切换及双语快速使用文档。

**Architecture:** 原生 WPF 覆盖层承载通知；Core 提供不依赖 WPF 的资源、诊断及偏好存储，Desktop 提供动态绑定适配。语言改变只刷新启动器文本，不重建视图或执行环境。

**Tech Stack:** C# / .NET 10 / WPF；现有可执行规格、原生 UI smoke、PowerShell 发布工具，不新增第三方依赖。

**Spec:** `docs/superpowers/specs/2026-09-23-localization-and-quickstart-design.md`

## Global Constraints

2026-09-23 用户追加授权：全部完成后合并到 main，删除本次功能分支，推送并发布 v1.0.0，优先提供 x64 / x86。此明确授权取代下方原先“不推送、不发布”的默认限制；不删除无关分支或改写已有 Release。

- 保留 Windows 原生 WPF、单项目、多启动动作结构。
- Core 继续不能引用 WPF。
- 切换语言不写 launcher.yaml、不改变配置脏状态、不重建主窗口、不终止或重新启动任务和终端。
- 没有明确要求时不推送、不发布、不替换业务项目中正在使用的 EXE。
- 错误出现、关闭或更新均不得改变编辑区尺寸，不遮挡保存按钮。
- 执行参数数组、子环境、配置备份/哈希、secret 脱敏、null/[] 参数区别及显式确认等 AGENTS.md 约束不变。

## Review Focus

1. Toast 关闭后再次保存非法输入：必须继续阻止保存并再次提示（任务 1）。
2. 首次偏好文件损坏或无法写入：不阻止启动、不覆盖损坏原件、本次语言选择仍生效（任务 2）。
3. 未保存的非法数字/空字符串与正在运行任务同时存在：切换语言不重新解析、丢值或停任务（任务 4）。
4. 自定义名称恰好等于中文 UI 文案：不可误译，旧历史及终端原文不变（任务 3、4）。
5. 最小窗口、英文长错误、重复报错：不挤压编辑区、不无限堆叠、不抢焦点（任务 1、5）。

## 文件与接口边界

- 新增 `src/ProjectLauncher.Core/Localization/TextCatalog.cs`：语言规范化、资源读取和格式化。
- 新增同目录 `ZhCn.json`、`EnUs.json`：嵌入资源，以稳定资源键组织，不存用户数据。
- 新增同目录 `LanguagePreferences.cs`：可注入路径的原子偏好读写。
- 新增同目录 `LocalizedDiagnostic.cs`：稳定诊断键与格式参数，保留技术详情。
- 新增 `src/ProjectLauncher.Desktop/Services/LocalizationService.cs`：INPC 索引器、语言切换事件。
- 新增同目录 `LocalizedText.cs`：XAML 标记扩展和程序控件绑定辅助。
- 新增 `src/ProjectLauncher.Desktop/Controls/ToastHost.xaml` 及 `.xaml.cs`：覆盖层内容，不创建额外窗口。
- 新增 `tests/ProjectLauncher.Specs/LocalizationSpecs.cs`、`Services/LocalizationSmoke.cs`、`Services/ToastSmoke.cs`：沿用现有规格运行方式注册。
- 修改现有窗口、Views、Controls、ShellViewModel、Core 诊断抛出位置；不改 Windows ConPTY API。

## Task 1: 不占位 Toast 与保存栏间距

**Files:** 新增 ToastHost、ToastSmoke；修改 `MainWindow.xaml`、`MainWindow.xaml.cs`、`ViewModels/ShellViewModel.cs`、`Controls/ActionWorkspace.cs`、`Views/SettingsView.xaml`、`Views/SettingsView.xaml.cs`、`Services/UiSmokeRunner.cs`。

**Interfaces:** ToastHost 暴露 `Show(string source, string summary, string detail)`、`Clear(string source)`。ActionWorkspace 暴露 `event Action<string?>? ValidationNotice`；null 表示对应错误已修正。SettingsView 将该事件路由至主窗口通知入口。错误字典继续独立保留，不能依赖 Toast 是否可见判断合法性。

- [ ] 在 ToastSmoke 注册真实控件回归：取得编辑卡片与保存栏 Rect，触发非法字段保存，关闭 Toast，再尝试保存，断言文件未改变且提示重新出现。

```csharp
// 回归的核心断言；before/after 由 TransformToAncestor 得到。
if (before != after) throw new Exception("Toast changed editor bounds");
if (originalYaml != File.ReadAllText(configPath))
    throw new Exception("Invalid draft was saved after dismissing toast");
```

- [ ] 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Windows.ps1`，确认旧布局不能满足不占位回归。
- [ ] 将通知和页面放入同一 Grid 单元；ToastHost 右上对齐，Panel.ZIndex=100，最大宽度 420，Margin=12。覆盖宿主不设置全屏透明 Background，仅卡片接收点击。不使用会自动抢焦点的 Popup。

```xml
<Grid>
  <ContentControl x:Name="PageHost" HorizontalContentAlignment="Stretch"
      VerticalContentAlignment="Stretch"/>
  <controls:ToastHost x:Name="Notices" Panel.ZIndex="100"
      HorizontalAlignment="Right" VerticalAlignment="Top"
      MaxWidth="420" Margin="12"/>
</Grid>
```

保留现有 PageHost 导航赋值。Toast 使用主题键、摘要、详情/关闭按钮。同 source 更新而非追加；错误不自动消失。详情沿用 Dialogs。
- [ ] 删除 ActionWorkspace 的 `_error` 及其布局行；校验失败仅在阻止用户操作时发事件；字段修正后清除对应提示。保留字段定位和校验字典。将保存栏 Margin 改为 `0,10,0,0`、Padding 改为 `0,10,0,0`。
- [ ] 重跑 Windows 测试；补测长消息、重复消息、空白区域点击穿透、焦点不转移和深浅主题。检查原有确认弹窗仍出现。
- [ ] 单独提交 `fix: show validation errors as non-layout toasts`。

## Task 2: 语言资源和全局偏好

**Files:** 上述 Core Localization 文件、Core csproj、LocalizationSpecs、`tests/ProjectLauncher.Specs/Program.cs`。

**Interfaces:** `TextCatalog.Normalize(string? saved, string systemLanguage): string`；`TextCatalog.Format(string language, string key, params object?[] args): string`；`LanguagePreferences(string path)`，`Load(): (string? Language, string? Error)`、`Save(string language): string?`（null=成功）。资源格式使用显式选定 CultureInfo，不修改业务解析文化。

- [ ] 注册规格：无偏好+zh-TW=>zh-CN、无偏好+fr-FR=>en-US、英文偏好覆盖中文系统、非法偏好回退；比较两套资源键及格式占位符集合。

```csharp
if (TextCatalog.Normalize(null, "zh-TW") != "zh-CN") throw new Exception("Chinese fallback");
if (TextCatalog.Normalize("en-US", "zh-CN") != "en-US") throw new Exception("Saved preference");
```

- [ ] 执行 `dotnet run --project tests/ProjectLauncher.Specs -c Release` 并记录预期失败。
- [ ] 嵌入两份 JSON，键例如 `Common.Save`、`Settings.Title`、`Error.InvalidNumber`。Format 缺少目标语言键回退英文，英文也缺失则返回键以便诊断。测试必须阻止缺键进入交付。
- [ ] 偏好只存 `{ "language": "en-US" }`，位置 `%LOCALAPPDATA%/ProjectLauncher/preferences.json`；读取限制 64 KiB。损坏文件保存原件后才能替换；备份或写入失败返回错误，不破坏原件。使用同目录临时文件和原子替换，finally 清理本次临时文件。
- [ ] 在临时目录测试损坏 JSON、未知语言、只读/不可写位置、保存再读取；确认无 launcher.yaml 写入。运行 Core 全套并提交 `feat: add language catalogs and user preferences`。

## Task 3: 启动器诊断可翻译但业务数据不变

**Files:** `LocalizedDiagnostic.cs`、`ConfigValidator.cs`、`ConfigStore.cs`、`CommandBuilder.cs`、`ProjectEnvironment.cs`、`ProjectSetup.cs`、`ActionEditing.cs`、`ProcessRunner.cs`、`ProjectInstaller.cs`、`ProjectLauncher.Cli/Program.cs`、LocalizationSpecs。

**Interfaces:** `LocalizedDiagnostic(string Key, object?[] Arguments, string? TechnicalDetail = null)`，提供 `Render(string language): string`。现有 ConfigException 增加诊断属性/构造重载，保留原有异常类型与兼容构造函数；GUI 在显示边界使用诊断，CLI 在入口选定系统语言。未知异常显示本地化摘要加原始详情。

- [ ] 添加 Core 规格：同一非法配置分别渲染两种语言，错误类型/参数不变，包含中文的用户名称不被翻译；未知异常原始详情仍可见。

```csharp
var diagnostic = new LocalizedDiagnostic("Error.InvalidNumber", new object?[] { "保存" });
if (!diagnostic.Render("en-US").Contains("保存"))
    throw new Exception("User value was translated");
```

- [ ] 运行 Core 规格确认失败，再按文件逐个将启动器错误改为稳定键和参数，不按最终字符串反向匹配。
- [ ] 每迁移一类错误增加资源并跑 Core 规格；不修改返回码、参数数组、null/[]、信任与依赖确认、配置 hash 或备份流程。
- [ ] 在 CLI 和 GUI 边界格式化已知诊断；业务 stdout/stderr 不进入 TextCatalog。旧历史状态无法明确识别时原样显示。
- [ ] 全套 Core/Windows 规格通过后提交 `feat: localize launcher diagnostics`。

## Task 4: 无损即时语言切换

**Files:** LocalizationService、LocalizedText、`App.xaml.cs`、`MainWindow.xaml`/`.cs`、`ViewModels/ShellViewModel.cs`；`Views/` 下五个窗口/页面及其 code-behind；`Controls/ParameterForm.cs`、`ParameterEditorWindow.cs`、`ActionWorkspace.cs`；`Services/Dialogs.cs`、LocalizationSmoke、UiSmokeRunner。

**Interfaces:** 单例 `LocalizationService.Current`，`Language`、索引器 `this[string key]`、`SetLanguage(string language)`、INPC。`LocalizedText` 创建 Source=Current、Path=`[key]` 的 OneWay Binding；代码控件使用相同绑定，带参数文本保留键+参数。不设置业务线程 CurrentCulture。

- [ ] 加入 UI 回归：中文到英文切换，原控件实例、TextBox.Text、焦点、选择动作、脏标记、进程/终端标识均保持；用户名称“保存”不变。

```csharp
var control = nameBox;
var typed = nameBox.Text;
LocalizationService.Current.SetLanguage("en-US");
if (!ReferenceEquals(control, nameBox) || nameBox.Text != typed)
    throw new Exception("Language switch rebuilt or edited the draft");
```

- [ ] 运行 Windows smoke 确认失败；初始化语言服务必须早于 ProjectSetupWindow 及启动错误。测试注入临时偏好路径，不改真实用户偏好。
- [ ] 实现 INPC 索引器通知 `PropertyChanged("Item[]")`；所有显示更新在 Dispatcher。语言保存失败只通知，不撤销本次选择。窗口/控件自有事件在卸载时解绑。
- [ ] 按启动绑定→主导航→运行→终端→环境→设置→参数弹窗顺序迁移文本，每页包含 Tooltip、AutomationProperties.Name、空状态、动态按钮与确认内容。终端缓冲和运行日志绝不重写。
- [ ] 启动方式与参数类型选项使用稳定 Value 和本地化 Display，禁止继续比较“Python 脚本”等显示字符串来决定业务行为。语言变化只刷新 Display，不重新设置 SelectedValue。
- [ ] 移除当前项目卡片，底部加入语言选择和目录图标；保留顶部项目名/描述、版本、主题/帮助和官网。首次绑定窗口同样支持当前语言。
- [ ] 运行无损场景：包含非法数字草稿、secret 字段、空 argv、中文用户标签、活动 ConPTY 和实际运行任务；切换后命令及未保存数据保持一致。新增/删除/保存等原有回归仍通过。
- [ ] 提交 `feat: support live Chinese and English UI`。

## Task 5: 双语布局及发布回归

**Files:** LocalizationSmoke、ToastSmoke、ActionEditorSmoke、ActionNavigationSmoke、ActionQuickControlSmoke、ProjectSetupSmoke、ThemeControlSmoke、UiSmokeRunner、`tools/Test-Portable.ps1`。

**Interfaces:** 沿用现有 smoke 入口和报告格式。测试查找控件 Name/AutomationId，不使用翻译内容作为唯一定位方式。

- [ ] 增加中文/英文×深/浅主题×最小/常规尺寸矩阵；验证按钮边界在窗口内、Toast 开关前后保存栏 Rect 相同。主动制造长英文错误并打开详情。
- [ ] 先运行新增回归，再针对失败修复文本换行、最小宽度或弹窗滚动；不通过截断重要错误内容隐藏问题。
- [ ] 执行完整测试并保留报告：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Windows.ps1
python -m unittest discover -s tests/demo -v
python tools/check_static.py
```

- [ ] 人工可用时检查 100/125/150/200% DPI、中文 IME、键盘语言切换及 Toast 关闭；无法执行的项目明确记为未验收。
- [ ] 提交 `test: cover bilingual layouts and nonblocking notifications`。

## Task 6: 快速使用 README 与 EXE 交付

**Files:** `README.md`、新增 `README.zh-CN.md`、`docs/images/launcher-en.png`、`docs/images/launcher-zh-CN.png`；更新 `docs/DEVELOPMENT.md`、`docs/TESTING.md`、`docs/CONFIG.md`、`CHANGELOG.md`、`docs/reports/static-checks.txt`、`MANIFEST.sha256`。

- [ ] 从任务 5 的真实 WPF 窗口生成中性示例截图，检查不含用户路径/秘密；两份 README 共享同样事实和步骤。
- [ ] README 顶部使用 `[English](README.md) | [简体中文](README.zh-CN.md)`；正文依次是定位、真实截图、取得/构建 EXE、复制至项目、绑定环境、添加动作参数、保存运行、FAQ。
- [ ] 核实仓库 Release 页面后决定取得 EXE 的文字。无 Release 时使用 Build.cmd 的真实构建路径，不放下载按钮。FAQ 覆盖缺少环境、pip 应使用项目解释器、语言偏好位置、配置备份、预览版/未签名与终端边界。开发细节链接现有文档。
- [ ] 检查每个相对链接实际存在、中英操作一致、截图为 native WPF；更新开发文档说明资源键、诊断和语言测试。
- [ ] 运行发布和便携验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build.ps1 -VerifyWindows
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-Portable.ps1
dotnet restore ProjectLauncher.sln
python tools/check_static.py
Get-FileHash artifacts/portable/Launcher.exe -Algorithm SHA256
git diff --check
```

- [ ] 依据实际输出更新测试计数、EXE 大小/哈希、人工未验项。更新源码 MANIFEST；检查 ZIP 附带文档与本轮 README 一致，不含业务环境或旧日志。
- [ ] 提交 `docs: publish bilingual quickstart and verified build notes`，确认工作树状态；交付 EXE 绝对路径、提交号和准确验证范围，不推送。

## 执行方式待用户选择

推荐在当前会话由主代理顺序实施：各项共享诊断与资源接口，顺序迁移更易保留状态；完成后独立复核。也可选择逐任务子代理实施与复核。本文是实施计划，尚未执行产品修改。
