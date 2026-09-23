# 给现有 Python 项目加 C# 启动器

## 一、正确的复制方式

需要的是**发布后的单文件** `Launcher.exe`，不是 C# 源码目录、开发目录里的 apphost，也不是旧 Python 版 `Start.bat`。

第一次先在源码目录运行 `Build.cmd`。成功后从 `artifacts/portable` 取 `Launcher.exe`。日常不修改启动器源码时，所有业务项目可复用这同一份 EXE，不需要每个项目重新编译。

把它复制到目标项目根目录，双击。已有合法 schema v1 的 `launcher.yaml`（Windows 下也支持 `Launcher.yaml`）时直接加载并保留原内容。没有配置时显示首次接入窗口：

1. 项目目录默认绑定 EXE 所在目录，不使用启动时的工作目录。
2. 检测项目根目录下一层的虚拟环境；一个候选时预选，多个候选时要求选择。可通过「选择目录」绑定项目外的环境。
3. 点击「绑定并进入」，保存 `launcher.yaml` 并进入主窗口。不要求入口文件，不生成默认动作。
4. 到「项目设置 → 启动动作」添加执行目标与参数；参数通过弹窗新增、编辑，保存后显示在当前动作的列表。点击「保存并查看」应用配置。没有环境时先到环境管理创建。

检测依据是 `pyvenv.cfg` 和 `Scripts/python.exe`，适用于 venv / virtualenv / uv 虚拟环境，不包含 Conda 环境。项目内部环境保存相对路径，外部环境保存绝对路径。只检查结构，不运行解释器；需要时再到环境管理检查实际解释器。取消不会写入配置；不会自动修改 pyproject.toml、requirements.txt、Python 源码或虚拟环境。空动作配置使用 actions: []，旧版 EXE 可能拒绝读取，请使用新版启动器。

每个项目独立放置 Launcher.exe，界面不再提供跨项目接入或切换入口。升级前请关闭启动器并备份旧 EXE，再复制新版本；保留项目已有的 Launcher.yaml。多个启动动作统一显示在左侧「运行项目」的可折叠菜单中。

接入脚本同样需要预先构建的 EXE：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-Into.ps1 -Target "D:\code\existing-project" -Entry "app.py" -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-Into.ps1 -Target "D:\code\existing-project" -Entry "app.py"
```

`-WhatIf` 只显示计划。脚本保留已有 YAML 的原始字节，在应用内加载时再做完整校验。

## 二、按已有环境选择模式

已有可用 `.venv`：

```yaml
runtime:
  mode: existing
  project_dir: .
  venv: .venv
```

uv 项目：

```yaml
runtime:
  mode: uv
  project_dir: .
  venv: .venv
```

在环境管理中点创建 / 同步，看到实际 `uv sync --project ...` 命令后确认。有锁文件时使用 `--locked`；不帮你静默更新锁文件。自定义 `.venv` 通过 `UV_PROJECT_ENVIRONMENT` 传入 uv，环境同步会尊重 uv 的规则，可能移除项目未声明的包。

传统依赖清单：

```yaml
runtime:
  mode: venv
  project_dir: .
  venv: .venv
  requirements: requirements.txt
```

不需要安装包的脚本，把 requirements 留空。venv 模式优先使用已安装的 uv；没有 uv 时使用已安装的 Python 创建环境。已有不完整非空环境不会被自动删除，先手动备份改名或更换环境路径。

不要把旧项目 `.venv` 整个拷到新路径冒充新环境。已有解释器文件存在也不代表环境可用；点「检查解释器」核对真正的 sys.prefix。

## 三、核对实际启动入口

| 原启动方式 | actions 中的 argv |
|---|---|
| `python main.py` | `[python, main.py]` |
| `python app.py` | `[python, app.py]` |
| `python -m invoice_app` | `[python, -m, invoice_app]` |
| `python server/run.py` | `[python, server/run.py]` |
| `python -m pytest -q` | `[python, -m, pytest, -q]`，并加 `parameters: []` |

`argv` 的每个项目是一整个参数，不要把完整命令塞成一项，不要给路径人为加入包裹引号。输入里的空格、中文和 `&` 按数据传递。需要 Shell 语法的命令必须自己显式选择 Shell；这与一般 Python argv 的转义规则不同，不应拼接用户输入。

uv 项目中常见的 `uv run python main.py` 通常可配置为 `mode: uv` 加 `[python, main.py]`，先显式同步，再用项目解释器运行；这是本启动器确保任务和终端用同一 `.venv` 的默认方式。如果原命令使用 uv 的临时依赖、脚本元数据或其他特殊功能，应自行核对，不能机械替换所有 uv 命令。

初始化 `python` 字段不是日常解释器自由回退选项。GUI 任务中的裸 `python` / `pip` 会替换成项目解释器；其他命令在项目 PATH 里查找。绝对路径指定的外部程序仍是可信配置的明确选择，不被自动改写。

## 四、可视化添加参数

进入「项目设置 → 参数设计器」，添加字段并填写：机器参数名、中文名称、类型、参数开关、默认值、说明。再到「启动命令」将参数引用加到动作，或者勾选「使用全部表单参数」。保存并应用后到运行页面即可看到控件。

注意 `parameters: []` 表示这个动作不接收表单参数。仅仅添加了一个字段，但动作仍保持 `[]`，该字段不会出现在该动作下，也不会被传入。

文件和目录参数可拖拽。高级参数只影响折叠，不影响传参；条件显示不满足时才不传参数。布尔开关要区分 `--flag` 与 `--flag true/false`，详见配置规范。

## 五、旧 Python 壳升级

建议在项目副本先试：保留现有 `launcher.yaml` 和业务 `.venv`，新增 `artifacts/portable/Launcher.exe`。此次源码实现了 v1 已有九种类型、参数绑定和条件显示，包内也保留了上一版的真实配置作为 C# 回归 fixture；**C# 自动测试已通过，但没有覆盖所有旧项目，不能把“保留 fixture”描述成全面兼容。**

第一次成功运行 C# 版后，旧 `Start.bat`、`launcher.pyw`、`.launcher/runtime` 可按需手动归档；启动器不会自动删它们。新数据写在 `.launcher/csharp`，不迁移旧版预设和日志。首次用 C# 设置页保存前会备份原 YAML 为 `.bak`，但一次备份不等于版本管理。

所有链接路径以配置位置 / project_dir 解析，不依赖你从哪个终端或快捷方式打开 EXE。保存新设置时，不允许任务或项目终端仍在使用旧环境。

## 六、最后验证这三件事

项目终端中运行：

```powershell
python -c "import sys,os; print(sys.executable); print(sys.prefix); print(os.getcwd())"
```

解释器和 sys.prefix 指向目标项目 `.venv`，当前目录是目标业务根目录。然后用最小输入跑一次原来的业务命令，再从 GUI 填相同参数运行；比较退出码和产物。

启动器对任意第三方脚本不能自动保证业务输出正确，也无法在没有任务协议时知道真实完成百分比。GUI 普通任务不接收 stdin；需要输入或持续交互的项目使用项目终端。
