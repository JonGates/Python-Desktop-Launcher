# Python Desktop Launcher schema 1

Reference for the repository's v1.0.0 implementation. Only the fields below are supported. YAML is UTF-8; unknown/duplicate keys, anchors, aliases and custom tags are rejected. Limits: 1 MB, 100 actions, 200 parameters, 24 nesting levels.

## Root and runtime

| Section | Fields |
|---|---|
| root | `schema_version: 1`, `app`, `runtime`, `actions`, `parameters` |
| app | `name` (required), `description`, `version`, `output_dir` |
| runtime | `mode` (`existing` / `venv` / `uv`), `project_dir`, `venv`, `python`, `requirements`, `shell` (`auto` / `powershell` / `pwsh` / `cmd`), `env` (string map) |
| action | `id`, `label`, `argv` (string array), `parameters` (names or null), `timeout_seconds` (0–604800; 0 = unlimited) |

`project_dir` is relative to the YAML location; `venv`, `requirements` and file/directory inputs are relative to that project root. Use portable relative paths when possible; single-quote Windows absolute paths. The environment directory cannot be the project root, a drive root or `.launcher`. `runtime.python` selects an interpreter only for initialization, not a business-task fallback. `requirements` may be empty. `uv` requires uv plus pyproject.toml and uses uv.lock when available. Initialization/sync is never automatic during configuration generation or task launch.

Bare `python`, `python.exe`, `python3`, `pythonw` and matching exe forms map to the bound interpreter. Bare pip/pip3 maps to that interpreter's `-m pip`. Other executables resolve using the child project's PATH. Use `argv: [python, -m, package]` for a verified module entry point. GUI actions are noninteractive; interactive programs belong in a project terminal. Never concatenate form data into shell commands or Python `-c` code.

IDs/names match `^[A-Za-z_][A-Za-z0-9_-]*$` and are unique within their respective lists. `actions: []` is valid; null is not. Per-action `parameters` omitted/null selects all definitions; `[]` selects none; explicit names select in that exact order.

## Packaged EXE and JAR actions

Every action currently checks for a bound Python virtual environment before execution, including EXE and Java actions. Do not claim standalone Go/Java project support without that environment. `runtime.mode` remains existing/venv/uv. Java and any EXE dependencies must separately exist; generation does not install them.

In Project settings → Launch actions, select **Other program** for a single EXE path such as `./bin/My Tool.exe`. Use **Advanced command** when fixed arguments are needed. That editor takes one argv element per line: no shell quoting around paths with spaces, no whole-command line, no accidental blank lines (they become empty arguments). YAML string quotes are syntax and do not become literal path quotes.

EXE action example (insert under actions; define input at top level as file + argument --input):

```yaml
- id: packaged_exe
  label: Packaged report
  argv: ['./bin/My Tool.exe']
  parameters: [input]
```

For a verified runnable `dist/My App.jar`, with documented application option `--port` accepting a separate value, the Advanced command editor is:

```text
java
-Xmx512m
-Dapp.mode=prod
-jar
./dist/My App.jar
```

Equivalent action: `argv: [java, -Xmx512m, '-Dapp.mode=prod', -jar, './dist/My App.jar']`, with `parameters: [port]`. Define port as integer, argument --port, default 8080, min 1, max 65535 if those constraints match the application. The final two tokens are `--port`, `8080`. JVM options must precede `-jar`; form parameters cannot be inserted before it. JARs without a valid entry point may need a documented classpath/main-class invocation instead; never guess one. Replace java with an explicit java.exe path when needed. JAVA_HOME alone is not executable lookup, and PATH cannot be overridden in runtime.env.

All actions run in runtime.project_dir, not automatically beside the EXE/JAR. No per-action cwd exists. Relative program paths with directory separators resolve against the project root; use `./Tool.exe` for a local file rather than a bare PATH lookup. Fixed argv does not expand environment variables or parameter placeholders. Keep packaged DLLs/resources according to the program's requirements.

Argument binding emits separate option/value tokens; it does not construct `--port=8080`. For an application requiring that single-token form, put a fixed value in argv or expose the whole token as positional text, explaining that numeric port validation is then unavailable. Only configure env secrets when the target actually reads that variable. GUI actions cannot accept console stdin. Configuration check does not verify EXE/JAR existence, Java compatibility or business readiness.

## Parameter definitions

Supported fields: `name`, `label`, `description`, `type`, `default`, `required`, `advanced`, `binding`, `argument`, `env_name`, `min`, `max`, `options`, `must_exist`, `secret`, `boolean_mode`, `false_argument`, `visible_when`.

| Type/binding | Meaning |
|---|---|
| text / textarea | One string, even when it contains spaces/newlines; textarea is not an argv array |
| integer / number | Int64 / finite numeric value; optional min/max |
| select | `options` is a string list, default must be one of them; quote numeric/boolean-looking strings |
| file / directory | Converted to absolute paths; `must_exist: true` checks path and type |
| password | Secret input; never persist a default secret |
| boolean | `boolean_mode: flag` by default: true emits argument only, false emits nothing (or false_argument); `value` emits lowercase true/false |
| binding: argument | Default; requires `argument`, produces option followed by value except boolean flag |
| binding: positional | Produces value only; boolean requires value mode |
| binding: env | Requires `env_name`; emits no argv; boolean uses lowercase true/false |

`secret: true` also protects ordinary inputs from preset persistence and known-value previews. Configuration itself is plaintext. Empty optional values are omitted; zero survives; required booleans may be false. Defaults must be scalars, not arrays/maps. `visible_when` maps other parameter names to scalar equality values (AND); no expressions, missing references, self references or cycles. Hidden parameters are not passed or validated as required; advanced collapsed fields still are. Include dependent fields together in new action lists.

Reserved env names (case-insensitive, both runtime.env and parameter env_name): PATH, PATHEXT, VIRTUAL_ENV, VIRTUAL_ENV_PROMPT, PYTHONHOME, PYTHONPATH, PYTHONSTARTUP, PYTHONUSERBASE, PYTHONNOUSERSITE, PYTHONUTF8, PYTHONIOENCODING, PYTHONUNBUFFERED, UV_PROJECT, UV_PROJECT_ENVIRONMENT, UV_PYTHON, CONDA_PREFIX, CONDA_DEFAULT_ENV, COMSPEC.

## Example: verified report CLI

Use this shape only if the project actually has `tools/report.py`, positional INPUT, `--format` choices csv/json, `--verbose` store_true, `.venv` and requirements.txt. No real input filename is assumed.

```yaml
schema_version: 1
app:
  name: Report Generator
  description: Generate a report from an input file.
runtime:
  mode: existing
  project_dir: .
  venv: .venv
  python: ''
  requirements: requirements.txt
  shell: auto
  env: {}
actions:
  - id: report
    label: Generate report
    argv: [python, tools/report.py]
    parameters: [input, format, verbose]
    timeout_seconds: 0
parameters:
  - name: input
    label: Input file
    type: file
    binding: positional
    required: true
    must_exist: true
  - name: format
    label: Output format
    type: select
    argument: --format
    options: [csv, json]
    default: csv
  - name: verbose
    label: Verbose logging
    type: boolean
    argument: --verbose
    boolean_mode: flag
    default: false
```

For INPUT `D:\data\input.csv` and verbose true, the argument tokens after the bound interpreter are `tools/report.py`, `D:\data\input.csv`, `--format`, `csv`, `--verbose`. A path containing spaces remains one token. The launcher's displayed command preview is diagnostic Windows argv notation, not a universal shell paste command.
