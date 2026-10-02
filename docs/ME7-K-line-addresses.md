# ME7 K-line addresses per image

Which slow-init and fast-init addresses each ME7 image answers, and where that is decided in the firmware. Use this when a report says `0x11` or fast init does not connect on a given ECU. Tool behavior and bench results are in [KWP2000.md](KWP2000.md).

Static analysis of the application firmware, not the RAM programming kernel. Traced on `8D0907551M-0002` (M), `8E0909518AK-0004` (AK), `06A906032NL` (NL) and `4B0906018DQ` (DQ). Groups for the other images come from byte-signature matches across the 94 images in ME7Sum `bins/`. Addresses are M flash/RAM addresses unless prefixed. A boot-sector target below `0x010000` is the same bytes at `0x800000` plus that offset when the image is loaded at `0x800000` (`0x002C1A` in a call chain is image `0x802C1A`).

In [KWP2000.md](KWP2000.md) the bench row labeled ME7.5 is a `4B0906018CH` unit running `06A906032NL`. AK (`8E0909518`) is the ME7.5 image family, a different ECU.

Bench-verified on two units only (direct K-line, FTDI and CH340):

- `8D0907551M` 0002: slow init `0x01` and `0x11`; fast init physical `0x01` (no reply at `0x10`)
- `06A906032NL` on a `4B0906018CH` unit: slow init `0x01` and `0x11`; fast init physical `0x10`

## Slow init

The 5-baud receiver (M `0x82B6DA`) samples the K-line RX pin (P3.11) against timer T4, takes a start bit plus 8 bit times, and compares the whole byte with an address table. Parity is not checked, so bit 7 must be 0 ([issue #125](https://github.com/NefMoto/NefMotoOpenSource/issues/125)).

The table has 30-byte entries (M flash `0x81B050`, 4 entries; pointer set at `0x82B672`):

| Offset | Content |
| --- | --- |
| +0 | address |
| +1 | sync `0x55` |
| +2, +3 | key bytes |
| +4 | expected tester `~KB2` |
| +5 | 1 for KWP1281 |
| +6 | byte sent after `~KB2` (the address complement) |
| +7 | baud index |
| +8..+0x17 | W timings (ms) |
| +0x18 | baud reload (10400 on all entries seen) |
| +0x1A | protocol handler pointer |

Complements from +6: `0x11` → `0xEE`, `0x01` (KWP2000 entry) → `0xFE`, `0xFE` → `0x01`, `0x33` → `0xCC`.

Key bytes in this table include the odd parity bit. KWP1281 `01 8A` is the connect log's `KB1=0x01 KB2=0x0A`. KWP2000 `EF 8F` is already the form the log shows.

| Group (count) | Entries (address: key bytes) | Images |
| --- | --- | --- |
| A (38) | `11:EF8F` `33:0808` `FE:EF8F` `01:018A` | 8D0907551F..Q incl. K/L/M, 4Z7907551 (most), 4B0907551R/S/T/AA, 4B0906018Q/R, 06A906032 (most), 066906032E, 8N0906018CJ |
| B (21) | `11:EF8F` `33:0808` `31:EF8F` `01:018A` | 4B0906018CH/DC/DH, 4B0907551AH/AL/M, 4Z7907551AA/L/M/N/Q/R/S, 8D0907551AA/T, 022906032CS/CT, 8E0906018B |
| C (20) | `11:EF8F` `33:0808` `01:018A` | 4B0906018 (no suffix)/AR, 4B0907551D/E/F/G/K/L, 8D0907551A..E, 4D0907558S, 4D0907559D, 06A906032AR/T |
| D (1) | `11:EF8F` `33:E98F` `01:018A` | 4Z7907551T |
| E (11) | `01:EF8F` `33:0808` `31:EF8F` | 8E0909518AA/AK, 4D1907558*, 4B0906018DA |
| F (3) | `01:EF8F` `33:E98F` | 4B0906018DQ, 4E0910559E, 006410010A0 |

- Groups A–D (80 images): `0x01` is KWP1281 (keys `01 8A`), `0x11` is KWP2000 (keys `EF 8F`).
- Groups E–F (14 images): `0x01` is KWP2000 directly; there is no `0x11` entry.
- `0x33` is the OBD entry, also gated by a coding word.

### KWP1281 then KWP2000 on `0x01`

Groups A–D only. This is the path the tool takes with **Connect address** `0x01`:

- Slow init at `0x01` (KWP1281). The ECU sends its ident blocks; the step is marked done only with engine speed 0.
- Tester sends a KWP1281 ACK block (`0x09`); marked only with engine speed 0.
- Tester sends end of communication (`0x06`). An idle counter starts (upper limit M calibration `0x196F3` = 100, lower limit `0x196F4` = 23; tick rate not traced).
- A second slow init at `0x01`, while the counter is between the limits and engine speed is 0, answers with the `0x11` entry's keys `EF 8F` and complement `0xFE` (M `0x82B8BE`, `0x82BC28`).

Engine-speed checks: M `0x8652C4`, `0x86441E`, `0x82B8B8`. With the engine running the second `0x01` should answer KWP1281 again, and the tool falls through to its `0x11` retry. Not bench-tested with a running engine.

## Fast init

The wake-up table (M `0x81B11C`, 8-byte entries: min/max low time in ms, handler) accepts 23–27 ms low on all 94 images and hands off to the same KWP2000 handler as slow init. A second window, 78–82 ms, goes to a separate handler (M `0x801D60`), purpose unknown.

After the header, the application's receive state machine calls an address filter in the boot sector. A rejected frame is dropped with no reply. The filter switches on the format byte's address mode:

| Format byte | Mode | Target must equal |
| --- | --- | --- |
| `0x0n` | no address | anything |
| `0x4n` | mode 1 | RAM `0xE20B` (AK), `0xE20D` (NL) |
| `0x8n` | physical | RAM physical byte |
| `0xCn` | functional | RAM functional byte or RAM physical byte |

Only StartCommunication is filtered in practice: after the `EF 8F` reply the tool uses 1-byte headers (`0x0n`).

| Image | Filter call | Physical RAM | Functional RAM |
| --- | --- | --- | --- |
| AK (MD5 `788d0b634a34df0443d2e281da59f231`) | `0x839EA6` → `0x000244` → `0x803DF6` | `0xE208` = `0x10` (`0x839668`) | `0xE209` = `0x31` (`0x839704`) |
| NL (MD5 `5c9047c68349946005fa09adcc40b28e`) | `0x839BCA` → `0x002C1A` | `0xE20A` = `0x10` (`0x8392EA`) | `0xE20B` = `0xFE` (`0x839386`) |
| DQ (MD5 `72a1e7d46162c91b2f28866ed9376bb3`) | `0x836664` → `0x000244` → `0x803DF6` | `0xE208` = `0x10` (`0x835E26`) | `0xE209` = `0x13` (`0x835EC2`) |

- The physical byte becomes `0x11` if the low word of RAM `0xE048` equals an image constant (AK `0x396C` at `0x82023A`, NL `0x2810` at `0x820242`, DQ `0x37DC` at `0x82023A`). What sets `0xE048` is not traced.
- An OBD slow init (`0x33`) or a StartCommunication to `0x33` sets the AK/DQ functional byte to `0x33`; the reply keys are then `E9 8F` (AK `0x839CC8`).
- NL's reply header takes its source byte from the physical byte (builder `0x8396FA`, boot copy `0x002D28`, image `0x802D28`).
- AK's boot sector has its own KWP2000 init (AK `0x803ADE`: physical `0x11`, functional `0x10`, keys `DF 8F`) and an uncalled-looking block (AK `0x808B56`: physical `0x01`, functional `0x10`). Neither is on the application's power-up path.

The M-box filter (M `0x808EA2`) compares the physical target with the literal `0x01`, and the functional target with RAM `0xE20B` (`0xFE`) or RAM `0xE20A` (`0x01`, or `0x11` under the untraced condition at M `0x8202DA`). Byte-signature sweep of the filter:

- Physical literal `0x01` only (42 images): 8D0907551 A..Q, most 4Z7907551 and 4B0907551, 06A906032CL, 4D0907558S, 4D0907559D.
- Physical `0x10` (45 images): 4D1907558*, 4Z7907551AA/L/M/N/Q/R/S/T, 4B0907551AH/AL/M, 8D0907551AA/T, 06A906032 (most), 066906032E, 022906032CS/CT, 4B0906018 (most), 8E0906018B, 8N0906018CJ, 8E0909518AA/AK. One filter copy has a literal `0x10`, another the RAM byte; which is live was checked on AK, NL and DQ only.
- Literal `0x01` plus a RAM-byte copy (5 images): 06A906032AR/T, 4B0906018 (no suffix)/AR/Q.
- Filter not found (2 images): 006410010A0, 4E0910559E.

The tool's physical fast-init order (`0x10`, then the connect address, `0x01`, `0x11`) covers the literal-`0x01` group, the `0x10` group, and the mixed literal-plus-RAM group. The two images whose filter was not found (`006410010A0`, `4E0910559E`) stay unchecked.
