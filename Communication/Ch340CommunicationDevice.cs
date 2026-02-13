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

Contact by Email: tony@nefariousmotorsports.com
*/

using System;
using System.Collections.Generic;
using Shared;

#pragma warning disable CA1416 // Validate platform compatibility - This is Windows-only code

namespace Communication
{
    /// <summary>
    /// Adapter using System.IO.Ports.SerialPort for CH340. Fast init. KWP2000 slow init uses break signal (no bit-bang).
    /// Bootmode: DTR pulsed (100ms high, 100ms low) then DTR=0, RTS=0. Windows only (WMI for detection).
    /// </summary>
    /// <remarks>
    /// Limitations: No bit-bang mode; slow init may be less reliable than FTDI. Bootmode baud-rate sensitive:
    /// default 57600; use 38400 if 57600 fails; 9600/19200 can give 0xFD or read-back failures.
    /// At 0x00FF18 (BUSCON3) bootmode-only: C_WRITE_WORD can get 0xFD (NAK) where FTDI succeeds.
    /// </remarks>
    public class Ch340CommunicationDevice : SerialPortCommunicationDeviceBase
    {
        public override DeviceType Type => DeviceType.CH340;

        protected override string DefaultDeviceName => "Unknown CH340 Device";

        protected override bool TryGetPortName(DeviceInfo deviceInfo, out string portName)
        {
            portName = null;
            if (deviceInfo is Ch340DeviceInfo ch340)
            {
                portName = ch340.PortName;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Enumerates all available CH340 devices by checking COM ports and identifying CH340 devices (VID 0x1A86)
        /// </summary>
        public static IEnumerable<DeviceInfo> Enumerate(DisplayStatusMessageDelegate logMessage = null)
        {
            return EnumerateByVid(
                "1A86",
                "EnumerateCh340Devices",
                (portName, description, serialNumber) =>
                {
                    string desc = description ?? $"CH340 Device ({portName})";
                    return new Ch340DeviceInfo(portName, desc, serialNumber ?? string.Empty, $"{portName}_{desc}");
                },
                logMessage);
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
