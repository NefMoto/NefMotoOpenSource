# Bootmode

Bootmode is a different protocol on the same K-line: the C167 bootstrap loader instead of KWP2000. Use it when KWP2000 will not start a session, after a failed entire-flash erase, or to read/write the physical SPI EEPROM (95040) on ME7.1 / ME7.5.

Connect setup and cables: [Getting started](getting-started.md). NAK / wrong ACK: [Troubleshooting](troubleshooting.md#bootmode-nak-or-wrong-ack).

Flash read/write still uses the **KWP2000 Flashing** tab. The protocol combo at the top of the window must be **Boot Mode**.

## When to use it

- KWP connect or programming session is dead
- Entire-flash erase left the ECU unable to boot
- Physical 95040 dump or write (not the KWP EEPROM mirror)

Simos 3.x and EDC15: bootmode flash only (layout auto-detect). EEPROM presets on the Flashing tab are ME7.1 / ME7.5.

## Entry and power

Same ground and +12 V as a KWP bench. Bootmode needs the ECU in bootstrap, not a normal KWP session.

Typical ME7 boot pin is **24** (hold at the level your pinout specifies while applying power). Confirm against a diagram for your connector. Simos3 / EDC15 entry differs; use a pinout for that ECU.

The app reports **Put ECU in boot mode** if the handshake never starts.

If ident shows the loader core already running (`0xAA`) with no stored flash ID: disconnect, power-cycle to a full boot entry, reconnect so the flash type can be read.

## Baud

Default **57600**. Try **38400** if 57600 fails (especially CH340).

9600 and 19200 can fail non-deterministically on CH340 (wrong ACKs, NAK, readback errors). Likely USB latency, buffering, jitter, or voltage — not baud error. Prefer FTDI at those lower rates. [Issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44)

**124800** is for speed after you have verified it on that ECU and cable. Most reliable on the bench.

FTDI and CH340 are equivalent for KWP. Bootmode is where CH340 baud caveats belong.

## Connect

- Protocol: **Boot Mode**
- Device list, **Refresh Devices**
- Baud as above
- **Connect Boot Mode**

Then **KWP2000 Info** → **Read ECU Info** (device ID, CPU family, SYSCON / BUSCON / ADDRSEL, memory status).

## Flash

On **KWP2000 Flashing**, the layout combo is **disabled**. Layout comes from the flash device ID (`FC_GETSTATE`). Tooltip shows base address, size, and sector count when detection succeeded.

- **Full Read Flash** / **Full Write Flash**
- **Diff Read Flash** is not supported in bootmode (the loader has no checksum-for-range)
- **Verify Write** is forced off for bootmode flash write
- Variant (ME7 vs Simos3 vs EDC15) is taken from the detected layout base (ME7 `0x800000`, Simos3/EDC15 `0x400000`). There is no separate variant dropdown.

**Choose Flash File** still loads a `.bin` for write.

After EEPROM access, the EEPROM driver has overwritten the flash driver at `0xF600`. Re-detect flash (disconnect / reconnect or **Read ECU Info**) before another flash read/write.

## EEPROM (physical 95040)

On the Flashing tab, with **Boot Mode** connected:

Presets:

- `ME7.5 - 95040 SSC P4.7 (512 B)`
- `ME7.1 - 95040 SSC P4.7 (512 B)`
- XSSC variants of the same (try if SSC fails)

**Read EEPROM (Bootmode)** dumps the chip (not a KWP mirror). Saves a `.bin`.

**Write EEPROM (Bootmode)** programs the chip. Immobilizer / VIN / adaptations can change. Backup first.

If ME7 95040 data-page checksums are invalid:

- **Yes** — correct data-page checksums, then continue (pages 28–29 HW/SW ID are left unchanged)
- **No** — write the file as-is
- **Cancel** — abort

Size mismatch: only the file length is written (no pad to 512 B). Confirm or cancel.

## Next

- [Flashing](flashing.md) — KWP layout, BT vs BB, RAM kernel after a KWP write
- [Troubleshooting](troubleshooting.md)
- [Getting started](getting-started.md)
