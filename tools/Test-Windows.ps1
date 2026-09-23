[CmdletBinding()]
param([switch]$SkipUiSmoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Validation command failed: dotnet $($Arguments -join ' ')" }
}
Push-Location $root
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Windows is required for WPF and ConPTY validation.' }
    New-Item -ItemType Directory -Force 'artifacts/test-reports' | Out-Null
    Start-Transcript -Path 'artifacts/test-reports/windows-test-transcript.txt' -Force | Out-Null
    Run-Dotnet @('build','ProjectLauncher.sln','-c','Release')
    Run-Dotnet @('run','--project','tests/ProjectLauncher.Specs','-c','Release','--no-build')
    Run-Dotnet @('run','--project','tests/ProjectLauncher.Windows.Specs','-c','Release','--no-build')
    if (-not $SkipUiSmoke) {
        $fixture = Join-Path $root 'artifacts/ui-smoke-project'
        New-Item -ItemType Directory -Force $fixture | Out-Null
        Copy-Item (Join-Path $root 'launcher.yaml') $fixture -Force
        $pictures = Join-Path $root 'artifacts/test-reports/ui'
        Run-Dotnet @('run','--project','src/ProjectLauncher.Desktop','-c','Release','--no-build','--','--project',$fixture,'--ui-smoke-test',$pictures)
    }
    Write-Host 'Automated commands passed. Complete the separate manual acceptance checklist before release.' -ForegroundColor Green
    exit 0
}
catch { Write-Error $_ -ErrorAction Continue; exit 1 }
finally { try { Stop-Transcript | Out-Null } catch { }; Pop-Location }
