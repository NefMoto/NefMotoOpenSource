; Bootmode range CRC-32. Uploaded to 0xF840. Not a flash-driver function.
; Source for Communication/Resources/BootmodeRangeChecksum.bin.
; Assemble with scripts/assemble-bootmode-checksum.py, which passes org 0xF840.
; make does not assemble this. The checked-in .bin is the image the host uploads.
;
; R9 = word count. R10 = offset. R11 = segment.
; R12 = CRC low 16. R13 = CRC high 16.
; R15 = 0, or 1 when the word count is 0.
; R8 and R14 are scratch. R0-R7 are not written.
; XOR is outside c167asm.py's verified set. The script passes allow_unverified.

        CMP R9, #0
        JMPR CC_EQ, bad
        MOV R12, #0xFFFF
        MOV R13, #0xFFFF
word:
        EXTS R11, #1
        MOV R14, [R10]
        MOV R15, R14
        SHR R15, #8
        AND R14, #0xFF
BYTECRC MACRO
        LOCAL bits, bit1, noc0, noc1, next
        XOR R12, R14
        MOV R8, #8
bits:
        SHR R12, #1
        JMPR CC_C, bit1
        SHR R13, #1
        JMPR CC_NC, noc0
        OR R12, #0x8000
noc0:
        JMPR CC_UC, next
bit1:
        SHR R13, #1
        JMPR CC_NC, noc1
        OR R12, #0x8000
noc1:
        XOR R12, #0x8320
        XOR R13, #0xEDB8
next:
        SUB R8, #1
        JMPR CC_NE, bits
        ENDM
        BYTECRC
        MOV R14, R15
        BYTECRC
        ADD R10, #2
        JMPR CC_NC, segok
        ADD R11, #1
segok:
        SUB R9, #1
        JMPR CC_NE, word
        XOR R12, #0xFFFF
        XOR R13, #0xFFFF
        MOV R15, #0
        RETS
bad:
        MOV R12, #0
        MOV R13, #0
        MOV R15, #1
        RETS
