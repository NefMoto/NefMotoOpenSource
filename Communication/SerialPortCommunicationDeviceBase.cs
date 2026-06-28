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
using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

        public bool WaitForTransmitDrain(uint timeoutMs)
        {
            if (!CheckIsOpen()) return false;
            return Win32SerialTransmitDrain.TryWaitForTxEmpty(_serialPort, timeoutMs);
        }

        /// <summary>
        /// Shared enumeration: finds COM ports matching the given USB VID and creates DeviceInfo via the factory.
        /// Uses batch WMI queries instead of per-port lookups so phantom/BT COM ports do not multiply WMI cost.
        /// </summary>
        protected static IEnumerable<DeviceInfo> EnumerateByVid(
            string vid,
            string logPrefix,
            Func<string, string, string, DeviceInfo> createDeviceInfo,
            DisplayStatusMessageDelegate logMessage = null)
        {
            var devices = new List<DeviceInfo>();
            var totalTimer = Stopwatch.StartNew();

            try
            {
                logMessage?.Invoke($"{logPrefix}: Starting enumeration", StatusMessageType.LOG);

                var candidates = new List<ComPortVidMatch>();
                var pnpTimer = Stopwatch.StartNew();
                bool pnpQueryOk = TryQueryPnPEntitiesByVid(vid, logMessage, logPrefix, candidates);
                logMessage?.Invoke(
                    $"{logPrefix}: Win32_PnPEntity query returned {candidates.Count} candidate(s) in {pnpTimer.Elapsed.TotalMilliseconds:F0} ms"
                        + (pnpQueryOk ? "" : " (query failed)"),
                    StatusMessageType.LOG);

                if (!pnpQueryOk)
                {
                    var serialTimer = Stopwatch.StartNew();
                    int beforeFallback = candidates.Count;
                    candidates.AddRange(QuerySerialPortsByVid(vid, logMessage, logPrefix));
                    logMessage?.Invoke(
                        $"{logPrefix}: Win32_SerialPort fallback returned {candidates.Count - beforeFallback} candidate(s) in {serialTimer.Elapsed.TotalMilliseconds:F0} ms",
                        StatusMessageType.LOG);
                }

                logMessage?.Invoke(
                    $"{logPrefix}: WMI total {candidates.Count} candidate port(s)",
                    StatusMessageType.LOG);

                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    DeviceManager.ReportEnumerationProgress?.Invoke(
                        $"Checking {candidate.PortName} ({i + 1}/{candidates.Count})…");

                    var probeTimer = Stopwatch.StartNew();
                    if (!IsPortAccessible(candidate.PortName))
                    {
                        logMessage?.Invoke(
                            $"{logPrefix}: {candidate.PortName} not accessible (probe {probeTimer.Elapsed.TotalMilliseconds:F0} ms)",
                            StatusMessageType.LOG);
                        continue;
                    }

                    logMessage?.Invoke(
                        $"{logPrefix}: {candidate.PortName} accessible (probe {probeTimer.Elapsed.TotalMilliseconds:F0} ms)",
                        StatusMessageType.LOG);
                    devices.Add(createDeviceInfo(candidate.PortName, candidate.Description, candidate.SerialNumber));
                }

                logMessage?.Invoke(
                    $"{logPrefix}: Finished with {devices.Count} device(s) in {totalTimer.Elapsed.TotalMilliseconds:F0} ms",
                    StatusMessageType.LOG);
            }
            catch (Exception ex)
            {
                logMessage?.Invoke(
                    $"{logPrefix}: Exception: {ex.GetType().Name}: {ex.Message}",
                    StatusMessageType.LOG);
            }

            return devices;
        }

        private readonly struct ComPortVidMatch
        {
            public ComPortVidMatch(string portName, string description, string serialNumber)
            {
                PortName = portName;
                Description = description;
                SerialNumber = serialNumber;
            }

            public string PortName { get; }
            public string Description { get; }
            public string SerialNumber { get; }
        }

        private static IEnumerable<ComPortVidMatch> QuerySerialPortsByVid(
            string vid,
            DisplayStatusMessageDelegate logMessage,
            string logPrefix)
        {
            var matches = new List<ComPortVidMatch>();
            string vidToken = $"VID_{vid}";

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Description, PNPDeviceID FROM Win32_SerialPort"))
                {
                    foreach (ManagementObject port in searcher.Get())
                    {
                        string portName = port["DeviceID"]?.ToString() ?? string.Empty;
                        string pnpId = port["PNPDeviceID"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrEmpty(portName)
                            || !pnpId.Contains(vidToken, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string description = port["Description"]?.ToString() ?? portName;
                        matches.Add(new ComPortVidMatch(
                            portName,
                            description,
                            ExtractSerialNumberFromPnpId(pnpId)));
                    }
                }
            }
            catch (Exception ex)
            {
                logMessage?.Invoke(
                    $"{logPrefix}: Win32_SerialPort WMI failed: {ex.GetType().Name}: {ex.Message}",
                    StatusMessageType.LOG);
            }

            return matches;
        }

        private static bool TryQueryPnPEntitiesByVid(
            string vid,
            DisplayStatusMessageDelegate logMessage,
            string logPrefix,
            List<ComPortVidMatch> matches)
        {
            // WQL LIKE treats '_' as single-char wildcard; escape so VID_1A86 matches literally.
            string vidLikePattern = $"%VID[_]{vid}%";

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE '" + vidLikePattern + "'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        string deviceId = device["DeviceID"]?.ToString() ?? string.Empty;
                        string name = device["Name"]?.ToString() ?? string.Empty;
                        if (!deviceId.Contains($"VID_{vid}", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!TryExtractComPortName(name, out string portName))
                        {
                            continue;
                        }

                        string serialNumber = null;
                        try
                        {
                            serialNumber = device["SerialNumber"]?.ToString();
                        }
                        catch (ManagementException)
                        {
                            serialNumber = null;
                        }

                        if (string.IsNullOrEmpty(serialNumber))
                        {
                            serialNumber = ExtractSerialNumberFromPnpId(deviceId);
                        }

                        matches.Add(new ComPortVidMatch(portName, name, serialNumber ?? string.Empty));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logMessage?.Invoke(
                    $"{logPrefix}: Win32_PnPEntity WMI failed: {ex.GetType().Name}: {ex.Message}",
                    StatusMessageType.LOG);
                return false;
            }
        }

        private static bool TryExtractComPortName(string deviceName, out string portName)
        {
            portName = null;
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            Match match = Regex.Match(deviceName, @"\((COM\d+)\)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            portName = match.Groups[1].Value;
            return !string.IsNullOrEmpty(portName);
        }

        private const int PortProbeTimeoutMs = 1000;

        protected static bool IsPortAccessible(string portName)
        {
            try
            {
                var probe = Task.Run(() => TryProbePort(portName));
                if (!probe.Wait(PortProbeTimeoutMs))
                {
                    return false;
                }

                return probe.Result;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryProbePort(string portName)
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

// vi: set sw=4 ts=8 expandtab:
