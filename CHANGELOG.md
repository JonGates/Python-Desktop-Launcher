# Change log

## 2.0.0-preview.1 — C# source delivery

- Replaced the Python GUI implementation with native WPF source and reusable Core / Windows / Desktop / CLI modules.
- Four independent pages with dark/light themes and a visual parameter/action editor.
- V1 YAML field support, exact project interpreter mapping for parameterized tasks, separate user state, confirmation-gated environment changes.
- Persistent ConPTY sessions, bounded VT model, output limits, multiline paste confirmation, native terminal fallback.
- CLI commands, safe existing-project integration, build/test scripts and Windows CI definition.
- Demo and docs included. The previous Python v1 config is included as a regression fixture.
- First Windows verification completed with .NET SDK 10.0.204: Release build, 62 Core specs, 4 Windows/ConPTY specs, and 8 native WPF render checks passed. Fixed WPF implicit `System.IO` imports, redirected-parent ConPTY input, and the progress binding startup crash. Self-contained EXE publishing and the separate manual acceptance checklist remain incomplete; see docs/TESTING.md.
