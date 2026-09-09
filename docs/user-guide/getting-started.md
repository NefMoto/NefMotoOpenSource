# Getting started

Install, connect a cable, and open a first KWP2000 session. Do not write flash until you have a backup of the ECU. [Flashing](flashing.md), [README Limitations](../../README.md#limitations).

If connect fails, see [Troubleshooting](troubleshooting.md).

## Install

- Download the MSI from the [latest GitHub release](https://github.com/NefMoto/NefMotoOpenSource/releases/latest).
- The installer is 64-bit and goes to `C:\Program Files\NefMotoECUFlasher`. An older 32-bit install under Program Files (x86) should be replaced on upgrade.
- The MSI checks for the [.NET 10 Desktop Runtime (x64)](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe). The SDK, console runtime, ASP.NET runtime, and .NET 8 Desktop runtime are not enough. The runtime is not bundled in the MSI.
- Windows only. Device listing uses WMI (needed for CH340 detection).

To build from source instead, see [BUILDING.md](../BUILDING.md).

## Cable

You need **either**:

- A dumb-mode K+DCAN or KKL cable: USB-to-serial with an FTDI or CH340 chipset, K-line pass-through to the ECU (no protocol translation), **or**
- A legacy Ross-Tech HEX-USB or HEX-USB+CAN with Ross-Tech VCP drivers, dumb K-line pass-through, and smart mode disabled — the interface must show up in NefMoto's device list (install VCP per [Ross-Tech](http://www.ross-tech.com/vag-com/usb/virtual-com-port.php) if it does not)

Ross-Tech HEX-V2 and HEX-NET are not supported; they lack dumb K-line pass-through. Do not use KII-USB (poor pass-through).

FTDI and CH340 are equivalent for KWP2000. Some clone cables fail slow init on either chipset; that is a cable/driver problem, not “CH340 cannot do 5-baud.” Enable **Slow init timing log** on the **KWP2000 Settings** tab when diagnosing connect issues. Bootmode baud caveats for CH340: [Bootmode](bootmode.md#baud).

Shopping: any cable that is a USB serial adapter plus a dumb K-line transceiver. No vendor endorsement here.

Many cheap cables labeled **KWP2000** are FTDI clones. The FTDI drivers that ship with Windows blacklist those chips, so they never show up as a working COM port and will not work with NefMoto. Use a genuine FTDI adapter or a CH340 cable instead.

## First launch

The connection controls are at the **top of the window**, not on a tab.

- Protocol combo: **KWP2000** or **Boot Mode** (brick recovery and physical EEPROM — [Bootmode](bootmode.md)).
- Baud: leave **10400** for KWP unless you already know another rate.
- Device list: pick the USB serial adapter. **Refresh Devices** if it is missing.
- Connection method: **Slow Init** (default) or **Fast Init**.
- **Connect Slow Init** / **Connect Fast Init** (the button label follows the method).

Tabs after you are connected:

- **KWP2000 Info** — ident and DTCs (**Read ECU Info**, **Read DTCs**, **Clear DTCs**)
- **KWP2000 Flashing** — read/write ([Flashing](flashing.md))
- **KWP2000 Logging** — live variables. It is not [ME7Logger](https://nefariousmotorsports.com/forum/).
- **KWP2000 Settings** — address, timings, **Verify cable in dumb mode**, **Slow init timing log**

## Logging and DTCs

**KWP2000 Info:** **Read DTCs**, **Clear DTCs**, save/load a DTCs file. Clearing DTCs does not fix an underlying fault.

**KWP2000 Logging:** live variables while KWP2000 is connected. This is not [ME7Logger](https://nefariousmotorsports.com/forum/). After a flash write, logging stays broken until a complete power cycle ([Flashing](flashing.md#after-a-successful-write)).

## Connect with slow init

Slow init is the default. Use it on the bench and in the car unless you already know fast init works on that ECU.

- Leave **Connect address** on the Settings tab at the default. ME7 bench units use `0x01`. Do not set a one-shot address of `0x11` (an internal retry path; it will not sync as a user address). Address meaning and hex detail: [KWP2000.md](../KWP2000.md).
- Wait **at least 2.6 seconds** after a failed attempt before clicking Connect again. The ECU needs that idle time between slow inits.
- In the car, K-line often goes through the cluster. That is slower and less predictable than a direct bench wire. If the handshake misses the address complement, that is [issue #95](https://github.com/NefMoto/NefMotoOpenSource/issues/95), not a CH340 5-baud failure.
- **Verify cable in dumb mode** is on by default. A failure there usually means the adapter is not in dumb pass-through, or TX/RX echo is wrong.

On the documented bench units (one ME7.1 and one ME7.5, both 29F800), slow init works with both FTDI and CH340. Fast init works on that ME7.1 and fails on that ME7.5. Other flash images may differ. There is no automatic fallback from fast to slow.

## Fast init

Use **Fast Init** only when you already know it is reliable on that ECU and cable. It is much quicker. If it fails, switch to **Slow Init** and wait 2.6 seconds.

On the documented ME7.5 bench unit, fast init failed after address and timing sweeps. Slow init still worked on the same K-line. Treat ME7.5 as slow-init unless you have proven otherwise. See [KWP2000.md](../KWP2000.md).

## Settings that persist

Saved in `preferences.json` next to the log file:

- Protocol, KWP baud, **Slow Init** / **Fast Init**, **Slow init timing log**

Not saved (reset when you restart the app):

- Connect address, timings, security options, number of connection attempts

**Restore To Defaults** on the Settings tab resets that tab only. It does not change the connect-bar init method or baud. You must be disconnected to use it.

## Bench vs in the car

**In the car:** ignition on as required by the car; engine usually off for programming. Expect cluster traffic on K-line. Prefer slow init. Patience on retries.

**On the bench:** direct K-line to the ECU, stable ground and +12 V, adapter in dumb mode.

Typical ME7 K-line is ECU pin 43, which is OBD pin 7. Confirm against a pinout for your connector.

**ME7.5 (121-pin) on the bench:** for KWP read/write, pin 121 must be at +12 V. Use the same switched +12 V as pins 3, 21, and 62. Pin 121 is the top right pin on the small ECU connector. The app prints this hint when it matters. A connect that works for ident can still fail a programming session if pin 121 is floating.

Bootmode uses the same power rails and a different entry pin (often pin 24 on ME7). See [Bootmode](bootmode.md).

## After you connect

Recommended first action: **KWP2000 Info** → **Read ECU Info**. Optionally **Read DTCs**. That confirms the session without touching flash.

A first flash action should be a **read** (backup), not a write. Layout selection, BT vs BB, and write recovery: [Flashing](flashing.md) and [Wrong Flash Layout](troubleshooting.md#wrong-flash-layout).

After a **successful** KWP write, ident may show `THIS-IS-THE-RAM-PROGRAM` until a **complete** power cycle (ME7.5: all +12 off, including pin 121). That string is not a brick. After an **abort**, stay powered and retry. [Flashing](flashing.md#after-a-successful-write)

## Log file

Session log:

`%AppData%\Nefarious Motorsports\NefMoto VW Audi ME7 Flasher Logger\NefMoto.log`

`%AppData%` is the **roaming** profile folder (`C:\Users\<you>\AppData\Roaming` on current Windows, not `AppData\Local`).

`preferences.json` is in the same folder. Use **File → Open Log File** or **Open Log File Location** when reporting connect or flash problems. That file is not the data-logger tab save.

## Next

- [Flashing](flashing.md)
- [Bootmode](bootmode.md)
- [Troubleshooting](troubleshooting.md)
- [README](../../README.md) — features, limitations, requirements
- [KWP2000.md](../KWP2000.md) — developer bench notes
