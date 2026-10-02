# Bootmode

Bootmode is a different protocol on the same K-line: the C167 bootstrap loader instead of KWP2000. Use it when KWP2000 will not start a session, after a failed entire-flash erase, or to read/write the physical SPI EEPROM (95040) on ME7.1 / ME7.5.

Connect setup and cables: [Getting started](getting-started.md). NAK / wrong ACK: [Troubleshooting](troubleshooting.md#bootmode-nak-or-wrong-ack).

Flash read/write uses the **Flashing** tab. The protocol combo at the top of the window must be **Boot Mode**. **KWP2000 Logging** is hidden in Boot Mode.

## When to use it

- KWP connect or programming session is dead
- Entire-flash erase left the ECU unable to boot
- Physical 95040 dump or write (not the KWP EEPROM mirror)

Simos 3.x and EDC15: bootmode flash only (layout auto-detect). EEPROM presets on the **Bootmode EEPROM** tab are ME7.1 / ME7.5.

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

On **Flashing**, the layout combo is **disabled**. Layout comes from the flash device ID (`FC_GETSTATE`). Tooltip shows base address, size, and sector count when detection succeeded.

- **Full Read Flash** / **Full Write Flash**
- **Diff Read Flash** is not supported in bootmode (the loader has no checksum-for-range)
- **Verify Write** is forced off for bootmode flash write
- **Verify Read** is off in bootmode. A bootmode full read sends no `0xC5`
- Variant (ME7 vs Simos3 vs EDC15) is taken from the detected layout base (ME7 `0x800000`, Simos3/EDC15 `0x400000`). There is no separate variant dropdown.

**Choose Flash File** still loads a `.bin` for write.

After EEPROM access, the EEPROM driver has overwritten the flash driver at `0xF600`. Re-detect flash (disconnect / reconnect or **Read ECU Info**) before another flash read/write.

## Bootmode EEPROM

On the **Bootmode EEPROM** tab, which is hidden unless the protocol combo is **Boot Mode**. Read and write need a bootmode connection. **Read EEPROM Mirror** stays on **KWP2000 Info**; that is the KWP page at `0x6001E0`, not this chip.

Presets:

- `ME7.1 - 95040 SSC P4.7 (512 B)`
- `ME7.5 - 95040 SSC P4.7 (512 B)`
- XSSC variants of the same (try if SSC fails)

The grid is read-only: 32 pages of 16 bytes. With no image loaded it stays on screen as grey `00`s, and the legend stays grey. After a read or open, the legend shows the Immo, SKC, `P0602`, and Lockout values, then Checksum **ok** in green or **not ok** in red. The ASCII column highlights the ECU part number at `0x1C2` (11 characters), both tool ID copies at `0x1E2` and `0x1F2` (6 characters), both VIN copies (`0xB5`–`0xB9` plus `0xD0`–`0xDB`, and `0xC5`–`0xC9` plus `0xE0`–`0xEB`), and both immobilizer IDs (`0xDC` plus `0xF0`–`0xFC`, and `0xEC` plus `0x100`–`0x10C`). Hover a highlighted character for the field name. A non-printable byte is `.`.

**Load EEPROM File** displays a file. **Read EEPROM** dumps the chip and shows it. It does not ask for confirmation. **Save EEPROM File** writes the image on screen to a `.bin`. None of those program the chip.

**Write EEPROM** programs that image. It stays disabled until an image is loaded. Immobilizer / VIN / adaptations can change. If data-page checksums are invalid:

- **Yes** — correct data-page checksums, then continue (pages 28–29 are left unchanged)
- **No** — write the image as-is
- **Cancel** — abort

**Immo Off**, **Reset Lockout**, and **Clear P0602** edit the image on screen only. Each stays disabled when no image is loaded. **Reset Lockout** also stays disabled when the lockout bytes are already `00 00`. When immo is already off, that button reads **Immo On**. When bit 7 is already clear, that button reads **Set P0602**. The legend sits under the hex. Read, load, save, and edit results are lines in the status window. A line under the legend says a write is still pending. **Backup before write** starts on and is remembered in `preferences.json`. When it is on, a pending edit asks for a `.bin` of the image from before those edits. **Verify Write** applies.

- **Immo Off** sets `0x012` and `0x022` to `0x02`. Both copies must match. **Immo On** sets that pair to `0x01` when both are `0x02`. Page 1 and page 2 checksums are corrected.
- **Reset Lockout** sets `0x1EC`–`0x1ED` and `0x1FC`–`0x1FD` to `00 00`. The `P0602` flag and counters stay. Page 30 and page 31 checksums are corrected when those bytes change. This is the lockout field measured on ME7.5.
- **Clear P0602** clears bit 7 at `0x1E8` and `0x1F8`. **Set P0602** sets that bit. Other bits, counters, and lockout bytes stay. Page 30 and page 31 checksums are corrected when those bytes change.

## Next

- [Flashing](flashing.md) — KWP layout, BT vs BB, RAM kernel after a KWP write
- [Troubleshooting](troubleshooting.md)
- [Getting started](getting-started.md)
