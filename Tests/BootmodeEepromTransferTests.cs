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
    public class BootmodeEepromTransferTests
    {
        [Theory]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromTransfer.DriverId.Ssc25, false, true, true, 16)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, BootmodeEepromTransfer.DriverId.Ssc25, false, true, true, 16)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromTransfer.DriverId.Xssc25, true, true, true, 16)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, BootmodeEepromTransfer.DriverId.Xssc25, true, true, true, 16)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S46_6Bit, BootmodeEepromTransfer.DriverId.Ssc93, false, true, false, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S56_8Bit, BootmodeEepromTransfer.DriverId.Ssc93, false, true, false, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S76_10Bit, BootmodeEepromTransfer.DriverId.Ssc93, false, true, false, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.I2C, BootstrapInterface.BootmodeEepromType.Type24C02_8Bit, BootmodeEepromTransfer.DriverId.I2c24, false, false, false, 0x40)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.I2C, BootstrapInterface.BootmodeEepromType.Type24C04_9Bit, BootmodeEepromTransfer.DriverId.I2c24, false, false, false, 0x40)]
        public void TryChoose_accepts_documented_pairs(
            BootstrapInterface.BootmodeEepromPeriph periph,
            BootstrapInterface.BootmodeEepromType type,
            BootmodeEepromTransfer.DriverId driver,
            bool configureXssc,
            bool patchChipSelect,
            bool requireDriverState,
            int writeBlockLength)
        {
            BootmodeEepromTransfer.Choice choice;
            Assert.True(BootmodeEepromTransfer.TryChoose(periph, type, out choice));
            Assert.Equal(driver, choice.Driver);
            Assert.Equal(configureXssc, choice.ConfigureXssc);
            Assert.Equal(patchChipSelect, choice.PatchChipSelect);
            Assert.Equal(requireDriverState, choice.RequireDriverState);
            Assert.Equal(BootmodeEepromTransfer.ReadBlockLengthValue, choice.ReadBlockLength);
            Assert.Equal((ushort)writeBlockLength, choice.WriteBlockLength);
        }

        [Theory]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type93S46_6Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type93S56_8Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type93S76_10Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type24C02_8Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type24C04_9Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type24C02_8Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type24C04_9Bit)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.I2C, BootstrapInterface.BootmodeEepromType.Type95040)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.I2C, BootstrapInterface.BootmodeEepromType.Type95080To95320)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.I2C, BootstrapInterface.BootmodeEepromType.Type93S46_6Bit)]
        public void TryChoose_rejects_illegal_pairs(
            BootstrapInterface.BootmodeEepromPeriph periph,
            BootstrapInterface.BootmodeEepromType type)
        {
            BootmodeEepromTransfer.Choice choice;
            Assert.False(BootmodeEepromTransfer.TryChoose(periph, type, out choice));
        }

        [Fact]
        public void Catalog_defaults_match_me71_and_me75()
        {
            Assert.Equal(4, BootmodeEepromCatalog.ChipSelects.Count);
            Assert.Equal("ME7.1 / ME7.5 P4.7", BootmodeEepromCatalog.ChipSelects[0].DisplayName);
            Assert.Equal(4, BootmodeEepromCatalog.ChipSelects[0].PortNumber);
            Assert.Equal(7, BootmodeEepromCatalog.ChipSelects[0].PinNumber);
            Assert.False(BootmodeEepromCatalog.ChipSelects[0].I2c);
            Assert.Null(BootmodeEepromCatalog.FindChipSelect("ME7.5 P4.7"));

            Assert.Equal("ME7.1.1 P6.3", BootmodeEepromCatalog.ChipSelects[1].DisplayName);
            Assert.Equal(6, BootmodeEepromCatalog.ChipSelects[1].PortNumber);
            Assert.Equal(3, BootmodeEepromCatalog.ChipSelects[1].PinNumber);

            Assert.Equal("Simos 3.2 P6.4", BootmodeEepromCatalog.ChipSelects[2].DisplayName);
            Assert.Equal(6, BootmodeEepromCatalog.ChipSelects[2].PortNumber);
            Assert.Equal(4, BootmodeEepromCatalog.ChipSelects[2].PinNumber);
            Assert.False(BootmodeEepromCatalog.ChipSelects[2].I2c);

            Assert.True(BootmodeEepromCatalog.ChipSelects[3].I2c);
            Assert.Equal("EDC15 I2C", BootmodeEepromCatalog.ChipSelects[3].DisplayName);

            var shared = BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[0],
                BootstrapInterface.BootmodeEepromPeriph.SSC,
                BootmodeEepromCatalog.DefaultSpiPart);
            var stock = BootstrapInterface.BootmodeEepromSettings.ForMe71();
            var stock75 = BootstrapInterface.BootmodeEepromSettings.ForMe75();
            Assert.Equal(stock.Periph, shared.Periph);
            Assert.Equal(stock.EepromType, shared.EepromType);
            Assert.Equal(stock.PortNumber, shared.PortNumber);
            Assert.Equal(stock.PinNumber, shared.PinNumber);
            Assert.Equal(stock.Size, shared.Size);
            Assert.Equal(stock75.Periph, shared.Periph);
            Assert.Equal(stock75.PortNumber, shared.PortNumber);
            Assert.Equal(stock75.PinNumber, shared.PinNumber);
            Assert.Equal(stock75.Size, shared.Size);

            Assert.Equal(512u, BootmodeEepromCatalog.DefaultSpiPart.Size);
            Assert.Equal(BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromCatalog.DefaultSpiPart.EepromType);
            Assert.Equal("95040", BootmodeEepromCatalog.DefaultSpiPart.DisplayName);
            Assert.Null(BootmodeEepromCatalog.FindPart(false, "95040 256"));
            Assert.Equal(512u, BootmodeEepromCatalog.DefaultI2cPart.Size);
            Assert.Equal(BootstrapInterface.BootmodeEepromType.Type24C04_9Bit, BootmodeEepromCatalog.DefaultI2cPart.EepromType);

            var p08 = BootmodeEepromCatalog.FindPart(false, "95P08");
            Assert.NotNull(p08);
            Assert.Equal(BootstrapInterface.BootmodeEepromType.Type95080To95320, p08.EepromType);
            Assert.Equal((ushort)0, (ushort)p08.EepromType);
            Assert.Equal(1024u, p08.Size);
            var p63 = BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[1],
                BootstrapInterface.BootmodeEepromPeriph.SSC,
                p08);
            Assert.Equal(6, p63.PortNumber);
            Assert.Equal(3, p63.PinNumber);
            Assert.Equal((ushort)0, (ushort)p63.EepromType);
            Assert.Equal(1024u, p63.Size);
            foreach (string name in new[] { "95080", "95160", "95320" })
            {
                var wide = BootmodeEepromCatalog.FindPart(false, name);
                Assert.NotNull(wide);
                Assert.Equal(BootstrapInterface.BootmodeEepromType.Type95080To95320, wide.EepromType);
            }

            Assert.Equal(
                BootstrapInterface.BootmodeEepromType.Type95040,
                BootmodeEepromCatalog.FindPart(false, "95040").EepromType);
            string p08Name = BootmodeEepromCatalog.DisplayNameFor(BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[0],
                BootstrapInterface.BootmodeEepromPeriph.SSC,
                p08));
            Assert.StartsWith("95P08", p08Name);

            var p80 = BootmodeEepromCatalog.FindPart(false, "95080");
            string p80Name = BootmodeEepromCatalog.DisplayNameFor(BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[0],
                BootstrapInterface.BootmodeEepromPeriph.SSC,
                p80));
            Assert.StartsWith("95080", p80Name);
        }

        [Fact]
        public void Catalog_parts_are_legal_pairs()
        {
            int xssc = 0;
            foreach (BootmodeEepromCatalog.Part part in BootmodeEepromCatalog.SpiParts)
            {
                BootmodeEepromTransfer.Choice choice;
                Assert.True(BootmodeEepromTransfer.TryChoose(
                    BootstrapInterface.BootmodeEepromPeriph.SSC,
                    part.EepromType,
                    out choice));
                if (part.AllowsXssc)
                {
                    xssc++;
                    Assert.True(BootmodeEepromTransfer.TryChoose(
                        BootstrapInterface.BootmodeEepromPeriph.XSSC,
                        part.EepromType,
                        out choice));
                }
                else
                {
                    Assert.False(BootmodeEepromTransfer.TryChoose(
                        BootstrapInterface.BootmodeEepromPeriph.XSSC,
                        part.EepromType,
                        out choice));
                }
            }

            Assert.Equal(5, xssc);

            foreach (BootmodeEepromCatalog.Part part in BootmodeEepromCatalog.I2cParts)
            {
                BootmodeEepromTransfer.Choice choice;
                Assert.False(part.AllowsXssc);
                Assert.True(BootmodeEepromTransfer.TryChoose(
                    BootstrapInterface.BootmodeEepromPeriph.I2C,
                    part.EepromType,
                    out choice));
                Assert.False(choice.PatchChipSelect);
                Assert.Equal(BootmodeEepromTransfer.I2cWriteBlockLength, choice.WriteBlockLength);
            }
        }

        [Fact]
        public void Write_progress_reports_every_ten_percent()
        {
            int bucket = 0;
            var reported = new System.Collections.Generic.List<uint>();
            for (uint offset = 16; offset <= 1024; offset += 16)
            {
                if (BootstrapInterface.EepromProgressDue(true, offset, 1024, ref bucket))
                {
                    reported.Add(offset);
                }
            }

            Assert.Equal(new uint[] { 112, 208, 320, 416, 512, 624, 720, 832, 928, 1024 }, reported);
        }

        [Fact]
        public void Read_progress_reports_every_block()
        {
            int bucket = 0;
            Assert.True(BootstrapInterface.EepromProgressDue(false, 512, 1024, ref bucket));
            Assert.True(BootstrapInterface.EepromProgressDue(false, 1024, 1024, ref bucket));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
