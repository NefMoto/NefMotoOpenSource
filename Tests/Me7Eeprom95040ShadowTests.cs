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
    public class Me7Eeprom95040ShadowTests
    {
        private const uint RamAddress = 0x380000;
        private const int ShadowOffset = 0x3DA8;

        [Fact]
        public void Fill_restores_withheld_pages_3_and_4_from_the_shadow()
        {
            byte[] chip = DistinctiveImage();
            byte[] ram = RamWithShadow(chip, ShadowOffset);
            byte[] image = Withhold(chip, out bool[] have, 3, 4);

            string status = Me7Eeprom95040Shadow.Fill(image, have, ram, RamAddress);

            Assert.Contains("found at 0x383DA8", status);
            Assert.Contains("Filled page(s) 3, 4;", status);
            Assert.True(have[3] && have[4]);
            Assert.Equal(chip, image);
        }

        [Fact]
        public void Fill_rejects_a_slot_that_fails_the_page_checksum()
        {
            byte[] chip = DistinctiveImage();
            byte[] ram = RamWithShadow(chip, ShadowOffset);
            ram[ShadowOffset + (Me7Eeprom95040Shadow.GetRecordId(4) * 16) + 5] ^= 0x01;
            byte[] image = Withhold(chip, out bool[] have, 3, 4);

            string status = Me7Eeprom95040Shadow.Fill(image, have, ram, RamAddress);

            Assert.Contains("Filled page(s) 3;", status);
            Assert.Contains("Page(s) 4 failed the checksum", status);
            Assert.False(have[4]);
            for (int i = 4 * 16; i < 5 * 16; i++)
            {
                Assert.Equal(0xFF, image[i]);
            }
        }

        [Fact]
        public void Fill_prefers_the_record_layout_over_a_page_ordered_copy()
        {
            byte[] chip = DistinctiveImage();
            byte[] ram = RamWithShadow(chip, ShadowOffset);
            byte[] image = Withhold(chip, out bool[] have, 3, 4);
            System.Buffer.BlockCopy(image, 0, ram, 0x100, image.Length);

            Assert.Contains("found at 0x383DA8", Me7Eeprom95040Shadow.Fill(image, have, ram, RamAddress));
            Assert.Equal(chip, image);
        }

        [Fact]
        public void Fill_leaves_the_image_alone_without_a_shadow()
        {
            byte[] chip = DistinctiveImage();
            byte[] image = Withhold(chip, out bool[] have, 3, 4);
            byte[] before = (byte[])image.Clone();

            Assert.Contains("not found", Me7Eeprom95040Shadow.Fill(image, have, new byte[0x8000], RamAddress));
            Assert.Equal(before, image);
        }

        private static byte[] DistinctiveImage()
        {
            var image = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int i = 0; i < image.Length; i++)
            {
                image[i] = (byte)((i * 37) + (i >> 4));
            }

            // Backup pages hold the primary's bytes, as on a real chip.
            foreach (int backup in new[] { 8, 10, 12, 14, 16, 31 })
            {
                System.Buffer.BlockCopy(image, (backup - 1) * 16, image, backup * 16, 14);
            }

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                Me7Eeprom95040Checksum.WritePageChecksum(image, page);
            }

            return image;
        }

        private static byte[] RamWithShadow(byte[] chip, int shadowOffset)
        {
            var ram = new byte[0x8000];
            var random = new System.Random(1);
            random.NextBytes(ram);
            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                byte record = Me7Eeprom95040Shadow.GetRecordId(page);
                if (record == Me7Eeprom95040Shadow.NoRecord || ((page > 0) && (Me7Eeprom95040Shadow.GetRecordId(page - 1) == record)))
                {
                    continue;
                }

                System.Buffer.BlockCopy(chip, page * 16, ram, shadowOffset + (record * 16), 16);
            }

            return ram;
        }

        private static byte[] Withhold(byte[] chip, out bool[] have, params int[] pages)
        {
            byte[] image = (byte[])chip.Clone();
            have = new bool[Me7Eeprom95040Checksum.PageCount];
            for (int page = 0; page < have.Length; page++)
            {
                have[page] = true;
            }

            foreach (int page in pages)
            {
                have[page] = false;
                for (int i = page * 16; i < (page + 1) * 16; i++)
                {
                    image[i] = 0xFF;
                }
            }

            return image;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
