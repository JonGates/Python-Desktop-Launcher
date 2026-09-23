# Project Launcher · C# 桌面版

**2.0.0-preview.1 · Windows / .NET 10 / WPF · 单项目启动器**

一个项目，一份 `launcher.yaml`。原生桌面表单负责参数，项目自己的 Python 负责业务；内置终端使用同一项目环境。**不是 Web 服务，不打开浏览器，不使用 WebView。**

> **交付状态必须先看：本包仍是源码预览版，不是已完成人工验收的稳定版。** 已在 Windows x64 / .NET 10 SDK 上完成 Release 编译、Core 与 Windows 原生规格、ConPTY、Job、项目锁及 8 张真实 WPF 深浅主题渲染检查；人工 DPI / IME / 完整工作流仍未执行。自包含单文件发布曾启动，但下载 .NET runtime pack 时停滞，因此仓库仍不捆绑 EXE。实际证据和未覆盖项见 [测试报告](docs/TESTING.md)。

## 先从哪里开始

首次使用：看本页「构建与运行」。已有 Python 项目：看 [接入说明](docs/INTEGRATION.md)。改配置：看 [配置规范](docs/CONFIG.md)。改界面与功能：看 [二开指南](docs/DEVELOPMENT.md)。

[打开本地 UI 设计预览](docs/preview/index.html) · [深色运行页设计图](docs/preview/run-dark.png) · [参数设计器设计图](docs/preview/settings-dark.png)

预览 HTML 是便于查看视觉设计的静态文档，不是产品实现；图片不是 Windows WPF 的实际运行截图。应用代码在 `src/ProjectLauncher.Desktop`，实际 WPF 截图可用 `Test-Windows.cmd` 在 Windows 上生成。

## 四个独立页面

| 页面 | 源码中实现的功能 |
|---|---|
| 运行项目 | 九种参数控件、中文标签与说明、输入校验、高级参数、条件显示、动作选择、参数预设、脱敏命令预览、实时输出、真实进度协议、停止、超时、运行历史和日志 |
| 项目终端 | Windows ConPTY 持久会话、最多四个内置标签、项目工作目录与环境注入、PowerShell / CMD / Python REPL、常用 ANSI/VT、选择复制、粘贴确认、滚动历史、调整字号、中文辅助输入行、系统终端后备 |
| 环境管理 | 明确的项目解释器路径、uv / venv / existing、手动确认初始化与依赖同步、实际解释器探测、诊断复制、缺少环境时阻止执行、不静默回退系统 Python |
| 项目设置 | 独立设置页面、基本信息表单、逐项 argv 编辑、可视化参数设计器、添加/复制/删除/排序、完整 YAML 高级编辑、合法性校验、.bak 备份、磁盘冲突检测 |

深色石墨 / 浅色两套主题共享同一组语义颜色；自绘圆角控件、矢量图标、侧栏导航、任务状态和本地通知。使用系统字体，不附带字体文件。

这不是 IDE，也不自动推断任意项目的业务参数。暂不包含源码断点调试、任意脚本暂停/恢复、远程任务、任意项目自动解析 argparse/click/typer、完整 Windows Terminal / xterm 兼容和全功能无障碍终端。

## 构建与运行

### 1. Windows 开发机安装 .NET 10 SDK

使用 [.NET 官方下载页](https://dotnet.microsoft.com/download/dotnet/10.0) 安装 **SDK**，不是只安装 Runtime。推荐先在 Windows 11 x64 验收。解压到可写的普通路径，例如：

```text
D:\tools\ProjectLauncher-CSharp\
```

确认命令可用：

```powershell
dotnet --version
```

应选择到 `10.x` SDK。项目的 `global.json` 允许更新的 10.0 feature band，不会选择预发布 SDK。首轮还原需要访问 NuGet 官方源，唯一直接第三方业务依赖是 YamlDotNet。

### 2. 先运行源码

双击根目录：

```text
Start-Dev.cmd
```

等价于：

```powershell
dotnet run --project src/ProjectLauncher.Desktop -- --project .
```

这一步只是运行 C# 启动器，**不要求先给业务 `.venv` 安装任何 UI 包**。首次打开 Demo 后，到「环境管理」创建项目环境，再检查解释器，最后回到运行页面处理文本。项目环境初始化需要已有 Python 或 uv；壳本身不负责无提示下载它们。

### 3. 发布真正可复制的 EXE

双击：

```text
Build.cmd
```

或在 PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
```

脚本会还原依赖、编译整个解决方案、运行 C# 核心回归程序，任一命令失败就停止；随后发布自包含、单文件的 GUI 和 CLI。只有这些命令在你的 Windows 上成功后，才会出现：

```text
artifacts/
├─ portable/
│  ├─ Launcher.exe                # 原生 GUI，可放到任意现有项目
│  ├─ Launcher.Cli.exe            # 可选，供命令行 / 脚本调用
│  ├─ LICENSE-Launcher.txt
│  ├─ THIRD_PARTY_NOTICES.md
│  └─ licenses/                   # 从已还原包收集的许可证
├─ demo/
│  ├─ Launcher.exe
│  ├─ Launcher.Cli.exe
│  ├─ launcher.yaml
│  └─ demo/
├─ ProjectLauncher-portable-win-x64.zip
└─ ProjectLauncher-demo-win-x64.zip
```

自包含发布包括 .NET 运行时，目标机不必另外安装 .NET；**这不意味着业务 Python、模型或数据也被打包**。单文件包含原生依赖时可能在用户临时目录展开，不能承诺零临时文件或固定 EXE 大小。发布脚本不启用 WPF 裁剪，也不承诺 Native AOT。详见 [构建说明](docs/BUILD_WINDOWS.md)。

没有 `.NET SDK`、还原网络失败、编译错误、测试失败时，脚本不会伪造成功结果。不要从 `bin/.../Launcher.exe` 随便抽走一个开发 apphost；它可能仍依赖旁边的 DLL。只有 `artifacts/portable/Launcher.exe` 是脚本的单文件交付目标。

## 最简单的现有项目接入

将**成功发布后的** `Launcher.exe` 放到现有项目根目录。没有配置时，首次打开会询问是否生成最小配置；有原来的 `launcher.yaml` 时直接读取，不自动覆盖。

```text
你的 Python 项目/
├─ Launcher.exe             # 新增
├─ launcher.yaml            # 新增 / 沿用已有 schema v1
├─ main.py                  # 原样保留
├─ pyproject.toml           # 原样保留
├─ uv.lock                  # 原样保留
└─ .venv/                   # 目标项目自身的环境
```

也可以在已发布的 GUI 左下方点 **「接入其他项目」**。该功能只复制启动器，缺少配置时创建最小配置，已有同名 EXE 时拒绝覆盖。

源码包提供接入脚本，可以先预览：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-Into.ps1 -Target "D:\code\invoice-tool" -Entry "main.py" -WhatIf
```

检查输出后，去掉 `-WhatIf` 正式接入。该脚本也需要先执行 Build 生成发布后的 EXE。

**不要复制这个源码目录中的 `.venv`、整个 `.launcher`、业务 Demo 或旧 Python 启动器 runtime。** 更详细的 uv、已有环境、模块入口及旧版迁移说明见 [接入文档](docs/INTEGRATION.md)。

## 参数怎么映射

原始命令：

```powershell
python main.py --input "D:\pdf" --workers 4
```

对应配置：

```yaml
schema_version: 1
app:
  name: PDF 处理工具
runtime:
  mode: existing
  project_dir: .
  venv: .venv
actions:
  - id: run
    label: 开始处理
    argv: [python, main.py]
    parameters: [input, workers]
parameters:
  - name: input
    label: 输入目录
    type: directory
    argument: --input
    required: true
    must_exist: true
    description: 选择需要处理的 PDF 所在目录。
  - name: workers
    label: 并发数量
    type: integer
    argument: --workers
    default: 4
    min: 1
    max: 16
    description: 同时处理的文件数量。
```

日常用户只看运行页的表单；开发者可在设置页可视化编辑，或直接编辑 YAML。**参数名和开关必须与原脚本一致**，启动器不能让一个不支持 `--workers` 的脚本凭空支持它。

## 内置终端与 CLI 不是一回事

内置终端位于 GUI 的「项目终端」页面，使用 ConPTY 创建持续会话；`cd`、Shell 变量和 REPL 状态不会因输入下一条命令而丢失。首次必须信任配置、准备好项目环境。

```powershell
python -c "import sys; print(sys.executable); print(sys.prefix)"
python demo/interactive_demo.py
python
```

可选的 `Launcher.Cli.exe` 则是给自动化脚本调用的无 GUI 入口：

```powershell
.\Launcher.Cli.exe check --project .
.\Launcher.Cli.exe list --project .
.\Launcher.Cli.exe init --project . --trust --yes
.\Launcher.Cli.exe env --project . --trust
.\Launcher.Cli.exe run process --project . --set repeat=2 --set overwrite=true --trust
.\Launcher.Cli.exe shell --project . --shell powershell --trust
```

GUI 和执行型 CLI 对同一份配置使用同一个项目占用锁，避免同时修改环境。内置终端本身可以有多个标签。仅 `check` / `list` / 不带 `--trust` 的 `env` 不执行项目代码。

终端继承项目环境，**不是沙箱**：显式运行别的解释器、切换环境、使用 `uv run` 自选项目或执行其他系统程序仍然可能离开该环境。内置终端支持基础 VT，不承诺所有全屏 TUI、复杂 emoji / 双向文字 / 鼠标跟踪等行为；遇到这类程序可使用同页面的「系统终端」。

## 项目自己的数据放在哪里

```text
.launcher/csharp/
├─ user.json          # 上次普通参数、预设、主题、窗口尺寸、配置信任哈希、历史
└─ logs/              # 本启动器保存的任务日志
```

此目录不复用旧 Python 版 `.launcher/runtime`。密码和 `secret: true` 表单值不保存到预设 / 上次参数。已知敏感值会在命令预览及任务输出中尽力做逐块脱敏，但不是通用敏感信息识别；Shell 自己的历史和业务代码主动输出的数据不在同一保证范围内。

配置是普通文本，不把真实密钥写进 `default`、`runtime.env` 或 `argv`。已存在的历史 / 日志不会因为后来把字段改成 secret 就自动变成安全文件。详见 [安全边界](docs/SECURITY.md)。

## 二次开发入口

| 想改什么 | 看哪里 |
|---|---|
| 主布局、导航、窗口行为 | `src/ProjectLauncher.Desktop/MainWindow.xaml` / `.cs` |
| 深色 / 浅色、圆角、按钮、字体 | `Themes/Dark.xaml`、`Light.xaml`、`Styles.xaml` |
| 动态参数控件 | `Controls/ParameterForm.cs` |
| 设置页 / 参数设计器 | `Views/SettingsView.xaml` / `.cs` |
| 配置、类型检查、参数映射 | `ProjectLauncher.Core` |
| Python / uv 环境 | `ProjectEnvironment.cs` |
| 进程、超时、脱敏 | `ProcessRunner.cs` |
| ConPTY、Job Object | `ProjectLauncher.Windows` |
| 基础 VT 解析和原生绘制 | `TerminalScreen.cs` / `Controls/TerminalControl.cs` |

核心层不依赖 WPF。视图状态使用 INPC；控件组装 / 选择器 / 生命周期保留少量 code-behind，不宣称这是纯 MVVM 框架。配置层和进程层可复用于别的 UI。完整步骤、新增控件示例和 Codex 约束在 [二开指南](docs/DEVELOPMENT.md) 与 [AGENTS.md](AGENTS.md)。

## 验收

在 Windows 上双击 `Test-Windows.cmd`。脚本会运行核心回归、Windows ConPTY / Job / 项目锁集成测试，以及四页面深浅主题的实际 WPF 渲染检查。测试程序是自带的可执行规格，不使用 `dotnet test` / xUnit 发现；以脚本的退出码和报告为准。

然后完成 [Windows 人工检查清单](docs/TESTING.md)。自动截图不代表点击每一个控件、中文输入法、高 DPI、异常退出和真实业务项目都通过测试。

本仓库没有启用遥测、远程管理或自动更新。许可见 [LICENSE](LICENSE)，第三方说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
