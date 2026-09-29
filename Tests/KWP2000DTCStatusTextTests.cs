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
    public class KWP2000DTCStatusTextTests
    {
        [Fact]
        public void FormatSetStatusBits_0x68_NamesStoredAndCurrentlyValidated()
        {
            Assert.Equal("inhibited, stored, validated now", KWP2000DTCInfo.FormatSetStatusBits(0x68));
        }

        [Fact]
        public void FormatSetStatusBits_0x40_DoesNotSayStored()
        {
            string text = KWP2000DTCInfo.FormatSetStatusBits(0x40);

            Assert.Equal("validated now", text);
            Assert.DoesNotContain("stored", text);
        }

        [Fact]
        public void FormatSetStatusBits_0x00_IsEmpty()
        {
            Assert.Equal(string.Empty, KWP2000DTCInfo.FormatSetStatusBits(0x00));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
