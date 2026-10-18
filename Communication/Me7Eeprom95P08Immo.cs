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
    /// Immobilizer status patch for a 95P08 image (1024 bytes, C167BootTool type 1).
    /// Off sets the mirrored bytes at 0x032 and 0x042 to 0x02 when they match.
    /// On sets that pair back to 0x01 when both are 0x02.
    /// The page checksum for this image is not known, so those bytes stay as they are.
    /// </summary>
    public static class Me7Eeprom95P08Immo
    {
        public const int EepromSize = 1024;
        public const byte ImmoOn = 0x01;
        public const byte ImmoOff = 0x02;
        public const int ImmoOffset1 = 0x032;
        public const int ImmoOffset2 = 0x042;

        public static bool IsOff(byte[] image)
        {
            return Fits(image) && image[ImmoOffset1] == ImmoOff && image[ImmoOffset2] == ImmoOff;
        }

        public static Me7Eeprom95040Immo.Result TryDisable(byte[] image)
        {
            return TrySet(image, ImmoOff, false);
        }

        public static Me7Eeprom95040Immo.Result TryEnable(byte[] image)
        {
            return TrySet(image, ImmoOn, true);
        }

        private static bool Fits(byte[] image)
        {
            return image != null && image.Length == EepromSize;
        }

        private static Me7Eeprom95040Immo.Result TrySet(byte[] image, byte value, bool enable)
        {
            if (!Fits(image))
            {
                return Me7Eeprom95040Immo.Result.Refused;
            }

            byte first = image[ImmoOffset1];
            byte second = image[ImmoOffset2];
            if (enable)
            {
                if (first == ImmoOn && second == ImmoOn)
                {
                    return Me7Eeprom95040Immo.Result.AlreadyOn;
                }

                if (first != second || first != ImmoOff)
                {
                    return Me7Eeprom95040Immo.Result.Refused;
                }
            }
            else if (first == ImmoOff && second == ImmoOff)
            {
                return Me7Eeprom95040Immo.Result.AlreadyOff;
            }
            else if (first != second)
            {
                return Me7Eeprom95040Immo.Result.Refused;
            }

            image[ImmoOffset1] = value;
            image[ImmoOffset2] = value;
            return Me7Eeprom95040Immo.Result.Applied;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
