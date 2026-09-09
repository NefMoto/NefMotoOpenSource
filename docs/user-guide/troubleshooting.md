# Troubleshooting

Symptom → what to check. Connect setup is in [Getting started](getting-started.md). Attach the session log when you open a GitHub issue.

Log file: `%AppData%\Nefarious Motorsports\NefMoto VW Audi ME7 Flasher Logger\NefMoto.log`

`%AppData%` is the **roaming** profile folder (`C:\Users\<you>\AppData\Roaming` on current Windows, not `AppData\Local`).

**File → Open Log File** or **Open Log File Location**. This is not the **KWP2000 Logging** tab save.

## Device not in the list

- Click **Refresh Devices**. Enumeration is deferred until the window loads; it can take a few seconds.
- Windows only. CH340 listing uses WMI.
- FTDI: official driver installed; the COM port visible in Device Manager. Clone cables labeled **KWP2000** often use counterfeit FTDI chips; stock Windows FTDI drivers blacklist them and they will not work. See [Getting started — Cable](getting-started.md#cable).
- Ross-Tech HEX-USB / HEX-USB+CAN: install [VCP](http://www.ross-tech.com/vag-com/usb/virtual-com-port.php), dumb pass-through, smart mode off. HEX-V2 and HEX-NET will never appear as a usable dumb K-line device. See [Getting started — Cable](getting-started.md#cable).
- Try another USB port. Unplug other serial adapters so the list is obvious.

## Slow init fails

- Wait **at least 2.6 seconds** between attempts.
- Confirm **Slow Init**, baud **10400**, default connect address. See [Getting started](getting-started.md#connect-with-slow-init).
- Adapter in **dumb** mode (no protocol translation). **Verify cable in dumb mode** on **KWP2000 Settings**.
- Enable **Slow init timing log** and retry. Compare the handshake against a known-good cable if you have one. Clone cables fail on FTDI and CH340; that is not a CH340-only 5-baud bug.
- If you just tried **Fast Init**, wait 2.6 seconds, then slow init.
- Bench: stable +12 V and ground; K-line actually on the ECU. ME7.5: pin 121 at +12 V for programming — [Getting started — Bench](getting-started.md#bench-vs-in-the-car).
- In-car: cluster on the K-line. A miss of the address complement is [issue #95](https://github.com/NefMoto/NefMotoOpenSource/issues/95), not “CH340 cannot send the address.”
- This is not [issue #100](https://github.com/NefMoto/NefMotoOpenSource/issues/100) (write hang **after** ident, when connect already succeeded).

## Fast init fails

- Switch to **Slow Init**. There is no automatic fallback.
- On the documented ME7.5 bench unit, fast init fails and slow init works on the same cable. That is expected for that unit. Other ME7.5 images may differ. [KWP2000.md](../KWP2000.md)
- After a flash write or abort, the ECU may still be on the RAM programming kernel. Prefer slow init. Do not change fast-init address mode to hunt for it. [issue #106](https://github.com/NefMoto/NefMotoOpenSource/issues/106)

## Connect works, programming or flash fails

- ME7.5 bench: pin 121 at +12 V (same switched rail as pins 3, 21, 62). Ident can succeed while a programming session fails if 121 is floating.
- Leave security settings at defaults unless you know you need otherwise.
- Stay on slow init for the programming session, especially after a previous write.
- Wrong memory layout is a **write** failure, not a connect failure — [Wrong Flash Layout](#wrong-flash-layout).

## Ident says RAM program after a write

`THIS-IS-THE-RAM-PROGRAM` (hyphens vary) means the ECU is still running the programming kernel in RAM. Logging, DTCs, and engine start stay broken until it leaves that kernel. The string by itself is not a brick.

- After a **successful** write: complete power cycle (ME7.5: all +12 off, including pin 121), then reconnect with **Slow Init**.
- After an **abort** (Wrong Flash Layout, sector fail, comm drop you can resume): **do not** power cycle. Retry KWP on the same power-on.

Developer detail: [Flashing](flashing.md#after-a-successful-write) and [KWP2000.md](../KWP2000.md#ram-kernel-after-write-this_is_the_ram_program).

## Bootmode NAK or wrong ACK

- Try **57600** or **38400**. Prefer those for first contact. 9600/19200 can fail non-deterministically on CH340 (wrong ACKs, NAK, readback errors) — USB latency/buffering, jitter, or voltage, not baud error. Prefer FTDI at those lower rates. [Bootmode](bootmode.md#baud), [issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44)
- **124800** is for performance after you have verified it on that ECU and cable.
- Confirm boot entry (often pin 24 on ME7), ECU variant (ME7 / Simos3 / EDC15), and power. See [Bootmode](bootmode.md).

## Wrong Flash Layout

KWP has no flash chip ID. You pick the XML layout. Top-boot (BT) and bottom-boot (BB) chips of the same density are different (`ME7 29F800BT` vs `ME7 29F800BB`).

On a KWP write, a boot-cluster erase failure now shows **Wrong Flash Layout** (OK only) and **aborts**. Status names the selected orientation vs what the chip looks like.

Recovery:

- Pick the **opposite** layout named in the dialog (for example `ME7 29F800BB` instead of `ME7 29F800BT`).
- **Diff Write Flash** — matching sectors are skipped (ECU checksum); the boot cluster still needs programming.
- Entire-flash erase does **not** fix BT vs BB. It still uses the current sector list.
- Switching layout in the same programming session is not implemented.

This is not **Sector Erase Failed** (persistent data on a middle 64KB sector). [Issue #103](https://github.com/NefMoto/NefMotoOpenSource/issues/103). [Flashing](flashing.md#after-an-abort)

Uniform `ME7 29F800` (sixteen 64KB sectors, no boot cluster) is a different mismatch.

## Sector Erase Failed

Yes/No/Cancel prompt about persistent data on a sector that is **not** the boot cluster of a BT/BB layout. That is the older path. Do not treat it as “pick the other BT/BB file.” [Flashing](flashing.md#erase)

## Flash verify or checksum fail

- Confirm the file matches the layout you selected.
- **Verify Checksums** is on the **KWP2000 Flashing** tab (there is no separate Checksum tab).
- After a write, verify can fail if the session dropped or the layout was wrong — see [Wrong Flash Layout](#wrong-flash-layout) and [Flashing](flashing.md).

## Write hangs after ident

Connect already succeeded; the hang is during write. That is [issue #100](https://github.com/NefMoto/NefMotoOpenSource/issues/100), not a slow-init failure. There is no documented workaround that is the “normal” write path. Attach the log.

## Reporting a problem

- Use **File → Open Log File** and attach a slice around the failure (connect attempt, handshake, first error).
- Say: protocol (KWP vs Boot Mode), Slow vs Fast init, baud, FTDI vs CH340 vs Ross-Tech, in-car vs bench, ECU (ME7.1 / ME7.5 / other), layout XML if flashing.
- Redact VIN or other personal data if it appears. Binary dumps are not required for a connect bug.
- Open an issue: <https://github.com/NefMoto/NefMotoOpenSource/issues>

## FAQ

**Slow or fast init?** Slow init is the default. Use fast only when you know it works. Fast init takes significantly less time to connect, but is not always supported. [Getting started](getting-started.md#connect-with-slow-init)

**Will my HEX-V2 / HEX-NET work?** No.

**In-car OK?** Often yes on ME7.1 with slow init; cluster K-line varies. [issue #95](https://github.com/NefMoto/NefMotoOpenSource/issues/95)

**CH340 OK?** Yes for KWP, same path as FTDI. Bootmode: prefer 57600/38400; see [Bootmode](bootmode.md#baud).

**Wrong BT/BB layout on a KWP write?** **Wrong Flash Layout**, abort, opposite XML, **Diff Write Flash**. Entire erase does not fix it. [Wrong Flash Layout](#wrong-flash-layout)
