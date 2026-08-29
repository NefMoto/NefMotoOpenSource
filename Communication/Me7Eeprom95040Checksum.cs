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

        /// <summary>
        /// Pages whose firmware descriptor has the CS bit but real ME7 dumps use for
        /// VAG HW/SW ASCII (e.g. 8D0907551M / 0002) and do not store the Bosch page checksum.
        /// Formula math is unchanged; these are excluded from pass/fail policy only.
        /// </summary>
        public static readonly int[] ChecksumExemptPages = { 28, 29 };

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
            /// <summary>CS pages that are enforced (descriptor CS bit, not exempt).</summary>
            public int PagesChecked { get; set; }

            /// <summary>Enforced pages whose stored checksum does not match the formula.</summary>
            public int PagesInvalid { get; set; }

            /// <summary>Exempt CS pages (HW/SW ID); not counted toward pass/fail.</summary>
            public int PagesExempt { get; set; }

            /// <summary>Exempt pages that also do not match the formula (expected on real chips).</summary>
            public int PagesExemptMismatch { get; set; }

            /// <summary>True when every enforced checksum page is valid (and at least one was checked).</summary>
            public bool AllChecksumPagesValid
            {
                get { return PagesInvalid == 0 && PagesChecked > 0; }
            }

            /// <summary>User-facing summary for status log / prompts.</summary>
            public string FormatStatusMessage(bool warnOnly = false)
            {
                string exemptNote = PagesExempt > 0
                    ? $"; pages 28-29 (HW/SW ID) not checksummed"
                    : string.Empty;

                if (AllChecksumPagesValid)
                {
                    return $"ME7 95040 page checksums OK ({PagesChecked} data pages){exemptNote}.";
                }

                string suffix = warnOnly ? "; writing anyway." : ".";
                return $"Warning: ME7 95040 page checksums failed ({PagesInvalid} bad / {PagesChecked} data pages checked){exemptNote}{suffix}";
            }
        }

        public static bool IsChecksumExemptPage(int pageNumber)
        {
            for (int i = 0; i < ChecksumExemptPages.Length; i++)
            {
                if (ChecksumExemptPages[i] == pageNumber)
                {
                    return true;
                }
            }

            return false;
        }

        public static ushort GetPageDescriptor(int pageNumber)
        {
            return PageDescriptors[pageNumber];
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

                ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, descriptor);
                ushort stored = (ushort)(data[pageOffset + 14] | (data[pageOffset + 15] << 8));
                bool match = stored == expected;

                if (IsChecksumExemptPage(page))
                {
                    result.PagesExempt++;
                    if (!match)
                    {
                        result.PagesExemptMismatch++;
                    }
                    continue;
                }

                result.PagesChecked++;
                if (!match)
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

        /// <summary>
        /// Writes Bosch page checksums for all non-exempt CS pages in place.
        /// Pages 28-29 (HW/SW ID) are never modified. Returns how many pages were updated.
        /// </summary>
        public static int CorrectChecksums(byte[] data, int dataOffset = 0)
        {
            int pagesUpdated = 0;

            if (data == null || data.Length < dataOffset + EepromSize)
            {
                return 0;
            }

            for (int page = 0; page < PageCount; page++)
            {
                if (IsChecksumExemptPage(page))
                {
                    continue;
                }

                ushort descriptor = PageDescriptors[page];
                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

                int pageOffset = dataOffset + (page * PageSize);
                ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, descriptor);
                ushort stored = (ushort)(data[pageOffset + 14] | (data[pageOffset + 15] << 8));
                if (stored == expected)
                {
                    continue;
                }

                data[pageOffset + 14] = (byte)(expected & 0xFF);
                data[pageOffset + 15] = (byte)(expected >> 8);
                pagesUpdated++;
            }

            return pagesUpdated;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
