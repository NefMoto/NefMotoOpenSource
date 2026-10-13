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

using System.Collections.Generic;

namespace Communication
{
    /// <summary>
    /// Chip-select labels and device rows from C167BootTool. Size, type, and
    /// peripheral stay on the device. The chip select is only the port and pin,
    /// or I2C with no pin.
    /// </summary>
    public static class BootmodeEepromCatalog
    {
        public sealed class ChipSelect
        {
            public ChipSelect(string displayName, bool i2c, byte portNumber, byte pinNumber)
            {
                DisplayName = displayName;
                I2c = i2c;
                PortNumber = portNumber;
                PinNumber = pinNumber;
            }

            public string DisplayName { get; private set; }
            public bool I2c { get; private set; }
            public byte PortNumber { get; private set; }
            public byte PinNumber { get; private set; }
        }

        public sealed class Device
        {
            public Device(
                string displayName,
                BootstrapInterface.BootmodeEepromPeriph periph,
                BootstrapInterface.BootmodeEepromType eepromType,
                uint size)
            {
                DisplayName = displayName;
                Periph = periph;
                EepromType = eepromType;
                Size = size;
            }

            public string DisplayName { get; private set; }
            public BootstrapInterface.BootmodeEepromPeriph Periph { get; private set; }
            public BootstrapInterface.BootmodeEepromType EepromType { get; private set; }
            public uint Size { get; private set; }
        }

        public static IReadOnlyList<ChipSelect> ChipSelects { get; } = new List<ChipSelect>
        {
            new ChipSelect("ME7.1 P4.7", false, 4, 7),
            new ChipSelect("ME7.5 P4.7", false, 4, 7),
            new ChipSelect("ME7.1.1 P6.3", false, 6, 3),
            new ChipSelect("Simos 3.2 P6.4", false, 6, 4),
            new ChipSelect("EDC15 I2C", true, 0, 0),
        };

        public static IReadOnlyList<Device> SpiDevices { get; } = new List<Device>
        {
            Spi("95040", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95040, 512),
            Spi("95040", BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95040, 512),
            Spi("95040", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95040, 256),
            Spi("95040", BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95040, 256),
            Spi("95080", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 1024),
            Spi("95080", BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 1024),
            Spi("95160", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 2048),
            Spi("95160", BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 2048),
            Spi("95320", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 4096),
            Spi("95320", BootstrapInterface.BootmodeEepromPeriph.XSSC, BootstrapInterface.BootmodeEepromType.Type95080To95320, 4096),
            Spi("93S46", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S46_6Bit, 128),
            Spi("93S56", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S56_8Bit, 256),
            Spi("93S66", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S56_8Bit, 512),
            Spi("93S76", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S76_10Bit, 1024),
            Spi("93S86", BootstrapInterface.BootmodeEepromPeriph.SSC, BootstrapInterface.BootmodeEepromType.Type93S76_10Bit, 2048),
        };

        public static IReadOnlyList<Device> I2cDevices { get; } = new List<Device>
        {
            new Device(
                "24C04 I2C 512 B",
                BootstrapInterface.BootmodeEepromPeriph.I2C,
                BootstrapInterface.BootmodeEepromType.Type24C04_9Bit,
                512),
            new Device(
                "24C02 I2C 256 B",
                BootstrapInterface.BootmodeEepromPeriph.I2C,
                BootstrapInterface.BootmodeEepromType.Type24C02_8Bit,
                256),
        };

        public static Device DefaultSpiDevice
        {
            get { return SpiDevices[0]; }
        }

        public static Device DefaultI2cDevice
        {
            get { return I2cDevices[0]; }
        }

        public static BootstrapInterface.BootmodeEepromSettings ToSettings(ChipSelect chip, Device device)
        {
            return new BootstrapInterface.BootmodeEepromSettings
            {
                Periph = device.Periph,
                EepromType = device.EepromType,
                PortNumber = chip.PortNumber,
                PinNumber = chip.PinNumber,
                Size = device.Size
            };
        }

        private static Device Spi(
            string name,
            BootstrapInterface.BootmodeEepromPeriph periph,
            BootstrapInterface.BootmodeEepromType eepromType,
            uint size)
        {
            return new Device(name + " " + periph + " " + size + " B", periph, eepromType, size);
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
