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
using System.Linq;
using System.Text;
using System.Diagnostics;

using Shared;

namespace Checksum
{
	public abstract class ChecksumOperation : Operation
	{
		public ChecksumOperation(byte[] imageToCheck)
		{
			ImageToCheck = imageToCheck;
		}

		protected void DisplayStatusMessage(string message, StatusMessageType messageType)
		{
			//TODO: hook this up
		}

		protected static bool DetectChecksums(byte[] imageToCheck, out uint baseAddress, out IEnumerable<BaseChecksum> detectedChecksums)
		{
			bool result = false;
			baseAddress = 0;
			var detectedChecksumList = new List<BaseChecksum>();
			detectedChecksums = detectedChecksumList;

			if ((imageToCheck != null) && (imageToCheck.Length > 0))
			{
				result = true;

				Checksum.RollingChecksums detectedRollingChecksum;
				Checksum.MultiRangeChecksum detectedMultiRangeChecksum;
				if (Checksum.ChecksumDetection.DetectRollingAndMultiRangeChecksums(imageToCheck, out detectedRollingChecksum, out detectedMultiRangeChecksum)
					|| Checksum.ChecksumDetection.DetectMultiRangeChecksum(imageToCheck, out detectedMultiRangeChecksum))
				{
					if (detectedMultiRangeChecksum != null)
					{
						detectedChecksumList.Add(detectedMultiRangeChecksum);
					}

					if (detectedRollingChecksum != null)
					{
						detectedChecksumList.Add(detectedRollingChecksum);
					}
				}
				else
				{					
					result = false;
				}

				Checksum.MainChecksum detectedMainChecksum;
				if (Checksum.ChecksumDetection.DetectMainChecksum(imageToCheck, out detectedMainChecksum))
				{
					detectedChecksumList.Add(detectedMainChecksum);
				}
				else
				{
					result = false;
				}				
				
				IEnumerable<Checksum.MultipointChecksum> detectedMultiPointChecksums;
				if (Checksum.ChecksumDetection.DetectMultiPointChecksums(imageToCheck, out baseAddress, out detectedMultiPointChecksums))
				{
					detectedChecksumList.AddRange(detectedMultiPointChecksums.Cast<Checksum.BaseChecksum>());
				}
				else
				{
					result = false;
				}
			}

			return result;
		}

		protected byte[] ImageToCheck { get; private set; }        
	}   

	public class ValidateChecksumsOperation : ChecksumOperation
	{
		public ValidateChecksumsOperation(byte[] imageToCheck)
			: base(imageToCheck)
		{
			AreChecksumsCorrect = false;
			NumChecksums = 0;
			NumIncorrectChecksums = 0;
		}

		public bool AreChecksumsCorrect { get; private set; }
		public uint NumChecksums { get; set; }
		public uint NumIncorrectChecksums { get; set; }

		protected override void OnOperationStart()
		{
			var asyncDel = (Action)(() =>
			{
				bool areChecksumsCorrect;
				uint numChecksums;
				uint numIncorrectChecksums;
				var success = ValidateChecksums(ImageToCheck, out areChecksumsCorrect, out numChecksums, out numIncorrectChecksums);

				AreChecksumsCorrect = areChecksumsCorrect;
				NumChecksums = numChecksums;
				NumIncorrectChecksums = numIncorrectChecksums;

				OperationCompleted(success);
			});

			asyncDel.BeginInvoke(null, null);
		}

		public static bool ValidateChecksums(byte[] imageToCheck, out bool areChecksumsCorrect, out uint numChecksums, out uint numIncorrectChecksums)
		{
			areChecksumsCorrect = false;
			numChecksums = 0;
			numIncorrectChecksums = 0;

			uint baseAddress;
			IEnumerable<BaseChecksum> detectedChecksums;
			if (DetectChecksums(imageToCheck, out baseAddress, out detectedChecksums))
			{
				numChecksums = (uint)detectedChecksums.Count();

				var memImageToCheck = new MemoryImage(imageToCheck, baseAddress);

				foreach (var checksum in detectedChecksums)
				{
					checksum.SetMemoryReference(memImageToCheck);

					if (!checksum.IsCorrect(false))
					{
						numIncorrectChecksums++;
					}
				}

				areChecksumsCorrect = (numIncorrectChecksums == 0);

				return true;
			}
			else
			{
				return false;
			}
		}
	}

	public class CorrectChecksumsOperation : ChecksumOperation
	{
		public CorrectChecksumsOperation(byte[] imageToCheck)
			: base(imageToCheck)
		{
			NumChecksums = 0;
			NumCorrectedChecksums = 0;
		}

		public uint NumChecksums { get; set; }
		public uint NumCorrectedChecksums { get; set; }

		protected override void OnOperationStart()
		{
			var asyncDel = (Action)(() =>
			{
				uint numChecksums;
				uint numCorrectedChecksums;
				var success = CorrectChecksums(ImageToCheck, out numChecksums, out numCorrectedChecksums);

				NumChecksums = numChecksums;
				NumCorrectedChecksums = numCorrectedChecksums;

				OperationCompleted(success);
			});

			asyncDel.BeginInvoke(null, null);
		}

		public static bool CorrectChecksums(byte[] imageToCheck, out uint numChecksums, out uint numCorrectedChecksums)
		{
			//TODO: make sure we are actually updating checksum data and not some random data

			numCorrectedChecksums = 0;
			numChecksums = 0;

			uint baseAddress;
			IEnumerable<BaseChecksum> detectedChecksums;
			if (DetectChecksums(imageToCheck, out baseAddress, out detectedChecksums))
			{
				numChecksums = (uint)detectedChecksums.Count();

				var memImageToCheck = new MemoryImage(imageToCheck, baseAddress);

				foreach (var checksum in detectedChecksums)
				{
					checksum.SetMemoryReference(memImageToCheck);

					if (checksum.UpdateChecksum(true) && checksum.CommitChecksum())
					{
						numCorrectedChecksums++;
					}
				}

				bool corrected = (numChecksums == numCorrectedChecksums);

				if (corrected)
				{
					foreach (var checksum in detectedChecksums)
					{
						checksum.SetMemoryReference(memImageToCheck);

						if (!checksum.IsCorrect(false))
						{
							corrected = false;
							break;
						}
					}
				}

				return corrected;
			}
			else
			{
				return false;
			}
		}
	}
}
