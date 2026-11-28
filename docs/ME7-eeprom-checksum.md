# ME7 EEPROM page checksum

Developer notes for the 512-byte map on ME7.1, ME7.5, and ME7.1.1. The user-facing screens are in [Bootmode](user-guide/bootmode.md).

`95040` in this tree means that map, including when the bytes are the first half of a `95P08` or a `95160`. The class names (`Me7Eeprom95040Checksum`, `Me7Eeprom95040Fields`, `Me7Eeprom95040Immo`) follow the map, not the chip.

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
- **ME7.1.1, 95P08.** SPI type `Type95080To95320` (type 0), a 16-bit address, 1024 bytes, P6.3 select. The map occupies the first 512 bytes. Bytes past 512 are not checksummed and are not rewritten. On the images that match the shipped list, the read reports ok or how many shipped pages failed.
- **95080 and larger.** SPI type `Type95080To95320`. `95P08` and `95160` use this same SPI type. A `95160` is 2048 bytes and is scored on the first 512 bytes. A `95080` read of a 95040-sized map is a different part. Do not score it as a `95P08` or a `95160`. When the first 512 pass the shipped list, the status says the chip looks like a 95040. When they do not, and page 0 byte 15 is not `0x5A`, the size is unverified.

## ME7.1.1 95160

The chip is 2048 bytes, a 16-bit address, chip select P6.3. EEPROM init selects that 16-bit address write. Immo Off uses the same map window as a `95P08`. Bytes outside that window stay.

One image's descriptor table has 128 slots. The highest page is 54, which ends at byte 879.

A type 1 read of a 16-bit chip is not a chip image.

## Edits

Immo off and on set `0x012` and `0x022`, then `WriteImmoPageChecksums` rewrites pages 1 and 2. Lockout and `P0602` write pages 30 and 31. Yes on an invalid image calls `CorrectChecksums`. `P63SizeAdvice` still uses the shipped result. A 512-byte P6.3 read that fails every shipped page still suggests a 95P08.
