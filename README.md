# NefMoto ME7 ECU Flasher

Open-source tool for reading, writing, and tuning VW/Audi ME7 ECUs via KWP2000

## Documentation

- [Docs index](docs/index.md)
- [Getting started](docs/user-guide/getting-started.md) — install, cable, first connect
- [Flashing](docs/user-guide/flashing.md) — KWP read/write, layouts
- [Bootmode](docs/user-guide/bootmode.md) — bootstrap, EEPROM
- [Troubleshooting](docs/user-guide/troubleshooting.md)

## Features

### Communication Protocols

- **KWP2000** (ISO 14230) - Full support for diagnostic and programming operations
- **KWP1281** - Legacy protocol support for older ECUs
- **Boot Mode** - Connection, ECU information reading, register access, flash read/write (M5.9.x/ME7/Simos3/EDC15 variants), and SPI EEPROM (95040) read/write on ME7.1/ME7.5. Layout auto-detect from device ID. Bootmode implementation is derived from [C167BootTool](https://github.com/EcuProg7/C167BootTool) (ME7BootTool.py)

### Connection Methods

- **Slow Init** - The default. More reliable across multiple environments.
- **Fast Init** - Connects much faster. Use this when you know it works reliably.

### ECU Operations

- Read and write flash memory with verification
- Read and write physical SPI EEPROM (95040) in bootmode (ME7.1 / ME7.5)
- Erase flash sectors
- Read ECU identification information (KWP2000 and Bootmode)
- Bootmode ECU information: Device ID, CPU family, system registers (SYSCON, BUSCON, ADDRSEL), memory status
- Read and clear diagnostic trouble codes (DTCs)
- Extended data logging
- Memory layout validation
- Checksum calculation and verification

### Supported Hardware

- **Dumb-mode K+DCAN or KKL cable** — one unit with USB-to-serial (FTDI or CH340 chipset) and K-line pass-through to the ECU (no protocol translation)
  - KWP2000 slow init and fast init (same per-bit break timing for slow init on both chipsets)
  - Bootmode
- **Legacy Ross-Tech** — discontinued HEX-USB or HEX-USB+CAN only; see [Requirements](#requirements)
- Some clone adapters or drivers may fail slow init — enable **Slow init timing log** in KWP2000 settings. Fake FTDI **KWP2000**-labeled cables are blacklisted by stock Windows FTDI drivers. [Getting started — Cable](docs/user-guide/getting-started.md#cable)

## Limitations

### USB Adapters (KWP2000)

- Slow init uses per-bit break timing (not a single low-baud UART frame). Validated on ME7.1 and ME7.5 bench with FTDI and CH340. [Getting started](docs/user-guide/getting-started.md#connect-with-slow-init)
- Clone or poor-quality adapters may fail slow init on either chip type; **Slow init timing log** helps compare timing. Fake FTDI chips in cables labeled **KWP2000** are blacklisted by stock Windows FTDI drivers. [Troubleshooting](docs/user-guide/troubleshooting.md#slow-init-fails)
- In-car K-line (cluster) is not the same as bench. [Getting started](docs/user-guide/getting-started.md#bench-vs-in-the-car)

### Bootmode

- Prefer **57600** or **38400**. 9600/19200 may fail non-deterministically on CH340. **124800** after you have verified it. [Bootmode](docs/user-guide/bootmode.md#baud), [issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44)

### Platform

- **Windows only** — WMI device enumeration (CH340 detection). [Getting started](docs/user-guide/getting-started.md#install)

### ECU Support

- **ME7.x** — primary target; full KWP2000 and bootmode. Most flash chips are 29F800BB (1MB) or 29F400BB (512KB). [Flashing](docs/user-guide/flashing.md#layout)
- **ME7.5 fast init** — not supported on one bench unit. Use **slow init**. Other ME7.5 images may differ. [Getting started](docs/user-guide/getting-started.md#fast-init), [KWP2000.md](docs/KWP2000.md)
- **Motronic 5.9.2 (M5.9.x)** — bootmode with 256KB 29F200 layout
- **Simos 3.x / EDC15** — bootmode flash (layout auto-detect). [Bootmode](docs/user-guide/bootmode.md)
- Some ECUs need different connect timing. [Getting started](docs/user-guide/getting-started.md#connect-with-slow-init)

### Known Issues

- [Issue #100](https://github.com/NefMoto/NefMotoOpenSource/issues/100) — KWP write hang after ident. [Troubleshooting](docs/user-guide/troubleshooting.md#write-hangs-after-ident)
- [Issue #103](https://github.com/NefMoto/NefMotoOpenSource/issues/103) — wrong BT/BB layout aborts with **Wrong Flash Layout**. Pick the opposite layout and **Diff Write Flash**. [Troubleshooting](docs/user-guide/troubleshooting.md#wrong-flash-layout), [Flashing](docs/user-guide/flashing.md#after-an-abort)
- Other issues: [GitHub Issues](https://github.com/NefMoto/NefMotoOpenSource/issues)

## Log file

Session log: `%AppData%\Nefarious Motorsports\NefMoto VW Audi ME7 Flasher Logger\NefMoto.log`

`%AppData%` is the **roaming** profile folder (`C:\Users\<you>\AppData\Roaming` on current Windows, not `AppData\Local`).

Preferences (`preferences.json`) live in the same folder. Use **File → Open Log File** (or **Open Log File Location**) when reporting connect or flash problems. This is not the data-logger tab save.

## Building

See [BUILDING.md](docs/BUILDING.md) for build instructions.

## Cutting a Release

See [RELEASE.md](docs/RELEASE.md) for release instructions.

## Installation

Pre-built releases are available at: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>

Install steps: [Getting started](docs/user-guide/getting-started.md#install).

The MSI is 64-bit and installs to `C:\Program Files\NefMotoECUFlasher`. Upgrading from an older x86 install should replace the copy under Program Files (x86).

## Requirements

- Windows operating system
- [.NET 10 Desktop Runtime (x64)](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe) — the MSI checks for this. The SDK, console runtime, ASP.NET runtime, and .NET 8 Desktop runtime are not sufficient.
- OBD-II/USB cable — **either**:
  - A dumb-mode K+DCAN or KKL cable: USB-to-serial with an FTDI or CH340 chipset, K-line pass-through to the ECU (no protocol translation), **or**
  - A legacy Ross-Tech HEX-USB or HEX-USB+CAN with Ross-Tech VCP drivers, dumb K-line pass-through, and smart mode disabled — the interface must show up in NefMoto's device list (install VCP per [Ross-Tech](http://www.ross-tech.com/vag-com/usb/virtual-com-port.php) if it does not)

*Ross-Tech HEX-V2 and HEX-NET are not supported; they lack dumb K-line pass-through. Do not use KII-USB (poor pass-through).*

Cheap cables labeled **KWP2000** often use counterfeit FTDI chips that stock Windows FTDI drivers blacklist; they will not enumerate. Use genuine FTDI or CH340. [Getting started — Cable](docs/user-guide/getting-started.md#cable)

## License

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See [LICENSE.txt](LICENSE.txt) for details.

## Links

- **Latest Release**: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>
- **Issues**: <https://github.com/NefMoto/NefMotoOpenSource/issues>
- **Discussion Thread**: <https://nefariousmotorsports.com/forum/index.php?topic=12861.0>

## Development

Developed using C# (.NET 10.0) and C166 assembly (Keil uVision) for bootstrap loaders.
