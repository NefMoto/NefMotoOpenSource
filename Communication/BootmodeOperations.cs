/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

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
    /// NOP communication action that stays "running" until explicitly completed.
    /// Used by bootmode ops that do work on a background thread.
    /// </summary>
    internal sealed class BootmodeNopCommunicationAction : CommunicationAction
    {
        public BootmodeNopCommunicationAction(CommunicationInterface commInterface)
            : base(commInterface)
        {
        }

        public override bool Start()
        {
            return base.Start();
        }

        public new void ActionCompleted(bool success)
        {
            ActionCompletedInternal(success, false);
        }
    }

    /// <summary>
    /// Bootmode operation that runs work on a background thread behind a NOP action.
    /// Subclasses implement <see cref="RunWorker"/> and return success/failure.
    /// </summary>
    public abstract class BootmodeBackgroundOperation : CommunicationOperation
    {
        protected BootmodeBackgroundOperation(CommunicationInterface commInterface, string operationLabel)
            : base(commInterface)
        {
            mOperationLabel = operationLabel ?? "Bootmode operation";
            mNOPAction = new BootmodeNopCommunicationAction(commInterface);
            mState = WorkerState.Start;
        }

        /// <summary>Perform the work; return true on success. Do not call StartNextAction.</summary>
        protected abstract bool RunWorker();

        /// <summary>Optional subclass reset (state is reset to Start by the base).</summary>
        protected virtual void OnResetWorker()
        {
        }

        protected void ReportProgress(uint bytesDone, uint totalBytes)
        {
            if (totalBytes > 0)
            {
                OnUpdatePercentComplete(((float)bytesDone / (float)totalBytes) * 100.0f);
            }
        }

        protected override void ResetOperation()
        {
            mState = WorkerState.Start;
            OnResetWorker();
        }

        protected override CommunicationAction NextAction()
        {
            if (!IsRunning)
            {
                return null;
            }

            switch (mState)
            {
                case WorkerState.Start:
                    mState = WorkerState.Running;
                    Thread worker = new Thread(() => WorkerThread());
                    worker.IsBackground = true;
                    worker.Start();
                    return mNOPAction;

                case WorkerState.Running:
                    return mNOPAction;

                case WorkerState.Finished:
                    mNOPAction.ActionCompleted(true);
                    OperationCompleted(true);
                    return null;

                case WorkerState.Failed:
                    mNOPAction.ActionCompleted(false);
                    OperationCompleted(false);
                    return null;

                default:
                    return null;
            }
        }

        private void WorkerThread()
        {
            try
            {
                bool success = RunWorker();
                mState = success ? WorkerState.Finished : WorkerState.Failed;
            }
            catch (Exception ex)
            {
                CommInterface.DisplayStatusMessage($"{mOperationLabel} exception: {ex.Message}", StatusMessageType.USER);
                CommInterface.DisplayStatusMessage(ex.StackTrace, StatusMessageType.LOG);
                mState = WorkerState.Failed;
            }

            StartNextAction();
        }

        private enum WorkerState
        {
            Start,
            Running,
            Finished,
            Failed
        }

        private readonly string mOperationLabel;
        private WorkerState mState;
        private readonly BootmodeNopCommunicationAction mNOPAction;
    }

    /// <summary>
    /// Bootmode flash read operation. Diff read not supported (MiniMon has no checksum-for-range); all sectors read.
    /// Checksum verification via MiniMon_GetChecksum() not yet implemented.
    /// </summary>
    /// <remarks>TODO: On block read failure, retry that block N times (e.g. 2–3) with short delay before failing operation. Implement in ReadFlashThread.</remarks>
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
            mNOPAction = new BootmodeNopCommunicationAction(commInterface);
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
        private BootmodeNopCommunicationAction mNOPAction;
    }

    /// <summary>
    /// Bootmode flash write operation: erase sectors and program blocks.
    /// Load flash driver before starting. Skips blocks that are all 0xFF per Python reference.
    /// </summary>
    /// <remarks>TODO: On sector erase or block program failure, retry that sector M times before failing. Implement in WriteFlashThread.</remarks>
    public class BootmodeWriteExternalFlashOperation : CommunicationOperation
    {
        public class BootmodeWriteExternalFlashSettings
        {
            public BootstrapInterface.ECUFlashVariant Variant = BootstrapInterface.ECUFlashVariant.ME7;
            public MemoryLayout FlashMemoryLayout;
        }

        public BootmodeWriteExternalFlashOperation(BootstrapInterface commInterface, BootmodeWriteExternalFlashSettings writeSettings, IEnumerable<MemoryImage> sectorImages)
            : base(commInterface)
        {
            mBootstrapInterface = commInterface;
            mSettings = writeSettings;
            mSectorImages = sectorImages;
            mState = WritingState.Start;

            mTotalSectors = sectorImages.Count();
            mTotalBytesToWrite = 0;
            foreach (var sector in sectorImages)
            {
                mTotalBytesToWrite += (uint)sector.Size;
            }
            mBytesWritten = 0;

            mNOPAction = new BootmodeNopCommunicationAction(commInterface);
        }

        public int NumSectors { get { return mTotalSectors; } }
        public int NumSuccessfullyFlashedSectors { get { return mNumSuccessfullyFlashedSectors; } }

        protected override void ResetOperation()
        {
            mState = WritingState.Start;
            mNumSuccessfullyFlashedSectors = 0;
            mBytesWritten = 0;
        }

        protected override CommunicationAction NextAction()
        {
            if (!IsRunning)
            {
                return null;
            }

            switch (mState)
            {
                case WritingState.Start:
                    mState = WritingState.Writing;
                    Thread writeThread = new Thread(() => WriteFlashThread());
                    writeThread.IsBackground = true;
                    writeThread.Start();
                    return mNOPAction;

                case WritingState.Writing:
                    return mNOPAction;

                case WritingState.Finished:
                    mNOPAction.ActionCompleted(true);
                    OperationCompleted(true);
                    return null;

                case WritingState.Failed:
                    mNOPAction.ActionCompleted(false);
                    OperationCompleted(false);
                    return null;

                default:
                    return null;
            }
        }

        private static bool IsBlockAllFF(byte[] block)
        {
            if (block == null) return true;
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] != 0xFF) return false;
            }
            return true;
        }

        private void WriteFlashThread()
        {
            try
            {
                CommInterface.DisplayStatusMessage("WriteFlashThread: Starting bootmode flash write", StatusMessageType.LOG);

                uint writeAddressBase = mBootstrapInterface.GetFlashWriteBaseAddress(mSettings.Variant);
                uint readAddressBase = mBootstrapInterface.GetFlashBaseAddress(mSettings.Variant);

                if (!mBootstrapInterface.ConfigureRegistersForExternalFlashWrite(mSettings.Variant))
                {
                    CommInterface.DisplayStatusMessage("WriteFlashThread: Failed to configure registers for write", StatusMessageType.USER);
                    mState = WritingState.Failed;
                    StartNextAction();
                    return;
                }

                uint sectorOffset = 0;
                int sectorIndex = 0;
                const int blockSize = 0x200;  // 512 bytes

                foreach (var sectorImage in mSectorImages)
                {
                    sectorIndex++;
                    if (!IsRunning)
                    {
                        CommInterface.DisplayStatusMessage($"WriteFlashThread: Operation stopped at sector {sectorIndex}", StatusMessageType.LOG);
                        mState = WritingState.Failed;
                        StartNextAction();
                        return;
                    }

                    uint sectorSize = (uint)sectorImage.Size;
                    byte[] sectorData = sectorImage.RawData;

                    CommInterface.DisplayStatusMessage($"WriteFlashThread: Erasing sector {sectorIndex}/{mTotalSectors} (offset 0x{sectorOffset:X6}, size {sectorSize})", StatusMessageType.LOG);
                    if (!mBootstrapInterface.EraseSector(writeAddressBase, readAddressBase, sectorOffset, sectorIndex - 1, sectorSize))
                    {
                        CommInterface.DisplayStatusMessage($"WriteFlashThread: Erase failed for sector {sectorIndex}", StatusMessageType.USER);
                        mState = WritingState.Failed;
                        StartNextAction();
                        return;
                    }

                    int blocksInSector = (int)((sectorSize + blockSize - 1) / blockSize);
                    for (int blockIdx = 0; blockIdx < blocksInSector; blockIdx++)
                    {
                        if (!IsRunning)
                        {
                            mState = WritingState.Failed;
                            StartNextAction();
                            return;
                        }

                        int blockStart = blockIdx * blockSize;
                        int blockLen = Math.Min(blockSize, sectorData.Length - blockStart);
                        byte[] blockData = new byte[blockLen];
                        Array.Copy(sectorData, blockStart, blockData, 0, blockLen);

                        if (IsBlockAllFF(blockData))
                        {
                            mBytesWritten += (uint)blockLen;
                            UpdateProgress();
                            continue;
                        }

                        uint blockOffset = sectorOffset + (uint)blockStart;
                        if (!mBootstrapInterface.ProgramBlock(mSettings.Variant, writeAddressBase, readAddressBase, blockOffset, blockData))
                        {
                            CommInterface.DisplayStatusMessage($"WriteFlashThread: Program failed at sector {sectorIndex}, block {blockIdx}", StatusMessageType.USER);
                            mState = WritingState.Failed;
                            StartNextAction();
                            return;
                        }

                        mBytesWritten += (uint)blockLen;
                        UpdateProgress();
                    }

                    mNumSuccessfullyFlashedSectors++;
                    sectorOffset += sectorSize;
                }

                CommInterface.DisplayStatusMessage("WriteFlashThread: Flash write completed successfully", StatusMessageType.LOG);
                mState = WritingState.Finished;
                StartNextAction();
            }
            catch (Exception ex)
            {
                CommInterface.DisplayStatusMessage($"WriteFlashThread: EXCEPTION - {ex.GetType().Name}: {ex.Message}", StatusMessageType.USER);
                CommInterface.DisplayStatusMessage($"WriteFlashThread: {ex.StackTrace}", StatusMessageType.LOG);
                mState = WritingState.Failed;
                StartNextAction();
            }
        }

        private void UpdateProgress()
        {
            if (mTotalBytesToWrite > 0)
            {
                float percent = ((float)mBytesWritten / (float)mTotalBytesToWrite) * 100.0f;
                OnUpdatePercentComplete(percent);
            }
        }

        private enum WritingState
        {
            Start,
            Writing,
            Finished,
            Failed
        }

        private BootstrapInterface mBootstrapInterface;
        private BootmodeWriteExternalFlashSettings mSettings;
        private IEnumerable<MemoryImage> mSectorImages;
        private WritingState mState;
        private int mTotalSectors;
        private uint mTotalBytesToWrite;
        private uint mBytesWritten;
        private int mNumSuccessfullyFlashedSectors;
        private BootmodeNopCommunicationAction mNOPAction;
    }

    /// <summary>
    /// Bootmode SPI EEPROM read (physical 95040 chip). Overwrites flash driver at 0xF600 —
    /// reload flash driver after this operation if flash work follows.
    /// </summary>
    public class BootmodeReadEepromOperation : BootmodeBackgroundOperation
    {
        public BootmodeReadEepromOperation(
            BootstrapInterface commInterface,
            BootstrapInterface.BootmodeEepromSettings settings)
            : base(commInterface, "Bootmode EEPROM read")
        {
            mBootstrapInterface = commInterface;
            mSettings = settings ?? BootstrapInterface.BootmodeEepromSettings.ForMe75();
        }

        public MemoryImage ReadMemory { get; private set; }

        protected override void OnResetWorker()
        {
            ReadMemory = null;
        }

        protected override bool RunWorker()
        {
            CommInterface.DisplayStatusMessage(
                $"Bootmode EEPROM read: {mSettings.Periph}, type={(ushort)mSettings.EepromType}, Port{mSettings.PortNumber}.{mSettings.PinNumber}, size={mSettings.Size}",
                StatusMessageType.USER);

            byte[] data;
            bool success = mBootstrapInterface.ReadEeprom(
                mSettings,
                out data,
                ReportProgress);

            if (!success || data == null)
            {
                return false;
            }

            ReadMemory = new MemoryImage(data, 0);
            BootmodeEepromChecksumMessages.ReportMe7Eeprom95040Checksums(CommInterface, mSettings, data, warnOnly: false);
            CommInterface.DisplayStatusMessage(
                "Bootmode EEPROM read complete. Reload flash driver before any flash operation.",
                StatusMessageType.USER);
            return true;
        }

        private readonly BootstrapInterface mBootstrapInterface;
        private readonly BootstrapInterface.BootmodeEepromSettings mSettings;
    }

    /// <summary>
    /// Bootmode SPI EEPROM write (physical 95040 chip). Overwrites flash driver at 0xF600 —
    /// reload flash driver after this operation if flash work follows.
    /// </summary>
    public class BootmodeWriteEepromOperation : BootmodeBackgroundOperation
    {
        public BootmodeWriteEepromOperation(
            BootstrapInterface commInterface,
            BootstrapInterface.BootmodeEepromSettings settings,
            byte[] dataToWrite,
            bool verify)
            : base(commInterface, "Bootmode EEPROM write")
        {
            if (dataToWrite == null || dataToWrite.Length == 0)
            {
                throw new ArgumentException("EEPROM write data is empty.", nameof(dataToWrite));
            }

            mBootstrapInterface = commInterface;
            mSettings = settings ?? BootstrapInterface.BootmodeEepromSettings.ForMe75();
            mDataToWrite = dataToWrite;
            mVerify = verify;
        }

        protected override bool RunWorker()
        {
            CommInterface.DisplayStatusMessage(
                $"Bootmode EEPROM write: {mSettings.Periph}, type={(ushort)mSettings.EepromType}, Port{mSettings.PortNumber}.{mSettings.PinNumber}, size={mDataToWrite.Length}, verify={mVerify}",
                StatusMessageType.USER);

            if (mDataToWrite.Length > mSettings.Size)
            {
                CommInterface.DisplayStatusMessage(
                    $"EEPROM file is {mDataToWrite.Length} bytes; preset max is {mSettings.Size}.",
                    StatusMessageType.USER);
                return false;
            }

            BootmodeEepromChecksumMessages.ReportMe7Eeprom95040Checksums(
                CommInterface,
                mSettings,
                mDataToWrite,
                warnOnly: true);

            bool success = mBootstrapInterface.WriteEeprom(
                mSettings,
                mDataToWrite,
                mVerify,
                ReportProgress);

            if (!success)
            {
                return false;
            }

            CommInterface.DisplayStatusMessage(
                "Bootmode EEPROM write complete. Reload flash driver before any flash operation.",
                StatusMessageType.USER);
            return true;
        }

        private readonly BootstrapInterface mBootstrapInterface;
        private readonly BootstrapInterface.BootmodeEepromSettings mSettings;
        private readonly byte[] mDataToWrite;
        private readonly bool mVerify;
    }

    internal static class BootmodeEepromChecksumMessages
    {
        public static void ReportMe7Eeprom95040Checksums(
            CommunicationInterface commInterface,
            BootstrapInterface.BootmodeEepromSettings settings,
            byte[] data,
            bool warnOnly)
        {
            if (settings == null
                || data == null
                || settings.EepromType != BootstrapInterface.BootmodeEepromType.Type95040
                || data.Length < Me7Eeprom95040Checksum.EepromSize)
            {
                return;
            }

            var checksum = Me7Eeprom95040Checksum.Validate(data);
            if (checksum.AllChecksumPagesValid)
            {
                if (!warnOnly)
                {
                    commInterface.DisplayStatusMessage(
                        checksum.FormatStatusMessage(warnOnly: false),
                        StatusMessageType.USER);
                }
                return;
            }

            commInterface.DisplayStatusMessage(
                checksum.FormatStatusMessage(warnOnly),
                StatusMessageType.USER);
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
