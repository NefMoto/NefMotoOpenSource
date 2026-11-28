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

using System.IO;
using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    /// <summary>
    /// Local dumps in issues/168-95P08-1k-csum. Those files hold VIN and SKC and are not
    /// in source control. A missing file returns without failing, so CI does not need them.
    /// </summary>
    public class Me7Eeprom95040BenchTests
    {
        [Fact]
        public void Eg_and_cs_stay_at_byte_0()
        {
            CheckAligned("022906032EG-eeprom.bin");
            CheckAligned("022906032CS-eeprom.bin");
        }

        [Fact]
        public void The_07K_512_read_still_suggests_a_95P08()
        {
            byte[]? image = ReadDump("eeprom-95040-OG.bin");
            if (image == null)
            {
                return;
            }

            var shipped = Me7Eeprom95040Checksum.Validate(image);
            var settings = new BootstrapInterface.BootmodeEepromSettings
            {
                PortNumber = 6,
                PinNumber = 3,
                EepromType = BootstrapInterface.BootmodeEepromType.Type95040,
                Size = Me7Eeprom95040Checksum.EepromSize
            };
            Assert.Equal("Try 95P08, 1024 bytes.", Me7Eeprom95040Checksum.P63SizeAdvice(settings, shipped));
        }

        private static void CheckAligned(string name)
        {
            byte[]? image = ReadDump(name);
            if (image == null)
            {
                return;
            }

            Assert.True(Me7Eeprom95040Checksum.Validate(image).AllChecksumPagesValid);
        }

        private static byte[]? ReadDump(string name)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string path = Path.Combine(dir.FullName, "issues", "168-95P08-1k-csum", name);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
