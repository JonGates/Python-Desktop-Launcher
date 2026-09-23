[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][string]$Target,
    [string]$Entry = '',
    [string]$Launcher = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Launcher) { $Launcher = Join-Path $root 'artifacts/portable/Launcher.exe' }
if (-not (Test-Path -LiteralPath $Launcher -PathType Leaf)) { throw 'Published Launcher.exe not found. Run Build.cmd first, or specify -Launcher.' }
if (-not (Test-Path -LiteralPath $Target -PathType Container)) { throw 'Target must be an existing project folder.' }
$Target = (Resolve-Path -LiteralPath $Target).Path
$destination = Join-Path $Target 'Launcher.exe'
$config = Join-Path $Target 'launcher.yaml'
if (Test-Path -LiteralPath $destination) { throw 'Target already contains Launcher.exe. Nothing was copied. Back up and rename the old executable before an upgrade.' }
if (-not $Entry) {
    foreach ($candidate in @('main.py','app.py','run.py','server.py')) { if (Test-Path (Join-Path $Target $candidate) -PathType Leaf) { $Entry = $candidate; break } }
    if (-not $Entry) { $Entry = 'main.py' }
}
$mode = if (Test-Path (Join-Path $Target 'uv.lock')) { 'uv' } elseif (Test-Path (Join-Path $Target '.venv/pyvenv.cfg')) { 'existing' } else { 'venv' }
$requirements = if ($mode -eq 'venv' -and (Test-Path (Join-Path $Target 'requirements.txt'))) { 'requirements.txt' } else { '' }
$nameJson = ConvertTo-Json ([IO.Path]::GetFileName($Target)) -Compress
$entryJson = ConvertTo-Json $Entry -Compress
$requirementsJson = ConvertTo-Json $requirements -Compress
$yaml = @"
schema_version: 1
app:
  name: $nameJson
  description: 'Configure the entry and parameters on the Settings page.'
  version: '1.0.0'
runtime:
  mode: $mode
  project_dir: .
  venv: .venv
  requirements: $requirementsJson
  shell: auto
actions:
  - id: run
    label: Run
    argv: [python, $entryJson]
    parameters: []
parameters: []
"@
Write-Host "Target: $Target"
Write-Host "Copy: Launcher.exe"
Write-Host $(if (Test-Path -LiteralPath $config) { 'Keep existing launcher.yaml byte-for-byte; validate it in the app before running.' } else { "Create launcher.yaml (mode=$mode, entry=$Entry)" })
if (-not $PSCmdlet.ShouldProcess($Target, 'Add Launcher.exe and create a missing launcher.yaml')) { return }
$created = [Collections.Generic.List[string]]::new()
$temp = Join-Path $Target ('.launcher-copy-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::Copy((Resolve-Path -LiteralPath $Launcher).Path,$temp,$false)
    [IO.File]::Move($temp,$destination)
    $created.Add($destination)
    if (-not (Test-Path -LiteralPath $config)) {
        $stream = [IO.File]::Open($config,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $created.Add($config)
        try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($yaml); $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    }
    Write-Host 'Done. Open the target Launcher.exe and review Settings before initialization or execution.' -ForegroundColor Green
}
catch { foreach ($path in $created) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }; throw }
finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
