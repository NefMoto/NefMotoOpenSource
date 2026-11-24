# ME7 EEPROM page checksum

Developer notes for the 512-byte map on ME7.1, ME7.5, and ME7.1.1. The user-facing screens are in [Bootmode](user-guide/bootmode.md).

`95040` in this tree means that map, including when the bytes are the first half of a `95P08`. The class names (`Me7Eeprom95040Checksum`, `Me7Eeprom95040Fields`, `Me7Eeprom95040Immo`) follow the map, not the chip.

## The sum

Thirty-two pages, 16 bytes each. The stored word is bytes 14-15, little-endian.

- Add bytes 0-13 and the page index.
- Subtract 1 when descriptor bit 6 is set. That is the only descriptor bit that changes the sum.
- Negate the sum as a 16-bit word.

The tool treats bit 0 as "this page stores a checksum." The firmware checker requires bits 0 and 1. Pages 28 and 29 are scored. `CorrectChecksums` does not rewrite them.

The shipped descriptor list is the ME7.1 and ME7.5 map. `TryCopyImageTable` replaces it only when a flash file is already open and the first 20 words match that list. A bootmode EEPROM read does not load the flash. Do not scan the image for the checksum function's table pointer during a read. An ME7.1.1 map that differs before word 20 is invisible to that search, and detection still has to work with no table.

## Families

- **ME7.1 and ME7.5.** Chip select P4.7. The map starts at byte 0 and matches the shipped list.
- **ME7.1.1, 95040.** Chip select P6.3. A real 512-byte chip on this pin also starts at byte 0. Do not reject every 512-byte read on P6.3. `P63SizeAdvice` says `Try 95P08, 1024 bytes.` only when every shipped page fails.
- **ME7.1.1, 95P08.** Same SPI type as the 95040 (`Type95040`), 1024 bytes, same P6.3 select. When the map starts at byte 0 it occupies the first 512 bytes. Bytes past 512 are not checksummed and are not rewritten. On the images that match the shipped list at byte 0, the read reports ok or how many shipped pages failed.
- **ME7.1.1, map at byte 1.** The same 14-byte sum matches when the window starts at byte 1. Byte 0 of the read is outside the map. The firmware checksum routine has no `+ 1`. The shift is a property of these reads. One such image matches 21 pages at byte 1 and none at byte 0. Page 31 can be stored `0x100` above the formula. Do not add `0x100` to force a match. Pages the ECU leaves unchecksummed match neither bit-6 variant. They are not failures.
- **95080 and larger.** SPI type `Type95080To95320`. A 95080 read of a 95040-sized map is a different shift. Do not treat it as a 95P08. When the first 512 pass the shipped list, the status says the chip looks like a 95040. When they do not, and page 0 byte 15 is not `0x5A`, the size is unverified.

## Choosing the window

`MapOffset` looks only at the EEPROM bytes.

- Any page the shipped list accepts at byte 0 ends the search. One bad page stays on the existing warning. A flash table that is already loaded counts as the shipped list for this step.
- Any page the formula matches at byte 0, with bit 6 clear or set, also stays at byte 0.
- Byte 1 is used only when byte 0 matches nothing and byte 1 matches at least 8 complete pages. Each page has 2 chances in 65536, so one accidental hit is not a map. A blank image stays a failure.
- A 512-byte buffer does not contain the last byte of a byte-1 window. Score and rewrite only complete pages.
- The status line for this path is how many pages matched. It does not say `N bad / M checked`, because a page that matched neither variant was not known to be checksummed.
- `P63SizeAdvice` still uses the shipped result. A 512-byte P6.3 read that fails every shipped page still suggests a 95P08, including when byte 1 matched.

## Edits

Immo off and on edit pages 1 and 2 when `MapOffset` is 1 and those pages already match. The checksum keeps the bit that matched. Lockout and `P0602` stay off, because they write page 31. At byte 0, `WriteKeptChecksum` writes the shipped checksum. The legend reads the map window, and a byte-1 checksum caption is how many pages matched. The hex grid is that same window. A byte-1 map starts one byte into the file, so chip byte 0 is not a cell. Field highlights and checksum color use the window offsets. An unedited byte-1 image is written as-is. Yes on a byte-0 image still calls `CorrectChecksums`.
