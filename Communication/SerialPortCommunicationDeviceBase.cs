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
using System.IO.Ports;
using System.Management;
using Shared;
#pragma warning disable CA1416 // Validate platform compatibility - This is Windows-only code

namespace Communication
{
    /// <summary>
    /// Base class for SerialPort-based communication devices (CH340, etc.).
    /// Shares common logic for COM port I/O, DTR/RTS, and WMI-based enumeration.
    /// </summary>
    public abstract class SerialPortCommunicationDeviceBase : ICommunicationDevice
    {
        protected SerialPort _serialPort;
        protected DeviceInfo _deviceInfo;

        protected SerialPortCommunicationDeviceBase()
        {
            _serialPort = null;
            _deviceInfo = null;
        }

        /// <summary>
        /// Device type (CH340, etc.)
        /// </summary>
        public abstract DeviceType Type { get; }

        /// <summary>
        /// Default device name when no device info is set
        /// </summary>
        protected abstract string DefaultDeviceName { get; }

        /// <summary>
        /// Attempts to get the COM port name from the given device info. Returns false if wrong type.
        /// </summary>
        protected abstract bool TryGetPortName(DeviceInfo deviceInfo, out string portName);

        private bool CheckIsOpen()
        {
            return _serialPort != null && _serialPort.IsOpen;
        }

        public bool IsOpen => CheckIsOpen();

        public string DeviceName => _deviceInfo?.Description ?? DefaultDeviceName;

        public string SerialNumber => _deviceInfo?.SerialNumber ?? string.Empty;

        public bool Open(DeviceInfo deviceInfo)
        {
            if (deviceInfo == null || !TryGetPortName(deviceInfo, out string portName))
            {
                return false;
            }

            _deviceInfo = deviceInfo;

            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                _serialPort = new SerialPort(portName);
                _serialPort.Open();

                if (Reset() && Purge(PurgeType.RX | PurgeType.TX))
                {
                    return true;
                }

                _serialPort.Close();
                _serialPort = null;
                return false;
            }
            catch
            {
                if (_serialPort != null)
                {
                    try { _serialPort.Close(); }
                    catch { }
                    _serialPort = null;
                }
                return false;
            }
        }

        public void Close()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try { _serialPort.Close(); }
                catch { }
            }
            _serialPort = null;
            _deviceInfo = null;
        }

        public bool Reset()
        {
            if (!CheckIsOpen()) return false;
            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                return true;
            }
            catch { return false; }
        }

        public bool Purge(PurgeType purgeType)
        {
            if (!CheckIsOpen()) return false;
            try
            {
                if ((purgeType & PurgeType.RX) != 0) _serialPort.DiscardInBuffer();
                if ((purgeType & PurgeType.TX) != 0) _serialPort.DiscardOutBuffer();
                return true;
            }
            catch { return false; }
        }

        public bool SetBaudRate(uint baudRate)
        {
            if (!CheckIsOpen()) return false;
            try { _serialPort.BaudRate = (int)baudRate; return true; }
            catch { return false; }
        }

        public bool SetDataCharacteristics(DataBits dataBits, StopBits stopBits, Parity parity)
        {
            if (!CheckIsOpen()) return false;
            try
            {
                _serialPort.DataBits = (int)dataBits;
                _serialPort.StopBits = stopBits == StopBits.Bits1 ? System.IO.Ports.StopBits.One : System.IO.Ports.StopBits.Two;
                _serialPort.Parity = parity switch
                {
                    Parity.None => System.IO.Ports.Parity.None,
                    Parity.Odd => System.IO.Ports.Parity.Odd,
                    Parity.Even => System.IO.Ports.Parity.Even,
                    Parity.Mark => System.IO.Ports.Parity.Mark,
                    Parity.Space => System.IO.Ports.Parity.Space,
                    _ => System.IO.Ports.Parity.None
                };
                return true;
            }
            catch { return false; }
        }

        public bool SetFlowControl(FlowControl flowControl)
        {
            if (!CheckIsOpen()) return false;
            try
            {
                _serialPort.Handshake = flowControl switch
                {
                    FlowControl.None => System.IO.Ports.Handshake.None,
                    FlowControl.RtsCts => System.IO.Ports.Handshake.RequestToSend,
                    FlowControl.DtrDsr => System.IO.Ports.Handshake.RequestToSendXOnXOff,
                    FlowControl.XonXoff => System.IO.Ports.Handshake.XOnXOff,
                    _ => System.IO.Ports.Handshake.None
                };
                return true;
            }
            catch { return false; }
        }

        public bool SetTimeouts(uint readTimeoutMs, uint writeTimeoutMs)
        {
            if (!CheckIsOpen()) return false;
            try
            {
                _serialPort.ReadTimeout = (int)readTimeoutMs;
                _serialPort.WriteTimeout = (int)writeTimeoutMs;
                return true;
            }
            catch { return false; }
        }

        public bool SetLatency(byte latencyMs) => true;

        public bool SetDTR(bool state)
        {
            if (!CheckIsOpen()) return false;
            try { _serialPort.DtrEnable = state; return true; }
            catch { return false; }
        }

        public bool SetRTS(bool state)
        {
            if (!CheckIsOpen()) return false;
            try { _serialPort.RtsEnable = state; return true; }
            catch { return false; }
        }

        public bool SetBreak(bool state)
        {
            if (!CheckIsOpen()) return false;
            try { _serialPort.BreakState = state; return true; }
            catch { return false; }
        }

        public bool SetBitMode(byte pinMask, BitMode mode) => false;

        public bool GetBitMode(out byte pinState)
        {
            pinState = 0;
            return false;
        }

        public bool Read(byte[] buffer, uint bytesToRead, ref uint bytesRead, uint timeoutMs)
        {
            bytesRead = 0;
            if (!CheckIsOpen() || buffer == null || buffer.Length < bytesToRead) return false;
            try
            {
                int orig = _serialPort.ReadTimeout;
                _serialPort.ReadTimeout = (int)timeoutMs;
                try
                {
                    int n = _serialPort.Read(buffer, 0, (int)bytesToRead);
                    bytesRead = (uint)n;
                    return true;
                }
                catch (TimeoutException)
                {
                    bytesRead = 0;
                    return true;
                }
                finally { _serialPort.ReadTimeout = orig; }
            }
            catch { return false; }
        }

        public bool Write(byte[] buffer, int bufferLength, ref uint bytesWritten, uint timeoutMs)
        {
            bytesWritten = 0;
            if (!CheckIsOpen() || buffer == null || bufferLength <= 0) return false;
            try
            {
                int orig = _serialPort.WriteTimeout;
                _serialPort.WriteTimeout = (int)timeoutMs;
                try
                {
                    _serialPort.Write(buffer, 0, bufferLength);
                    bytesWritten = (uint)bufferLength;
                    return true;
                }
                finally { _serialPort.WriteTimeout = orig; }
            }
            catch { return false; }
        }

        public bool GetRxBytesAvailable(ref uint bytesAvailable, uint maxAttempts)
        {
            bytesAvailable = 0;
            if (!CheckIsOpen()) return false;
            try { bytesAvailable = (uint)_serialPort.BytesToRead; return true; }
            catch { return false; }
        }

        public bool GetTxBytesWaiting(ref uint bytesWaiting)
        {
            bytesWaiting = 0;
            if (!CheckIsOpen()) return false;
            try { bytesWaiting = (uint)_serialPort.BytesToWrite; return true; }
            catch { return false; }
        }

        /// <summary>
        /// Shared enumeration: finds COM ports matching the given USB VID and creates DeviceInfo via the factory.
        /// </summary>
        protected static IEnumerable<DeviceInfo> EnumerateByVid(
            string vid,
            string logPrefix,
            Func<string, string, string, DeviceInfo> createDeviceInfo,
            DisplayStatusMessageDelegate logMessage = null)
        {
            var devices = new List<DeviceInfo>();
            try
            {
                if (logMessage != null)
                    logMessage($"{logPrefix}: Starting enumeration", StatusMessageType.LOG);

                string[] portNames;
                try
                {
                    portNames = SerialPort.GetPortNames();
                    if (logMessage != null)
                        logMessage($"{logPrefix}: Found {portNames.Length} COM ports", StatusMessageType.LOG);
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    if (logMessage != null)
                        logMessage($"{logPrefix}: FileNotFoundException: {ex.Message}", StatusMessageType.USER);
                    return devices;
                }
                catch (Exception ex)
                {
                    if (logMessage != null)
                        logMessage($"{logPrefix}: Exception: {ex.GetType().Name}: {ex.Message}", StatusMessageType.USER);
                    return devices;
                }

                foreach (string portName in portNames)
                {
                    try
                    {
                        if (!IsPortAccessible(portName)) continue;
                        if (IsComPortDeviceByVid(portName, vid, out string description, out string serialNumber))
                        {
                            devices.Add(createDeviceInfo(portName, description, serialNumber));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return devices;
        }

        protected static bool IsPortAccessible(string portName)
        {
            SerialPort testPort = null;
            try
            {
                testPort = new SerialPort(portName);
                testPort.Open();
                if (!testPort.IsOpen) return false;
                try
                {
                    _ = testPort.BaudRate;
                    _ = testPort.Parity;
                    _ = testPort.DataBits;
                    _ = testPort.StopBits;
                    _ = testPort.BytesToRead;
                    _ = testPort.BytesToWrite;
                    int orig = testPort.BaudRate;
                    testPort.BaudRate = 9600;
                    if (testPort.BaudRate != 9600) return false;
                    testPort.BaudRate = orig;
                    return true;
                }
                catch { return false; }
            }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
            finally
            {
                try
                {
                    if (testPort != null && testPort.IsOpen) testPort.Close();
                    testPort?.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// Checks if a COM port belongs to a device with the given USB VID (e.g. "0403" for FTDI, "1A86" for CH340).
        /// </summary>
        protected static bool IsComPortDeviceByVid(string portName, string vid, out string description, out string serialNumber)
        {
            description = null;
            serialNumber = null;

            try
            {
                string vidUpper = $"VID_{vid}";

                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_SerialPort WHERE DeviceID = '{portName}'"))
                {
                    foreach (ManagementObject port in searcher.Get())
                    {
                        string pnpId = port["PNPDeviceID"]?.ToString() ?? "";
                        string desc = port["Description"]?.ToString() ?? "";

                        if (pnpId.Contains(vidUpper, StringComparison.OrdinalIgnoreCase))
                        {
                            description = desc;
                            serialNumber = ExtractSerialNumberFromPnpId(pnpId);
                            return true;
                        }
                    }
                }

                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%{vidUpper}%' AND Name LIKE '%{portName}%'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        try
                        {
                            string deviceID = device["DeviceID"]?.ToString() ?? "";
                            string name = device["Name"]?.ToString() ?? "";

                            if (deviceID.Contains(vidUpper, StringComparison.OrdinalIgnoreCase))
                            {
                                description = name;
                                try
                                {
                                    serialNumber = device["SerialNumber"]?.ToString();
                                }
                                catch (ManagementException) { serialNumber = null; }
                                catch (Exception) { serialNumber = null; }
                                if (string.IsNullOrEmpty(serialNumber))
                                    serialNumber = ExtractSerialNumberFromPnpId(deviceID);
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }

        protected static string ExtractSerialNumberFromPnpId(string pnpDeviceID)
        {
            if (string.IsNullOrEmpty(pnpDeviceID)) return string.Empty;
            try
            {
                string[] parts = pnpDeviceID.Split('\\');
                if (parts.Length >= 3)
                {
                    string serialPart = parts[2];
                    int i = serialPart.IndexOf('&');
                    if (i > 0) serialPart = serialPart.Substring(0, i);
                    return serialPart;
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
#pragma warning restore CA1416 // Validate platform compatibility
