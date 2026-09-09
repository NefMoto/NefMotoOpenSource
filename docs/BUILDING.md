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

# Framework-dependent publish folder (not the MSI; still needs .NET 10)
make publish

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

REM Framework-dependent publish folder (not the MSI; still needs .NET 10)
build.bat publish
```

## Prerequisites

- **.NET 10 SDK** (preferred) or **Visual Studio** with MSBuild. End users of the MSI need the **.NET 10 Desktop Runtime**, not only the console runtime.
- Target framework is `NetTfm` in [`Directory.Build.props`](../Directory.Build.props). Output dirs do not include the TFM (`ECUFlasher/bin/msil/Debug` / `Release`).
- **WiX Toolset** (installer builds; MSI is **x64**, `-arch x64`). Version is [`.config/dotnet-tools.json`](../.config/dotnet-tools.json) (Dependabot can bump it). [`installer.ps1`](../installer.ps1) runs `dotnet tool restore` then `dotnet tool run wix` — no global `wix` on `PATH`. `make installer` and `build.bat installer` both invoke that script. UI and NetFx extensions are added at the json version during the installer build.

`make publish` / `build.bat publish` writes a framework-dependent copy to `publish/NefMotoECUFlasher`. It is not a substitute for the MSI: no shortcuts, no installer .NET check, and it still needs the .NET 10 Desktop runtime. `MemoryLayouts` is copied next to the executable. This is not a single-file exe.
