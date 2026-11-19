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

using System.IO.Ports;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Communication
{
    /// <summary>
    /// Reads the native handle stored on <see cref="SerialPort.BaseStream"/>.
    /// </summary>
    internal static class SerialPortSafeHandle
    {
        public static bool TryGet(SerialPort serialPort, out SafeHandle handle)
        {
            handle = null;
            if (serialPort == null)
            {
                return false;
            }

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
    }
}

// vi: set sw=4 ts=8 expandtab:
