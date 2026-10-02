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
    public class Me7Eeprom95040Page3031Tests
    {
        [Fact]
        public void TryClearP0602_clears_bit_7_and_those_checksums_only()
        {
            byte[] image = ValidImage();
            image[0x1E8] = 0xC3;
            image[0x1F8] = 0x80;
            image[0x1E9] = 0x42;
            image[0x1EC] = 0x11;
            Me7Eeprom95040Checksum.WritePageChecksum(image, 30);
            Me7Eeprom95040Checksum.WritePageChecksum(image, 31);
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Page3031.Result.Applied, Me7Eeprom95040Page3031.TryClearP0602(image));
            Assert.Equal(0x43, image[0x1E8]);
            Assert.Equal(0x00, image[0x1F8]);
            Assert.Equal(0x42, image[0x1E9]);
            Assert.Equal(0x11, image[0x1EC]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[30]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[31]);
            AssertBytesExcept(before, image, 0x1E8, 0x1F8, 0x1EE, 0x1EF, 0x1FE, 0x1FF);
        }

        [Fact]
        public void TryClearP0602_leaves_a_clear_flag_unchanged()
        {
            byte[] image = ValidImage();
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Page3031.Result.AlreadyDone, Me7Eeprom95040Page3031.TryClearP0602(image));
            AssertBytesExcept(before, image);
        }

        [Fact]
        public void TrySetP0602_sets_bit_7_and_keeps_the_other_bits()
        {
            byte[] image = ValidImage();
            image[0x1E8] = 0x43;
            image[0x1F8] = 0x01;
            image[0x1E9] = 0x42;
            image[0x1EC] = 0x11;
            Me7Eeprom95040Checksum.WritePageChecksum(image, 30);
            Me7Eeprom95040Checksum.WritePageChecksum(image, 31);
            byte[] before = (byte[])image.Clone();

            Assert.True(Me7Eeprom95040Page3031.IsClear(image));
            Assert.Equal(Me7Eeprom95040Page3031.Result.Applied, Me7Eeprom95040Page3031.TrySetP0602(image));
            Assert.Equal(0xC3, image[0x1E8]);
            Assert.Equal(0x81, image[0x1F8]);
            Assert.Equal(0x42, image[0x1E9]);
            Assert.Equal(0x11, image[0x1EC]);
            Assert.False(Me7Eeprom95040Page3031.IsClear(image));
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[30]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[31]);
            AssertBytesExcept(before, image, 0x1E8, 0x1F8, 0x1EE, 0x1EF, 0x1FE, 0x1FF);
        }

        [Fact]
        public void TrySetP0602_leaves_a_set_flag_unchanged()
        {
            byte[] image = ValidImage();
            image[0x1E8] = 0x80;
            image[0x1F8] = 0x80;
            Me7Eeprom95040Checksum.WritePageChecksum(image, 30);
            Me7Eeprom95040Checksum.WritePageChecksum(image, 31);
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Page3031.Result.AlreadyDone, Me7Eeprom95040Page3031.TrySetP0602(image));
            AssertBytesExcept(before, image);
        }

        [Fact]
        public void TryResetLockout_zeros_bytes_12_and_13_only()
        {
            byte[] image = ValidImage();
            image[0x1E8] = 0x80;
            image[0x1EC] = 0x42;
            image[0x1ED] = 0x22;
            image[0x1FC] = 0x01;
            image[0x1FD] = 0x02;
            Me7Eeprom95040Checksum.WritePageChecksum(image, 30);
            Me7Eeprom95040Checksum.WritePageChecksum(image, 31);
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Page3031.Result.Applied, Me7Eeprom95040Page3031.TryResetLockout(image));
            Assert.Equal(0x80, image[0x1E8]);
            Assert.Equal(0x00, image[0x1EC]);
            Assert.Equal(0x00, image[0x1ED]);
            Assert.Equal(0x00, image[0x1FC]);
            Assert.Equal(0x00, image[0x1FD]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[30]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image).PageChecksumKinds[31]);
            AssertBytesExcept(before, image, 0x1EC, 0x1ED, 0x1FC, 0x1FD, 0x1EE, 0x1EF, 0x1FE, 0x1FF);
        }

        [Fact]
        public void TryResetLockout_leaves_zeros_unchanged()
        {
            byte[] image = ValidImage();
            byte[] before = (byte[])image.Clone();

            Assert.Equal(Me7Eeprom95040Page3031.Result.AlreadyDone, Me7Eeprom95040Page3031.TryResetLockout(image));
            AssertBytesExcept(before, image);
        }

        private static void AssertBytesExcept(byte[] before, byte[] after, params int[] ignored)
        {
            for (int i = 0; i < before.Length; i++)
            {
                bool skip = false;
                for (int j = 0; j < ignored.Length; j++)
                {
                    if (ignored[j] == i)
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                {
                    Assert.Equal(before[i], after[i]);
                }
            }
        }

        private static byte[] ValidImage()
        {
            var image = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                Me7Eeprom95040Checksum.WritePageChecksum(image, page);
            }

            return image;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
