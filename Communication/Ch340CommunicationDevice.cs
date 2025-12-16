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
using System.IO.Ports;
using System.Management;
using Shared;
#pragma warning disable CA1416 // Validate platform compatibility - This is Windows-only code

namespace Communication
{
    /// <summary>
    /// Adapter class that implements ICommunicationDevice interface using System.IO.Ports.SerialPort for CH340 devices.
    /// CH340 devices appear as standard COM ports in Windows.
    /// </summary>
    public class Ch340CommunicationDevice : ICommunicationDevice
    {
        private SerialPort _serialPort;
        private Ch340DeviceInfo _deviceInfo;

        public Ch340CommunicationDevice()
        {
            _serialPort = null;
        }

        /// <summary>
        /// Helper method to check if device is open, returns false if not open
        /// </summary>
        private bool CheckIsOpen()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                return false;
            }
            return true;
        }

        public bool IsOpen
        {
            get { return _serialPort != null && _serialPort.IsOpen; }
        }

        public DeviceType Type
        {
            get { return DeviceType.CH340; }
        }

        public string DeviceName
        {
            get { return _deviceInfo?.Description ?? "Unknown CH340 Device"; }
        }

        public string SerialNumber
        {
            get { return _deviceInfo?.SerialNumber ?? string.Empty; }
        }

        public bool Open(DeviceInfo deviceInfo)
        {
            if (deviceInfo == null)
            {
                return false;
            }

            if (!(deviceInfo is Ch340DeviceInfo ch340DeviceInfo))
            {
                return false;
            }

            _deviceInfo = ch340DeviceInfo;

            try
            {
                // Close existing port if open
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                // Create and open serial port
                _serialPort = new SerialPort(ch340DeviceInfo.PortName);
                _serialPort.Open();

                // Reset and purge the device
                if (Reset() && Purge(PurgeType.RX | PurgeType.TX))
                {
                    return true;
                }
                else
                {
                    _serialPort.Close();
                    _serialPort = null;
                    return false;
                }
            }
            catch
            {
                if (_serialPort != null)
                {
                    try
                    {
                        _serialPort.Close();
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }
                    _serialPort = null;
                }
                return false;
            }
        }

        public void Close()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    _serialPort.Close();
                }
                catch
                {
                    // Ignore errors during close
                }
            }
            _serialPort = null;
            _deviceInfo = null;
        }

        public bool Reset()
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                // SerialPort doesn't have a direct reset, so we'll close and reopen
                // For now, just purge buffers as a reset equivalent
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Purge(PurgeType purgeType)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                if ((purgeType & PurgeType.RX) != 0)
                {
                    _serialPort.DiscardInBuffer();
                }
                if ((purgeType & PurgeType.TX) != 0)
                {
                    _serialPort.DiscardOutBuffer();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetBaudRate(uint baudRate)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                _serialPort.BaudRate = (int)baudRate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetDataCharacteristics(DataBits dataBits, StopBits stopBits, Parity parity)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                _serialPort.DataBits = (int)dataBits;

                _serialPort.StopBits = stopBits == Communication.StopBits.Bits1 ? System.IO.Ports.StopBits.One : System.IO.Ports.StopBits.Two;

                switch (parity)
                {
                    case Communication.Parity.None:
                        _serialPort.Parity = System.IO.Ports.Parity.None;
                        break;
                    case Communication.Parity.Odd:
                        _serialPort.Parity = System.IO.Ports.Parity.Odd;
                        break;
                    case Communication.Parity.Even:
                        _serialPort.Parity = System.IO.Ports.Parity.Even;
                        break;
                    case Communication.Parity.Mark:
                        _serialPort.Parity = System.IO.Ports.Parity.Mark;
                        break;
                    case Communication.Parity.Space:
                        _serialPort.Parity = System.IO.Ports.Parity.Space;
                        break;
                    default:
                        _serialPort.Parity = System.IO.Ports.Parity.None;
                        break;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetFlowControl(FlowControl flowControl)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                switch (flowControl)
                {
                    case Communication.FlowControl.None:
                        _serialPort.Handshake = System.IO.Ports.Handshake.None;
                        break;
                    case Communication.FlowControl.RtsCts:
                        _serialPort.Handshake = System.IO.Ports.Handshake.RequestToSend;
                        break;
                    case Communication.FlowControl.DtrDsr:
                        _serialPort.Handshake = System.IO.Ports.Handshake.RequestToSendXOnXOff; // DTR/DSR not directly supported, use closest equivalent
                        break;
                    case Communication.FlowControl.XonXoff:
                        _serialPort.Handshake = System.IO.Ports.Handshake.XOnXOff;
                        break;
                    default:
                        _serialPort.Handshake = System.IO.Ports.Handshake.None;
                        break;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetTimeouts(uint readTimeoutMs, uint writeTimeoutMs)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                _serialPort.ReadTimeout = (int)readTimeoutMs;
                _serialPort.WriteTimeout = (int)writeTimeoutMs;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetLatency(byte latencyMs)
        {
            // SerialPort doesn't support latency timer setting
            // This is a USB-specific feature, not applicable to standard serial ports
            return true; // Return true to indicate "not an error, just not supported"
        }

        public bool SetDTR(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                _serialPort.DtrEnable = state;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetRTS(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                _serialPort.RtsEnable = state;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetBreak(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                if (state)
                {
                    _serialPort.BreakState = true;
                }
                else
                {
                    _serialPort.BreakState = false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetBitMode(byte pinMask, BitMode mode)
        {
            // CH340 doesn't support bit-bang mode (this is FTDI-specific)
            // Return false to indicate not supported
            return false;
        }

        public bool GetBitMode(out byte pinState)
        {
            // CH340 doesn't support bit-bang mode (this is FTDI-specific)
            pinState = 0;
            return false;
        }

        public bool Read(byte[] buffer, uint bytesToRead, ref uint bytesRead, uint timeoutMs)
        {
            bytesRead = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            if (buffer == null || buffer.Length < bytesToRead)
            {
                return false;
            }

            try
            {
                // Temporarily set read timeout
                int originalTimeout = _serialPort.ReadTimeout;
                _serialPort.ReadTimeout = (int)timeoutMs;

                try
                {
                    int bytesReadInt = _serialPort.Read(buffer, 0, (int)bytesToRead);
                    bytesRead = (uint)bytesReadInt;
                    return true;
                }
                catch (TimeoutException)
                {
                    // Timeout is acceptable - return true with bytesRead = 0
                    bytesRead = 0;
                    return true;
                }
                finally
                {
                    _serialPort.ReadTimeout = originalTimeout;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool Write(byte[] buffer, int bufferLength, ref uint bytesWritten, uint timeoutMs)
        {
            bytesWritten = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            if (buffer == null || bufferLength <= 0)
            {
                return false;
            }

            try
            {
                // Temporarily set write timeout
                int originalTimeout = _serialPort.WriteTimeout;
                _serialPort.WriteTimeout = (int)timeoutMs;

                try
                {
                    _serialPort.Write(buffer, 0, bufferLength);
                    bytesWritten = (uint)bufferLength;
                    return true;
                }
                finally
                {
                    _serialPort.WriteTimeout = originalTimeout;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool GetRxBytesAvailable(ref uint bytesAvailable, uint maxAttempts)
        {
            bytesAvailable = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                bytesAvailable = (uint)_serialPort.BytesToRead;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool GetTxBytesWaiting(ref uint bytesWaiting)
        {
            bytesWaiting = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            try
            {
                bytesWaiting = (uint)_serialPort.BytesToWrite;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enumerates all available CH340 devices by checking COM ports and identifying CH340 devices
        /// </summary>
        public static IEnumerable<DeviceInfo> Enumerate(DisplayStatusMessageDelegate logMessage = null)
        {
            var ch340Devices = new List<DeviceInfo>();

            try
            {
                if (logMessage != null)
                {
                    logMessage("EnumerateCh340Devices: Starting CH340 enumeration", StatusMessageType.LOG);
                }

                // Get all available COM ports - this is where System.IO.Ports.dll is first used
                string[] portNames;
                try
                {
                    portNames = SerialPort.GetPortNames();
                    if (logMessage != null)
                    {
                        logMessage($"EnumerateCh340Devices: Found {portNames.Length} COM ports", StatusMessageType.LOG);
                    }
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    if (logMessage != null)
                    {
                        logMessage($"EnumerateCh340Devices: FileNotFoundException calling SerialPort.GetPortNames(): {ex.Message}", StatusMessageType.USER);
                        logMessage($"EnumerateCh340Devices: Missing file: {ex.FileName}", StatusMessageType.USER);
                        logMessage($"EnumerateCh340Devices: Fusion log: {ex.FusionLog}", StatusMessageType.LOG);
                    }
                    return ch340Devices; // Return empty list
                }
                catch (Exception ex)
                {
                    if (logMessage != null)
                    {
                        logMessage($"EnumerateCh340Devices: Exception calling SerialPort.GetPortNames(): {ex.GetType().Name}: {ex.Message}", StatusMessageType.USER);
                        logMessage($"EnumerateCh340Devices: Stack trace: {ex.StackTrace}", StatusMessageType.LOG);
                    }
                    return ch340Devices; // Return empty list
                }

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
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
