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
using System.Threading;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Windows.Data;

using Shared;

namespace Communication
{
    public class ExtendedDataLoggingDefineReadVariables : KWP2000Action
    {
        public const byte DefineVariablesID = 0xB7;
        public const byte ReadVariablesID = 0xB8;

        public class VariableDefinition
        {
            public uint StartAddress
            {
                get;
                set;
            }

            public enum VariableType
            {
                Byte,
                Word
            }

            public VariableType VarType
            {
                get;
                set;
            }

            public VariableDefinition(uint startAddress, VariableType varType)
            {
                StartAddress = startAddress;
                VarType = varType;
            }
        }

        public enum Mode
        {
            Define,
            Read
        }

        public ExtendedDataLoggingDefineReadVariables(KWP2000Interface commInterface, Mode mode, IEnumerable<VariableDefinition> variables)
            : base(commInterface)
        {
            mVariables = variables;
            mMode = mode;
            NumVariablesDefined = 0;
            MaxNumVariableReadsPerTick = 255;
            MaxNumBytesPerRead = 64;
        }

        public byte DefineFailureResponseCode;

        public override bool Start()
        {
            mReadVariableData = new List<byte[]>();

            bool started = false;

            if (base.Start())
            {
                if (mMode == Mode.Define)
                {
                    DefineFailureResponseCode = 0;
                    NumVariablesDefined = 0;

                    var numVariablesDefined = Math.Min(mVariables.Count(), 252 / 3);//don't allow the definition of more variables than can fit in a message

                    var variableData = new byte[(numVariablesDefined * 3) + 1];
                    variableData[0] = (byte)numVariablesDefined;

                    int x = 1;
                    foreach (var variable in mVariables)
                    {
                        byte sizeBit = (byte)((variable.VarType == VariableDefinition.VariableType.Byte) ? 0 : 1);

                        variableData[x] = (byte)(((variable.StartAddress >> 16) & 0x7F) | (byte)(sizeBit << 7));
                        variableData[x + 1] = (byte)((variable.StartAddress >> 8) & 0xFF);
                        variableData[x + 2] = (byte)(variable.StartAddress & 0xFF);
                        x += 3;

                        if (x >= variableData.Length)
                        {
                            break;
                        }
                    }

                    SendMessage(DefineVariablesID, variableData);

                    started = true;
                }
                else
                {
                    mReadVariableIndexLastRead = 0;

                    started = SendReadMessage();
                }
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (mMode == Mode.Define)
                {
                    if (KWP2000Interface.IsPositiveResponseToRequest(DefineVariablesID, message))
                    {
                        NumVariablesDefined = message.mData[0];

                        ActionCompleted(true);
                        handled = true;
                    }
                    else
                    {
                        KWP2000Interface.IsNegativeResponseToRequest(DefineVariablesID, message, out DefineFailureResponseCode);
                    }
                }
                else
                {
                    if (KWP2000Interface.IsPositiveResponseToRequest(ReadVariablesID, message))
                    {
                        int dataIndex = 0;

                        byte readVariablesEndIndex = (byte)(mReadVariableIndexLastRead + mNumReadVariablesLastRead);

                        for (var curVarIndex = mReadVariableIndexLastRead; curVarIndex < readVariablesEndIndex; ++curVarIndex)
                        {
                            var curVariable = mVariables.ElementAt(curVarIndex);

                            var dataSize = (curVariable.VarType == VariableDefinition.VariableType.Byte) ? 1 : 2;

                            if (dataIndex + dataSize > message.DataLength)
                            {
                                break;
                            }

                            var variableData = new byte[dataSize];
                            Buffer.BlockCopy(message.mData, dataIndex, variableData, 0, dataSize);

                            mReadVariableData.Add(variableData);

                            dataIndex += dataSize;
                        }

                        mReadVariableIndexLastRead = readVariablesEndIndex;

                        if (mReadVariableIndexLastRead >= NumVariablesDefined)
                        {
                            ActionCompleted(true);
                        }
                        else
                        {
                            SendReadMessage();
                        }

                        handled = true;
                    }
                }

                if (!handled)
                {
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        byte mReadVariableIndexLastRead = 0;
        byte mNumReadVariablesLastRead = 0;

        protected bool SendReadMessage()
        {
            //byte numUpdatesToReadAllVariables = (byte)Math.Max(NumVariablesDefined / MaxNumVariableReadsPerTick, 1);
            //numUpdatesToReadAllVariables += (byte)((NumVariablesDefined % MaxNumVariableReadsPerTick != 0) ? 1 : 0);
            //byte numVariablesReadPerUpdateAverage = (byte)(NumVariablesDefined / numUpdatesToReadAllVariables);

            //TODO: figure out how many variables can be read with the given number of bytes
            var maxNumVariables = (byte)(MaxNumBytesPerRead / 2);
            mNumReadVariablesLastRead = (byte)Math.Min(maxNumVariables, NumVariablesDefined - mReadVariableIndexLastRead);

            var readData = new byte[3];
            readData[0] = MaxNumVariableReadsPerTick;//num variable reads per ECU tick
            readData[1] = mReadVariableIndexLastRead;//starting variable index
            readData[2] = mNumReadVariablesLastRead;//num variables to read
            SendMessage(ReadVariablesID, readData);

            return true;
        }

        public IEnumerable<byte[]> ReadVariableData
        {
            get { return mReadVariableData; }
        }

        public byte NumVariablesDefined
        {
            get;
            private set;
        }

        public Mode ReadMode
        {
            get { return mMode; }
            set { mMode = value; }
        }

        public byte MaxNumVariableReadsPerTick
        {
            get;
            set;
        }

        public byte MaxNumBytesPerRead
        {
            get;
            set;
        }

        private IEnumerable<VariableDefinition> mVariables;
        private List<byte[]> mReadVariableData;
        private Mode mMode;
    }

    public class SetupExtendedDataLoggingOperation : RelocateMessageHandlingTableOperation
    {
        public SetupExtendedDataLoggingOperation(KWP2000Interface commInterface, List<uint> baudRates)
            : base(commInterface, baudRates)
        {
            mDefineFunctionData = new byte[0x7C];
            Buffer.BlockCopy(Communication.Properties.Resources.KWP2000DataLoggingFunctions, 0, mDefineFunctionData, 0, mDefineFunctionData.Length);
            Debug.Assert(mDefineFunctionData[mDefineFunctionData.Length - 2] == 0xDB);
            Debug.Assert(mDefineFunctionData[mDefineFunctionData.Length - 1] == 0x00);

            mReadFunctionData = new byte[Communication.Properties.Resources.KWP2000DataLoggingFunctions.Length - mDefineFunctionData.Length];
            Buffer.BlockCopy(Communication.Properties.Resources.KWP2000DataLoggingFunctions, mDefineFunctionData.Length, mReadFunctionData, 0, mReadFunctionData.Length);

            mVariableDefinitionsData = new byte[257];//257 is hardcoded into the assembly function
            for (int x = 0; x < mVariableDefinitionsData.Length; x++)
            {
                mVariableDefinitionsData[x] = 0;
            }
        }

        protected override List<byte> GetRelocatedServiceIDs()
        {
            //DefineVariablesID must be setup first to map in the variable definitions buffer
            return new List<byte> { ExtendedDataLoggingDefineReadVariables.DefineVariablesID, ExtendedDataLoggingDefineReadVariables.ReadVariablesID };
        }

        protected override byte[] GetRelocatedFunctionData(byte serviceID, uint destinationAddress, out uint offsetToFunction)
        {
            Debug.Assert(destinationAddress % 2 == 0);

            byte[] functionData = null;
            offsetToFunction = 0;

            if (serviceID == ExtendedDataLoggingDefineReadVariables.DefineVariablesID)
            {
                mVariableDefinitionsAddress = destinationAddress;
            }

            Debug.Assert(mVariableDefinitionsAddress != 0);

            switch (serviceID)
            {
                case ExtendedDataLoggingDefineReadVariables.DefineVariablesID:
                    {
                        offsetToFunction += (uint)mVariableDefinitionsData.Length;
                        offsetToFunction += (offsetToFunction % 2);//make function offset even

                        functionData = new byte[offsetToFunction + mDefineFunctionData.Length];
                        mVariableDefinitionsData.CopyTo(functionData, 0);
                        mDefineFunctionData.CopyTo(functionData, offsetToFunction);

                        break;
                    }
                case ExtendedDataLoggingDefineReadVariables.ReadVariablesID:
                    {
                        functionData = new byte[offsetToFunction + mReadFunctionData.Length];
                        mReadFunctionData.CopyTo(functionData, offsetToFunction);

                        break;
                    }
            }

            if (functionData != null)
            {
                var variableDefinitionsAddress = (UInt16)(mVariableDefinitionsAddress & 0xFFFF);
                var variableDefinitionsSegment = (UInt16)((mVariableDefinitionsAddress >> 16) & 0xFF);

                for (int x = 0; x < functionData.Length - 1; x += 2)
                {
                    UInt16 curWord = BitConverter.ToUInt16(functionData, x);

                    switch (curWord)
                    {
                        case 0x1112:
                            {
                                BitConverter.GetBytes(mCurrentMessageFirstDataByteAddress).CopyTo(functionData, x);
                                break;
                            }
                        case 0x3334:
                            {
                                BitConverter.GetBytes(mCurrentMessageNumDataBytesAddress).CopyTo(functionData, x);
                                break;
                            }
                        case 0x5556:
                            {
                                BitConverter.GetBytes(variableDefinitionsAddress).CopyTo(functionData, x);
                                break;
                            }
                        case 0x6666:
                            {
                                BitConverter.GetBytes(variableDefinitionsSegment).CopyTo(functionData, x);
                                break;
                            }
                        case 0x7778:
                            {
                                BitConverter.GetBytes(mCurrentServiceIDAddress).CopyTo(functionData, x);
                                break;
                            }
                        case 0x8888:
                            {
                                BitConverter.GetBytes(mCommunicationFlagsAddress).CopyTo(functionData, x);
                                break;
                            }
                    }
                }
            }

            return functionData;
        }

        private byte[] mDefineFunctionData;
        private byte[] mReadFunctionData;
        private byte[] mVariableDefinitionsData;
        private uint mVariableDefinitionsAddress;
    }

    public class ExtendedDataLoggingOperation : SetupExtendedDataLoggingOperation, ITrackedMemoryRegionsOperation
    {
        private class ExtendedDataLoggingMemoryRegion : TaggedMemoryImage
        {
            public uint ReferenceCount
            {
                get;
                set;
            }

            public ExtendedDataLoggingMemoryRegion(uint numBytes, uint startAddress, object userTag)
                : base(numBytes, startAddress, userTag)
            {
                ReferenceCount = 1;
            }
        }

        public ExtendedDataLoggingOperation(KWP2000Interface commInterface, List<uint> baudRates)
            : base(commInterface, baudRates)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
            ShouldRelocateMessageHandlingTable = false;

            mState = State.DefineVariables;
            mExtendedDataLoggingSetup = false;

            mLastReadStartTime = DateTime.Now;
            MaxReadsPerSecond = 20;
            MaxVariableReadsPerTick = 255;
            MaxNumBytesPerRead = 64;
        }

        public float MaxReadsPerSecond { get; set; }
        public byte MaxVariableReadsPerTick { get; set; }
        public byte MaxNumBytesPerRead { get; set; }

        public event MemoryRegionsRead RegionsRead;

        public void AddMemoryRegion(uint startAddress, uint numBytes, object userTag)
        {
            Debug.Assert(numBytes <= 2);

            var existingRegion = mSynchronizedRegions.Find(region => ((region.StartAddress == startAddress) && (region.Size == numBytes) && region.UserTag.Equals(userTag)));

            if (existingRegion != null)
            {
                existingRegion.ReferenceCount++;
            }
            else
            {
                mSynchronizedRegions.Add(new ExtendedDataLoggingMemoryRegion(numBytes, startAddress, userTag));

                mState = State.DefineVariables;
            }
        }

        public void RemoveMemoryRegion(object userTag)
        {
            var existingRegions = mSynchronizedRegions.FindAll(region => region.UserTag.Equals(userTag));

            foreach (var region in existingRegions)
            {
                Debug.Assert(region.ReferenceCount > 0);
                region.ReferenceCount--;

                if (region.ReferenceCount <= 0)
                {
                    mSynchronizedRegions.Remove(region);
                    mState = State.DefineVariables;
                }
            }
        }

        public void RemoveAllMemoryRegions()
        {
            mSynchronizedRegions.Clear();
            mState = State.DefineVariables;
        }

        protected override CommunicationAction NextAction()
        {
            var currentTime = DateTime.Now;

#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("NextAction started at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                if (mState == State.ReadVariables)
                {
                    {
                        var minTimeBetweenReads = TimeSpan.FromMilliseconds(1000.0f / MaxReadsPerSecond);
                        var nextReadTime = mLastReadStartTime + minTimeBetweenReads;
                        var timeUntilNextRead = nextReadTime - currentTime;

                        if (timeUntilNextRead.TotalMilliseconds > 0)
                        {
                            Thread.Sleep(timeUntilNextRead);//TODO: test performance if we don't sleep....
                            while (DateTime.Now < nextReadTime) ;
                        }

                        mLastReadStartTime = nextReadTime;
                    }

                    mDataLogAction.ReadMode = ExtendedDataLoggingDefineReadVariables.Mode.Read;
                    mDataLogAction.MaxNumVariableReadsPerTick = MaxVariableReadsPerTick;
                    mDataLogAction.MaxNumBytesPerRead = MaxNumBytesPerRead;
                    nextAction = mDataLogAction;
                }
                else if (mState == State.DefineVariables)
                {
                    var variables = new List<ExtendedDataLoggingDefineReadVariables.VariableDefinition>(mSynchronizedRegions.Count);

                    foreach (var operationVariable in mSynchronizedRegions)
                    {
                        var varType = ExtendedDataLoggingDefineReadVariables.VariableDefinition.VariableType.Byte;

                        if (operationVariable.Size > 1)
                        {
                            varType = ExtendedDataLoggingDefineReadVariables.VariableDefinition.VariableType.Word;
                        }

                        var actionVariable = new ExtendedDataLoggingDefineReadVariables.VariableDefinition(operationVariable.StartAddress, varType);

                        variables.Add(actionVariable);
                    }

                    CommInterface.DisplayStatusMessage("Defining " + variables.Count + " variables with the ECU for data logging.", StatusMessageType.USER);

                    mDataLogAction = new ExtendedDataLoggingDefineReadVariables(KWP2000CommInterface, ExtendedDataLoggingDefineReadVariables.Mode.Define, variables);
                    nextAction = mDataLogAction;
                }

                mMyLastStartedAction = nextAction;
            }

#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("NextAction finished at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
            return nextAction;
        }

        protected override void OnActionStarted(CommunicationAction action)
        {
#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("OnActionStarted started at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
            base.OnActionStarted(action);

            //ignore actions not started by this code
            if (action == mMyLastStartedAction)
            {
                //done after the ActionCompletedHandler so the next message is sent before we process updates
                if ((mState == State.ReadVariables) && (mLastReadData != null))
                {
                    var regionsReadEventCopy = RegionsRead;

                    if (regionsReadEventCopy != null)
                    {
                        var updatedRegions = new List<TaggedMemoryImage>(mLastReadData.Count);

                        var dataIter = mLastReadData.GetEnumerator();

                        //update newly read regions
                        foreach (var curRegion in mSynchronizedRegions)
                        {
                            if (!dataIter.MoveNext())
                            {
                                break;
                            }

                            dataIter.Current.CopyTo(curRegion.RawData, 0);
                            updatedRegions.Add(curRegion);
                        }

                        regionsReadEventCopy(updatedRegions);
                    }

                    mLastReadData = null;
                }
            }

#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("OnActionStarted finished at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("OnActionCompleted started at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
            //ignore actions not started by this code
            if (action == mMyLastStartedAction)
            {
                if (action.CompletedWithoutCommunicationError)
                {
                    if (success)
                    {
                        if ((mState == State.DefineVariables) && (mDataLogAction.ReadMode == ExtendedDataLoggingDefineReadVariables.Mode.Define))
                        {
                            mState = State.ReadVariables;
                            mExtendedDataLoggingSetup = true;
                            mLastReadData = null;

                            CommInterface.DisplayStatusMessage("ECU reports " + mDataLogAction.NumVariablesDefined + " variables were defined for data logging.", StatusMessageType.USER);
                        }
                        else if ((mState == State.ReadVariables) && (mDataLogAction.ReadMode == ExtendedDataLoggingDefineReadVariables.Mode.Read))
                        {
                            mLastReadData = new List<byte[]>(mDataLogAction.ReadVariableData.Count());

                            foreach (var data in mDataLogAction.ReadVariableData)
                            {
                                mLastReadData.Add(data);
                            }
                        }
                    }
                    else
                    {
                        if (mState == State.DefineVariables)
                        {
                            if (mDataLogAction.DefineFailureResponseCode == (byte)KWP2000ResponseCode.ServiceNotSupported)
                            {
                                CommInterface.DisplayStatusMessage("Failed to define data logged variables with the ECU. Extended data logging protocol does not appear to have been setup.", StatusMessageType.USER);

                                if (!mExtendedDataLoggingSetup)
                                {
                                    CommInterface.DisplayStatusMessage("Setting up extended data logging protocol.", StatusMessageType.USER);
                                    ShouldRelocateMessageHandlingTable = true;
                                    success = true;
                                }
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Failed to define data logged variables with the ECU.", StatusMessageType.USER);
                            }
                        }
                        else if (mState == State.ReadVariables)
                        {
                            CommInterface.DisplayStatusMessage("Failed to read data logged variables from the ECU.", StatusMessageType.USER);
                        }
                    }
                }
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);

#if LOG_PERFORMANCE
			CommInterface.DisplayStatusMessage("OnActionCompleted finished at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);	
#endif
        }

        enum State
        {
            DefineVariables,
            ReadVariables
        }
        private State mState;

        private bool mExtendedDataLoggingSetup;

        private List<ExtendedDataLoggingMemoryRegion> mSynchronizedRegions = new List<ExtendedDataLoggingMemoryRegion>();

        private ExtendedDataLoggingDefineReadVariables mDataLogAction;
        private List<byte[]> mLastReadData;

        private DateTime mLastReadStartTime;

        private CommunicationAction mMyLastStartedAction;
    }	
}