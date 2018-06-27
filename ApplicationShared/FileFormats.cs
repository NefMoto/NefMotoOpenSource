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
using System.Linq;
using System.Text;
using System.Xml.Serialization;

using Shared;
using Communication;
using FTD2XX_NET;

namespace ApplicationShared
{
	[Serializable]
	[XmlType("FTDIDeviceInfo")]
	public class FTDIDeviceInfo : FTDI.FT_DEVICE_INFO_NODE
	{
		public FTDIDeviceInfo()
		{
		}

		public FTDIDeviceInfo(FTDI.FT_DEVICE_INFO_NODE node, uint index, uint chipID)
		{
			Description = node.Description;
			Flags = node.Flags;
			ftHandle = IntPtr.Zero;
			ID = node.ID;
			LocId = node.LocId;
			SerialNumber = node.SerialNumber;
			Type = node.Type;

			Index = index;
			ChipID = chipID;
		}

		public string DescriptionProp { get { return Description; } }
		public uint IDProp { get { return ID; } }
		public uint FlagsProp { get { return Flags; } }
		public string SerialNumberProp { get { return SerialNumber; } }
		public FTDI.FT_DEVICE TypeProp { get { return Type; } }

		public uint Index { get; set; }
		public uint ChipID { get; set; }

		//public override bool Equals(object obj)
		//{
		//    if (obj is FTDIDeviceInfo)
		//    {
		//        var otherObj = obj as FTDIDeviceInfo;

		//        if (otherObj.Description != Description)
		//        {
		//            return false;
		//        }

		//        if (otherObj.Flags != Flags)
		//        {
		//            return false;
		//        }

		//        if (otherObj.ftHandle != ftHandle)
		//        {
		//            return false;
		//        }

		//        if (otherObj.ID != ID)
		//        {
		//            return false;
		//        }

		//        if (otherObj.LocId != LocId)
		//        {
		//            return false;
		//        }

		//        if (otherObj.SerialNumber != SerialNumber)
		//        {
		//            return false;
		//        }

		//        if (otherObj.Type != Type)
		//        {
		//            return false;
		//        }

		//        if (otherObj.Index != Index)
		//        {
		//            return false;
		//        }

		//        if (otherObj.ChipID != ChipID)
		//        {
		//            return false;
		//        }

		//        return true;
		//    }

		//    return base.Equals(obj);
		//}
	}

	public class BaseFile
	{
		[XmlAttribute]
		public string Version
		{
			get { return _Version; }
			set { _Version = value; }
		}
		private string _Version = new Version(1, 0, 0, 0).ToString();
	}

	[XmlType("FTDIDevicesFile")]
	public class FTDIDevicesFile : BaseFile
	{
		public const string SHORT_EXT = ".xml";
		public const string EXT = ".FTDIDevices" + SHORT_EXT;
		public static readonly string FILTER = "FTDI Devices (*" + EXT + ")|*" + EXT;

		[XmlArray]
		[XmlArrayItem(ElementName = "Devices", Type = typeof(FTDIDeviceInfo))]
		public List<FTDIDeviceInfo> Devices
		{
			get
			{
				if (_Devices == null)
				{
					_Devices = new List<FTDIDeviceInfo>();
				}

				return _Devices;
			}
			set { _Devices = value; }
		}
		private List<FTDIDeviceInfo> _Devices;
	}

	[XmlType("DTCsFile")]
	public class DTCsFile : BaseFile
	{
		public const string SHORT_EXT = ".xml";
		public const string EXT = ".DTCs" + SHORT_EXT;
		public static readonly string FILTER = "DTCs (*" + EXT + ")|*" + EXT;

		[XmlArray]
		[XmlArrayItem(ElementName = "DTC", Type = typeof(KWP2000DTCInfo))]
		public List<KWP2000DTCInfo> DTCs
		{
			get
			{
				if (_DTCs == null)
				{
					_DTCs = new List<KWP2000DTCInfo>();
				}

				return _DTCs;
			}
			set { _DTCs = value; }
		}
		private List<KWP2000DTCInfo> _DTCs;
	}

	[XmlType("IdentificationFile")]
	public class IdentificationFile : BaseFile
	{
		public const string SHORT_EXT = ".xml";
		public const string EXT = ".Info" + SHORT_EXT;
		public static readonly string FILTER = "ECU Info (*" + EXT + ")|*" + EXT;

		[XmlArray]
		[XmlArrayItem(ElementName = "IdentificationEntry", Type = typeof(KWP2000IdentificationOptionValue))]
		public List<KWP2000IdentificationOptionValue> IdentificationValues
		{
			get
			{
				if (_IdentificationValues == null)
				{
					_IdentificationValues = new List<KWP2000IdentificationOptionValue>();
				}

				return _IdentificationValues;
			}
			set { _IdentificationValues = value; }
		}
		private List<KWP2000IdentificationOptionValue> _IdentificationValues;
	}

	[XmlType("EntireECUFile")]
	public class EntireECUFile : BaseFile
	{
		public const string SHORT_EXT = ".xml";
		public const string EXT = ".EntireECU" + SHORT_EXT;
		public static readonly string FILTER = "Entire ECU Files (*" + EXT + ")|*" + EXT;

		[XmlArray]
		[XmlArrayItem(ElementName = "IdentificationEntry", Type = typeof(KWP2000IdentificationOptionValue))]
		public List<KWP2000IdentificationOptionValue> IdentificationValues
		{
			get
			{
				if (_IdentificationValues == null)
				{
					_IdentificationValues = new List<KWP2000IdentificationOptionValue>();
				}

				return _IdentificationValues;
			}
			set { _IdentificationValues = value; }
		}
		private List<KWP2000IdentificationOptionValue> _IdentificationValues;

		[XmlArray]
		[XmlArrayItem(ElementName = "DTC", Type = typeof(KWP2000DTCInfo))]
		public List<KWP2000DTCInfo> DTCs
		{
			get
			{
				if (_DTCs == null)
				{
					_DTCs = new List<KWP2000DTCInfo>();
				}

				return _DTCs;
			}
			set { _DTCs = value; }
		}
		private List<KWP2000DTCInfo> _DTCs;

		public MemoryImage FlashMemory;
		public MemoryLayout FlashLayout;
	}

	[XmlType("EntireECUAndDevicesFile")]
	public class EntireECUAndDevicesFile : EntireECUFile
	{
		public new const string SHORT_EXT = ".xml";
		public new const string EXT = ".EntireECUAndDevices" + SHORT_EXT;
		public static readonly new string FILTER = "Entire ECU And Devices Files (*" + EXT + ")|*" + EXT;

		[XmlArray]
		[XmlArrayItem(ElementName = "FTDIDevices", Type = typeof(FTDIDeviceInfo))]
		public List<FTDIDeviceInfo> FTDIDevices
		{
			get
			{
				if (_FTDIDevices == null)
				{
					_FTDIDevices = new List<FTDIDeviceInfo>();
				}

				return _FTDIDevices;
			}
			set { _FTDIDevices = value; }
		}
		private List<FTDIDeviceInfo> _FTDIDevices;
	}
}
