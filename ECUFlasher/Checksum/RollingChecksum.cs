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
	public class RollingChecksums : BaseChecksum
	{
		public RollingChecksums(uint seedAddress)
		{
			mSeedAddress = seedAddress;

			mUseChaining = false;
			mAddressRanges = new List<IEnumerable<AddressRange>>();
			mRangeChecksumAddresses = new List<uint>();
			mRangeChecksums = new List<uint>();
		}

		public void EnableInitRange(uint startAddress, uint numBytes)
		{
			mUseChaining = true;
			mInitAddressRange = new AddressRange(startAddress, numBytes);
		}		

		public void AddAddressRange(IEnumerable<AddressRange> addressRanges, uint checksumAddress)
		{
			mAddressRanges.Add(addressRanges);
			mRangeChecksumAddresses.Add(checksumAddress);
		}

		public override bool LoadChecksum()
		{
			bool result = true;
			
			mRangeChecksums.Clear();

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				foreach (var checksumAddress in mRangeChecksumAddresses)
				{
					uint checksum;
					result &= mMemory.ReadRawIntValueByType(out checksum, DataUtils.DataType.UInt32, checksumAddress);
					mRangeChecksums.Add(~checksum);//checksums are stored inverted
				}				
			}

			return result;
		}
		public override bool UpdateChecksum(bool outputMessage)
		{
			bool result = true;
			
			mRangeChecksums.Clear();

			uint calculatedChecksum = 0xFFFFFFFF;			

			if (mUseChaining)
			{
				result &= CalculateRollingChecksumForRange(mInitAddressRange.StartAddress, mInitAddressRange.NumBytes, mSeedAddress, ref calculatedChecksum);
			}

			foreach (var ranges in mAddressRanges)
			{
				if (!mUseChaining)
				{
					calculatedChecksum = 0xFFFFFFFF;
				}

				foreach (var range in ranges)
				{
					result &= CalculateRollingChecksumForRange(range.StartAddress, range.NumBytes, mSeedAddress, ref calculatedChecksum);									
				}

				mRangeChecksums.Add(calculatedChecksum);	

				if (!result)
				{
					return false;
				}
			}

			return result;
		}
		public override bool IsCorrect(bool outputMessage)
		{
			bool result = true;

			uint calculatedChecksum = 0xFFFFFFFF;

			if (mUseChaining)
			{
				result &= CalculateRollingChecksumForRange(mInitAddressRange.StartAddress, mInitAddressRange.NumBytes, mSeedAddress, ref calculatedChecksum);
			}

			if (mAddressRanges.Count() != mRangeChecksums.Count())
			{
				return false;
			}

			var checksumIter = mRangeChecksums.GetEnumerator();

			foreach (var ranges in mAddressRanges)
			{
				checksumIter.MoveNext();

				if (!mUseChaining)
				{
					calculatedChecksum = 0xFFFFFFFF;
				}				

				foreach (var range in ranges)
				{
					result &= CalculateRollingChecksumForRange(range.StartAddress, range.NumBytes, mSeedAddress, ref calculatedChecksum);					
				}
				
				if (!result || (calculatedChecksum != checksumIter.Current))
				{
					return false;
				}				
			}
			
			return result;
		}
		public override bool CommitChecksum()
		{
			bool result = false;

			if ((mMemory != null) && (mMemory.Size > 0))
			{
				if (mRangeChecksums.Count() != mRangeChecksumAddresses.Count())
				{
					return false;
				}

				var checksumIter = mRangeChecksums.GetEnumerator();

				result = true;

				foreach (var checksumAddress in mRangeChecksumAddresses)
				{
					checksumIter.MoveNext();

					uint checksum = ~checksumIter.Current;//checksums are stored inverted						
					result &= mMemory.WriteRawIntValueByType(checksum, DataUtils.DataType.UInt32, checksumAddress);					
				}
			}

			return result;
		}		

		private uint mSeedAddress;

		private bool mUseChaining;
		private AddressRange mInitAddressRange;

		private List<IEnumerable<AddressRange>> mAddressRanges;
		private List<uint> mRangeChecksumAddresses;
		private List<uint> mRangeChecksums;
	}
}
