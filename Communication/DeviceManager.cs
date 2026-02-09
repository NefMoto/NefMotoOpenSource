/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

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

Contact by Email: tony@nefariousmotorsports.com
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Shared;

namespace Communication
{
    /// <summary>
    /// Manages device enumeration and creation of communication device instances.
    /// Enumerates FTDI (via FTD2XX_NET) and CH340 (via WMI/COM port detection).
    /// </summary>
    /// <remarks>
    /// To add a new cable type: (1) Implement ICommunicationDevice (e.g. Cp2102CommunicationDevice),
    /// (2) Add DeviceInfo subclass and DeviceType enum value, (3) Extend EnumerateAllDevices() and CreateDevice(),
    /// (4) Map serial I/O, DTR/RTS, break, baud rate to the new hardware API.
    /// </remarks>
    public static class DeviceManager
    {
        /// <summary>
        /// Optional logging delegate for debug messages during device enumeration
        /// </summary>
        public static DisplayStatusMessageDelegate LogMessage { get; set; }

        /// <summary>
        /// Enumerates all available communication devices
        /// </summary>
        public static IEnumerable<DeviceInfo> EnumerateAllDevices()
        {
            var devices = new List<DeviceInfo>();

            // Enumerate FTDI devices
            devices.AddRange(FtdiCommunicationDevice.Enumerate(LogMessage));

            // Enumerate CH340 devices
            devices.AddRange(Ch340CommunicationDevice.Enumerate(LogMessage));

            return devices;
        }


        /// <summary>
        /// Creates a communication device instance for the given device info
        /// </summary>
        public static ICommunicationDevice CreateDevice(DeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
            {
                return null;
            }

            switch (deviceInfo.Type)
            {
                case DeviceType.FTDI:
                    return new FtdiCommunicationDevice();
                case DeviceType.CH340:
                    return new Ch340CommunicationDevice();
                default:
                    return null;
            }
        }
    }
}
