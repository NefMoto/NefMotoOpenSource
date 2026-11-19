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
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;

namespace Communication
{
    /// <summary>
    /// macOS serial helpers. System.IO.Ports 11 sets break with tcsendbreak, which pulses
    /// the line for a fraction of a second and cannot be cleared. Slow init needs the line
    /// held low for one bit (~200 ms), so break is TIOCSBRK until TIOCCBRK.
    /// TX drain is tcdrain. BytesToWrite is not the kernel queue on Unix.
    /// </summary>
    internal static class PosixSerialPort
    {
        // _IO('t', 123) and _IO('t', 122) from sys/ttycom.h.
        private const ulong Tiocsbrk = 0x2000747b;
        private const ulong Tioccbrk = 0x2000747a;

        public static bool TrySetBreak(SerialPort serialPort, bool state)
        {
            if (!TryGetFd(serialPort, out int fd))
            {
                return false;
            }

            ulong request = state ? Tiocsbrk : Tioccbrk;
            return ioctl(fd, request, 0) == 0;
        }

        public static bool TryDrain(SerialPort serialPort, uint timeoutMs)
        {
            if (!TryGetFd(serialPort, out int fd))
            {
                return false;
            }

            int result = -1;
            var thread = new Thread(() => { result = tcdrain(fd); })
            {
                IsBackground = true,
                Name = "serial-tcdrain"
            };
            thread.Start();
            int waitMs = timeoutMs > int.MaxValue ? int.MaxValue : (int)timeoutMs;
            if (!thread.Join(waitMs))
            {
                return false;
            }

            return result == 0;
        }

        private static bool TryGetFd(SerialPort serialPort, out int fd)
        {
            fd = -1;
            if (serialPort == null || !serialPort.IsOpen)
            {
                return false;
            }

            if (!SerialPortSafeHandle.TryGet(serialPort, out var handle) || handle == null || handle.IsInvalid)
            {
                return false;
            }

            fd = handle.DangerousGetHandle().ToInt32();
            return fd >= 0;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, ulong request, int arg);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcdrain(int fd);
    }
}

// vi: set sw=4 ts=8 expandtab:
