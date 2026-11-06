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
    public class FirstWakeFlashTests
    {
        [Theory]
        [InlineData(0x00000000u, 0x00000000u)]
        [InlineData(0x00000001u, 0x00000020u)]
        [InlineData(0x80000000u, 0x91A10741u)]
        [InlineData(0x12345678u, 0x77AC8BA2u)]
        public void Key_matches_known_seeds(uint seed, uint key)
        {
            Assert.Equal(key, FirstWakeFlashCodec.Key(seed, FirstWakeFlashCodec.FirstWakeFlashPolynomial));
        }

        [Theory]
        [InlineData(0x00000000u, 0x00000000u)]
        [InlineData(0x00000001u, 0x00000020u)]
        [InlineData(0x80000000u, 0x8520AD30u)]
        [InlineData(0x12345678u, 0xF9F07478u)]
        public void Level1Key_matches_seedkey_level1(uint seed, uint key)
        {
            Assert.Equal(key, FirstWakeFlashCodec.Key(seed, FirstWakeFlashCodec.Level1Polynomial));
        }

        [Fact]
        public void Compress_encodes_a_run_and_literals()
        {
            byte[] compressed = FirstWakeFlashCodec.Compress(new byte[] { (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'X', (byte)'X', (byte)'X', (byte)'X' });
            Assert.Equal("1A01400541000458585858C0000002A5", Bytes(compressed));
        }

        [Fact]
        public void Compress_encodes_a_short_literal()
        {
            byte[] compressed = FirstWakeFlashCodec.Compress(new byte[] { 1, 2, 3, 4, 5 });
            Assert.Equal("1A0100050102030405C00000000F", Bytes(compressed));
        }

        [Fact]
        public void Transfer_cipher_keeps_the_header_and_xors_with_disposition()
        {
            byte[] data = FirstWakeFlashCodec.Compress(new byte[] { (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'X', (byte)'X', (byte)'X', (byte)'X' });
            FirstWakeFlashCodec.ApplyTransferCipher(data);
            Assert.Equal(0x1A, data[0]);
            Assert.Equal(0x01, data[1]);
            Assert.Equal(0x40 ^ (byte)'D', data[2]);
        }

        [Fact]
        public void PrepareImage_fixes_the_calibration_and_summary_words()
        {
            byte[] image = new byte[0xBF06];
            image[0x6900] = 0x00;
            image[0x6901] = (byte)'i';
            image[0x6902] = 0x01;
            image[0xBF02] = 0x12;
            image[0xBF03] = 0x34;

            string status = FirstWakeFlashCodec.PrepareImage(image);

            Assert.Equal(0xFF, image[0]);
            Assert.Equal(0xFF, image[0x400]);
            Assert.Equal(0x00, image[0xBF04]);
            Assert.Equal(0x6A, image[0xBF05]);
            Assert.Equal(0x12, image[0xBF00]);
            Assert.Equal(0x9E, image[0xBF01]);
            Assert.Contains("6A", status);
            Assert.Contains("129E", status);
        }

        [Theory]
        [InlineData(9600u, "14")]
        [InlineData(10400u, "14")]
        [InlineData(38400u, "5014")]
        [InlineData(52800u, "5014")]
        [InlineData(56000u, "5014")]
        [InlineData(124800u, "5014")]
        public void SessionSpeeds_stay_at_or_below_the_baud_ceiling(uint ceiling, string speeds)
        {
            string actual = "";
            foreach (byte speed in FirstWakeFlashCodec.SessionSpeeds(ceiling))
            {
                actual += speed.ToString("X2");
            }

            Assert.Equal(speeds, actual);
        }

        [Fact]
        public void Erase_request_uses_the_known_trailer()
        {
            Assert.Equal("C400000003FFFF0001010A81FE", Bytes(FirstWakeFlashCodec.EraseRequest(0, 0x3FFFF)));
        }

        private static string Bytes(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "");
        }
    }

    public class KWP1281CalibrationReadTests
    {
        [Fact]
        public void Eeprom_marker_copies_immo_byte()
        {
            var window = new byte[0x100];
            for (int i = 0; i < window.Length; i++)
            {
                window[i] = 0x11;
            }

            window[0x20] = 0x12;
            window[0x21] = 0x68;
            window[0x22] = 0x30;
            window[0x23] = 0x12;
            window[0x20 + KWP1281CalibrationRead.ImmoOffset] = KWP1281CalibrationRead.ImmoEnabled;

            byte[] eeprom = KWP1281CalibrationRead.CopyEeprom(window);
            Assert.Equal(KWP1281CalibrationRead.EepromSize, eeprom.Length);
            Assert.Equal(KWP1281CalibrationRead.ImmoEnabled, eeprom[KWP1281CalibrationRead.ImmoOffset]);
            Assert.Contains("enabled", KWP1281CalibrationRead.Describe(eeprom));
        }

        [Fact]
        public void Eeprom_marker_at_the_end_is_padded()
        {
            var window = new byte[0x20];
            window[0x1C] = 0x12;
            window[0x1D] = 0x68;
            window[0x1E] = 0x30;
            window[0x1F] = 0x12;

            byte[] eeprom = KWP1281CalibrationRead.CopyEeprom(window);
            Assert.Equal(0x12, eeprom[0]);
            Assert.Equal(0xFF, eeprom[KWP1281CalibrationRead.ImmoOffset]);
            Assert.Contains("unrecognized", KWP1281CalibrationRead.Describe(eeprom));
        }

        [Fact]
        public void Missing_eeprom_marker_is_reported()
        {
            Assert.Null(KWP1281CalibrationRead.CopyEeprom(new byte[] { 0x12, 0x68, 0x30 }));
            Assert.Contains("not found", KWP1281CalibrationRead.Describe(null));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
