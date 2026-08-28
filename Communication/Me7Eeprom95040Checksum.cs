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
    /// ME7 95040 EEPROM page checksum validation (512 B, 32 × 16 B pages).
    /// Algorithm from Bosch ME7 immo EEPROM layout; reference implementations:
    /// 360trev/ME7_95040sum (EEPROMsum.c), NefMoto forum ME7 EEPROM checksum guide.
    /// </summary>
    public static class Me7Eeprom95040Checksum
    {
        public const int EepromSize = 512;
        public const int PageSize = 16;
        public const int PageCount = 32;

        private const ushort ChecksumPresentMask = 0x0001;
        private const ushort ChecksumBitMask = 0x0040;

        /// <summary>Per-page descriptor words from ME7 firmware EEPROM page table.</summary>
        private static readonly ushort[] PageDescriptors =
        {
            0xFF18, 0x0017, 0x0117, 0x0207, 0x0307, 0x0437, 0x0533, 0x06B7,
            0x06F7, 0x07B3, 0x07F3, 0x08B7, 0x08F7, 0x09B3, 0x09F3, 0x0AB3,
            0x0AF3, 0x0B32, 0x0B10, 0x0B10, 0x0B10, 0x0C37, 0x0D33, 0x0E33,
            0x0F33, 0x1033, 0x1133, 0x1233, 0x1235, 0x1235, 0x13B7, 0x13F7,
        };

        public sealed class ValidationResult
        {
            public int PagesWithChecksum { get; set; }
            public int PagesInvalid { get; set; }

            public bool AllChecksumPagesValid
            {
                get { return PagesInvalid == 0 && PagesWithChecksum > 0; }
            }
        }

        public static ValidationResult Validate(byte[] data, int dataOffset = 0)
        {
            var result = new ValidationResult();

            if (data == null || data.Length < dataOffset + EepromSize)
            {
                return result;
            }

            for (int page = 0; page < PageCount; page++)
            {
                int pageOffset = dataOffset + (page * PageSize);
                ushort descriptor = PageDescriptors[page];

                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

                result.PagesWithChecksum++;

                ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, descriptor);
                ushort stored = (ushort)(data[pageOffset + 14] | (data[pageOffset + 15] << 8));

                if (stored != expected)
                {
                    result.PagesInvalid++;
                }
            }

            return result;
        }

        public static ushort CalculatePageChecksum(byte[] data, int pageOffset, ushort pageNumber, ushort descriptor)
        {
            ushort sum = 0;
            for (int i = 0; i <= 13; i++)
            {
                sum += (ushort)data[pageOffset + i];
            }

            sum += pageNumber;

            if ((descriptor & ChecksumBitMask) != 0)
            {
                sum -= 1;
            }

            return unchecked((ushort)(-sum));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
