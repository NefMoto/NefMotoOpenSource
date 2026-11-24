#!/usr/bin/env python3
"""Assemble the Keil SSC and XSSC EEPROM drivers.

Keil A166 is not installed here. This covers the instructions in
gremlinDriver.a66 and ST10XDriver.a66, and it is only trusted when the
unmodified source reproduces the checked-in driver image. It is not a
general C166 assembler.

  python3 scripts/assemble-eeprom-driver.py Communication/Resources/gremlinDriver.a66 --check Communication/Resources/gremlindriver.bin
  python3 scripts/assemble-eeprom-driver.py Communication/Resources/ST10XDriver.a66 --check Communication/Resources/ST10XDriver.bin
"""

import argparse
import re
import sys
from pathlib import Path

GPR = {f"R{i}": i for i in range(16)}
GPR_B = {}
for _i in range(8):
    GPR_B[f"RL{_i}"] = 2 * _i
    GPR_B[f"RH{_i}"] = 2 * _i + 1

CC = {
    "CC_UC": 0x0,
    "CC_Z": 0x2,
    "CC_EQ": 0x2,
    "CC_NZ": 0x3,
    "CC_NE": 0x3,
    "CC_UGT": 0xE,
}


class AsmError(Exception):
    pass


def _num(text):
    text = text.strip()
    if text.lower().startswith("0x"):
        return int(text, 16)
    if text.lower().endswith("h"):
        return int(text[:-1], 16)
    return int(text, 10)


def _sym(name, syms):
    key = name.strip().upper()
    if key not in syms:
        raise AsmError(f"unknown symbol {name}")
    return syms[key]


def _bit(text, syms):
    text = text.strip()
    if text.upper() in syms and isinstance(syms[text.upper()], tuple):
        return syms[text.upper()]
    match = re.fullmatch(r"([A-Za-z_]\w*)\.(\d+)", text)
    if not match:
        raise AsmError(f"not a bit address: {text}")
    addr = _sym(match.group(1), syms)
    if isinstance(addr, tuple):
        raise AsmError(f"bit of a bit: {text}")
    return (addr, int(match.group(2)))


def _bitoff(addr):
    if 0xFF00 <= addr <= 0xFFDE and (addr & 1) == 0:
        return 0x80 + ((addr - 0xFF00) // 2)
    raise AsmError(f"no bit offset for 0x{addr:04X}")


def _short_reg(addr):
    if 0xFF00 <= addr <= 0xFFDE and (addr & 1) == 0:
        return 0x80 + ((addr - 0xFF00) // 2)
    if 0xF000 <= addr <= 0xF0FE and (addr & 1) == 0:
        reg = (addr - 0xF000) // 2
        if reg <= 0x7F:
            return reg
    raise AsmError(f"no short register for 0x{addr:04X}")


def _cc(text):
    key = text.strip().upper()
    if not key.startswith("CC_"):
        key = "CC_" + key
    if key not in CC:
        raise AsmError(f"unknown condition {text}")
    return CC[key]


def _reg(text):
    key = text.strip().upper()
    if key not in GPR:
        raise AsmError(f"expected a word register: {text}")
    return GPR[key]


def _breg(text):
    key = text.strip().upper()
    if key not in GPR_B:
        raise AsmError(f"expected a byte register: {text}")
    return GPR_B[key]


def _ind(text):
    match = re.fullmatch(r"\[(R\d+)\]", text.strip(), re.I)
    if not match:
        raise AsmError(f"expected [Rw]: {text}")
    return _reg(match.group(1))


def _imm(text, syms):
    text = text.strip()
    if text.startswith("#"):
        text = text[1:].strip()
    if re.fullmatch(r"[A-Za-z_]\w*", text):
        value = _sym(text, syms)
        if isinstance(value, tuple):
            raise AsmError(f"bit used as a number: {text}")
        return value
    return _num(text)


def _is_reg(text):
    return text.strip().upper() in GPR


def _is_breg(text):
    return text.strip().upper() in GPR_B


def _is_ind(text):
    return re.fullmatch(r"\[R\d+\]", text.strip(), re.I) is not None


def _is_sfr(text, syms):
    if text.startswith("#") or _is_reg(text) or _is_breg(text) or _is_ind(text):
        return False
    if not re.fullmatch(r"[A-Za-z_]\w*", text.strip()):
        return False
    value = _sym(text, syms)
    return isinstance(value, int) and value >= 0xF000


def _rel(target, pc, width, syms):
    dest = syms[target.strip().upper()]
    if isinstance(dest, tuple):
        raise AsmError(f"branch to a bit: {target}")
    rel = (dest - (pc + width)) // 2
    if not -128 <= rel <= 127:
        raise AsmError(f"branch from 0x{pc:04X} to 0x{dest:04X} is out of range")
    return rel & 0xFF


def _size(mnem, args, syms):
    op = mnem.lower()
    if op in ("bset", "bclr", "ret", "rets", "nop", "call", "callr", "push", "pop", "jmpr", "jmp", "cmpd1", "ror"):
        return 2
    if op == "calla":
        return 4
    if op == "extr":
        return 2
    if op in ("bfldh", "bfldl", "exts", "jnb"):
        return 4
    if op == "movb":
        return 2
    if op == "or":
        return 2
    if op in ("add", "sub", "and", "cmp"):
        if len(args) == 2 and args[1].strip().startswith("#"):
            imm = _imm(args[1], syms)
            return 2 if 0 <= imm <= 7 else 4
        return 2
    if op == "mov":
        dst, src = args[0].strip(), args[1].strip()
        if _is_ind(dst) or _is_ind(src):
            return 2
        if _is_reg(dst) and _is_reg(src):
            return 2
        if _is_reg(dst) and src.startswith("#"):
            imm = _imm(src, syms)
            return 2 if 0 <= imm <= 15 else 4
        return 4
    raise AsmError(f"no size for {mnem}")


def _encode(mnem, args, pc, syms):
    op = mnem.lower()
    if op in ("bset", "bclr"):
        addr, bit = _bit(args[0], syms)
        opcode = (bit << 4) | (0x0F if op == "bset" else 0x0E)
        return bytes([opcode, _bitoff(addr)])
    if op == "ret":
        return bytes([0xCB, 0x00])
    if op == "rets":
        return bytes([0xDB, 0x00])
    if op == "nop":
        return bytes([0xCC, 0x00])
    if op in ("call", "callr"):
        return bytes([0xBB, _rel(args[0], pc, 2, syms)])
    if op == "calla":
        dest = syms[args[1].strip().upper()]
        if isinstance(dest, tuple):
            raise AsmError(f"CALLA to a bit: {args[1]}")
        return bytes([0xCA, _cc(args[0]) << 4, dest & 0xFF, (dest >> 8) & 0xFF])
    if op == "push":
        return bytes([0xEC, 0xF0 | _reg(args[0])])
    if op == "pop":
        return bytes([0xFC, 0xF0 | _reg(args[0])])
    if op == "extr":
        count = _imm(args[0], syms)
        if count != 1:
            raise AsmError(f"EXTR #{count} is not encoded")
        return bytes([0xD1, 0x80])
    if op in ("jmp", "jmpr"):
        return bytes([(_cc(args[0]) << 4) | 0x0D, _rel(args[1], pc, 2, syms)])
    if op == "jnb":
        addr, bit = _bit(args[0], syms)
        return bytes([0x9A, _bitoff(addr), _rel(args[1], pc, 4, syms), bit << 4])
    if op == "cmpd1":
        return bytes([0xA0, (_imm(args[1], syms) << 4) | _reg(args[0])])
    if op == "ror":
        return bytes([0x3C, (_imm(args[1], syms) << 4) | _reg(args[0])])
    if op == "or":
        return bytes([0x70, (_reg(args[0]) << 4) | _reg(args[1])])
    if op == "exts":
        seg = _imm(args[0], syms)
        count = _imm(args[1], syms)
        if not 1 <= count <= 4:
            raise AsmError(f"EXTS count {count}")
        return bytes([0xD7, (count - 1) << 4, seg & 0xFF, (seg >> 8) & 0xFF])
    if op in ("bfldh", "bfldl"):
        addr = _sym(args[0], syms)
        mask = _imm(args[1], syms)
        data = _imm(args[2], syms)
        if op == "bfldh":
            # Second word is mask:data, stored little-endian.
            return bytes([0x1A, _bitoff(addr), data & 0xFF, mask & 0xFF])
        return bytes([0x0A, _bitoff(addr), mask & 0xFF, data & 0xFF])
    if op == "movb":
        dst, src = args[0].strip(), args[1].strip()
        if _is_breg(dst) and _is_ind(src):
            return bytes([0xA9, (_breg(dst) << 4) | _ind(src)])
        if _is_ind(dst) and _is_breg(src):
            return bytes([0xB9, (_breg(src) << 4) | _ind(dst)])
        raise AsmError(f"unsupported MOVB {dst}, {src}")
    if op in ("add", "sub", "and", "cmp"):
        dst, src = args[0].strip(), args[1].strip()
        base = {"add": 0x00, "sub": 0x20, "cmp": 0x40, "and": 0x60}[op]
        if _is_reg(dst) and src.startswith("#"):
            imm = _imm(src, syms)
            if 0 <= imm <= 7:
                return bytes([base | 0x08, (_reg(dst) << 4) | imm])
            return bytes([base | 0x06, 0xF0 | _reg(dst), imm & 0xFF, (imm >> 8) & 0xFF])
        if _is_reg(dst) and _is_reg(src):
            return bytes([base, (_reg(dst) << 4) | _reg(src)])
        raise AsmError(f"unsupported {mnem} {dst}, {src}")
    if op == "mov":
        dst, src = args[0].strip(), args[1].strip()
        if _is_reg(dst) and src.startswith("#"):
            imm = _imm(src, syms)
            if 0 <= imm <= 15:
                return bytes([0xE0, (imm << 4) | _reg(dst)])
            return bytes([0xE6, 0xF0 | _reg(dst), imm & 0xFF, (imm >> 8) & 0xFF])
        if _is_reg(dst) and _is_reg(src):
            return bytes([0xF0, (_reg(dst) << 4) | _reg(src)])
        if _is_ind(dst) and _is_reg(src):
            return bytes([0xB8, (_reg(src) << 4) | _ind(dst)])
        if _is_reg(dst) and _is_ind(src):
            return bytes([0xA8, (_reg(dst) << 4) | _ind(src)])
        if _is_sfr(dst, syms) and src.startswith("#"):
            addr = _sym(dst, syms)
            imm = _imm(src, syms)
            return bytes([0xE6, _short_reg(addr), imm & 0xFF, (imm >> 8) & 0xFF])
        if _is_sfr(dst, syms) and _is_reg(src):
            addr = _sym(dst, syms)
            return bytes([0xF6, 0xF0 | _reg(src), addr & 0xFF, (addr >> 8) & 0xFF])
        if _is_reg(dst) and _is_sfr(src, syms):
            addr = _sym(src, syms)
            return bytes([0xF2, 0xF0 | _reg(dst), addr & 0xFF, (addr >> 8) & 0xFF])
        raise AsmError(f"unsupported MOV {dst}, {src}")
    raise AsmError(f"instruction not implemented: {mnem}")


def _parse(text):
    items = []
    for lineno, raw in enumerate(text.splitlines(), 1):
        line = raw.split(";", 1)[0].strip()
        if not line or line.startswith("$"):
            continue
        label = None
        match = re.match(r"^(\w+)\s*:\s*(.*)$", line)
        if match:
            label = match.group(1)
            line = match.group(2).strip()
        if label and not line:
            items.append((lineno, "label", label))
            continue
        if not line:
            continue
        if re.match(r"^assume\b", line, re.I):
            continue
        if re.fullmatch(r"end", line, re.I):
            break
        match = re.match(r"^(\w+)\s+section\s+code\s+at\s+(\S+)\s*$", line, re.I)
        if match:
            items.append((lineno, "section", match.group(2)))
            continue
        match = re.match(r"^(\w+)\s+(?:equ|defr)\s+(\S+)\s*$", line, re.I)
        if match:
            items.append((lineno, "equ", (match.group(1), match.group(2))))
            continue
        match = re.match(r"^(\w+)\s+proc\s+(?:near|far)\s*$", line, re.I)
        if match:
            items.append((lineno, "label", match.group(1)))
            continue
        if re.match(r"^(?:\w+\s+)?(?:endp|ends)\b", line, re.I):
            continue
        if label:
            items.append((lineno, "label", label))
        parts = line.split(None, 1)
        args = [part.strip() for part in parts[1].split(",")] if len(parts) > 1 else []
        items.append((lineno, "insn", (parts[0], args)))
    return items


def assemble(text):
    items = _parse(text)
    syms = {}
    pc = None
    base = None
    sized = []
    for lineno, kind, payload in items:
        try:
            if kind == "equ":
                name, expr = payload
                if re.fullmatch(r"[A-Za-z_]\w*\.\d+", expr):
                    syms[name.upper()] = _bit(expr, syms)
                else:
                    value = _imm(expr, syms) if re.fullmatch(r"[A-Za-z_]\w*", expr) else _num(expr)
                    syms[name.upper()] = value
            elif kind == "section":
                addr = _num(payload)
                if base is None:
                    base = addr
                elif addr < pc:
                    raise AsmError(f"section moves backward to 0x{addr:04X}")
                pc = addr
                sized.append((pc, "section", addr))
            elif kind == "label":
                if pc is None:
                    raise AsmError(f"label {payload} before a section")
                syms[payload.upper()] = pc
            elif kind == "insn":
                mnem, args = payload
                pc += _size(mnem, args, syms)
                sized.append((pc - _size(mnem, args, syms), "insn", payload))
        except AsmError as exc:
            raise AsmError(f"line {lineno}: {exc}") from exc

    out = bytearray()
    cursor = base
    for addr, kind, payload in sized:
        if kind == "section":
            if payload < cursor:
                raise AsmError(f"overlap at 0x{payload:04X}")
            out += b"\xFF" * (payload - cursor)
            cursor = payload
            continue
        mnem, args = payload
        try:
            encoded = _encode(mnem, args, addr, syms)
        except AsmError as exc:
            raise AsmError(f"at 0x{addr:04X}: {exc}") from exc
        if addr != cursor:
            raise AsmError(f"gap at 0x{addr:04X}")
        out += encoded
        cursor += len(encoded)
    return bytes(out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("--check", help="compare against this image and do not write")
    parser.add_argument("--output", help="write the image here")
    args = parser.parse_args()
    source = Path(args.source)
    image = assemble(source.read_text(encoding="utf-8", errors="replace"))
    if args.check:
        expected = Path(args.check).read_bytes()
        if image != expected:
            limit = min(len(image), len(expected))
            at = next((i for i in range(limit) if image[i] != expected[i]), limit)
            raise SystemExit(
                f"mismatch at 0x{at:04X}: got {image[at:at+8].hex()} "
                f"expected {expected[at:at+8].hex()} "
                f"(got {len(image)} bytes, expected {len(expected)})"
            )
        print(f"match {source.name}: {len(image)} bytes")
        return
    dest = Path(args.output) if args.output else source.with_suffix(".bin")
    dest.write_bytes(image)
    print(f"wrote {dest} ({len(image)} bytes)")


if __name__ == "__main__":
    try:
        main()
    except AsmError as exc:
        raise SystemExit(f"assemble failed: {exc}")
