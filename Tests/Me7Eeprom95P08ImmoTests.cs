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
    public class Me7Eeprom95P08ImmoTests
    {
        private static byte[] Image(byte first, byte second)
        {
            byte[] image = new byte[Me7Eeprom95P08Immo.EepromSize];
            image[Me7Eeprom95P08Immo.ImmoOffset1] = first;
            image[Me7Eeprom95P08Immo.ImmoOffset2] = second;
            image[0x012] = 0x11;
            image[0x022] = 0x11;
            return image;
        }

        [Fact]
        public void TryDisable_sets_both_status_bytes_and_nothing_else()
        {
            byte[] image = Image(0x01, 0x01);
            byte[] before = (byte[])image.Clone();

            Me7Eeprom95040Immo.Result result = Me7Eeprom95P08Immo.TryDisable(image);

            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, result);
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOff, image[0x032]);
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOff, image[0x042]);
            for (int i = 0; i < image.Length; i++)
            {
                if (i == 0x032 || i == 0x042)
                {
                    continue;
                }

                Assert.Equal(before[i], image[i]);
            }
        }

        [Fact]
        public void TryEnable_restores_a_matching_off_pair()
        {
            byte[] image = Image(0x02, 0x02);

            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95P08Immo.TryEnable(image));
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOn, image[0x032]);
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOn, image[0x042]);
            Assert.False(Me7Eeprom95P08Immo.IsOff(image));
        }

        [Fact]
        public void TryDisable_refuses_a_mismatched_pair_and_the_wrong_length()
        {
            byte[] image = Image(0x01, 0x02);
            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95P08Immo.TryDisable(image));
            Assert.Equal(0x01, image[0x032]);
            Assert.Equal(0x02, image[0x042]);

            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95P08Immo.TryDisable(new byte[512]));
            Assert.Equal(Me7Eeprom95040Immo.Result.AlreadyOff, Me7Eeprom95P08Immo.TryDisable(Image(0x02, 0x02)));
            Assert.Equal(Me7Eeprom95040Immo.Result.AlreadyOn, Me7Eeprom95P08Immo.TryEnable(Image(0x01, 0x01)));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
