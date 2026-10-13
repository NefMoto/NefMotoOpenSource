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
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromTransfer.DriverId.Ssc25, false, true, true, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, BootmodeEepromTransfer.DriverId.Ssc25, false, true, true, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromTransfer.DriverId.Xssc25, true, true, true, 0x200)]
        [InlineData(BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, BootmodeEepromTransfer.DriverId.Xssc25, true, true, true, 0x200)]
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
            Assert.Equal("ME7.1 P4.7", BootmodeEepromCatalog.ChipSelects[0].DisplayName);
            Assert.Equal("ME7.5 P4.7", BootmodeEepromCatalog.ChipSelects[1].DisplayName);
            Assert.Equal(4, BootmodeEepromCatalog.ChipSelects[0].PortNumber);
            Assert.Equal(7, BootmodeEepromCatalog.ChipSelects[0].PinNumber);
            Assert.Equal(BootmodeEepromCatalog.ChipSelects[0].PortNumber, BootmodeEepromCatalog.ChipSelects[1].PortNumber);
            Assert.Equal(BootmodeEepromCatalog.ChipSelects[0].PinNumber, BootmodeEepromCatalog.ChipSelects[1].PinNumber);
            Assert.False(BootmodeEepromCatalog.ChipSelects[0].I2c);

            Assert.Equal("ME7.1.1 P6.3", BootmodeEepromCatalog.ChipSelects[2].DisplayName);
            Assert.Equal(6, BootmodeEepromCatalog.ChipSelects[2].PortNumber);
            Assert.Equal(3, BootmodeEepromCatalog.ChipSelects[2].PinNumber);

            Assert.Equal("Simos 3.2 P6.4", BootmodeEepromCatalog.ChipSelects[3].DisplayName);
            Assert.Equal(6, BootmodeEepromCatalog.ChipSelects[3].PortNumber);
            Assert.Equal(4, BootmodeEepromCatalog.ChipSelects[3].PinNumber);
            Assert.False(BootmodeEepromCatalog.ChipSelects[3].I2c);

            Assert.True(BootmodeEepromCatalog.ChipSelects[4].I2c);
            Assert.Equal("EDC15 I2C", BootmodeEepromCatalog.ChipSelects[4].DisplayName);

            var me71 = BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[0],
                BootmodeEepromCatalog.DefaultSpiDevice);
            var stock = BootstrapInterface.BootmodeEepromSettings.ForMe71();
            Assert.Equal(stock.Periph, me71.Periph);
            Assert.Equal(stock.EepromType, me71.EepromType);
            Assert.Equal(stock.PortNumber, me71.PortNumber);
            Assert.Equal(stock.PinNumber, me71.PinNumber);
            Assert.Equal(stock.Size, me71.Size);

            var me75 = BootmodeEepromCatalog.ToSettings(
                BootmodeEepromCatalog.ChipSelects[1],
                BootmodeEepromCatalog.DefaultSpiDevice);
            var stock75 = BootstrapInterface.BootmodeEepromSettings.ForMe75();
            Assert.Equal(stock75.Periph, me75.Periph);
            Assert.Equal(stock75.PortNumber, me75.PortNumber);
            Assert.Equal(stock75.PinNumber, me75.PinNumber);
            Assert.Equal(stock75.Size, me75.Size);
            Assert.Equal(512u, BootmodeEepromCatalog.DefaultSpiDevice.Size);
            Assert.Equal(BootstrapInterface.BootmodeEepromType.Type95040, BootmodeEepromCatalog.DefaultSpiDevice.EepromType);
            Assert.Equal(512u, BootmodeEepromCatalog.DefaultI2cDevice.Size);
            Assert.Equal(BootstrapInterface.BootmodeEepromType.Type24C04_9Bit, BootmodeEepromCatalog.DefaultI2cDevice.EepromType);
        }

        [Fact]
        public void Catalog_devices_are_legal_pairs()
        {
            foreach (BootmodeEepromCatalog.Device device in BootmodeEepromCatalog.SpiDevices)
            {
                BootmodeEepromTransfer.Choice choice;
                Assert.True(BootmodeEepromTransfer.TryChoose(device.Periph, device.EepromType, out choice));
                Assert.NotEqual(BootstrapInterface.BootmodeEepromPeriph.I2C, device.Periph);
            }

            foreach (BootmodeEepromCatalog.Device device in BootmodeEepromCatalog.I2cDevices)
            {
                BootmodeEepromTransfer.Choice choice;
                Assert.True(BootmodeEepromTransfer.TryChoose(device.Periph, device.EepromType, out choice));
                Assert.Equal(BootstrapInterface.BootmodeEepromPeriph.I2C, device.Periph);
                Assert.False(choice.PatchChipSelect);
                Assert.Equal(BootmodeEepromTransfer.I2cWriteBlockLength, choice.WriteBlockLength);
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
