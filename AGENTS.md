# Project Launcher contributor contract

Read README.md, docs/CONFIG.md, docs/DEVELOPMENT.md and docs/TESTING.md before editing.

This is a Windows-native, single-project C# / WPF launcher. Do not replace it with a web
server, WebView, Electron or a global project dashboard. Settings remain a separate page.
The executable is independent of the target Python environment. Do not install launcher
UI libraries into the business .venv or import business Python modules into the launcher.

The source delivery is 2.0.0-preview.1 and has NOT been compiled/tested on Windows in the
original generation environment. Establish a real Windows build before promising a release.

Execution invariants:
- Use argument arrays and explicit child environments, never interpolate form values into a shell.
- Resolve executables with the child project PATH, not the launcher's ambient PATH.
- Missing .venv is an error, not permission to use system Python for a business task.
- Initializing/synchronizing dependencies requires an explicit user action and confirmation.
- Null actions.parameters = all definitions; [] = none. Preserve this distinction.
- Protect reserved environment variables. Password/secret values must not persist in presets.
- Back up config, detect stale disk hashes, validate before writing. No destructive auto-repair.
- Close running jobs/sessions before applying a changed configuration.
- Keep GUI job execution noninteractive; use ConPTY sessions for persistent shell / REPL state.
- Do not send Run-page commands into an arbitrary existing terminal/REPL.
- WPF updates are on the dispatcher. Blocking ConPTY reads/writes/close run off the UI thread.
- Protect terminal output bounds and multi-line paste. Ignore OSC clipboard/URL side effects.
- Do not call Process.Kill "pause". Real progress requires actual task events.
- Never overwrite unrelated project files or copy .venv / old launcher runtime into a target.

Testing:
Add failing tests for new behavior, implement, run the complete suite and report exact results.
The test projects are executable specs, not xUnit discovery: run `dotnet run --project tests/...`.
Run tools/Test-Windows.ps1 for native tests + WPF render checks, then manual DPI/IME/workflow tests.
Never present static HTML preview screenshots as native WPF runtime screenshots.
Do not fabricate SDK availability, compiler results, test counts, signed executables or CI runs.

Keep source files focused. Use the theme dictionary keys in both themes. Don't bundle fonts.
The Core project cannot reference WPF; the Windows project cannot depend on Desktop.

Delivery: after each requested upgrade, run verification and create a Git commit containing
the relevant changes. Report the commit hash; do not imply a push occurred unless verified.
