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
using System.Threading;
using Shared;

namespace Communication
{
    /// <summary>
    /// Bootmode flash read operation.
    ///
    /// NOTE: Diff read (skipping matching sectors) is NOT supported for BootMode.
    /// Unlike KWP2000, BootMode/MINIMONK does not provide a service to calculate
    /// checksums of flash memory ranges before reading. Therefore, all sectors
    /// must be read regardless of whether they match the base image.
    ///
    /// Checksum verification after reading IS supported via MiniMon_GetChecksum(),
    /// but this is not yet implemented in this operation.
    /// </summary>
    public class BootmodeReadExternalFlashOperation : CommunicationOperation
    {
        public class BootmodeReadExternalFlashSettings
        {
            public BootstrapInterface.ECUFlashVariant Variant = BootstrapInterface.ECUFlashVariant.ME7;
            public uint StartAddress = 0;
            public uint Size = 0;
        }

        public BootmodeReadExternalFlashOperation(BootstrapInterface commInterface, BootmodeReadExternalFlashSettings readSettings, IEnumerable<MemoryImage> flashBlockList)
            : base(commInterface)
        {
            mBootstrapInterface = commInterface;
            mSettings = readSettings;
            mFlashBlockList = flashBlockList;
            mCurrentBlock = mFlashBlockList.GetEnumerator();
            mCurrentBlock.MoveNext();
            mState = ReadingState.Start;

            // Calculate total bytes to read
            mTotalBytesToRead = 0;
            foreach (var block in flashBlockList)
            {
                mTotalBytesToRead += (uint)block.Size;
            }
            mTotalBytesRead = 0;

            // Create a NOP action that will stay "running" while our thread does the work
            mNOPAction = new NOPCommunicationAction(commInterface);
        }

        protected override void ResetOperation()
        {
            mState = ReadingState.Start;
            mCurrentBlock = mFlashBlockList.GetEnumerator();
            mCurrentBlock.MoveNext();
            mTotalBytesRead = 0;
        }

        protected override CommunicationAction NextAction()
        {
            if (!IsRunning)
            {
                return null;
            }

            switch (mState)
            {
                case ReadingState.Start:
                    mState = ReadingState.Reading;
                    // Start reading in a background thread
                    Thread readThread = new Thread(() => ReadFlashThread());
                    readThread.IsBackground = true;
                    readThread.Start();
                    // Return the NOP action so the base class doesn't think we're done
                    return mNOPAction;

                case ReadingState.Reading:
                    // Reading is happening in background thread - keep returning NOP action
                    return mNOPAction;

                case ReadingState.Finished:
                    // Complete the NOP action and then complete the operation
                    mNOPAction.ActionCompleted(true);
                    OperationCompleted(true);
                    return null;

                case ReadingState.Failed:
                    // Complete the NOP action and then complete the operation
                    mNOPAction.ActionCompleted(false);
                    OperationCompleted(false);
                    return null;

                default:
                    return null;
            }
        }

        private void ReadFlashThread()
        {
            try
            {
                CommInterface.DisplayStatusMessage("ReadFlashThread: Starting flash read thread", StatusMessageType.LOG);
                CommInterface.DisplayStatusMessage($"ReadFlashThread: Settings - Variant={mSettings.Variant}, StartAddress=0x{mSettings.StartAddress:X6}, Size={mSettings.Size}", StatusMessageType.LOG);
                CommInterface.DisplayStatusMessage($"ReadFlashThread: Total blocks to read: {mFlashBlockList.Count()}, Total bytes: {mTotalBytesToRead}", StatusMessageType.LOG);

                bool success = true;
                var readSectorData = new List<byte[]>();
                int blockIndex = 0;
                uint bytesReadFromPreviousBlocks = 0;

                foreach (var block in mFlashBlockList)
                {
                    blockIndex++;
                    if (!IsRunning)
                    {
                        CommInterface.DisplayStatusMessage($"ReadFlashThread: Operation stopped, aborting at block {blockIndex}", StatusMessageType.LOG);
                        success = false;
                        break;
                    }

                    uint blockStartAddress = (uint)(block.StartAddress - mSettings.StartAddress);
                    uint blockSize = (uint)block.Size;

                    CommInterface.DisplayStatusMessage($"ReadFlashThread: Block {blockIndex}/{mFlashBlockList.Count()}: StartAddress=0x{block.StartAddress:X6}, Size={blockSize}, Offset=0x{blockStartAddress:X6}", StatusMessageType.LOG);

                    byte[] blockData;
                    if (mBootstrapInterface.ReadExternalFlash(
                        mSettings.Variant,
                        blockStartAddress,
                        blockSize,
                        out blockData,
                        (bytesReadInBlock, totalBytesInBlock) =>
                        {
                            // Calculate overall progress: bytes from previous blocks + bytes read in current block
                            uint totalBytesReadSoFar = bytesReadFromPreviousBlocks + bytesReadInBlock;
                            if (mTotalBytesToRead > 0)
                            {
                                float percentComplete = ((float)totalBytesReadSoFar / (float)mTotalBytesToRead) * 100.0f;
                                OnUpdatePercentComplete(percentComplete);
                            }
                        },
                        blockIndex,  // sectorNumber
                        mFlashBlockList.Count()))  // totalSectors
                    {
                        CommInterface.DisplayStatusMessage($"ReadFlashThread: Block {blockIndex} read successfully, got {blockData?.Length ?? 0} bytes", StatusMessageType.LOG);
                        readSectorData.Add(blockData);
                        bytesReadFromPreviousBlocks += blockSize;
                        mTotalBytesRead = bytesReadFromPreviousBlocks;

                        // Update overall progress to 100% for this block
                        if (mTotalBytesToRead > 0)
                        {
                            float percentComplete = ((float)mTotalBytesRead / (float)mTotalBytesToRead) * 100.0f;
                            OnUpdatePercentComplete(percentComplete);
                        }
                    }
                    else
                    {
                        CommInterface.DisplayStatusMessage($"ReadFlashThread: Block {blockIndex} read FAILED at offset 0x{blockStartAddress:X6}, size {blockSize}", StatusMessageType.LOG);
                        CommInterface.DisplayStatusMessage($"ReadFlashThread: ReadExternalFlash returned false for block {blockIndex}", StatusMessageType.USER);
                        success = false;
                        break;
                    }
                }

                if (success)
                {
                    CommInterface.DisplayStatusMessage($"ReadFlashThread: All blocks read successfully. Combining {readSectorData.Count} blocks into memory image...", StatusMessageType.LOG);
                    // Combine all blocks into a single memory image
                    if (readSectorData.Count > 0)
                    {
                        int totalSize = 0;
                        foreach (var blockData in readSectorData)
                        {
                            totalSize += blockData.Length;
                        }

                        CommInterface.DisplayStatusMessage($"ReadFlashThread: Total combined size: {totalSize} bytes", StatusMessageType.LOG);

                        byte[] combinedData = new byte[totalSize];
                        int offset = 0;
                        foreach (var blockData in readSectorData)
                        {
                            Array.Copy(blockData, 0, combinedData, offset, blockData.Length);
                            offset += blockData.Length;
                        }

                        mReadFlashMemory = new MemoryImage(combinedData, mSettings.StartAddress);
                        CommInterface.DisplayStatusMessage($"ReadFlashThread: Memory image created successfully at address 0x{mSettings.StartAddress:X6}", StatusMessageType.LOG);
                    }
                    else
                    {
                        CommInterface.DisplayStatusMessage("ReadFlashThread: WARNING - No blocks were read successfully!", StatusMessageType.LOG);
                    }

                    mState = ReadingState.Finished;
                    CommInterface.DisplayStatusMessage("ReadFlashThread: Operation completed successfully", StatusMessageType.LOG);
                }
                else
                {
                    CommInterface.DisplayStatusMessage($"ReadFlashThread: Operation FAILED after reading {readSectorData.Count} blocks", StatusMessageType.LOG);
                    mState = ReadingState.Failed;
                }

                // Trigger NextAction to check state
                StartNextAction();
            }
            catch (Exception ex)
            {
                CommInterface.DisplayStatusMessage($"ReadFlashThread: EXCEPTION - {ex.GetType().Name}: {ex.Message}", StatusMessageType.USER);
                CommInterface.DisplayStatusMessage($"ReadFlashThread: Stack trace: {ex.StackTrace}", StatusMessageType.LOG);
                if (ex.InnerException != null)
                {
                    CommInterface.DisplayStatusMessage($"ReadFlashThread: Inner exception: {ex.InnerException.Message}", StatusMessageType.LOG);
                }
                mState = ReadingState.Failed;
                StartNextAction();
            }
        }

        public IEnumerable<MemoryImage> FlashBlockList
        {
            get
            {
                return mFlashBlockList;
            }
        }

        private enum ReadingState
        {
            Start,
            Reading,
            Finished,
            Failed
        }

        private BootstrapInterface mBootstrapInterface;
        private BootmodeReadExternalFlashSettings mSettings;
        private IEnumerable<MemoryImage> mFlashBlockList;
        private IEnumerator<MemoryImage> mCurrentBlock;
        private ReadingState mState;
        private uint mTotalBytesToRead;
        private uint mTotalBytesRead;
        public MemoryImage mReadFlashMemory;
        private NOPCommunicationAction mNOPAction;

        /// <summary>
        /// A NOP (No Operation) communication action that stays "running" until explicitly completed.
        /// Used to prevent the base class from thinking the operation is complete when we're using a background thread.
        /// </summary>
        private class NOPCommunicationAction : CommunicationAction
        {
            public NOPCommunicationAction(CommunicationInterface commInterface)
                : base(commInterface)
            {
                // Base constructor sets IsComplete = true, but Start() will set it to false
            }

            public override bool Start()
            {
                // Call base Start() which sets IsComplete = false and makes it "running"
                return base.Start();
            }

            public new void ActionCompleted(bool success)
            {
                // Complete the action explicitly
                ActionCompletedInternal(success, false);
            }
        }
    }
}
