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
    public class ReadConnectIdentTests
    {
        [Fact]
        public void IdentText_drops_padding_and_unprintables()
        {
            Assert.Equal("06A906032NL", ReadConnectIdentOperation.IdentText(new byte[] { 0x30, 0x36, 0x41, 0x39, 0x30, 0x36, 0x30, 0x33, 0x32, 0x4E, 0x4C, 0x20, 0x00, 0xFF }));
            Assert.Equal("", ReadConnectIdentOperation.IdentText(null));
        }

        [Theory]
        [InlineData("THIS-IS-THE-RAM-PROGRAM", true)]
        [InlineData("06A906032NL", false)]
        public void RamProgramIdent_detected_in_ident_text(string text, bool expected)
        {
            Assert.Equal(expected, KWP2000RamProgramIdent.ContainsRamProgramIdent(text));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
