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

namespace Communication
{
    /// <summary>
    /// Purge options for clearing communication buffers
    /// </summary>
    [Flags]
    public enum PurgeType
    {
        None = 0,
        RX = 1,  // Clear receive buffer
        TX = 2   // Clear transmit buffer
    }

    /// <summary>
    /// Data bits configuration for serial communication
    /// </summary>
    public enum DataBits
    {
        Bits7 = 7,
        Bits8 = 8
    }

    /// <summary>
    /// Stop bits configuration for serial communication
    /// </summary>
    public enum StopBits
    {
        Bits1 = 1,
        Bits2 = 2
    }

    /// <summary>
    /// Parity configuration for serial communication
    /// </summary>
    public enum Parity
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }

    /// <summary>
    /// Flow control configuration for serial communication
    /// </summary>
    public enum FlowControl
    {
        None,
        RtsCts,
        DtrDsr,
        XonXoff
    }

    /// <summary>
    /// Bit mode configuration for bit-bang mode (FTDI-specific, optional for other devices)
    /// </summary>
    public enum BitMode
    {
        Reset = 0x00,
        AsyncBitBang = 0x01,
        Mpsse = 0x02,
        SyncBitBang = 0x04,
        McuHost = 0x08,
        FastSerial = 0x10,
        CbusBitBang = 0x20,
        SyncFifo = 0x40
    }

    /// <summary>
    /// Device type enumeration
    /// </summary>
    public enum DeviceType
    {
        FTDI,
        CH340
    }

    /// <summary>
    /// Interface for hardware abstraction layer (HAL) for communication devices.
    /// Provides a unified interface for different USB-to-serial adapter types.
    /// </summary>
    public interface ICommunicationDevice
    {
        /// <summary>
        /// Gets whether the device is currently open/connected
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Gets the device type (FTDI, CH340, etc.)
        /// </summary>
        DeviceType Type { get; }

        /// <summary>
        /// Gets the device name/description
        /// </summary>
        string DeviceName { get; }

        /// <summary>
        /// Gets the device serial number
        /// </summary>
        string SerialNumber { get; }

        // Device Management
        /// <summary>
        /// Opens the device using the provided device info
        /// </summary>
        /// <param name="deviceInfo">Device information to open</param>
        /// <returns>True if opened successfully</returns>
        bool Open(DeviceInfo deviceInfo);

        /// <summary>
        /// Closes the device connection
        /// </summary>
        void Close();

        /// <summary>
        /// Resets the device
        /// </summary>
        /// <returns>True if reset successful</returns>
        bool Reset();

        /// <summary>
        /// Purges the specified buffers
        /// </summary>
        /// <param name="purgeType">Which buffers to purge (RX, TX, or both)</param>
        /// <returns>True if purge successful</returns>
        bool Purge(PurgeType purgeType);

        // Serial Configuration
        /// <summary>
        /// Sets the baud rate
        /// </summary>
        /// <param name="baudRate">Baud rate in bits per second</param>
        /// <returns>True if set successfully</returns>
        bool SetBaudRate(uint baudRate);

        /// <summary>
        /// Sets data characteristics (data bits, stop bits, parity)
        /// </summary>
        /// <param name="dataBits">Number of data bits</param>
        /// <param name="stopBits">Number of stop bits</param>
        /// <param name="parity">Parity setting</param>
        /// <returns>True if set successfully</returns>
        bool SetDataCharacteristics(DataBits dataBits, StopBits stopBits, Parity parity);

        /// <summary>
        /// Sets flow control
        /// </summary>
        /// <param name="flowControl">Flow control type</param>
        /// <returns>True if set successfully</returns>
        bool SetFlowControl(FlowControl flowControl);

        /// <summary>
        /// Sets read and write timeouts
        /// </summary>
        /// <param name="readTimeoutMs">Read timeout in milliseconds</param>
        /// <param name="writeTimeoutMs">Write timeout in milliseconds</param>
        /// <returns>True if set successfully</returns>
        bool SetTimeouts(uint readTimeoutMs, uint writeTimeoutMs);

        /// <summary>
        /// Sets USB latency timer (device-specific, may not be supported by all devices)
        /// </summary>
        /// <param name="latencyMs">Latency in milliseconds</param>
        /// <returns>True if set successfully, false if not supported</returns>
        bool SetLatency(byte latencyMs);

        // Control Lines
        /// <summary>
        /// Sets DTR (Data Terminal Ready) line state
        /// </summary>
        /// <param name="state">True = high/asserted, False = low/deasserted</param>
        /// <returns>True if set successfully</returns>
        bool SetDTR(bool state);

        /// <summary>
        /// Sets RTS (Request To Send) line state
        /// </summary>
        /// <param name="state">True = high/asserted, False = low/deasserted</param>
        /// <returns>True if set successfully</returns>
        bool SetRTS(bool state);

        /// <summary>
        /// Sets break signal state
        /// </summary>
        /// <param name="state">True = break asserted (line pulled low), False = break cleared</param>
        /// <returns>True if set successfully</returns>
        bool SetBreak(bool state);

        // Bit-Bang Mode (Optional - may not be supported by all devices)
        /// <summary>
        /// Sets bit-bang mode (FTDI-specific feature, may not be supported by all devices)
        /// </summary>
        /// <param name="pinMask">Pin mask (which pins to use for bit-bang)</param>
        /// <param name="mode">Bit mode to use</param>
        /// <returns>True if set successfully, false if not supported</returns>
        bool SetBitMode(byte pinMask, BitMode mode);

        /// <summary>
        /// Gets current bit mode state (may not be supported by all devices)
        /// </summary>
        /// <param name="pinState">Output parameter for current pin state</param>
        /// <returns>True if retrieved successfully, false if not supported</returns>
        bool GetBitMode(out byte pinState);

        // I/O Operations
        /// <summary>
        /// Reads data from the device
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="bytesToRead">Number of bytes to read</param>
        /// <param name="bytesRead">Output parameter for actual number of bytes read</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>True if read successful</returns>
        bool Read(byte[] buffer, uint bytesToRead, ref uint bytesRead, uint timeoutMs);

        /// <summary>
        /// Writes data to the device
        /// </summary>
        /// <param name="buffer">Buffer containing data to write</param>
        /// <param name="bufferLength">Number of bytes to write</param>
        /// <param name="bytesWritten">Output parameter for actual number of bytes written</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>True if write successful</returns>
        bool Write(byte[] buffer, int bufferLength, ref uint bytesWritten, uint timeoutMs);

        // Queue Status Operations
        /// <summary>
        /// Gets the number of bytes available in the receive queue
        /// </summary>
        /// <param name="bytesAvailable">Output parameter for number of bytes available</param>
        /// <param name="maxAttempts">Maximum number of attempts if IO error occurs</param>
        /// <returns>True if operation successful</returns>
        bool GetRxBytesAvailable(ref uint bytesAvailable, uint maxAttempts);

        /// <summary>
        /// Gets the number of bytes waiting in the transmit buffer
        /// </summary>
        /// <param name="bytesWaiting">Output parameter for number of bytes waiting</param>
        /// <returns>True if operation successful</returns>
        bool GetTxBytesWaiting(ref uint bytesWaiting);
    }
}
