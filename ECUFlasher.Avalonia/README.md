# NefMoto ECU Flasher (Avalonia)

**Extremely experimental.** Long-lived port branch. The WPF app in `ECUFlasher/` remains the shipped Windows product (MSI). Do **not** treat this UI as supported for flashing or as a substitute for the release.

Avalonia-specific documentation lives **only here** so this branch does not churn root/`docs/` markdown (avoids merge noise with `master`).

Shared libraries (`Shared`, `Communication`, `Checksum`, `ApplicationShared`, `FTD2XX_NET`) are UI-agnostic on `master` (`net10.0`). Converter logic: `Shared/ConverterLogic.cs`. Avalonia wrappers: `Converters/`.

## Build / run

Requires .NET 10 SDK. General WPF build/install docs stay in the main repo (`docs/BUILDING.md` on `master`); use this section for Avalonia only.

```bash
dotnet build ECUFlasher.Avalonia/NefMotoECUFlasher.Avalonia.csproj
dotnet run --project ECUFlasher.Avalonia/NefMotoECUFlasher.Avalonia.csproj
# or (from repo root, if Makefile target is present on this branch):
make run-avalonia
```

Windows (no `build.bat` Avalonia target):

```bat
dotnet run --project ECUFlasher.Avalonia\NefMotoECUFlasher.Avalonia.csproj
```

- TFM: `net10.0` via `Directory.Build.props` (`IsNonWindowsTfm` for this project name).
- Avalonia packages: 11.3.x in the `.csproj` (bump the set together).
- On macOS/Linux the Avalonia UI can run; the WPF project may still *build* with `EnableWindowsTargeting` but does not run outside Windows.

## Status

| Area | State |
|------|--------|
| Solution / `make run-avalonia` | Done (this branch) |
| Theme, banner, icon, converters | Done |
| Main shell (menu, protocol ComboBox, tabs, status, About) | Done (shell only) |
| KWP2000 / Boot Mode connection views | Not started |
| ECU Info / Flashing / Data Logger / Settings content | Placeholders |
| Window size persistence, packaging | Not started |
| Cable I/O on macOS / Linux | Not implemented (see below) |

## Layout

- `App.axaml` — Fluent theme overrides, NefMoto colors, converter resources
- `MainWindow.axaml` — menu, banner, protocol selector, connection placeholder, tabs, status bar
- `Views/AboutWindow.axaml` — version + links
- `ViewModels/` — shell ViewModels (`CommunityToolkit.Mvvm`)
- `Assets/` — banner, `.ico` / `.png` / `.icns`

## Next work (priority)

**UI workalike first** (Windows cables already work). Defer deep OS-independent cable work until connection UI + ≥1 tab, or an explicit Mac/Linux hardware goal. Optional: short design note only — see local `plans/Avalonia-Porting-Plan.md` §0 / §15 (not in git).

1. **Connection UI** — KWP2000 and Bootstrap connection / status / settings; non-WPF ViewModels. ← current focus
2. **Tab content** — ECU Info or Flashing first, then Data Logger.
3. **Status / log window** — Avalonia equivalent of `StatusWindow`.
4. **Settings persistence** — without WPF `Properties.Settings`.
5. **Polish** — validation, auto-scroll; optional ControlTemplates.
6. **Packaging** — `dotnet publish` for Windows/macOS; optional `.app` for Dock icon.
7. **Later — cable backends** — CH340 Unix enum/drain, then FTDI; extend `ICommunicationDevice` / `DeviceManager`, don’t invent a second HAL.

Do not reintroduce WPF types into Shared/Communication.

## Cables on macOS / Linux

**Deferred** relative to UI workalike. Not never; not automatic with Avalonia. Protocols use `ICommunicationDevice`; enumeration and natives are still Windows-shaped (WMI for CH340, `FTD2XX.DLL` + `kernel32`, Win32 TX drain).

| Target | Outlook |
|--------|---------|
| CH340 KKL | Best first Unix path (`System.IO.Ports` + platform enum + non-Win32 drain) |
| Genuine FTDI KKL | Doable (port D2XX to `libftd2xx`, or VCP `SerialPort`) |
| Clone FTDI / Ross-Tech HEX | Unreliable / Windows-centric |
| HEX-V2 / HEX-NET | Out of scope (no dumb K-line) |

Until Communication grows OS-aware backends, non-Windows hosts are a **UI experiment** only. Implement cable work after Windows Avalonia is dogfoodable (or when Mac/Linux hardware is an immediate goal).

## Project references

ApplicationShared, Communication, Checksum, Shared, FTD2XX_NET.

## Doc policy (this branch)

- Keep Avalonia notes in **this file only**.
- Do not add Avalonia sections to root `README.md`, `docs/BUILDING.md`, `docs/index.md`, `docs/RELEASE.md`, or user guides while the branch is long-lived.
- Local plans (if any) stay out of revision control and must not be linked from tracked docs.

## Merge thrashing (summary)

Dual UI = dual edits for shell/theme/About/assets already; connection + tabs will amplify that until ViewModels leave WPF-only `ECUFlasher/` into a shared lib. Rebase hotspots vs `master`: `ECUFlasher.sln`, `Directory.Build.props`, `Makefile` — keep those diffs small. Full inventory: local `plans/Avalonia-Porting-Plan.md` §0.1.
