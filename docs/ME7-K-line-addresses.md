# ME7 K-line addresses

The application firmware picks the slow-init and fast-init address. The connect sequence is in [KWP2000.md](KWP2000.md).

## Slow init

The 5-baud receiver samples the K-line RX pin (P3.11) against timer T4, takes a start bit plus 8 bit times, and compares the whole byte with an address table. Parity is not checked, so bit 7 must be 0 ([issue #125](https://github.com/NefMoto/NefMotoOpenSource/issues/125)).

The table has 30-byte entries:

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
| +0x18 | baud reload (10400) |
| +0x1A | protocol handler pointer |

Complements from +6: `0x11` → `0xEE`, `0x01` (KWP2000 entry) → `0xFE`, `0xFE` → `0x01`, `0x33` → `0xCC`.

Key bytes in this table include the odd parity bit. KWP1281 `01 8A` is the connect log's `KB1=0x01 KB2=0x0A`. KWP2000 `EF 8F` is logged as `KB1=0x6F KB2=0x0F`.

Two table shapes:

- `0x01` is KWP1281 (keys `01 8A`) and `0x11` is KWP2000 (keys `EF 8F`). `0x33` is the OBD entry, also gated by a coding word.
- `0x01` is KWP2000 directly. There is no `0x11` entry. Calibration ident `1A 9B` that does not contain `M5.9` keeps the ME7 path ([M5.9.2](M5.9.2.md)).

### KWP1281 then KWP2000 on `0x01`

Only the first shape. This is the path the tool takes with **Connect address** `0x01`:

- Slow init at `0x01` (KWP1281). The ECU sends its ident blocks. The step is marked done only with engine speed 0.
- Tester sends a KWP1281 ACK block (`0x09`), marked only with engine speed 0.
- Tester sends end of communication (`0x06`). An idle counter starts.
- A second slow init at `0x01`, while the counter is inside its limits and engine speed is 0, answers with the `0x11` entry's keys `EF 8F` and complement `0xFE`.

With the engine running the second `0x01` answers KWP1281 again, and the tool falls through to its `0x11` retry.

## Fast init

The wake-up table accepts 23–27 ms low and hands off to the same KWP2000 handler as slow init. A second window, 78–82 ms, uses a separate handler.

After the header, the application's receive state machine calls an address filter in the boot sector. A rejected frame is dropped with no reply. The filter switches on the format byte's address mode:

| Format byte | Mode | Target must equal |
| --- | --- | --- |
| `0x0n` | no address | anything |
| `0x4n` | one address | that image's one-address RAM byte |
| `0x8n` | physical | the physical RAM byte |
| `0xCn` | functional | the functional RAM byte or the physical RAM byte |

Only StartCommunication is filtered: after the `EF 8F` reply the tool uses 1-byte headers (`0x0n`).

The physical byte is `0x10`, or `0x01` on images whose filter compares with that literal. It becomes `0x11` when the low word of RAM `0xE048` matches an image constant. The reply source byte comes from the physical byte.

The tool's physical fast-init order (`0x10`, then the connect address, `0x01`, `0x11`) covers both physical targets. Functional fast init is unchanged.
