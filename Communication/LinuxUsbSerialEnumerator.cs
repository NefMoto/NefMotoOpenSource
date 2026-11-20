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
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Communication
{
    /// <summary>
    /// Lists ttyUSB and ttyACM nodes under sysfs and walks parents for idVendor.
    /// </summary>
    internal static class LinuxUsbSerialEnumerator
    {
        private const int MaxParentDepth = 12;

        public static List<UsbSerialPort> Query()
        {
            return Query("/sys");
        }

        public static List<UsbSerialPort> Query(string sysfsRoot)
        {
            var found = new List<UsbSerialPort>();
            if (string.IsNullOrEmpty(sysfsRoot))
            {
                return found;
            }

            string classTty = Path.Combine(sysfsRoot, "class", "tty");
            if (!Directory.Exists(classTty))
            {
                return found;
            }

            string rootFull = Path.GetFullPath(sysfsRoot);
            foreach (string ttyDir in Directory.GetDirectories(classTty))
            {
                string name = Path.GetFileName(ttyDir);
                if (!IsUsbSerialName(name))
                {
                    continue;
                }

                if (!TryReadIdentity(ttyDir, rootFull, out int vendorId, out string product, out string serial))
                {
                    continue;
                }

                found.Add(new UsbSerialPort(vendorId, "/dev/" + name, product, serial));
            }

            return found;
        }

        private static bool IsUsbSerialName(string name)
        {
            return name.StartsWith("ttyUSB", StringComparison.Ordinal)
                || name.StartsWith("ttyACM", StringComparison.Ordinal);
        }

        private static bool TryReadIdentity(string ttyDir, string rootFull, out int vendorId, out string product, out string serial)
        {
            vendorId = 0;
            product = null;
            serial = null;
            string deviceLink = Path.Combine(ttyDir, "device");
            if (!Directory.Exists(deviceLink))
            {
                return false;
            }

            string current = ResolveDirectory(deviceLink);
            for (int depth = 0; depth < MaxParentDepth && !string.IsNullOrEmpty(current); depth++)
            {
                if (!IsUnderRoot(current, rootFull))
                {
                    return false;
                }

                string vendorPath = Path.Combine(current, "idVendor");
                if (File.Exists(vendorPath) && TryParseVendor(vendorPath, out vendorId))
                {
                    product = ReadTrimmed(Path.Combine(current, "product"));
                    serial = ReadTrimmed(Path.Combine(current, "serial"));
                    return true;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }

            return false;
        }

        private static string ResolveDirectory(string path)
        {
            var info = new DirectoryInfo(path);
            FileSystemInfo target = info.ResolveLinkTarget(true);
            if (target != null)
            {
                return target.FullName;
            }

            return info.FullName;
        }

        private static bool IsUnderRoot(string path, string rootFull)
        {
            string full = Path.GetFullPath(path);
            if (full.Equals(rootFull, StringComparison.Ordinal))
            {
                return true;
            }

            string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool TryParseVendor(string path, out int vendorId)
        {
            vendorId = 0;
            try
            {
                string text = File.ReadAllText(path).Trim();
                return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ReadTrimmed(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string text = File.ReadAllText(path).Trim();
                return text.Length == 0 ? null : text;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
