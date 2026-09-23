[CmdletBinding()]
param([string]$LauncherPath = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $LauncherPath) { $LauncherPath = Join-Path $root 'artifacts/portable/Launcher.exe' }
$LauncherPath = (Resolve-Path -LiteralPath $LauncherPath).Path
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$runRoot = Join-Path $root ('artifacts/portable-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$results = [Collections.Generic.List[string]]::new()
function Wait-Window($Process, [string]$Title) {
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    do {
        $Process.Refresh()
        if ($Process.HasExited) { throw "Launcher exited early: $($Process.ExitCode)" }
        $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
            [Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id))
        foreach ($window in $windows) { if ($window.Current.Name -like $Title) { return $window } }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Window not found: $Title"
}
function Control($Window, [string]$Id) {
    $control = $Window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
    if (-not $control) { throw "Control not found: $Id" }; return $control
}
function Close-Launcher($Process) {
    if (-not $Process.HasExited) {
        [void]$Process.CloseMainWindow()
        if (-not $Process.WaitForExit(10000)) { throw 'Launcher did not close normally.' }
    }
}
try {
    foreach ($scenario in @('已有环境 中文项目', '没有环境 中文项目', '取消接入')) {
        $project = Join-Path $runRoot $scenario
        New-Item -ItemType Directory -Path $project | Out-Null
        Copy-Item -LiteralPath $LauncherPath -Destination (Join-Path $project 'Launcher.exe')
        if ($scenario -eq '已有环境 中文项目') {
            & python -m venv --without-pip (Join-Path $project 'my env')
            if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated Python test environment.' }
        }
        # A different working directory proves startup uses the EXE's directory.
        $process = Start-Process -FilePath (Join-Path $project 'Launcher.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru
        try {
            $window = Wait-Window $process '首次接入*'
            $pathControl = Control $window 'ProjectPath'
            $value = $pathControl.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
            if ($value.Current.Value -ne $project) { throw 'Startup used the working directory instead of the EXE directory.' }
            $button = Control $window $(if ($scenario -eq '取消接入') { 'CancelButton' } else { 'CreateButton' })
            $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            $config = Join-Path $project 'launcher.yaml'
            if ($scenario -eq '取消接入') {
                if (-not $process.WaitForExit(10000)) { throw 'Cancel did not exit.' }
                if (Test-Path -LiteralPath $config) { throw 'Cancel wrote configuration.' }
            } else {
                [void](Wait-Window $process '* · Project Launcher')
                $process.Refresh()
                # Single-file .NET can statically link CoreCLR. Check the bundled WPF native runtime instead.
                $runtime = @($process.Modules | Where-Object ModuleName -eq 'wpfgfx_cor3.dll')
                if ($runtime.Count -ne 1 -or $runtime[0].FileName -notlike '*\.net\Launcher\*') { throw "Unexpected WPF runtime modules: $($runtime.FileName -join ', ')" }
                if (-not (Test-Path -LiteralPath $config)) { throw 'Setup did not save configuration.' }
                $yaml = Get-Content -LiteralPath $config -Raw
                if ($yaml -notmatch 'actions: \[\]') { throw 'First binding should create no launch actions.' }
                if ($scenario -eq '已有环境 中文项目' -and ($yaml -notmatch 'mode: existing' -or $yaml -notmatch 'venv: my env')) { throw 'Existing environment was not bound.' }
                if ($scenario -eq '没有环境 中文项目' -and (Test-Path (Join-Path $project '.venv'))) { throw 'Setup unexpectedly created an environment.' }
            }
        } catch { $results.Add("FAIL workflow: $_"); throw } finally { Close-Launcher $process }
        $results.Add("PASS copied single EXE: $scenario")
        if ($scenario -eq '已有环境 中文项目') {
            $config = Join-Path $project 'launcher.yaml'
            $renamed = Join-Path $project 'Launcher.yaml'
            Rename-Item -LiteralPath $config -NewName 'Launcher.yaml'
            $before = (Get-FileHash -LiteralPath $renamed).Hash
            $process = Start-Process -FilePath (Join-Path $project 'Launcher.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru
            try { [void](Wait-Window $process '* · Project Launcher') } finally { Close-Launcher $process }
            if ((Get-FileHash -LiteralPath $renamed).Hash -ne $before) { throw 'Reopening modified existing Launcher.yaml.' }
            $results.Add('PASS existing Launcher.yaml loads directly and stays byte-identical')
            $cli = Join-Path $root 'artifacts/portable/Launcher.Cli.exe'
            & $cli env --project $project --trust
            if ($LASTEXITCODE -ne 0) { throw 'Bound Python environment probe failed.' }
            $results.Add('PASS bound real Python environment probe')
        }
    }
    $results | Set-Content -LiteralPath (Join-Path $runRoot 'report.txt') -Encoding UTF8
    $results
    "Portable test report: $runRoot\report.txt"
} catch {
    $results.Add("FAIL $_")
    $results | Set-Content -LiteralPath (Join-Path $runRoot 'report.txt') -Encoding UTF8
    throw
}
