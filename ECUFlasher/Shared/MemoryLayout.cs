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
using System.Runtime.Serialization;
//using System.Xml.Serialization;
using System.IO;
using System.Resources;
using System.Reflection;
using System.Diagnostics;


namespace Shared
{
	[Serializable]
	public class MemoryLayout : IDataErrorInfo
	{
		public const string MEMORY_LAYOUT_FILE_SHORT_EXT = ".xml";
		public const string MEMORY_LAYOUT_FILE_EXT = ".MemoryLayout" + MEMORY_LAYOUT_FILE_SHORT_EXT;
		public const string MEMORY_LAYOUT_FILE_FILTER = "Memory Layout (*" + MEMORY_LAYOUT_FILE_EXT + ")|*" + MEMORY_LAYOUT_FILE_EXT;

		public MemoryLayout()
			: this(0, 0, new List<uint>())
		{				
		}

		public MemoryLayout(uint baseAddress, uint size, List<uint> sectorSizes)
		{
            BaseAddress = baseAddress;
            Size = size;
            SectorSizes = sectorSizes;
			mPropertyErrors = new Dictionary<string, string>();
            Validate();
		}

        public void Reset()
        {
            BaseAddress = 0;
            Size = 0;
            SectorSizes.Clear();
            Validate();
        }

		public uint BaseAddress { get; set; }
        public uint Size { get; set; }

		private bool ValidateBaseAddress()
		{
			string error = null;

			if (BaseAddress % 2 != 0)
			{
				error = "BaseAddress must be an even number";
			}

			this["BaseAddress"] = error;

			return (error == null);
		}

        private bool ValidateSize()
        {
            string error = null;

            if (Size % 2 != 0)
            {
                error = "Size must be an even number";
            }

            this["Size"] = error;

            return (error == null);
        }

		public uint EndAddress
		{
			get
			{
                return BaseAddress + Size;
			}
		}
		
		private bool ValidateSectorSizes()
		{
			string error = null;

			if (SectorSizes == null)
			{
                error = "SectorSizes is null";
			}
            else if (SectorSizes.Count == 0)
			{
                error = "SectorSizes is empty";
			}
			else
			{
                uint totalSize = 0;

                foreach (uint size in SectorSizes)
				{
					if (size % 2 != 0)
					{
                        error = "All SectorSizes must be even numbers";
						break;
					}

                    totalSize += size;
				}

                if (error == null)
                {
                    if (totalSize != Size)
                    {
                        error = "Sum of SectorSizes does not match Size";
                    }
                }
			}

            this["SectorSizes"] = error;

			return (error == null);
		}

		public bool Validate()
		{
			bool isValid = ValidateBaseAddress();
            isValid &= ValidateSize();
			isValid &= ValidateSectorSizes();

			return isValid;
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

        public List<uint> SectorSizes { get; set; }
        
		[NonSerialized]
		private Dictionary<string, string> mPropertyErrors;
	}

    public class MemoryUtils
    {
        public static IEnumerable<MemoryImage> SplitMemoryImageIntoSectors(byte[] sourceData, MemoryLayout layout)
        {
            var sectorImages = new List<MemoryImage>();

            uint sectorStartAddress = layout.BaseAddress;

            for (uint sectorIndex = 0; sectorIndex < layout.SectorSizes.Count; sectorIndex++)
            {
                uint sectorSize = layout.SectorSizes[(int)sectorIndex];

                byte[] data = new byte[sectorSize];
                Buffer.BlockCopy(sourceData, (int)(sectorStartAddress - layout.BaseAddress), data, 0, data.Length);

                sectorImages.Add(new MemoryImage(data, sectorStartAddress));

                sectorStartAddress += sectorSize;
            }

            return sectorImages;
        }

        public static bool CombineMemorySectorsIntoImage(IEnumerable<byte[]> sectorData, MemoryLayout layout, out MemoryImage combinedData)
        {
            bool success = true;
			combinedData = new MemoryImage(layout.Size, layout.BaseAddress);

            int curIndex = 0;

            foreach (var curSector in sectorData)
            {
                if ((curSector == null) || (curIndex + curSector.Length > layout.Size))
                {
                    success = false;
                    break;
                }

                Buffer.BlockCopy(curSector, 0, combinedData.RawData, curIndex, curSector.Length);
                curIndex += curSector.Length;
            }

            return success;
        }

        public static bool LoadHexFromFile(string fileName, ref List<MemoryImage> memoryImages)
        {
            bool retval = false;

            try
            {
                if (File.Exists(fileName))
                {
                    var rawData = File.ReadAllBytes(fileName);

                    //TODO - load hex file

                    retval = true;
                }
            }
            catch
            {
                retval = false;
            }

            return retval;
        }

        public static bool LoadHexFromResource(Assembly asm, string resourceName, ref List<MemoryImage> memoryImages)
        {
            bool retVal = false;

            Stream stream = asm.GetManifestResourceStream(resourceName);

            if ((stream != null) && (stream.Length > 0))
            {
                var rawData = new byte[stream.Length];
                                
                int readLength = stream.Read(rawData, 0, (int)stream.Length);

                if (readLength == stream.Length)
                {
					//TODO - load hex file

                    retVal = true;
                }
            }

            return retVal;
        }
    }
}
