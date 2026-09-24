# Python Desktop Launcher

[English](README.md) | [简体中文](README.zh-CN.md)

**一个 Python 项目，一个桌面启动器，多个启动动作。**

把项目命令变成易用的表单：将 `Launcher.exe` 复制到 Python 项目，绑定项目环境，配置动作和参数，无需改动业务代码。

Windows x64 / x86 · 原生 C# / WPF · 中英文 · MIT · **v1.1.0**

![原生 WPF 启动动作设置](docs/images/launcher-zh-CN.png)

*隔离示例项目的真实原生 WPF 渲染，不是网页效果图。*

## 为什么使用它？

- **沿用项目环境：** 检测并绑定虚拟环境，不向业务 `.venv` 安装启动器界面依赖。
- **每个动作独立配置：** 服务、应用、脚本各自拥有参数表单和预设。
- **看清运行情况：** 命令预览、实时日志、退出状态、历史，以及需要确认的停止操作。
- **真正的终端：** 项目环境中的持续 PowerShell、CMD 和 Python 会话。
- **即时语言切换：** 中英文切换不丢草稿，不重启任务或终端。

一个启动器管理一个项目。目前**同时运行一个 GUI 任务**，可以打开多个终端会话。不是 IDE、沙箱或全局项目管理平台。

## 快速使用

### 1. 获取 Launcher.exe

直接下载对应架构的 **EXE**，无需解压或安装：

- [下载 v1.1.0 · Windows x64](https://github.com/JonGates/Python-Desktop-Launcher/releases/download/v1.1.0/Launcher-v1.1.0-win-x64.exe)：64 位 Windows，推荐。
- [下载 v1.1.0 · Windows x86](https://github.com/JonGates/Python-Desktop-Launcher/releases/download/v1.1.0/Launcher-v1.1.0-win-x86.exe)：32 位 Windows。

将下载的文件放入项目目录，可重命名为 `Launcher.exe`，方便对应下文示例。启动器是自包含程序，业务项目仍需自己的 Python 环境。

[发布页](https://github.com/JonGates/Python-Desktop-Launcher/releases/tag/v1.1.0)同时提供附带许可证的可选 ZIP 包，重新分发时请保留相关许可证。需要从源码构建时，参阅 [Windows 构建说明](docs/BUILD_WINDOWS.md)。

### 2. 复制到项目

```text
my-project/
├── Launcher.exe       ← 复制到这里
├── .venv/             ← 项目自己的环境
├── server.py
└── requirements.txt
```

不要复制本仓库的环境、Demo 文件或旧启动器 runtime。重新分发时请保留随附许可证。

### 3. 绑定环境

双击 `Launcher.exe`。

- 已有 `launcher.yaml` / `Launcher.yaml`：直接读取使用。
- 没有配置：确认项目目录，选择检测到的环境，绑定并生成配置。
- 没有环境：可以先绑定，再进入「环境管理」，明确确认后创建环境、安装依赖。

首次绑定无需填写入口文件。检测不执行项目代码；缺少环境时不会静默回退系统 Python。

### 4. 添加动作和参数

进入「项目设置 → 启动动作 → 添加动作」。

以支持 `python server.py --port 8000` 的脚本为例：

| 设置 | 填写内容 |
|---|---|
| 动作名称 | API 服务 |
| 启动方式 | Python 脚本 |
| 执行目标 | server.py |
| 添加参数 → 参数名称 | 端口 |
| 命令参数 | --port |
| 控件类型 / 默认值 | integer / 8000 |

在弹窗中新增、编辑参数，每个动作拥有自己的参数。选项必须是脚本实际支持的，启动器不会凭空增加 CLI 能力，也不会自动解析任意 Python 代码。

点击「保存并查看」，立即看到该动作的参数表单。

### 5. 运行

展开「运行项目」，选择动作、调整参数，点击「启动动作」。侧栏绿色三角也可以开始运行；红色停止圆圈会在确认后终止任务。

配置信任与依赖修改需要确认。错误使用可关闭的悬浮通知，不会把编辑区挤下去。

## Go EXE 与 Java JAR 启动案例

**当前限制：以下两种动作仍要求绑定的 Python 虚拟环境存在。** 案例使用现有启动动作能力，并非新增 Go / Java 环境模式。业务 EXE / JAR 及其运行依赖需自行准备；示例文件名和参数假定程序确实支持，请按实际程序调整。

### Go：启动已经编译好的 EXE

假设 Go 程序已打包为 `bin/report.exe`，支持以下命令：

```text
./bin/report.exe --input "D:\data\sales report.csv" --workers 4
```

进入「项目设置 → 启动动作 → 添加动作」，填写：

| 设置 | 填写内容 |
|---|---|
| 动作名称 | Go 报表 |
| 启动方式 | 其他程序 |
| 执行目标 | ./bin/report.exe |
| 添加输入参数 | 名称：输入文件；类型：file；绑定：argument；命令参数：--input；必填；要求文件存在 |
| 添加并发参数 | 名称：并发数；类型：integer；绑定：argument；命令参数：--workers；默认：4；最小：1；最大：32 |

点击「保存并查看」，选择输入文件，调整并发数，检查命令预览后启动。这里直接执行业务 EXE，不执行 `go build`，运行此动作不需要 Go 编译器；程序所需的资源和本地库仍须保留。含空格路径会保持为一个参数，目标输入框和文件参数中不用自己加引号。

### Java：启动已经打包好的 JAR

假设 `dist/service.jar` 有可运行入口，业务支持 `--port 8080`，并且机器已准备好 Java。启动方式选择「高级命令」，将固定命令完整替换为以下五行，每行一个参数：

```text
java
-Xmx512m
-Dapp.mode=prod
-jar
./dist/service.jar
```

添加「端口」参数：类型 `integer`，绑定 `argument`，命令参数 `--port`，默认 `8080`，最小 `1`，最大 `65535`。「保存并查看」后，把端口改为 `9090`，实际命令结构为：

```text
java -Xmx512m -Dapp.mode=prod -jar ./dist/service.jar --port 9090
```

`-Xmx...`、`-D...` 是 JVM 参数，固定放在 **`-jar` 前**；表单填写的业务参数追加在 JAR 后。如果子进程 PATH 找不到 Java，将第一行替换为实际 `java.exe` 的完整路径，在编辑框中不要额外加引号。JAR 不能像 EXE 一样直接作为「其他程序」执行。

### 两个动作对应的完整 YAML

下面是一份完整配置示例。将其放在 `Launcher.exe` 旁，项目根目录下需有真实 `.venv`、`bin/report.exe` 和 `dist/service.jar`。已有配置时请在设置页审阅合并，不要直接覆盖。示例不附带这些业务程序。

```yaml
schema_version: 1
app:
  name: 打包程序示例
runtime:
  mode: existing
  project_dir: .
  venv: .venv
  requirements: ''
actions:
  - id: go_report
    label: Go 报表
    argv: ['./bin/report.exe']
    parameters: [input, workers]
  - id: java_service
    label: Java 服务
    argv: [java, -Xmx512m, '-Dapp.mode=prod', -jar, './dist/service.jar']
    parameters: [port]
parameters:
  - name: input
    label: 输入文件
    type: file
    argument: --input
    required: true
    must_exist: true
  - name: workers
    label: 并发数
    type: integer
    argument: --workers
    default: 4
    min: 1
    max: 32
  - name: port
    label: 端口
    type: integer
    argument: --port
    default: 8080
    min: 1
    max: 65535
```

固定 `argv` 决定程序和固定选项；动作的 `parameters` 列表决定追加哪些表单参数及其顺序。`name` 用于配置内部引用，`argument` 才是实际命令选项；可编辑选项不要重复写进固定 argv。所有动作的工作目录均为 `runtime.project_dir`，不会自动切到 EXE 所在目录。案例采用选项和值分开的语法；只接受 `--port=8080` 等形式的程序，参阅[详细配置说明](docs/LAUNCH_ACTIONS.md)。

## 使用 AI 生成启动配置

仓库提供 [generate-launcher-config skill](skills/generate-launcher-config/SKILL.md)：让 AI 根据 Python 项目的真实入口、命令行参数和环境生成 `launcher.yaml`。配置规范随 skill 一起提供，复制整个目录后可独立使用。

无需安装，也可直接给 AI 以下指令（把示例路径替换为实际路径）：

```text
读取 D:/tools/Python-Desktop-Launcher/skills/generate-launcher-config/SKILL.md，
按该 skill 为 D:/projects/my-python-app 生成 launcher.yaml。
从真实代码和文档确认启动入口、参数及其来源。
如果有 Launcher.Cli.exe，使用 check 验证配置。
不要运行业务代码，不要初始化环境或安装依赖。
```

经常使用时，将整个 `skills/generate-launcher-config` 文件夹复制到 AI 工具的 skills 目录。[Codex 当前文档](https://learn.chatgpt.com/docs/build-skills#where-codex-loads-local-skills)列出的个人目录为 `~/.agents/skills`，项目目录为 `<目标项目>/.agents/skills`；如果已安装版本使用其他 skills 位置，请按该版本的配置放置。发现该 skill 后输入：

```text
使用 $generate-launcher-config 为当前 Python 项目生成并验证 launcher.yaml。
```

skill 会检查已有配置是否被外部修改，并在替换前备份。入口不明确时，会询问实际启动命令，或只生成不含动作的环境绑定配置。无法运行启动器校验时会明确说明，并保留已有配置，另存候选文件供检查。

生成后，在**项目设置**中核对配置，在运行页查看**命令预览**，再主动启动动作。配置校验不代表业务依赖已就绪；初始化环境、同步依赖仍需单独确认。字段详情见[配置规范](docs/CONFIG.md)。

## 常见问题

**每次运行或安装库，都要填写 python.exe 吗？**

不用。Python 脚本和模块动作使用绑定的解释器。在项目终端优先使用 `python -m pip install <库名>`。环境本身必须包含 pip，某些 uv 环境不自带 pip；直接输入 `pip` 可能沿 PATH 找到其他环境。详见 [环境与接入说明](docs/INTEGRATION.md)。

**在哪里切换语言？**

左下角选择语言。首次跟随系统（中文系统使用中文，其余使用英文），之后记住选择，保存在 `%LOCALAPPDATA%/ProjectLauncher/preferences.json`。项目名称、参数值、业务日志和终端输出保持原文。

**保存设置会覆盖业务代码吗？**

设置写入 `launcher.yaml`，保存前校验、保留 `.bak`，检测到磁盘变化时拒绝覆盖。设置页不改写业务代码和依赖文件。应用配置修改前需结束任务和终端；YAML 注释和排版可能被规范化。

**终端是沙箱吗？**

不是。显式命令仍可离开项目环境。复杂全屏 TUI 可使用系统终端。不要把密钥写入配置默认值，详见 [安全边界](docs/SECURITY.md)。

**现在是正式稳定版吗？**

v1.1.0 程序未签名。已进行原生构建和自动测试，人工 DPI、IME、干净机器与完整业务流程验收尚未完成。详见 [准确的验证范围](docs/TESTING.md)。

## 更多文档与参与贡献

- [配置规范](docs/CONFIG.md) · [接入说明](docs/INTEGRATION.md)
- [二开指南](docs/DEVELOPMENT.md) · [测试与验收](docs/TESTING.md)
- [反馈问题或建议](https://github.com/JonGates/Python-Desktop-Launcher/issues)
- [许可证](LICENSE) · [第三方说明](THIRD_PARTY_NOTICES.md)

反馈时请附 Windows 版本、启动器版本、复现步骤与脱敏配置，不要上传密钥或私有业务日志。
