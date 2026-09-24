# Python Desktop Launcher

[English](README.md) | [简体中文](README.zh-CN.md)

**One Python project. One desktop launcher. Multiple launch actions.**

Turn project commands into forms. Copy `Launcher.exe` into your Python project, bind its environment, and configure actions and parameters—without changing business code.

Windows x64 / x86 · Native C# / WPF · Chinese / English · MIT · **v1.0.0**

![Native WPF action settings](docs/images/launcher-en.png)

*Actual native WPF rendering from an isolated example project—not a web mockup.*

## Why use it?

- **Your own environment:** detect and bind a virtual environment; no launcher UI dependencies in your business `.venv`.
- **Action-specific forms:** separate service, app and script actions, each with parameters and presets.
- **Visible execution:** command preview, live logs, exit status, history and confirmed stop controls.
- **Real terminals:** persistent PowerShell, CMD and Python sessions in the project environment.
- **Live language switching:** Chinese / English without losing drafts or restarting jobs and terminals.

One project per launcher. Currently **one GUI job at a time**, with multiple terminal sessions. Not an IDE, sandbox or global project dashboard.

## Quick start

### 1. Get Launcher.exe

Get the **portable ZIP** from [Releases](https://github.com/JonGates/Python-Desktop-Launcher/releases). Choose **win-x64** for 64-bit Windows (recommended), or **win-x86** for 32-bit Windows. Extract it and keep its licenses.

To build from source instead, use Windows and the **.NET 10 SDK**:

```powershell
git clone https://github.com/JonGates/Python-Desktop-Launcher.git
cd Python-Desktop-Launcher
.\Build.cmd
```

Output: `artifacts/portable/Launcher.exe`. This is a self-contained single-file build; the project still needs its own Python environment. [Build details](docs/BUILD_WINDOWS.md).

### 2. Copy it into your project

```text
my-project/
├── Launcher.exe       ← copy here
├── .venv/             ← your project's environment
├── server.py
└── requirements.txt
```

Do not copy this repository's environment, demo files or old launcher runtime. Preserve the supplied licenses when redistributing the launcher.

### 3. Bind the environment

Double-click `Launcher.exe`.

- Existing `launcher.yaml` / `Launcher.yaml`: loads directly.
- No configuration: confirm the project directory, select the detected environment, then bind and create configuration.
- No environment: bind first, then use **Environment** to explicitly initialize one and install dependencies.

No entry file is required during binding. Detection does not execute project code. Missing environments never silently fall back to system Python.

### 4. Add an action and parameters

Open **Project settings → Launch actions → Add action**.

For a script that supports `python server.py --port 8000`:

| Setting | Value |
|---|---|
| Action name | API service |
| Launch type | Python script |
| Target | server.py |
| Add parameter → Name | Port |
| Command argument | --port |
| Type / Default | integer / 8000 |

Add and edit parameters in the dialog. Each action owns its parameters. Options must match your script; the launcher does not invent CLI options or automatically parse arbitrary Python code.

Click **Save and view** to see the action's parameter form immediately.

### 5. Run

Select an action under **Run project**, adjust its values and click **Start action**. The sidebar's green triangle also starts it; the red stop circle asks for confirmation before terminating the job.

Configuration trust and dependency changes require confirmation. Errors use dismissible overlay notifications without pushing the editor down.

## Generate configuration with AI

For packaged applications, use **Other program** with the EXE path, or **Advanced command** with one token per line: `java`, JVM options, `-jar`, and the JAR path. Add editable application inputs through the action's parameter form; they are appended after the fixed command. JVM options must stay before `-jar`. All actions currently still require a bound Python virtual environment. See the [step-by-step EXE/JAR guide (Chinese)](docs/LAUNCH_ACTIONS.md) and [complete YAML example](docs/examples/packaged-apps.yaml).

The included [generate-launcher-config skill](skills/generate-launcher-config/SKILL.md) reads your Python project's entry points, CLI definitions and environment to generate `launcher.yaml`. Its bundled schema reference works independently of this repository.

Give your AI assistant the skill path and target project (replace these example paths):

```text
Read D:/tools/Python-Desktop-Launcher/skills/generate-launcher-config/SKILL.md.
Use it to generate launcher.yaml for D:/projects/my-python-app.
Find the actual entry points and parameters, and explain their source locations.
Validate with Launcher.Cli.exe check if available.
Do not run business code or initialize/install dependencies.
```

For repeated use, copy the entire `skills/generate-launcher-config` folder into your AI tool's skills directory. Current [Codex skill documentation](https://learn.chatgpt.com/docs/build-skills#where-codex-loads-local-skills) lists `~/.agents/skills` for personal use and `<target-project>/.agents/skills` for project use. If your installed version uses another skills location, use that configured location. Once the skill is available, invoke:

```text
Use $generate-launcher-config to generate and validate launcher.yaml for this Python project.
```

The skill checks for concurrent edits and backs up before replacing existing configuration. Unknown entry points require clarification or an environment-only configuration with no actions. If launcher validation is unavailable, it reports that limitation and leaves existing configuration untouched, producing a candidate for review.

Review the result in **Project settings**, inspect the **command preview**, then explicitly start an action. Configuration validation does not prove dependency readiness; environment initialization/synchronization remains a separate confirmed action. See [configuration rules](docs/CONFIG.md).

## FAQ

**Do I need a python.exe path for each command or package installation?**

No. Python-script and Python-module actions use the bound interpreter. In the project terminal, prefer `python -m pip install <package>`. The environment must contain pip; some uv environments do not. Bare `pip` may resolve elsewhere on PATH. See [integration and environments](docs/INTEGRATION.md).

**Where is the language setting?**

Bottom left. First launch follows the system (Chinese → Chinese; otherwise English), then remembers your choice in `%LOCALAPPDATA%/ProjectLauncher/preferences.json`. Project names, parameter values, business logs and terminal output remain unchanged.

**Will saving overwrite my code?**

Settings write `launcher.yaml`, validate first, keep a `.bak` and reject stale disk changes. Settings do not rewrite business code or dependency files. Stop jobs and terminal sessions before applying changed configuration. YAML comments/formatting may be normalized.

**Is the terminal a sandbox?**

No. Explicit commands can leave the project environment. Use the system-terminal option for complex full-screen TUI programs. Do not put secrets in configuration defaults. See [security boundaries](docs/SECURITY.md).

**Is this production-ready?**

v1.0.0 binaries are unsigned. Native builds and automated tests have been run; manual DPI, IME, clean-machine and full business-workflow acceptance remain outstanding. See [exact verification scope](docs/TESTING.md).

## Learn more & contribute

- [Configuration](docs/CONFIG.md) · [Integration](docs/INTEGRATION.md)
- [Development](docs/DEVELOPMENT.md) · [Testing](docs/TESTING.md)
- [Report a bug or suggest a feature](https://github.com/JonGates/Python-Desktop-Launcher/issues)
- [License](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

Include Windows version, launcher version, reproduction steps and sanitized configuration in bug reports. Never upload secrets or private business logs.
