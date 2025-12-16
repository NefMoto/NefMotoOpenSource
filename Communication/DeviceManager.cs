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

using System.Collections.Generic;
using FTD2XX_NET;

namespace Communication
{
    /// <summary>
    /// Manages device enumeration and creation of communication device instances
    /// </summary>
    public static class DeviceManager
    {
        /// <summary>
        /// Enumerates all available communication devices
        /// </summary>
        public static IEnumerable<DeviceInfo> EnumerateAllDevices()
        {
            var devices = new List<DeviceInfo>();

            // Enumerate FTDI devices (existing)
            if (FTDI.IsFTD2XXDLLLoaded())
            {
                var ftdiLibrary = new FTDI(null, null);
                var ftdiDevices = ftdiLibrary.EnumerateFTDIDevices();
                foreach (var ftdiNode in ftdiDevices)
                {
                    // Skip unknown FTDI devices
                    if (ftdiNode.Type == FTDI.FT_DEVICE.FT_DEVICE_UNKNOWN)
                    {
                        continue;
                    }

                    // Try to get chip ID if available
                    uint chipID = 0;
                    if (FTDI.IsFTDChipIDDLLLoaded())
                    {
                        var tempFtdi = new FTDI(null, null);
                        if (tempFtdi.OpenByLocation(ftdiNode.LocId) == FTDI.FT_STATUS.FT_OK)
                        {
                            tempFtdi.GetChipIDFromCurrentDevice(out chipID);
                            tempFtdi.Close();
                        }
                    }
                    devices.Add(new FtdiDeviceInfo(ftdiNode, chipID));
                }
            }

            // TODO: Enumerate CH340 devices when CH340 support is implemented

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
                    // TODO: Return Ch340CommunicationDevice when implemented
                    return null;
                default:
                    return null;
            }
        }
    }
}
