# Change log

## Action settings split layout

- Replaced the action dropdown with a persistent left-side list and independently scrolling configuration panel.
- Moved action add/delete controls below the list; preserved parameter dialogs and bottom-page save controls.
- Verified action switching preserves edits and invalid fields continue to block disruptive selection changes.

## Theme controls and project identity

- Fixed tab header right-edge clipping by preserving inter-tab spacing.
- Added compact rounded theme-aware vertical and horizontal scrollbars, including hover/drag states.
- Sidebar header now shows the project's name and description; the bottom-left status area shows its version.
- Moved Add parameter to the right side of the parameter section heading.

## Sidebar action quick controls

- Added a green start triangle and red stop circle beside each action, with operation tooltips.
- Start uses the current action form values and existing trust/validation checks. Stop retains confirmation and only targets the running action.
- Added a native WPF workflow test using a real isolated child process, including stop cancellation and completion reset.

## Action parameter dialogs and compact layout

- Removed draft preview; added parameter lists and independent add/edit dialogs with cancellation isolation.
- Shared parameters and dependent conditions are copied on edit for the current action only.
- Run forms use compact responsive columns. The footer now links to GitHub as “Python-Desktop-Launcher 官网”.

## Unified action editor and environment-only setup

- First-run binding no longer asks for a Python entry or invents a launch action; empty projects are supported by GUI, config validation and CLI diagnostics.
- Added action-local parameter editing, automatic identifiers, script/module/program targets, advanced argv preservation, live draft form/command preview and save-to-run navigation.
- Added explicit protection for shared parameters and all-parameter membership changes. Invalid draft edits block disruptive UI actions instead of being silently discarded.
- Added native editor workflow regressions and dark/light minimum-window captures. No multi-action concurrent execution or automatic business dependency installation was added.

## Compact action navigation

- Replaced cross-project GUI controls with an expandable Run navigation group and selectable launch actions.
- Added a compact fixed execution toolbar, collapsible command preview, and action-specific parameter display. Execution remains single-task.
- Added native WPF checks for action selection, parameter retention, collapse, and minimum-window layouts in both themes.

## First-run environment binding and Windows executable

- Added a native first-run window that discovers project virtual environments, accepts an external environment directory, confirms a Python entry, and creates configuration only on confirmation. Existing configuration stays unchanged.
- Added 11 Core regressions, six setup render/workflow cases, cancellation and concurrent-config UI checks, and a copied-single-EXE automation test with real Python environment probing.
- Fixed reentrant WPF window closing when there are no terminal sessions. Fixed PowerShell license-cache discovery and included the version-pinned YamlDotNet license for packaging.
- Completed Windows x64 self-contained GUI / CLI publishing and portable / demo ZIP generation. See docs/TESTING.md for current results and remaining manual checks.

## 2.0.0-preview.1 — C# source delivery

- Replaced the Python GUI implementation with native WPF source and reusable Core / Windows / Desktop / CLI modules.
- Four independent pages with dark/light themes and a visual parameter/action editor.
- V1 YAML field support, exact project interpreter mapping for parameterized tasks, separate user state, confirmation-gated environment changes.
- Persistent ConPTY sessions, bounded VT model, output limits, multiline paste confirmation, native terminal fallback.
- CLI commands, safe existing-project integration, build/test scripts and Windows CI definition.
- Demo and docs included. The previous Python v1 config is included as a regression fixture.
- First Windows verification completed with .NET SDK 10.0.204: Release build, 62 Core specs, 4 Windows/ConPTY specs, and 8 native WPF render checks passed. Fixed WPF implicit `System.IO` imports, redirected-parent ConPTY input, and the progress binding startup crash. Self-contained EXE publishing and the separate manual acceptance checklist remain incomplete; see docs/TESTING.md.
