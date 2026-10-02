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
    /// Bootmode patches for the mirrored programming pages 30 and 31.
    /// </summary>
    public static class Me7Eeprom95040Page3031
    {
        public enum Result
        {
            Applied,
            AlreadyDone,
            Refused
        }

        public static bool IsClear(byte[] image)
        {
            if (!Loaded(image))
            {
                return false;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            return (ee.P0602Page30 & 0x80) == 0 && (ee.P0602Page31 & 0x80) == 0;
        }

        public static Result TryClearP0602(byte[] image)
        {
            if (!Loaded(image))
            {
                return Result.Refused;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            bool page30 = (ee.P0602Page30 & 0x80) != 0;
            bool page31 = (ee.P0602Page31 & 0x80) != 0;
            if (page30)
            {
                ee.P0602Page30 = (byte)(ee.P0602Page30 & 0x7F);
            }

            if (page31)
            {
                ee.P0602Page31 = (byte)(ee.P0602Page31 & 0x7F);
            }

            return Commit(
                image,
                ee,
                page30,
                page31,
                nameof(Me7Eeprom95040Fields.Image.P0602Page30),
                nameof(Me7Eeprom95040Fields.Image.P0602Page31));
        }

        public static Result TrySetP0602(byte[] image)
        {
            if (!Loaded(image))
            {
                return Result.Refused;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            bool page30 = (ee.P0602Page30 & 0x80) == 0;
            bool page31 = (ee.P0602Page31 & 0x80) == 0;
            if (page30)
            {
                ee.P0602Page30 = (byte)(ee.P0602Page30 | 0x80);
            }

            if (page31)
            {
                ee.P0602Page31 = (byte)(ee.P0602Page31 | 0x80);
            }

            return Commit(
                image,
                ee,
                page30,
                page31,
                nameof(Me7Eeprom95040Fields.Image.P0602Page30),
                nameof(Me7Eeprom95040Fields.Image.P0602Page31));
        }

        public static Result TryResetLockout(byte[] image)
        {
            if (!Loaded(image))
            {
                return Result.Refused;
            }

            Me7Eeprom95040Fields.Image ee = Me7Eeprom95040Fields.Read(image);
            bool page30 = ee.LockoutPage30 != 0;
            bool page31 = ee.LockoutPage31 != 0;
            if (page30)
            {
                ee.LockoutPage30 = 0;
            }

            if (page31)
            {
                ee.LockoutPage31 = 0;
            }

            return Commit(
                image,
                ee,
                page30,
                page31,
                nameof(Me7Eeprom95040Fields.Image.LockoutPage30),
                nameof(Me7Eeprom95040Fields.Image.LockoutPage31));
        }

        private static bool Loaded(byte[] image)
        {
            return image != null && image.Length == Me7Eeprom95040Checksum.EepromSize;
        }

        private static Result Commit(
            byte[] image,
            Me7Eeprom95040Fields.Image ee,
            bool page30,
            bool page31,
            string field30,
            string field31)
        {
            if (!page30 && !page31)
            {
                return Result.AlreadyDone;
            }

            Me7Eeprom95040Fields.Write(image, ee);
            if (page30)
            {
                Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(field30));
            }

            if (page31)
            {
                Me7Eeprom95040Checksum.WritePageChecksum(image, Me7Eeprom95040Fields.PageOf(field31));
            }

            return Result.Applied;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
