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
	public class MainChecksum : BaseChecksum
	{
		public MainChecksum(uint addressLocation, uint checksumLocation, uint numRanges)
		{
			AddressLocation = addressLocation;
			ChecksumLocation = checksumLocation;
			NumRanges = numRanges;
			
			mChecksum = 0;
			mInvChecksum = 0;
		}

        //for serialization
        public MainChecksum()
            : this(0, 0, 0)
        {
        }
				
		public override bool LoadChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				result = true;
				uint dataTypeSize = DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32);

				result &= mMemory.ReadRawIntValueByType(out mChecksum, DataUtils.DataType.UInt32, ChecksumLocation);
				result &= mMemory.ReadRawIntValueByType(out mInvChecksum, DataUtils.DataType.UInt32, ChecksumLocation + dataTypeSize);

				for (uint x = 0; x < NumRanges; x++)
				{
					uint currentAddress = AddressLocation + (dataTypeSize * 2 * x);

					result &= mMemory.ReadRawIntValueByType(out mStartAddresses[x], DataUtils.DataType.UInt32, currentAddress);
					result &= mMemory.ReadRawIntValueByType(out mEndAddresses[x], DataUtils.DataType.UInt32, currentAddress + dataTypeSize);					
				}
			}

			return result;
		}

		public override bool UpdateChecksum(bool outputMessage)
		{
			mChecksum = 0;
			bool failed = false;

			for (uint x = 0; x < NumRanges; x++)
			{
				uint newChecksum;
				if (!CalculateChecksumForRange(mStartAddresses[x], mEndAddresses[x] - mStartAddresses[x], DataUtils.DataType.UInt16, out newChecksum))
				{
					failed = true;
					break;
				}

				mChecksum += newChecksum;
			}

			mInvChecksum = ~mChecksum;

			bool result = !failed;

			if (result)
			{
				DisplayStatusMessage("Main checksum updated", StatusMessageType.LOG);
			}
			else
			{
				DisplayStatusMessage("Main checksum failed to update", StatusMessageType.LOG);
			}
			
			return result;
		}

		public override bool IsCorrect(bool outputMessage)
		{
			uint calcChecksum = 0;
			bool failed = false;

			for (uint x = 0; x < NumRanges; x++)
			{
				uint newChecksum;
				if (!CalculateChecksumForRange(mStartAddresses[x], mEndAddresses[x] - mStartAddresses[x], DataUtils.DataType.UInt16, out newChecksum))
				{
					failed = true;
					break;
				}

				calcChecksum += newChecksum;
			}

			bool result = !failed && (calcChecksum == mChecksum);

			if (outputMessage)
			{
				if (!result)
				{
					DisplayStatusMessage("Main checksum incorrect", StatusMessageType.LOG);
				}
				else
				{
					DisplayStatusMessage("Main checksum OK", StatusMessageType.LOG);
				}
			}			

			return result;
		}

		public override bool CommitChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				result = mMemory.WriteRawIntValueByType(mChecksum, DataUtils.DataType.UInt32, ChecksumLocation)
				&& mMemory.WriteRawIntValueByType(mInvChecksum, DataUtils.DataType.UInt32, ChecksumLocation + DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32));
			}

			return result;
		}				

		public uint AddressLocation { get; set; }
        public uint ChecksumLocation { get; set; }
		public uint NumRanges 
		{
			get
			{
				return _NumRanges;
			}
			set
			{
				if (value != _NumRanges)
				{
					_NumRanges = value;

					if (_NumRanges > 0)
					{
						mStartAddresses = new uint[_NumRanges];
						mEndAddresses = new uint[_NumRanges];
					}
					else
					{
						mStartAddresses = null;
						mEndAddresses = null;
					}
				}
			}
		}
		private uint _NumRanges;

		protected uint[] mStartAddresses;
		protected uint[] mEndAddresses;		
		protected uint mChecksum;
		protected uint mInvChecksum;
	}
}
