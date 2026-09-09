# Flashing

KWP2000 flash read and write on the **KWP2000 Flashing** tab. Connect first ([Getting started](getting-started.md)). Back up with a **read** before any write.

EEPROM buttons on the same tab are bootmode-only. See [Bootmode](bootmode.md).

If a write fails, see [Troubleshooting](troubleshooting.md).

## Layout

KWP has no flash chip ID. You pick the XML in the layout combo:

- `ME7 29F800BT` — 1MB, **top-boot** small-sector cluster
- `ME7 29F800BB` — 1MB, **bottom-boot**
- `ME7 29F800` — 1MB, sixteen 64KB sectors (no boot cluster)
- `ME7 29F400` / `ME7 29F400BB` — 512KB
- `ME7 29F200` / `ME7 29F200BB` — 256KB

BT vs BB is the same density with the small sectors at opposite ends. A write with the wrong orientation can program most of the chip, then fail on the boot cluster. That is **Wrong Flash Layout**, not “persistent data.” Recovery: [Wrong Flash Layout](troubleshooting.md#wrong-flash-layout).

Most ME7.x ECUs are **29F800BB** (1MB) or **29F400BB** (512KB). The documented bench units are 29F800BB (`ME7 29F800BB`).

## File

**Choose Flash File** loads a `.bin` (or any file). Size must match the selected layout.

**Verify Checksums** checks ME7 checksums in the loaded file. There is no separate Checksum tab. **Correct Checksums** is not on the tab.

## Read

- **Full Read Flash** — whole layout
- **Diff Read Flash** — only sectors that differ from the loaded file
- **Check if Flash Matches** — compare without saving a new dump

Engine off. Confirmation dialogs include the ME7.5 pin 121 hint when connected over KWP.

If addressable flash extends past the selected layout (512KB chip mirrored into 1MB, or a 1MB chip with a 512KB layout), NefMoto asks whether to check for a mirror:

- **Yes** — compare a sample at the start of the layout with the same offset above it
- **No** — continue with this layout
- **Cancel** — abort

A matching sample is strong evidence of mirroring, not proof of chip size. A mismatch usually means the layout is too small (try `ME7 29F800`). Example: `06A 906 032 CL` — [issue #80](https://github.com/NefMoto/NefMotoOpenSource/issues/80).

## Write

- **Full Write Flash** — every sector in the layout
- **Diff Write Flash** — sectors whose ECU checksum does not match the file
- **Verify Write** — checked by default on KWP (read-back after program)

Confirmation requires: valid file and layout (BT vs BB must match the chip), engine not running, battery at least 12 V, adaptations will reset, the process can run uninterrupted.

**ME7.5 (121-pin) on the bench:** pin 121 must be at +12 V for KWP read/write. Same switched +12 V as pins 3, 21, and 62. Top right pin on the small connector. Ident can succeed while a programming session fails if 121 is floating.

Known: [issue #100](https://github.com/NefMoto/NefMotoOpenSource/issues/100) — write can hang after ident. Connect already worked. Attach the log; there is no documented “normal” workaround.

### After a successful write

Ident may show `THIS-IS-THE-RAM-PROGRAM` (hyphens vary) until a **complete** power cycle (ME7.5: all +12 off, including pin 121). Logging, DTCs, and engine start stay broken until then. The string is not a brick. Reconnect with **Slow Init**. [KWP2000.md](../KWP2000.md#ram-kernel-after-write-this_is_the_ram_program)

### After an abort

**Wrong Flash Layout**, a sector fail you can resume, or a comm drop: **do not** power cycle. Stay on the same power-on and retry KWP with slow init. [issue #106](https://github.com/NefMoto/NefMotoOpenSource/issues/106)

**Wrong Flash Layout** (OK only): pick the opposite layout named in the dialog, then write again. **Diff Write Flash** skips sectors that already match (ECU checksum), so the boot cluster is what still needs programming. Entire-flash erase does not fix BT vs BB. Switching layout in the same programming session is not implemented. [Issue #103](https://github.com/NefMoto/NefMotoOpenSource/issues/103)

## Erase

There is no standalone erase button. Sectors are erased as part of a write.

**Sector Erase Failed** (Yes/No/Cancel) is persistent data on a sector that is **not** a BT/BB boot cluster:

- **Yes** — erase the entire flash and restart the write. If that write then fails, the ECU may not boot; recover with [Bootmode](bootmode.md).
- **No** — skip that sector and continue
- **Cancel** — abort

Do not use entire-flash erase to “fix” a BT/BB mismatch.

## Next

- [Bootmode](bootmode.md)
- [Troubleshooting](troubleshooting.md)
- [Getting started](getting-started.md)
