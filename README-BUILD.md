# Building NefMotoECUFlasher

This project supports both Unix-style (bash/Makefile) and Windows batch file builds.

## Unix-style Build (Recommended)

If you're using bash (Cygwin, MSYS, Git Bash, WSL, or any Unix system), use the Makefile:

```bash
# Build Debug (default)
make debug

# Build Release
make Release

# Build installer
make installer

# Clean build artifacts
make clean
```

## Windows


```cmd
# Build Debug (default)
build.bat

# Build installer
# FIXME: can't yet!
```

## Prerequisites

- **.NET SDK** (preferred) or **Visual Studio** with MSBuild
- **WiX Toolset v6.0+** (for installer builds)
  - `dotnet tool install --global wix --version 6.0.2`
