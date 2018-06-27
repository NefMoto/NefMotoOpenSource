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
	public class AddressRange
	{
		public AddressRange(uint start, uint numBytes)
		{
			StartAddress = start;
			NumBytes = numBytes;
		}

		public uint StartAddress { get; set; }
		public uint NumBytes { get; set; }
	}

    [Serializable]
	public abstract class BaseChecksum
	{
        //for serialization
        public BaseChecksum()
        {
        }

		public void SetMemoryReference(MemoryImage memory)
		{
			mMemory = memory;

            LoadChecksum();
		}

		public abstract bool LoadChecksum();
		public abstract bool UpdateChecksum(bool outputMessage);
		public abstract bool IsCorrect(bool outputMessage);
		public abstract bool CommitChecksum();

		protected bool CalculateChecksumForRange(uint startAddr, uint numBytes, DataUtils.DataType readingType, out uint checksum)
		{
			bool result = false;
			checksum = 0;

			if ((startAddr >= 0) && (numBytes > 0) && (mMemory != null))
			{
				result = true;

				for (uint addr = startAddr; addr < startAddr + numBytes; addr += DataUtils.GetDataTypeSize(readingType))
				{			
					uint temp = 0;

					result &= mMemory.ReadRawIntValueByType(out temp, readingType, addr);
					
					checksum += temp;
				}
			}

			return result;
		}

		protected bool CalculateRollingChecksumForRange(uint startAddr, uint numBytes, uint seedAddr, ref uint checksum)
		{
			bool result = false;			

			if ((startAddr >= 0) && (numBytes > 0) && (mMemory != null) && (numBytes < mMemory.Size) && (seedAddr < mMemory.EndAddress))
			{
				result = true;

				for (uint index = startAddr; index < startAddr + numBytes; index++)
				{	
					uint currentByte = 0;
					result &= mMemory.ReadRawIntValueByType(out currentByte, DataUtils.DataType.UInt8, index);
					
					uint seed = 0;
					result &= mMemory.ReadRawIntValueByType(out seed, DataUtils.DataType.UInt32, seedAddr + ((currentByte ^ (checksum & 0xFF)) << 2));
					
					checksum >>= 8;			
					checksum ^= seed;			
				}				
			}

			return result;
		}

		protected void DisplayStatusMessage(string message, StatusMessageType messageType)
		{
            if (DisplayStatusMessageEvent != null)
			{
                DisplayStatusMessageEvent(message, messageType);
			}
		}

		protected MemoryImage mMemory;

        public event DisplayStatusMessageDelegate DisplayStatusMessageEvent;
	}
}
