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
using Communication;

namespace ECUFlasher
{
    /// <summary>UI option for bootmode SPI EEPROM presets (ME7.1/7.5, SSC/XSSC).</summary>
    public sealed class BootmodeEepromPresetOption
    {
        public string DisplayName { get; private set; }
        public BootstrapInterface.BootmodeEepromSettings Settings { get; private set; }

        public BootmodeEepromPresetOption(string displayName, BootstrapInterface.BootmodeEepromSettings settings)
        {
            DisplayName = displayName;
            Settings = settings;
        }

        public static IReadOnlyList<BootmodeEepromPresetOption> All { get; } = new List<BootmodeEepromPresetOption>
        {
            new BootmodeEepromPresetOption(
                "ME7.1 - 95040 SSC P4.7 (512 B)",
                BootstrapInterface.BootmodeEepromSettings.ForMe71()),
            new BootmodeEepromPresetOption(
                "ME7.5 - 95040 SSC P4.7 (512 B)",
                BootstrapInterface.BootmodeEepromSettings.ForMe75()),
            new BootmodeEepromPresetOption(
                "ME7.1 - 95040 XSSC P4.7 (512 B)",
                MakeXssc(BootstrapInterface.BootmodeEepromSettings.ForMe71())),
            new BootmodeEepromPresetOption(
                "ME7.5 - 95040 XSSC P4.7 (512 B)",
                MakeXssc(BootstrapInterface.BootmodeEepromSettings.ForMe75())),
        };

        private static BootstrapInterface.BootmodeEepromSettings MakeXssc(BootstrapInterface.BootmodeEepromSettings baseSettings)
        {
            return new BootstrapInterface.BootmodeEepromSettings
            {
                Periph = BootstrapInterface.BootmodeEepromPeriph.XSSC,
                EepromType = baseSettings.EepromType,
                PortNumber = baseSettings.PortNumber,
                PinNumber = baseSettings.PinNumber,
                Size = baseSettings.Size
            };
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
