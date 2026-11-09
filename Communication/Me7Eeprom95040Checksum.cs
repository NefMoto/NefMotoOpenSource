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

        public enum PageChecksumKind
        {
            None,
            Ok,
            Bad,
            Exempt
        }

        private const ushort ChecksumPresentMask = 0x0001;
        private const ushort ChecksumBitMask = 0x0040;
        private const int ImageTablePrefixPages = 20;
        private const int Page20 = 20;

        /// <summary>
        /// Flags for a page-20 check when no flash table was found. Bit 6 is clear, so the
        /// formula does not subtract one. A stored word that matches counts as ok.
        /// </summary>
        private const ushort Page20MatchFlags = 0x0037;

        /// <summary>Page 0 byte 15 on the 95040 images in issue #138.</summary>
        public const byte Page0Signature = 0x5A;

        /// <summary>
        /// Pages 28 and 29 are scored by <see cref="Validate"/> when their descriptor has the
        /// checksum bit. <see cref="CorrectChecksums"/> never rewrites them, and the KWP shadow
        /// fill never copies them.
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
            /// <summary>Pages whose descriptor has the checksum bit.</summary>
            public int PagesChecked { get; set; }

            /// <summary>Checked pages whose stored checksum does not match the formula.</summary>
            public int PagesInvalid { get; set; }

            /// <summary>True when every checked page is valid (and at least one was checked).</summary>
            public bool AllChecksumPagesValid
            {
                get { return PagesInvalid == 0 && PagesChecked > 0; }
            }

            /// <summary>One entry per page. Unchecked pages stay <see cref="PageChecksumKind.None"/>.</summary>
            public PageChecksumKind[] PageChecksumKinds { get; set; }

            /// <summary>User-facing summary for status log / prompts.</summary>
            public string FormatStatusMessage(bool warnOnly = false)
            {
                if (AllChecksumPagesValid)
                {
                    return $"ME7 95040 page checksums OK ({PagesChecked} data pages).";
                }

                string suffix = warnOnly ? "; writing anyway." : ".";
                return $"Warning: ME7 95040 page checksums failed ({PagesInvalid} bad / {PagesChecked} data pages checked){suffix}";
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

        /// <summary>
        /// Descriptor used to score <paramref name="page"/>. An image table replaces the shipped
        /// list. With no table, page 20 uses <see cref="Page20MatchFlags"/> only when the stored
        /// word already matches that formula; a mismatch stays unchecked.
        /// </summary>
        public static ushort ActiveDescriptor(int page, byte[] data, int pageOffset, ushort[] imageTable)
        {
            if (imageTable != null && imageTable.Length == PageCount)
            {
                return imageTable[page];
            }

            if (page == Page20
                && data != null
                && pageOffset >= 0
                && data.Length >= pageOffset + PageSize)
            {
                ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, Page20MatchFlags);
                ushort stored = (ushort)(data[pageOffset + 14] | (data[pageOffset + 15] << 8));
                if (stored == expected)
                {
                    return Page20MatchFlags;
                }

                return 0;
            }

            return PageDescriptors[page];
        }

        /// <summary>
        /// First 20 shipped descriptors, as little-endian words, locate the table. The 32 words
        /// at the first even hit are this image's table. Returns null when the prefix is absent
        /// or the 32 words do not fit.
        /// </summary>
        public static ushort[] TryCopyImageTable(byte[] flash)
        {
            int tableBytes = PageCount * 2;
            if (flash == null || flash.Length < tableBytes)
            {
                return null;
            }

            for (int at = 0; at + tableBytes <= flash.Length; at += 2)
            {
                if (!PrefixMatches(flash, at))
                {
                    continue;
                }

                var table = new ushort[PageCount];
                for (int page = 0; page < PageCount; page++)
                {
                    int offset = at + (page * 2);
                    table[page] = (ushort)(flash[offset] | (flash[offset + 1] << 8));
                }

                return table;
            }

            return null;
        }

        /// <summary>
        /// Status for a bootmode read whose selected size is over 512 bytes, using the shipped
        /// list on the first 512. Null means the plain OK line still applies.
        /// </summary>
        public static string OversizedReadStatus(byte[] data, string deviceName)
        {
            if (data == null || deviceName == null)
            {
                return null;
            }

            if (Validate(data).AllChecksumPagesValid)
            {
                return "EEPROM looks like a 95040; selected device is " + deviceName + ".";
            }

            if (data.Length > 15 && data[15] != Page0Signature)
            {
                return "EEPROM size is unverified; selected device is " + deviceName + ".";
            }

            return null;
        }

        public static ValidationResult Validate(byte[] data, int dataOffset = 0, ushort[] imageTable = null)
        {
            var result = new ValidationResult();
            result.PageChecksumKinds = new PageChecksumKind[PageCount];

            if (data == null || data.Length < dataOffset + EepromSize)
            {
                return result;
            }

            for (int page = 0; page < PageCount; page++)
            {
                int pageOffset = dataOffset + (page * PageSize);
                ushort descriptor = ActiveDescriptor(page, data, pageOffset, imageTable);
                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

                ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, descriptor);
                ushort stored = (ushort)(data[pageOffset + 14] | (data[pageOffset + 15] << 8));
                result.PagesChecked++;
                if (stored != expected)
                {
                    result.PagesInvalid++;
                    result.PageChecksumKinds[page] = PageChecksumKind.Bad;
                }
                else
                {
                    result.PageChecksumKinds[page] = PageChecksumKind.Ok;
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
        /// Writes the Bosch checksum for one page. Pages 28-29 and pages with no checksum bit are left unchanged.
        /// </summary>
        public static void WritePageChecksum(byte[] data, int page, int dataOffset = 0)
        {
            if (data == null || page < 0 || page >= PageCount || IsChecksumExemptPage(page))
            {
                return;
            }

            if (data.Length < dataOffset + EepromSize)
            {
                return;
            }

            ushort descriptor = PageDescriptors[page];
            if ((descriptor & ChecksumPresentMask) == 0)
            {
                return;
            }

            int pageOffset = dataOffset + (page * PageSize);
            ushort expected = CalculatePageChecksum(data, pageOffset, (ushort)page, descriptor);
            data[pageOffset + 14] = (byte)(expected & 0xFF);
            data[pageOffset + 15] = (byte)(expected >> 8);
        }

        /// <summary>
        /// Writes Bosch page checksums for checksummed pages in place.
        /// Pages 28-29 are never modified. With no image table, page 20 is not written.
        /// Returns how many pages were updated.
        /// </summary>
        public static int CorrectChecksums(byte[] data, int dataOffset = 0, ushort[] imageTable = null)
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

                int pageOffset = dataOffset + (page * PageSize);
                ushort descriptor = ActiveDescriptor(page, data, pageOffset, imageTable);
                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

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

        private static bool PrefixMatches(byte[] flash, int at)
        {
            for (int page = 0; page < ImageTablePrefixPages; page++)
            {
                int offset = at + (page * 2);
                ushort word = (ushort)(flash[offset] | (flash[offset + 1] << 8));
                if (word != PageDescriptors[page])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
