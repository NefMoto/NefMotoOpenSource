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
        [Fact]
        public void Validate_rejects_all_ff_image()
        {
            var data = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = 0xFF;
            }

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.True(result.PagesWithChecksum > 0);
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
    }
}

// vi: set sw=4 ts=8 expandtab:
