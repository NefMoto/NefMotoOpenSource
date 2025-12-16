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
using System.IO.Ports;

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
    }
}
