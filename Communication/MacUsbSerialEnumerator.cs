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
using System.Runtime.InteropServices;
using System.Text;

namespace Communication
{
    /// <summary>
    /// Lists IOSerialBSDClient callout nodes and walks IOService parents for idVendor.
    /// macOS only. Callers must check <see cref="OperatingSystem.IsMacOS"/> first.
    /// </summary>
    internal static class MacUsbSerialEnumerator
    {
        private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint CFStringEncodingUtf8 = 0x08000100;
        private const int CFNumberSInt32 = 3;
        private const int MaxParentDepth = 24;

        public static List<UsbSerialPort> Query()
        {
            var found = new List<UsbSerialPort>();
            IntPtr matching = IOServiceMatching("IOSerialBSDClient");
            if (matching == IntPtr.Zero)
            {
                return found;
            }

            if (IOServiceGetMatchingServices(0, matching, out uint iterator) != 0)
            {
                return found;
            }

            try
            {
                uint service;
                while ((service = IOIteratorNext(iterator)) != 0)
                {
                    try
                    {
                        AddIfUsbSerial(service, found);
                    }
                    finally
                    {
                        IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                IOObjectRelease(iterator);
            }

            return found;
        }

        private static void AddIfUsbSerial(uint service, List<UsbSerialPort> found)
        {
            string callout = ReadString(service, "IOCalloutDevice");
            if (string.IsNullOrEmpty(callout))
            {
                return;
            }

            if (!TryFindUsbIdentity(service, out int vendorId, out string product, out string serial))
            {
                return;
            }

            found.Add(new UsbSerialPort(vendorId, callout, product, serial));
        }

        private static bool TryFindUsbIdentity(uint entry, out int vendorId, out string product, out string serial)
        {
            vendorId = 0;
            product = null;
            serial = null;
            uint current = entry;
            bool releaseCurrent = false;
            try
            {
                for (int depth = 0; depth < MaxParentDepth; depth++)
                {
                    if (TryReadInt(current, "idVendor", out int vid))
                    {
                        vendorId = vid;
                        product = ReadString(current, "USB Product Name");
                        serial = ReadString(current, "USB Serial Number");
                        return true;
                    }

                    if (IORegistryEntryGetParentEntry(current, "IOService", out uint parent) != 0)
                    {
                        return false;
                    }

                    if (releaseCurrent)
                    {
                        IOObjectRelease(current);
                    }

                    current = parent;
                    releaseCurrent = true;
                }

                return false;
            }
            finally
            {
                if (releaseCurrent)
                {
                    IOObjectRelease(current);
                }
            }
        }

        private static IntPtr CopyProperty(uint entry, string key)
        {
            IntPtr cfKey = CFStringCreateWithCString(IntPtr.Zero, key, CFStringEncodingUtf8);
            if (cfKey == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr value = IORegistryEntryCreateCFProperty(entry, cfKey, IntPtr.Zero, 0);
            CFRelease(cfKey);
            return value;
        }

        private static string ReadString(uint entry, string key)
        {
            IntPtr value = CopyProperty(entry, key);
            if (value == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                if (CFGetTypeID(value) != CFStringGetTypeID())
                {
                    return null;
                }

                byte[] buffer = new byte[1024];
                if (!CFStringGetCString(value, buffer, buffer.Length, CFStringEncodingUtf8))
                {
                    return null;
                }

                int length = Array.IndexOf(buffer, (byte)0);
                if (length < 0)
                {
                    length = buffer.Length;
                }

                if (length == 0)
                {
                    return null;
                }

                return Encoding.UTF8.GetString(buffer, 0, length);
            }
            finally
            {
                CFRelease(value);
            }
        }

        private static bool TryReadInt(uint entry, string key, out int number)
        {
            number = 0;
            IntPtr value = CopyProperty(entry, key);
            if (value == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (CFGetTypeID(value) != CFNumberGetTypeID())
                {
                    return false;
                }

                return CFNumberGetValue(value, CFNumberSInt32, out number);
            }
            finally
            {
                CFRelease(value);
            }
        }

        [DllImport(IOKit)]
        private static extern IntPtr IOServiceMatching(string name);

        [DllImport(IOKit)]
        private static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint existing);

        [DllImport(IOKit)]
        private static extern uint IOIteratorNext(uint iterator);

        [DllImport(IOKit)]
        private static extern int IOObjectRelease(uint obj);

        [DllImport(IOKit)]
        private static extern int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

        [DllImport(IOKit)]
        private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(
            IntPtr alloc,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            uint encoding);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, nint bufferSize, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern ulong CFGetTypeID(IntPtr cf);

        [DllImport(CoreFoundation)]
        private static extern ulong CFStringGetTypeID();

        [DllImport(CoreFoundation)]
        private static extern ulong CFNumberGetTypeID();

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool CFNumberGetValue(IntPtr number, nint theType, out int value);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);
    }
}

// vi: set sw=4 ts=8 expandtab:
