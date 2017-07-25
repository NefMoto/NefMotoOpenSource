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
using System.Linq;

using Shared;

namespace Checksum
{
	[Serializable]
	public class MultiRangeChecksum : BaseChecksum
	{
		public MultiRangeChecksum(uint checksumLocation)
		{			
			ChecksumLocation = checksumLocation;		
			mChecksum = 0;
			mInverseChecksum = 0;

			mAddressRanges = new List<AddressRange>();
		}

		//for serialization
		public MultiRangeChecksum()
			: this(0)
		{
		}

		public void AddRange(AddressRange range)
		{
			mAddressRanges.Add(range);
		}

		public override bool LoadChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				result = true;
				uint dataTypeSize = DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32);

				result &= mMemory.ReadRawIntValueByType(out mChecksum, DataUtils.DataType.UInt32, ChecksumLocation);
				result &= mMemory.ReadRawIntValueByType(out mInverseChecksum, DataUtils.DataType.UInt32, ChecksumLocation + DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32));
			}

			return result;
		}

		public override bool UpdateChecksum(bool outputMessage)
		{
			mChecksum = 0;
			bool failed = false;

			foreach (var range in mAddressRanges)
			{
				uint newChecksum;
				if (!CalculateChecksumForRange(range.StartAddress, range.NumBytes, DataUtils.DataType.UInt8, out newChecksum))
				{
					failed = true;
					break;
				}

				mChecksum += newChecksum;
			}

			mInverseChecksum = ~mChecksum;

			bool result = !failed;

			if (result)
			{
				DisplayStatusMessage("Multi range checksum updated", StatusMessageType.LOG);
			}
			else
			{
				DisplayStatusMessage("Multi range checksum failed to update", StatusMessageType.LOG);
			}

			return result;
		}

		public override bool IsCorrect(bool outputMessage)
		{
			uint calcChecksum = 0;
			bool failed = false;

			foreach (var range in mAddressRanges)
			{
				uint newChecksum;
				if (!CalculateChecksumForRange(range.StartAddress, range.NumBytes, DataUtils.DataType.UInt8, out newChecksum))
				{
					failed = true;
					break;
				}

				calcChecksum += newChecksum;
			}

			bool result = !failed && (calcChecksum == mChecksum) && (~calcChecksum == mInverseChecksum);

			if (outputMessage)
			{
				if (!result)
				{
					DisplayStatusMessage("Multi range checksum incorrect", StatusMessageType.LOG);
				}
				else
				{
					DisplayStatusMessage("Multi range checksum OK", StatusMessageType.LOG);
				}
			}

			return result;
		}

		public override bool CommitChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				result = true;

				result &= mMemory.WriteRawIntValueByType(mChecksum, DataUtils.DataType.UInt32, ChecksumLocation);
				result &= mMemory.WriteRawIntValueByType(mInverseChecksum, DataUtils.DataType.UInt32, ChecksumLocation + DataUtils.GetDataTypeSize(DataUtils.DataType.UInt32));
			}

			return result;
		}
		
		public uint ChecksumLocation { get; set; }
		
		protected List<AddressRange> mAddressRanges;		
		protected uint mChecksum;
		protected uint mInverseChecksum;
	}
}
