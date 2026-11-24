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
    /// A map at byte 1 is edited only when those pages already match.
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

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.ReadMapped(image);
            return ee.Immo1 == Me7Eeprom95040Fields.ImmoOff && ee.Immo2 == Me7Eeprom95040Fields.ImmoOff;
        }

        public static Result TryDisable(byte[] image)
        {
            if (image == null || image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                return Result.Refused;
            }

            int offset = Me7Eeprom95040Checksum.MapOffset(image);
            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image, offset);
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
            return Store(image, offset, ee) ? Result.Applied : Result.Refused;
        }

        public static Result TryEnable(byte[] image)
        {
            if (image == null || image.Length != Me7Eeprom95040Checksum.EepromSize)
            {
                return Result.Refused;
            }

            int offset = Me7Eeprom95040Checksum.MapOffset(image);
            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image, offset);
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
            return Store(image, offset, ee) ? Result.Applied : Result.Refused;
        }

        private static bool Store(byte[] image, int offset, Me7Eeprom95040Fields.Image ee)
        {
            if (!Me7Eeprom95040Checksum.ImmoPagesMatch(image, offset, out bool bit1, out bool bit2))
            {
                return false;
            }

            Me7Eeprom95040Fields.Write(image, ee, offset);
            Me7Eeprom95040Checksum.WriteImmoChecksums(image, offset, bit1, bit2);
            return true;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
