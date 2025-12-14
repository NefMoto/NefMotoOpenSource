@echo off

REM Get version from git describe, similar to Makefile
for /f "usebackq delims=" %%v in (`git describe --tags --abbrev^=4 --always --dirty 2^>nul`) do set FULL_VERSION=%%v
if "%FULL_VERSION%"=="" set FULL_VERSION=unknown

echo Building ECUFlasher.sln using dotnet (Debug)...
echo Full Version: %FULL_VERSION%
dotnet build ECUFlasher.sln --configuration Debug --verbosity minimal
exit /b %ERRORLEVEL%
