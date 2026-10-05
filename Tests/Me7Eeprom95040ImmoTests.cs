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
    public class Me7Eeprom95040ImmoTests
    {
        // bins/eeprom/DQ-read-OEM.bin pages 1 and 2.
        private static readonly byte[] DqPage1 =
        {
            0x05, 0x01, 0x01, 0x00, 0xEC, 0x94, 0x00, 0x00,
            0x00, 0x00, 0x69, 0xC1, 0x00, 0xA5, 0xA9, 0xFC,
        };

        private static readonly byte[] DqPage2 =
        {
            0x05, 0x01, 0x01, 0x00, 0xEC, 0x94, 0x00, 0x00,
            0x00, 0x00, 0x69, 0xC1, 0x00, 0xA5, 0xA8, 0xFC,
        };

        [Fact]
        public void TryDisable_sets_both_status_bytes_and_those_checksums_only()
        {
            byte[] image = ImageWithDqImmoPages();
            byte[] before = (byte[])image.Clone();

            Me7Eeprom95040Immo.Result result = Me7Eeprom95040Immo.TryDisable(image);

            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, result);
            Assert.Equal(0x02, image[0x012]);
            Assert.Equal(0x02, image[0x022]);
            Assert.Equal(0x01, image[0x011]);
            Assert.Equal(0x01, image[0x021]);
            Assert.Equal(0xA8, image[0x01E]);
            Assert.Equal(0xFC, image[0x01F]);
            Assert.Equal(0xA7, image[0x02E]);
            Assert.Equal(0xFC, image[0x02F]);

            for (int i = 0; i < image.Length; i++)
            {
                if (i == 0x012 || i == 0x022 || i == 0x01E || i == 0x01F || i == 0x02E || i == 0x02F)
                {
                    continue;
                }

                Assert.Equal(before[i], image[i]);
            }

            var validation = Me7Eeprom95040Checksum.Validate(image);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[1]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[2]);
            Assert.Equal("off", Me7Eeprom95040Fields.FormatImmoValue(image));
        }

        [Fact]
        public void TryDisable_sets_a_matching_04_pair_to_02()
        {
            byte[] image = ImageWithDqImmoPages();
            image[0x012] = 0x04;
            image[0x022] = 0x04;

            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95040Immo.TryDisable(image));
            Assert.Equal(0x02, image[0x012]);
            Assert.Equal(0x02, image[0x022]);
            Assert.Equal(0x01, image[0x011]);
            Assert.Equal(0x01, image[0x021]);

            var validation = Me7Eeprom95040Checksum.Validate(image);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[1]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[2]);
        }

        [Fact]
        public void TryDisable_leaves_an_already_off_image_unchanged()
        {
            byte[] image = ImageWithDqImmoPages();
            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95040Immo.TryDisable(image));
            byte[] off = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Immo.Result.AlreadyOff, Me7Eeprom95040Immo.TryDisable(image));
            Assert.Equal(off, image);
        }

        [Fact]
        public void TryDisable_refuses_a_mismatched_pair()
        {
            byte[] image = ImageWithDqImmoPages();
            image[0x022] = 0x02;
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95040Immo.TryDisable(image));
            Assert.Equal(before, image);
        }

        [Fact]
        public void TryDisable_refuses_a_short_buffer()
        {
            var image = new byte[16];
            image[0] = 0x01;

            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95040Immo.TryDisable(image));
        }

        [Fact]
        public void TryEnable_sets_an_off_pair_back_to_01()
        {
            byte[] image = ImageWithDqImmoPages();
            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95040Immo.TryDisable(image));
            byte[] off = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95040Immo.TryEnable(image));
            Assert.Equal(0x01, image[0x012]);
            Assert.Equal(0x01, image[0x022]);
            Assert.Equal(0x01, image[0x011]);
            Assert.Equal(0x01, image[0x021]);
            Assert.Equal("on", Me7Eeprom95040Fields.FormatImmoValue(image));

            var validation = Me7Eeprom95040Checksum.Validate(image);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[1]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, validation.PageChecksumKinds[2]);

            for (int i = 0; i < image.Length; i++)
            {
                if (i == 0x012 || i == 0x022 || i == 0x01E || i == 0x01F || i == 0x02E || i == 0x02F)
                {
                    continue;
                }

                Assert.Equal(off[i], image[i]);
            }
        }

        [Fact]
        public void TryEnable_leaves_an_on_pair_unchanged()
        {
            byte[] image = ImageWithDqImmoPages();
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Immo.Result.AlreadyOn, Me7Eeprom95040Immo.TryEnable(image));
            Assert.Equal(before, image);
        }

        [Fact]
        public void TryEnable_refuses_a_matching_04_pair()
        {
            byte[] image = ImageWithDqImmoPages();
            image[0x012] = 0x04;
            image[0x022] = 0x04;
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95040Immo.TryEnable(image));
            Assert.Equal(before, image);
        }

        private static byte[] ImageWithDqImmoPages()
        {
            var image = new byte[Me7Eeprom95040Checksum.EepromSize];
            image[0x30] = 0x11;
            image[0x1E8] = 0x80;
            image[0x1EC] = 0x42;
            image[0x1ED] = 0x22;

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                ushort descriptor = Me7Eeprom95040Checksum.GetPageDescriptor(page);
                if ((descriptor & 0x0001) == 0 || Me7Eeprom95040Checksum.IsChecksumExemptPage(page))
                {
                    continue;
                }

                int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
                ushort cs = Me7Eeprom95040Checksum.CalculatePageChecksum(image, pageOffset, (ushort)page, descriptor);
                image[pageOffset + 14] = (byte)(cs & 0xFF);
                image[pageOffset + 15] = (byte)(cs >> 8);
            }

            DqPage1.CopyTo(image, 0x010);
            DqPage2.CopyTo(image, 0x020);
            return image;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
