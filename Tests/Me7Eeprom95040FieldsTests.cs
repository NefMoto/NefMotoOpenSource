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
    public class Me7Eeprom95040FieldsTests
    {
        [Theory]
        [InlineData(0x012, Me7Eeprom95040Fields.Role.ImmoStatus)]
        [InlineData(0x022, Me7Eeprom95040Fields.Role.ImmoStatus)]
        [InlineData(0x014, Me7Eeprom95040Fields.Role.Skc)]
        [InlineData(0x025, Me7Eeprom95040Fields.Role.Skc)]
        [InlineData(0x1C0, Me7Eeprom95040Fields.Role.None)]
        [InlineData(0x1E8, Me7Eeprom95040Fields.Role.P0602Flag)]
        [InlineData(0x1F8, Me7Eeprom95040Fields.Role.P0602Flag)]
        [InlineData(0x1EC, Me7Eeprom95040Fields.Role.Lockout)]
        [InlineData(0x1FD, Me7Eeprom95040Fields.Role.Lockout)]
        [InlineData(0x000, Me7Eeprom95040Fields.Role.None)]
        public void RoleAt_marks_known_bytes(int offset, Me7Eeprom95040Fields.Role role)
        {
            Assert.Equal(role, Me7Eeprom95040Fields.RoleAt(offset));
        }

        [Fact]
        public void FormatValues_reads_the_dq_immo_pages()
        {
            var image = new byte[Me7Eeprom95040Checksum.EepromSize];
            image[0x012] = 0x01;
            image[0x022] = 0x01;
            image[0x014] = 0xEC;
            image[0x015] = 0x94;
            image[0x024] = 0xEC;
            image[0x025] = 0x94;
            image[0x1E8] = 0x80;
            image[0x1F8] = 0x00;
            image[0x1EC] = 0x42;
            image[0x1ED] = 0x22;

            Assert.Equal("on", Me7Eeprom95040Fields.FormatImmoValue(image));
            Assert.Equal("94EC", Me7Eeprom95040Fields.FormatSkcValue(image));
            Assert.Equal("set / clear", Me7Eeprom95040Fields.FormatP0602Value(image));
            Assert.Equal("42 22 / 00 00", Me7Eeprom95040Fields.FormatLockoutValue(image));
            Assert.True(Me7Eeprom95040Fields.IsEcuPartNumber(0x1C2));
            Assert.True(Me7Eeprom95040Fields.IsEcuPartNumber(0x1CC));
            Assert.False(Me7Eeprom95040Fields.IsEcuPartNumber(0x1CD));
            Assert.True(Me7Eeprom95040Fields.IsToolId(0x1E2));
            Assert.True(Me7Eeprom95040Fields.IsToolId(0x1E7));
            Assert.False(Me7Eeprom95040Fields.IsToolId(0x1E8));
            Assert.True(Me7Eeprom95040Fields.IsToolId(0x1F2));
            Assert.True(Me7Eeprom95040Fields.IsToolId(0x1F7));
            Assert.False(Me7Eeprom95040Fields.IsToolId(0x1F8));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0B5));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0B9));
            Assert.False(Me7Eeprom95040Fields.IsVin(0x0BA));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0C5));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0C9));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0D0));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0DB));
            Assert.False(Me7Eeprom95040Fields.IsVin(0x0DC));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0E0));
            Assert.True(Me7Eeprom95040Fields.IsVin(0x0EB));
            Assert.False(Me7Eeprom95040Fields.IsVin(0x0EC));
            Assert.Equal(Me7Eeprom95040Fields.Role.None, Me7Eeprom95040Fields.RoleAt(0x0B5));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x0DC));
            Assert.False(Me7Eeprom95040Fields.IsImmoId(0x0DD));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x0F0));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x0FC));
            Assert.False(Me7Eeprom95040Fields.IsImmoId(0x0FD));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x0EC));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x100));
            Assert.True(Me7Eeprom95040Fields.IsImmoId(0x10C));
            Assert.False(Me7Eeprom95040Fields.IsImmoId(0x10D));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
