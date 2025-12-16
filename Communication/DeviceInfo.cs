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
    /// Base class for device information used to identify and open communication devices
    /// </summary>
    public abstract class DeviceInfo
    {
        /// <summary>
        /// Gets the device description/name
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the device serial number
        /// </summary>
        public abstract string SerialNumber { get; }

        /// <summary>
        /// Gets a unique device identifier
        /// </summary>
        public abstract string DeviceID { get; }

        /// <summary>
        /// Gets the device type
        /// </summary>
        public abstract DeviceType Type { get; }

        /// <summary>
        /// Gets a user-friendly display name for the device
        /// </summary>
        public virtual string DisplayName
        {
            get
            {
                return $"{Type}: {Description}";
            }
        }

        /// <summary>
        /// Gets the device description (for UI binding compatibility)
        /// </summary>
        public virtual string DescriptionProp
        {
            get { return Description; }
        }

        /// <summary>
        /// Gets the device ID (for UI binding compatibility)
        /// Base implementation returns DeviceID as string, but derived classes may override to return numeric ID
        /// </summary>
        public virtual object IDProp
        {
            get { return DeviceID; }
        }
    }

    /// <summary>
    /// Device information for FTDI devices
    /// </summary>
    public class FtdiDeviceInfo : DeviceInfo
    {
        private readonly FTD2XX_NET.FTDI.FT_DEVICE_INFO_NODE _ftdiNode;
        private readonly uint _chipID;

        public FtdiDeviceInfo(FTD2XX_NET.FTDI.FT_DEVICE_INFO_NODE ftdiNode, uint chipID = 0)
        {
            _ftdiNode = ftdiNode ?? throw new ArgumentNullException(nameof(ftdiNode));
            _chipID = chipID;
        }

        public override string Description
        {
            get { return _ftdiNode.Description ?? "Unknown FTDI Device"; }
        }

        public override string SerialNumber
        {
            get { return _ftdiNode.SerialNumber ?? string.Empty; }
        }

        public override string DeviceID
        {
            get { return _ftdiNode.ID.ToString("X8"); }
        }

        public override DeviceType Type
        {
            get { return DeviceType.FTDI; }
        }

        /// <summary>
        /// Gets the FTDI location ID (used for opening the device)
        /// </summary>
        public uint LocId
        {
            get { return _ftdiNode.LocId; }
        }

        /// <summary>
        /// Gets the FTDI chip ID (if available)
        /// </summary>
        public uint ChipID
        {
            get { return _chipID; }
        }

        /// <summary>
        /// Gets the underlying FTDI device info node
        /// </summary>
        public FTD2XX_NET.FTDI.FT_DEVICE_INFO_NODE FtdiNode
        {
            get { return _ftdiNode; }
        }

        /// <summary>
        /// Gets the device ID as uint (for UI binding compatibility with old ApplicationShared.FTDIDeviceInfo)
        /// </summary>
        public override object IDProp
        {
            get { return _ftdiNode.ID; }
        }

        public override bool Equals(object obj)
        {
            if (obj is FtdiDeviceInfo other)
            {
                return _ftdiNode.LocId == other._ftdiNode.LocId;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return _ftdiNode.LocId.GetHashCode();
        }
    }

    /// <summary>
    /// Device information for CH340 devices
    /// </summary>
    public class Ch340DeviceInfo : DeviceInfo
    {
        private readonly string _portName;
        private readonly string _description;
        private readonly string _serialNumber;
        private readonly string _deviceID;

        public Ch340DeviceInfo(string portName, string description = null, string serialNumber = null, string deviceID = null)
        {
            _portName = portName ?? throw new ArgumentNullException(nameof(portName));
            _description = description ?? $"CH340 Device ({portName})";
            _serialNumber = serialNumber ?? string.Empty;
            _deviceID = deviceID ?? portName;
        }

        public override string Description
        {
            get { return _description; }
        }

        public override string SerialNumber
        {
            get { return _serialNumber; }
        }

        public override string DeviceID
        {
            get { return _deviceID; }
        }

        public override DeviceType Type
        {
            get { return DeviceType.CH340; }
        }

        /// <summary>
        /// Gets the COM port name (e.g., "COM3")
        /// </summary>
        public string PortName
        {
            get { return _portName; }
        }

        /// <summary>
        /// Gets the device ID as string (for UI binding compatibility)
        /// </summary>
        public override object IDProp
        {
            get { return _deviceID; }
        }

        public override bool Equals(object obj)
        {
            if (obj is Ch340DeviceInfo other)
            {
                return _portName == other._portName;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return _portName.GetHashCode();
        }
    }
}
