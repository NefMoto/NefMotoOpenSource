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
    /// Immobilizer status patch for a ME7 95040 image. Off sets the mirrored
    /// bytes at 0x012 and 0x022 to 0x02 when they match. On sets that pair back
    /// to 0x01 when both are 0x02. Either change refreshes those two page checksums.
    /// </summary>
    public static class Me7Eeprom95040Immo
    {
        public enum Result
        {
            Applied,
            AlreadyOff,
            AlreadyOn,
            Refused
        }

        public static bool IsOff(byte[] image)
        {
            if (image == null || image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                return false;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            return ee.Immo1 == Me7Eeprom95040Fields.ImmoOff && ee.Immo2 == Me7Eeprom95040Fields.ImmoOff;
        }

        public static Result TryDisable(byte[] image)
        {
            if (image == null || image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                return Result.Refused;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            if (ee.Immo1 == Me7Eeprom95040Fields.ImmoOff && ee.Immo2 == Me7Eeprom95040Fields.ImmoOff)
            {
                return Result.AlreadyOff;
            }

            if (ee.Immo1 != ee.Immo2)
            {
                return Result.Refused;
            }

            ee.Immo1 = Me7Eeprom95040Fields.ImmoOff;
            ee.Immo2 = Me7Eeprom95040Fields.ImmoOff;
            Me7Eeprom95040Fields.Write(image, ee);
            Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(nameof(Me7Eeprom95040Fields.Image.Immo1)));
            Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(nameof(Me7Eeprom95040Fields.Image.Immo2)));
            return Result.Applied;
        }

        public static Result TryEnable(byte[] image)
        {
            if (image == null || image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                return Result.Refused;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            if (ee.Immo1 == Me7Eeprom95040Fields.ImmoOn && ee.Immo2 == Me7Eeprom95040Fields.ImmoOn)
            {
                return Result.AlreadyOn;
            }

            if (ee.Immo1 != ee.Immo2 || ee.Immo1 != Me7Eeprom95040Fields.ImmoOff)
            {
                return Result.Refused;
            }

            ee.Immo1 = Me7Eeprom95040Fields.ImmoOn;
            ee.Immo2 = Me7Eeprom95040Fields.ImmoOn;
            Me7Eeprom95040Fields.Write(image, ee);
            Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(nameof(Me7Eeprom95040Fields.Image.Immo1)));
            Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(nameof(Me7Eeprom95040Fields.Image.Immo2)));
            return Result.Applied;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
