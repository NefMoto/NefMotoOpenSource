@echo off
REM Build script for ECUFlasher solution (Release)
REM Uses dotnet build when available, otherwise falls back to MSBuild

setlocal enabledelayedexpansion

REM Try dotnet first, then MSBuild - search common installation paths
for %%P in ("C:\Program Files" "C:\Program Files (x86)") do (
    REM Check for dotnet in this location
    set "TEST_PATH=%%~P\dotnet\dotnet.exe"
    if exist !TEST_PATH! (
        set "DOTNET_PATH=!TEST_PATH!"
        goto USE_DOTNET
    )

    REM Check for MSBuild in this location
    for %%V in (2026 2022 2019 2017 18 17 16 15) do (
        for %%E in (Community Enterprise Professional BuildTools) do (
            set "TEST_PATH=%%~P\Microsoft Visual Studio\%%V\%%E\MSBuild\Current\Bin\MSBuild.exe"
            if exist !TEST_PATH! (
                set "MSBUILD_PATH=!TEST_PATH!"
                goto USE_MSBUILD
            )
        )
    )
)

echo dotnet.exe and MSBuild.exe not found.

echo ERROR: Could not find dotnet.exe or MSBuild.exe
echo Please either:
echo 1. Install the .NET SDK from https://aka.ms/dotnet/download
echo 2. Open the solution in Visual Studio and build from there
echo 3. Use the Developer Command Prompt for Visual Studio
exit /b 1

:USE_DOTNET
echo Building ECUFlasher.sln using dotnet (Debug)...
"%DOTNET_PATH%" build ECUFlasher.sln --configuration Debug --verbosity minimal
exit /b %ERRORLEVEL%

:USE_MSBUILD
echo Building ECUFlasher.sln using MSBuild (Debug)...
"%MSBUILD_PATH%" ECUFlasher.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal
exit /b %ERRORLEVEL%
