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

namespace Communication
{
    /// <summary>
    /// CRC-32 of a flash range, and the C166 routine that computes it in boot mode.
    /// The polynomial, init, and final XOR match <see cref="ValidateFlashChecksumAction"/>,
    /// which keeps the low 16 bits. This value is the full 32.
    /// </summary>
    public static class BootmodeRangeChecksum
    {
        /// <summary>Internal RAM address. Above every driver loaded at 0xF600, below MiniMon at 0xFA60.</summary>
        public const uint LoadAddress = 0x00F840;

        /// <summary>
        /// C166 routine in Resources/BootmodeRangeChecksum.bin, assembled from
        /// Resources/BootmodeRangeChecksum.asm. R9 is the length in words, so a
        /// 64 KB sector fits. R10 is the offset, R11 the segment. R12 and R13
        /// return the CRC. R15 is 0, or 1 when the word count is 0. R0-R7 are not written.
        /// </summary>
        public static byte[] Image => Properties.Resources.BootmodeRangeChecksum;

        public static uint Compute(byte[] data)
        {
            uint checksum = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((checksum & 1) != 0)
                    {
                        checksum = (checksum >> 1) ^ 0xEDB88320;
                    }
                    else
                    {
                        checksum >>= 1;
                    }
                }
            }

            return checksum ^ 0xFFFFFFFF;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
