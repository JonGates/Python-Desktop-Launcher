[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$VerifyWindows
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}
function Copy-LicenseNotices([string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    # Capture to completion: Select-Object -First can stop the native pipeline early in Windows PowerShell.
    $cacheOutput = @(& dotnet nuget locals global-packages --list)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate restored NuGet packages for license collection.' }
    $cacheLine = $cacheOutput | Where-Object { $_ -match '^global-packages:' } | Select-Object -First 1
    if (-not $cacheLine) { throw 'NuGet did not report its global package directory.' }
    $cache = ($cacheLine -replace '^global-packages:\s*','').Trim()
    if (-not (Test-Path -LiteralPath $cache -PathType Container)) { throw "NuGet cache does not exist: $cache" }
    $found = @()
    foreach ($package in @('yamldotnet', "microsoft.netcore.app.runtime.$Runtime", "microsoft.windowsdesktop.app.runtime.$Runtime")) {
        $packageRoot = Join-Path $cache $package
        if (-not (Test-Path -LiteralPath $packageRoot)) { Write-Warning "No restored package directory for $package; review third-party notices before redistribution."; continue }
        $versions = @(Get-ChildItem -LiteralPath $packageRoot -Directory | Where-Object { if ($package -eq 'yamldotnet') { $_.Name -eq '18.1.0' } else { $_.Name -like '10.*' } })
        foreach ($version in $versions) {
            $notices = @(Get-ChildItem -LiteralPath $version.FullName -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|THIRD[-_]PARTY[-_]NOTICES)(\..*)?$' })
            foreach ($notice in $notices) {
                $relative = $notice.FullName.Substring($version.FullName.Length).TrimStart('\','/') -replace '[\\/]','_'
                $name = "$package-$($version.Name)-$relative"
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $Destination $name) -Force
                $found += $name
            }
        }
    }
    if (-not ($found | Where-Object { $_ -like 'yamldotnet-*' })) {
        # YamlDotNet 18.1.0 declares MIT in its nuspec but does not bundle the license text.
        # This copy is pinned to the repository commit recorded in that exact package.
        $license = Join-Path $root 'licenses/YamlDotNet-18.1.0-LICENSE.txt'
        if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw 'Verified YamlDotNet license is missing.' }
        $name = 'yamldotnet-18.1.0-LICENSE.txt'
        Copy-Item -LiteralPath $license -Destination (Join-Path $Destination $name)
        $found += $name
    }
    $found | Sort-Object -Unique | Set-Content -LiteralPath (Join-Path $Destination 'COLLECTED-NOTICES.txt') -Encoding UTF8
    Write-Host 'Copied available restored-package notices; this is an inventory, not a legal completeness guarantee.'
}
$staging = $null
Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK first. See README.md. A runtime alone is not enough to build source.' }
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $sdk.StartsWith('10.')) { throw "This source targets the .NET 10 SDK. Selected SDK: $sdk" }
    New-Item -ItemType Directory -Force -Path 'artifacts' | Out-Null
    Invoke-Dotnet @('restore','ProjectLauncher.sln')
    Invoke-Dotnet @('build','ProjectLauncher.sln','-c','Release','--no-restore')
    if (-not $SkipTests) { Invoke-Dotnet @('run','--project','tests/ProjectLauncher.Specs','-c','Release','--no-build') }
    if ($VerifyWindows) { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-Windows.ps1'); if ($LASTEXITCODE -ne 0) { throw 'Windows validation failed.' } }
    foreach ($component in @('Desktop','Cli')) {
        $destination = "artifacts/publish-$component-$Runtime"
        Invoke-Dotnet @('publish',"src/ProjectLauncher.$component/ProjectLauncher.$component.csproj",'-c','Release','-r',$Runtime,'--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true','-p:PublishTrimmed=false','-o',$destination)
        $externalDlls = @(Get-ChildItem $destination -Filter '*.dll' -File)
        if ($externalDlls.Count -gt 0) { throw "Publish left external DLLs in $destination. Do not distribute only the EXE until the publish configuration is fixed." }
    }
    # Fresh staging prevents a previous Demo's .venv, secrets or outputs entering a release ZIP.
    $staging = Join-Path $root ('artifacts/stage-' + [guid]::NewGuid().ToString('N'))
    $portable = Join-Path $staging 'portable'
    $demo = Join-Path $staging 'demo'
    New-Item -ItemType Directory -Force -Path $portable,$demo | Out-Null
    $gui = Join-Path $root "artifacts/publish-Desktop-$Runtime/Launcher.exe"
    $cli = Join-Path $root "artifacts/publish-Cli-$Runtime/Launcher.Cli.exe"
    foreach ($dest in @($portable,$demo)) {
        Copy-Item $gui (Join-Path $dest 'Launcher.exe') -Force
        Copy-Item $cli (Join-Path $dest 'Launcher.Cli.exe') -Force
        Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dest 'LICENSE-Launcher.txt') -Force
        Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $dest -Force
        Copy-Item (Join-Path $root 'LICENSE') $dest -Force
        foreach ($readme in @('README.md', 'README.zh-CN.md')) { Copy-Item (Join-Path $root $readme) $dest -Force }
        New-Item -ItemType Directory -Force -Path (Join-Path $dest 'docs') | Out-Null
        Get-ChildItem (Join-Path $root 'docs') -Filter '*.md' -File | Copy-Item -Destination (Join-Path $dest 'docs') -Force
        Copy-Item (Join-Path $root 'docs/images') -Destination (Join-Path $dest 'docs') -Recurse
        Copy-Item (Join-Path $root 'docs/examples') -Destination (Join-Path $dest 'docs') -Recurse
        Copy-Item (Join-Path $root 'skills') -Destination $dest -Recurse
    }
    Copy-LicenseNotices (Join-Path $portable 'licenses')
    Copy-Item -LiteralPath (Join-Path $portable 'licenses') -Destination $demo -Recurse
    # Portable has no fixed project config: first launch creates one only after user confirmation.
    Copy-Item (Join-Path $root 'docs/INTEGRATION.md') (Join-Path $portable 'README-Integration.md') -Force
    Copy-Item (Join-Path $root 'launcher.yaml') $demo -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $demo 'demo') | Out-Null
    Get-ChildItem (Join-Path $root 'demo') -File | Copy-Item -Destination (Join-Path $demo 'demo') -Force
    Copy-Item (Join-Path $root 'README.md') (Join-Path $demo 'README.md') -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $demo 'docs') | Out-Null
    Get-ChildItem (Join-Path $root 'docs') -Filter '*.md' -File | Copy-Item -Destination (Join-Path $demo 'docs') -Force
    if (Test-Path (Join-Path $root 'docs/preview')) { Copy-Item (Join-Path $root 'docs/preview') -Destination (Join-Path $demo 'docs') -Recurse }
    foreach ($name in @('portable','demo')) {
        $folder = Join-Path $root "artifacts/$name"
        if (Test-Path -LiteralPath $folder) {
            $backup = $folder + '.previous-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6)
            Move-Item -LiteralPath $folder -Destination $backup
            Write-Host "Preserved prior output at $backup"
        }
        Move-Item -LiteralPath (Join-Path $staging $name) -Destination $folder
        $zip = Join-Path $root "artifacts/ProjectLauncher-$name-$Runtime.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -CompressionLevel Optimal
        (Get-FileHash $zip -Algorithm SHA256).Hash + '  ' + (Split-Path $zip -Leaf) | Set-Content "$zip.sha256" -Encoding ASCII
    }
    Write-Host "`nBuild completed: $root\artifacts\portable\Launcher.exe" -ForegroundColor Green
    Write-Host "Demo: $root\artifacts\demo\Launcher.exe"
    Write-Host 'A build is not a complete Windows usability/terminal acceptance test. Run Test-Windows.cmd and follow docs/TESTING.md.'
    exit 0
}
catch { Write-Error $_ -ErrorAction Continue; exit 1 }
finally { if ($staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }; Pop-Location }
