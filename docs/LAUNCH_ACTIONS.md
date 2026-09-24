# 启动动作与参数配置：Python、EXE 和 JAR

本页说明当前版本已有能力，不代表新增 Go / Java 运行模式。**所有动作（包括 EXE、JAR）执行前仍要求绑定的 Python 虚拟环境存在。** `runtime.mode` 只能是 `existing`、`venv` 或 `uv`；没有 `exe`、`java`、`go` 模式。EXE / Java 本身的运行依赖也必须准备好，启动器不会自动安装 Java、编译源码或补齐 DLL。

## 先拆分一条能运行的命令

启动器按下面的顺序执行：

```text
固定 argv（第一个元素是程序） + 当前动作的表单参数（按引用顺序追加）
```

环境变量参数只进入子进程环境，不追加到命令行。名称与选项必须来自实际程序的使用说明或代码；添加表单不会给程序增加新功能。

| 配置内容 | 放在哪里 | 示例 |
|---|---|---|
| 程序、脚本 / JAR 路径、固定子命令 | 启动动作的目标或固定 argv | `./bin/My Tool.exe`、`java`、`-jar` |
| 固定启动选项 | 「高级命令」固定 argv | `-Xmx512m`、`-Dapp.mode=prod` |
| 用户每次可修改的业务输入 | 当前动作 → 添加参数 | 输入文件、端口 |
| 显示名称 | 动作 label / 参数 label | 生成报表、服务端口 |
| 内部引用名称 | 动作 id / 参数 name | `report_exe`、`service_port` |

参数的 `name` 不是程序的命令选项。比如 `name: service_port` 只用于配置引用，`argument: --port` 才会传给程序。可视化编辑器会生成内部名称；中文填在显示名称中。

## 在设置页选择启动类型

打开「项目设置 → 启动动作 → 添加动作」。

| 启动类型 | 目标 / 固定命令 | 用途 |
|---|---|---|
| Python 脚本 | `server.py` | 生成 `python server.py` |
| Python 模块 | `package.cli` | 生成 `python -m package.cli` |
| 其他程序 | `./bin/My Tool.exe` | 单个程序路径，其后可追加表单参数 |
| 高级命令 | 每行一个 argv 元素 | EXE 固定子命令、Java/JAR、其他多元素命令 |

「高级命令」中每行是一个完整参数，**不要给含空格的路径额外加引号，不要把整条命令写在一行，不要留无意义空行**；空行会作为空字符串参数保留。切换启动类型会确认替换固定命令，应在填写前选好类型。

YAML 中则用字符串数组，含空格或反斜杠的路径可用 YAML 单引号包围，例如 `argv: ['./bin/My Tool.exe']`。这对引号是 YAML 语法，不属于传给程序的路径。不要将 `"` 作为路径的一部分。

## 启动已打包的 EXE

假设项目中已有 `bin/My Tool.exe`，程序明确支持 `--input <文件>`。这是业务 EXE，不是启动器自身的 `Launcher.exe`；Go、Python 或其他语言打包的 Windows EXE 都按普通程序处理。

1. 启动类型选「其他程序」。目标填写 `./bin/My Tool.exe`，或通过浏览选择实际文件。
2. 添加参数：显示名称「输入文件」，类型 `file`，绑定 `argument`，命令参数 `--input`，必填，要求文件存在。
3. 参数弹窗保存后，点「保存并查看」。在运行页选择输入文件并检查命令预览，再启动动作。

如果 EXE 还要求固定子命令 `convert`，使用「高级命令」，内容为：

```text
./bin/My Tool.exe
convert
```

此时 `--input` 和所选路径由表单追加，不要再写进固定命令。没有业务参数时，不必添加表单字段，在 YAML 中写 `parameters: []`。

程序的工作目录始终为 `runtime.project_dir`，不会自动切到 EXE 所在目录。如果程序依赖当前目录下的资源，先确认其要求，再配置项目根目录或使用其真实支持的资源路径选项。所有动作共用项目根目录，当前没有逐动作 `cwd` 字段。发布物旁的 DLL、配置和资源须按该程序要求保留。

## 启动已打包的 JAR

JAR 是交给 Java 读取的文件，不能作为「其他程序」直接执行。Java 命令的顺序是 `java [JVM选项] -jar 文件.jar [业务参数]`；JAR 需要有可启动入口。普通库 JAR 不一定能用 `-jar` 运行。见 [Java 官方命令说明](https://docs.oracle.com/en/java/javase/21/docs/specs/man/java.html)。

假设项目已有 `dist/My App.jar`，其业务明确接受两个独立参数 `--port 8080`。启动类型选择「高级命令」，逐行填写：

```text
java
-Xmx512m
-Dapp.mode=prod
-jar
./dist/My App.jar
```

添加业务参数：显示名称「服务端口」，类型 `integer`，命令参数 `--port`，默认值 `8080`，范围 `1` 到 `65535`。实际执行结构为：

```text
java | -Xmx512m | -Dapp.mode=prod | -jar | ./dist/My App.jar | --port | 8080
```

这里 `|` 仅表示参数边界，不要输入到配置中。**JVM 参数必须留在固定 argv 的 `-jar` 前面**；把 `-Xmx512m` 或 `-D...` 放进普通业务参数表单，会追加到 JAR 后面，成为业务参数。当前不支持将表单值插入固定 argv 中间，也不替换 `{port}`、`${port}` 或 `%JAVA_HOME%`。

若 `java` 无法从子进程 PATH 找到，或者要指定 Java 版本，把第一行替换成真实路径，如 `C:\Program Files\Java\jdk-21\bin\java.exe`。这只是路径示例，不表示启动器会安装该版本。仅设置 `JAVA_HOME` 不会让启动器自动查找它下面的 java.exe；`runtime.env` 也禁止覆盖 PATH。要看标准输出 / 错误日志，使用 `java.exe`。

没有可执行 JAR 入口但有已确认的主类命令时，可按真实说明配置 `java`、`-cp`、类路径、主类，每项一行；不要猜测主类，也不要混用 `-jar` 与期望生效的外部 classpath。

## 表单参数如何传递

| 程序需要的形态 | 参数配置 | 实际追加结果 |
|---|---|---|
| `--port 8080` | integer，binding: argument，argument: --port | 两个元素 `--port`、`8080` |
| 单独一个文件路径 | file，binding: positional | 一个绝对文件路径元素 |
| 启用时只加 `--verbose` | boolean，boolean_mode: flag | true 加开关；false 不加 |
| `--enabled true/false` | boolean，boolean_mode: value | 选项和值两个元素 |
| 应用读取 `APP_TOKEN` | password，binding: env，env_name: APP_TOKEN | 只设置子进程环境变量 |

`argument` 填选项本身，不要填「选项 + 默认值」。路径由 file / directory 字段转换成绝对路径，含空格仍是一个元素；text / textarea 不自动拆词。多个位置参数按动作的 `parameters` 列表顺序传递。

若程序**只接受**单元素 `--port=8080`，当前 argument 绑定不会自动拼接等号；把 `argument` 写成 `--port=` 仍会产生两个元素。固定值可直接写进 argv；需要可编辑时，可用一个 positional text 字段填写完整的 `--port=8080`，但它只受文本校验，不再具备独立端口整数校验。不要假定所有 JAR 都接受 `--port`，Spring 等框架的选项也应以应用实际文档为准。

可选空值不传，数字 0 保留，布尔 false 按其模式处理。未设默认值时不会替程序推断默认值；必填文件应由用户选择真实路径。password / secret 不应设置明文密钥默认值，也不会保存到预设。参数定义与运行时填写值是两层：保存表单预设不改变固定 argv。

YAML 顶层 `parameters` 是定义列表；每个动作的 `parameters` 是名称引用列表。新配置建议显式引用；`[]` 表示无参数，省略 / null 表示引用全部定义，不能混淆。

## 完整示例与校验

[packaged-apps.yaml](examples/packaged-apps.yaml) 包含独立的 EXE 和 JAR 动作以及它们各自的参数。它是字段示例，不附带业务程序；替换路径和选项后才能用于实际项目。不要覆盖已有 launcher.yaml，先在同目录另存候选或通过设置页审阅导入。

```powershell
& 'C:\tools\Launcher.Cli.exe' check --project 'D:\my-project\launcher.candidate.yaml'
```

`check` 校验配置结构，不启动 EXE / Java，不保证程序文件、Java 版本、JAR 入口或业务依赖可用。运行前仍须准备并绑定 Python 环境；没有环境时要由用户在环境页明确初始化并确认，不能创建假环境文件绕过检查。

运行页命令不支持 stdin 交互；需要控制台输入的程序使用项目终端。没有日志不一定是启动失败，GUI EXE 可能没有 stdout；业务进程若启动另一程序后立即退出，启动器状态以它实际观察到的进程为准，不等于完成业务验收。
