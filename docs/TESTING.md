# 验证记录与 Windows 验收

版本：**2.0.0-preview.1**。记录日期：2026-09-23。以下区分已执行检查、未完成的发布步骤和人工验收，不把自动化通过等同于产品已验收。

## 本次实际执行了什么

验证环境：Windows 10.0.26200.9457 x64，.NET SDK 10.0.204，Microsoft.WindowsDesktop.App 10.0.8。以下命令在本机实际执行并以退出码判断；构建和测试产物位于被 `.gitignore` 排除的 `artifacts/`、`bin/`、`obj/`。

| 检查 | 状态 | 证据与范围 |
|---|---|---|
| Release 解决方案编译 | 已执行，通过 | `dotnet build ProjectLauncher.sln -c Release`；0 个警告，0 个错误 |
| C# Core 回归程序 | 已执行，通过 | `tests/ProjectLauncher.Specs`：62 passed，0 failed |
| Windows ConPTY / Job / 项目锁测试 | 已执行，通过 | 4 项通过；包含父进程标准流重定向时的 ConPTY 输入回归 |
| WPF 深浅色截图与绑定错误检查 | 已执行，通过 | `tools/Test-Windows.ps1` 生成 8 张原生 WPF 图片；报告 8 项 PASS |
| Python Demo 自动化测试 | 已执行，通过 | 13 tests，OK；只验证示例业务脚本 |
| 静态源码检查 | 已执行，通过 | 542 passed，0 failed；不是 C# 编译器或 WPF 运行时 |
| 源码文件 SHA-256 | 已更新 | 根目录 `MANIFEST.sha256`；仅代表所列源码文件完整性 |
| 单文件自包含 Windows EXE | **未完成** | `Build.ps1 -VerifyWindows` 已通过其编译与测试阶段；下载 `Microsoft.NETCore.App.Runtime.win-x64 10.0.8` 时无进展，已停止，未生成可交付 ZIP / EXE |
| 人工 DPI / IME / 完整工作流 | **未执行** | 仍须按下方清单在交互式桌面逐项验收 |

首次 Windows 编译发现并修正了 WPF 临时编译项目缺少 `System.IO` 全局引用；原生测试发现并修正了父进程标准流重定向时 CMD 绕过 ConPTY 输入管道的问题，并加入独立子进程回归；WPF smoke 发现并修正了只读 `Progress` 属性被默认 TwoWay 绑定导致的启动异常。静态 HTML 预览仍只描述设计方向，不是本次 WPF 运行证据。

## 可复核的本地检查

```powershell
python -m unittest discover -s tests/demo -v
python tools/check_static.py
```

第二条使用 Python 标准库；安装 PyYAML 后会额外检查 YAML 示例结构。未安装时会明确输出未执行这部分。这里用 PyYAML 检查示例，不能证明实际应用使用的 YamlDotNet 的反序列化行为。

Python 测试覆盖：中文及空格路径、大小写转换、前缀中的 Shell 字符作为数据、重复与真实进度、禁止覆盖、显式覆盖、主动失败状态、缺少输入、非法间隔、输入输出同路径、密钥不回显、多行备注和环境探针。Demo 输出两个文件时不是跨文件原子事务；如果第二次写入失败，第一个已经完成的文件可能保留。

## Windows 上先编译并执行 C# 测试

在装有 .NET 10 SDK 的 Windows 开发机运行：

```powershell
dotnet restore ProjectLauncher.sln
dotnet build ProjectLauncher.sln -c Release --no-restore
dotnet run --project tests/ProjectLauncher.Specs -c Release --no-build
dotnet run --project tests/ProjectLauncher.Windows.Specs -c Release --no-build
```

核心测试是自包含控制台回归程序，不是 xUnit，因此用 `dotnet run`，不要用 `dotnet test` 的空输出声称通过。任意编译、测试失败都需要先修复；保留错误日志和首个错误位置。

Core 测试代码覆盖 v1 配置、非法字段和循环引用、参数顺序和三种绑定、数值/路径、布尔语义、敏感值持久化、进程参数、超时和取消、终端 VT 模型、环境路径和安全接入。Windows 测试代码会实际创建 ConPTY 和 CMD，测试环境变量、持久会话、尺寸修改、关闭、Job 生命周期以及同项目互斥；不在 Windows 上不会假装通过。

## 一键 Windows 测试与实际 WPF 截图

```powershell
.\Test-Windows.cmd
```

脚本会编译、运行两个回归程序，再以 `--ui-smoke-test` 启动 WPF 窗口。在可交互的 Windows 桌面会话中执行；远程 CI / 无桌面账户可能无法完成 GUI 检查。

WPF 检查遍历四个页面与深/浅主题，使用 `RenderTargetBitmap` 导出 8 张实际 WPF 图片，并记录捕获到的绑定错误。测试终端标签使用**明确标注的测试屏幕内容**；它只是覆盖终端控件绘制，不代表这个截图过程启动了真实 Python。真实 ConPTY 测试由另一个原生测试程序执行。

自动截图检查也有边界：它不覆盖所有鼠标/键盘交互、所有启动阶段绑定错误、IME、不同缩放和全部 TUI 兼容。不能仅凭八张图片宣告产品已经验收。

## 人工验收清单

### 运行与环境

- [ ] 从包含中文和空格的目录启动，工作目录与配置一致，而不是 CMD 当前目录。
- [ ] 移走业务 `.venv` 后，壳仍能打开设置/环境页；运行任务明确报错，不退回系统 Python。
- [ ] 分别检验 `existing`、`venv`、有锁文件和无锁文件的 `uv` 项目。
- [ ] 只有确认后才初始化或安装；没有 requirements 时不会编造依赖清单；有内容的损坏环境不自动删除。
- [ ] GUI 任务、内置终端和外部终端中 `sys.executable`、`sys.prefix` 都指向目标项目。
- [ ] 在终端中使用 `python -m pip` / `python -m pytest` 核对解释器；裸 `pip` / `pytest` 如果项目没安装对应命令，仍可能沿系统 PATH 查找，不是沙箱。
- [ ] 业务脚本正常退出、失败、超时、手动停止时，退出码/耗时/状态符合事实。
- [ ] 大量 stdout / stderr 不让 UI 无限增长；无业务进度协议时只显示不确定进度。
- [ ] 同一项目不允许第二个本壳 GUI 或 CLI 同时执行；不同项目可以分别打开。

### 参数与设置

- [ ] 参数的 `0`、false、空值、位置参数、带空格路径、中文、多行内容和选项值都按规范传递。
- [ ] 条件不满足的参数不传入；高级参数只控制折叠显示，不被意外丢掉。
- [ ] 参数设计器添加/复制/删除/排序/改名后，动作引用和条件引用仍正确。
- [ ] 不修改的 YAML 标量选项与条件保存后保持其语义；格式/注释可能变化。
- [ ] 设置页保存生成 `.bak`；外部编辑之后的冲突需要先重新载入，不直接覆盖。
- [ ] 任务、内置终端或本壳打开的外部终端运行期间，不允许更换配置环境。
- [ ] 改主题/窗口尺寸/普通参数/预设后重开可恢复；password / secret 不写入用户参数状态。
- [ ] 密钥不出现在脱敏预览和普通已知值日志中；仍检查子进程变形输出、历史旧文件和系统命令行暴露风险。

### 真正终端与 UI

- [ ] PowerShell / CMD 中 `cd`、变量赋值和命令历史在同一标签里保持；不是一条命令起一个新 Shell。
- [ ] Python REPL、Tab、上下箭头、Ctrl+C、复制粘贴、带中文的辅助输入行、终端标签切换可用。
- [ ] 多行粘贴有确认；恶意 OSC52 不读写剪贴板；动态颜色、常见进度条刷新和窗口缩放不过度闪烁。
- [ ] 关闭终端与退出窗口能清理本会话相关进程；特别检查子进程派生与父进程先退出的情况。
- [ ] 100%、125%、150%、200% 缩放，最小窗口和最大化下，主按钮和保存按钮可见或可滚动到达。
- [ ] 分别检查深色、浅色、长中文项目名/说明、长命令、10 个以上参数、键盘焦点和文件拖入。
- [ ] 源码的原生终端不是完整 Windows Terminal；鼠标报告、复杂 Unicode、完整 IME/TUI 与辅助技术另行验收。

### 发布与接入

- [ ] `Build.cmd` 无编译或测试失败，生成 `artifacts/portable/Launcher.exe`。
- [ ] 复制发布目录的 EXE，而不是 bin 目录的开发 apphost；在未安装 .NET 的 Windows 测试机上启动。
- [ ] 未安装 Python 时壳可打开，但业务任务应显示环境未就绪。
- [ ] 接入现有项目只增加壳/缺失配置，不覆盖业务源码、依赖文件或已有 `.venv`。
- [ ] 已有 Launcher.exe 时拒绝覆盖；已有 YAML 时保持原内容；失败只回滚本次创建的文件。
- [ ] 重复构建使用全新封装暂存目录，不把上一次 Demo 的环境、日志、输出或密钥打进 ZIP；原产物保留为 previous 备份。
- [ ] 分发时附上自身及实际收集的第三方许可证，检查 SmartScreen / 杀毒软件与未签名提示；本项目未提供商业代码签名。

## 已补录与仍待补录

已补录 Windows 版本/架构、SDK、Release 编译、两个回归程序和 8 张 GUI 图片/报告。仍需在 runtime pack 可正常获取时重新执行自包含发布，记录发布 EXE / ZIP 的 SHA-256，并完成上述人工验收清单。完成这些证据前保留 preview 标识，不把本包宣传为生产级已验证发行版。
