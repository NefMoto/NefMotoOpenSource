# NefMoto ME7 ECU Flasher

Open-source tool for reading, writing, and tuning VW/Audi ME7 ECUs via KWP2000

## Features

### Communication Protocols

- **KWP2000** (ISO 14230) - Full support for diagnostic and programming operations
- **KWP1281** - Legacy protocol support for older ECUs
- **Boot Mode** - Connection, ECU information reading, register access, flash read and flash write (M5.9.x/ME7/Simos3/EDC15 variants). Layout auto-detect from device ID. Bootmode implementation is derived from [C167BootTool](https://github.com/EcuProg7/C167BootTool) (ME7BootTool.py)

### Connection Methods

- **Slow Init** - The default. More reliable across multiple environments.
- **Fast Init** - Connects much faster. Use this when you know it works reliably.

### ECU Operations

- Read flash memory (full or partial)
- Write flash memory with verification
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
- Some clone adapters or drivers may fail slow init — enable **Slow init timing log** in KWP2000 settings when diagnosing connect issues

## Limitations

### USB Adapters (KWP2000)

- Slow init uses per-bit break timing (not a single low-baud UART frame). Validated on ME7.1 and ME7.5 bench with FTDI and CH340.
- Clone or poor-quality adapters may fail slow init on either chip type; **Slow init timing log** helps compare timing.

### Bootmode

- Use 57600 or 38400 baud for best compatibility. 9600/19200 may fail non-deterministically on CH340 cables (wrong ACKs, NAK, readback errors). Likely cause: USB latency/buffering, jitter, or voltage; not baud error. Prefer FTDI for lower bootmode baud rates. See [issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44).
- 124800 baud should work on many different ECU/cable combinations, and most reliably on the bench. Use this for best performance once you have verified it is reliable.

### Platform

- **Windows only** - Requires Windows for WMI-based device enumeration (CH340 detection)

### ECU Support

- **ME7.x** - Primary target; full KWP2000 and bootmode support
- **ME7.5 fast init** - Not supported on one bench unit after address and timing sweeps. Use **slow init** instead. Other ME7.5 images may differ. See [docs/KWP2000.md](docs/KWP2000.md)
- **Motronic 5.9.2 (M5.9.x)** - Bootmode support with 256KB 29F200 layout
- **Simos 3.x / EDC15** - Bootmode support (layout auto-detect)
- Memory layouts provided for common flash chips (29F200, 29F400, 29F800 series)
- Some ECUs may require specific connection parameters or timing adjustments

### Known Issues

See [GitHub Issues](https://github.com/NefMoto/NefMotoOpenSource/issues) for current known issues and feature requests.

## Building

See [BUILDING.md](docs/BUILDING.md) for build instructions.

## Cutting a Release

See [RELEASE.md](docs/RELEASE.md) for release instructions.

## Installation

Pre-built releases are available at: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>

## Requirements

- Windows operating system
- OBD-II/USB cable — **either**:
  - A dumb-mode K+DCAN or KKL cable: USB-to-serial with an FTDI or CH340 chipset, K-line pass-through to the ECU (no protocol translation), **or**
  - A legacy Ross-Tech HEX-USB or HEX-USB+CAN with Ross-Tech VCP drivers, dumb K-line pass-through, and smart mode disabled — the interface must show up in NefMoto's device list (install VCP per [Ross-Tech](http://www.ross-tech.com/vag-com/usb/virtual-com-port.php) if it does not)

*Ross-Tech HEX-V2 and HEX-NET are not supported; they lack dumb K-line pass-through. Do not use KII-USB (poor pass-through).*

## License

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See [LICENSE.txt](LICENSE.txt) for details.

## Links

- **Latest Release**: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>
- **Issues**: <https://github.com/NefMoto/NefMotoOpenSource/issues>
- **Discussion Thread**: <https://nefariousmotorsports.com/forum/index.php?topic=12861.0>

## Development

Developed using C# (.NET 8.0) and C166 assembly (Keil uVision) for bootstrap loaders.
