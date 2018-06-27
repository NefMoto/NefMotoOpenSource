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
using System.Text;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace Shared
{
	public class MemoryImage
	{
		public MemoryImage()
		{
            Reset();
		}

		public MemoryImage(MemoryImage other)
		{
			CopyFrom(other);
		}

		public MemoryImage(uint numBytes, uint baseAddress)
		{
			RawData = new byte[numBytes];
			StartAddress = baseAddress;
		}

		public MemoryImage(byte[] data, uint baseAddress)
		{
			RawData = data;
			StartAddress = baseAddress;
		}

        public void Reset()
        {
            StartAddress = 0;
            RawData = null;
        }

		public uint StartAddress { get; set; }

		public uint EndAddress
		{
			get
			{
				return StartAddress + Size;
			}
		}

        public uint Size
        {
            get
            {
                if (RawData != null)
                {
                    return (uint)RawData.Length;
                }
                else
                {
                    return 0;
                }
            }
        }

        public byte[] RawData
        {
            get { return mRawData; }
            set { mRawData = value; }
        }
        private byte[] mRawData;		

		public bool WriteRawFloatValueByType(double value, DataUtils.DataType destType, uint address)
		{
			bool result = false;

			if (address >= StartAddress)
			{
				result = DataUtils.WriteRawFloatValueByType(value, destType, RawData, address - StartAddress);				
			}

			return result;
		}

		public bool ReadRawFloatValueByType(out double value, DataUtils.DataType sourceType, uint address)
		{
			bool result = false;
			value = 0;

			if (address >= StartAddress)
			{
				result = DataUtils.ReadRawFloatValueByType(out value, sourceType, RawData, address - StartAddress);
			}

			return result;
		}

        public bool WriteRawIntValueByType(uint value, DataUtils.DataType destType, uint address)
		{
			bool result = false;

			if (address >= StartAddress)
			{
				result = DataUtils.WriteRawIntValueByType(value, destType, RawData, address - StartAddress);				
			}

			return result;
		}

		public bool ReadRawIntValueByType(out uint value, DataUtils.DataType sourceType, uint address)
		{
			bool result = false;
			value = 0;

			if (address >= StartAddress)
			{
				result = DataUtils.ReadRawIntValueByType(out value, sourceType, RawData, address - StartAddress);
			}

			return result;
		}

		public void CopyFrom(MemoryImage otherImage)
		{
			StartAddress = otherImage.StartAddress;

			if ((otherImage.RawData != null) && (otherImage.RawData.Length > 0))
			{
				if (RawData == null)
				{
					RawData = new byte[otherImage.RawData.Length];
				}

				if (RawData.Length != otherImage.RawData.Length)
				{
					Array.Resize<byte>(ref mRawData, otherImage.RawData.Length);
				}

				Buffer.BlockCopy(otherImage.RawData, 0, RawData, 0, RawData.Length);
			}
			else
			{
				RawData = null;
			}
		}		

		public bool SaveToFile(string fileName)
		{
			bool retval = false;

			try
			{
				if ((RawData != null) && (RawData.Length > 0))
				{
					File.WriteAllBytes(fileName, RawData);
					retval = true;
				}
			}
			catch
			{
				retval = false;
			}

			return retval;
		}
	}

	public class TaggedMemoryImage : MemoryImage
	{
		public TaggedMemoryImage(byte[] data, uint baseAddress, object userTag)
			: base(data, baseAddress)
		{
			UserTag = userTag;
		}

		public TaggedMemoryImage(uint numBytes, uint baseAddress, object userTag)
			: base(numBytes, baseAddress)
		{
			UserTag = userTag;
		}

		public object UserTag
		{
			get;
			set;
		}
	}
}
