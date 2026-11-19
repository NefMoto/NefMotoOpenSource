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

namespace Communication
{
    /// <summary>
    /// Maps a USB vendor id and serial-port path to a <see cref="DeviceInfo"/>.
    /// Dial-in nodes (/dev/tty.*) are skipped. Callout nodes and Windows COM names are kept.
    /// </summary>
    public static class UsbSerialDeviceMapping
    {
        public const int FtdiVendorId = 0x0403;
        public const int Ch340VendorId = 0x1A86;

        public static DeviceInfo TryCreate(int vendorId, string portName, string description, string serialNumber)
        {
            if (!IsUsablePort(portName))
            {
                return null;
            }

            if (vendorId == FtdiVendorId)
            {
                return new SerialPortDeviceInfo(DeviceType.FTDI, portName, description, serialNumber);
            }

            if (vendorId == Ch340VendorId)
            {
                return new SerialPortDeviceInfo(DeviceType.CH340, portName, description, serialNumber);
            }

            return null;
        }

        public static bool IsUsablePort(string portName)
        {
            if (string.IsNullOrEmpty(portName))
            {
                return false;
            }

            string name = portName;
            int slash = portName.LastIndexOf('/');
            if (slash >= 0)
            {
                name = portName.Substring(slash + 1);
            }

            if (name.StartsWith("tty.", StringComparison.Ordinal))
            {
                return false;
            }

            if (portName.StartsWith("/dev/", StringComparison.Ordinal) && !name.StartsWith("cu.", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
