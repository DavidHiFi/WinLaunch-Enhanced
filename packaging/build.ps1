# Builds dist\WinLaunchEnhanced (framework-dependent, ReadyToRun, win-x64), the
# Inno Setup installer, a self-contained portable zip (runs with no .NET
# installed) and a SHA256SUMS file covering both. Usage: pwsh packaging\build.ps1
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
$portable = Join-Path $root 'dist\portable'
if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o $portable -nologo
if ($LASTEXITCODE) { throw "portable publish failed" }
Get-ChildItem $portable -Filter *.pdb | Remove-Item
$dist = Join-Path $root 'dist'
$setupName = "WinLaunchEnhanced-$version-Setup.exe"
$zipName = "WinLaunchEnhanced-$version-Portable.zip"
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $dist $zipName) -Force
$sums = Join-Path $dist 'SHA256SUMS'
$lines = foreach ($file in @($setupName, $zipName)) {
    $hash = (Get-FileHash (Join-Path $dist $file) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $file"
}
$lines | Set-Content $sums -NoNewline:$false
Get-ChildItem $dist -Filter *.exe | Select-Object Name, Length
Get-ChildItem $dist -Filter *.zip | Select-Object Name, Length
Get-Content $sums
