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
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Communication
{
    /// <summary>
    /// Win32 serial TX drain (ClearCommError / WaitCommEvent) for SerialPort devices on Windows.
    /// </summary>
    internal static class Win32SerialTransmitDrain
    {
        private const uint EV_TXEMPTY = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct COMSTAT
        {
            public uint Flags;
            public uint cbInQue;
            public uint cbOutQue;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ClearCommError(SafeHandle hFile, out uint lpErrors, out COMSTAT lpStat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommMask(SafeHandle hFile, uint dwEvtMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WaitCommEvent(SafeHandle hFile, ref uint lpEvtMask, IntPtr lpOverlapped);

        /// <summary>
        /// Waits until the driver TX queue is empty or timeout expires.
        /// </summary>
        public static bool TryWaitForTxEmpty(SerialPort serialPort, uint timeoutMs)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                return false;
            }

            if (!TryGetSafeHandle(serialPort, out SafeHandle handle) || handle == null || handle.IsInvalid)
            {
                return PollBytesToWrite(serialPort, timeoutMs);
            }

            if (ClearCommError(handle, out _, out COMSTAT initialStat) && initialStat.cbOutQue == 0)
            {
                return true;
            }

            SetCommMask(handle, EV_TXEMPTY);

            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (ClearCommError(handle, out _, out COMSTAT stat) && stat.cbOutQue == 0)
                {
                    uint eventMask = EV_TXEMPTY;
                    WaitCommEvent(handle, ref eventMask, IntPtr.Zero);
                    return true;
                }

                Thread.Sleep(1);
            }

            return ClearCommError(handle, out _, out COMSTAT finalStat) && finalStat.cbOutQue == 0;
        }

        private static bool TryGetSafeHandle(SerialPort serialPort, out SafeHandle handle)
        {
            handle = null;
            try
            {
                object stream = serialPort.BaseStream;
                if (stream == null)
                {
                    return false;
                }

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                foreach (string fieldName in new[] { "_handle", "handle" })
                {
                    FieldInfo field = stream.GetType().GetField(fieldName, flags);
                    if (field?.GetValue(stream) is SafeHandle safeHandle && !safeHandle.IsInvalid)
                    {
                        handle = safeHandle;
                        return true;
                    }
                }

                PropertyInfo prop = stream.GetType().GetProperty("SafeFileHandle", flags);
                if (prop?.GetValue(stream) is SafeHandle propHandle && !propHandle.IsInvalid)
                {
                    handle = propHandle;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool PollBytesToWrite(SerialPort serialPort, uint timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (serialPort.BytesToWrite == 0)
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return serialPort.BytesToWrite == 0;
        }
    }
}
