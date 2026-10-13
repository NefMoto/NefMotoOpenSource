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

namespace Communication
{
    /// <summary>
    /// Which EEPROM driver to load, and how to prepare it, for a C167BootTool
    /// peripheral and type pair. Illegal pairs return false.
    /// </summary>
    public static class BootmodeEepromTransfer
    {
        public enum DriverId
        {
            Ssc25,
            Xssc25,
            Ssc93,
            I2c24
        }

        public readonly struct Choice
        {
            public Choice(
                DriverId driver,
                bool configureXssc,
                bool patchChipSelect,
                bool requireDriverState,
                ushort readBlockLength,
                ushort writeBlockLength)
            {
                Driver = driver;
                ConfigureXssc = configureXssc;
                PatchChipSelect = patchChipSelect;
                RequireDriverState = requireDriverState;
                ReadBlockLength = readBlockLength;
                WriteBlockLength = writeBlockLength;
            }

            public DriverId Driver { get; }
            public bool ConfigureXssc { get; }
            public bool PatchChipSelect { get; }
            public bool RequireDriverState { get; }
            public ushort ReadBlockLength { get; }
            public ushort WriteBlockLength { get; }
        }

        public const ushort ReadBlockLengthValue = 0x200;
        public const ushort I2cWriteBlockLength = 0x40;

        public static bool TryChoose(
            BootstrapInterface.BootmodeEepromPeriph periph,
            BootstrapInterface.BootmodeEepromType type,
            out Choice choice)
        {
            choice = default(Choice);

            bool spi25 = type == BootstrapInterface.BootmodeEepromType.Type95080To95320
                || type == BootstrapInterface.BootmodeEepromType.Type95040;
            bool microwire = type == BootstrapInterface.BootmodeEepromType.Type93S46_6Bit
                || type == BootstrapInterface.BootmodeEepromType.Type93S56_8Bit
                || type == BootstrapInterface.BootmodeEepromType.Type93S76_10Bit;
            bool i2cType = type == BootstrapInterface.BootmodeEepromType.Type24C02_8Bit
                || type == BootstrapInterface.BootmodeEepromType.Type24C04_9Bit;

            if (periph == BootstrapInterface.BootmodeEepromPeriph.I2C)
            {
                if (!i2cType)
                {
                    return false;
                }

                choice = new Choice(
                    DriverId.I2c24,
                    configureXssc: false,
                    patchChipSelect: false,
                    requireDriverState: false,
                    ReadBlockLengthValue,
                    I2cWriteBlockLength);
                return true;
            }

            if (i2cType || (!spi25 && !microwire))
            {
                return false;
            }

            if (periph == BootstrapInterface.BootmodeEepromPeriph.XSSC)
            {
                if (!spi25)
                {
                    return false;
                }

                choice = new Choice(
                    DriverId.Xssc25,
                    configureXssc: true,
                    patchChipSelect: true,
                    requireDriverState: true,
                    ReadBlockLengthValue,
                    ReadBlockLengthValue);
                return true;
            }

            if (periph != BootstrapInterface.BootmodeEepromPeriph.SSC)
            {
                return false;
            }

            if (microwire)
            {
                choice = new Choice(
                    DriverId.Ssc93,
                    configureXssc: false,
                    patchChipSelect: true,
                    requireDriverState: false,
                    ReadBlockLengthValue,
                    ReadBlockLengthValue);
                return true;
            }

            choice = new Choice(
                DriverId.Ssc25,
                configureXssc: false,
                patchChipSelect: true,
                requireDriverState: true,
                ReadBlockLengthValue,
                ReadBlockLengthValue);
            return true;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
