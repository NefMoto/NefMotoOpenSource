@echo off

REM Set configuration based on argument
if "%1"=="installer" (
    set CONFIG=Release
) else if "%1"=="release" (
    set CONFIG=Release
) else (
    set CONFIG=Debug
)

REM Get version from git describe, similar to Makefile
for /f "usebackq delims=" %%v in (`git describe --tags --abbrev^=4 --always --dirty 2^>nul`) do set FULL_VERSION=%%v
if "%FULL_VERSION%"=="" set FULL_VERSION=unknown

echo Full Version: %FULL_VERSION%

REM Build the solution
echo Building ECUFlasher.sln using dotnet (%CONFIG%)...
dotnet build ECUFlasher.sln --configuration %CONFIG% --verbosity minimal
if errorlevel 1 exit /b %ERRORLEVEL%

REM If installer argument provided, build installer
if "%1"=="installer" (
    set ECUFlasher_TargetDir=ECUFlasher/bin/msil/Release/net8.0-windows/
    echo Building installer/bin/Release/NefMotoECUFlasher-%FULL_VERSION%.msi...
    wix build -arch x86 -ext WixToolset.UI.wixext -ext WixToolset.NetFx.wixext -o Installer/bin/Release/NefMotoECUFlasher-%FULL_VERSION%.msi Installer/Product.wxs
    if errorlevel 1 exit /b %ERRORLEVEL%
)

echo Done!
exit /b 0
