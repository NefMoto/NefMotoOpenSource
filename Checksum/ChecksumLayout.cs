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
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Checksum
{
    [Serializable]	
    public class ChecksumLayout : IDataErrorInfo
    {
        public ChecksumLayout()
        {
            mPropertyErrors = new Dictionary<string, string>();

            Validate();
        }

        public void Reset()
        {
            BaseAddress = 0;

            MainChecksumAddress = 0;
            MainChecksumBlockAddress = 0;

            MultipointChecksumBlocksStartAddress = 0;
            NumMultipointChecksumBlocks = 0;

            RollingChecksumSeedAddress = 0;
            RollingChecksumBlocksStartAddress = 0;
            RollingChecksumAddressRanges.Clear();

            Validate();
        }

        public bool Validate()
        {
            this["BaseAddress"] = (BaseAddress % 2 != 0) ? "BaseAddress must be an even number" : null;

            this["MainChecksumAddress"] = (MainChecksumAddress % 2 != 0) ? "MainChecksumAddress must be an even number" : null;
            this["MainChecksumBlockAddress"] = (MainChecksumBlockAddress % 2 != 0) ? "MainChecksumBlockAddress must be an even number" : null;

            this["MultipointChecksumBlocksStartAddress"] = (MultipointChecksumBlocksStartAddress % 2 != 0) ? "MultipointChecksumBlocksStartAddress must be an even number" : null;
            this["NumMultipointChecksumBlocks"] = (NumMultipointChecksumBlocks > 0) ? "NumMultipointChecksumBlocks must be greater than zero" : null;

            this["RollingChecksumSeedAddress"] = (RollingChecksumSeedAddress % 2 != 0) ? "RollingChecksumSeedAddress must be an even number" : null;
            this["RollingChecksumBlocksStartAddress"] = (RollingChecksumBlocksStartAddress % 2 != 0) ? "RollingChecksumBlocksStartAddress must be an even number" : null;
            this["RollingChecksumAddressRanges"] = (RollingChecksumAddressRanges.Count > 0) ? "RollingChecksumAddressRanges must have at least one entry" : null;
            
            return (Error == null);
        }

        public string Error
        {
            get
            {
                string error = null;

                foreach (string propertyName in mPropertyErrors.Keys)
                {
                    error = mPropertyErrors[propertyName];

                    if (error != null)
                    {
                        break;
                    }
                }

                return error;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null;

                if (mPropertyErrors.ContainsKey(columnName))
                {
                    error = mPropertyErrors[columnName];
                }

                return error;
            }

            private set
            {
                if (!mPropertyErrors.ContainsKey(columnName) || (value != mPropertyErrors[columnName]))
                {
                    mPropertyErrors[columnName] = value;
                }
            }
        }

        [NonSerialized]
        private Dictionary<string, string> mPropertyErrors;

        public uint BaseAddress { get; set; }

        public uint MainChecksumAddress { get; set; }
        public uint MainChecksumBlockAddress { get; set; }

        public uint MultipointChecksumBlocksStartAddress { get; set; }
        public uint NumMultipointChecksumBlocks { get; set; }

        [Serializable]
        public struct RollingChecksumAddressRange
        {
            public uint ChecksumStartAddress { get; set; }
            public uint ChecksumEndAddress { get; set; }
        }

        public uint RollingChecksumSeedAddress { get; set; }
        public uint RollingChecksumBlocksStartAddress { get; set; }
        public List<RollingChecksumAddressRange> RollingChecksumAddressRanges { get; set; }        
    }
}
