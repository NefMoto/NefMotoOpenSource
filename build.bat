@echo off

REM Set configuration based on argument
if "%1"=="installer" (
    set CONFIG=Release
) else if "%1"=="publish" (
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

REM TFM from Directory.Build.props (same source as the SDK)
for /f "tokens=3 delims=<>" %%a in ('findstr /c:"<NetTfm>" Directory.Build.props') do set NET_TFM=%%a
for /f "tokens=1 delims=." %%a in ("%NET_TFM:net=%") do set DOTNET_MAJOR=%%a

REM Build the solution
echo Building ECUFlasher.sln using dotnet (%CONFIG%)...
dotnet build ECUFlasher.sln --configuration %CONFIG% --verbosity minimal
if errorlevel 1 exit /b %ERRORLEVEL%

REM Framework-dependent publish folder (not single-file, not the MSI)
if "%1"=="publish" (
    echo Publishing to publish\NefMotoECUFlasher %FULL_VERSION%
    dotnet publish ECUFlasher/ECUFlasher.csproj --configuration Release --self-contained false -p:PublishSingleFile=false -o publish/NefMotoECUFlasher --verbosity minimal
    if errorlevel 1 exit /b %ERRORLEVEL%
    dir /b publish\NefMotoECUFlasher\MemoryLayouts\*.MemoryLayout.xml >nul 2>nul
    if errorlevel 1 (
        echo error: MemoryLayouts missing from publish\NefMotoECUFlasher
        exit /b 1
    )
)

REM If installer argument provided, build installer
if "%1"=="installer" (
    set ECUFlasher_TargetDir=ECUFlasher/bin/msil/Release/
    echo Building installer/bin/Release/NefMotoECUFlasher-%FULL_VERSION%.msi...
    wix build -arch x86 -d RuntimeTfm=%NET_TFM% -d DotNetMajor=%DOTNET_MAJOR% -ext WixToolset.UI.wixext -ext WixToolset.NetFx.wixext -o Installer/bin/Release/NefMotoECUFlasher-%FULL_VERSION%.msi Installer/Product.wxs
    if errorlevel 1 exit /b %ERRORLEVEL%
)

echo Done!
exit /b 0
