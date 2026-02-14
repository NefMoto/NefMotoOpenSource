# NefMoto ME7 ECU Flasher

Open-source tool for reading, writing, and tuning VW/Audi ME7 ECUs via KWP2000

## Features

### Communication Protocols

- **KWP2000** (ISO 14230) - Full support for diagnostic and programming operations
- **KWP1281** - Legacy protocol support for older ECUs
- **Boot Mode** - Connection, ECU information reading, register access, flash read and flash write (ME7/Simos3/EDC15 variants). Layout auto-detect from device ID. Bootmode implementation is derived from [C167BootTool](https://github.com/EcuProg7/C167BootTool) (ME7BootTool.py)

### Connection Methods

- **Fast Init** - Supported on FTDI and CH340 devices
- **Slow Init** - Supported on FTDI devices only (5-baud initialization)

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

- **FTDI USB-to-Serial adapters** (FT232R, FT2232, etc.)
  - Full feature support including slow init
  - Bit-bang mode for 5-baud slow init
- **CH340 USB-to-Serial adapters**
  - Fast init connection method - slow init not supported (hardware limitation)
  - Standard KWP2000 operations

### User Interface

- Tabbed interface for ECU info, flashing, and logging
- Real-time status messages and logging
- Configurable communication timeouts and baud rates
- Memory layout selection and validation
- Progress indicators for long operations

## Limitations

### CH340 Devices

- **Slow init is not supported** - CH340 devices cannot reliably generate the 5-baud break signal required for slow init. Use fast init instead.
- **Bootmode:** Use 57600 or 38400 baud for best compatibility. 9600/19200 may fail non-deterministically (wrong ACKs, NAK, readback errors). Likely cause: USB latency/buffering, jitter, or voltage; not baud error. Use FTDI for lower baud rates. See [issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44).

### Platform

- **Windows only** - Requires Windows for WMI-based device enumeration (CH340 detection)

### ECU Support

- **ME7.x** - Primary target; full KWP2000 and bootmode support
- **Simos 3.x / EDC15** - Bootmode support (layout auto-detect)
- Memory layouts provided for common flash chips (29F200, 29F400, 29F800 series)
- Some ECUs may require specific connection parameters or timing adjustments

### Known Issues

See [GitHub Issues](https://github.com/NefMoto/NefMotoOpenSource/issues) for current known issues and feature requests.

## Building

See [BUILDING.md](BUILDING.md) for build instructions.

## Installation

Pre-built releases are available at: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>

## Requirements

- Windows operating system
- Compatible USB-to-serial adapter (FTDI or CH340)
- OBD-II cable in "dumb mode" (no protocol translation)

## License

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See [LICENSE.txt](LICENSE.txt) for details.

## Links

- **Latest Release**: <https://github.com/NefMoto/NefMotoOpenSource/releases/latest>
- **Issues**: <https://github.com/NefMoto/NefMotoOpenSource/issues>
- **Discussion Thread**: <https://nefariousmotorsports.com/forum/index.php?topic=12861.0title=>

## Development

Developed using C# (.NET 8.0) and C166 assembly (Keil uVision) for bootstrap loaders.
