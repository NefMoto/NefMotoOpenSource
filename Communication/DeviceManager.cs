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

#pragma warning disable CA1416 // Validate platform compatibility - This is Windows-only code
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using FTD2XX_NET;
using System.Management;
using Shared;

namespace Communication
{
    /// <summary>
    /// Manages device enumeration and creation of communication device instances
    /// </summary>
    public static class DeviceManager
    {
        /// <summary>
        /// Optional logging delegate for debug messages during device enumeration
        /// </summary>
        public static DisplayStatusMessageDelegate LogMessage { get; set; }

        /// <summary>
        /// Wrapper for LogMessage?.Invoke to simplify logging calls
        /// </summary>
        private static void Log(string message, StatusMessageType messageType = StatusMessageType.LOG)
        {
            LogMessage?.Invoke(message, messageType);
        }

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

            // Enumerate CH340 devices
            devices.AddRange(EnumerateCh340Devices());

            return devices;
        }

        /// <summary>
        /// Enumerates CH340 devices by checking COM ports and identifying CH340 devices
        /// </summary>
        private static IEnumerable<DeviceInfo> EnumerateCh340Devices()
        {
            var ch340Devices = new List<DeviceInfo>();

            try
            {
                // Get all available COM ports
                string[] portNames = SerialPort.GetPortNames();

                foreach (string portName in portNames)
                {
                    try
                    {
                        // First verify the port is actually accessible (not a phantom port)
                        if (!IsPortAccessible(portName))
                        {
                            continue; // Skip phantom/inaccessible ports
                        }

                        // Check if this port is a CH340 device
                        if (IsCh340Device(portName, out string description, out string serialNumber))
                        {
                            string deviceID = $"{portName}_{description}";
                            ch340Devices.Add(new Ch340DeviceInfo(portName, description, serialNumber, deviceID));
                        }
                    }
                    catch
                    {
                        // Skip ports that can't be queried
                        continue;
                    }
                }
            }
            catch
            {
                // If enumeration fails, return empty list
            }
            return ch340Devices;
        }

        /// <summary>
        /// Verifies that a COM port is actually accessible and functional (not a phantom port)
        /// </summary>
        private static bool IsPortAccessible(string portName)
        {
            SerialPort testPort = null;
            try
            {
                // Try to open the port briefly to verify it's accessible
                testPort = new SerialPort(portName);
                testPort.Open();

                // Verify the port is actually open and functional
                if (!testPort.IsOpen)
                {
                    return false;
                }

                // Try to read and set port properties that only real ports support
                // Phantom ports may not support all operations
                try
                {
                    // Accessing properties verifies the port is functional
                    _ = testPort.BaudRate;
                    _ = testPort.Parity;
                    _ = testPort.DataBits;
                    _ = testPort.StopBits;

                    // Try to get bytes available (this often fails on phantom ports)
                    _ = testPort.BytesToRead;
                    _ = testPort.BytesToWrite;

                    // Try to set a property and verify it sticks (phantom ports may not persist changes)
                    int originalBaudRate = testPort.BaudRate;
                    testPort.BaudRate = 9600;
                    if (testPort.BaudRate != 9600)
                    {
                        // Property didn't set correctly - likely a phantom port
                        return false;
                    }
                    testPort.BaudRate = originalBaudRate; // Restore original

                    return true;
                }
                catch
                {
                    // If we can't read/set properties, it's likely a phantom port
                    return false;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Port is in use by another application - it's real but unavailable
                return true;
            }
            catch (ArgumentException)
            {
                // Invalid port name
                return false;
            }
            catch (InvalidOperationException)
            {
                // Port is already open or invalid
                return false;
            }
            catch
            {
                // Port doesn't exist or is inaccessible (phantom port)
                return false;
            }
            finally
            {
                // Always close the port if we opened it
                try
                {
                    if (testPort != null && testPort.IsOpen)
                    {
                        testPort.Close();
                    }
                    testPort?.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        /// <summary>
        /// Checks if a COM port is a CH340 device
        /// Uses WMI to verify VID/PID - only enumerates confirmed CH340 devices
        /// </summary>
        private static bool IsCh340Device(string portName, out string description, out string serialNumber)
        {
            description = null;
            serialNumber = null;


            // Always use WMI to verify VID/PID - only enumerate confirmed CH340 devices
            try
            {
                // CH340 devices have VID 0x1A86 and various PIDs (0x7523, 0x5523, 0x7522, etc.)
                // Query WMI to get device information, filtering by VID and port name
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_1A86%' AND Name LIKE '%{portName}%'"))
                {
                    var devices = searcher.Get();

                    foreach (ManagementObject device in devices)
                    {
                        try
                        {
                            // Check if this device is associated with the COM port
                            string deviceID = device["DeviceID"]?.ToString() ?? "";
                            string name = device["Name"]?.ToString() ?? "";

                            // Device already filtered by port name in query, verify VID
                            if (deviceID.Contains("VID_1A86"))
                            {
                                // Check if it's a CH340 device (VID 1A86)
                                if (deviceID.Contains("VID_1A86"))
                                {
                                    description = name;
                                    // TODO/FIXME: Proper serial number detection for CH340 devices
                                    // CH340 devices often don't expose SerialNumber in WMI, and the current
                                    // PNPDeviceID extraction method may not reliably get the actual serial number.
                                    // Need to investigate alternative methods (registry, USB device queries, etc.)
                                    // Try to get serial number from WMI property first
                                    try
                                    {
                                        object serialNumberObj = device["SerialNumber"];
                                        serialNumber = serialNumberObj?.ToString();
                                    }
                                    catch (System.Management.ManagementException)
                                    {
                                        // SerialNumber property doesn't exist for this device - this is normal for CH340
                                        serialNumber = null;
                                    }
                                    catch (Exception)
                                    {
                                        serialNumber = null;
                                    }
                                    // If not available, extract from PNPDeviceID
                                    if (string.IsNullOrEmpty(serialNumber))
                                    {
                                        serialNumber = ExtractSerialNumberFromPnpId(deviceID);
                                    }
                                    return true;
                                }
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }

                // Alternative: Check by port description and get serial number from PNPDeviceID
                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        $"SELECT * FROM Win32_SerialPort WHERE DeviceID = '{portName}'"))
                    {
                        var ports = searcher.Get();

                        foreach (ManagementObject port in ports)
                        {
                            string pnpDeviceID = port["PNPDeviceID"]?.ToString() ?? "";
                            string desc = port["Description"]?.ToString() ?? "";

                            if (pnpDeviceID.Contains("VID_1A86"))
                            {
                                description = desc ?? $"CH340 Device ({portName})";
                                // Extract serial number from PNPDeviceID (format: USB\VID_1A86&PID_XXXX\SERIALNUMBER)
                                serialNumber = ExtractSerialNumberFromPnpId(pnpDeviceID);
                                return true;
                            }

                            // Only accept if description contains CH340 AND PNPDeviceID confirms VID_1A86
                            if (desc.ToUpper().Contains("CH340") && pnpDeviceID.Contains("VID_1A86"))
                            {
                                description = desc;
                                // Try to get serial number from associated PnP entity
                                serialNumber = ExtractSerialNumberFromPnpId(pnpDeviceID);
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // WMI query failed
                }

                // Try to get serial number from Win32_PnPEntity by matching COM port
                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_1A86%' AND Name LIKE '%{portName}%'"))
                    {
                        foreach (ManagementObject device in searcher.Get())
                        {
                            try
                            {
                                string name = device["Name"]?.ToString() ?? "";
                                string deviceID = device["DeviceID"]?.ToString() ?? "";

                                // Device already filtered by port name and VID in query
                                if (deviceID.Contains("VID_1A86"))
                                {
                                    // Try to get serial number from the device
                                    string sn = null;
                                    try
                                    {
                                        object serialNumberObj = device["SerialNumber"];
                                        sn = serialNumberObj?.ToString();
                                        if (!string.IsNullOrEmpty(sn))
                                        {
                                            description = name;
                                            serialNumber = sn;
                                            return true;
                                        }
                                    }
                                    catch (System.Management.ManagementException)
                                    {
                                        // SerialNumber property doesn't exist for this device - this is normal for CH340
                                    }
                                    catch (Exception)
                                    {
                                    }

                                    // Fallback: extract from PNPDeviceID
                                    serialNumber = ExtractSerialNumberFromPnpId(deviceID);
                                    if (!string.IsNullOrEmpty(serialNumber))
                                    {
                                        description = name;
                                        return true;
                                    }
                                    else
                                    {
                                        // Even without serial number, we can still add the device
                                        description = name;
                                        serialNumber = string.Empty;
                                        return true;
                                    }
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                }
                catch
                {
                    // WMI query failed
                }
            }
            catch
            {
                // WMI not available or query failed - cannot confirm device is CH340
                description = null;
                serialNumber = null;
                return false;
            }

            // If we get here, WMI queries didn't find a CH340 device
            // Only enumerate devices we can confirm are CH340 (VID 1A86)
            description = null;
            serialNumber = null;
            return false;
        }

        /// <summary>
        /// Extracts serial number from PNPDeviceID string
        /// Format: USB\VID_1A86&PID_XXXX\SERIALNUMBER or USB\VID_1A86&PID_XXXX\XXXXX
        /// </summary>
        private static string ExtractSerialNumberFromPnpId(string pnpDeviceID)
        {
            if (string.IsNullOrEmpty(pnpDeviceID))
            {
                return string.Empty;
            }

            try
            {
                // PNPDeviceID format: USB\VID_1A86&PID_7523\SERIALNUMBER
                // Split by backslash and get the last part (after VID/PID)
                string[] parts = pnpDeviceID.Split('\\');
                if (parts.Length >= 3)
                {
                    string serialPart = parts[2];
                    // Remove any instance identifiers (e.g., "&0&1" at the end)
                    int instanceIndex = serialPart.IndexOf('&');
                    if (instanceIndex > 0)
                    {
                        serialPart = serialPart.Substring(0, instanceIndex);
                    }
                    return serialPart;
                }
            }
            catch
            {
                // If extraction fails, return empty
            }

            return string.Empty;
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
