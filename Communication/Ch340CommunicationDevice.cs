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

using System.Collections.Generic;
using System.Threading;
using Shared;

namespace Communication
{
    /// <summary>
    /// CH340 KKL line helpers and Windows WMI enumeration (VID 0x1A86).
    /// Slow init uses per-bit SetBreak; low-baud UART is the fallback.
    /// Bootmode: DTR 100 ms high, 100 ms low, then DTR=0, RTS=0.
    /// </summary>
    /// <remarks>
    /// No bit-bang. Some clones still fail slow init. Bootmode: prefer 57600,
    /// then 38400. 9600/19200 can NAK or fail read-back.
    /// </remarks>
    public static class Ch340CommunicationDevice
    {
        /// <summary>
        /// KKL-style DTR pulse for CH340 (100 ms high, 100 ms low) then DTR=0, RTS=0.
        /// Matches bootmode setup.
        /// </summary>
        public static bool ApplyKklDtrPulse(ICommunicationDevice device)
        {
            if (device == null || device.Type != DeviceType.CH340)
            {
                return true;
            }

            return device.SetDTR(true)
                && DelayMs(100)
                && device.SetDTR(false)
                && DelayMs(100)
                && device.SetDTR(false)
                && device.SetRTS(false);
        }

        /// <summary>
        /// Restore KWP2000 connect line states after CH340 KKL DTR pulse.
        /// </summary>
        public static bool RestoreKwp2000ControlLines(ICommunicationDevice device)
        {
            if (device == null)
            {
                return false;
            }

            return device.SetDTR(true) && device.SetRTS(false) && device.SetBreak(false);
        }

        /// <summary>
        /// Windows COM ports for VID 0x1A86.
        /// </summary>
        public static IEnumerable<DeviceInfo> Enumerate(DisplayStatusMessageDelegate logMessage = null)
        {
            return SerialPortCommunicationDeviceBase.EnumerateByVid(
                "1A86",
                "EnumerateCh340Devices",
                (portName, description, serialNumber) =>
                    new SerialPortDeviceInfo(DeviceType.CH340, portName, description, serialNumber),
                logMessage);
        }

        private static bool DelayMs(int milliseconds)
        {
            Thread.Sleep(milliseconds);
            return true;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
