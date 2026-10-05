/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: nyet@nyet.org
*/

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Communication
{
    /// <summary>
    /// ME7 95040 image as a packed struct over the 512-byte buffer.
    /// <see cref="Read"/> and <see cref="Write"/> copy that struct in and out.
    /// </summary>
    public static class Me7Eeprom95040Fields
    {
        public const byte ImmoOn = 0x01;
        public const byte ImmoOff = 0x02;

        [InlineArray(11)]
        public struct EcuPartNumberBytes
        {
            private byte _element0;
        }

        [InlineArray(6)]
        public struct ToolIdBytes
        {
            private byte _element0;
        }

        [InlineArray(5)]
        public struct VinPrefixBytes
        {
            private byte _element0;
        }

        [InlineArray(12)]
        public struct VinSuffixBytes
        {
            private byte _element0;
        }

        [InlineArray(13)]
        public struct ImmoIdTailBytes
        {
            private byte _element0;
        }

        public static Image Read(byte[] image)
        {
            return MemoryMarshal.Read<Image>(image.AsSpan(0, Me7Eeprom95040Checksum.EepromSize));
        }

        public static void Write(byte[] image, in Image value)
        {
            MemoryMarshal.Write(image.AsSpan(0, Me7Eeprom95040Checksum.EepromSize), in value);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = Me7Eeprom95040Checksum.EepromSize)]
        public struct Image
        {
            [FieldOffset(0x012)] public byte Immo1;
            [FieldOffset(0x022)] public byte Immo2;
            [FieldOffset(0x014)] public ushort Skc1;
            [FieldOffset(0x024)] public ushort Skc2;
            [FieldOffset(0x1C2)] public EcuPartNumberBytes EcuPartNumber;
            [FieldOffset(0x1E2)] public ToolIdBytes ToolId1;
            [FieldOffset(0x1F2)] public ToolIdBytes ToolId2;
            [FieldOffset(0x0B5)] public VinPrefixBytes VinPrefix1;
            [FieldOffset(0x0C5)] public VinPrefixBytes VinPrefix2;
            [FieldOffset(0x0D0)] public VinSuffixBytes VinSuffix1;
            [FieldOffset(0x0E0)] public VinSuffixBytes VinSuffix2;
            [FieldOffset(0x0DC)] public byte ImmoIdHead1;
            [FieldOffset(0x0EC)] public byte ImmoIdHead2;
            [FieldOffset(0x0F0)] public ImmoIdTailBytes ImmoIdTail1;
            [FieldOffset(0x100)] public ImmoIdTailBytes ImmoIdTail2;
            [FieldOffset(0x1E8)] public byte P0602Page30;
            [FieldOffset(0x1F8)] public byte P0602Page31;
            [FieldOffset(0x1EC)] public ushort LockoutPage30;
            [FieldOffset(0x1FC)] public ushort LockoutPage31;
        }

        public enum Role
        {
            None,
            ImmoStatus,
            Skc,
            P0602Flag,
            Lockout
        }

        public static int PageOf(string field)
        {
            return OffsetOf(field) / Me7Eeprom95040Checksum.PageSize;
        }

        public static Role RoleAt(int offset)
        {
            if (IsField(offset, nameof(Image.Immo1)) || IsField(offset, nameof(Image.Immo2)))
            {
                return Role.ImmoStatus;
            }

            if (Covers<ushort>(offset, nameof(Image.Skc1)) || Covers<ushort>(offset, nameof(Image.Skc2)))
            {
                return Role.Skc;
            }

            if (IsField(offset, nameof(Image.P0602Page30)) || IsField(offset, nameof(Image.P0602Page31)))
            {
                return Role.P0602Flag;
            }

            if (Covers<ushort>(offset, nameof(Image.LockoutPage30)) || Covers<ushort>(offset, nameof(Image.LockoutPage31)))
            {
                return Role.Lockout;
            }

            return Role.None;
        }

        public static bool IsEcuPartNumber(int offset)
        {
            return Covers<EcuPartNumberBytes>(offset, nameof(Image.EcuPartNumber));
        }

        public static bool IsToolId(int offset)
        {
            return Covers<ToolIdBytes>(offset, nameof(Image.ToolId1)) || Covers<ToolIdBytes>(offset, nameof(Image.ToolId2));
        }

        public static bool IsVin(int offset)
        {
            return Covers<VinPrefixBytes>(offset, nameof(Image.VinPrefix1))
                || Covers<VinPrefixBytes>(offset, nameof(Image.VinPrefix2))
                || Covers<VinSuffixBytes>(offset, nameof(Image.VinSuffix1))
                || Covers<VinSuffixBytes>(offset, nameof(Image.VinSuffix2));
        }

        public static bool IsImmoId(int offset)
        {
            return IsField(offset, nameof(Image.ImmoIdHead1))
                || IsField(offset, nameof(Image.ImmoIdHead2))
                || Covers<ImmoIdTailBytes>(offset, nameof(Image.ImmoIdTail1))
                || Covers<ImmoIdTailBytes>(offset, nameof(Image.ImmoIdTail2));
        }

        public static string FormatImmoValue(byte[] image)
        {
            return FormatImmoValue(Read(image));
        }

        public static string FormatImmoValue(Image ee)
        {
            if (ee.Immo1 == ImmoOn && ee.Immo2 == ImmoOn)
            {
                return "on";
            }

            if (ee.Immo1 == ImmoOff && ee.Immo2 == ImmoOff)
            {
                return "off";
            }

            return ee.Immo1.ToString("X2") + "/" + ee.Immo2.ToString("X2");
        }

        public static string FormatSkcValue(byte[] image)
        {
            return FormatSkcValue(Read(image));
        }

        public static string FormatSkcValue(Image ee)
        {
            return Pair(ee.Skc1.ToString("X4"), ee.Skc2.ToString("X4"));
        }

        public static string FormatP0602Value(byte[] image)
        {
            return FormatP0602Value(Read(image));
        }

        public static string FormatP0602Value(Image ee)
        {
            return Pair(Bit7(ee.P0602Page30), Bit7(ee.P0602Page31));
        }

        public static string FormatLockoutValue(byte[] image)
        {
            return FormatLockoutValue(Read(image));
        }

        public static string FormatLockoutValue(Image ee)
        {
            return Pair(LeBytes(ee.LockoutPage30), LeBytes(ee.LockoutPage31));
        }

        private static int OffsetOf(string field)
        {
            return (int)Marshal.OffsetOf<Image>(field);
        }

        private static bool IsField(int offset, string field)
        {
            return offset == OffsetOf(field);
        }

        private static bool Covers<T>(int offset, string field) where T : struct
        {
            int start = OffsetOf(field);
            return offset >= start && offset < start + Unsafe.SizeOf<T>();
        }

        private static string Pair(string primary, string mirror)
        {
            return primary == mirror ? primary : primary + " / " + mirror;
        }

        private static string LeBytes(ushort value)
        {
            return (value & 0xFF).ToString("X2") + " " + (value >> 8).ToString("X2");
        }

        private static string Bit7(byte value)
        {
            return (value & 0x80) != 0 ? "set" : "clear";
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
