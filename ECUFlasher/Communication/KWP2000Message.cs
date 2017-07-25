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
	public delegate void MessageChangedDelegate(KWP2000Interface commInterface, KWP2000Message message);
	public delegate void MessageSendFinishedDelegate(KWP2000Interface commInterface, KWP2000Message message, bool sentProperly, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries);

	public class KWP2000Message
	{
		public const uint DEFAULT_MAX_NUM_MESSAGE_RETRIES = 2;//2 is default according to ISO14230-2 spec

		public KWP2000AddressMode mAddressMode;
		public byte mSource;
		public byte mDestination;
		public byte mServiceID;
		public byte[] mData;

		public uint mMaxNumRetries;
		public event MessageSendFinishedDelegate ResponsesFinishedEvent;

		public KWP2000Message(KWP2000AddressMode addressMode, byte sourceAddress, byte destAddress, byte serviceID, uint maxNumRetries, byte[] data)
		{
			mAddressMode = addressMode;
			mSource = sourceAddress;
			mDestination = destAddress;
			mServiceID = serviceID;
			mData = data;
			mMaxNumRetries = maxNumRetries;
		}

		public KWP2000Message(KWP2000AddressMode addressMode, byte sourceAddress, byte destAddress, byte serviceID, byte[] data)
			: this(addressMode, sourceAddress, destAddress, serviceID, DEFAULT_MAX_NUM_MESSAGE_RETRIES, data)
		{
		}

		public byte[] GetMessageDataBytes(KWP2000Interface commInterface)
		{
			bool useOneByteHeader = false;
			UInt32 numHeaderBytes = 3;
			var actualAddressMode = mAddressMode;

			if (commInterface.IsOneByteHeaderSupported())
			{
				useOneByteHeader = true;
				numHeaderBytes = 1;
				actualAddressMode = KWP2000AddressMode.None;
			}
			else
			{
				//if one byte headers aren't supported, then use three byte headers.
				//if key bytes don't allow any address info, then just use three byte headers.
				useOneByteHeader = false;
				numHeaderBytes = 3;
				actualAddressMode = mAddressMode;
			}

			byte format = (byte)actualAddressMode;

			bool extraDataLengthByteNeeded = false;
			byte dataLength = 1;//one for the service ID

			if (mData != null)
			{
				Debug.Assert(dataLength + mData.Length <= 255);
				dataLength += (byte)mData.Length;
			}

			//if key bytes don't allow any length info, then just ignore them
			bool keyBytesDontSpecifyAnyLengthInfo = !commInterface.IsAdditionalLengthByteSupported() && !commInterface.IsLengthInfoInFormatByteSupported();

			if ((dataLength <= KWP2000Interface.MAX_MESSAGE_DATA_SIZE_WITHOUT_LENGTH_BYTE) && (commInterface.IsLengthInfoInFormatByteSupported() || keyBytesDontSpecifyAnyLengthInfo))
			{
				format |= dataLength;
			}
			else
			{
				extraDataLengthByteNeeded = true;
			}

			if (extraDataLengthByteNeeded)
			{
				numHeaderBytes += 1;
			}

			uint totalNumBytesInMessage = numHeaderBytes + dataLength + 1;//data bytes, format byte, target byte, source byte, checksum byte
			byte[] messageBuffer = new byte[totalNumBytesInMessage];

			if (useOneByteHeader)
			{
				messageBuffer[0] = format;
			}
			else
			{
				messageBuffer[0] = format;
				messageBuffer[1] = mDestination;
				messageBuffer[2] = mSource;
			}

			if (extraDataLengthByteNeeded)
			{
				messageBuffer[numHeaderBytes - 1] = dataLength;
			}

			messageBuffer[numHeaderBytes] = (byte)mServiceID;

			if (mData != null)
			{
				Buffer.BlockCopy(mData, 0, messageBuffer, (int)numHeaderBytes + 1, mData.Length);
			}

			byte checksum = 0;

			for (int x = 0; x < (numHeaderBytes + dataLength); x++)
			{
				checksum += messageBuffer[x];
			}

			messageBuffer[totalNumBytesInMessage - 1] = checksum;

			return messageBuffer;
		}

		public string GetDataString(KWP2000Interface commInterface)
		{
			byte[] dataBytes = GetMessageDataBytes(commInterface);
			string dataString = "";

			for (int x = 0; x < dataBytes.Length; x++)
			{
				dataString += string.Format("{0:X2}", dataBytes[x]) + ", ";
			}

			return dataString;
		}

		public MulticastDelegate GetResponsesFinishedEvent()
		{
			return ResponsesFinishedEvent;
		}

		public int DataLength
		{
			get
			{
				if (mData != null)
				{
					return mData.Length;
				}

				return 0;
			}
		}
	};

	public abstract class KWP2000MessageHelpers
	{
		public static KWP2000Message SendValidateFlashChecksumMessage(KWP2000Interface commInterface, uint startAddress, uint endAddress, ushort checksum)
		{
			Debug.Assert(startAddress % 2 == 0);
			Debug.Assert(endAddress % 2 == 1);

			byte[] tempData = new byte[9];
			tempData[0] = (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum;
			tempData[1] = (byte)((startAddress >> 16) & 0xFF);
			tempData[2] = (byte)((startAddress >> 8) & 0xFF);
			tempData[3] = (byte)((startAddress) & 0xFF);
			tempData[4] = (byte)((endAddress >> 16) & 0xFF);
			tempData[5] = (byte)((endAddress >> 8) & 0xFF);
			tempData[6] = (byte)((endAddress) & 0xFF);
			tempData[7] = (byte)((checksum >> 8) & 0xFF);
			tempData[8] = (byte)((checksum) & 0xFF);

			return commInterface.SendMessage((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, tempData);
		}

		public static KWP2000Message SendRequestValidateFlashChecksumResultMessage(KWP2000Interface commInterface)
		{
			byte[] tempData = new byte[1];
			tempData[0] = (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum;

			return commInterface.SendMessage((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, tempData);
		}

		public static KWP2000Message SendRequestEraseFlashMessage(KWP2000Interface commInterface, uint startAddress, uint endAddress, string flashToolCode)
		{
			Debug.Assert(flashToolCode != null);
			Debug.Assert(flashToolCode.Length <= 6);
			char[] FTC = flashToolCode.ToCharArray();

			byte[] tempData = new byte[13];
			tempData[0] = (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash;
			tempData[1] = (byte)((startAddress >> 16) & 0xFF);
			tempData[2] = (byte)((startAddress >> 8) & 0xFF);
			tempData[3] = (byte)((startAddress) & 0xFF);
			tempData[4] = (byte)((endAddress >> 16) & 0xFF);
			tempData[5] = (byte)((endAddress >> 8) & 0xFF);
			tempData[6] = (byte)((endAddress) & 0xFF);
			tempData[7] = (byte)FTC[0];
			tempData[8] = (byte)FTC[1];
			tempData[9] = (byte)FTC[2];
			tempData[10] = (byte)FTC[3];
			tempData[11] = (byte)FTC[4];
			tempData[12] = (byte)FTC[5];

			return commInterface.SendMessage((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, tempData);
		}

		public static KWP2000Message SendRequestEraseFlashResultMessage(KWP2000Interface commInterface)
		{
			byte[] tempData = new byte[1];
			tempData[0] = (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash;

			return commInterface.SendMessage((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, tempData);
		}

		public static KWP2000Message SendRequestDownloadMessage(KWP2000Interface commInterface, uint address, uint size, byte format)
		{
			byte[] data = new byte[7];
			data[0] = (byte)((address >> 16) & 0xFF);
			data[1] = (byte)((address >> 8) & 0xFF);
			data[2] = (byte)(address & 0xFF);
			data[3] = format;
			data[4] = (byte)((size >> 16) & 0xFF);
			data[5] = (byte)((size >> 8) & 0xFF);
			data[6] = (byte)(size & 0xFF);

			return commInterface.SendMessage((byte)KWP2000ServiceID.RequestDownload, data);
		}

		public static KWP2000Message SendRequestUploadMessage(KWP2000Interface commInterface, uint address, uint size, byte format)
		{
			byte[] data = new byte[7];
			data[0] = (byte)((address >> 16) & 0xFF);
			data[1] = (byte)((address >> 8) & 0xFF);
			data[2] = (byte)(address & 0xFF);
			data[3] = format;
			data[4] = (byte)((size >> 16) & 0xFF);
			data[5] = (byte)((size >> 8) & 0xFF);
			data[6] = (byte)(size & 0xFF);

			return commInterface.SendMessage((byte)KWP2000ServiceID.RequestUpload, data);
		}
	}
}