# 二次开发指南

## 先跑通构建，再扩功能

安装 .NET 10 SDK，在 Windows 上打开 `ProjectLauncher.sln` 或运行 `Start-Dev.cmd`。本次已完成 Windows Release 编译、自动化测试和 win-x64 单文件发布，结果见 [测试记录](TESTING.md)；修改后仍应重新执行 Build / Test-Windows。人工验收尚未完成，保留 preview 标识。

```powershell
dotnet restore ProjectLauncher.sln
dotnet build ProjectLauncher.sln -c Release
dotnet run --project tests/ProjectLauncher.Specs -c Release
dotnet run --project tests/ProjectLauncher.Windows.Specs -c Release
```

核心测试是可执行规格程序，以进程退出码判断。并没有 xUnit / NUnit 测试发现配置，单独 `dotnet test` 不能证明已执行本包回归。

## 目录与职责

```text
src/
  ProjectLauncher.Core/
    Models.cs                  v1 模型、通用值、命令计划
    ConfigStore.cs             YAML、结构保护、哈希与原子保存
    ConfigValidator.cs         字段、范围、引用、保留环境变量
    CommandBuilder.cs          argv / env / 条件 / 类型 / 脱敏预览
    ProjectEnvironment.cs      项目环境、初始化计划、实际探测
    ProcessRunner.cs           非交互任务、流式日志、超时、脱敏
    StateStore.cs              本地状态、预设、历史、有限磁盘日志
    ProjectInstaller.cs        不覆盖现有项目的安装计划
    ProjectSetup.cs            首次接入的环境发现、绑定和只创建配置
    TerminalScreen.cs          有界基础 VT 屏幕模型
  ProjectLauncher.Windows/
    NativeMethods.cs           Windows 原生签名与布局
    JobObject.cs               kill-on-close 进程归属
    ConPtySession.cs           持久伪控制台、独立管道工作线程
    ProjectLease.cs            跨 GUI/CLI 项目锁，线程拥有权隔离
  ProjectLauncher.Desktop/
    App.xaml / .cs             启动、初始配置、异常、项目窗口
    MainWindow.xaml / .cs      窗口、导航、接入、主题、关闭
    Themes/                    语义颜色与控件模板
    ViewModels/                INPC 共享状态、运行/日志/环境流程
    Views/                     Run / Terminal / Environment / Settings
    Controls/ParameterForm.cs  参数控件生成
    Controls/ActionWorkspace.cs 动作编辑、参数列表与成员保护
    Controls/ParameterEditorWindow.cs 独立参数弹窗、取消隔离和保存校验
    Controls/TerminalControl.cs 原生 VT 绘制与输入
    Services/                  主题、对话框、Windows UI smoke test
  ProjectLauncher.Cli/         命令行入口，复用核心和 Windows 服务
```

Core 不引用 WPF，Windows 层不引用 GUI。Desktop 采用 ViewModel + 明确的 UI code-behind，不引入庞大 DI / MVVM 框架。不要把第三方业务库导入壳；新增业务脚本仍留在用户项目中，壳只启动子进程。

## 核心接口

```csharp
var snapshot = ConfigStore.Load(@"D:\project\launcher.yaml");
var runtime = new ProjectEnvironment(snapshot.Config, snapshot.Path);
var values = new Dictionary<string, object?> { ["workers"] = 4 };
var command = CommandBuilder.Build(snapshot.Config, "run", values,
    runtime.Root, runtime.RequirePython());
using var runner = new ProcessRunner(() => new JobObject());
runner.Output += text => Console.Write(text);
var result = await runner.RunAsync(command, runtime.Root,
    runtime.ExecutionEnvironment(), cancellationToken);
```

调用层必须先获得配置信任和占用锁；以上省略的是 UI 交互，而不是允许后台无确认执行任意项目。GUI 已实现这层确认，CLI 用显式 --trust。

ConPTY 的接口是 `Start(argv, cwd, env, cols, rows)`、`Write(text)`、`Resize(cols, rows)`、`CloseAsync()`。一个实例对应一个持续会话，不要复用已关闭对象。原生输出来自工作线程，UI 只消费队列；不能直接在输出线程访问 WPF 控件。

## 修改界面

所有语义颜色在 Dark/Light 成对维护。按钮、输入框、卡片、导航的模板在 Styles。布局在 XAML；参数是动态生成时由 ParameterForm 使用同一资源词典，不应该新增一套硬编码调色板。

保留三种视觉层级：低对比度背景、可区分的卡片/输入区、少量高对比度主操作。主按钮同一页面只突出一个。长表单和小屏幕使用滚动区域，不能依赖固定 1920×1080 才能点到保存/运行按钮。

修改后在真实 Windows 下运行 UI smoke-test，并人工检查 1060×700 与 1360×920、深浅主题、125%/150%/200% DPI。静态 HTML 预览只描述设计方向，不作为 XAML 测试。

## 例：增加 date 参数

先在 `tests/ProjectLauncher.Specs/Program.cs` 添加一个失败规格：合法日期得到标准 `yyyy-MM-dd` argv、非法日期抛 ConfigException、空可选值省略、必填为空拒绝。运行到失败后再实现。

接着把 date 加入 `ConfigValidator.ParameterTypes`；在 `CommandBuilder.Normalize` 按 `DateOnly.TryParseExact(..., "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out ...)` 校验并标准化。不要使用机器当前地区去猜月/日顺序。

ParameterForm 增加 DatePicker（或明确格式文本框）；其 read/write 接口仍返回同一值类型。参数设计器来自类型集合，会显示新增类型，但默认值编辑也必须保持和 Core 一致。添加预设保存/恢复、可见性和 UI 回归。

完成后跑全部 Core 和 Windows 测试，而不是只测新增方法，再重新发布 EXE。

## 例：增加一个业务常用动作

不改 C#，在 YAML 添加：

```yaml
- id: check_data
  label: 校验业务数据
  argv: [python, scripts/check_data.py]
  parameters: [input]
  timeout_seconds: 120
```

这不是插件接口，不会让 scripts/check_data.py 自动存在。原项目需要真实实现该脚本。

## 真实进度协议

普通脚本可不改，任务运行时显示不确定进度。能提供真实完成量时逐行输出：

```python
import json
print('@@launcher:' + json.dumps({
    'progress': 23, 'total': 100, 'message': '已处理 23 个文件'
}, ensure_ascii=False), flush=True)
```

只有实际发来合法数字才更新。无总数时不要自己写定时器伪造百分比。任务成功退出时可显示执行完成，但业务成功含义由业务退出码定义。

## 真终端开发边界

ConPTY 后端和 VT 渲染是分开的：后端提供伪控制台，不自动替你实现所有终端显示能力。TerminalScreen 是基础兼容层，支持常见颜色、光标定位、删除、滚动区域、备用屏幕、DSR、bracketed paste 和基础宽字符；不实现完整 emoji shaping / bidi / 鼠标协议 / accessibility 文本模式。

对于新协议，先给屏幕模型写分段转义序列和边界尺寸测试，再在 Windows 验证真实 PowerShell / REPL / 全屏应用。不要声称某个 TUI 兼容，除非实际测试过。

多行粘贴确认不能删除。OSC 52 剪贴板请求不自动执行；OSC 链接不自动打开。中文辅助输入行向当前会话发送原始输入，不应该被改成临时启动一个新 Shell。

## 进程与状态约束

非交互 GUI 任务关闭 stdin；业务需要 input() 时使用终端。普通任务通过独立 argv 调用，密钥优先 env；不得把参数值串进 cmd / PowerShell / Python -c。

停止是终止进程，不是暂停。Windows GUI 任务在启动后尽快加入 Job，仍有极短启动窗口；ConPTY 子进程是挂起创建、加入 Job 后恢复。不宣传为容器/沙箱；外部服务管理器或主动脱离的进程不一定能由壳完整控制。

配置保存要在任务和终端已结束后执行，避免界面显示新环境而旧进程仍在运行。项目锁的互斥对象需要同一线程拥有/释放；CLI 在 await 后可能换线程，因此 ProjectLease 用专门线程持有，不能简化成异步方法前后直接 ReleaseMutex。

状态里只放非 secret 参数。配置本身仍是明文；用户把旧普通字段改成 secret 后，新保存会清理预设值，但旧磁盘日志和业务脚本输出不会自动回溯清洗。

## 给 Codex 的执行说明

### 国际化与通知

Core 的 `Localization/ZhCn.json` 与 `EnUs.json` 是同键嵌入资源。新增文案要同时添加两种语言，使用稳定资源键和格式参数，不按渲染后的中文反向匹配；格式中的字面大括号需转义。Core 的 `LocalizedDiagnostic` 保留键、参数和原始技术信息，`ConfigException` 在显示边界渲染。

Desktop 在启动窗口前初始化 `LocalizationService`。XAML 使用 `{loc:LocalizedText Key}`，代码创建的控件使用 `L.Localize` 或 `L.Dynamic`；动态文案闭包只捕获稳定上下文。选项值使用不变标识，本地化显示不能参与业务判断。不要修改业务线程的 CurrentCulture、重建页面或重写终端缓冲来切换语言。

`ShellViewModel` 管理通知状态，`ToastHost` 只负责覆盖层显示。字段合法性独立于通知是否关闭：关闭后再次保存非法值仍须阻止，修正字段只清除本来源的提示。新增通知优先保留异常或资源键，避免冻结当前语言。

`LocalizationSpecs` 检查资源、偏好恢复和结构化诊断；原生 `LocalizationSmoke`、`ToastSmoke` 与 `BilingualLayoutSmoke` 验证草稿保留、不占位通知和中英深浅主题矩阵。实际进程与 CMD 变量保留由 `ActionQuickControlSmoke` 验证。测试使用隔离偏好，不修改开发者的语言选择。

项目根 `AGENTS.md` 已整理不变式。建议第一次指令是：

```text
阅读 README、docs/DEVELOPMENT.md、docs/CONFIG.md、docs/TESTING.md。
先在 Windows 构建现有源码并运行所有规格与原生测试，修复失败后再做新功能。
保留单项目 WPF 架构与独立设置页，不改为 WebView 或 Web 服务。
对本次修改增加失败测试，修复后运行全量回归；报告真正运行过的命令、退出码和跳过项。
```

源码中的 `docs/design` 保存了本版范围与实现记录，不是对未来功能的完成承诺。
