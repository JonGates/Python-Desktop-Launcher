# launcher.yaml 配置规范

配置沿用 `schema_version: 1`，UTF-8。开发者定义配置，普通用户在运行页填写值，用户态另存 `.launcher/csharp/user.json`。保存配置会整理格式，不保留注释；会保存上一个版本 `.bak` 并检查磁盘哈希，避免直接覆盖外部修改。

仅支持本文列出的字段。未知字段、重复键、不支持的 schema、YAML 锚点 / 别名 / 自定义标签会拒绝，而不是悄悄忽略。配置上限 1 MB、200 个参数、100 个动作、24 层嵌套。已有 v1 业务配置仍需在自己的项目验证；旧版 Python 用户状态不自动迁移。

## 基本结构

```yaml
schema_version: 1
app:
  name: 我的项目
  description: 这是项目的功能说明。
  version: 1.0.0
  output_dir: output
runtime:
  mode: venv
  project_dir: .
  venv: .venv
  python: ''
  requirements: ''
  shell: auto
  env: {}
actions:
  - id: run
    label: 运行项目
    argv: [python, main.py]
    parameters: []
    timeout_seconds: 0
parameters: []
```

`app.output_dir` 只控制「打开输出目录」按钮，不自动变成某个同名参数的当前值。目录不存在时提示，不自动创建；业务脚本负责自己的输出。

`runtime.project_dir` 相对于 YAML 所在目录；venv、requirements、文件 / 目录参数相对于业务项目根目录。接受绝对路径。不是相对于当前终端 `cd` 位置，也不是单文件 EXE 解包目录。环境目录不能是项目根目录、盘符根目录或启动器 `.launcher` 区域。

## 环境模式

| mode | 行为 |
|---|---|
| existing | 使用配置的已存在虚拟环境；不安装、不清空、不自动修复 |
| venv | 缺少环境时创建；优先用已安装 uv，否则用已有 Python；存在 requirements 时安装 |
| uv | 需要 uv 和 pyproject.toml；显式执行 uv sync；存在 uv.lock 时加 --locked |

`python` 只用于初始化解释器的选择，可为空；有 uv 时可填写其支持的 Python 要求，例如 `3.12`。没有 uv 时可指定 Python 完整路径；Windows 上版本号也可以交给已安装的 Python Launcher `py`。不把该字段当作任务环境缺失时的回退。

venv 模式借助 uv 创建时使用 `--seed`。uv 项目模式不为了 `pip` 命令额外改写业务依赖，所以环境可能没有 pip。没有项目 pip 时，终端中的裸 `pip` 可能从系统 PATH 找到另一个 pip；不要使用它，改用 `python -m pip`（会明确报告项目未安装 pip）或 `uv pip --python 项目解释器路径`。类似地，未安装在项目里的 pytest 也可能指向系统命令；优先 `python -m pytest`。可以使用 `uv pip` 管理包，项目依赖应继续在项目自身定义中维护。

不自动给任务执行 `uv sync`。初始化 / 同步必须显式确认，避免每次运行都联网或更改环境。业务脚本环境存在性与实际可用性分开：文件存在只是结构检查；环境页的检查解释器才实际启动 Python 验证 sys.prefix。

`shell` 支持 auto / powershell / pwsh / cmd。auto 优先找到 PowerShell 7，其次 Windows PowerShell，最后 CMD。内置终端上方的新建按钮可显式选种类；「系统终端」使用配置默认值。Python REPL 按钮直接使用所选 `.venv` 的解释器。

`runtime.env` 是普通字符串映射。不能覆盖 PATH、VIRTUAL_ENV、PYTHONHOME、PYTHONPATH、UV_PROJECT_ENVIRONMENT 等受保护变量，完整清单见 `ConfigValidator.ReservedEnvironment`。不要把密钥写进该明文配置。

## actions：按 argv 执行，而不是拼接 Shell

允许 `actions: []`，表示环境已绑定、尚未添加动作；运行页显示添加动作入口，不执行默认脚本。仍拒绝 `actions: null`。旧版 EXE 可能不支持空动作配置。

新版设置页以动作为单位展示参数列表，新动作自动生成内部 ID，中文名称放在 label。新增和编辑参数使用独立弹窗，取消不改变配置；保存后立即更新列表。新增参数自动绑定当前动作；移除参数只移除当前动作引用。编辑旧共享参数时自动复制为当前动作的独立参数，关联的显示条件一同跟随，其他动作保持不变。对使用全部参数的旧动作修改成员前，会确认转为显式列表；未触及成员时保留 null / [] 的语义。

设置页不再显示草稿预览。弹窗保存仅更新设置中的待保存配置，点击「保存并查看」才写入文件并进入对应动作的运行表单。保存失败停留在设置页。高级 argv 仍逐项执行，空字符串参数保持原义。运行页短参数采用自适应双列，路径和多行文本占满一行；参数区随内容收缩，大量参数内部滚动。

```yaml
actions:
  - id: run
    label: 正常执行
    argv: [python, main.py]
    parameters: [input, output, workers]
    timeout_seconds: 600
  - id: tests
    label: 运行测试
    argv: [python, -m, pytest, -q]
    parameters: []
  - id: snippet
    label: 查看解释器
    argv: [python, -c, 'import sys; print(sys.executable)']
    parameters: []
```

`id` 和参数 name 以英文字母 / 下划线开头，可含数字与短横线，必须唯一。`label` 是显示名称；参数 name 与 action id 不要求相同。

`parameters` **省略 / null** 表示使用全部定义，按 definitions 顺序追加；`[]` 表示不附加参数；显式列表按列表顺序追加。位置参数对顺序敏感。

`timeout_seconds` 是 C# 版新增可选项，0 表示不限时，最大 604800 秒。超时终止任务，不是业务函数级超时回调。

裸 `python` / `python.exe` / `python3` / `pythonw` 以及对应 exe 名映射到项目解释器；裸 pip / pip3 映射为同一 Python 的 `-m pip`。在 Windows 中不会因为某个名字碰巧存在于启动器进程的 PATH，就绕过项目 PATH；运行器会先在传给子进程的环境中解析普通可执行文件。

显式路径程序保留开发者选择。配置可以写其他程序，但项目环境仍必须存在；不是通用的无 Python 任意命令面板。直接 `.bat` / `.cmd` 不当作普通可执行文件启动；需要自己显式配置 CMD 并理解其解析规则。不要用代码字符串 `-c` 或 Shell 字符串拼接用户的表单内容。

“复制命令”得到的是脱敏诊断预览，采用 Windows argv 的展示方式；不能保证直接粘贴到 PowerShell、CMD、bash 都具有完全一致的语义。真正执行使用 `ProcessStartInfo.ArgumentList`。

## 九种参数

| type | 界面 / 传递值 |
|---|---|
| text | 单行文本，按原值传递，不 trim 业务内容 |
| integer | 整数文本输入，执行时校验 Int64 和范围 |
| number | 数值输入，有限数值、英文小数点 |
| boolean | 开关，见下方两种布尔规则 |
| select | 下拉菜单，值必须来自 options |
| file | 文件选择，传入完整路径 |
| directory | 目录选择，传入完整路径 |
| password | 密码输入，不持久化运行时值 |
| textarea | 多行文本，可包含换行，作为一个 argv 值或环境变量传入 |

例如：

```yaml
parameters:
  - name: input
    label: 输入文件
    description: 需要处理的 UTF-8 文件。
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
    advanced: true
```

通用字段：name、label、description、type、default、required、advanced、binding、argument、env_name、min/max、options、must_exist、secret、boolean_mode、false_argument、visible_when。

默认 label 为空则显示 name。空的可选参数不传；0 保留。布尔 false 不按“空值”丢弃，而按布尔模式决定。`required` 不把 false 强制成 true。

`must_exist` 检查 file / directory 类型匹配；未要求存在的 file 参数使用保存路径选择器。文件或目录参数变成绝对路径；text 绝不自动当路径转换。

下拉 options 是字符串列表；例如编号 `001`、值 `true` 应加引号，避免 YAML 把它们看成数字 / 布尔。参数设计器适合常规文本选项；包含换行的选项等边缘结构请直接使用 YAML，且核对 UI 能否正确表示。

## 三种绑定

```yaml
# 普通开关：--input value
- name: input
  type: file
  binding: argument
  argument: --input

# 位置参数：value
- name: source
  type: file
  binding: positional

# 仅传环境变量，不放进 argv
- name: token
  type: password
  binding: env
  env_name: MY_API_KEY
```

binding 默认 argument。env 绑定同样不能覆盖保留环境变量。`secret: true` 可把普通字段标记为不持久化、需要脱敏；password 自动具备该标记。

## 布尔规则

`boolean_mode: flag`（默认）对应 argparse store_true：

```yaml
- name: overwrite
  type: boolean
  argument: --overwrite
  default: false
```

true 只传 `--overwrite`；false 不传。需要明确关闭开关时可加 `false_argument: --no-overwrite`。

`boolean_mode: value`：

```yaml
- name: enabled
  type: boolean
  argument: --enabled
  boolean_mode: value
  default: true
```

分别传 `--enabled true` / `--enabled false`。布尔 env 绑定也传小写字符串。布尔位置参数必须使用 value 模式。

## 条件显示与高级参数

```yaml
- name: mode
  type: select
  argument: --mode
  options: [fast, custom]
  default: fast
- name: limit
  type: integer
  argument: --limit
  default: 100
  visible_when:
    mode: custom
```

多个比较是 AND，比较普通标量的标准文本值；字符串区分大小写，不执行表达式。拒绝不存在的引用、自引用或循环。条件不满足时既不显示、不校验必填，也不传参；条件满足但 advanced 折叠时仍正常传参。

切换动作时只显示该动作引用的参数。一个参数依赖另一个未被动作引用的参数时，使用保存值 / 默认值参与比较，但不会擅自把依赖参数追加到 argv；建议一起引用关联字段。

## 保存与用户状态

可视化编辑与完整 YAML 编辑是两种明确入口。处于完整 YAML 标签时，保存以该文本为准；编辑了可视化字段后，要先点「从表单生成 YAML」再继续在 YAML 标签编辑。反向导入会要求确认覆盖表单草稿。

多个工具同时修改配置时，哈希不一致会拒绝写入，提示重载；这不是跨设备分布式锁。不同别名 / 符号链接访问同一文件和外部工具的原子级竞态仍需自行避免。配置格式重排不保留注释，保留 `.bak` 和 Git 历史。

参数设计器对常规值提供可视化编辑，对未改动的复杂 options / visible_when 保留原配置值；编辑这两项时使用每行文本 / 每行 name=value 规则。没有任意嵌套对象、多选数组、表达式执行或 JSON Schema 全量兼容。
