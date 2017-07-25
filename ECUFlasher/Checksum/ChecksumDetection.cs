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
	public class ChecksumDetection
	{
		public static bool DetectMainChecksum(byte[] data, out MainChecksum detectedChecksum)
		{
			var detectedFunctionAddress = DetectMainChecksum(data, 0, out detectedChecksum);

#if DEBUG
			if (detectedFunctionAddress >= 0)
			{
				MainChecksum tempChecksum;
				if (DetectMainChecksum(data, (uint)(detectedFunctionAddress + 2), out tempChecksum) >= 0)
				{
					Debug.Fail("duplicate main checksum detected");
				}
			}
#endif
			return (detectedFunctionAddress >= 0);
		}

		private static int DetectMainChecksum(byte[] data, uint offset, out MainChecksum detectedChecksum)
		{
			detectedChecksum = null;

			byte[] startPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_MainCheckSumFunctions_0x879C0C_Head;
			byte[] startMask = Checksum.Properties.Resources.Mask_8D0907551M_002_MainCheckSumFunctions_0x879C0C_Head;

			var mainchecksumFunctionLocationStart = LocatePattern(data, offset, -1, startPattern, startMask, 2);

			if (mainchecksumFunctionLocationStart < 0)
			{
				return -1;
			}

			byte[] endPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_MainCheckSumFunctions_0x879C0C_Tail;
			byte[] endMask = Checksum.Properties.Resources.Mask_8D0907551M_002_MainCheckSumFunctions_0x879C0C_Tail;

			var mainchecksumFunctionLocationEnd = LocatePattern(data, (uint)mainchecksumFunctionLocationStart, mainchecksumFunctionLocationStart + startPattern.Length + 128, endPattern, endMask, 2);

			if (mainchecksumFunctionLocationEnd < 0)
			{
				return -1;
			}

			uint instructionSize;
			var curInstructionAddress = FindNextInstruction(data, (uint)mainchecksumFunctionLocationStart, C166Instructions.EXTP, out instructionSize);

			if (curInstructionAddress < 0)
			{
				return -1;
			}

			uint MainChecksumRangesPage;
			uint numEXTPCycles;
			if (!ParseEXTPInstruction(data, (uint)curInstructionAddress, out MainChecksumRangesPage, out numEXTPCycles, out instructionSize))
			{
				return -1;
			}

			curInstructionAddress += (int)instructionSize;

			uint temp;
			uint MainChecksumRangesAddress;
			if (!ParseMOVInstruction(data, (uint)curInstructionAddress, out temp, out MainChecksumRangesAddress, out instructionSize))
			{
				return -1;
			}

			curInstructionAddress += (int)instructionSize;

			uint MainChecksumRangesStartSegment;
			if (!ParseMOVInstruction(data, (uint)curInstructionAddress, out temp, out MainChecksumRangesStartSegment, out instructionSize))
			{
				return -1;
			}

			curInstructionAddress += (int)instructionSize;

			curInstructionAddress = FindNextInstruction(data, (uint)curInstructionAddress, C166Instructions.CMPB, out instructionSize);

			if (curInstructionAddress < 0)
			{
				return -1;
			}

			curInstructionAddress += (int)instructionSize;

			curInstructionAddress = FindNextInstruction(data, (uint)curInstructionAddress, C166Instructions.CMPB, out instructionSize);

			if (curInstructionAddress < 0)
			{
				return -1;
			}

			uint MainChecksumNumRanges;
			if (!ParseCMPBInstruction(data, (uint)curInstructionAddress, out temp, out MainChecksumNumRanges, out instructionSize))
			{
				return -1;
			}

			MainChecksumNumRanges /= 2;

			curInstructionAddress = FindNextInstruction(data, (uint)curInstructionAddress, C166Instructions.EXTP, out instructionSize);

			if (curInstructionAddress < 0)
			{
				return -1;
			}

			uint MainChecksumValuesPage;
			if (!ParseEXTPInstruction(data, (uint)curInstructionAddress, out MainChecksumValuesPage, out numEXTPCycles, out instructionSize))
			{
				return -1;
			}

			curInstructionAddress += (int)instructionSize;

			uint MainChecksumValuesAddress;
			if (!ParseSUBInstruction(data, (uint)curInstructionAddress, out temp, out MainChecksumValuesAddress, out instructionSize))
			{
				return -1;
			}

			detectedChecksum = new MainChecksum((MainChecksumRangesPage << 14) + MainChecksumRangesAddress, (MainChecksumValuesPage << 14) + MainChecksumValuesAddress, MainChecksumNumRanges);			

			return mainchecksumFunctionLocationStart;
		}		

		public static bool DetectMultiPointChecksums(byte[] data, out uint baseAddress, out IEnumerable<MultipointChecksum> multiPointChecksumBlocks)
		{
			multiPointChecksumBlocks = null;
			baseAddress = 0;

			uint multipointChecksumsAddress = 0;
			uint multipointChecksumsNumBlocks = 0;

			byte[] startPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_MultiPointChecksumFunction_0x87CA3E;
			byte[] startMask = Checksum.Properties.Resources.Mask_8D0907551M_002_MultiPointChecksumFunction_0x87CA3E;

			var detectedSelectMultiPointChecksumFunctionAddress = LocatePattern(data, 0, -1, startPattern, startMask, 2);

			if (detectedSelectMultiPointChecksumFunctionAddress >= 0)
			{
#if DEBUG
				{
					if (LocatePattern(data, (uint)(detectedSelectMultiPointChecksumFunctionAddress + 2), -1, startPattern, startMask, 2) >= 0)
					{
						Debug.Fail("detected duplicate multipoint checksum setup function");
					}
				}
#endif

				uint currentInstruction = (uint)(detectedSelectMultiPointChecksumFunctionAddress + startPattern.Length - 18);

				uint numInstructionBytes;
				uint temp;
				uint address;
				if (!ParseMOVInstruction(data, currentInstruction, out temp, out address, out numInstructionBytes))
				{
					return false;
				}

				currentInstruction += numInstructionBytes;

				uint page;
				if (!ParseMOVInstruction(data, currentInstruction, out temp, out page, out numInstructionBytes))
				{
					return false;
				}

				multipointChecksumsAddress = (page << 14) | address;

				startPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_MultiPointChecksumCalculationFunction_0x87C532;
				startMask = Checksum.Properties.Resources.Mask_8D0907551M_002_MultiPointChecksumCalculationFunction_0x87C532;

				var detectedCalculateMultiPointFunctionLocationStart = LocatePattern(data, 0, -1, startPattern, startMask, 2);

				if (detectedCalculateMultiPointFunctionLocationStart < 0)
				{
					return false;
				}

#if DEBUG
				{
					if (LocatePattern(data, (uint)(detectedCalculateMultiPointFunctionLocationStart + 2), -1, startPattern, startMask, 2) >= 0)
					{
						Debug.Fail("detected duplicate multipoint checksum calculation function");
					}
				}
#endif

				currentInstruction = (uint)(detectedCalculateMultiPointFunctionLocationStart + startPattern.Length - 4);

				if (!ParseCMPInstruction(data, currentInstruction, out temp, out multipointChecksumsNumBlocks, out numInstructionBytes))
				{
					return false;
				}

				var callsAddress = FindNextInstruction(data, (uint)detectedCalculateMultiPointFunctionLocationStart, C166Instructions.CALLS, out numInstructionBytes);

				if (callsAddress < 0)
				{
					return false;
				}

				uint selectMultiPointChecksumFunctionAddress;
				if (!ParseCALLSInstruction(data, (uint)callsAddress, out selectMultiPointChecksumFunctionAddress, out numInstructionBytes))
				{
					return false;
				}

				baseAddress = (uint)((selectMultiPointChecksumFunctionAddress - detectedSelectMultiPointChecksumFunctionAddress) & 0xFFFFF000);
			}
			else
			{
				startPattern = Checksum.Properties.Resources.Pattern_8D0907551C_MultiPointChecksumFunction_0x86DA62;
				startMask = Checksum.Properties.Resources.Mask_8D0907551C_MultiPointChecksumFunction_0x86DA62;

				var multipointFunctionLocationStart = LocatePattern(data, 0, -1, startPattern, startMask, 2);

				if (multipointFunctionLocationStart < 0)
				{
					return false;
				}

#if DEBUG
				{
					if (LocatePattern(data, (uint)(multipointFunctionLocationStart + 2), -1, startPattern, startMask, 2) >= 0)
					{
						Debug.Fail("detected duplicate multipoint checksum function");
					}
				}
#endif

				uint currentInstruction = (uint)(multipointFunctionLocationStart + startPattern.Length - 8);

				uint numInstructionBytes;
				uint temp;
				uint address;
				if (!ParseMOVInstruction(data, currentInstruction, out temp, out address, out numInstructionBytes))
				{
					return false;
				}

				currentInstruction += numInstructionBytes;

				uint page;
				if (!ParseMOVInstruction(data, currentInstruction, out temp, out page, out numInstructionBytes))
				{
					return false;
				}

				multipointChecksumsAddress = (page << 14) | address;

				currentInstruction = (uint)(multipointFunctionLocationStart + startPattern.Length - 24);

				if (!ParseCMPInstruction(data, currentInstruction, out temp, out multipointChecksumsNumBlocks, out numInstructionBytes))
				{
					return false;
				}

				var callsAddress = FindNextInstruction(data, (uint)multipointFunctionLocationStart, C166Instructions.CALLS, out numInstructionBytes);

				if (callsAddress < 0)
				{
					return false;
				}

				uint functionAddress;
				if (!ParseCALLSInstruction(data, (uint)callsAddress, out functionAddress, out numInstructionBytes))
				{
					return false;
				}

				baseAddress = (uint)((functionAddress & 0xFFFFF000) - (multipointFunctionLocationStart & 0xFFFFF000));
			}

			var multipointChecksums = new List<Checksum.MultipointChecksum>((int)multipointChecksumsNumBlocks);

			for (var x = 0; x < multipointChecksumsNumBlocks; x++)
			{
				multipointChecksums.Add(new Checksum.MultipointChecksum((uint)(multipointChecksumsAddress + (Checksum.MultipointChecksum.GetChecksumBlockSize() * x))));
			}

			multiPointChecksumBlocks = multipointChecksums;			

			return true;
		}

		public static bool DetectRollingAndMultiRangeChecksums(byte[] data, out RollingChecksums detectedRollingChecksums, out MultiRangeChecksum detectedMultiRangeChecksums)
		{
			detectedRollingChecksums = null;
			detectedMultiRangeChecksums = null;

			bool isUsingDualChecksums = false;

			//if this pattern doesn't work well, we may be able to just detect the values from the seed table itself, since they appear to be constant
			byte[] startPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_RollingChecksumSeedsFunction_0x87A94A;
			byte[] startMask = Checksum.Properties.Resources.Mask_8D0907551M_002_RollingChecksumSeedsFunction_0x87A94A;

			var me71RollingSeedsFunctionLocationStart = LocatePattern(data, 0, -1, startPattern, startMask, 2);

			if (me71RollingSeedsFunctionLocationStart < 0)
			{
				return false;
			}

			uint currentAddress = (uint)(me71RollingSeedsFunctionLocationStart + startPattern.Length - 8);

			uint numInstructionBytes;
			uint temp;
			uint address;
			if (!ParseMOVInstruction(data, currentAddress, out temp, out address, out numInstructionBytes))
			{
				return false;
			}

			currentAddress += numInstructionBytes;

			uint segment;
			if (!ParseMOVInstruction(data, currentAddress, out temp, out segment, out numInstructionBytes))
			{
				return false;
			}

			uint seedTableAddress = (segment << 16) | address;

			detectedRollingChecksums = new RollingChecksums(seedTableAddress);

			uint savedCurrentAddress = currentAddress;

			//detect the initialization checksum
			{
				currentAddress = savedCurrentAddress;

				startPattern = Checksum.Properties.Resources.Pattern_8D0907551K_RollingChecksumValueInit_0x890742;
				startMask = Checksum.Properties.Resources.Mask_8D0907551K_RollingChecksumValueInit_0x890742;

				var detectedInitValueAddress = LocatePattern(data, currentAddress, (int)(currentAddress + 4096), startPattern, startMask, 2);

				if (detectedInitValueAddress > 0)
				{
					uint instructionSize;

					isUsingDualChecksums = ParseMOVInstruction(data, (uint)detectedInitValueAddress, out temp, out temp, out instructionSize);

					detectedInitValueAddress += 14;

					uint initAddress;
					if (!ParseMOVInstruction(data, (uint)detectedInitValueAddress, out temp, out initAddress, out instructionSize))
					{
						return false;
					}

					detectedInitValueAddress += (int)instructionSize;

					uint initSegment;
					if (!ParseMOVInstruction(data, (uint)detectedInitValueAddress, out temp, out initSegment, out instructionSize))
					{
						return false;
					}

					detectedInitValueAddress += (int)instructionSize;

					uint initNumBytes;
					if (!ParseMOVInstruction(data, (uint)detectedInitValueAddress, out temp, out initNumBytes, out instructionSize))
					{
						return false;
					}

					detectedRollingChecksums.EnableInitRange(initAddress | (initSegment << 16), initNumBytes);
				}
			}

			var checksumMap = new Dictionary<uint, uint>();
			var transitionMap = new Dictionary<uint, uint>();

			//checksum value address pattern
			//mov R4, #xxxx; mov R5, #xxxx, calls xxxx
			startPattern = new byte[] { 0xE6, 0xF4, 0x72, 0xA8, 0xE6, 0xF5, 0x87, 0x00, 0xDA, 0x00, 0xD8, 0x7E };
			startMask = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };

			uint stopAddress = currentAddress + 2048;

			while (currentAddress < stopAddress)
			{
				//TODO: could switch this to an instruction sequence
				var detectedPatternAddress = LocatePattern(data, currentAddress, (int)stopAddress, startPattern, startMask, 2);

				if (detectedPatternAddress > 0)
				{
					var instructionSequence = new List<C166Instructions> { C166Instructions.MOVB, C166Instructions.CMPB };
					IEnumerable<uint> sequenceSizes;
					var valueIndexAddress = FindPrevInstructionSequenceStart(data, (uint)detectedPatternAddress, detectedPatternAddress - 64, instructionSequence, out sequenceSizes);

					if (valueIndexAddress > 0)
					{
						valueIndexAddress += (int)sequenceSizes.ElementAt(0);

						uint valueIndex;
						if (!ParseCMPBInstruction(data, (uint)valueIndexAddress, out temp, out valueIndex, out numInstructionBytes))
						{
							return false;
						}

						if (checksumMap.ContainsKey(valueIndex))
						{
							return false;
						}

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress, out temp, out address, out numInstructionBytes))
						{
							return false;
						}

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress + numInstructionBytes, out temp, out segment, out numInstructionBytes))
						{
							return false;
						}

						checksumMap.Add(valueIndex, address | (uint)(segment << 16));

						currentAddress = (uint)(detectedPatternAddress + startPattern.Length);
					}
					else if (isUsingDualChecksums)
					{
						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress, out temp, out address, out numInstructionBytes))
						{
							return false;
						}

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress + numInstructionBytes, out temp, out segment, out numInstructionBytes))
						{
							return false;
						}

						detectedMultiRangeChecksums = new MultiRangeChecksum(address | (uint)(segment << 16));

						break;
					}
					else
					{
						return false;
					}
				}
				else
				{
					break;
				}
			}

			if ((checksumMap.Count <= 0) || (isUsingDualChecksums && (detectedMultiRangeChecksums == null)))
			{
				return false;
			}

			var addressRangeMap = new Dictionary<uint, AddressRange>();

			currentAddress = savedCurrentAddress;
			stopAddress = currentAddress + 2048;//probably doesn't need to be this far

			startPattern = Checksum.Properties.Resources.Pattern_8D0907551M_002_RollingChecksumRanges_0x87AB80;
			startMask = Checksum.Properties.Resources.Mask_8D0907551M_002_RollingChecksumRanges_0x87AB80;

			while (currentAddress < stopAddress)
			{
				var detectedPatternAddress = LocatePattern(data, currentAddress, (int)stopAddress, startPattern, startMask, 2);

				if (detectedPatternAddress > 0)
				{
					uint rangeStartAddress;
					uint rangeStartSegment;
					uint rangeEndAddress;
					uint rangeEndSegment;
					{
						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress, out temp, out rangeStartAddress, out numInstructionBytes))
						{
							break;
						}

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress + numInstructionBytes, out temp, out rangeStartSegment, out numInstructionBytes))
						{
							break;
						}

						detectedPatternAddress += startPattern.Length - 8;

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress, out temp, out rangeEndAddress, out numInstructionBytes))
						{
							break;
						}

						if (!ParseMOVInstruction(data, (uint)detectedPatternAddress + numInstructionBytes, out temp, out rangeEndSegment, out numInstructionBytes))
						{
							break;
						}
					}

					uint index;
					uint nextIndex;
					{
						var instructionSequence = new List<C166Instructions> { C166Instructions.MOVB, C166Instructions.CMPB };
						IEnumerable<uint> sequenceSizes;
						var indexAddress = FindPrevInstructionSequenceStart(data, (uint)detectedPatternAddress, detectedPatternAddress - 64, instructionSequence, out sequenceSizes);

						if (indexAddress <= 0)
						{
							return false;
						}

						indexAddress += (int)sequenceSizes.ElementAt(0);

						if (!ParseCMPBInstruction(data, (uint)indexAddress, out temp, out index, out numInstructionBytes))
						{
							return false;
						}

						if (transitionMap.ContainsKey(index) || addressRangeMap.ContainsKey(index))
						{
							return false;
						}

						var nextIndexAddress = FindNextInstruction(data, (uint)detectedPatternAddress, C166Instructions.MOVB, out numInstructionBytes);

						if (nextIndexAddress <= 0)
						{
							return false;
						}

						if (!ParseMOVBInstruction(data, (uint)nextIndexAddress, out temp, out nextIndex, out numInstructionBytes))
						{
							return false;
						}
					}

					var startRange = rangeStartAddress | (uint)(rangeStartSegment << 16);
					var endRange = rangeEndAddress | (uint)(rangeEndSegment << 16);

					var addressRange = new AddressRange(startRange, endRange - startRange + 1);
					addressRangeMap.Add(index, addressRange);
					transitionMap.Add(index, nextIndex);

					currentAddress = (uint)(detectedPatternAddress + startPattern.Length);
				}
				else
				{
					break;
				}
			}

			//if the M box pattern didn't work, try the c box pattern
			if (!addressRangeMap.Values.Any())
			{
				startPattern = Checksum.Properties.Resources.Pattern_8D0907551C_RollingChecksumRanges_0x86C094;
				startMask = Checksum.Properties.Resources.Mask_8D0907551C_RollingChecksumRanges_0x86C094;

				while (currentAddress < stopAddress)
				{
					var detectedPatternAddress = LocatePattern(data, currentAddress, (int)stopAddress, startPattern, startMask, 2);

					if (detectedPatternAddress > 0)
					{
						uint rangeStartAddress;
						uint rangeEndAddress;
						uint rangeSegment;
						{
							detectedPatternAddress += startPattern.Length;

							if (!ParseADDInstruction(data, (uint)detectedPatternAddress, out temp, out rangeStartAddress, out numInstructionBytes))
							{
								break;
							}

							detectedPatternAddress += (int)numInstructionBytes;

							if (!ParseADDCInstruction(data, (uint)detectedPatternAddress, out temp, out rangeSegment, out numInstructionBytes))
							{
								break;
							}

							detectedPatternAddress = FindNextInstruction(data, (uint)detectedPatternAddress, C166Instructions.JMPR, out numInstructionBytes);

							if (detectedPatternAddress < 0)
							{
								break;
							}

							detectedPatternAddress += (int)numInstructionBytes;

							if (!ParseCMPInstruction(data, (uint)detectedPatternAddress, out temp, out rangeEndAddress, out numInstructionBytes))
							{
								break;
							}

							detectedPatternAddress = FindNextInstruction(data, (uint)detectedPatternAddress, C166Instructions.MOV, out numInstructionBytes);

							if (detectedPatternAddress < 0)
							{
								break;
							}

							uint rangeEndAddressOffset;
							if (!ParseMOVInstruction(data, (uint)detectedPatternAddress, out temp, out rangeEndAddressOffset, out numInstructionBytes))
							{
								break;
							}

							rangeEndAddress += rangeEndAddressOffset;
						}

						uint index;
						uint nextIndex;
						{
							var instructionSequence = new List<C166Instructions> { C166Instructions.MOVB, C166Instructions.CMPB, C166Instructions.JMPR };
							IEnumerable<uint> sequenceSizes;
							var indexAddress = FindPrevInstructionSequenceStart(data, (uint)detectedPatternAddress, detectedPatternAddress - 64, instructionSequence, out sequenceSizes);

							if (indexAddress <= 0)
							{
								return false;
							}

							indexAddress += (int)sequenceSizes.ElementAt(0);

							if (!ParseCMPBInstruction(data, (uint)indexAddress, out temp, out index, out numInstructionBytes))
							{
								return false;
							}

							if (transitionMap.ContainsKey(index) || addressRangeMap.ContainsKey(index))
							{
								return false;
							}

							var nextIndexAddress = FindNextInstruction(data, (uint)detectedPatternAddress, C166Instructions.MOVB, out numInstructionBytes);

							if (nextIndexAddress <= 0)
							{
								return false;
							}

							if (!ParseMOVBInstruction(data, (uint)nextIndexAddress, out temp, out nextIndex, out numInstructionBytes))
							{
								return false;
							}
						}

						var startRange = rangeStartAddress | (uint)(rangeSegment << 16);
						var endRange = rangeEndAddress | (uint)(rangeSegment << 16);

						var addressRange = new AddressRange(startRange, endRange - startRange + 1);
						addressRangeMap.Add(index, addressRange);
						transitionMap.Add(index, nextIndex);

						currentAddress = (uint)(detectedPatternAddress + startPattern.Length);
					}
					else
					{
						break;
					}
				}
			}

			{
				var rangesMap = new Dictionary<uint, List<AddressRange>>();

				foreach (var rangeIndex in addressRangeMap.Keys)
				{
					var currentRangeIndex = rangeIndex;

					while (true)
					{
						if (!transitionMap.ContainsKey(currentRangeIndex))
						{
							return false;
						}

						var rangeNextIndex = transitionMap[currentRangeIndex];

						if (checksumMap.ContainsKey(rangeNextIndex))
						{
							if (!rangesMap.ContainsKey(rangeNextIndex))
							{
								rangesMap.Add(rangeNextIndex, new List<AddressRange>());
							}

							rangesMap[rangeNextIndex].Add(addressRangeMap[rangeIndex]);
							break;
						}

						currentRangeIndex = rangeNextIndex;
					}
				}


				if (!checksumMap.Any() || checksumMap.Keys.Any(checksumIndex => !rangesMap.ContainsKey(checksumIndex)))
				{
					return false;
				}

				foreach (var key in checksumMap.Keys)
				{
					var ranges = rangesMap[key];

					detectedRollingChecksums.AddAddressRange(ranges, checksumMap[key]);

					if (detectedMultiRangeChecksums != null)
					{
						foreach (var range in ranges)
						{
							detectedMultiRangeChecksums.AddRange(range);
						}
					}
				}
			}

			return true;
		}

		public static bool DetectMultiRangeChecksum(byte[] data, out MultiRangeChecksum detectedChecksum)
		{
			detectedChecksum = null;

			var addressRanges = new List<AddressRange>();

			byte[] addressPattern = Checksum.Properties.Resources.Pattern_4Z7907551R_MultiRangeChecksumFunction_0x8ABAB4;
			byte[] addressMask = Checksum.Properties.Resources.Mask_4Z7907551R_MultiRangeChecksumFunction_0x8ABAB4;

			uint temp;
			uint instructionSize;
			int currentAddress;
			uint lastDetectBlockEndAddress = 0;

			for (currentAddress = 0; (currentAddress < data.Length) && (currentAddress >= 0); )
			{
				currentAddress = LocatePattern(data, (uint)currentAddress, -1, addressPattern, addressMask, 2);

				if (currentAddress < 0)
				{
					break;
				}

				currentAddress = FindNextInstruction(data, (uint)currentAddress, C166Instructions.MOV, out instructionSize);

				if (currentAddress < 0)
				{
					return false;
				}

				uint rangeStartAddress;

				if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out rangeStartAddress, out instructionSize))
				{
					return false;
				}

				currentAddress += (int)instructionSize;

				uint rangeStartSegment;

				if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out rangeStartSegment, out instructionSize))
				{
					return false;
				}

				currentAddress = FindNextInstruction(data, (uint)currentAddress, C166Instructions.ADDC, out instructionSize);

				if (currentAddress < 0)
				{
					return false;
				}

				currentAddress += (int)instructionSize;

				uint rangeEndAddress;

				if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out rangeEndAddress, out instructionSize))
				{
					return false;
				}

				currentAddress += (int)instructionSize;

				uint rangeEndSegment;

				if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out rangeEndSegment, out instructionSize))
				{
					return false;
				}

				lastDetectBlockEndAddress = (uint)currentAddress + instructionSize;

				uint rangeStart = rangeStartAddress | (rangeStartSegment << 16);
				uint rangeEnd = rangeEndAddress | (rangeEndSegment << 16);

				addressRanges.Add(new AddressRange(rangeStart, rangeEnd - rangeStart + 1));
			}

			if (addressRanges.Count <= 0)
			{
				return false;
			}

			var valuePattern = Checksum.Properties.Resources.Pattern_4Z7907551R_MultiRangeChecksumFunctionValue_0x8ABC40;
			var valueMask = Checksum.Properties.Resources.Mask_4Z7907551R_MultiRangeChecksumFunctionValue_0x8ABC40;

			currentAddress = LocatePattern(data, lastDetectBlockEndAddress, (int)(lastDetectBlockEndAddress + 256), valuePattern, valueMask, 2);

			if (currentAddress < 0)
			{
				return false;
			}

			currentAddress = FindNextInstruction(data, (uint)currentAddress, C166Instructions.MOV, out instructionSize);

			if (currentAddress < 0)
			{
				return false;
			}

			uint valueAddress;

			if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out valueAddress, out instructionSize))
			{
				return false;
			}

			currentAddress += (int)instructionSize;

			uint valueSegment;

			if (!ParseMOVInstruction(data, (uint)currentAddress, out temp, out valueSegment, out instructionSize))
			{
				return false;
			}

			uint checksumValueAddress = valueAddress | (valueSegment << 16);

			detectedChecksum = new MultiRangeChecksum(checksumValueAddress);

			foreach (var range in addressRanges)
			{
				detectedChecksum.AddRange(range);
			}

			return true;
		}

		public static int LocatePattern(byte[] data, uint offset, int maxOffset, byte[] pattern, byte[] mask, uint stepSize)
		{
#if DEBUG
			Debug.Assert(pattern != null);
			Debug.Assert(mask != null);
			Debug.Assert(pattern.Length == mask.Length);

			uint maxNumBytesMatched = 0;
			int addressOfMaxBytesMatched = -1;
#endif
			if ((data != null) && (pattern != null) && (mask != null) && (pattern.Length == mask.Length))
			{
				if (maxOffset < 0)
				{
					maxOffset = data.Length;
				}

				for (uint x = offset; (x < data.Length) && (x < maxOffset); x += stepSize)
				{
					uint y;

					for (y = 0; (y < pattern.Length) && (x + y < data.Length); y++)
					{
						if (((data[x + y] ^ pattern[y]) & mask[y]) != 0)
						{
							//pattern doesn't match
							break;
						}
					}

					if (y >= pattern.Length)
					{
						//pattern matches all the way to the end
						return (int)x;
					}

#if DEBUG
					if (y > maxNumBytesMatched)
					{
						maxNumBytesMatched = y;
						addressOfMaxBytesMatched = (int)x;
					}
#endif
				}
			}

			return -1;
		}

		public enum C166Instructions
		{
			ADD,
			ADDB,
			ADDC,
			ADDCB,
			AND,
			ANDB,
			ASHR,
			ATOMIC,
			BAND,
			BCMP,
			BMOV,
			BMOVN,
			BOR,
			BXOR,
			BCLR,
			BSET,
			BFLDH,
			BFLDL,
			CALLA,
			CALLI,
			CALLR,
			CALLS,
			CMPD1,
			CMPD2,
			CMPI1,
			CMPI2,
			CMP,
			CMPB,
			CPL,
			CPLB,
			DISWDT,
			DIV,
			DIVL,
			DIVLU,
			DIVU,
			EINIT,
			EXTP,
			EXTPR,
			EXTR,
			EXTS,
			EXTSR,
			IDLE,
			JB,
			JBC,
			JMPA,
			JMPI,
			JMPS,
			JMPR,
			JNB,
			JNBS,
			MOV,
			MOVB,
			MOVBS,
			MOVBZ,
			MUL,
			MULU,
			NEG,
			NEGB,
			NOP,
			OR,
			ORB,
			PCALL,
			POP,
			PRIOR,
			PUSH,
			PWRDN,
			RET,
			RETI,
			RETP,
			RETS,
			ROL,
			ROR,
			SCXT,
			SHL,
			SHR,
			SRST,
			SRVWDT,
			SUB,
			SUBB,
			SUBC,
			SUBCB,
			TRAP,
			XOR,
			XORB,
		}

		public static bool ParseInstruction(byte[] data, uint offset, out C166Instructions instruction, out uint numBytesForInstruction)
		{
			instruction = C166Instructions.NOP;
			numBytesForInstruction = 1;

			//JMPR cc, rel / cD rr / 2
			if ((data[offset] & 0x0F) == 0x0D)
			{
				instruction = C166Instructions.JMPR;
				numBytesForInstruction = 2;
				return true;
			}

			switch (data[offset])
			{
				case 0xDA://CALLS seg, caddr / DA SS MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.CALLS;
					return true;
				}
				case 0xEA://JMPA cc, caddr / EA c0 MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.JMPA;
					return true;
				}
				case 0xDC://EXTP/EXTS
				{
					numBytesForInstruction = 2;

					switch (data[offset + 1] & 0xC0)
					{
						case 0x00:
						{
							instruction = C166Instructions.EXTS;
							return true;
						}
						case 0x40:
						{
							instruction = C166Instructions.EXTP;
							return true;
						}
					}

					break;
				}
				case 0xD7://EXTP/EXTS
				{
					numBytesForInstruction = 4;

					switch (data[offset + 1] & 0xC0)
					{
						case 0x00:
						{
							instruction = C166Instructions.EXTS;
							return true;
						}
						case 0x40:
						{
							instruction = C166Instructions.EXTP;
							return true;
						}
					}

					break;
				}
				case 0x20://SUB Rwn, Rwm / 20 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.SUB;
					return true;
				}
				case 0x28://SUB Rwn, [Rwi] / 28 n:10ii / 2 //SUB Rwn, [Rwi+] / 28 n:11ii / 2 //SUB Rwn, #data3 / 28 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.SUB;
					return true;
				}
				case 0x26://SUB reg, #data16 / 26 RR ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.SUB;
					return true;
				}
				case 0x22://SUB reg, mem / 22 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.SUB;
					return true;
				}
				case 0x24://SUB mem, reg / 24 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.SUB;
					return true;
				}
				case 0x00://ADD Rwn, Rwm / 00 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.ADD;
					return true;
				}
				case 0x08://ADD Rwn, [Rwi+] / 08 n:11ii / 2 //ADD Rwn, [Rwi] / 08 n:10ii / 2//ADD Rwn, #data3 / 08 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.ADD;
					return true;
				}
				case 0x06://ADD reg, #data16 / 06 RR ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADD;
					return true;
				}
				case 0x02://ADD reg, mem / 02 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADD;
					return true;
				}
				case 0x04://ADD mem, reg / 04 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADD;
					return true;
				}
				case 0x10://ADDC Rwn, Rwm / 10 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.ADDC;
					return true;
				}
				case 0x18://ADDC Rwn, [Rwi+] / 18 n:11ii / 2 //ADDC Rwn, [Rwi] / 18 n:10ii / 2 //ADDC Rwn, #data3 / 18 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.ADDC;
					return true;
				}
				case 0x16://ADDC reg, #data16 / 16 RR ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADDC;
					return true;
				}
				case 0x12://ADDC reg, mem / 12 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADDC;
					return true;
				}
				case 0x14://ADDC mem, reg / 14 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.ADDC;
					return true;
				}
				case 0x40://CMP Rwn, Rwm / 40 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.CMP;
					return true;
				}
				case 0x48://CMP Rwn, [Rwi+] / 48 n:11ii / 2 //CMP Rwn, [Rwi] / 48 n:10ii / 2 //CMP Rwn, #data3 / 48 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.CMP;
					return true;
				}
				case 0x46://CMP reg, #data16 / 46 RR ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.CMP;
					return true;
				}
				case 0x42://CMP reg, mem / 42 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.CMP;
					return true;
				}
				case 0x41://CMPB Rbn, Rbm / 41 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.CMPB;
					return true;
				}
				case 0x49://CMPB Rbn, [Rwi] / 49 n:10ii / 2 //CMPB Rbn, [Rwi+] / 49 n:11ii / 2 //CMPB Rbn, #data3 / 49 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.CMPB;
					return true;
				}
				case 0x47://CMPB reg, #data16 / 47 RR ## xx / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.CMPB;
					return true;
				}
				case 0x43://CMPB reg, mem / 43 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.CMPB;
					return true;
				}
				case 0x84://MOV [Rw], mem 			
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0x88://MOV [-Rw], Rw 	
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0x94://MOV mem, [Rw] 
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0x98://MOV Rw, [Rw+] 	
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xA8://MOV Rw, [Rw]
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xB8://MOV [Rw], Rw
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xC4://MOV [Rw+#data16], Rw
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xC8://MOV [Rw], [Rw] 
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xD4://MOV Rw, [Rw + #data16]	
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xD8://MOV [Rw+], [Rw] 	
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xE0://MOV Rw, #data4
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xE6://MOV reg, #data16
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xE8://MOV [Rw], [Rw+]
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xF0://MOV Rw, Rw
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xF2://MOV reg, mem
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xF6://MOV mem, reg
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOV;
					return true;
				}
				case 0xF1://MOVB Rbn, Rbm / F1 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xE1://MOVB Rbn, #data4 / E1 #n / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xE7://MOVB reg, #data16 / E7 RR ## xx / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xA9://MOVB Rbn, [Rwm] / A9 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0x99://MOVB Rbn, [Rwm+] / 99 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xB9://MOVB [Rwm], Rbn / B9 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0x89://MOVB [-Rwm], Rbn / 89 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xC9://MOVB [Rwn], [Rwm] / C9 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xD9://MOVB [Rwn+], [Rwm] / D9 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xE9://MOVB [Rwn], [Rwm+] / E9 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xF4://MOVB Rbn, [Rwm+#data16] / F4 nm ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xE4://MOVB [Rwm+#data16], Rbn / E4 nm ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xA4://MOVB [Rwn], mem / A4 0n MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xB4://MOVB mem, [Rwn] / B4 0n MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xF3://MOVB reg, mem / F3 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0xF7://MOVB mem, reg / F7 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.MOVB;
					return true;
				}
				case 0x50://XOR Rwn, Rwm / 50 nm / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.XOR;
					return true;
				}
				case 0x58://XOR Rwn, [Rwi+] / 58 n:11ii / 2 //XOR Rwn, [Rwi] / 58 n:10ii / 2 //XOR Rwn, #data3 / 58 n:0### / 2
				{
					numBytesForInstruction = 2;
					instruction = C166Instructions.XOR;
					return true;
				}
				case 0x56://XOR reg, #data16 / 56 RR ## ## / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.XOR;
					return true;
				}
				case 0x52://XOR reg, mem / 52 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.XOR;
					return true;
				}
				case 0x54://XOR mem, reg / 54 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					instruction = C166Instructions.XOR;
					return true;
				}
			}

			return false;
		}

		public static bool ParseEXTPInstruction(byte[] data, uint offset, out uint page, out uint numCycles, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.EXTP));
#endif
			switch (data[offset])
			{
				case 0xD7://EXTP or EXTS four bytes
				{
					numBytes = 4;

					switch (data[offset + 1] & 0xC0)
					{
						case 0x40://EXTP #pag, #irang2 / D7 :01##-0 pp 0:00pp / 4
						{
							//EXTP can have 1 to 4 cycles
							numCycles = (uint)((data[offset + 1] & 0x30) >> 4) + 1;
							page = (uint)(data[offset + 2] | ((data[offset + 3] & 0x3) << 8));

							return true;
						}
					}

					break;
				}
			}

			page = 0;
			numCycles = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseJMPAInstruction(byte[] data, uint offset, out uint address, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.JMPA));
#endif
			address = (uint)(data[offset + 2] | (data[offset + 3] << 8));
			numBytes = 4;
			return true;
		}

		public static bool ParseCALLSInstruction(byte[] data, uint offset, out uint address, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.CALLS));
#endif
			address = (uint)(data[offset + 2] | (data[offset + 3] << 8) | (data[offset + 1] << 16));
			numBytes = 4;
			return true;
		}

		public static bool ParseMOVInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
			switch (data[offset])
			{
				case 0x84://MOV [Rwn], mem / 84 0n MM MM / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x88://MOV [-Rwm], Rwn / 88 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0x94://MOV mem, [Rwn] / 94 0n MM MM / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;//register
					numBytes = 4;
					return true;
				}
				case 0x98://MOV Rwn, [Rwm+] / 98 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xA8://MOV Rwn, [Rwm] / A8 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xB8://MOV [Rwm], Rwn / B8 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xC4://MOV [Rwm+#data16], Rwn / C4 nm ## ## / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;//register
					numBytes = 4;
					return true;
				}
				case 0xC8://MOV [Rwn], [Rwm] / C8 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xD4://MOV Rwn, [Rwm+#data16] / D4 nm ## ## / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0xD8://MOV [Rwn+], [Rwm] / D8 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xE0://MOV Rwn, #data4 / E0 #n / 2
				{
					operand1 = 0;//register
					operand2 = (uint)((data[offset + 1] & 0xF0) >> 4);
					numBytes = 2;
					return true;
				}
				case 0xE6://MOV reg, #data16 / E6 RR ## ## / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0xE8://MOV [Rwn], [Rwm+] / E8 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xF0://MOV Rwn, Rwm / F0 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0xF2://MOV reg, mem / F2 RR MM MM / 4
				{
					operand1 = 0;
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0xF6://MOV mem, reg / F6 RR MM MM / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;
					numBytes = 4;
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseMOVBInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytesForInstruction)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytesForInstruction);
			Debug.Assert(result && (instruction == C166Instructions.MOVB));
#endif
			switch (data[offset])
			{
				case 0xF1://MOVB Rbn, Rbm / F1 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xE1://MOVB Rbn, #data4 / E1 #n / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = (uint)(data[offset + 1] >> 4);
					return true;
				}
				case 0xE7://MOVB reg, #data16 / E7 RR ## xx / 4
				{
					numBytesForInstruction = 4;
					operand1 = 0;
					operand2 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					return true;
				}
				case 0xA9://MOVB Rbn, [Rwm] / A9 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0x99://MOVB Rbn, [Rwm+] / 99 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xB9://MOVB [Rwm], Rbn / B9 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0x89://MOVB [-Rwm], Rbn / 89 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xC9://MOVB [Rwn], [Rwm] / C9 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xD9://MOVB [Rwn+], [Rwm] / D9 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xE9://MOVB [Rwn], [Rwm+] / E9 nm / 2
				{
					numBytesForInstruction = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0xF4://MOVB Rbn, [Rwm+#data16] / F4 nm ## ## / 4
				{
					numBytesForInstruction = 4;
					operand1 = 0;
					operand2 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					return true;
				}
				case 0xE4://MOVB [Rwm+#data16], Rbn / E4 nm ## ## / 4
				{
					numBytesForInstruction = 4;
					operand1 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					operand2 = 0;
					return true;
				}
				case 0xA4://MOVB [Rwn], mem / A4 0n MM MM / 4
				{
					numBytesForInstruction = 4;
					operand1 = 0;
					operand2 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					return true;
				}
				case 0xB4://MOVB mem, [Rwn] / B4 0n MM MM / 4
				{
					numBytesForInstruction = 4;
					operand1 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					operand2 = 0;
					return true;
				}
				case 0xF3://MOVB reg, mem / F3 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					operand1 = 0;
					operand2 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					return true;
				}
				case 0xF7://MOVB mem, reg / F7 RR MM MM / 4
				{
					numBytesForInstruction = 4;
					operand1 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					operand2 = 0;
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytesForInstruction = 1;
			return false;
		}

		public static bool ParseCMPBInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.CMPB));
#endif
			switch (data[offset])
			{
				case 0x41://CMPB Rbn, Rbm / 41 nm / 2
				{
					numBytes = 2;
					operand1 = 0;//register
					operand2 = 0;//register
					return true;
				}
				case 0x49://CMPB Rbn, [Rwi] / 49 n:10ii / 2 //CMPB Rbn, [Rwi+] / 49 n:11ii / 2 //CMPB Rbn, #data3 / 49 n:0### / 2
				{
					numBytes = 2;

					switch (data[offset + 1] & 0x0C)
					{
						case 0x08:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x0C:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x04:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
						case 0x00:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
					}

					break;
				}
				case 0x47://CMPB reg, #data16 / 47 RR ## xx / 4
				{
					numBytes = 4;
					operand1 = 0;//register
					operand2 = data[offset + 2] | (uint)(data[offset + 3] << 8);
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseCMPInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.CMP));
#endif
			switch (data[offset])
			{
				case 0x40://CMP Rwn, Rwm / 40 nm / 2
				{
					numBytes = 2;
					operand1 = 0;
					operand2 = 0;
					return true;
				}
				case 0x48://CMP Rwn, [Rwi+] / 48 n:11ii / 2 //CMP Rwn, [Rwi] / 48 n:10ii / 2 //CMP Rwn, #data3 / 48 n:0### / 2
				{
					numBytes = 2;

					switch (data[offset + 1] & 0x0C)
					{
						case 0x08:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x0C:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x04:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
						case 0x00:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
					}

					break;
				}
				case 0x46://CMP reg, #data16 / 46 RR ## ## / 4
				{
					numBytes = 4;
					operand1 = 0;//register
					operand2 = (uint)((data[offset + 3] << 8) | data[offset + 2]);
					return true;
				}
				case 0x42://CMP reg, mem / 42 RR MM MM / 4
				{
					numBytes = 4;
					operand1 = 0;//register
					operand2 = (uint)((data[offset + 3] << 8) | data[offset + 2]);
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseSUBInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.SUB));
#endif

			switch (data[offset])
			{
				case 0x20://SUB Rwn, Rwm / 20 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0x28://SUB Rwn, [Rwi] / 28 n:10ii / 2 //SUB Rwn, [Rwi+] / 28 n:11ii / 2 //SUB Rwn, #data3 / 28 n:0### / 2
				{
					numBytes = 2;

					switch (data[offset + 1] & 0x0C)
					{
						case 0x08:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x0C:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x04:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
						case 0x00:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
					}

					break;
				}
				case 0x26://SUB reg, #data16 / 26 RR ## ## / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x22://SUB reg, mem / 22 RR MM MM / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x24://SUB mem, reg / 24 RR MM MM / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;//register					
					numBytes = 4;
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseADDInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.ADD));
#endif

			switch (data[offset])
			{
				case 0x00://ADD Rwn, Rwm / 00 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0x08://ADD Rwn, [Rwi+] / 08 n:11ii / 2 //ADD Rwn, [Rwi] / 08 n:10ii / 2//ADD Rwn, #data3 / 08 n:0### / 2
				{
					numBytes = 2;

					switch (data[offset + 1] & 0x0C)
					{
						case 0x08:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x0C:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x04:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
						case 0x00:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
					}

					break;
				}
				case 0x06://ADD reg, #data16 / 06 RR ## ## / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x02://ADD reg, mem / 02 RR MM MM / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x04://ADD mem, reg / 04 RR MM MM / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;//register					
					numBytes = 4;
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static bool ParseADDCInstruction(byte[] data, uint offset, out uint operand1, out uint operand2, out uint numBytes)
		{
#if DEBUG
			C166Instructions instruction;
			bool result = ParseInstruction(data, offset, out instruction, out numBytes);
			Debug.Assert(result && (instruction == C166Instructions.ADDC));
#endif

			switch (data[offset])
			{
				case 0x10://ADDC Rwn, Rwm / 10 nm / 2
				{
					operand1 = 0;//register
					operand2 = 0;//register
					numBytes = 2;
					return true;
				}
				case 0x18://ADDC Rwn, [Rwi+] / 18 n:11ii / 2 //ADDC Rwn, [Rwi] / 18 n:10ii / 2 //ADDC Rwn, #data3 / 18 n:0### / 2
				{
					numBytes = 2;

					switch (data[offset + 1] & 0x0C)
					{
						case 0x08:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x0C:
						{
							operand1 = 0;//register
							operand2 = 0;//register
							return true;
						}
						case 0x04:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
						case 0x00:
						{
							operand1 = 0;//register
							operand2 = (uint)(data[offset + 1] & 0x7);
							return true;
						}
					}

					break;
				}
				case 0x16://ADDC reg, #data16 / 16 RR ## ## / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x12://ADDC reg, mem / 12 RR MM MM / 4
				{
					operand1 = 0;//register
					operand2 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					numBytes = 4;
					return true;
				}
				case 0x14://ADDC mem, reg / 14 RR MM MM / 4
				{
					operand1 = (uint)(data[offset + 2] | (data[offset + 3] << 8));
					operand2 = 0;//register					
					numBytes = 4;
					return true;
				}
			}

			operand1 = 0;
			operand2 = 0;
			numBytes = 1;
			return false;
		}

		public static int FindNextInstruction(byte[] data, uint offset, C166Instructions instruction, out uint numBytes)
		{
			numBytes = 2;

			for (uint x = offset; x < data.Length; x += numBytes)
			{
				C166Instructions curInstruction;
				if (ParseInstruction(data, x, out curInstruction, out numBytes))
				{
					if (curInstruction == instruction)
					{
						return (int)x;
					}
				}
				else
				{
					numBytes = 2;
				}
			}

			return -1;
		}

		public static int FindPrevInstructionSequenceStart(byte[] data, uint startOffset, int endOffset, IEnumerable<C166Instructions> sequence, out IEnumerable<uint> instructionSizes)
		{
			var numBytesPer = new List<uint>();
			instructionSizes = numBytesPer;

			if (endOffset < 0)
			{
				endOffset = 0;
			}

			for (uint x = startOffset - 2; x >= endOffset; x -= 2)
			{
				uint curAddress = x;
				bool foundInstruction = false;

				foreach (var instruction in sequence.Reverse())
				{
					curAddress -= 2;

					uint numBytes;
					foundInstruction = false;

					C166Instructions curInstruction;
					if (ParseInstruction(data, curAddress, out curInstruction, out numBytes) && (numBytes == 2) && (curInstruction == instruction))
					{
						foundInstruction = true;
					}
					else if (ParseInstruction(data, curAddress - 2, out curInstruction, out numBytes) && (numBytes == 4) && (curInstruction == instruction))
					{
						curAddress -= 2;
						foundInstruction = true;
					}

					if (foundInstruction)
					{
						numBytesPer.Insert(0, numBytes);
					}
					else
					{
						break;
					}
				}

				if (foundInstruction)
				{
					return (int)curAddress;
				}
				else
				{
					numBytesPer.Clear();
				}
			}

			return -1;
		}
	}
}