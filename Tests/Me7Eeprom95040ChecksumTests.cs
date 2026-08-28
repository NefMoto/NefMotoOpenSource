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

using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    public class Me7Eeprom95040ChecksumTests
    {
        private const ushort ChecksumPresentMask = 0x0001;

        [Fact]
        public void Validate_rejects_all_ff_image()
        {
            var data = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = 0xFF;
            }

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.True(result.PagesChecked > 0);
            Assert.NotEqual(0, result.PagesInvalid);
            Assert.False(result.AllChecksumPagesValid);
        }

        [Fact]
        public void CalculatePageChecksum_matches_reference_page_1()
        {
            // Forum sample block 01: first 14 bytes from ME7 EEPROM checksum guide output
            var page = new byte[]
            {
                0x05, 0x01, 0x01, 0x00, 0xCB, 0x28, 0x00, 0x00,
                0x00, 0x00, 0x69, 0xC1, 0x00, 0xA5, 0x36, 0xFD,
            };

            ushort expected = Me7Eeprom95040Checksum.CalculatePageChecksum(page, 0, 1, 0x0017);

            Assert.Equal(0xFD36, expected);
        }

        [Fact]
        public void Validate_exempts_hw_sw_id_pages_28_and_29()
        {
            var data = BuildImageWithValidEnforcedChecksums();

            // Real ME7 chips store VAG HW/SW ASCII here, not the Bosch page checksum.
            data[28 * 16 + 2] = (byte)'8';
            data[28 * 16 + 14] = 0x7F;
            data[28 * 16 + 15] = 0x09;
            data[29 * 16 + 0] = (byte)'0';
            data[29 * 16 + 14] = 0x39;
            data[29 * 16 + 15] = 0x03;

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(2, result.PagesExempt);
            Assert.Equal(2, result.PagesExemptMismatch);
            Assert.Equal(0, result.PagesInvalid);
            Assert.True(result.AllChecksumPagesValid);
            Assert.Contains("HW/SW ID", result.FormatStatusMessage());
            Assert.DoesNotContain("failed", result.FormatStatusMessage());
        }

        [Fact]
        public void Validate_still_fails_when_non_exempt_page_is_corrupt()
        {
            var data = BuildImageWithValidEnforcedChecksums();
            data[1 * 16 + 14] ^= 0xFF;
            data[1 * 16 + 15] ^= 0xFF;

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(1, result.PagesInvalid);
            Assert.False(result.AllChecksumPagesValid);
            Assert.Contains("failed", result.FormatStatusMessage());
        }

        [Fact]
        public void CorrectChecksums_fixes_non_exempt_pages_only()
        {
            var data = BuildImageWithValidEnforcedChecksums();

            // Corrupt an enforced page and leave exempt pages with non-formula trailers.
            data[1 * 16 + 14] ^= 0xFF;
            data[1 * 16 + 15] ^= 0xFF;
            byte p28_14 = data[28 * 16 + 14] = 0x7F;
            byte p28_15 = data[28 * 16 + 15] = 0x09;
            byte p29_14 = data[29 * 16 + 14] = 0x39;
            byte p29_15 = data[29 * 16 + 15] = 0x03;

            Assert.False(Me7Eeprom95040Checksum.Validate(data).AllChecksumPagesValid);

            int updated = Me7Eeprom95040Checksum.CorrectChecksums(data);

            Assert.Equal(1, updated);
            Assert.True(Me7Eeprom95040Checksum.Validate(data).AllChecksumPagesValid);
            Assert.Equal(p28_14, data[28 * 16 + 14]);
            Assert.Equal(p28_15, data[28 * 16 + 15]);
            Assert.Equal(p29_14, data[29 * 16 + 14]);
            Assert.Equal(p29_15, data[29 * 16 + 15]);
        }

        private static byte[] BuildImageWithValidEnforcedChecksums()
        {
            var data = new byte[Me7Eeprom95040Checksum.EepromSize];

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                ushort descriptor = Me7Eeprom95040Checksum.GetPageDescriptor(page);
                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

                if (Me7Eeprom95040Checksum.IsChecksumExemptPage(page))
                {
                    continue;
                }

                int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
                ushort cs = Me7Eeprom95040Checksum.CalculatePageChecksum(
                    data,
                    pageOffset,
                    (ushort)page,
                    descriptor);
                data[pageOffset + 14] = (byte)(cs & 0xFF);
                data[pageOffset + 15] = (byte)(cs >> 8);
            }

            return data;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
