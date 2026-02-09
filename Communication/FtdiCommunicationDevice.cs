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

using FTD2XX_NET;
using System;
using System.Collections.Generic;
using Shared;

namespace Communication
{
    /// <summary>
    /// Adapter that wraps FTD2XX_NET for FTDI devices. Bit-bang mode for KWP2000 slow init.
    /// Read/Write timeoutMs is converted to max attempts (FTDI driver does not use milliseconds directly).
    /// DTR=1 for self-powered. SetLatency and SetBitMode supported.
    /// </summary>
    public class FtdiCommunicationDevice : ICommunicationDevice
    {
        private readonly FTDI _ftdi;
        private FtdiDeviceInfo _deviceInfo;

        public FtdiCommunicationDevice()
        {
            _ftdi = new FTDI(null, null); // No error/warning delegates at this level
        }

        /// <summary>
        /// Helper method to check if device is open, returns false if not open
        /// </summary>
        private bool CheckIsOpen()
        {
            if (!_ftdi.IsOpen)
            {
                return false;
            }
            return true;
        }

        public bool IsOpen
        {
            get { return _ftdi.IsOpen; }
        }

        public DeviceType Type
        {
            get { return DeviceType.FTDI; }
        }

        public string DeviceName
        {
            get { return _deviceInfo?.Description ?? "Unknown FTDI Device"; }
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

            if (!(deviceInfo is FtdiDeviceInfo ftdiDeviceInfo))
            {
                return false;
            }

            if (!FTDI.IsFTD2XXDLLLoaded())
            {
                return false;
            }

            _deviceInfo = ftdiDeviceInfo;

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.OpenByLocation(ftdiDeviceInfo.LocId);
            if (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
            {
                // Reset and purge the device
                status |= _ftdi.ResetDevice();
                status |= _ftdi.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                if (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK)
                {
                    return true;
                }
                else
                {
                    _ftdi.Close();
                    return false;
                }
            }

            return false;
        }

        public void Close()
        {
            if (_ftdi.IsOpen)
            {
                _ftdi.Close();
            }
            _deviceInfo = null;
        }

        public bool Reset()
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.ResetDevice();
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool Purge(PurgeType purgeType)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            uint purgeMask = 0;
            if ((purgeType & PurgeType.RX) != 0)
            {
                purgeMask |= FTDI.FT_PURGE.FT_PURGE_RX;
            }
            if ((purgeType & PurgeType.TX) != 0)
            {
                purgeMask |= FTDI.FT_PURGE.FT_PURGE_TX;
            }

            if (purgeMask == 0)
            {
                return true; // Nothing to purge
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.Purge(purgeMask);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetBaudRate(uint baudRate)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetBaudRate(baudRate);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetDataCharacteristics(DataBits dataBits, StopBits stopBits, Parity parity)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            byte dataBitsValue = (dataBits == DataBits.Bits7) ? FTDI.FT_DATA_BITS.FT_BITS_7 : FTDI.FT_DATA_BITS.FT_BITS_8;
            byte stopBitsValue = (stopBits == StopBits.Bits1) ? FTDI.FT_STOP_BITS.FT_STOP_BITS_1 : FTDI.FT_STOP_BITS.FT_STOP_BITS_2;
            byte parityValue;

            switch (parity)
            {
                case Parity.None:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_NONE;
                    break;
                case Parity.Odd:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_ODD;
                    break;
                case Parity.Even:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_EVEN;
                    break;
                case Parity.Mark:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_MARK;
                    break;
                case Parity.Space:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_SPACE;
                    break;
                default:
                    parityValue = FTDI.FT_PARITY.FT_PARITY_NONE;
                    break;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetDataCharacteristics(dataBitsValue, stopBitsValue, parityValue);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetFlowControl(FlowControl flowControl)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            ushort flowControlValue;
            byte xon = 0;
            byte xoff = 0;

            switch (flowControl)
            {
                case FlowControl.None:
                    flowControlValue = FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE;
                    break;
                case FlowControl.RtsCts:
                    flowControlValue = FTDI.FT_FLOW_CONTROL.FT_FLOW_RTS_CTS;
                    break;
                case FlowControl.DtrDsr:
                    flowControlValue = FTDI.FT_FLOW_CONTROL.FT_FLOW_DTR_DSR;
                    break;
                case FlowControl.XonXoff:
                    flowControlValue = FTDI.FT_FLOW_CONTROL.FT_FLOW_XON_XOFF;
                    xon = 0x11;
                    xoff = 0x13;
                    break;
                default:
                    flowControlValue = FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE;
                    break;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetFlowControl(flowControlValue, xon, xoff);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetTimeouts(uint readTimeoutMs, uint writeTimeoutMs)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetTimeouts(readTimeoutMs, writeTimeoutMs);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetLatency(byte latencyMs)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetLatency(latencyMs);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetDTR(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetDTR(state);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetRTS(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetRTS(state);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetBreak(bool state)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetBreak(state);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool SetBitMode(byte pinMask, BitMode mode)
        {
            if (!CheckIsOpen())
            {
                return false;
            }

            byte modeValue;
            switch (mode)
            {
                case BitMode.Reset:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET;
                    break;
                case BitMode.AsyncBitBang:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_ASYNC_BITBANG;
                    break;
                case BitMode.Mpsse:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE;
                    break;
                case BitMode.SyncBitBang:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_SYNC_BITBANG;
                    break;
                case BitMode.McuHost:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_MCU_HOST;
                    break;
                case BitMode.FastSerial:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_FAST_SERIAL;
                    break;
                case BitMode.CbusBitBang:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_CBUS_BITBANG;
                    break;
                case BitMode.SyncFifo:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_SYNC_FIFO;
                    break;
                default:
                    modeValue = FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET;
                    break;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.SetBitMode(pinMask, modeValue);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool GetBitMode(out byte pinState)
        {
            pinState = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.GetPinStates(ref pinState);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        /// <summary>
        /// Reads from the FTDI device. May return fewer bytes than requested (incomplete read);
        /// FTDI driver uses "attempts" not ms converted from timeoutMs.
        /// </summary>
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

            // Convert timeout to max attempts (approximate - FTDI Read uses attempts, not milliseconds)
            // For now, we'll use a reasonable default attempt count
            // The timeout parameter will be converted approximately: timeoutMs / 50ms per attempt
            uint maxAttempts = Math.Max(1, timeoutMs / 50);

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.Read(buffer, bytesToRead, ref bytesRead, maxAttempts);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
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

            // Convert timeout to max attempts (approximate - FTDI Write uses attempts, not milliseconds)
            // For now, we'll use a reasonable default attempt count
            // The timeout parameter will be converted approximately: timeoutMs / 50ms per attempt
            uint maxAttempts = Math.Max(1, timeoutMs / 50);

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.Write(buffer, bufferLength, ref bytesWritten, maxAttempts);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool GetRxBytesAvailable(ref uint bytesAvailable, uint maxAttempts)
        {
            bytesAvailable = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.GetRxBytesAvailable(ref bytesAvailable, maxAttempts);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        public bool GetTxBytesWaiting(ref uint bytesWaiting)
        {
            bytesWaiting = 0;

            if (!CheckIsOpen())
            {
                return false;
            }

            FTD2XX_NET.FTDI.FT_STATUS status = _ftdi.GetTxBytesWaiting(ref bytesWaiting);
            return (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK);
        }

        /// <summary>
        /// Enumerates all available FTDI devices
        /// </summary>
        public static IEnumerable<DeviceInfo> Enumerate(DisplayStatusMessageDelegate logMessage = null)
        {
            var devices = new List<DeviceInfo>();

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

            return devices;
        }
    }
}
