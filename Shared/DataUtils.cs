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
using System.ComponentModel;
using System.Globalization;

namespace Shared
{
	public abstract class DataUtils
	{
		public enum DataType
		{
            [Description("Int8")] 
			Int8,
            [Description("UInt8")] 
			UInt8,
            [Description("Int16")] 
			Int16,
            [Description("UInt16")] 
			UInt16,
            [Description("Int32")] 
			Int32,
            [Description("UInt32")] 
			UInt32,
            [Description("Undefined")] 
			Undefined
		};

        public static UInt32 GetDataTypeSize(DataType type)
		{
			switch (type)
			{
				case DataType.Int8:				
					{
						return 1;
					}
				case DataType.UInt8:
					{
						return 1;
					}
				case DataType.Int16:
					{
						return 2;
					}
				case DataType.UInt16:
					{
						return 2;
					}
				case DataType.Int32:
					{
						return 4;
					}
				case DataType.UInt32:
					{
						return 4;
					}
			}

			Debug.Assert(false);
			return 0;
		}

		public static float ClampedScale(float value, float scale, float min, float max)
		{
			float result = value * scale;

			if (result > max)
			{
				result = max;
			}

			else if (result < min)
			{
				result = min;
			}

			return result;
		}

		public static float ClampedOffset(float value, float offset, float min, float max)
		{
			float result = value + offset;

			if (result > max)
			{
				result = max;
			}

			else if (result < min)
			{
				result = min;
			}

			return result;
		}

		//TODO: could probably replace read/write raw value functions with the BitConverter class, except BitConverter doesn't support UInt8

		public static unsafe bool WriteRawFloatValueByType(double value, DataUtils.DataType destType, byte[] rawDataDest, UInt32 offset)
		{
			bool result = false;

			if ((rawDataDest != null) && (GetDataTypeSize(destType) + offset <= (rawDataDest.Length)))
			{
				result = true;

				fixed (void* dest = &rawDataDest[offset])
				{
					switch (destType)
					{
						case DataUtils.DataType.Int8:
							{
								SByte* castDest = (SByte*)dest;
								*castDest = (SByte)Math.Min(Math.Max(value, SByte.MinValue), SByte.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt8:
							{
								Byte* castDest = (Byte*)dest;
								*castDest = (Byte)Math.Min(Math.Max(value, Byte.MinValue), Byte.MaxValue);
								break;
							}
						case DataUtils.DataType.Int16:
							{
								Int16* castDest = (Int16*)dest;
								*castDest = (Int16)Math.Min(Math.Max(value, Int16.MinValue), Int16.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt16:
							{
								UInt16* castDest = (UInt16*)dest;
								*castDest = (UInt16)Math.Min(Math.Max(value, UInt16.MinValue), UInt16.MaxValue);
								break;
							}
						case DataUtils.DataType.Int32:
							{
								Int32* castDest = (Int32*)dest;
								*castDest = (Int32)Math.Min(Math.Max(value, Int32.MinValue), Int32.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt32:
							{
								UInt32* castDest = (UInt32*)dest;
								*castDest = (UInt32)Math.Min(Math.Max(value, UInt32.MinValue), UInt32.MaxValue);
								break;
							}
						default:
							{
								result = false;
								break;
							}
					}
				}
			}

			return result;
		}

		public static unsafe bool ReadRawFloatValueByType(out double value, DataUtils.DataType sourceType, byte[] rawData, UInt32 offset)
		{
			bool result = false;
			value = 0;

			if ((rawData != null) && (GetDataTypeSize(sourceType) + offset <= rawData.Length))
			{
				result = true;

				fixed (void* source = &rawData[offset])
				{
					switch (sourceType)
					{
						case DataUtils.DataType.Int8:
							{
								value = *(SByte*)(source);
								break;
							}
						case DataUtils.DataType.UInt8:
							{
								value = *(Byte*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						case DataUtils.DataType.Int16:
							{
								value = *(Int16*)(source);
								break;
							}
						case DataUtils.DataType.UInt16:
							{
								value = *(UInt16*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						case DataUtils.DataType.Int32:
							{
								value = *(Int32*)(source);
								break;
							}
						case DataUtils.DataType.UInt32:
							{
								value = *(UInt32*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						default:
							{
								value = 0;
								result = false;
								break;
							}
					}
				}
			}

			return result;
		}

        public static unsafe bool WriteRawIntValueByType(UInt32 value, DataUtils.DataType destType, byte[] rawDataDest, UInt32 offset)
		{
			bool result = false;

			if ((rawDataDest != null) && (GetDataTypeSize(destType) + offset <= rawDataDest.Length))
			{
				result = true;

				fixed (void* dest = &rawDataDest[offset])
				{
					switch (destType)
					{
						case DataUtils.DataType.Int8:
							{
								SByte* castDest = (SByte*)dest;
								*castDest = (SByte)Math.Min(Math.Max(value, SByte.MinValue), SByte.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt8:
							{
								Byte* castDest = (Byte*)dest;
								*castDest = (Byte)Math.Min(Math.Max(value, Byte.MinValue), Byte.MaxValue);
								break;
							}
						case DataUtils.DataType.Int16:
							{
								Int16* castDest = (Int16*)dest;
								*castDest = (Int16)Math.Min(Math.Max(value, Int16.MinValue), Int16.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt16:
							{
								UInt16* castDest = (UInt16*)dest;
								*castDest = (UInt16)Math.Min(Math.Max(value, UInt16.MinValue), UInt16.MaxValue);
								break;
							}
						case DataUtils.DataType.Int32:
							{
								Int32* castDest = (Int32*)dest;
								*castDest = (Int32)Math.Min(Math.Max(value, Int32.MinValue), Int32.MaxValue);
								break;
							}
						case DataUtils.DataType.UInt32:
							{
								UInt32* castDest = (UInt32*)dest;
								*castDest = (UInt32)Math.Min(Math.Max(value, UInt32.MinValue), UInt32.MaxValue);
								break;
							}
						default:
							{
								result = false;
								break;
							}
					}
				}
			}

			return result;
		}

		public static unsafe bool ReadRawIntValueByType(out UInt32 value, DataUtils.DataType sourceType, byte[] rawData, UInt32 offset)
		{
			bool result = false;
			value = 0;

			if ((rawData != null) && (GetDataTypeSize(sourceType) + offset <= rawData.Length))
			{
				result = true;

				fixed (void* source = &rawData[offset])
				{
					switch (sourceType)
					{
						case DataUtils.DataType.Int8:
							{
								value = (UInt32)(*(SByte*)(source));
								break;
							}
						case DataUtils.DataType.UInt8:
							{
								value = *(Byte*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						case DataUtils.DataType.Int16:
							{
								value = (UInt32)(*(Int16*)(source));
								break;
							}
						case DataUtils.DataType.UInt16:
							{
								value = *(UInt16*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						case DataUtils.DataType.Int32:
							{
								value = (UInt32)(*(Int32*)(source));
								break;
							}
						case DataUtils.DataType.UInt32:
							{
								value = *(UInt32*)(source);
								Debug.Assert(value >= 0.0);
								break;
							}
						default:
							{
								value = 0;
								result = false;
								break;
							}
					}
				}
			}

			return result;
		}

		public static double GetCorrectedValueFromRaw(double rawValue, double scale, double offset)
		{
			return (rawValue * scale) + offset;
		}

		public static double GetRawValueFromCorrected(double correctedValue, double scale, double offset)
		{
			return (correctedValue - offset) / scale;
		}

        public static uint ReadHexString(string hexString)
        {
            uint number = 0;

            if (hexString != null)
            {
                var style = NumberStyles.Number;

                if (hexString.StartsWith("0x", true, CultureInfo.CurrentCulture))
                {
                    style = NumberStyles.HexNumber;

                    hexString = hexString.ToLower().Replace("0x", "");
                }

                if (!string.IsNullOrEmpty(hexString))
                {
                    UInt32.TryParse(hexString, style, CultureInfo.CurrentCulture, out number);
                }
            }

            return number;
        }

        public static string WriteHexString(object value)
        {
            return string.Format("0x{0:X}", value);
        }
	}
}
