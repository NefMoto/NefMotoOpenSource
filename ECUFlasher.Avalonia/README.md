# ECUFlash (Avalonia companion)

**Extremely experimental / parked.** Sketch of a Fluent UI for **`ecuflash`**. Legacy **NefMotoECUFlasher** (WPF + MSI) remains the shipped Windows suite.

**Blocked:** the real product is one `ecuflash` binary with headless/GUI mode detection. That requires changing `ecuflash` (not this standalone project). Do not treat this tree as the shipping GUI host. Resume only after dual-mode entry + [#159](https://github.com/NefMoto/NefMotoOpenSource/issues/159) / [#160](https://github.com/NefMoto/NefMotoOpenSource/issues/160) on `master`.

Avalonia notes live **only in this README** (do not churn root/`docs/` on this branch).

## Stance

| Product | Role |
|---------|------|
| `NefMotoECUFlasher` / WPF | Full Windows suite + MSI — keep |
| `ecuflash` CLI | Headless flash ops (on `master`) |
| This Avalonia app | Companion GUI: flash + verify + DTC + immo/EEPROM (v1) — **feature wiring paused** |

Not a WPF workalike. Layout is **Device → Protocol → Actions → Log**. Action panels wait on [#159](https://github.com/NefMoto/NefMotoOpenSource/issues/159) / [#160](https://github.com/NefMoto/NefMotoOpenSource/issues/160) on `master`.

## Build / run

Requires .NET 10 SDK.

```bash
dotnet build ECUFlasher.Avalonia/NefMotoECUFlasher.Avalonia.csproj
dotnet run --project ECUFlasher.Avalonia/NefMotoECUFlasher.Avalonia.csproj
# or:
make run-avalonia
```

- TFM: `net10.0` via `Directory.Build.props` (`IsNonWindowsTfm` for `NefMotoECUFlasher.Avalonia`).
- Avalonia 11.3.x packages in the `.csproj` (bump the set together).
- Runs on Windows / macOS / Linux; cable I/O follows `Communication` backends.

## Status

| Area | State |
|------|--------|
| Fluent shell + NefMoto accent | Done (parked) |
| Device list / Refresh (`DeviceManager`) | Done |
| Protocol selector (KWP / Boot) | Done (selection only) |
| Layout list / Refresh | Done (reads `MemoryLayouts/`; no CLI) |
| Actions (Info / Flash / Verify / DTC / Immo) | Stub commands → log only — **ops blocked on #159/#160** |
| Shared flash/info + #159 verify on `master` | Not started (prerequisite) |
| #160 `ecuflash` DTC/immo CLI on `master` | Not started (prerequisite) |
| Dual-mode into `ecuflash` Main | Not started |
| Packaging | Not started |

## Layout

- `App.axaml` — Fluent light theme, NefMoto `#901212` accents
- `MainWindow.axaml` — device, layout, protocol, action stubs, log
- `ViewModels/` — shell, device/layout selection, action stubs (`CommunityToolkit.Mvvm`)
- `Assets/` — app icon

## Next

**Paused for feature wiring** until on `master`:

1. Shared flash/info ops + [#159](https://github.com/NefMoto/NefMotoOpenSource/issues/159) bootmode verify (WPF + `ecuflash`)
2. [#160](https://github.com/NefMoto/NefMotoOpenSource/issues/160) `ecuflash` KWP DTC read/clear + bootmode immo-off

Then Avalonia: Info / Flash+verify / DTC / Immo against those helpers; later dual-mode into `ecuflash`.

Do not reintroduce WPF types into Shared/Communication.
