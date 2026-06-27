# Building NefMotoECUFlasher

This project supports both Unix-style (bash/Makefile) and Windows batch file builds.

## Unix-style Build (Recommended)

If you're using bash (Cygwin, MSYS, Git Bash, WSL, or any Unix system), use the Makefile:

```bash
# Build Debug (default)
make

# Build Release
make release

# Build Release and installer
make installer

# Clean build artifacts
make clean
```

## Windows

```bat
REM Build Debug (default)
build.bat

REM Build Release
build.bat release

REM Build Release and installer
build.bat installer
```

## Prerequisites

- **.NET SDK** (preferred) or **Visual Studio** with MSBuild
- **WiX Toolset v6.0+** (for installer builds)
  - `dotnet tool install --global wix --version 6.0.2`
- **WiX UI and NetFX extensions**
  - `wix extension add WixToolset.UI.wixext WixToolset.NetFx.wixext --global`
