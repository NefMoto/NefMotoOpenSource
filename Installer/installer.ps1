# Called by Makefile and build.bat. Requires FULL_VERSION, NET_TFM, DOTNET_MAJOR, ECUFlasher_TargetDir.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$wixVersion = (Get-Content .config/dotnet-tools.json -Raw | ConvertFrom-Json).tools.wix.version
if (-not $wixVersion) { throw 'wix version missing from .config/dotnet-tools.json' }

$msi = "Installer/bin/Release/NefMotoECUFlasher-$($env:FULL_VERSION).msi"
New-Item -ItemType Directory -Force -Path Installer/bin/Release | Out-Null
Write-Host "WiX $wixVersion (local tool), $msi"

dotnet tool restore
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet tool run wix -- extension add "WixToolset.UI.wixext/$wixVersion" "WixToolset.NetFx.wixext/$wixVersion" --global
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet tool run wix -- build -arch x64 -d "RuntimeTfm=$($env:NET_TFM)" -d "DotNetMajor=$($env:DOTNET_MAJOR)" -ext WixToolset.UI.wixext -ext WixToolset.NetFx.wixext -o $msi Installer/Product.wxs
if ($LASTEXITCODE) { exit $LASTEXITCODE }
