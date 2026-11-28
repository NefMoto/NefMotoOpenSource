# Bootmode

Bootmode is a different protocol on the same K-line: the C167 bootstrap loader instead of KWP2000. Use it when KWP2000 will not start a session, after a failed entire-flash erase, or to read/write the physical EEPROM.

Connect setup and cables: [Getting started](getting-started.md). NAK / wrong ACK: [Troubleshooting](troubleshooting.md#bootmode-nak-or-wrong-ack).

Flash read/write uses the **Flashing** tab. The protocol combo at the top of the window must be **Boot Mode**. **KWP2000 Logging** is hidden in Boot Mode.

## When to use it

- KWP connect or programming session is dead
- Entire-flash erase left the ECU unable to boot
- Physical EEPROM dump or write (not the KWP EEPROM mirror)

Simos 3.x and EDC15 flash layout is auto-detected. EEPROM chip select on the **EEPROM** tab includes ME7.1 and ME7.5 together on P4.7, plus ME7.1.1, Simos 3.2, and EDC15 I2C.

## M5.9.2

M5.9.2 does not use Boot Mode. This screen is the C167 bootstrap loader. M5.9.2 is an 80C196 ECU, so it does not answer. The hello (`0x00` in, device ID back) is only a cable echo. No ID follows, at 9600 or at 38400.

Use **KWP2000**. Pin 43 selects which session the ECU answers.

- **Full Write** needs pin 43 at +12 V. It sends the loaded file on the first KWP2000 wake.
- **Full Read** needs pin 43 off +12 V. The held KWP1281 session copies `0x0000`-`0xBFFF` (49152 bytes). That is the calibration, not the whole flash.
- **Read EEPROM** uses that same read session and copies the 24C02. Pin 43 stays off +12 V.

### Write

Pin 43 stays at +12 V for the whole attempt. Set it before you apply power.

- Protocol **KWP2000**, connection method **Fast Init**. The connect-bar baud selects the speed byte.
- Connect. A good connect says `OK. Connect succeeded. First wake stayed KWP2000. Tester present is not sent.`
- **Full Write** sends the loaded file. Let it run. Tester present is not sent, and the app does not close this session on its own.
- **Read DTCs** and **Clear DTCs** need the read session, pin 43 off +12 V. They are blocked on the programming wake.

### If the write fails

[M5.9.2 write fails](troubleshooting.md#m592-write-fails).

Protocol detail is in [M5.9.2](../M5.9.2.md).

## Entry and power

Same ground and +12 V as a KWP bench. Bootmode needs the ECU in bootstrap, not a normal KWP session.

Ground flash pin 24, or short the boot pads, as ignition power comes up. Release it after 2-3 seconds. Leave it grounded while programming and the write fails. The CPU samples P0L.4 at power-up.

ME7.1 and ME7.5 have a pad pair for that net. Short the pads, or fit a switch across them.

![ME7.1 boot pads](../images/me7.1-bootmode.png)

ME7.1.1 boards differ. This one has a pad pair. On a board without them, ground flash pin 24. With the plugs to the right and the pin-1 mark at the top right of the flash, pin 24 is the second pin in from the left on the bottom edge.

![ME7.1.1 boot pads](../images/me7.1.1-bootmode.png)

Simos3 / EDC15 entry differs; use a pinout for that ECU.

The app reports **Put ECU in boot mode** if the handshake never starts.

If the loader core is already running (`0xAA`), the app uploads the flash driver and reads the flash type with `FC_GETSTATE`. A flash id from an earlier connect in this process is not reused. If that query fails, the app shows **Flash Type Not Detected** and flash read and write stay disabled. Disconnect, power-cycle to a full boot entry, and reconnect.

## Baud

Default **57600**. Try **38400** if 57600 fails (especially CH340).

M5.9.2 does not use this loader. The programming wake is pin 43 at +12 V for a write, and off +12 V for a read ([M5.9.2](#m592)).

9600 and 19200 can fail non-deterministically on CH340 (wrong ACKs, NAK, readback errors). Likely USB latency, buffering, jitter, or voltage — not baud error. Prefer FTDI at those lower rates. [Issue #44](https://github.com/NefMoto/NefMotoOpenSource/issues/44)

**124800** is for speed after you have verified it on that ECU and cable. Most reliable on the bench.

FTDI and CH340 are equivalent for KWP. Bootmode is where CH340 baud caveats belong.

## Connect

- Protocol: **Boot Mode**
- Device list, **Refresh Devices**
- Baud as above
- **Connect Boot Mode**

Then **KWP2000 Info** → **Read ECU Info** (device ID, CPU family, SYSCON / BUSCON / ADDRSEL, memory status). `ecuflash bootmode info` prints the same entries. `-o` writes those lines to a file. See [Flashing — Command line](flashing.md#command-line).

## Flash

On **Flashing**, the layout combo is **disabled**. Layout comes from the flash device ID (`FC_GETSTATE`). Tooltip shows base address, size, and sector count when detection succeeded. A running core gets the same driver upload and `FC_GETSTATE` on every connect. A flash id from an earlier connect is not reused. If that query fails, the combo shows **Flash type not detected** and **Flash Type Not Detected** appears.

- **Full Read Flash** / **Full Write Flash**
- **Diff Read Flash** / **Diff Write Flash** skip a sector whose checksum matches the loaded file. The routine is uploaded to `0xF840`. A bootmode read sends no `0xC5`
- **Verify Checksums**, while connected, checksums each sector against the loaded file. Sector bytes are not read. A differing sector is named. Nothing is saved. `ecuflash bootmode verify -i` runs the same check
- **`ecuflash bootmode reset`** sends command `0x32` and closes the port. It does not connect again. The boot pin is sampled on that reset. Held, the chip waits for the loader. Released, the chip starts from flash
- **Verify Write** — checked, each programmed sector is checksummed after the write. Unchecked, a bootmode write does not checksum. A matching sector is still skipped before erase
- **Verify Read** — checked, each transferred sector is checksummed after the read. Unchecked, a bootmode read does not checksum
- Variant (ME7 vs Simos3 vs EDC15) is taken from the detected layout base (ME7 `0x800000`, Simos3/EDC15 `0x400000`). There is no separate variant dropdown.

**File > Choose Flash File** still loads a `.bin` for write.

After EEPROM access, the EEPROM driver has overwritten the flash driver at `0xF600`. Re-detect flash (disconnect / reconnect or **Read ECU Info**) before another flash read/write.

## Bootmode EEPROM

On the **EEPROM** tab. **Read EEPROM** reads the chip in bootmode, or through the KWP mirror over a KWP2000 connection. **Write EEPROM** needs a bootmode connection. **Read EEPROM Mirror** stays on **KWP2000 Info**; that is the KWP page at `0x6001E0`, not this chip.

### Read EEPROM over KWP2000

Connected with KWP2000 instead of bootmode, **Read EEPROM** reads a 95040 through the ECU. It uses the development session, like **Read EEPROM Mirror**, and reads the mirror at `0x600000` one 16-byte page at a time. No login is needed and nothing is written.

ME7.1.1 refuses pages 3 and 4 (SKC and cluster code) with `7F 23 12`. The read logs them as withheld and carries on. Then it reads RAM `0x380000-0x387FFF` and looks for the firmware's EEPROM shadow: one 16-byte slot per record id, in the high byte of the page descriptor. The base is where the most served pages match, and at least 4 must match. A withheld page is taken from its slot only when its own page checksum is valid.

Pages that still can't be read stay `FF` and the status log names them. **Save EEPROM File** keeps them as `FF`. **Write EEPROM** is disabled for an image with missing pages, because writing it would erase them on the chip.

Chip select:

- `ME7.1 / ME7.5 P4.7` — one entry. Both use port 4 pin 7
- `ME7.1.1 P6.3`
- `Simos 3.2 P6.4`
- `EDC15 I2C` (no pin)

Part, for a SPI chip select. Each part has one size. The default is `95040`, 512 bytes, SSC.

- `95040` — 512 bytes. Driver type 1
- `95P08` — 1024 bytes. Driver type 0, a 16-bit address. Often used with `ME7.1.1 P6.3`
- `95080` — 1024 bytes. Driver type 0
- `95160` — 2048 bytes. Driver type 0, a 16-bit address. The `07K906032B` part, on `ME7.1.1 P6.3`
- `95320` — 4096 bytes
- `93S46` — 128 bytes. SSC only
- `93S56` — 256 bytes. SSC only
- `93S66` — 512 bytes. SSC only
- `93S76` — 1024 bytes. SSC only
- `93S86` — 2048 bytes. SSC only

A Debug build adds an `SSC` / `XSSC` dropdown for `95040`, `95P08`, `95080`, `95160`, and `95320`. A Release build does not. Those parts use SSC. EDC15 replaces the part list with `24C04` (512 bytes, the default) and `24C02` (256 bytes) and forces I2C.

The chip select, peripheral, and part are remembered in `preferences.json`. The size is not stored. It comes from the part.

The grid is read-only, 16 bytes per row. With no image loaded it stays on screen as grey `00`s for the selected part size, and the legend stays grey. A `95040` 512-byte image shows 32 rows. The legend then shows the Immo, SKC, `P0602`, and Lockout values, then Checksum **ok** in green or **not ok** in red. The SKC is page 3 bytes 2-3 (`0x032`, copy at `0x042`), little-endian, shown as 5 decimal digits, or **unset** when the bytes are `FF FF`. The word at `0x014`/`0x024` is the legend's immobiliser state field, not the SKC. On `07K906032B` the id `0x22` reader does not use that word. The ASCII column highlights the ECU part number at `0x1C2` (11 characters), both tool ID copies at `0x1E2` and `0x1F2` (6 characters), both VIN copies (`0xB5`–`0xB9` plus `0xD0`–`0xDB`, and `0xC5`–`0xC9` plus `0xE0`–`0xEB`), and both immobilizer IDs (`0xDC` plus `0xF0`–`0xFC`, and `0xEC` plus `0x100`–`0x10C`). Hover a highlighted character for the field name. Those addresses are offsets in the map window. Checksum color is columns 14 and 15 of each full page in that window. A non-printable byte is `.`. A `95P08` or `95160` image uses that legend on the map window. Bytes after the window are plain hex, labeled with the chip offset. Any other part size is plain hex, with a grey legend.

**Load EEPROM File** displays a file of the selected part size. **Read EEPROM** dumps the chip and shows it. It does not ask for confirmation. A 512-byte read on P6.3 whose every checksummed page fails suggests trying a `95P08`. A bootmode read whose selected size is over 512 bytes checks the first 512 as a 95040. A `95P08` or `95160` reports those page checksums as ok or how many failed. For any other part, a match says the chip looks like a 95040 and names the selected part. When they do not match and page 0 byte 15 is not `0x5A`, the status says the size is unverified. **Save EEPROM File** writes the image on screen to a `.bin`. None of those program the chip.

On `07K906032B` the part is a `95160`, 2048 bytes, chip select `ME7.1.1 P6.3`. Immo Off uses the same map window as a `95P08`. Nefmoto topic 20757 has 2048-byte dumps from another `07K906032B`. Those differ at `0x12`, `0x1E`, `0x22`, and `0x2E` only, match at byte 0, and have data from `0x410` through `0x6AF`. They are a different EEPROM from this bench read.

**Write EEPROM** programs that image. It stays disabled until the image length matches the selected part. Immobilizer / VIN / adaptations can change. If data-page checksums are invalid on a `95040`, a `95P08`, or a `95160`:

- **Yes** — correct data-page checksums, then continue (pages 28–29 are left unchanged)
- **No** — write the image as-is
- **Cancel** — abort

A ME7.1.1 P6.3 session also holds P6.4 low, and releases it high when the read or write returns. A `95040`, `95P08`, or `95160` write clears block protect once before programming when BP0 or BP1 is set. A driver state of `0x00F0`, `0x0080`, `0x0000`, or `0x0002` does not stop the read or the write. `0x0000` is the status register after bit 7 has been cleared. `0x0002` is the write-enable latch left set when a cycle does not finish. `0x00FF` is still a wrong chip select. If BP0 or BP1 stays set, the write stops and the status line says the EEPROM is write-protected and was not modified. A `95040` write sends write-enable, then one address byte with A8 in the opcode, then 16 data bytes, before chip select rises. A `95P08` or `95160` write sends write-enable, opcode `0x02`, the high address byte, the low address byte, then 16 data bytes. Chip select then stays high for the program cycle, and the status register is read once. In `07K906032B`, EEPROM init at `0x8CE472` sets bit `0x52.6`, and the writer at `0x8CD75A` sends that 16-bit address and then raises chip select and returns. `0x8CD7CA` is the one-address-byte writer, and it runs only when that bit is clear. A write-enable latch that is still set fails the page. The build that polled the busy bit immediately reported every page done and left chip `0x0041` as `0x00` where the image has `0x04`. The reads before that write matched 18 pages. The original image matches 21. The clocked `SSCCON` is `0xC037`. The ECU loads `0x4637`, sets the enable bit, and clears bits 8-11 before each byte. The write progress line advances every 10 percent. Verify reads the whole chip and names every byte that differs from the image that was written.

**Immo Off** edits a `95040` 512-byte image, a `95P08` 1024-byte image, or a `95160` 2048-byte image on screen only. **Reset Lockout** and **Clear P0602** edit a `95040`, a `95P08`, or a `95160`. On any other device they stay disabled. Each stays disabled when no image is loaded. **Reset Lockout** also stays disabled when the lockout bytes are already `00 00`. When immo is already off, that button reads **Immo On**. When bit 7 is already clear, that button reads **Set P0602**. The legend sits under the hex. Read, load, save, and edit results are lines in the status window. A line under the legend says a write is still pending. **Backup before write** starts on and is remembered in `preferences.json`. When it is on, a pending edit asks for a `.bin` of the image from before those edits. **Verify Write** applies.

- **Immo Off** on a `95040`, a `95P08`, or a `95160` sets map `0x012` and `0x022` to `0x02`. Both copies must match. **Immo On** sets that pair to `0x01` when both are `0x02`. Page 1 and page 2 checksums are corrected. Bytes outside the map window stay. On `07K906032B` the id `0x22` reply copies chip `0x012` and chip `0x022`/`0x023` and does not compare them with `0x01` or `0x02`. No EEPROM byte in that image disables the immobilizer.
- **Reset Lockout** sets `0x1EC`–`0x1ED` and `0x1FC`–`0x1FD` to `00 00`. The `P0602` flag and counters stay. Page 30 and page 31 checksums are corrected when those bytes change.
- **Clear P0602** clears bit 7 at `0x1E8` and `0x1F8`. **Set P0602** sets that bit. Other bits, counters, and lockout bytes stay. Page 30 and page 31 checksums are corrected when those bytes change.

`ecuflash bootmode immo off` reads the chip, sets those immo bytes, saves the previous image, and writes the chip. The default preset is `95040` on P4.7. `--95p08` selects `95P08` on P6.3. `-o` chooses the backup. An existing file is left in place and the chip is not written. See [Flashing — Command line](flashing.md#command-line).

## Next

- [Flashing](flashing.md) — KWP layout, BT vs BB, RAM kernel after a KWP write
- [Troubleshooting](troubleshooting.md)
- [Getting started](getting-started.md)
