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

using System.Text;
using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    public class BootmodeRangeChecksumTests
    {
        [Fact]
        public void Compute_matches_the_crc32_check_value()
        {
            byte[] data = Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0xCBF43926u, BootmodeRangeChecksum.Compute(data));
        }

        [Fact]
        public void Compute_matches_vectors_the_routine_image_was_simulated_against()
        {
            Assert.Equal(0x41D912FFu, BootmodeRangeChecksum.Compute(new byte[] { 0x00, 0x00 }));
            Assert.Equal(0x9AE0DAAFu, BootmodeRangeChecksum.Compute(Encoding.ASCII.GetBytes("12345678")));
            var sector = new byte[65536];
            for (int i = 0; i < sector.Length; i++)
            {
                sector[i] = 0x5A;
            }
            Assert.Equal(0xF489848Eu, BootmodeRangeChecksum.Compute(sector));
        }

        [Fact]
        public void Image_fits_under_the_monitor_and_starts_with_the_length_check()
        {
            Assert.Equal(144, BootmodeRangeChecksum.Image.Length);
            Assert.True(BootmodeRangeChecksum.LoadAddress + (uint)BootmodeRangeChecksum.Image.Length <= 0xFA60);
            Assert.Equal(new byte[] { 0x46, 0xF9, 0x00, 0x00 }, BootmodeRangeChecksum.Image.AsSpan(0, 4).ToArray());
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
