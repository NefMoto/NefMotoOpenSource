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
using System.Collections.ObjectModel;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics;
using System.ComponentModel;
using System.Xml.Serialization;

using Shared;

namespace Communication
{
	public struct KWP2000FlashStatus
	{
		[Flags]
		public enum FlashConsistency : byte
		{
			FlashCannotBeProgrammed = 0x01,
			CommunicationError = 0x02,//set when flashing failed due to a communication error
			FlashDefect = 0x04,//gets set when an erase fails because the persistent data could not be copied
			EEPROMError = 0x08,
			UnUsed1 = 0x10,
			UnUsed2 = 0x20,
			UnUsed3 = 0x40,
			FlashInconsistent = 0x80,//set when flashing starts, cleared when disconencted after flashing complete
		};

		[Flags]
		public enum ProgrammingSessionPreconditions : byte
		{
			[Description("Engine is running")]
			EngineRunning = 0x01,
			[Description("Immobilizer not authenticated")]
			ImmobilizerNotAuthenticated = 0x02,
			[Description("Security lockout countdown is running")]
			SecurityLockOutCountDownRunning = 0x04,
			[Description("Unknown precondition 3")]
			UnknownPrecondition3 = 0x08,
			[Description("Unknown precondition 4")]
			UnknownPrecondition4 = 0x10,
			[Description("Unknown precondition 5")]
			UnknownPrecondition5 = 0x20,
			[Description("Unknown precondition 6")]
			UnknownPrecondition6 = 0x40,
			[Description("Unknown precondition 7")]
			UnknownPrecondition7 = 0x80
		};

		public KWP2000FlashStatus(byte[] fromByteArray)
		{
			mFlashConsistency = 0;
			mNumFlashAttempts = 0;
			mNumSuccessfulFlashAttempts = 0;
			mProgrammingSessionPreconditions = 0;

			if (fromByteArray.Length > 0)
			{
				mFlashConsistency = (FlashConsistency)fromByteArray[0];

				if (fromByteArray.Length > 1)
				{
					mNumFlashAttempts = fromByteArray[1];

					if (fromByteArray.Length > 2)
					{
						mNumSuccessfulFlashAttempts = fromByteArray[2];

						if (fromByteArray.Length > 3)
						{
							mProgrammingSessionPreconditions = (ProgrammingSessionPreconditions)fromByteArray[3];
						}
					}
				}
			}
		}

		public FlashConsistency mFlashConsistency;
		public byte mNumFlashAttempts;
		public byte mNumSuccessfulFlashAttempts;
		public ProgrammingSessionPreconditions mProgrammingSessionPreconditions;
	};

	public class KWP2000ScalingTableRecordEntry
	{
		//high nibble of scaling byte
		public enum ScalingType : byte
		{
			UnsignedNumeric = 0,
			SignedNumeric = 1,
			BitMappedReportedWithoutMask = 2,
			BitMappedReportedWithMask = 3,
			BinaryCodedDecimal = 4,
			StateEncodedVariable = 5,
			ASCII = 6,
			SignedFloatingPoint = 7,
			Packet = 8,
			Formula = 9,
			UnitFormula = 10,
			UnsignedNumericWithIndication = 11,
			VehicleManufacturerSpecific1 = 12,
			VehicleManufacturerSpecific2 = 13,
			VehicleManufacturerSpecific3 = 14,
			ReservedByDocument = 15
		}

		public KWP2000ScalingTableRecordEntry()
		{
		}

		public KWP2000ScalingTableRecordEntry(ScalingType scalingType, byte numScalingBytes)
		{
			RecordScalingType = scalingType;
			NumScalingBytes = numScalingBytes;
		}

		public string GetStringFromData(byte[] data, int offset)
		{
			string valueString = "?";

			if((data.Length >= offset + NumScalingBytes) && (NumScalingBytes > 0))
			{
				switch (RecordScalingType)
				{
					case KWP2000ScalingTableRecordEntry.ScalingType.UnsignedNumeric:
					{
						Debug.Assert(NumScalingBytes <= 4);
						UInt32 unsignedNumber = 0;

						for (int x = 0; x < NumScalingBytes; x++)
						{
							unsignedNumber <<= 8;
							unsignedNumber |= data[x + offset];
						}

						int numDigits = NumScalingBytes * 2;									
						valueString = "0x" + unsignedNumber.ToString("X" + numDigits);

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.SignedNumeric:
					{
						Debug.Assert(NumScalingBytes <= 8);
						Debug.Assert((NumScalingBytes == 1) || (NumScalingBytes % 2 == 0));

						if(NumScalingBytes == 1)
						{
							var value = (sbyte)data[offset];
							valueString = value.ToString();
						}
						else if(NumScalingBytes == 2)
						{
							var value = BitConverter.ToInt16(data, offset);
							valueString = value.ToString();
						}
						else if(NumScalingBytes == 4)
						{
							var value = BitConverter.ToInt32(data, offset);
							valueString = value.ToString();
						}
						else if(NumScalingBytes == 8)
						{
							var value = BitConverter.ToInt64(data, offset);
							valueString = value.ToString();
						}

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.BitMappedReportedWithoutMask:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.BitMappedReportedWithMask:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.BinaryCodedDecimal:
					{
						var builder = new StringBuilder();

						for (int x = 0; x < NumScalingBytes; x++)
						{
							byte BCD = data[x + offset];

							byte highDigit = (byte)Math.Min(9, (BCD >> 4));
							byte lowDigit = (byte)Math.Min(9, (BCD & 0x0F));

							builder.Append(highDigit.ToString("D1") + lowDigit.ToString("D1"));
						}

						valueString = builder.ToString();

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.StateEncodedVariable:
					{
						Debug.Assert(NumScalingBytes == 1);

						valueString = data[offset].ToString("D1");

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.ASCII:
					{
						Debug.Assert(NumScalingBytes <= 15);

						bool allNull = true;

						for (int x = offset; x < offset + NumScalingBytes; x++)
						{
							if (data[offset] != 0)
							{
								allNull = false;
								break;
							}
						}

						if (allNull)
						{
							valueString = "";
						}
						else
						{
							var encoder = new ASCIIEncoding();
							valueString = encoder.GetString(data, offset, NumScalingBytes);
						}

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.SignedFloatingPoint:
					{
						//according to ANSI/IEEE Std 754 - 1985 standard

						Debug.Assert((NumScalingBytes == 4) || (NumScalingBytes == 8));
									
						if(NumScalingBytes == 4)
						{
							var value = BitConverter.ToSingle(data, offset);
							valueString = value.ToString();
						}
						else if(NumScalingBytes == 8)
						{
							var value = BitConverter.ToDouble(data, offset);
							valueString = value.ToString();
						}

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.Packet:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.Formula:
					{
						//TODO: support this scaling type, which also means we have to support ScalingExtension

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.UnitFormula:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.UnsignedNumericWithIndication:
					{
						//according to SAE J1939/71 spec
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.VehicleManufacturerSpecific1:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.VehicleManufacturerSpecific2:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.VehicleManufacturerSpecific3:
					{
						//TODO: support this scaling type

						break;
					}
					case KWP2000ScalingTableRecordEntry.ScalingType.ReservedByDocument:
					{
						//TODO: support this scaling type

						break;
					}
					default:
					{
						Debug.Assert(false, "Unknown scaling type: " + RecordScalingType);

						break;
					}
				}
			}

			return valueString;
		}

		public ScalingType RecordScalingType { get; set; }
		public byte NumScalingBytes { get; set; }
	}

	public class KWP2000ScalingTableRecord
	{
		public KWP2000ScalingTableRecord()
		{			
		}

		public KWP2000ScalingTableRecord(byte[] recordData)
		{
			RecordData = recordData;
		}

		public byte[] RecordData 
		{
			get
			{
				return _RecordData;
			}

			set
			{
				if (_RecordData != value)
				{
					_RecordData = value;

					byte identOption;
					List<KWP2000ScalingTableRecordEntry> entries;
					GenerateRecordEntries(_RecordData, out identOption, out entries);

					IdentOption = identOption;
					Entries = entries;
				}
			}
		}
		private byte[] _RecordData;

		public byte IdentOption { get; set; }//docs say this could be two bytes...

		public List<KWP2000ScalingTableRecordEntry> Entries { get; set; }

		public void GetStringsFromData(byte[] data, out List<string> valueStrings)
		{
			valueStrings = new List<string>();

			if(Entries != null)
			{
				int offset = 0;

				foreach (var recordEntry in Entries)
				{
					var stringValue = recordEntry.GetStringFromData(data, offset);
					valueStrings.Add(stringValue);

					offset += recordEntry.NumScalingBytes;
				}
			}
		}

		private static bool GenerateRecordEntries(byte[] recordData, out byte identOption, out List<KWP2000ScalingTableRecordEntry> scalingEntries)
		{
			bool result = false;

			identOption = 0;
			scalingEntries = new List<KWP2000ScalingTableRecordEntry>();

			if (recordData != null)
			{
				var state = State.Offset;
				bool finished = false;

				for (int recordIndex = 0; (recordIndex < recordData.Length) && !finished; recordIndex++)
				{
					switch (state)
					{
						case State.Offset:
						{
							//offset 0xFF means the end of the scaling table
							if (recordData[recordIndex] != 0xFF)
							{
								state = State.OptionIdent;
							}
							else
							{
								Debug.Assert(recordData.Length == 1);
								finished = true;
							}
							break;
						}
						case State.OptionIdent:
						{
							//TODO: docs say this could be multiple bytes...
							identOption = recordData[recordIndex];

							state = State.Scaling;

							break;
						}
						case State.Scaling:
						{
							result = true;

							//TODO: doc says scaling type could be multiple bytes, and then the num bytes of the entry is sum of low nibbles of all scaling type bytes

							var scalingType = (KWP2000ScalingTableRecordEntry.ScalingType)((recordData[recordIndex] >> 4) & 0x0F);
							var scalingTypeNumBytes = (byte)(recordData[recordIndex] & 0x0F);

							scalingEntries.Add(new KWP2000ScalingTableRecordEntry(scalingType, scalingTypeNumBytes));

							break;
						}
						case State.ScalingExtension:
						{
							//TODO: only used by Formula scaling types
							finished = true; result = false;

							break;
						}
						default:
						{
							Debug.Assert(false, "undefined state");
							finished = true; result = false;

							break;
						}
					}
				}
			}

			return result;
		}
				
		private enum State
		{
			Offset,
			OptionIdent,
			Scaling,
			ScalingExtension
		}
	};

	public class KWP2000ScalingTable
	{
		public KWP2000ScalingTable()
		{
		}

		public List<KWP2000ScalingTableRecord> TableRecords { get; set; }

		public byte[] ScalingTableData
		{
			private get
			{
				return _ScalingTableData;
			}
			
			set
			{
				if (_ScalingTableData != value)
				{
					_ScalingTableData = value;

					List<KWP2000ScalingTableRecord> tempRecords;
					GenerateTableRecords(_ScalingTableData, out tempRecords);

					TableRecords = tempRecords;
				}
			}
		}
		byte[] _ScalingTableData;

		public bool GetIdentificationOptionsInScalingTable(out byte[] identOptions)
		{
			bool retVal = false;
			identOptions = null;

			var tempIdentOptions = new List<byte>();

			if (TableRecords != null)
			{
				foreach (var record in TableRecords)
				{
					tempIdentOptions.Add(record.IdentOption);
					retVal = true;
				}
			}

			identOptions = new byte[tempIdentOptions.Count];
			tempIdentOptions.CopyTo(identOptions);

			return retVal;
		}

		public bool GetScalingRecordForIdentificationOption(byte identificationOption, out KWP2000ScalingTableRecord record)
		{
			bool found = false;
			record = null;

			if (TableRecords != null)
			{
				if (identificationOption == (byte)KWP2000IdentificationOption.ECUIdentificationDataTable)
				{
					found = true;

					var recordData = new byte[ScalingTableData.Length];
					Buffer.BlockCopy(ScalingTableData, 0, recordData, 0, ScalingTableData.Length);

					record = new KWP2000ScalingTableRecord(recordData);
				}
				else
				{
					foreach (var curRecord in TableRecords)
					{
						if (curRecord.IdentOption == identificationOption)
						{
							found = true;
							record = curRecord;
							break;
						}
					}
				}
			}

			return found;
		}

		private static void GenerateTableRecords(byte[] tableData, out List<KWP2000ScalingTableRecord> records)
		{
			records = new List<KWP2000ScalingTableRecord>();

			if (tableData != null)
			{
				int indexOfOffset = 0;

				while (indexOfOffset < tableData.Length)
				{
					byte sizeOfRecord = tableData[indexOfOffset];

					//check for end of table offset, and for running off the end of the array
					if ((sizeOfRecord != 0xFF) && (indexOfOffset + sizeOfRecord <= tableData.Length))
					{
						//is the record large enough to include the identification option and offset?
						if (sizeOfRecord >= 2)
						{
							var recordData = new byte[sizeOfRecord];
							Buffer.BlockCopy(tableData, indexOfOffset, recordData, 0, sizeOfRecord);
							
							var record = new KWP2000ScalingTableRecord(recordData);
							records.Add(record);
						}

						indexOfOffset += sizeOfRecord;
					}
					else
					{
						break;
					}
				}
			}
		}
	}

	public class KWP2000IdentificationInfo
	{
		public void Clear()
		{
			ScalingTable.ScalingTableData = null;
			IdentOptionData.Clear();
		}

		public KWP2000ScalingTable ScalingTable
		{
			get
			{
				if (_ScalingTable == null)
				{
					_ScalingTable = new KWP2000ScalingTable();
				}

				return _ScalingTable;
			}
		}
		private KWP2000ScalingTable _ScalingTable;

		public Dictionary<byte, byte[]> IdentOptionData
		{
			get
			{
				if (_IdentOptionData == null)
				{
					_IdentOptionData = new Dictionary<byte, byte[]>();
				}

				return _IdentOptionData;
			}

		}
		private Dictionary<byte, byte[]> _IdentOptionData;

		public bool GetIdentificationOptionValue(byte identOption, out KWP2000IdentificationOptionValue optionValue)
		{
			optionValue = null;
			
			KWP2000ScalingTableRecord scalingRecord;
			if (ScalingTable.GetScalingRecordForIdentificationOption(identOption, out scalingRecord))
			{
				optionValue = new KWP2000IdentificationOptionValue();
				optionValue.ValueData = IdentOptionData[identOption];
				optionValue.ScalingData = scalingRecord.RecordData;
			}

			return (optionValue != null);
		}
	}

	public class KWP2000IdentificationOptionValue
	{
		public byte[] ValueData { get; set; }
		public byte[] ScalingData 
		{
			get
			{
				return _ScalingData;
			}

			set
			{
				if (_ScalingData != value)
				{
					_ScalingData = value;
					ScalingRecord = new KWP2000ScalingTableRecord(_ScalingData);
				}
			}
		}
		private byte[] _ScalingData;
		private KWP2000ScalingTableRecord ScalingRecord;

		[XmlIgnore]
		public byte IdentOption
		{
			get
			{
				return ScalingRecord.IdentOption;
			}
		}

		public static string GetValueAsString(byte[] scalingData, byte[] valueData)
		{
			var scalingRecord = new KWP2000ScalingTableRecord(scalingData);

			return GetValueAsString(scalingRecord, valueData);
		}

		public static string GetValueAsString(KWP2000ScalingTableRecord scalingRecord, byte[] valueData)
		{
			string result = null;

			if (scalingRecord != null)
			{
				List<string> valueStrings;
				scalingRecord.GetStringsFromData(valueData, out valueStrings);

				if (valueStrings != null)
				{
					foreach (var valueString in valueStrings)
					{
						if (result == null)
						{
							result = "";
						}
						else
						{
							result += ", ";
						}

						result += valueString;
					}
				}
			}

			return result;
		}

		public override string ToString()
		{
			return GetValueAsString(ScalingRecord, ValueData);
		}

		public override bool Equals(object obj)
		{
			if (obj is KWP2000IdentificationOptionValue)
			{
				var otherObj = obj as KWP2000IdentificationOptionValue;

				if ((ValueData == null) || (otherObj.ValueData == null))
				{
					if (ValueData != otherObj.ValueData)
					{
						return false;
					}
				}
				else
				{
					if (ValueData.Length != otherObj.ValueData.Length)
					{
						return false;
					}

					for (int x = 0; x < ValueData.Length; x++)
					{
						if (ValueData[x] != otherObj.ValueData[x])
						{
							return false;
						}
					}
				}

				if ((ScalingData == null) || (otherObj.ScalingData == null))
				{
					if (ScalingData != otherObj.ScalingData)
					{
						return false;
					}
				}
				else
				{
					if (ScalingData.Length != otherObj.ScalingData.Length)
					{
						return false;
					}

					for (int x = 0; x < ScalingData.Length; x++)
					{
						if (ScalingData[x] != otherObj.ScalingData[x])
						{
							return false;
						}
					}
				}

				return true;
			}

			return base.Equals(obj);
		}
	}
}