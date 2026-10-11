# Builds dist\WinLaunchEnhanced (framework-dependent, ReadyToRun, win-x64) and
# the Inno Setup installer. Usage: pwsh packaging\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\WinLaunch.Enhanced\WinLaunch.Enhanced.csproj'
[xml]$x = Get-Content $proj
$version = ($x.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$out = Join-Path $root 'dist\WinLaunchEnhanced'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $out -nologo
if ($LASTEXITCODE) { throw "publish failed" }
Get-ChildItem $out -Filter *.pdb | Remove-Item
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc "/DAppVersion=$version" "/DSourceDir=$out" "/DOutputDir=$(Join-Path $root 'dist')" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE) { throw "ISCC failed" }
Get-ChildItem (Join-Path $root 'dist') -Filter *.exe | Select-Object Name, Length
