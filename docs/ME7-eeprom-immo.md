# ME7 EEPROM immobilizer

Developer notes for ME7.1, ME7.5, and ME7.1.1. The button is in [Bootmode](user-guide/bootmode.md). Page sums are in [ME7 EEPROM page checksum](ME7-eeprom-checksum.md). Most ME7.1 images use the ME7.5 compare. Some copy the two words and never compare them with `0x02`.

The ECU compares the selected status byte with `0x02`. Immo Off writes `0x012`. That is the compared byte only when the flag selects the later copy.

## Copies

Pages are 16 bytes. Byte 2 of a page is the field. Each field is an earlier byte and a later byte.

- `0x002` and `0x012` are the status pair.
- `0x022` and `0x032` are the login pair.

Page flag bits `0x98` choose the copy.

- **ME7.1.1.** Clear flags use the earlier byte (`0x002` or `0x022`). Set flags use the later byte (`0x012` or `0x032`).
- **ME7.5, and most ME7.1.** The earlier byte is stored first. The later byte overwrites it when that page's flags are also clear, so both flags clear leaves `0x012` and `0x022`. Some ME7.1 images copy `0x002` and `0x012` into two different words and do not compare either word with `0x02`.

## Status byte

The id `0x22` reply carries the selected status byte. The caller zero-extends it and compares the word with `0x0004` and with `0x0002`. The next byte has to stay `0x00`.

- `0x01`, the usual stored value, sets the immobilizer bit.
- `0x02` and `0x04` leave that bit clear.

Login tests the same stored word for `2`, `4`, `5`, and `6` after the EEPROM byte has been read. `5` and `6` are states login writes. The EEPROM value that takes the clear path when the byte is loaded is `0x02` or `0x04`.

## Login word

The selected login byte (`0x022` or `0x032`) is compared with the bytes received from the cluster. It is not compared with the constant `0x02`. That match is authentication.

## What Immo Off writes

Immo Off sets `0x012` and `0x022` to `0x02` when those two bytes match, then rewrites the two page checksums. Immo On sets that pair back to `0x01` when both are `0x02`. `0x002` and `0x032` are left as they are.

On ME7.5, and on most ME7.1, both flags clear leaves `0x012`, which this edit changes. On ME7.1.1 with the status flag clear, the live byte is `0x002`, which this edit does not change. On the ME7.1 images that do not compare the status byte, this edit changes a copied word and does not clear an immobilizer bit.
