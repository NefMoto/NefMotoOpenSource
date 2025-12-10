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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Shared;

namespace Communication
{
    /// <summary>
    /// Settings for boot mode flash operations
    /// </summary>
    public class BootModeFlashSettings
    {
        public bool CheckIfWriteRequired = true;
        public bool OnlyWriteNonMatchingSectors = false;
        public bool VerifyWrittenData = true;
        public bool EraseSectorsBeforeWrite = true;
        public uint RamBufferAddress = 0x10000; // Default RAM buffer address for MiniMon
    }

    /// <summary>
    /// Operation for writing external flash via boot mode
    /// </summary>
    public class WriteExternalFlashBootModeOperation : CommunicationOperation
    {
        public WriteExternalFlashBootModeOperation(BootstrapInterface commInterface, BootModeFlashSettings writeSettings, IEnumerable<MemoryImage> sectorImages)
            : base(commInterface)
        {
            mCommInterface = commInterface;
            mWriteSettings = writeSettings;
            mFlashBlockList = new List<FlashBlock>();

            ShouldCheckIfFlashRequired = writeSettings.CheckIfWriteRequired;
            OnlyFlashRequiredSectors = writeSettings.OnlyWriteNonMatchingSectors;
            ShouldVerifyFlashedSectors = writeSettings.VerifyWrittenData;
            mEraseSectorsBeforeWrite = writeSettings.EraseSectorsBeforeWrite;
            mRamBufferAddress = writeSettings.RamBufferAddress;

            mTotalBytesToFlash = 0;

            foreach (var image in sectorImages)
            {
                Debug.Assert(image.StartAddress % 2 == 0, "start address is not a multiple of 2");
                Debug.Assert(image.Size > 0, "size is zero");
                Debug.Assert(image.Size % 2 == 0, "size is not a multiple of 2");

                var block = new FlashBlock();
                block.mMemoryImage = image;
                block.mFlashingIsRequired = !ShouldCheckIfFlashRequired;
                block.mWasErased = false;
                block.mFlashComplete = false;
                block.mFlashSuccessful = false;
                block.mNumFlashAttempts = 0;
                block.mNumBytesFlashed = 0;

                mFlashBlockList.Add(block);
                mTotalBytesToFlash += image.Size;
            }

            mCurrentBlock = mFlashBlockList.FirstOrDefault();
            mState = FlashingState.StartBlock;
        }

        public bool ShouldCheckIfFlashRequired { get; private set; }
        public bool OnlyFlashRequiredSectors { get; private set; }
        public bool ShouldVerifyFlashedSectors { get; private set; }
        public uint TotalBytesToFlash { get { return mTotalBytesToFlash; } }
        public uint TotalBytesFlashed { get { return mTotalBytesFlashed; } }

        protected override CommunicationAction NextAction()
        {
            CommunicationAction nextAction = null;

            if (mCurrentBlock == null)
            {
                return null;
            }

            switch (mState)
            {
                case FlashingState.StartBlock:
                {
                    mState = ShouldCheckIfFlashRequired ? FlashingState.CheckIfFlashRequired : FlashingState.EraseFlash;
                    return NextAction(); // Recursive call to continue
                }

                case FlashingState.CheckIfFlashRequired:
                {
                    // Read current flash contents and compare
                    uint endAddress = mCurrentBlock.mMemoryImage.StartAddress + (uint)mCurrentBlock.mMemoryImage.RawData.Length;
                    CommInterface.DisplayStatusMessage("Checking if flash is required for range: 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);

                    return new ReadFlashBlockBootModeAction(mCommInterface, mCurrentBlock.mMemoryImage.StartAddress, (uint)mCurrentBlock.mMemoryImage.RawData.Length, (data) =>
                    {
                        // Compare data
                        bool dataMatches = data != null &&
                                          data.Length == mCurrentBlock.mMemoryImage.RawData.Length &&
                                          data.SequenceEqual(mCurrentBlock.mMemoryImage.RawData);

                        if (dataMatches)
                        {
                            CommInterface.DisplayStatusMessage("Flash data matches, skipping sector", StatusMessageType.USER);
                            mCurrentBlock.mFlashComplete = true;
                            mCurrentBlock.mFlashSuccessful = true;
                            mState = FlashingState.FinishedBlock;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Flash data differs, flashing required", StatusMessageType.USER);
                            mCurrentBlock.mFlashingIsRequired = true;
                            mState = FlashingState.EraseFlash;
                        }
                    });
                }

                case FlashingState.EraseFlash:
                {
                    if (mEraseSectorsBeforeWrite && !mCurrentBlock.mWasErased)
                    {
                        CommInterface.DisplayStatusMessage("Erasing flash sector at 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8"), StatusMessageType.USER);

                        return new EraseFlashSectorBootModeAction(mCommInterface, mCurrentBlock.mMemoryImage.StartAddress, (uint)mCurrentBlock.mMemoryImage.RawData.Length, (success) =>
                        {
                            if (success)
                            {
                                mCurrentBlock.mWasErased = true;
                                mState = FlashingState.ProgramFlash;
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Flash erase failed", StatusMessageType.USER);
                                mCurrentBlock.mFlashComplete = true;
                                mCurrentBlock.mFlashSuccessful = false;
                                mState = FlashingState.FinishedBlock;
                            }
                        });
                    }
                    else
                    {
                        mState = FlashingState.ProgramFlash;
                        return NextAction(); // Recursive call to continue
                    }
                }

                case FlashingState.ProgramFlash:
                {
                    CommInterface.DisplayStatusMessage("Programming flash at 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8"), StatusMessageType.USER);

                    return new ProgramFlashBlockBootModeAction(mCommInterface, mCurrentBlock.mMemoryImage.StartAddress, mCurrentBlock.mMemoryImage.RawData, mRamBufferAddress, (success) =>
                    {
                        if (success)
                        {
                            mCurrentBlock.mNumBytesFlashed = (uint)mCurrentBlock.mMemoryImage.RawData.Length;
                            mTotalBytesFlashed += mCurrentBlock.mNumBytesFlashed;
                            mState = ShouldVerifyFlashedSectors ? FlashingState.VerifyFlash : FlashingState.FinishedBlock;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Flash programming failed", StatusMessageType.USER);
                            mCurrentBlock.mFlashComplete = true;
                            mCurrentBlock.mFlashSuccessful = false;
                            mState = FlashingState.FinishedBlock;
                        }
                    });
                }

                case FlashingState.VerifyFlash:
                {
                    CommInterface.DisplayStatusMessage("Verifying flashed data at 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8"), StatusMessageType.USER);

                    return new ReadFlashBlockBootModeAction(mCommInterface, mCurrentBlock.mMemoryImage.StartAddress, (uint)mCurrentBlock.mMemoryImage.RawData.Length, (verifyData) =>
                    {
                        if (verifyData != null)
                        {
                            bool dataMatches = verifyData.Length == mCurrentBlock.mMemoryImage.RawData.Length &&
                                              verifyData.SequenceEqual(mCurrentBlock.mMemoryImage.RawData);

                            if (dataMatches)
                            {
                                CommInterface.DisplayStatusMessage("Flash verification successful", StatusMessageType.USER);
                                mCurrentBlock.mFlashComplete = true;
                                mCurrentBlock.mFlashSuccessful = true;
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Flash verification failed - data mismatch", StatusMessageType.USER);
                                mCurrentBlock.mFlashComplete = true;
                                mCurrentBlock.mFlashSuccessful = false;
                            }
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Flash verification failed - could not read flash", StatusMessageType.USER);
                            mCurrentBlock.mFlashComplete = true;
                            mCurrentBlock.mFlashSuccessful = false;
                        }

                        mState = FlashingState.FinishedBlock;
                    });
                }

                case FlashingState.FinishedBlock:
                {
                    // Move to next block
                    var nextBlock = mFlashBlockList.SkipWhile(b => b != mCurrentBlock).Skip(1).FirstOrDefault();

                    if (nextBlock != null)
                    {
                        mCurrentBlock = nextBlock;
                        mState = FlashingState.StartBlock;
                        return NextAction(); // Recursive call to continue with next block
                    }
                    else
                    {
                        // All blocks processed
                        mState = FlashingState.FinishedAll;
                        return null;
                    }
                }

                case FlashingState.FinishedAll:
                {
                    return null;
                }
            }

            return nextAction;
        }

        protected override void ResetOperation()
        {
            mState = FlashingState.StartBlock;
            mCurrentBlock = mFlashBlockList.FirstOrDefault();
            mTotalBytesFlashed = 0;
            base.ResetOperation();
        }

        protected override bool OnOperationCompleted(bool success)
        {
            int successfulBlocks = mFlashBlockList.Count(b => b.mFlashSuccessful);
            int totalBlocks = mFlashBlockList.Count;

            CommInterface.DisplayStatusMessage($"Boot mode flash operation completed: {successfulBlocks}/{totalBlocks} sectors successful", StatusMessageType.USER);

            return base.OnOperationCompleted(success && (successfulBlocks == totalBlocks));
        }

        private enum FlashingState
        {
            StartBlock,
            CheckIfFlashRequired,
            EraseFlash,
            ProgramFlash,
            VerifyFlash,
            FinishedBlock,
            FinishedAll
        }

        private class FlashBlock
        {
            public MemoryImage mMemoryImage;
            public bool mFlashingIsRequired;
            public bool mWasErased;
            public bool mFlashComplete;
            public bool mFlashSuccessful;
            public int mNumFlashAttempts;
            public uint mNumBytesFlashed;
        }

        private BootstrapInterface mCommInterface;
        private BootModeFlashSettings mWriteSettings;
        private List<FlashBlock> mFlashBlockList;
        private FlashBlock mCurrentBlock;
        private FlashingState mState;
        private bool mEraseSectorsBeforeWrite;
        private uint mRamBufferAddress;
        private uint mTotalBytesToFlash;
        private uint mTotalBytesFlashed;
    }

    /// <summary>
    /// Action to read a flash block via boot mode
    /// </summary>
    public class ReadFlashBlockBootModeAction : CommunicationAction
    {
        public ReadFlashBlockBootModeAction(BootstrapInterface commInterface, uint address, uint size, Action<byte[]> onComplete)
            : base(commInterface)
        {
            mCommInterface = commInterface;
            mAddress = address;
            mSize = size;
            mOnComplete = onComplete;
        }

        public override bool Start()
        {
            if (!base.Start())
            {
                return false;
            }

            // Execute the read operation asynchronously
            Task.Run(() =>
            {
                byte[] data;
                bool success = mCommInterface.ReadFlashBlock(mAddress, mSize, out data);

                if (success && mOnComplete != null)
                {
                    mOnComplete(data);
                }

                ActionCompleted(success);
            });

            return true;
        }

        private BootstrapInterface mCommInterface;
        private uint mAddress;
        private uint mSize;
        private Action<byte[]> mOnComplete;
    }

    /// <summary>
    /// Action to erase a flash sector via boot mode
    /// </summary>
    public class EraseFlashSectorBootModeAction : CommunicationAction
    {
        public EraseFlashSectorBootModeAction(BootstrapInterface commInterface, uint address, uint size, Action<bool> onComplete)
            : base(commInterface)
        {
            mCommInterface = commInterface;
            mAddress = address;
            mSize = size;
            mOnComplete = onComplete;
        }

        public override bool Start()
        {
            if (!base.Start())
            {
                return false;
            }

            // Execute the erase operation asynchronously
            Task.Run(() =>
            {
                bool success = mCommInterface.EraseFlashSector(mAddress, mSize);

                if (mOnComplete != null)
                {
                    mOnComplete(success);
                }

                ActionCompleted(success);
            });

            return true;
        }

        private BootstrapInterface mCommInterface;
        private uint mAddress;
        private uint mSize;
        private Action<bool> mOnComplete;
    }

    /// <summary>
    /// Action to program a flash block via boot mode
    /// </summary>
    public class ProgramFlashBlockBootModeAction : CommunicationAction
    {
        public ProgramFlashBlockBootModeAction(BootstrapInterface commInterface, uint address, byte[] data, uint ramBufferAddress, Action<bool> onComplete)
            : base(commInterface)
        {
            mCommInterface = commInterface;
            mAddress = address;
            mData = data;
            mRamBufferAddress = ramBufferAddress;
            mOnComplete = onComplete;
        }

        public override bool Start()
        {
            if (!base.Start())
            {
                return false;
            }

            // Execute the program operation asynchronously
            Task.Run(() =>
            {
                bool success = mCommInterface.ProgramFlashBlock(mAddress, mData, mRamBufferAddress);

                if (mOnComplete != null)
                {
                    mOnComplete(success);
                }

                ActionCompleted(success);
            });

            return true;
        }

        private BootstrapInterface mCommInterface;
        private uint mAddress;
        private byte[] mData;
        private uint mRamBufferAddress;
        private Action<bool> mOnComplete;
    }
}
