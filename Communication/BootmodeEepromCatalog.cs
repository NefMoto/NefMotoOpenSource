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

using System;
using System.Collections.Generic;

namespace Communication
{
    /// <summary>
    /// Chip-select labels and EEPROM parts from C167BootTool. Each part has one
    /// size. 95040 is type 1, with address bits in the opcode. 95P08 is type 0,
    /// a 16-bit address, the same code as 95080. Peripheral is chosen separately.
    /// ME7.1 and ME7.5 share P4.7.
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

        public sealed class Part
        {
            public Part(
                string displayName,
                BootstrapInterface.BootmodeEepromType eepromType,
                uint size,
                bool allowsXssc)
            {
                DisplayName = displayName;
                EepromType = eepromType;
                Size = size;
                AllowsXssc = allowsXssc;
            }

            public string DisplayName { get; private set; }
            public BootstrapInterface.BootmodeEepromType EepromType { get; private set; }
            public uint Size { get; private set; }
            public bool AllowsXssc { get; private set; }
        }

        public static IReadOnlyList<ChipSelect> ChipSelects { get; } = new List<ChipSelect>
        {
            new ChipSelect("ME7.1 / ME7.5 P4.7", false, 4, 7),
            new ChipSelect("ME7.1.1 P6.3", false, 6, 3),
            new ChipSelect("Simos 3.2 P6.4", false, 6, 4),
            new ChipSelect("EDC15 I2C", true, 0, 0),
        };

        public static IReadOnlyList<Part> SpiParts { get; } = new List<Part>
        {
            new Part("95040", BootstrapInterface.BootmodeEepromType.Type95040, 512, true),
            new Part("95P08", BootstrapInterface.BootmodeEepromType.Type95080To95320, 1024, true),
            new Part("95080", BootstrapInterface.BootmodeEepromType.Type95080To95320, 1024, true),
            new Part("95160", BootstrapInterface.BootmodeEepromType.Type95080To95320, 2048, true),
            new Part("95320", BootstrapInterface.BootmodeEepromType.Type95080To95320, 4096, true),
            new Part("93S46", BootstrapInterface.BootmodeEepromType.Type93S46_6Bit, 128, false),
            new Part("93S56", BootstrapInterface.BootmodeEepromType.Type93S56_8Bit, 256, false),
            new Part("93S66", BootstrapInterface.BootmodeEepromType.Type93S56_8Bit, 512, false),
            new Part("93S76", BootstrapInterface.BootmodeEepromType.Type93S76_10Bit, 1024, false),
            new Part("93S86", BootstrapInterface.BootmodeEepromType.Type93S76_10Bit, 2048, false),
        };

        public static IReadOnlyList<Part> I2cParts { get; } = new List<Part>
        {
            new Part("24C04", BootstrapInterface.BootmodeEepromType.Type24C04_9Bit, 512, false),
            new Part("24C02", BootstrapInterface.BootmodeEepromType.Type24C02_8Bit, 256, false),
        };

        public static Part DefaultSpiPart
        {
            get { return SpiParts[0]; }
        }

        public static Part DefaultI2cPart
        {
            get { return I2cParts[0]; }
        }

        public static ChipSelect FindChipSelect(string displayName)
        {
            return string.IsNullOrEmpty(displayName)
                ? null
                : First(ChipSelects, chip => chip.DisplayName == displayName);
        }

        public static Part FindPart(bool i2c, string displayName)
        {
            return string.IsNullOrEmpty(displayName)
                ? null
                : First(i2c ? I2cParts : SpiParts, part => part.DisplayName == displayName);
        }

        public static BootstrapInterface.BootmodeEepromSettings ToSettings(
            ChipSelect chip,
            BootstrapInterface.BootmodeEepromPeriph periph,
            Part part)
        {
            return new BootstrapInterface.BootmodeEepromSettings
            {
                Periph = periph,
                EepromType = part.EepromType,
                PortNumber = chip.PortNumber,
                PinNumber = chip.PinNumber,
                Size = part.Size,
                PartName = part.DisplayName
            };
        }

        public static string DisplayNameFor(BootstrapInterface.BootmodeEepromSettings settings)
        {
            if (settings == null)
            {
                return "";
            }

            Part part = First(SpiParts, item => SamePart(item, settings))
                ?? First(I2cParts, item => SamePart(item, settings));
            string label = string.IsNullOrEmpty(settings.PartName)
                ? (part == null ? null : part.DisplayName)
                : settings.PartName;
            string name = label == null ? settings.Periph.ToString() : label + " " + settings.Periph;
            return name + " " + settings.Size + " B";
        }

        private static bool SamePart(Part part, BootstrapInterface.BootmodeEepromSettings settings)
        {
            return part.EepromType == settings.EepromType && part.Size == settings.Size;
        }

        private static T First<T>(IEnumerable<T> items, Func<T, bool> match) where T : class
        {
            foreach (T item in items)
            {
                if (match(item))
                {
                    return item;
                }
            }

            return null;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
