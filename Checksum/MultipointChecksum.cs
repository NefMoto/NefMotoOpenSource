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
using System.Diagnostics;

using Shared;

namespace Checksum
{
    [Serializable]
	public class MultipointChecksum : BaseChecksum
	{
		public MultipointChecksum(uint location)
		{
			Location = location;
			mStartAddress = 0;
			mEndAddress = 0;
			mChecksum = 0;
			mInvChecksum = 0;
		}
	
	    //for serialization
        public MultipointChecksum()
            : this(0)
        {

        }

		public static uint GetChecksumBlockSize()
		{
			return DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32) * 4;
		}

		public override bool LoadChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				result = true;

				uint dataTypeSize = DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32);
				uint checksumBlockSize = GetChecksumBlockSize();

				result &= mMemory.ReadRawIntValueByType(out mStartAddress, DataUtils.DataType.UInt32, Location);
				result &= mMemory.ReadRawIntValueByType(out mEndAddress, DataUtils.DataType.UInt32, Location + dataTypeSize);
				result &= mMemory.ReadRawIntValueByType(out mChecksum, DataUtils.DataType.UInt32, Location + (dataTypeSize * 2));
				result &= mMemory.ReadRawIntValueByType(out mInvChecksum, DataUtils.DataType.UInt32, Location + (dataTypeSize * 3));							
			}

			return result;
		}

		public override bool UpdateChecksum(bool outputMessage)
		{
			bool result = CalculateChecksumForRange(mStartAddress, mEndAddress - mStartAddress, DataUtils.DataType.UInt16, out mChecksum);
			mInvChecksum = ~mChecksum;

			bool rangeValid = true;

			if (!result)
			{
				if ((mStartAddress < mMemory.StartAddress) || (mEndAddress >= mMemory.EndAddress))
				{
					result = true;
					rangeValid = false;
				}
			}

			if (result)
			{
				DisplayStatusMessage("Multipoint checksum updated", StatusMessageType.LOG);
			}
			else
			{
				if (rangeValid)
				{
					DisplayStatusMessage("Multipoint checksum failed to update", StatusMessageType.LOG);
				}
				else
				{
					DisplayStatusMessage("Multipoint checksum has invalid address range", StatusMessageType.LOG);
				}
			}

			return result;
		}

		public override bool IsCorrect(bool outputMessage)
		{
			uint calculatedChecksum;
			bool result = CalculateChecksumForRange(mStartAddress, mEndAddress - mStartAddress, DataUtils.DataType.UInt16, out calculatedChecksum) && (calculatedChecksum == mChecksum);

			bool rangeValid = true;

			if (!result)
			{
				//ignore checksums that are in internal ROM
				if ((mStartAddress < mMemory.StartAddress) && (mEndAddress < mMemory.StartAddress))
				{
					result = true;
					rangeValid = false;
				}
			}

			if (outputMessage)
			{
				if (!result)
				{
					if (rangeValid)
					{
						DisplayStatusMessage("Multipoint checksum incorrect.", StatusMessageType.LOG);
					}
					else
					{
						DisplayStatusMessage("Multipoint checksum address range is outside memory image.", StatusMessageType.LOG);
					}
				}
				else
				{
					DisplayStatusMessage("Multipoint checksum OK.", StatusMessageType.LOG);
				}
			}		

			return result;
		}

		public override bool CommitChecksum()
		{
			uint uintSize = DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32);

			bool result = mMemory.WriteRawIntValueByType(mChecksum, DataUtils.DataType.UInt32, Location + (uintSize * 2))
			&& mMemory.WriteRawIntValueByType(mInvChecksum, DataUtils.DataType.UInt32, Location + (uintSize * 3));

			return result;
		}

		public void GetAddresses(out uint startAddress, out uint endAddress)
		{
			startAddress = mStartAddress;
			endAddress = mEndAddress;
		}	

        public uint Location { get; set; }

		private uint mStartAddress;
		private uint mEndAddress;
		private uint mChecksum;
		private uint mInvChecksum;
	}
}
