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

//#define LOG_PERFORMANCE
// Treat boot-cluster checksums as a miss (diff-write on an unchanged image). Do not commit enabled.
//#define FORCE_BOOT_CLUSTER_CHECKSUM_MISMATCH

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using Shared;

namespace Communication
{
    public abstract class KWP2000Operation : CommunicationOperation
    {
        public const byte DEFAULT_MAX_BLOCK_SIZE = 254;

        public KWP2000Operation(KWP2000Interface commInterface)
            : base(commInterface)
        {
            mShouldAutoNegotiateTiming = false;
            mShouldAutoStartDiagnosticSession = false;
            mDesiredDiagnosticSessionType = (uint)KWP2000DiagnosticSessionType.InternalUndefined;
        }

        protected override void ResetOperation()
        {
            mState = State.Begin;

            base.ResetOperation();
        }

        protected override CommunicationAction NextAction()
        {
            CommunicationAction nextAction = null;

            if (IsRunning)
            {
                nextAction = base.NextAction();

                if (nextAction == null)
                {
                    //determine the next state
                    switch (mState)
                    {
                        case State.Begin:
                        {
                            uint desiredBaudRate = (uint)KWP2000BaudRates.BAUD_UNSPECIFIED;

                            if (mShouldAutoStartDiagnosticSession && (mDesiredBaudRates != null))
                            {
                                desiredBaudRate = mDesiredBaudRates.First();
                            }

                            bool shouldStart = mShouldAutoStartDiagnosticSession && StartDiagnosticSessionAction.ShouldStartDiagnosticSession(KWP2000CommInterface, mDesiredDiagnosticSessionType, desiredBaudRate);

                            if (mDesiredDiagnosticSessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
                            {
                                // Already in the programming session: skip the precondition read.
                                // Default timing still logs in before StartDiagnosticSession.
                                // The 0x11 slow-init fallback also skips it: that read can open a dialog,
                                // and the baud request has to go out while tester present is still at 10400.
                                mState = (shouldStart && !mSkipProgrammingSessionPreconditions)
                                    ? State.CheckProgrammingSessionPreconditions
                                    : State.SwitchToDefaultTimingForProgrammingSession;
                            }
                            else if (shouldStart && mShouldAutoNegotiateSecurity)
                            {
                                mState = State.PreNegotiateSecurityForProgrammingSession;
                            }
                            else if (shouldStart)
                            {
                                mState = State.StartDiagnosticSession;
                            }
                            else if (!mShouldAutoStartDiagnosticSession && mShouldAutoNegotiateSecurity)
                            {
                                // ReadMemory stays in the current session and logs in there.
                                // A development session does not serve ReadMemoryByAddress.
                                mState = State.PreNegotiateSecurityForProgrammingSession;
                            }
                            else
                            {
                                mState = StateAfterStartDiagnosticSession();
                            }

                            break;
                        }
                        case State.CheckProgrammingSessionPreconditions:
                        {
                            mState = State.SwitchToDefaultTimingForProgrammingSession;
                            break;
                        }
                        case State.SwitchToDefaultTimingForProgrammingSession:
                        {
                            if (mShouldAutoNegotiateSecurity)
                            {
                                // DQ answers StartDiagnosticSession with 7F 10 33 until this login.
                                // That key sets bit 12 of [0xE074], so the login inside the programming
                                // session gets a zero seed and must still send its key. A development
                                // session stops after this login.
                                mState = State.PreNegotiateSecurityForProgrammingSession;
                            }
                            else
                            {
                                mState = State.StartDiagnosticSession;
                            }
                            break;
                        }
                        case State.PreNegotiateSecurityForProgrammingSession:
                        {
                            mState = mShouldAutoStartDiagnosticSession
                                ? State.StartDiagnosticSession
                                : StateAfterStartDiagnosticSession();
                            break;
                        }
                        case State.StartDiagnosticSession:
                        {
                            mState = StateAfterStartDiagnosticSession();
                            break;
                        }
                        case State.NegotiateTiming:
                        {
                            mState = NeedsInSessionSecurity() ? State.NegotiateSecurity : State.Finished;
                            break;
                        }
                        case State.NegotiateSecurity:
                        {
                            mState = State.Finished;
                            break;
                        }
                    }

                    //start the action for the new state
                    switch (mState)
                    {
                        case State.CheckProgrammingSessionPreconditions:
                        {
                            nextAction = new ReadECUIdentificationAction(KWP2000CommInterface, (byte)KWP2000IdentificationOption.calibrationEquipmentSoftwareNumber);
                            break;
                        }
                        case State.SwitchToDefaultTimingForProgrammingSession:
                        {
                            nextAction = new NegotiateTimingParameters(KWP2000CommInterface, NegotiateTimingParameters.NegotiationTarget.Default);
                            break;
                        }
                        case State.PreNegotiateSecurityForProgrammingSession:
                        {
                            nextAction = new SecurityAccessAction(KWP2000CommInterface, mSecuritySettings);
                            break;
                        }
                        case State.StartDiagnosticSession:
                        {
                            nextAction = new StartDiagnosticSessionAction(KWP2000CommInterface, mDesiredDiagnosticSessionType, mDesiredBaudRates);
                            break;
                        }
                        case State.NegotiateTiming:
                        {
                            nextAction = new NegotiateTimingParameters(KWP2000CommInterface, mTimingTarget);
                            break;
                        }
                        case State.NegotiateSecurity:
                        {
                            nextAction = new SecurityAccessAction(KWP2000CommInterface, mSecuritySettings);
                            break;
                        }
                    }
                }
            }

            mMyLastStartedAction = nextAction;

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (action == mMyLastStartedAction)
            {
                #region CheckProgrammingSessionPreconditions
                if (mState == State.CheckProgrammingSessionPreconditions)
                {
                    if (success)
                    {
                        #region ReadECUIdentification
                        if (action is ReadECUIdentificationAction)
                        {
                            var readIdentAction = action as ReadECUIdentificationAction;

                            if (readIdentAction.IdentificationOption == (byte)KWP2000IdentificationOption.calibrationEquipmentSoftwareNumber)
                            {
                                byte[] statusBytes = readIdentAction.IdentificationData;
                                if ((statusBytes != null) && (statusBytes.Length > 0))
                                {
                                    string hex = BitConverter.ToString(statusBytes, 0, Math.Min(statusBytes.Length, 4)).Replace("-", " ");
                                    CommInterface.DisplayStatusMessage("Flash status 1A 9C: " + hex, StatusMessageType.USER);
                                }
                                unsafe
                                {
                                    if (readIdentAction.IdentificationData.Length >= sizeof(KWP2000FlashStatus))
                                    {
                                        var flashStatus = new KWP2000FlashStatus(readIdentAction.IdentificationData);

                                        //check if flash preconditions have been met
                                        if (flashStatus.mProgrammingSessionPreconditions != 0)
                                        {
                                            string warning = "ECU reports programming session preconditions have not been met.";
                                            string reasons = "Reasons preconditions failed:";

                                            CommInterface.DisplayStatusMessage(warning, StatusMessageType.USER);
                                            CommInterface.DisplayStatusMessage(reasons, StatusMessageType.USER);

                                            foreach (KWP2000FlashStatus.ProgrammingSessionPreconditions precondition in System.Enum.GetValues(typeof(KWP2000FlashStatus.ProgrammingSessionPreconditions)))
                                            {
                                                if ((((byte)flashStatus.mProgrammingSessionPreconditions) & ((byte)precondition)) != 0)
                                                {
                                                    string reasonDescription = "-" + DescriptionAttributeConverterLogic.GetDescriptionAttribute(precondition);
                                                    CommInterface.DisplayStatusMessage(reasonDescription, StatusMessageType.USER);

                                                    reasons += "\n" + reasonDescription;
                                                }
                                            }

                                            DeferPreconditionsPrompt(action, warning + "\n" + reasons + "\n\n Do you want to attempt to continue?");
                                            return;
                                        }
                                        else
                                        {
                                            CommInterface.DisplayStatusMessage("ECU reports programming session preconditions have been met.", StatusMessageType.USER);
                                        }
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                    else if (action.CompletedWithoutCommunicationError)
                    {
                        CommInterface.DisplayStatusMessage("Unable to validate programming session preconditions, attempting to continue.", StatusMessageType.USER);

                        success = true;
                    }
                }
                #endregion
                #region PreNegotiateSecurityForProgrammingSession
                else if (mState == State.PreNegotiateSecurityForProgrammingSession)
                {
                    // CH, HL, and NL reject this login (GeneralReject or ServiceNotSupported).
                    // It does not count. DQ accepts it. A timeout still fails the operation.
                    success = action.CompletedWithoutCommunicationError;
                }
                #endregion
                #region NegotiateTiming SwitchToDefaultTimingForProgrammingSession
                else if ((mState == State.NegotiateTiming) || (mState == State.SwitchToDefaultTimingForProgrammingSession))
                {
                    success = action.CompletedWithoutCommunicationError;
                }
                #endregion
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);
        }

        protected void EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType sessionType, IEnumerable<uint> baudRates)
        {
            mDesiredDiagnosticSessionType = sessionType;
            mDesiredBaudRates = baudRates;
            mShouldAutoStartDiagnosticSession = true;

            // DQ answers StartDiagnosticSession with 7F 10 33 for these sessions until a login.
            // A later EnableAutoNegotiateSecurity replaces these settings.
            if (!mShouldAutoNegotiateSecurity
                && ((sessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
                    || (sessionType == KWP2000DiagnosticSessionType.DevelopmentSession)))
            {
                var settings = KWP2000CommInterface.SecuritySettings ?? new SecurityAccessAction.SecurityAccessSettings();
                EnableAutoNegotiateSecurity(settings);
            }
        }

        protected void EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType sessionType, uint baudRate)
        {
            EnableAutoStartDiagnosticSession(sessionType, new List<uint>() { baudRate });
        }

        protected void EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget target)
        {
            mShouldAutoNegotiateTiming = true;
            mTimingTarget = target;
        }

        protected void EnableAutoNegotiateSecurity(SecurityAccessAction.SecurityAccessSettings settings)
        {
            mShouldAutoNegotiateSecurity = true;

            mSecuritySettings = settings;
        }

        protected void EnableProgrammingSession(IEnumerable<uint> baudRates, SecurityAccessAction.SecurityAccessSettings securitySettings)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.ProgrammingSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
            EnableAutoNegotiateSecurity(securitySettings);
        }

        protected void SkipProgrammingSessionPreconditions()
        {
            mSkipProgrammingSessionPreconditions = true;
        }

        protected static string RangeText(uint start, uint length)
        {
            return "0x" + start.ToString("X8") + "-0x" + (start + length - 1).ToString("X8");
        }

        // Post the dialog and return to SendReceiveThread. That loop is the only tester-present sender.
        protected void PostUserPrompt(string title, string message, UserPromptType promptType, Action<UserPromptResult> apply)
        {
            mAwaitingUserPrompt = true;
            CommInterface.BeginDisplayUserPrompt(title, message, promptType, result =>
            {
                lock (this)
                {
                    if (!IsRunning || !mAwaitingUserPrompt)
                    {
                        mAwaitingUserPrompt = false;
                        return;
                    }

                    mAwaitingUserPrompt = false;
                    apply(result);
                }
            });
        }

        protected bool UserPromptIsPending
        {
            get { return mAwaitingUserPrompt; }
        }

        private void DeferPreconditionsPrompt(CommunicationAction action, string message)
        {
            PostUserPrompt(
                "Programming Session Preconditions Not Met",
                message,
                UserPromptType.OK_CANCEL,
                result =>
                {
                    bool success = result == UserPromptResult.OK;
                    CommInterface.DisplayStatusMessage(
                        success
                            ? "Continuing despite programming session preconditions not being met."
                            : "Stopping because programming session preconditions have not been met.",
                        StatusMessageType.USER);

                    mMyLastStartedAction = null;
                    base.OnActionCompleted(action, success);
                });
        }

        private enum State
        {
            Begin,
            CheckProgrammingSessionPreconditions,
            SwitchToDefaultTimingForProgrammingSession,
            PreNegotiateSecurityForProgrammingSession,
            StartDiagnosticSession,
            NegotiateTiming,
            NegotiateSecurity,
            Finished
        }

        private State StateAfterStartDiagnosticSession()
        {
            if (mShouldAutoNegotiateTiming)
            {
                return State.NegotiateTiming;
            }

            if (NeedsInSessionSecurity())
            {
                return State.NegotiateSecurity;
            }

            return State.Finished;
        }

        private bool NeedsInSessionSecurity()
        {
            // The programming-session kernel clears 0x381B30. The pre-session key sets bit 12
            // of [0xE074], so this login gets a zero seed and must still send its key.
            return mShouldAutoNegotiateSecurity && (mDesiredDiagnosticSessionType == KWP2000DiagnosticSessionType.ProgrammingSession);
        }

        protected KWP2000Interface KWP2000CommInterface
        {
            get
            {
                return CommInterface as KWP2000Interface;
            }
            set
            {
                CommInterface = value;
            }
        }

        private State mState;
        private bool mShouldAutoStartDiagnosticSession;
        private bool mSkipProgrammingSessionPreconditions;
        private IEnumerable<uint> mDesiredBaudRates;
        private KWP2000DiagnosticSessionType mDesiredDiagnosticSessionType;

        private bool mShouldAutoNegotiateTiming;
        private NegotiateTimingParameters.NegotiationTarget mTimingTarget;

        private bool mShouldAutoNegotiateSecurity;
        private SecurityAccessAction.SecurityAccessSettings mSecuritySettings;

        private CommunicationAction mMyLastStartedAction;
        private bool mAwaitingUserPrompt;
    };

    // Issue #127. After slow init falls through to 0x11, request the connect-bar baud
    // before tester present sits at 10400. No precondition dialog.
    public class SlowInitFallbackBaudOperation : KWP2000Operation
    {
        public SlowInitFallbackBaudOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            SkipProgrammingSessionPreconditions();
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.ProgrammingSession, baudRates);
        }
    }

    public abstract class KWP2000SequencialOperation : KWP2000Operation
    {
        public KWP2000SequencialOperation(KWP2000Interface commInterface)
            : base(commInterface)
        {
            mCurrentActionIndex = -1;
        }

        protected override void ResetOperation()
        {
            mCurrentActionIndex = -1;

            base.ResetOperation();
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                mCurrentActionIndex++;

                if ((mActionArray != null) && (mCurrentActionIndex < mActionArray.Length))
                {
                    nextAction = mActionArray[mCurrentActionIndex];
                }
            }

            return nextAction;
        }

        protected KWP2000Action[] mActionArray;
        private int mCurrentActionIndex;
    }

    public class ReadMemoryOperation : KWP2000SequencialOperation
    {
        public ReadMemoryOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, uint startAddress, uint numBytes, byte maxBlockSize)
            : base(commInterface)
        {
            // NL serves ReadMemoryByAddress only in a development session. The connect
            // session answers 7F 23 11. DQ's development session does too; the clear path
            // continues when this read fails. A rejected pre-session login does not abort.
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mReadMemory = null;
            mReadMemoryAction = new ReadMemoryAction(commInterface, startAddress, numBytes, maxBlockSize, null);

            mActionArray = new KWP2000Action[1];
            mActionArray[0] = mReadMemoryAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (!success && action.CompletedWithoutCommunicationError && (action is SecurityAccessAction))
            {
                // CH rejects the login before a session. The read still has to run.
                success = true;
            }

            base.OnActionCompleted(action, success);
        }

        protected override bool OnOperationCompleted(bool success)
        {
            if (success)
            {
                mReadMemory = new MemoryImage(mReadMemoryAction.ReadData, mReadMemoryAction.mStartAddress);
            }

            return base.OnOperationCompleted(success);
        }

        public MemoryImage mReadMemory;
        protected ReadMemoryAction mReadMemoryAction;

        public byte FailureResponseCode
        {
            get
            {
                return (mReadMemoryAction != null) ? mReadMemoryAction.FailureResponseCode : (byte)0;
            }
        }

    };

    public class WriteMemoryOperation : KWP2000SequencialOperation
    {
        public WriteMemoryOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, uint startAddress, byte maxBlockSize, byte[] dataToWrite)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mActionArray = new KWP2000Action[1];
            mActionArray[0] = new WriteMemoryAction(commInterface, startAddress, maxBlockSize, dataToWrite, null);
        }
    };

    public class ReadEntireSerialEEPROMOperation : ReadMemoryOperation
    {
        public static readonly UInt32 SERIAL_EEPROM_SIZE = 512;
        public static readonly UInt32 SERIAL_EEPROM_START_ADDR = 0x600000;

        public ReadEntireSerialEEPROMOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface, baudRates, SERIAL_EEPROM_START_ADDR, SERIAL_EEPROM_SIZE, 16)
        {
        }
    };

    //todo - need to match and replace the 16 byte pages individually
    public class WriteEntireSerialEEPROMOperation : KWP2000SequencialOperation
    {
        public static readonly UInt32 EXTERNAL_RAM_SIZE = 0x8000;
        public static readonly UInt32 EXTERNAL_RAM_START_ADDRESS = 0x380000;
        public static readonly UInt32 SERIAL_EEPROM_SIZE = 512;
        public static readonly UInt32 SERIAL_EEPROM_START_ADDRESS = 0x600000;

        public WriteEntireSerialEEPROMOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, byte[] dataToWrite)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mReadRAMAction = new ReadMemoryAction(commInterface, EXTERNAL_RAM_START_ADDRESS, EXTERNAL_RAM_SIZE, DEFAULT_MAX_BLOCK_SIZE, null);
            mReadSerialEEPROMAction = new ReadMemoryAction(commInterface, SERIAL_EEPROM_START_ADDRESS, SERIAL_EEPROM_SIZE, 16, null);
            mWriteRAMAction = new WriteMemoryAction(commInterface, 0, 0, dataToWrite, null);

            mActionArray = new KWP2000Action[3];
            mActionArray[0] = mReadRAMAction;
            mActionArray[1] = mReadSerialEEPROMAction;
            mActionArray[2] = mWriteRAMAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (success)
            {
                if (action == mReadSerialEEPROMAction)
                {
                    bool matched = true;
                    UInt32 x = 0;

                    for (x = (EXTERNAL_RAM_SIZE - SERIAL_EEPROM_SIZE); x >= 0;  x--)
                    {
                        matched = true;

                        for (UInt32 y = 0; y < SERIAL_EEPROM_SIZE; y++)
                        {
                            if (mReadRAMAction.ReadData[x + y] != mReadSerialEEPROMAction.ReadData[y])
                            {
                                matched = false;
                                break;
                            }
                        }

                        if (matched)
                        {
                            break;
                        }
                    }

                    if (matched)
                    {
                        mWriteRAMAction.SetStartAddressAndNumBytes(x + EXTERNAL_RAM_START_ADDRESS, SERIAL_EEPROM_SIZE);
                    }
                    else
                    {
                        CommInterface.DisplayStatusMessage("Could not locate serial eeprom data in RAM", StatusMessageType.USER);
                        success = false;
                    }
                }
            }

            base.OnActionCompleted(action, success);
        }

        protected ReadMemoryAction mReadRAMAction;
        protected ReadMemoryAction mReadSerialEEPROMAction;
        protected WriteMemoryAction mWriteRAMAction;
        protected UInt32 mSerialEEPROMBackupAddress;
    };

    /// <summary>
    /// Shared helpers for the ambiguous 512KB-layout / mirrored-1MB address-space case.
    /// </summary>
    internal static class MirroredFlashLayoutHelper
    {
        public const uint CompareSampleSize = 64;

        public static string PromptMessage(bool forWrite)
        {
            string operation = forWrite ? "write" : "read";
            return "Addressable flash extends past the selected layout (possible 512KB mirror, or a 1MB chip with a 512KB layout).\n\n"
                + "Check for a mirrored mapping before continuing the " + operation + "?";
        }

        public static void ChooseCompareSample(uint layoutStart, uint layoutEnd, out uint sampleOffset, out uint sampleSize)
        {
            uint layoutSize = layoutEnd - layoutStart;
            sampleSize = CompareSampleSize;
            if (sampleSize > layoutSize)
            {
                sampleSize = layoutSize;
            }
            sampleSize &= ~1u;
            if (sampleSize < 2)
            {
                sampleSize = Math.Min(2u, layoutSize & ~1u);
            }

            // Start of flash: across ME7Sum bins (94 real 1MB images), a 64-byte
            // window at offset 0 never matched half1; mid/near-end can both be 0xFF.
            sampleOffset = 0;
        }

        public static bool SamplesMatch(byte[] primary, byte[] mirror)
        {
            if ((primary == null) || (mirror == null) || (primary.Length == 0) || (primary.Length != mirror.Length))
            {
                return false;
            }

            for (int i = 0; i < primary.Length; i++)
            {
                if (primary[i] != mirror[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static string PassedStatus(uint offset)
        {
            return "Mirror check passed: sample at layout offset 0x" + offset.ToString("X") + " matches the region above the layout. Continuing with this layout.";
        }

        public const string FailedStatus = "Mirror check failed: sample does not match. This layout is likely too small (try 29F800 for a 1MB chip).";
        public const string ContinueTitle = "Flash does not appear mirrored";
        public const string ContinueBody = "The sample above the layout does not match the layout range. A 1MB layout (29F800) is probably required.\n\nContinue with this layout anyway?";
    }

    internal static class LayoutValidationText
    {
        public static void Apply(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result, bool actionSucceeded, bool completedWithoutCommunicationError, out bool completed, out bool layoutIsValid, out bool possiblyMirrored, out string message)
        {
            completed = true;
            layoutIsValid = false;
            possiblyMirrored = false;
            message = null;

            if (actionSucceeded)
            {
                if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.Valid)
                {
                    layoutIsValid = true;
                    message = "Memory layout is valid.";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.PossiblyMirrored)
                {
                    possiblyMirrored = true;
                    message = "Addressable flash extends past this layout (possible 512KB mirror or larger chip).";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.StartInvalid)
                {
                    message = "Start address is not a valid address in flash memory.";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.EndInvalid)
                {
                    message = "End address is not a valid address in flash memory.";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.StartIsntLowest)
                {
                    message = "Start address is not the start of flash memory.";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.EndIsntHighest)
                {
                    message = "End address is not the end of flash memory. The ECU may have a larger flash chip than the selected layout (try 29F800), or flash may extend beyond this layout.";
                }
                else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.ValidationDidNotComplete
                    || result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestDownloadNotSupported
                    || result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestDownloadRejected)
                {
                    completed = false;
                    message = "Validation did not complete.";
                }
                else
                {
                    Debug.Fail("Unknown memory layout validation result");
                    completed = false;
                    message = "Unknown validation result.";
                }
            }
            else if (completedWithoutCommunicationError)
            {
                completed = false;
                message = ValidateStartAndEndAddressesWithRequestUploadDownloadAction.SpecificFailureStatusMessage(result);
                if (message == null)
                {
                    message = "Memory layout validation failed.";
                }
            }
        }

        public static bool MustAbort(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result)
        {
            return (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted)
                || (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported)
                || (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadRejected);
        }

        public static void ContinuePrompt(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result, bool writing, out string title, out string body)
        {
            string verb = writing ? "writing" : "reading";
            string specific = ValidateStartAndEndAddressesWithRequestUploadDownloadAction.SpecificFailureStatusMessage(result);
            if (specific != null)
            {
                title = (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.SecurityAccessNotGranted)
                    ? "Security access is not granted"
                    : (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestUploadNotSupported)
                        ? "RequestUpload is not supported"
                        : "RequestUpload was rejected";
                body = specific;
                return;
            }

            title = "Unable to validate memory layout";
            body = "Unable to validate memory layout. Do you want to continue " + verb + " flash memory without validating the memory layout?";

            if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestDownloadNotSupported)
            {
                title = "RequestDownload is not supported";
                body = "The ECU reports that RequestDownload is not supported. Do you want to continue " + verb + " flash memory without validating the memory layout?";
            }
            else if (result == ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result.RequestDownloadRejected)
            {
                title = "RequestDownload was rejected";
                body = "The ECU rejected the RequestDownload service. Do you want to continue " + verb + " flash memory without validating the memory layout?";
            }
        }

        public static bool TryFailurePrompt(ValidateStartAndEndAddressesWithRequestUploadDownloadAction.Result result, bool validationCompleted, bool layoutIsValid, bool writing, out string title, out string body, out UserPromptType promptType, out bool continueOnOk)
        {
            title = null;
            body = null;
            promptType = UserPromptType.OK;
            continueOnOk = false;

            if (!validationCompleted)
            {
                ContinuePrompt(result, writing, out title, out body);
                continueOnOk = !MustAbort(result);
                promptType = continueOnOk ? UserPromptType.OK_CANCEL : UserPromptType.OK;
                return true;
            }

            if (!layoutIsValid)
            {
                string verb = writing ? "writing" : "reading";
                title = "Memory layout appears invalid";
                body = "Memory layout appears invalid. The ECU may expose more addressable flash than this layout covers (common with 512KB chips mirrored into 1MB). Try a larger layout, or continue if you are sure this layout matches the physical chip. Continue " + verb + " flash memory?";
                promptType = UserPromptType.OK_CANCEL;
                continueOnOk = true;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Programming session, then a matching checksum of each supplied block. No upload and no erase.
    /// </summary>
    public class ChecksumFlashBlocksOperation : KWP2000Operation
    {
        public const uint SectorAddress = 0x800000;
        public const uint SectorSize = 0x4000;

        public ChecksumFlashBlocksOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, SecurityAccessAction.SecurityAccessSettings securitySettings, IList<MemoryImage> blocks)
            : base(commInterface)
        {
            EnableProgrammingSession(baudRates, securitySettings);

            mBlocks = new List<MemoryImage>(blocks);
            mTotalBytes = (uint)mBlocks.Sum(block => (long)block.Size);
            mAllMatched = true;
            mChecksum = new ValidateFlashChecksumAction(commInterface, mBlocks[0].StartAddress, mBlocks[0].RawData);
        }

        public bool ReportedMismatch { get; private set; }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            return nextAction ?? mChecksum;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (action != mChecksum)
            {
                base.OnActionCompleted(action, success);
                return;
            }

            MemoryImage block = mBlocks[mIndex];
            bool matched = success && mChecksum.IsFlashChecksumCorrect;
            if (!matched && success)
            {
                ReportedMismatch = true;
            }

            mAllMatched &= matched;
            CommInterface.DisplayStatusMessage("Checksum " + RangeText(block.StartAddress, block.Size) + (matched ? " matched." : success ? " reported a mismatch." : " failed."), StatusMessageType.USER);
            if (!success)
            {
                OperationCompleted(false);
                return;
            }

            mBytesChecked += block.Size;
            OnUpdatePercentComplete(((float)mBytesChecked) / ((float)mTotalBytes) * 100.0f);

            if (++mIndex >= mBlocks.Count)
            {
                OperationCompleted(mAllMatched);
                return;
            }

            block = mBlocks[mIndex];
            mChecksum = new ValidateFlashChecksumAction(KWP2000CommInterface, block.StartAddress, block.RawData);
            Continue();
        }

        private void Continue()
        {
            if (!StartNextAction() || CurrentAction == null)
            {
                OperationCompleted(false);
            }
        }

        private readonly IList<MemoryImage> mBlocks;
        private readonly uint mTotalBytes;
        private uint mBytesChecked;
        private ValidateFlashChecksumAction mChecksum;
        private int mIndex;
        private bool mAllMatched;
    }

    /// <summary>
    /// Uploads the given blocks into their existing buffers. No layout probe and no checksum.
    /// </summary>
    public class ReadFlashBlocksOperation : KWP2000Operation
    {
        private enum Step
        {
            Upload,
            Transfer,
            Exit
        }

        public ReadFlashBlocksOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, SecurityAccessAction.SecurityAccessSettings securitySettings, IList<MemoryImage> blocks)
            : base(commInterface)
        {
            EnableProgrammingSession(baudRates, securitySettings);

            mBlocks = new List<MemoryImage>(blocks);
            mStep = Step.Upload;
            mMaxBlockSize = TransferDataAction.DEFAULT_MAX_BLOCK_SIZE;
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();
            if (nextAction != null)
            {
                return nextAction;
            }

            MemoryImage block = mBlocks[mIndex];
            if (mStep == Step.Upload)
            {
                CommInterface.DisplayStatusMessage("Reading " + RangeText(block.StartAddress, block.Size) + " from the ECU.", StatusMessageType.USER);
                mCurrent = new RequestUploadFromECUAction(KWP2000CommInterface, block.StartAddress, block.Size, TransferDataAction.CompressionType.Uncompressed, TransferDataAction.EncryptionType.Unencrypted);
            }
            else if (mStep == Step.Transfer)
            {
                mCurrent = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, TransferDataAction.EncryptionType.Unencrypted, TransferDataAction.CompressionType.Uncompressed, mMaxBlockSize, block.RawData, null);
            }
            else
            {
                mCurrent = new RequestTransferExitAction(KWP2000CommInterface);
            }

            return mCurrent;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (action != mCurrent)
            {
                base.OnActionCompleted(action, success);
                return;
            }

            if (!success)
            {
                OperationCompleted(false);
                return;
            }

            if (mStep == Step.Upload)
            {
                mMaxBlockSize = ((RequestUploadFromECUAction)action).GetMaxBlockSize();
                mStep = Step.Transfer;
            }
            else if (mStep == Step.Transfer)
            {
                mStep = Step.Exit;
            }
            else if (++mIndex >= mBlocks.Count)
            {
                OperationCompleted(true);
                return;
            }
            else
            {
                mStep = Step.Upload;
            }

            if (!StartNextAction() || CurrentAction == null)
            {
                OperationCompleted(false);
            }
        }

        private readonly IList<MemoryImage> mBlocks;
        private CommunicationAction mCurrent;
        private Step mStep;
        private int mIndex;
        private byte mMaxBlockSize;
    }

    /// <summary>
    /// Closing step for the programming flag: a 2-byte upload, transfer, and exit, then three matching checksums.
    /// </summary>
    // ME7 kernel: an accepted RequestUpload (the layout check sends one) blocks the 33 C5
    // match streak until an upload finishes with RequestTransferExit. Three matches in a row
    // with no open upload clear bit 7 of 95040 page 30/31 byte 8, which the login set.
    internal class ProgrammingFlagCloser
    {
        private const int ChecksumCount = 3;

        private enum Step
        {
            RequestUpload,
            Transfer,
            Exit,
            Checksum
        }

        public ProgrammingFlagCloser(KWP2000Interface commInterface, uint uploadAddress, MemoryImage checksumBlock)
        {
            mCommInterface = commInterface;
            mUploadAddress = uploadAddress;
            mChecksumBlock = checksumBlock;
            mMaxBlockSize = TransferDataAction.DEFAULT_MAX_BLOCK_SIZE;
        }

        public CommunicationAction NextAction()
        {
            switch (mStep)
            {
                case Step.RequestUpload:
                {
                    mCommInterface.DisplayStatusMessage("Closing the open upload: uploading 0x" + mUploadAddress.ToString("X8") + " to 0x" + (mUploadAddress + 1).ToString("X8") + ".", StatusMessageType.USER);
                    return new RequestUploadFromECUAction(mCommInterface, mUploadAddress, 2, TransferDataAction.CompressionType.Uncompressed, TransferDataAction.EncryptionType.Unencrypted);
                }
                case Step.Transfer:
                {
                    return new TransferDataAction(mCommInterface, TransferDataAction.TransferMode.UploadFromECU, TransferDataAction.EncryptionType.Unencrypted, TransferDataAction.CompressionType.Uncompressed, mMaxBlockSize, new byte[2], null);
                }
                case Step.Exit:
                {
                    return new RequestTransferExitAction(mCommInterface);
                }
                default:
                {
                    mCommInterface.DisplayStatusMessage("Programming flag checksum " + (mChecksumsSent + 1) + " of " + ChecksumCount + ": 0x" + mChecksumBlock.StartAddress.ToString("X8") + " to 0x" + (mChecksumBlock.EndAddress - 1).ToString("X8") + ".", StatusMessageType.USER);
                    return new ValidateFlashChecksumAction(mCommInterface, mChecksumBlock.StartAddress, mChecksumBlock.RawData);
                }
            }
        }

        /// <summary>
        /// Returns true when the closing step has ended, whether or not it succeeded.
        /// </summary>
        public bool OnActionCompleted(CommunicationAction action, bool success)
        {
            var checksum = action as ValidateFlashChecksumAction;
            if (!success || ((checksum != null) && !checksum.IsFlashChecksumCorrect))
            {
                return End("Closing the programming flag failed at " + mStep + ". The flag may stay set.");
            }

            if (mStep == Step.RequestUpload)
            {
                mMaxBlockSize = ((RequestUploadFromECUAction)action).GetMaxBlockSize();
                mStep = Step.Transfer;
            }
            else if (mStep == Step.Transfer)
            {
                mStep = Step.Exit;
            }
            else if (mStep == Step.Exit)
            {
                mStep = Step.Checksum;
            }
            else if (++mChecksumsSent == ChecksumCount)
            {
                return End("Sent " + ChecksumCount + " matching checksums with the upload closed. The programming flag should be clear.");
            }

            return false;
        }

        private bool End(string message)
        {
            mCommInterface.DisplayStatusMessage(message, StatusMessageType.USER);
            return true;
        }

        private readonly KWP2000Interface mCommInterface;
        private readonly uint mUploadAddress;
        private readonly MemoryImage mChecksumBlock;
        private Step mStep;
        private byte mMaxBlockSize;
        private int mChecksumsSent;
    }

    public class ReadExternalFlashOperation : KWP2000Operation
    {
        public struct UploadRange
        {
            public uint StartAddress;
            public uint Size;

            public UploadRange(uint startAddress, uint size)
            {
                StartAddress = startAddress;
                Size = size;
            }
        }

        public class ReadExternalFlashSettings
        {
            public bool CheckIfSectorReadRequired = true;
            public bool OnlyReadNonMatchingSectors = false;
            public bool VerifyReadData = true;

            // Null checksums every uploaded sector when VerifyReadData is true.
            public UploadRange[] ChecksumRanges = null;

            public SecurityAccessAction.SecurityAccessSettings SecuritySettings = new SecurityAccessAction.SecurityAccessSettings();
        }

        public ReadExternalFlashOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, ReadExternalFlashSettings readSettings, IEnumerable<MemoryImage> flashBlockList)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.ProgrammingSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
            EnableAutoNegotiateSecurity(readSettings.SecuritySettings);

            mFlashBlockList = flashBlockList;
            mCurrentBlock = mFlashBlockList.GetEnumerator();
            mCurrentBlock.MoveNext();
            mNumAttemptsForCurrentBlock = 0;
            mMaxBlockSize = TransferDataAction.DEFAULT_MAX_BLOCK_SIZE;
            mEncryptionType = TransferDataAction.EncryptionType.Unencrypted;
            mCompressionType = TransferDataAction.CompressionType.Uncompressed;

            mHasVerfiedRequestUploadSupported = false;

            mState = ReadingState.Start;

            bool shouldValidateMemoryLayout = true;

            if (shouldValidateMemoryLayout)
            {
                mState = ReadingState.ValidateMemoryLayout;
            }

            CheckIfSectorsRequireRead = readSettings.CheckIfSectorReadRequired;
            OnlyReadRequiredSectors = readSettings.OnlyReadNonMatchingSectors;
            ShouldVerifyReadSectors = readSettings.VerifyReadData;
            mChecksumRanges = readSettings.ChecksumRanges;

            ElapsedTimeReadingUnrequiredSectors = TimeSpan.Zero;
            ElapsedTimeCheckingIfSectorsRequireReading = TimeSpan.Zero;
            ElapsedTimeVerifyingReadSectors = TimeSpan.Zero;

            mTotalBytesValidated = 0;
            mTotalBytesToRead = 0;

            foreach (var currentBlock in flashBlockList)
            {
                Debug.Assert(currentBlock.StartAddress % 2 == 0, "start address is not a multiple of 2");
                Debug.Assert(currentBlock.Size > 0, "size is zero");
                Debug.Assert(currentBlock.Size % 2 == 0, "size is not a multiple of 2");

                mTotalBytesToRead += currentBlock.Size;
            }
        }

        public bool CheckIfSectorsRequireRead { get; private set; }
        public bool OnlyReadRequiredSectors { get; private set; }
        public bool ShouldVerifyReadSectors { get; private set; }
        public bool WasCancelled { get { return mCancelRequested; } }

        private bool ShouldChecksumSector(MemoryImage image)
        {
            return (mChecksumRanges == null) || (mChecksumRanges.Length == 0)
                || mChecksumRanges.Any(range => (image.StartAddress == range.StartAddress) && (image.Size == range.Size));
        }

        public TimeSpan ElapsedTimeReadingUnrequiredSectors { get; private set; }
        public TimeSpan ElapsedTimeCheckingIfSectorsRequireReading { get; private set; }
        public TimeSpan ElapsedTimeVerifyingReadSectors { get; private set; }

        public IEnumerable<MemoryImage> FlashBlockList
        {
            get { return mFlashBlockList; }
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                MemoryImage currentMemoryImage = null;
                if (mState != ReadingState.FinishedAll)
                {
                    currentMemoryImage = mCurrentBlock.Current;
                }

                if ((currentMemoryImage != null) || (mState == ReadingState.ClearFlagAfterFailure))
                {
                    if (mState == ReadingState.Start)
                    {
                        CommInterface.DisplayStatusMessage("Starting to read data block.", StatusMessageType.USER);

                        mCurrentSectorRequiresRead = true;

                        // The layout check leaves an accepted RequestUpload open. Until an upload ends with
                        // RequestTransferExit, the ME7 kernel does not count matching checksums toward
                        // clearing the programming flag, so checksum only after one sector has been uploaded.
                        if (CheckIfSectorsRequireRead && mHasVerfiedRequestUploadSupported)
                        {
                            mState = ReadingState.CheckIfReadRequired;
                        }
                        else
                        {
                            mState = ReadingState.RequestUpload;
                        }
                    }

                    switch (mState)
                    {
                        case ReadingState.ValidateMemoryLayout:
                        {
                            nextAction = new ValidateStartAndEndAddressesWithRequestUploadDownloadAction(KWP2000CommInterface, mFlashBlockList.First().StartAddress, mFlashBlockList.Last().EndAddress);
                            break;
                        }
                        case ReadingState.MirrorCheckRequestPrimary:
                        {
                            CommInterface.DisplayStatusMessage("Checking for mirrored flash: reading sample at 0x" + (mFlashBlockList.First().StartAddress + mMirrorCheckOffset).ToString("X8") + ".", StatusMessageType.USER);
                            nextAction = new RequestUploadFromECUAction(KWP2000CommInterface, mFlashBlockList.First().StartAddress + mMirrorCheckOffset, mMirrorCheckSize, mCompressionType, mEncryptionType);
                            break;
                        }
                        case ReadingState.MirrorCheckTransferPrimary:
                        {
                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, mEncryptionType, mCompressionType, mMaxBlockSize, mMirrorCheckPrimaryData, null);
                            break;
                        }
                        case ReadingState.MirrorCheckExitPrimary:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case ReadingState.MirrorCheckRequestMirror:
                        {
                            uint mirrorAddress = mFlashBlockList.Last().EndAddress + mMirrorCheckOffset;
                            CommInterface.DisplayStatusMessage("Checking for mirrored flash: reading sample at 0x" + mirrorAddress.ToString("X8") + ".", StatusMessageType.USER);
                            nextAction = new RequestUploadFromECUAction(KWP2000CommInterface, mirrorAddress, mMirrorCheckSize, mCompressionType, mEncryptionType);
                            break;
                        }
                        case ReadingState.MirrorCheckTransferMirror:
                        {
                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, mEncryptionType, mCompressionType, mMaxBlockSize, mMirrorCheckMirrorData, null);
                            break;
                        }
                        case ReadingState.MirrorCheckExitMirror:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case ReadingState.CheckIfReadRequired:
                        {
                            uint endAddress = currentMemoryImage.StartAddress + (uint)currentMemoryImage.RawData.Length - 1;
                            CommInterface.DisplayStatusMessage("Calculating flash checksum to determine if reading is necessary for range: 0x" + currentMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);
                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, currentMemoryImage.StartAddress, currentMemoryImage.RawData);
                            break;
                        }
                        case ReadingState.RequestUpload:
                        {
                            nextAction = new RequestUploadFromECUAction(KWP2000CommInterface, currentMemoryImage.StartAddress, (uint)currentMemoryImage.RawData.Length, mCompressionType, mEncryptionType);
                            break;
                        }
                        case ReadingState.TransferData:
                        {
                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, mEncryptionType, mCompressionType, mMaxBlockSize, currentMemoryImage.RawData, this.NotifyBytesReadHandler);
                            break;
                        }
                        case ReadingState.ExitTransfer:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case ReadingState.ValidateReadData:
                        {
                            uint endAddress = currentMemoryImage.StartAddress + (uint)currentMemoryImage.RawData.Length - 1;
                            CommInterface.DisplayStatusMessage("Calculating flash checksum to determine if reading was successful for range: 0x" + currentMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);
                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, currentMemoryImage.StartAddress, currentMemoryImage.RawData);
                            break;
                        }
                        case ReadingState.FinishedAll:
                        {
                            nextAction = null;
                            break;
                        }
                        case ReadingState.ClearFlagAfterFailure:
                        {
                            nextAction = mClearFlag.NextAction();
                            break;
                        }
                        default:
                        {
                            Debug.Fail("Unknown reading state");
                            break;
                        }
                    }
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            //only pay attention to actions this code started
            if (action == mMyLastStartedAction)
            {
                if (mState == ReadingState.ClearFlagAfterFailure)
                {
                    if (mClearFlag.OnActionCompleted(action, success))
                    {
                        success = false;//to ensure this is recognized as a failure
                        OperationCompleted(false);
                    }
                    else
                    {
                        success = true;
                    }
                }
                else if (action is ValidateFlashChecksumAction)
                {
                    switch (mState)
                    {
                        case ReadingState.CheckIfReadRequired:
                        {
                            ElapsedTimeCheckingIfSectorsRequireReading += action.ActionElapsedTime;

                            if (!mCurrentSectorRequiresRead)
                            {
                                ElapsedTimeReadingUnrequiredSectors += action.ActionElapsedTime;
                            }

                            break;
                        }
                        case ReadingState.ValidateReadData:
                        {
                            ElapsedTimeVerifyingReadSectors += action.ActionElapsedTime;

                            break;
                        }
                    }

                    bool currentBlockFinished = false;

                    if (mState == ReadingState.ValidateReadData)
                    {
                        mNumAttemptsForCurrentBlock++;
                    }

                    if (success)
                    {
                        if (((ValidateFlashChecksumAction)action).IsFlashChecksumCorrect)
                        {
                            if (mState == ReadingState.CheckIfReadRequired)
                            {
                                mCurrentSectorRequiresRead = false;

                                if (OnlyReadRequiredSectors)
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum matches, reading flash data is unnecessary, skipping read.", StatusMessageType.USER);
                                    currentBlockFinished = true;
                                }
                                else
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum matches, reading flash data is unnecessary, but reading anyway.", StatusMessageType.USER);
                                }
                            }
                            else
                            {
                                Debug.Assert(mState == ReadingState.ValidateReadData);

                                currentBlockFinished = true;
                                CommInterface.DisplayStatusMessage("Flash checksum matches read data, reading was successful.", StatusMessageType.USER);
                            }
                        }
                        else
                        {
                            if (mState == ReadingState.CheckIfReadRequired)
                            {
                                CommInterface.DisplayStatusMessage("Flash checksum does not match, reading flash data is necessary.", StatusMessageType.USER);
                            }
                            else
                            {
                                Debug.Assert(mState == ReadingState.ValidateReadData);

                                if (mNumAttemptsForCurrentBlock <= 3)
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum does not match, trying to read flash data again.", StatusMessageType.USER);
                                }
                                else
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum still does not match after 3 attempts, giving up and skipping.", StatusMessageType.USER);
                                    currentBlockFinished = true;
                                }
                            }
                        }
                    }
                    else if (action.CompletedWithoutCommunicationError)
                    {
                        if (mState == ReadingState.CheckIfReadRequired)
                        {
                            CommInterface.DisplayStatusMessage("Failed to check if existing flash checksum matches, reading anyways.", StatusMessageType.USER);
                        }
                        else
                        {
                            Debug.Assert(mState == ReadingState.ValidateReadData);

                            CommInterface.DisplayStatusMessage("Failed to check if flash checksum matches, assuming read was successful.", StatusMessageType.USER);
                            currentBlockFinished = true;
                        }

                        success = true;
                    }

                    if (success)
                    {
                        if (currentBlockFinished)
                        {
                            mState = ReadingState.FinishedBlock;
                        }
                        else
                        {
                            mState = ReadingState.RequestUpload;
                        }
                    }
                }
                else if (action is RequestUploadFromECUAction)
                {
                    if (!mCurrentSectorRequiresRead)
                    {
                        ElapsedTimeReadingUnrequiredSectors += action.ActionElapsedTime;
                    }

                    mCurrentBlockBytesRead = 0;

                    if (success)
                    {
                        mHasVerfiedRequestUploadSupported = true;

                        mMaxBlockSize = ((RequestUploadFromECUAction)action).GetMaxBlockSize();

                        if (mState == ReadingState.MirrorCheckRequestPrimary)
                        {
                            mState = ReadingState.MirrorCheckTransferPrimary;
                        }
                        else if (mState == ReadingState.MirrorCheckRequestMirror)
                        {
                            mState = ReadingState.MirrorCheckTransferMirror;
                        }
                        else
                        {
                            mState = ReadingState.TransferData;
                        }
                    }
                }
                else if (action is TransferDataAction)
                {
                    if (!mCurrentSectorRequiresRead)
                    {
                        ElapsedTimeReadingUnrequiredSectors += action.ActionElapsedTime;
                    }

                    if (success)
                    {
                        if (mState == ReadingState.MirrorCheckTransferPrimary)
                        {
                            mState = ReadingState.MirrorCheckExitPrimary;
                        }
                        else if (mState == ReadingState.MirrorCheckTransferMirror)
                        {
                            mState = ReadingState.MirrorCheckExitMirror;
                        }
                        else
                        {
                            mState = ReadingState.ExitTransfer;
                        }
                    }
                }
                else if (action is RequestTransferExitAction)
                {
                    if (!mCurrentSectorRequiresRead)
                    {
                        ElapsedTimeReadingUnrequiredSectors += action.ActionElapsedTime;
                    }

                    mCurrentBlockBytesRead = 0;

                    if (success)
                    {
                        if (mState == ReadingState.MirrorCheckExitPrimary)
                        {
                            mState = ReadingState.MirrorCheckRequestMirror;
                        }
                        else if (mState == ReadingState.MirrorCheckExitMirror)
                        {
                            if (MirroredFlashLayoutHelper.SamplesMatch(mMirrorCheckPrimaryData, mMirrorCheckMirrorData))
                            {
                                CommInterface.DisplayStatusMessage(MirroredFlashLayoutHelper.PassedStatus(mMirrorCheckOffset), StatusMessageType.USER);
                                mState = ReadingState.Start;
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage(MirroredFlashLayoutHelper.FailedStatus, StatusMessageType.USER);
                                AskReadPrompt(action, MirroredFlashLayoutHelper.ContinueTitle, MirroredFlashLayoutHelper.ContinueBody, UserPromptType.OK_CANCEL, true);
                                return;
                            }
                        }
                        else if (ShouldVerifyReadSectors && ShouldChecksumSector(mCurrentBlock.Current))
                        {
                            mState = ReadingState.ValidateReadData;
                        }
                        else
                        {
                            if (ShouldVerifyReadSectors)
                            {
                                MemoryImage uploaded = mCurrentBlock.Current;
                                uint uploadedEnd = uploaded.StartAddress + uploaded.Size - 1;
                                CommInterface.DisplayStatusMessage("Uploaded 0x" + uploaded.StartAddress.ToString("X8") + " to 0x" + uploadedEnd.ToString("X8") + ". No checksum.", StatusMessageType.USER);
                            }

                            mState = ReadingState.FinishedBlock;
                        }
                    }
                }
                else if (action is ValidateStartAndEndAddressesWithRequestUploadDownloadAction)
                {
                    bool validationCompleted = true;
                    bool layoutIsValid = false;
                    bool possiblyMirrored = false;

                    string validationMesage = null;

                    var validationResult = ((ValidateStartAndEndAddressesWithRequestUploadDownloadAction)action).ValidationResult;
                    LayoutValidationText.Apply(validationResult, success, action.CompletedWithoutCommunicationError, out validationCompleted, out layoutIsValid, out possiblyMirrored, out validationMesage);

                    if (validationMesage != null)
                    {
                        CommInterface.DisplayStatusMessage(validationMesage, StatusMessageType.USER);
                    }

                    success = validationCompleted && layoutIsValid;

                    if (possiblyMirrored)
                    {
                        PostUserPrompt(
                            "Memory layout may be ambiguous",
                            MirroredFlashLayoutHelper.PromptMessage(false),
                            UserPromptType.CONTINUE_CHECK_CANCEL,
                            result => ApplyReadMirrorLayoutPrompt(action, result));
                        return;
                    }

                    string promptTitle;
                    string promptBody;
                    UserPromptType promptType;
                    bool continueOnOk;
                    if (!success && LayoutValidationText.TryFailurePrompt(validationResult, validationCompleted, layoutIsValid, false, out promptTitle, out promptBody, out promptType, out continueOnOk))
                    {
                        AskReadPrompt(action, promptTitle, promptBody, promptType, continueOnOk);
                        return;
                    }
                    else if (success)
                    {
                        mState = ReadingState.Start;
                    }
                }
            }

            FinishReadAction(action, success);
        }

        private void ApplyReadMirrorLayoutPrompt(CommunicationAction action, UserPromptResult promptResult)
        {
            bool success = false;

            if (promptResult == UserPromptResult.OK)
            {
                success = true;
                mState = ReadingState.Start;
            }
            else if (promptResult == UserPromptResult.CHECK)
            {
                MirroredFlashLayoutHelper.ChooseCompareSample(
                    mFlashBlockList.First().StartAddress,
                    mFlashBlockList.Last().EndAddress,
                    out mMirrorCheckOffset,
                    out mMirrorCheckSize);
                mMirrorCheckPrimaryData = new byte[mMirrorCheckSize];
                mMirrorCheckMirrorData = new byte[mMirrorCheckSize];
                success = true;
                mState = ReadingState.MirrorCheckRequestPrimary;
            }

            FinishReadAction(action, success);
        }

        private void AskReadPrompt(CommunicationAction action, string title, string message, UserPromptType promptType, bool continueOnOk)
        {
            PostUserPrompt(title, message, promptType, result =>
            {
                bool success = false;
                if (continueOnOk && (result == UserPromptResult.OK))
                {
                    success = true;
                    mState = ReadingState.Start;
                }

                FinishReadAction(action, success);
            });
        }

        private void FinishReadAction(CommunicationAction action, bool success)
        {
            if (action == mMyLastStartedAction)
            {
                if (mState == ReadingState.FinishedBlock)
                {
                    mTotalBytesValidated += mCurrentBlock.Current.Size;
                    OnUpdatePercentComplete(((float)mTotalBytesValidated) / ((float)mTotalBytesToRead) * 100.0f);

                    // More than 3 attempts means the sector was skipped with a checksum mismatch.
                    if ((mFirstFinishedBlock == null) && (mNumAttemptsForCurrentBlock <= 3))
                    {
                        mFirstFinishedBlock = mCurrentBlock.Current;
                    }

                    mNumAttemptsForCurrentBlock = 0;

                    if (!mCurrentBlock.MoveNext())
                    {
                        mState = ReadingState.FinishedAll;
                    }
                    else
                    {
                        mState = ReadingState.Start;
                    }
                }

                if (mCancelRequested && (mState != ReadingState.ClearFlagAfterFailure) && (mState != ReadingState.FinishedAll))
                {
                    success = false;
                }

                if (!success && (mState != ReadingState.ClearFlagAfterFailure))
                {
                    CommInterface.DisplayStatusMessage(mCancelRequested ? "Reading ECU flash memory cancelled." : "Reading ECU flash memory failed.", StatusMessageType.USER);
                    if (KWP2000CommInterface.CurrentDiagnosticSessionType != KWP2000DiagnosticSessionType.ProgrammingSession)
                    {
                        KWP2000SettingsDefaults.LogMe75Pin121Hint(CommInterface);
                    }

                    if (mFirstFinishedBlock != null)
                    {
                        mClearFlag = new ProgrammingFlagCloser(KWP2000CommInterface, mFlashBlockList.First().StartAddress, mFirstFinishedBlock);
                        mState = ReadingState.ClearFlagAfterFailure;
                        success = true;
                    }
                    else
                    {
                        CommInterface.DisplayStatusMessage("No sector finished, so the programming flag stays set. Use Clear DTCs.", StatusMessageType.USER);
                    }
                }
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);
        }

        public override void Abort()
        {
            // A second cancel, or nothing to close with, stops immediately.
            bool closeFirst = IsRunning && !mCancelRequested && (mFirstFinishedBlock != null) && (mState != ReadingState.ClearFlagAfterFailure);
            mCancelRequested = true;
            if (closeFirst)
            {
                CommInterface.DisplayStatusMessage("Cancelling after the current request, then closing the programming flag. Cancel again to stop now.", StatusMessageType.USER);
                return;
            }

            base.Abort();
        }

        private void NotifyBytesReadHandler(UInt32 newAmountWritten, UInt32 totalWritten, UInt32 totalToWrite)
        {
            mCurrentBlockBytesRead += newAmountWritten;

            OnUpdatePercentComplete(((float)mCurrentBlockBytesRead + mTotalBytesValidated) / ((float)mTotalBytesToRead) * 100.0f);
        }

        private enum ReadingState
        {
            ValidateMemoryLayout,
            MirrorCheckRequestPrimary,
            MirrorCheckTransferPrimary,
            MirrorCheckExitPrimary,
            MirrorCheckRequestMirror,
            MirrorCheckTransferMirror,
            MirrorCheckExitMirror,
            Start,
            CheckIfReadRequired,
            RequestUpload,
            TransferData,
            ExitTransfer,
            ValidateReadData,
            FinishedBlock,
            FinishedAll,
            ClearFlagAfterFailure
        }

        private CommunicationAction mMyLastStartedAction;
        private ReadingState mState;
        private IEnumerable<MemoryImage> mFlashBlockList;
        private IEnumerator<MemoryImage> mCurrentBlock;
        private byte mMaxBlockSize;
        private bool mHasVerfiedRequestUploadSupported;
        private readonly UploadRange[] mChecksumRanges;
        private MemoryImage mFirstFinishedBlock;
        private ProgrammingFlagCloser mClearFlag;
        private volatile bool mCancelRequested;
        private int mNumAttemptsForCurrentBlock;
        private uint mCurrentBlockBytesRead;
        private uint mTotalBytesToRead;
        private uint mTotalBytesValidated;
        private TransferDataAction.CompressionType mCompressionType;
        private TransferDataAction.EncryptionType mEncryptionType;

        private bool mCurrentSectorRequiresRead;

        private uint mMirrorCheckOffset;
        private uint mMirrorCheckSize;
        private byte[] mMirrorCheckPrimaryData;
        private byte[] mMirrorCheckMirrorData;
    };

    public class WriteExternalFlashOperation : KWP2000Operation
    {
        public class WriteExternalFlashSettings
        {
            public bool CheckIfWriteRequired = true;
            public bool OnlyWriteNonMatchingSectors = false;
            public bool VerifyWrittenData = true;
            public bool EraseEntireFlashAtOnce = false;
            public MemoryLayout FlashMemoryLayout;

            public SecurityAccessAction.SecurityAccessSettings SecuritySettings = new SecurityAccessAction.SecurityAccessSettings();
        }

        public WriteExternalFlashOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, WriteExternalFlashSettings writeSettings, IEnumerable<MemoryImage> sectorImages)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.ProgrammingSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
            EnableAutoNegotiateSecurity(writeSettings.SecuritySettings);

            WasFailureCausedByPreviousIncompleteDownload = false;

            ShouldCheckIfFlashRequired = writeSettings.CheckIfWriteRequired;
            OnlyFlashRequiredSectors = writeSettings.OnlyWriteNonMatchingSectors;
            ShouldVerifyFlashedSectors = writeSettings.VerifyWrittenData;

            mTotalBytesToFlash = 0;
            mTotalBytesValidated = 0;

            mFlashBlockList = new List<FlashBlock>();

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

            mCurrentBlock = mFlashBlockList.First();

            bool shouldValidateMemoryLayout = true;

            mState = shouldValidateMemoryLayout ? FlashingState.ValidateStartAndEndAddresses : FlashingState.StartBlock;

            mMaxBlockSize = TransferDataAction.DEFAULT_MAX_BLOCK_SIZE;
            mEraseEntireFlashAtOnce = writeSettings.EraseEntireFlashAtOnce;
            mFlashMemoryLayout = writeSettings.FlashMemoryLayout;
            mValidatedEraseMode = false;
            mEncryptionType = TransferDataAction.EncryptionType.Bosch;
            mCompressionType = TransferDataAction.CompressionType.Bosch;

            ElapsedTimeFlashingUnrequiredSectors = TimeSpan.Zero;
            ElapsedTimeCheckingIfSectorsRequireFlashing = TimeSpan.Zero;
            ElapsedTimeVerifyingWrittenSectors = TimeSpan.Zero;

            Debug.Assert(!mEraseEntireFlashAtOnce || (mFlashBlockList.Count == 1));
        }

        protected void RestartFlashingProcessAndEraseEntireFlashAtOnce()
        {
            mEraseEntireFlashAtOnce = true;
            ShouldCheckIfFlashRequired = false;
            OnlyFlashRequiredSectors = false;

            mTotalBytesValidated = 0;
            mValidatedEraseMode = true;
            mState = FlashingState.StartBlock;

            foreach (var block in mFlashBlockList)
            {
                block.mFlashingIsRequired = !ShouldCheckIfFlashRequired;
                block.mWasErased = false;
                block.mFlashComplete = false;
                block.mFlashSuccessful = false;
                block.mNumFlashAttempts = 0;
                block.mNumBytesFlashed = 0;
            }

            mCurrentBlock = mFlashBlockList.First();
        }

        private bool ApplyEraseFailurePromptResult(UserPromptResult promptResult)
        {
            if (promptResult == UserPromptResult.YES)
            {
                CommInterface.DisplayStatusMessage("Erasing entire flash and restarting flashing process.", StatusMessageType.USER);

                RestartFlashingProcessAndEraseEntireFlashAtOnce();
                return true;
            }

            if (promptResult == UserPromptResult.NO)
            {
                CommInterface.DisplayStatusMessage("Skipping flash sector and continuing flashing process.", StatusMessageType.USER);

                mCurrentBlock.mFlashComplete = true;
                mState = FlashingState.FinishedBlock;
                return true;
            }

            return false;
        }

        public bool OnlyFlashRequiredSectors { get; private set; }
        public bool ShouldCheckIfFlashRequired { get; private set; }
        public bool ShouldVerifyFlashedSectors { get; private set; }

        public int NumSectors { get { return mFlashBlockList.Count; } }
        public int NumSuccessfullyFlashedSectors
        {
            get
            {
                int numSuccessFullyFlashedSectors = 0;
                foreach (var block in mFlashBlockList)
                {
                    if (block.mFlashSuccessful && block.mFlashComplete)
                    {
                        numSuccessFullyFlashedSectors++;
                    }
                }

                return numSuccessFullyFlashedSectors;
            }
        }

        public TimeSpan ElapsedTimeFlashingUnrequiredSectors { get; private set; }
        public TimeSpan ElapsedTimeCheckingIfSectorsRequireFlashing { get; private set; }
        public TimeSpan ElapsedTimeVerifyingWrittenSectors { get; private set; }

        public bool WasFailureCausedByPreviousIncompleteDownload { get; private set; }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                if (mCurrentBlock != null)
                {
                    if ((mState == FlashingState.StartBlock) || (mState == FlashingState.FinishedBlock))
                    {
                        //find the next block to flash
                        foreach (var block in mFlashBlockList)
                        {
                            if (!block.mFlashComplete)
                            {
                                mCurrentBlock = block;
                                break;
                            }
                        }

                        mState = FlashingState.StartBlock;

                        //if we didn't find another block to flash, we are done
                        if (mCurrentBlock.mFlashComplete)
                        {
                            mState = FlashingState.FinishedBlock;

                            if (!mClearFlagAttempted)
                            {
                                mClearFlagAttempted = true;
                                FlashBlock clearFlagBlock = mFlashBlockList.FirstOrDefault(b => b.mFlashComplete && b.mFlashSuccessful);
                                if (clearFlagBlock != null)
                                {
                                    mClearFlag = new ProgrammingFlagCloser(KWP2000CommInterface, mFlashBlockList.First().mMemoryImage.StartAddress, clearFlagBlock.mMemoryImage);
                                    mState = FlashingState.ClearFlag;
                                }
                            }
                        }
                    }

                    if (mState == FlashingState.StartBlock)
                    {
                        CommInterface.DisplayStatusMessage("Starting to flash data block.", StatusMessageType.USER);

                        if (!mCurrentBlock.mFlashingIsRequired)
                        {
                            mState = FlashingState.CheckIfFlashRequired;
                        }
                        else
                        {
                            mState = FlashingState.StartProgrammingBlock;
                        }

                        mCurrentBlock.mNumFlashAttempts++;
                    }

                    if (mState == FlashingState.StartProgrammingBlock)
                    {
                        if (mEraseEntireFlashAtOnce && (mCurrentBlock != mFlashBlockList.First()))
                        {
                            mState = FlashingState.RequestDownload;
                        }
                        else
                        {
                            mState = FlashingState.EraseFlash;
                        }
                    }

                    switch (mState)
                    {
                        case FlashingState.ValidateStartAndEndAddresses:
                        {
                            nextAction = new ValidateStartAndEndAddressesWithRequestUploadDownloadAction(KWP2000CommInterface, mFlashBlockList.First().mMemoryImage.StartAddress, mFlashBlockList.Last().mMemoryImage.EndAddress);
                            break;
                        }
                        case FlashingState.MirrorCheckRequestPrimary:
                        {
                            CommInterface.DisplayStatusMessage("Checking for mirrored flash: reading sample at 0x" + (mFlashBlockList.First().mMemoryImage.StartAddress + mMirrorCheckOffset).ToString("X8") + ".", StatusMessageType.USER);
                            nextAction = new RequestUploadFromECUAction(KWP2000CommInterface, mFlashBlockList.First().mMemoryImage.StartAddress + mMirrorCheckOffset, mMirrorCheckSize, TransferDataAction.CompressionType.Uncompressed, TransferDataAction.EncryptionType.Unencrypted);
                            break;
                        }
                        case FlashingState.MirrorCheckTransferPrimary:
                        {
                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, TransferDataAction.EncryptionType.Unencrypted, TransferDataAction.CompressionType.Uncompressed, mMaxBlockSize, mMirrorCheckPrimaryData, null);
                            break;
                        }
                        case FlashingState.MirrorCheckExitPrimary:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case FlashingState.MirrorCheckRequestMirror:
                        {
                            uint mirrorAddress = mFlashBlockList.Last().mMemoryImage.EndAddress + mMirrorCheckOffset;
                            CommInterface.DisplayStatusMessage("Checking for mirrored flash: reading sample at 0x" + mirrorAddress.ToString("X8") + ".", StatusMessageType.USER);
                            nextAction = new RequestUploadFromECUAction(KWP2000CommInterface, mirrorAddress, mMirrorCheckSize, TransferDataAction.CompressionType.Uncompressed, TransferDataAction.EncryptionType.Unencrypted);
                            break;
                        }
                        case FlashingState.MirrorCheckTransferMirror:
                        {
                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.UploadFromECU, TransferDataAction.EncryptionType.Unencrypted, TransferDataAction.CompressionType.Uncompressed, mMaxBlockSize, mMirrorCheckMirrorData, null);
                            break;
                        }
                        case FlashingState.MirrorCheckExitMirror:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case FlashingState.CheckIfFirstBlockAccidentallyErased:
                        {
                            CommInterface.DisplayStatusMessage("Calculating checksum for first memory range to determine which erase mode the ECU is using.", StatusMessageType.USER);

                            var firstBlock = mFlashBlockList.First();

                            byte[] blankData = new byte[firstBlock.mMemoryImage.Size];
                            for (uint x = 0; x < blankData.LongLength; x++)
                            {
                                blankData[x] = 0xFF;
                            }

                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, firstBlock.mMemoryImage.StartAddress, blankData);
                            break;
                        }
                        case FlashingState.CheckIfFlashRequired:
                        {
                            uint endAddress = mCurrentBlock.mMemoryImage.StartAddress + (uint)mCurrentBlock.mMemoryImage.RawData.Length - 1;
                            CommInterface.DisplayStatusMessage("Calculating flash checksum to determine if flashing is necessary for range: 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);
                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, mCurrentBlock.mMemoryImage.StartAddress, mCurrentBlock.mMemoryImage.RawData);
                            break;
                        }
                        case FlashingState.EraseFlash:
                        {
                            if (mEraseEntireFlashAtOnce)
                            {
                                nextAction = new EraseFlashAction(KWP2000CommInterface, "NEFMTO");//erase the entire flash
                            }
                            else
                            {
                                nextAction = new EraseFlashAction(KWP2000CommInterface, mCurrentBlock.mMemoryImage.StartAddress, (uint)mCurrentBlock.mMemoryImage.RawData.Length, "NEFMTO");//erase a range of sectors of the flash
                            }
                            break;
                        }
                        case FlashingState.VerifyErase:
                        {
                            var blankData = new byte[mCurrentBlock.mMemoryImage.Size];

                            for (int x = 0; x < blankData.Length; x++)
                            {
                                blankData[x] = 0xFF;
                            }

                            uint endAddress = mCurrentBlock.mMemoryImage.StartAddress + (uint)mCurrentBlock.mMemoryImage.RawData.Length - 1;
                            CommInterface.DisplayStatusMessage("Calculating flash checksum to verify if erase was successful for range: 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);
                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, mCurrentBlock.mMemoryImage.StartAddress, blankData);
                            break;
                        }
                        case FlashingState.RequestDownload:
                        {
                            nextAction = new RequestDownloadToECUAction(KWP2000CommInterface, mCurrentBlock.mMemoryImage.StartAddress, (uint)mCurrentBlock.mMemoryImage.RawData.Length, mCompressionType, mEncryptionType);
                            break;
                        }
                        case FlashingState.TransferDataToFailTransfer:
                        {
                            uint blankLength = mTotalBytesToFlash;
                            blankLength += blankLength / 16;//send more than max data to ensure the transfer is considered invalid

                            byte[] blankData = new byte[blankLength];

                            for (var x = 0; x < blankData.Length; x++)
                            {
                                blankData[x] = 0xFF;//send all 0xFF, since that won't be able to be written over non-blank memory and will fail
                            }

                            nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.DownloadToECU, mEncryptionType, mCompressionType, DEFAULT_MAX_BLOCK_SIZE, blankData, null);
                            break;
                        }
                        case FlashingState.TransferData:
                        {
                            if (mTransferDataActionToResume != null)
                            {
                                mTransferDataActionToResume.ShouldResumeTransfer = true;
                                nextAction = mTransferDataActionToResume;
                                mTransferDataActionToResume = null;
                            }
                            else
                            {
                                nextAction = new TransferDataAction(KWP2000CommInterface, TransferDataAction.TransferMode.DownloadToECU, mEncryptionType, mCompressionType, mMaxBlockSize, mCurrentBlock.mMemoryImage.RawData, this.NotifyBytesWrittenHandler);
                            }
                            break;
                        }
                        case FlashingState.ExitTransfer:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case FlashingState.ExitPreviousFailedTransfer:
                        {
                            nextAction = new RequestTransferExitAction(KWP2000CommInterface);
                            break;
                        }
                        case FlashingState.ValidateFlashedData:
                        {
                            uint endAddress = mCurrentBlock.mMemoryImage.StartAddress + (uint)mCurrentBlock.mMemoryImage.RawData.Length - 1;
                            CommInterface.DisplayStatusMessage("Calculating flash checksum to determine if flashing was successful for range: 0x" + mCurrentBlock.mMemoryImage.StartAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8"), StatusMessageType.USER);
                            nextAction = new ValidateFlashChecksumAction(KWP2000CommInterface, mCurrentBlock.mMemoryImage.StartAddress, mCurrentBlock.mMemoryImage.RawData);
                            break;
                        }
                        case FlashingState.ClearFlag:
                        {
                            nextAction = mClearFlag.NextAction();
                            break;
                        }
                        case FlashingState.FinishedBlock:
                        {
                            OperationCompleted(true);//complete before disconnecting
                            if (mFlashBlockList.Any(block => block.mWasErased))
                            {
                                CommInterface.DisplayStatusMessage("A sector was erased. Disconnecting from ECU.", StatusMessageType.USER);
                                KWP2000CommInterface.DisconnectFromECU();
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("No sector was erased. Leaving the connection open.", StatusMessageType.USER);
                            }

                            //we are finished
                            nextAction = null;
                            break;
                        }
                        default:
                        {
                            Debug.Fail("Unknown flashing state");
                            break;
                        }
                    }
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            //only pay attention to actions started by this code
            if(action == mMyLastStartedAction)
            {
                if (mState == FlashingState.ClearFlag)
                {
                    if (mClearFlag.OnActionCompleted(action, success))
                    {
                        mState = FlashingState.StartBlock;
                    }

                    success = true;
                }
                #region ValidateChecksumAndCheckEraseMode
                else if (action is ValidateFlashChecksumAction)
                {
                    if (mState == FlashingState.CheckIfFirstBlockAccidentallyErased)
                    {
                        if (success)
                        {
                            if (((ValidateFlashChecksumAction)action).IsFlashChecksumCorrect)
                            {
                                //matched checksum for blank data, erase mode is entire flash at once
                                CommInterface.DisplayStatusMessage("ECU appears to be in erase entire flash mode, restarting the flash process.", StatusMessageType.USER);
                                RestartFlashingProcessAndEraseEntireFlashAtOnce();
                            }
                            else
                            {
                                //didn't match checksum for blank data, erase mode is sector at a time
                                CommInterface.DisplayStatusMessage("ECU appears to be in erase sector mode, continuing the flash process.", StatusMessageType.USER);
                                mState = FlashingState.StartBlock;
                            }
                        }
                        else if (action.CompletedWithoutCommunicationError)
                        {
                            //if we are in entire flash mode, and try to flash sectors, we fail.
                            //if we are in sector mode, restarting in entire flash mode guarantees success.

                            CommInterface.DisplayStatusMessage("Failed to determine what erase mode ECU is in, assuming erase entire flash mode and restarting the flash process.", StatusMessageType.USER);
                            RestartFlashingProcessAndEraseEntireFlashAtOnce();
                            success = true;
                        }

                        mValidatedEraseMode = true;
                        Debug.Assert(mState != FlashingState.CheckIfFirstBlockAccidentallyErased);
                    }
                    else if (mState == FlashingState.CheckIfFlashRequired)
                    {
                        ElapsedTimeCheckingIfSectorsRequireFlashing += action.ActionElapsedTime;

                        //goto programming state unless something else happens
                        mState = FlashingState.StartProgrammingBlock;

                        if (success)
                        {
                            bool checksumCorrect = ((ValidateFlashChecksumAction)action).IsFlashChecksumCorrect;
#if FORCE_BOOT_CLUSTER_CHECKSUM_MISMATCH
                            int sectorIndex = mFlashBlockList.IndexOf(mCurrentBlock);
                            if (checksumCorrect && (mFlashMemoryLayout != null)
                                && mFlashMemoryLayout.IsBootClusterSector(sectorIndex))
                            {
                                CommInterface.DisplayStatusMessage("FORCE_BOOT_CLUSTER_CHECKSUM_MISMATCH: treating boot-cluster sector as a checksum miss.", StatusMessageType.USER);
                                checksumCorrect = false;
                            }
#endif
                            if (checksumCorrect)
                            {
                                mCurrentBlock.mFlashingIsRequired = false;

                                if (OnlyFlashRequiredSectors && !mCurrentBlock.mFlashingIsRequired)
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum matches new data, flashing is unnecessary, skipping.", StatusMessageType.USER);

                                    mCurrentBlock.mFlashComplete = true;
                                    mCurrentBlock.mFlashSuccessful = true;
                                    mState = FlashingState.FinishedBlock;
                                }
                                else
                                {
                                    CommInterface.DisplayStatusMessage("Flash checksum matches new data, flashing is unnecessary, but flashing anyway.", StatusMessageType.USER);
                                }
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Flash checksum does not match new data, flashing is necessary.", StatusMessageType.USER);
                            }
                        }
                        else if(action.CompletedWithoutCommunicationError)
                        {
                            CommInterface.DisplayStatusMessage("Failed to check if existing flash checksum matches new data, flashing anyways.", StatusMessageType.USER);
                            success = true;
                        }
                    }
                    else if (mState == FlashingState.ValidateFlashedData)
                    {
                        ElapsedTimeVerifyingWrittenSectors += action.ActionElapsedTime;

                        if (!mCurrentBlock.mFlashingIsRequired)
                        {
                            ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                        }

                        mState = FlashingState.FinishedBlock;

                        if (success)
                        {
                            if (((ValidateFlashChecksumAction)action).IsFlashChecksumCorrect)
                            {
                                CommInterface.DisplayStatusMessage("Flash checksum matches new data, flashing was successful.", StatusMessageType.USER);
                                mCurrentBlock.mFlashComplete = true;
                                //request transfer exit sets the mFlashSuccessful to true
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Flash checksum does not match new data.", StatusMessageType.USER);
                                mCurrentBlock.mFlashSuccessful = false;//need to set back to false, because request transfer exit sets it to true
                                //will retry if we haven't reached the limit of flashing attempts
                            }
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to check if flash checksum matches new data, assuming flashing was successful.", StatusMessageType.USER);
                            mCurrentBlock.mFlashComplete = true;
                            //request transfer exit sets the mFlashSuccessful to true
                            success = true;
                        }
                    }
                    else//FlashingState.VerifyErase
                    {
                        Debug.Assert(mState == FlashingState.VerifyErase);

                        if (!mCurrentBlock.mFlashingIsRequired)
                        {
                            ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                        }

                        if (success)
                        {
                            if (((ValidateFlashChecksumAction)action).IsFlashChecksumCorrect)
                            {
                                CommInterface.DisplayStatusMessage("Verified the memory sector was erased properly.", StatusMessageType.USER);
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("The memory sector was NOT erased properly, will continue and attempt to write memory sector.", StatusMessageType.USER);
                            }
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to verify if the memory sector was erased properly, assuming erase was successful.", StatusMessageType.USER);
                            success = true;
                        }

                        mCurrentBlock.mWasErased = true;
                        mState = FlashingState.RequestDownload;
                    }
                }
                #endregion
                #region EraseFlash
                else if (action is EraseFlashAction)
                {
                    mCurrentBlock.mNumBytesFlashed = 0;

                    if (!mCurrentBlock.mFlashingIsRequired)
                    {
                        ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                    }

                    if (success)
                    {
                        mCurrentBlock.mWasErased = true;
                        mState = FlashingState.RequestDownload;
                    }
                    else if (action.CompletedWithoutCommunicationError)
                    {
                        if (!mEraseEntireFlashAtOnce)
                        {
                            int sectorIndex = mFlashBlockList.IndexOf(mCurrentBlock);
                            MemoryLayout selectedLayout = mFlashMemoryLayout;

                            if ((selectedLayout != null) && selectedLayout.IsBootClusterSector(sectorIndex))
                            {
                                uint startAddress = mCurrentBlock.mMemoryImage.StartAddress;
                                uint sectorSize = mCurrentBlock.mMemoryImage.Size;
                                uint endAddress = startAddress + sectorSize - 1;
                                bool isTopBoot = (selectedLayout.BootOrientation == FlashBootOrientation.TopBoot);
                                string selectedName = !String.IsNullOrEmpty(selectedLayout.BootLabel)
                                    ? selectedLayout.BootLabel
                                    : (isTopBoot ? "top-boot (BT)" : "bottom-boot (BB)");
                                string suspectedName = !String.IsNullOrEmpty(selectedLayout.OppositeBootLabel)
                                    ? selectedLayout.OppositeBootLabel
                                    : (isTopBoot ? "bottom-boot (BB)" : "top-boot (BT)");
                                string oppositeName = !String.IsNullOrEmpty(selectedLayout.OppositeLayout)
                                    ? selectedLayout.OppositeLayout
                                    : "the opposite boot layout";

                                CommInterface.DisplayStatusMessage("Flash write failed: selected "
                                    + selectedName + " layout, chip looks " + suspectedName + ".",
                                    StatusMessageType.USER);

                                DeferWritePrompt(
                                    action,
                                    "Wrong Flash Layout",
                                    "Erase failed at 0x" + startAddress.ToString("X8") + "-0x" + endAddress.ToString("X8")
                                    + ". Selected layout is " + selectedName + "; this chip looks " + suspectedName + "."
                                    + " Re-flash with " + oppositeName + ".",
                                    UserPromptType.OK,
                                    result => false);
                                return;
                            }
                            else if (((EraseFlashAction)action).FailedBecauseOfPersistentData)
                            {
                                DeferWritePrompt(
                                    action,
                                    "Sector Erase Failed",
                                    "Failed to erase the memory sector. The new data being flashed likely conflicts with the ECU's persistent data. The persistent ECU data can be replaced by erasing the entire flash memory before programming. If the ECU may contains a non-standard flash memory chip, it could be causing the erase to fail."
                                    + "\n\nPress Yes, to erase the entire ECU flash memory and restart the flashing process."
                                    + "\nPress No, to skip flashing this sector and continue the flashing process."
                                    + "\nPress Cancel, to abort the flashing process."
                                    + "\n\nWARNING: Only erase the entire flash memory if you have a good communication connection with the ECU. If flashing fails after erasing the entire flash memory, it is likely the ECU will no longer boot, and will have to be reflashed on the bench using boot mode."
                                    + " This software does NOT support boot mode.",
                                    UserPromptType.YES_NO_CANCEL,
                                    ApplyEraseFailurePromptResult);
                                return;
                            }
                            else//regular failure
                            {
                                CommInterface.DisplayStatusMessage("Sector erase reported as failed. Calculating checksum to verify erase.", StatusMessageType.USER);

                                mState = FlashingState.VerifyErase;
                                success = true;
                            }
                        }
                    }
                }
                #endregion
                #region RequestDownload
                else if (action is RequestDownloadToECUAction)
                {
                    if (!mCurrentBlock.mFlashingIsRequired)
                    {
                        ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                    }

                    if (success)
                    {
                        mMaxBlockSize = ((RequestDownloadToECUAction)action).GetMaxBlockSize();
                        mState = FlashingState.TransferData;
                    }
                    else if (action.CompletedWithoutCommunicationError)
                    {
                        if (mState == FlashingState.RequestDownload)
                        {
                            if (((RequestDownloadToECUAction)action).WasFailureCausedByPreviousIncompleteDownload)
                            {
                                CommInterface.DisplayStatusMessage("A previous flash programming attempt was not completed. Transfering invalid data to force the previous incomplete operation to fail.", StatusMessageType.USER);

                                WasFailureCausedByPreviousIncompleteDownload = true;
                                mState = FlashingState.TransferDataToFailTransfer;
                                success = true;
                            }
                            //else
                            //{
                            //    //don't think we can do anything besides fail
                            //}
                        }
                        //else
                        //{
                        //    //don't think we can do anything besides fail
                        //}
                    }
                }
                #endregion
                #region RequestUpload (mirror check)
                else if (action is RequestUploadFromECUAction)
                {
                    if (success)
                    {
                        mMaxBlockSize = ((RequestUploadFromECUAction)action).GetMaxBlockSize();

                        if (mState == FlashingState.MirrorCheckRequestPrimary)
                        {
                            mState = FlashingState.MirrorCheckTransferPrimary;
                        }
                        else if (mState == FlashingState.MirrorCheckRequestMirror)
                        {
                            mState = FlashingState.MirrorCheckTransferMirror;
                        }
                        else
                        {
                            Debug.Fail("Unexpected RequestUpload during flash write");
                            success = false;
                        }
                    }
                }
                #endregion
                #region TransferData
                else if (action is TransferDataAction)
                {
                    if ((mState == FlashingState.MirrorCheckTransferPrimary) || (mState == FlashingState.MirrorCheckTransferMirror))
                    {
                        if (success)
                        {
                            if (mState == FlashingState.MirrorCheckTransferPrimary)
                            {
                                mState = FlashingState.MirrorCheckExitPrimary;
                            }
                            else
                            {
                                mState = FlashingState.MirrorCheckExitMirror;
                            }
                        }
                    }
                    else if (mState == FlashingState.TransferDataToFailTransfer)
                    {
                        if (action.CompletedWithoutCommunicationError)
                        {
                            mState = FlashingState.ExitPreviousFailedTransfer;
                            success = true;
                        }
                    }
                    else //handle regular transfer data completing
                    {
                        Debug.Assert(mState == FlashingState.TransferData);

                        if (!mCurrentBlock.mFlashingIsRequired)
                        {
                            ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                        }

                        if (action.CompletedWithoutCommunicationError)
                        {
                            mState = FlashingState.ExitTransfer;
                            success = true;
                        }
                        else
                        {
                            mTransferDataActionToResume = (action as TransferDataAction);
                        }
                    }
                }
                #endregion
                #region RequestTransferExit
                else if (action is RequestTransferExitAction)
                {
                    if ((mState == FlashingState.MirrorCheckExitPrimary) || (mState == FlashingState.MirrorCheckExitMirror))
                    {
                        if (success)
                        {
                            if (mState == FlashingState.MirrorCheckExitPrimary)
                            {
                                mState = FlashingState.MirrorCheckRequestMirror;
                            }
                            else if (MirroredFlashLayoutHelper.SamplesMatch(mMirrorCheckPrimaryData, mMirrorCheckMirrorData))
                            {
                                CommInterface.DisplayStatusMessage(MirroredFlashLayoutHelper.PassedStatus(mMirrorCheckOffset), StatusMessageType.USER);
                                mState = FlashingState.StartBlock;
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage(MirroredFlashLayoutHelper.FailedStatus, StatusMessageType.USER);
                                AskWritePrompt(action, MirroredFlashLayoutHelper.ContinueTitle, MirroredFlashLayoutHelper.ContinueBody, UserPromptType.OK_CANCEL, true);
                                return;
                            }
                        }
                    }
                    else if (mState == FlashingState.ExitPreviousFailedTransfer)
                    {
                        if (action.CompletedWithoutCommunicationError)
                        {
                            if (WasFailureCausedByPreviousIncompleteDownload)
                            {
                                CommInterface.DisplayStatusMessage("Finished causing previous flash programming attempt to fail. Retrying flash programming.", StatusMessageType.USER);

                                WasFailureCausedByPreviousIncompleteDownload = false;

                                //start flashing from the first incomplete block
                                mState = FlashingState.StartBlock;
                                success = true;
                            }
                            else
                            {
                                DeferWritePrompt(
                                    action,
                                    "Flash Programming Sector Failed",
                                    "Flash programming failed on the current sector. Press OK to retry programming the current sector, or Cancel to abort flash programming.",
                                    UserPromptType.OK_CANCEL,
                                    result =>
                                    {
                                        if (result == UserPromptResult.OK)
                                        {
                                            //start flashing from the current block
                                            mState = FlashingState.StartProgrammingBlock;
                                        }
                                        else
                                        {
                                            OperationCompleted(false);
                                        }

                                        return true;
                                    });
                                return;
                            }
                        }
                        //else
                        //{
                        //    //don't think I can clean up anything here
                        //}
                    }
                    else//handle regular request transfer exit completing
                    {
                        Debug.Assert(mState == FlashingState.ExitTransfer);

                        if (!mCurrentBlock.mFlashingIsRequired)
                        {
                            ElapsedTimeFlashingUnrequiredSectors += action.ActionElapsedTime;
                        }

                        if (success)
                        {
                            //at this point begin assuming that the sector was flashed correctly
                            mCurrentBlock.mFlashSuccessful = true;

                            if (ShouldVerifyFlashedSectors)
                            {
                                mState = FlashingState.ValidateFlashedData;
                            }
                            else
                            {
                                mState = FlashingState.FinishedBlock;

                                mCurrentBlock.mFlashComplete = true;
                            }
                        }
                        else if(action.CompletedWithoutCommunicationError)
                        {
                            CommInterface.DisplayStatusMessage("Failed to properly exit flash transfer. Transfering invalid data to force flashing to fail.", StatusMessageType.USER);

                            mState = FlashingState.TransferDataToFailTransfer;

                            success = true;
                        }
                        //else
                        //{
                        //    //don't think I can clean up anything here
                        //}
                    }
                }
                #endregion
                #region ValidateStartAndEndAddresses
                else if (action is ValidateStartAndEndAddressesWithRequestUploadDownloadAction)
                {
                    bool validationCompleted = true;
                    bool layoutIsValid = false;
                    bool possiblyMirrored = false;

                    string validationMesage = null;

                    var validationResult = ((ValidateStartAndEndAddressesWithRequestUploadDownloadAction)action).ValidationResult;
                    LayoutValidationText.Apply(validationResult, success, action.CompletedWithoutCommunicationError, out validationCompleted, out layoutIsValid, out possiblyMirrored, out validationMesage);

                    if (validationMesage != null)
                    {
                        CommInterface.DisplayStatusMessage(validationMesage, StatusMessageType.USER);
                    }

                    success = validationCompleted && layoutIsValid;

                    if (possiblyMirrored)
                    {
                        PostUserPrompt(
                            "Memory layout may be ambiguous",
                            MirroredFlashLayoutHelper.PromptMessage(true),
                            UserPromptType.CONTINUE_CHECK_CANCEL,
                            result => ApplyWriteMirrorLayoutPrompt(action, result));
                        return;
                    }

                    string promptTitle;
                    string promptBody;
                    UserPromptType promptType;
                    bool continueOnOk;
                    if (!success && LayoutValidationText.TryFailurePrompt(validationResult, validationCompleted, layoutIsValid, true, out promptTitle, out promptBody, out promptType, out continueOnOk))
                    {
                        AskWritePrompt(action, promptTitle, promptBody, promptType, continueOnOk);
                        return;
                    }
                    else if (success)
                    {
                        mState = FlashingState.StartBlock;
                    }
                }
                #endregion
                ApplyFinishedWriteBlock();
            }

            FinishWriteAction(action, success);
        }

        private void ApplyFinishedWriteBlock()
        {
            if (mState != FlashingState.FinishedBlock)
            {
                return;
            }

            //erase flash and validate flashed data checksums are the only two actions that will allow for a retry
            if (!mCurrentBlock.mFlashComplete)
            {
                int MAX_FLASH_BLOCK_RETRIES = 3;

                if (mCurrentBlock.mNumFlashAttempts <= MAX_FLASH_BLOCK_RETRIES)
                {
                    CommInterface.DisplayStatusMessage("Previous flash attempt failed, will retry.", StatusMessageType.USER);
                }
                else
                {
                    CommInterface.DisplayStatusMessage("Previous flashing attempts have failed " + MAX_FLASH_BLOCK_RETRIES + " times, giving up and skipping.", StatusMessageType.USER);
                    mCurrentBlock.mFlashComplete = true;
                }
            }

            //did we finish the current block?
            if (mCurrentBlock.mFlashComplete)
            {
                //update the stats
                mTotalBytesValidated += mCurrentBlock.mMemoryImage.Size;
                OnUpdatePercentComplete(((float)mTotalBytesValidated) / ((float)mTotalBytesToFlash) * 100.0f);

                if (!mEraseEntireFlashAtOnce && !mValidatedEraseMode && mCurrentBlock.mWasErased && (mCurrentBlock != mFlashBlockList.First()))
                {
                    mState = FlashingState.CheckIfFirstBlockAccidentallyErased;
                }
            }
        }

        private void ApplyWriteMirrorLayoutPrompt(CommunicationAction action, UserPromptResult promptResult)
        {
            bool success = false;

            if (promptResult == UserPromptResult.OK)
            {
                success = true;
                mState = FlashingState.StartBlock;
            }
            else if (promptResult == UserPromptResult.CHECK)
            {
                MirroredFlashLayoutHelper.ChooseCompareSample(
                    mFlashBlockList.First().mMemoryImage.StartAddress,
                    mFlashBlockList.Last().mMemoryImage.EndAddress,
                    out mMirrorCheckOffset,
                    out mMirrorCheckSize);
                mMirrorCheckPrimaryData = new byte[mMirrorCheckSize];
                mMirrorCheckMirrorData = new byte[mMirrorCheckSize];
                success = true;
                mState = FlashingState.MirrorCheckRequestPrimary;
            }

            FinishWriteAfterPrompt(action, success);
        }

        private void AskWritePrompt(CommunicationAction action, string title, string message, UserPromptType promptType, bool continueOnOk)
        {
            DeferWritePrompt(action, title, message, promptType, result =>
            {
                if (continueOnOk && (result == UserPromptResult.OK))
                {
                    mState = FlashingState.StartBlock;
                    return true;
                }

                return false;
            });
        }

        private void DeferWritePrompt(CommunicationAction action, string title, string message, UserPromptType promptType, Func<UserPromptResult, bool> apply)
        {
            PostUserPrompt(title, message, promptType, result => FinishWriteAfterPrompt(action, apply(result)));
        }

        private void FinishWriteAfterPrompt(CommunicationAction action, bool success)
        {
            ApplyFinishedWriteBlock();
            FinishWriteAction(action, success);
        }

        private void FinishWriteAction(CommunicationAction action, bool success)
        {
            if (!success && !action.CompletedWithoutCommunicationError)
            {
                BeginCommunicationErrorPrompt(continuing =>
                {
                    if (continuing)
                    {
                        KWP2000CommInterface.DisconnectFromECU();
                        NoteWaitingToReconnect();
                    }

                    FinishWriteTail(action, success);
                });
                return;
            }

            FinishWriteTail(action, success);
        }

        private void FinishWriteTail(CommunicationAction action, bool success)
        {
            mMyLastStartedAction = null;

            if (!mWaitingToReconnect)
            {
                base.OnActionCompleted(action, success);
            }
        }

        bool mWaitingToReconnect = false;
        bool mUserPromptedToContinueAfterCommunicationFailure = false;

        private void NoteWaitingToReconnect()
        {
            CommInterface.DisplayStatusMessage("Waiting for user to reconnect to ECU before continuing...", StatusMessageType.USER);
        }

        private void BeginCommunicationErrorPrompt(Action<bool> after)
        {
            if (mUserPromptedToContinueAfterCommunicationFailure || UserPromptIsPending)
            {
                after(mWaitingToReconnect);
                return;
            }

            mUserPromptedToContinueAfterCommunicationFailure = true;
            PostUserPrompt(
                "Communication Error",
                "A communication error was encountered while flashing. Press Cancel to abort, or OK to continue after reconnecting.",
                UserPromptType.OK_CANCEL,
                result =>
                {
                    if (result == UserPromptResult.OK)
                    {
                        mWaitingToReconnect = true;

                        if (mTransferDataActionToResume != null)
                        {
                            mState = FlashingState.TransferData;//resume the current transfer data action
                        }
                        else
                        {
                            mState = FlashingState.StartProgrammingBlock;//start flashing from the current block
                        }
                    }

                    after(mWaitingToReconnect);
                });
        }

        protected override void OnConnectionChanged(CommunicationInterface commInterface, CommunicationInterface.ConnectionStatusType status, bool willReconnect)
        {
            if(mWaitingToReconnect)
            {
                if (status == CommunicationInterface.ConnectionStatusType.Connected)
                {
                    mWaitingToReconnect = false;
                    mUserPromptedToContinueAfterCommunicationFailure = false;

                    ResetOperation();

                    bool success = StartNextAction();

                    if (CurrentAction == null)
                    {
                        if (!success)
                        {
                            BeginCommunicationErrorPrompt(continuing =>
                            {
                                if (continuing)
                                {
                                    NoteWaitingToReconnect();
                                }

                                if (!mWaitingToReconnect)
                                {
                                    OperationCompleted(success);
                                }
                            });
                            return;
                        }

                        OperationCompleted(success);
                    }
                }
            }
            else if ((status == CommunicationInterface.ConnectionStatusType.Disconnected) && !UserPromptIsPending)
            {
                BeginCommunicationErrorPrompt(continuing => FinishConnectionChange(commInterface, status, willReconnect));
                return;
            }

            FinishConnectionChange(commInterface, status, willReconnect);
        }

        private void FinishConnectionChange(CommunicationInterface commInterface, CommunicationInterface.ConnectionStatusType status, bool willReconnect)
        {
            base.OnConnectionChanged(commInterface, status, willReconnect);

            if (mWaitingToReconnect && (status == CommunicationInterface.ConnectionStatusType.Disconnected))
            {
                NoteWaitingToReconnect();
            }
        }

        protected override bool ShouldFailOnDisconnect()
        {
            return !mWaitingToReconnect;
        }

        private void NotifyBytesWrittenHandler(uint newAmountWritten, uint totalWritten, uint totalToWrite)
        {
            mCurrentBlock.mNumBytesFlashed += newAmountWritten;

            OnUpdatePercentComplete(((float)mCurrentBlock.mNumBytesFlashed + mTotalBytesValidated) / ((float)mTotalBytesToFlash) * 100.0f);
        }

        private enum FlashingState
        {
            ValidateStartAndEndAddresses,
            MirrorCheckRequestPrimary,
            MirrorCheckTransferPrimary,
            MirrorCheckExitPrimary,
            MirrorCheckRequestMirror,
            MirrorCheckTransferMirror,
            MirrorCheckExitMirror,
            StartBlock,//intermediate state
            CheckIfFirstBlockAccidentallyErased,
            ClearFlag,
            CheckIfFlashRequired,
            StartProgrammingBlock,//intermediate state
            EraseFlash,
            VerifyErase,
            RequestDownload,
            TransferDataToFailTransfer,
            TransferData,
            ExitTransfer,
            ExitPreviousFailedTransfer,
            ValidateFlashedData,
            FinishedBlock//intermediate state
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

        private CommunicationAction mMyLastStartedAction;
        private FlashingState mState;
        private List<FlashBlock> mFlashBlockList;
        private FlashBlock mCurrentBlock;
        private bool mClearFlagAttempted;
        private ProgrammingFlagCloser mClearFlag;

        private byte mMaxBlockSize;
        private bool mEraseEntireFlashAtOnce;
        private MemoryLayout mFlashMemoryLayout;
        private bool mValidatedEraseMode;

        private uint mTotalBytesToFlash;
        private uint mTotalBytesValidated;
        private TransferDataAction.CompressionType mCompressionType;
        private TransferDataAction.EncryptionType mEncryptionType;

        private uint mMirrorCheckOffset;
        private uint mMirrorCheckSize;
        private byte[] mMirrorCheckPrimaryData;
        private byte[] mMirrorCheckMirrorData;

        private TransferDataAction mTransferDataActionToResume;
    };

    public delegate void MemoryRegionsRead(IEnumerable<TaggedMemoryImage> regionsRead);

    public interface ITrackedMemoryRegionsOperation
    {
        event MemoryRegionsRead RegionsRead;

        void AddMemoryRegion(uint startAddress, uint numBytes, object userTag);
        void RemoveMemoryRegion(object userTag);
        void RemoveAllMemoryRegions();

        float MaxReadsPerSecond { get; set; }
        byte MaxVariableReadsPerTick { get; set; }
        byte MaxNumBytesPerRead { get; set; }
    }

    public class SynchronizeRAMRegionsOperation : KWP2000Operation, ITrackedMemoryRegionsOperation
    {
        private class SynchronizedMemoryRegion : TaggedMemoryImage
        {
            public uint ReferenceCount
            {
                get;
                set;
            }

            public SynchronizedMemoryRegion(uint numBytes, uint startAddress, object userTag)
                : base(numBytes, startAddress, userTag)
            {
                ReferenceCount = 1;
            }
        }

        public SynchronizeRAMRegionsOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, bool readRegionsInBlocks)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mRegionsPendingRead = new List<SynchronizedMemoryRegion>();
            mNewlyReadRegions = new List<SynchronizedMemoryRegion>();

            mReadRegionsInBlocks = readRegionsInBlocks;
            mMode = SynchronizationMode.READ;

            mLastReadMemoryImage = null;

            mReadAction = new ReadMemoryAction(commInterface, 0, 0, DEFAULT_MAX_BLOCK_SIZE, null);

            mLastReadStartTime = DateTime.Now;
            MaxReadsPerSecond = 20;

            //254 byte fixed size at timing limits and 128400 baud caused the engine to lock up at 4000rpm
            //128 byte region size at timing limtis and 128400 baud caused the engine to lock up at 4000rpm
            //64 byte region size at timing limtis and 128400 baud causes no lock ups
            //engine resets at high rpm appear to be caused by transmitting a large amount of data at a high baud rate
            MaxNumBytesPerRead = 64;
        }

        public float MaxReadsPerSecond { get; set; }
        public byte MaxVariableReadsPerTick { get; set; }//not used
        public byte MaxNumBytesPerRead { get; set; }

        public event MemoryRegionsRead RegionsRead;

        public void AddMemoryRegion(uint startAddress, uint numBytes, object userTag)
        {
            var existingRegion = mSynchronizedRegions.Find(region => ((region.StartAddress == startAddress) && (region.Size == numBytes) && region.UserTag.Equals(userTag)));

            if (existingRegion != null)
            {
                existingRegion.ReferenceCount++;
            }
            else
            {
                mSynchronizedRegions.Add(new SynchronizedMemoryRegion(numBytes, startAddress, userTag));
                mRegionIterator = null;
                mAreRegionsSorted = false;
            }
        }

        public void RemoveMemoryRegion(object userTag)
        {
            bool removedRegion = false;

            var existingRegions = mSynchronizedRegions.FindAll(region => region.UserTag.Equals(userTag));

            foreach (var region in existingRegions)
            {
                Debug.Assert(region.ReferenceCount > 0);
                region.ReferenceCount--;

                if (region.ReferenceCount <= 0)
                {
                    mSynchronizedRegions.Remove(region);
                    removedRegion = true;
                }
            }

            if (removedRegion)
            {
                mRegionIterator = mSynchronizedRegions.GetEnumerator();
            }
        }

        public void RemoveAllMemoryRegions()
        {
            mSynchronizedRegions.Clear();
            mRegionIterator = mSynchronizedRegions.GetEnumerator();
            mAreRegionsSorted = true;
        }

        private void SortRegions()
        {
            if (!mAreRegionsSorted)
            {
                mSynchronizedRegions.Sort(delegate(SynchronizedMemoryRegion x, SynchronizedMemoryRegion y)
                {
                    if (x.StartAddress < y.StartAddress)
                    {
                        return -1;
                    }

                    if (x.StartAddress > y.StartAddress)
                    {
                        return 1;
                    }

                    return 0;
                });

                mRegionIterator = mSynchronizedRegions.GetEnumerator();

                mAreRegionsSorted = true;
            }
        }

        private List<SynchronizedMemoryRegion> mSynchronizedRegions = new List<SynchronizedMemoryRegion>();
        private IEnumerator<SynchronizedMemoryRegion> mRegionIterator;
        private bool mAreRegionsSorted = false;

        protected override void ResetOperation()
        {
            mMode = SynchronizationMode.READ;

            mRegionIterator = mSynchronizedRegions.GetEnumerator();
            mCurrentRegion = null;

            base.ResetOperation();
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
                if (mMode == SynchronizationMode.READ)
                {
                    {
                        var minTimeBetweenReads = TimeSpan.FromMilliseconds(1000.0f / MaxReadsPerSecond);
                        var nextReadTime = mLastReadStartTime + minTimeBetweenReads;
                        var timeUntilNextRead = nextReadTime - currentTime;

                        if (timeUntilNextRead.TotalMilliseconds > 0)
                        {
                            Thread.Sleep(timeUntilNextRead);
                            while (DateTime.Now < nextReadTime) ;
                        }

                        mLastReadStartTime = nextReadTime;
                    }

                    //clear the previous pending regions
                    mRegionsPendingRead.Clear();

                    uint numBytesToRead = MaxNumBytesPerRead;
                    uint addressToRead = 0;

                    if (mCurrentRegion == null)
                    {
                        MoveToNextRegion();
                    }

                    if (mCurrentRegion != null)
                    {
                        addressToRead = mCurrentRegion.StartAddress;

                        uint maxAddressToRead = addressToRead + MaxNumBytesPerRead;
                        uint lastRegionAddressRead = mCurrentRegion.StartAddress + mCurrentRegion.Size;

                        //determine how many regions we can read in one block
                        do
                        {
                            mRegionsPendingRead.Add(mCurrentRegion);
                            lastRegionAddressRead = mCurrentRegion.StartAddress + mCurrentRegion.Size;

                            MoveToNextRegion();

                            //keep going through regions until we loop, or go past the end of the block
                        } while (mReadRegionsInBlocks
                            && (mCurrentRegion != null)
                            && (mCurrentRegion.StartAddress < maxAddressToRead)
                            && (mCurrentRegion.StartAddress + mCurrentRegion.Size <= maxAddressToRead)
                            && (mCurrentRegion.StartAddress >= addressToRead));

                        numBytesToRead = (byte)(lastRegionAddressRead - addressToRead);
                    }

                    Debug.Assert(numBytesToRead > 0);
                    Debug.Assert(numBytesToRead <= MaxNumBytesPerRead);

                    mReadAction.SetStartAddressAndNumBytes(addressToRead, numBytesToRead);
                    nextAction = mReadAction;

                    CommInterface.DisplayStatusMessage("Reading address 0x" + addressToRead.ToString("X") + " with 0x" + numBytesToRead.ToString("X") + " bytes.", StatusMessageType.TRACE);
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
                //done after the ActionCompletedHandler so the next message is sent before we process all the region updates.
                if ((mNewlyReadRegions.Count > 0) && (mLastReadMemoryImage != null))
                {
                    var regionsReadCopy = RegionsRead;

                    if (regionsReadCopy != null)
                    {
                        //update newly read regions
                        foreach (var curRegion in mNewlyReadRegions)
                        {
                            Buffer.BlockCopy(mLastReadMemoryImage.RawData, (int)(curRegion.StartAddress - mLastReadMemoryImage.StartAddress), curRegion.RawData, 0, (int)curRegion.Size);
                        }

                        regionsReadCopy(mNewlyReadRegions.Cast<TaggedMemoryImage>());
                    }

                    mNewlyReadRegions.Clear();
                    mLastReadMemoryImage = null;
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
                if (action is ReadMemoryAction)
                {
                    var readAction = action as ReadMemoryAction;
                    Debug.Assert(readAction != null);

                    if (action.CompletedWithoutCommunicationError)
                    {
                        if (success)
                        {
                            Debug.Assert(readAction.ReadData != null);
                            Debug.Assert(readAction.ReadData.Length > 0);

                            CommInterface.DisplayStatusMessage("Read address 0x" + readAction.mStartAddress.ToString("X") + " with 0x" + readAction.ReadData.Length.ToString("X") + " bytes.", StatusMessageType.TRACE);

                            //swap the pending read and newly read lists
                            List<SynchronizedMemoryRegion> temp = mNewlyReadRegions;
                            mNewlyReadRegions = mRegionsPendingRead;
                            mRegionsPendingRead = temp;
                            mRegionsPendingRead.Clear();

                            //update the ram memory image state with the received data
                            mLastReadMemoryImage = new MemoryImage((uint)readAction.ReadData.Length, readAction.mStartAddress);
                            Buffer.BlockCopy(readAction.ReadData, 0, mLastReadMemoryImage.RawData, 0, readAction.ReadData.Length);
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to read address 0x" + readAction.mStartAddress.ToString("X") + " with 0x" + readAction.ReadData.Length.ToString("X") + " bytes.", StatusMessageType.LOG);

                            mNewlyReadRegions.Clear();
                            mRegionsPendingRead.Clear();
                            mLastReadMemoryImage = null;

                            if (readAction.FailureResponseCode != (byte)KWP2000ResponseCode.ServiceNotSupported)
                            {
                                success = true;//keep running happily unless there is a communication error
                            }
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

        private bool MoveToNextRegion()
        {
            SortRegions();

            mCurrentRegion = null;

            if (mRegionIterator.MoveNext())
            {
                mCurrentRegion = mRegionIterator.Current;
            }
            else if(mSynchronizedRegions.Any())
            {
                mRegionIterator = mSynchronizedRegions.GetEnumerator();

                if (mRegionIterator.MoveNext())
                {
                    mCurrentRegion = mRegionIterator.Current;
                }
            }

            return (mCurrentRegion != null);
        }

        private SynchronizedMemoryRegion mCurrentRegion;
        private List<SynchronizedMemoryRegion> mRegionsPendingRead;
        private List<SynchronizedMemoryRegion> mNewlyReadRegions;

        private ReadMemoryAction mReadAction;
        private SynchronizationMode mMode;
        private bool mReadRegionsInBlocks;

        private MemoryImage mLastReadMemoryImage;
        private DateTime mLastReadStartTime;

        private CommunicationAction mMyLastStartedAction;

//TODO: we can support writing of regions as long as we write them individually and not as blocks
        private enum SynchronizationMode
        {
            READ,
            FINISHED
        };
    };

    public class ReadAllECUIdentificationOptionsOperation : KWP2000Operation
    {
        public ReadAllECUIdentificationOptionsOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates, KWP2000DiagnosticSessionType sessionType)
            : base(commInterface)
        {
            mCurrentIdentOptionIndex = 0;

            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            if (sessionType != KWP2000DiagnosticSessionType.InternalUndefined)
            {
                EnableAutoStartDiagnosticSession(sessionType, baudRates);
            }
        }

        public KWP2000IdentificationInfo ECUInfo
        {
            get
            {
                if (_ECUInfo == null)
                {
                    _ECUInfo = new KWP2000IdentificationInfo();
                }

                return _ECUInfo;
            }
        }
        private KWP2000IdentificationInfo _ECUInfo;

        protected override void ResetOperation()
        {
            mState = State.ReadScalingTable;

            ECUInfo.Clear();

            base.ResetOperation();
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                switch (mState)
                {
                    case State.ReadScalingTable:
                    {
                        nextAction = new ReadECUIdentificationAction(KWP2000CommInterface, (byte)KWP2000IdentificationOption.ECUIdentificationScalingTable);
                        break;
                    }
                    case State.ReadIdentificationOptions:
                    {
                        nextAction = new ReadECUIdentificationAction(KWP2000CommInterface, mIdentOptionsInScalingTable[mCurrentIdentOptionIndex]);
                        break;
                    }
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            //ignore actions not started by this code
            if (action == mMyLastStartedAction)
            {
                if (success)
                {
                    if (action is ReadECUIdentificationAction)
                    {
                        var identAction = action as ReadECUIdentificationAction;

                        if (mState == State.ReadScalingTable)
                        {
                            ECUInfo.ScalingTable.ScalingTableData = identAction.IdentificationData;

                            mCurrentIdentOptionIndex = 0;

                            if (ECUInfo.ScalingTable.GetIdentificationOptionsInScalingTable(out mIdentOptionsInScalingTable))
                            {
                                mState = State.ReadIdentificationOptions;
                            }
                            else
                            {
                                mState = State.Finished;
                            }
                        }
                        else
                        {
                            ECUInfo.IdentOptionData.Add(identAction.IdentificationOption, identAction.IdentificationData);

                            mCurrentIdentOptionIndex++;

                            if (mCurrentIdentOptionIndex < mIdentOptionsInScalingTable.Length)
                            {
                                mState = State.ReadIdentificationOptions;
                            }
                            else
                            {
                                mState = State.Finished;
                            }
                        }

                        OnUpdatePercentComplete(((float)mCurrentIdentOptionIndex / (float)mIdentOptionsInScalingTable.Length) * 100);
                    }
                }
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);
        }

        private enum State
        {
            ReadScalingTable,
            ReadIdentificationOptions,
            Finished
        }

        private CommunicationAction mMyLastStartedAction;
        private State mState;
        private int mCurrentIdentOptionIndex;
        private byte[] mIdentOptionsInScalingTable;
    };

    public class ReadDiagnosticTroubleCodesOperation : KWP2000SequencialOperation
    {
        public ReadDiagnosticTroubleCodesOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.StandardSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mDTCAction = new ReadDiagnosticTroubleCodesByStatusAction(commInterface, 0x00, 0x0000);

            mActionArray = new KWP2000Action[1];
            mActionArray[0] = mDTCAction;
        }

        public IEnumerable<KWP2000DTCInfo> DTCsRead
        {
            get { return mDTCAction.DTCsRead; }
        }

        public int ExpectedNumDTCs
        {
            get { return mDTCAction.ExpectedNumDTCs; }
        }

        protected ReadDiagnosticTroubleCodesByStatusAction mDTCAction;
    };

    public class ClearDiagnosticInformationOperation : KWP2000SequencialOperation
    {
        public ClearDiagnosticInformationOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.StandardSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);

            mActionArray = new KWP2000Action[1];
            mActionArray[0] = new ClearDiagnosticInformationAction(commInterface, 0x0000);
        }
    };

    public class ReadAllLocalIdentifiersOperation : KWP2000Operation
    {
        public ReadAllLocalIdentifiersOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.StandardSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
        }

        protected override void ResetOperation()
        {
            mLastLocalIdentifier = 0xFF;

            base.ResetOperation();
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                mLastLocalIdentifier--;

                if (mLastLocalIdentifier > 0)
                {
                    nextAction = new ReadDataByLocalIdentifierAction(KWP2000CommInterface, mLastLocalIdentifier);
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            //ignore actions not started by this code
            if (action == mMyLastStartedAction)
            {
                success = true;
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);
        }

        private byte mLastLocalIdentifier;
        private CommunicationAction mMyLastStartedAction;
    }

    public class ReadAllCommonIdentifiersOperation : KWP2000Operation
    {
        public ReadAllCommonIdentifiersOperation(KWP2000Interface commInterface, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.StandardSession, baudRates);
            EnableAutoNegotiateTiming(NegotiateTimingParameters.NegotiationTarget.Limits);
        }

        protected override void ResetOperation()
        {
            mLastCommonIdentifier = 0xFFFF;

            base.ResetOperation();
        }

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                if (mLastCommonIdentifier >= 1)
                {
                    mLastCommonIdentifier--;

                    nextAction = new ReadDataByCommonIdentifierAction(KWP2000CommInterface, mLastCommonIdentifier);
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            //ignore actions not started by this code
            if (action == mMyLastStartedAction)
            {
                success = true;
            }

            base.OnActionCompleted(action, success);
        }

        private ushort mLastCommonIdentifier;
        private CommunicationAction mMyLastStartedAction;
    }

    public class RelocateMessageHandlingTableOperation : KWP2000Operation
    {
        public RelocateMessageHandlingTableOperation(KWP2000Interface commInterface, List<uint> baudRates)
            : base(commInterface)
        {
            EnableAutoStartDiagnosticSession(KWP2000DiagnosticSessionType.DevelopmentSession, baudRates);

            mRedirectFunctionData = Communication.Properties.Resources.KWP2000RedirectionFunction;

            ShouldRelocateMessageHandlingTable = true;
        }

        protected bool ShouldRelocateMessageHandlingTable
        {
            get
            {
                return _ShouldRelocateMessageHandlingTable;
            }
            set
            {
                if (_ShouldRelocateMessageHandlingTable != value)
                {
                    _ShouldRelocateMessageHandlingTable = value;

                    if (_ShouldRelocateMessageHandlingTable)
                    {
                        mRelocateState = (RelocateState)(Enum.GetValues(typeof(RelocateState)).GetValue(0));
                    }
                    else
                    {
                        mRelocateState = RelocateState.Finished;
                    }
                }
            }
        }
        private bool _ShouldRelocateMessageHandlingTable;

        protected override CommunicationAction NextAction()
        {
            var nextAction = base.NextAction();

            if (nextAction == null)
            {
                switch (mRelocateState)
                {
                    case RelocateState.LocateHandlerIndexAndFunctionTablePointers:
                    {
                        CommInterface.DisplayStatusMessage("Locating important KWP2000 addresses in the ECU.", StatusMessageType.USER);

                        //read 4 bytes at 0xE1B0
                        nextAction = new ReadMemoryAction(KWP2000CommInterface, 0xE1B0, 4, DEFAULT_MAX_BLOCK_SIZE, null);
                        break;
                    }
                    case RelocateState.ReadHandlerIndexAndFunctionTablePointers:
                    {
                        CommInterface.DisplayStatusMessage("Reading current KWP2000 message handling configuration in the ECU.", StatusMessageType.USER);

                        //Read 8 bytes at 0xE228
                        nextAction = new ReadMemoryAction(KWP2000CommInterface, mIndexAndFunctionTablePointerAddress, 8, DEFAULT_MAX_BLOCK_SIZE, null);
                        break;
                    }
                    case RelocateState.ReadCurrentEnabledServiceIDsTable:
                    {
                        CommInterface.DisplayStatusMessage("Reading current KWP2000 enabled message types in the ECU.", StatusMessageType.USER);

                        //Read 16 bytes at 0xE210
                        nextAction = new ReadMemoryAction(KWP2000CommInterface, mEnabledServiceIDsTableAddress, 16, DEFAULT_MAX_BLOCK_SIZE, null);
                        break;
                    }
                    case RelocateState.LocateEmptyRAM:
                    {
                        CommInterface.DisplayStatusMessage("Locating empty RAM in the ECU for new KWP2000 configuration.", StatusMessageType.USER);

                        mEmptyRAMStartAddress = 0;
                        mEmptyRAMNumBytes = 0;

                        mRequiredSizeForRelocatedFunctionData = (uint)GetRelocatedData(0).Length;//determine how much empty space we need by asking to get the relocated data based at address zero
                        mRequiredSizeForRelocatedFunctionData += 1;//add one incase empty ram starts at an odd address and we need to offset by one to start

                        //Read 0x8000 bytes at 0x380000
                        nextAction = new ReadMemoryAction(KWP2000CommInterface, 0x380000, 0x8000, DEFAULT_MAX_BLOCK_SIZE, this.LookingForEmptyRAMCallback);
                        break;
                    }
                    case RelocateState.WriteAndVerifyHandlerIndexAndFunctionTablesAndRedirectFunctionToRAM:
                    {
                        CommInterface.DisplayStatusMessage("Writing new KWP2000 message handling configuration to empty RAM.", StatusMessageType.USER);

                        uint targetAddress = mEmptyRAMStartAddress + (mEmptyRAMStartAddress % 2);//make sure the address is even
                        var relocatedData = GetRelocatedData(targetAddress);

                        if (relocatedData.Length <= (mEmptyRAMNumBytes - (targetAddress - mEmptyRAMStartAddress)))
                        {
                            CommInterface.DisplayStatusMessage("Writing new KWP2000 message handling configuration to address 0x" + mNewFunctionTableAddress.ToString("X") + " with size 0x" + relocatedData.Length.ToString("X"), StatusMessageType.DEV_USER);

                            nextAction = new WriteMemoryAction(KWP2000CommInterface, targetAddress, DEFAULT_MAX_BLOCK_SIZE, relocatedData, null);
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to write new KWP2000 message handling configuration because there is not enough empty RAM.", StatusMessageType.USER);
                            OperationCompleted(false);
                        }
                        break;
                    }
                    case RelocateState.WriteNewEnabledServiceIDsTable:
                    {
                        CommInterface.DisplayStatusMessage("Writing new KWP2000 enabled message types in the ECU.", StatusMessageType.USER);

                        nextAction = new WriteMemoryAction(KWP2000CommInterface, mEnabledServiceIDsTableAddress, DEFAULT_MAX_BLOCK_SIZE, mEnabledServiceIDs, null);
                        break;
                    }
                    case RelocateState.ChangeHandlerIndexAndFunctionTablePointers:
                    {
                        CommInterface.DisplayStatusMessage("Changing KWP2000 addresses in the ECU to use new message handling configuration.", StatusMessageType.USER);

                        //Write 8 bytes at 0xE228
                        var newPointerData = new byte[8];

                        var newFunctionTablePointerAddress = (UInt16)(mNewFunctionTableAddress & 0x3FFF);
                        var newFunctionTablePointerPage = (UInt16)((mNewFunctionTableAddress >> 14) & 0x3FF);
                        var newIndexTablePointerAddress = (UInt16)(mNewIndexTableAddress & 0x3FFF);
                        var newIndexTablePointerPage = (UInt16)((mNewIndexTableAddress >> 14) & 0x3FF);

                        BitConverter.GetBytes(newFunctionTablePointerAddress).CopyTo(newPointerData, 0);
                        BitConverter.GetBytes(newFunctionTablePointerPage).CopyTo(newPointerData, 2);
                        BitConverter.GetBytes(newIndexTablePointerAddress).CopyTo(newPointerData, 4);
                        BitConverter.GetBytes(newIndexTablePointerPage).CopyTo(newPointerData, 6);

                        nextAction = new WriteMemoryAction(KWP2000CommInterface, mIndexAndFunctionTablePointerAddress, DEFAULT_MAX_BLOCK_SIZE, newPointerData, null);
                        break;
                    }
                }

                mMyLastStartedAction = nextAction;
            }

            return nextAction;
        }

        private byte[] GetRelocatedData(uint targetAddress)
        {
            Debug.Assert(targetAddress % 2 == 0);//this function assumes an even start address

            //Write 128 bytes for index table
            var tempIndexTable = new byte[128];
            for (int x = 0; x < tempIndexTable.Length; x++)
            {
                tempIndexTable[x] = 0;//point all entries to the zero index for the redirect function
            }

            var relocatedServiceIDs = GetRelocatedServiceIDs();
            byte numRelocatedFunctions = 0;

            if (relocatedServiceIDs != null)
            {
                numRelocatedFunctions = (byte)relocatedServiceIDs.Count;

                byte functionIndex = 1;

                foreach (var serviceID in relocatedServiceIDs)
                {
                    var indexTableEntry = ((serviceID & 0x80) >> 1) | (serviceID & 0x3F);

                    tempIndexTable[indexTableEntry] = functionIndex;

                    mEnabledServiceIDs[(indexTableEntry >> 3) & 0x1F] |= (byte)(0x01 << (indexTableEntry & 0x7));

                    functionIndex++;
                }
            }

            //Write 4 x N bytes at empty RAM location
            mNewFunctionTableMaxEntries = (byte)(numRelocatedFunctions + 1);
            mNewFunctionTableNumEntries = mNewFunctionTableMaxEntries;
            var newFunctionTableData = new byte[mNewFunctionTableMaxEntries * 4];

            mNewIndexTableAddress = targetAddress;
            mNewIndexTableAddress += 4;//add two for the marker word, and add two for the size of the injected code

            mNewFunctionTableAddress = mNewIndexTableAddress + 2 + (uint)tempIndexTable.Length;//plus two for num entries byte and max entries byte
            mNewFunctionTableAddress += (mNewFunctionTableAddress % 2);//make sure address is even

            uint redirectFunctionAddress = mNewFunctionTableAddress + (uint)newFunctionTableData.Length;
            redirectFunctionAddress += (redirectFunctionAddress % 2);//make sure address is even

            //make all entries point at the redirect function by default
            {
                var functionPointerBytes = BitConverter.GetBytes(redirectFunctionAddress);
                Debug.Assert(functionPointerBytes.Length == 4);

                for (int x = 0; x < mNewFunctionTableMaxEntries; x++)
                {
                    functionPointerBytes.CopyTo(newFunctionTableData, x * 4);
                }
            }

            //create the redirect function data
            var patchedRedirectFunctionData = new byte[mRedirectFunctionData.Length];
            mRedirectFunctionData.CopyTo(patchedRedirectFunctionData, 0);
            {
                var originalFunctionTablePointerAddress = (UInt16)(mOriginalFunctionTableAddress & 0x3FFF);
                var originalFunctionTablePointerPage = (UInt16)((mOriginalFunctionTableAddress >> 14) & 0x03FF);
                var originalIndexTablePointerAddress = (UInt16)(mOriginalIndexTableAddress & 0x3FFF);
                var originalIndexTablePointerPage = (UInt16)((mOriginalIndexTableAddress >> 14) & 0x03FF);

                Debug.Assert(BitConverter.ToUInt16(patchedRedirectFunctionData, 0x06) == 0xDEAD);
                BitConverter.GetBytes(mCurrentServiceIDAddress).CopyTo(patchedRedirectFunctionData, 0x06);

                Debug.Assert(BitConverter.ToUInt16(patchedRedirectFunctionData, 0x12) == 0x03EF);
                Debug.Assert(BitConverter.ToUInt16(patchedRedirectFunctionData, 0x16) == 0xDEAD);
                BitConverter.GetBytes(originalIndexTablePointerPage).CopyTo(patchedRedirectFunctionData, 0x12);
                BitConverter.GetBytes(originalIndexTablePointerAddress).CopyTo(patchedRedirectFunctionData, 0x16);

                Debug.Assert(BitConverter.ToUInt16(patchedRedirectFunctionData, 0x22) == 0x03EF);
                Debug.Assert(BitConverter.ToUInt16(patchedRedirectFunctionData, 0x1E) == 0xDEAD);
                BitConverter.GetBytes(originalFunctionTablePointerPage).CopyTo(patchedRedirectFunctionData, 0x22);
                BitConverter.GetBytes(originalFunctionTablePointerAddress).CopyTo(patchedRedirectFunctionData, 0x1E);
            }

            uint relocatedFunctionStartAddress = redirectFunctionAddress + (uint)patchedRedirectFunctionData.Length;
            relocatedFunctionStartAddress += (relocatedFunctionStartAddress % 2);//make sure address is even
            uint nextRelocatedFunctionAddress = relocatedFunctionStartAddress;

            byte[] relocatedFunctionData = null;

            if (relocatedServiceIDs != null)
            {
                //function index 0 is the redirect function
                int functionIndex = 1;

                foreach (var serviceID in relocatedServiceIDs)
                {
                    uint offsetToFunction = 0;
                    var functionData = GetRelocatedFunctionData(serviceID, nextRelocatedFunctionAddress, out offsetToFunction);

                    if (functionData != null)
                    {
                        var functionPointerBytes = BitConverter.GetBytes(nextRelocatedFunctionAddress + offsetToFunction);
                        Debug.Assert(functionPointerBytes.Length == 4);
                        functionPointerBytes.CopyTo(newFunctionTableData, functionIndex * 4);

                        if (relocatedFunctionData == null)
                        {
                            relocatedFunctionData = new byte[functionData.Length];
                            functionData.CopyTo(relocatedFunctionData, 0);
                        }
                        else
                        {
                            var newRelocatedFunctionData = new byte[relocatedFunctionData.Length + functionData.Length];
                            relocatedFunctionData.CopyTo(newRelocatedFunctionData, 0);
                            functionData.CopyTo(newRelocatedFunctionData, relocatedFunctionData.Length);
                            relocatedFunctionData = newRelocatedFunctionData;
                        }

                        nextRelocatedFunctionAddress += (uint)functionData.Length;
                        nextRelocatedFunctionAddress += (nextRelocatedFunctionAddress % 2);//make sure address is even
                        functionIndex++;
                    }
                }
            }

            var writeMessageAddress = mNewIndexTableAddress - 4;//minus two to include the start marker, and minus two to include the injected code size
            var messageData = new byte[nextRelocatedFunctionAddress - writeMessageAddress + 2];//plus two for the end marker

            uint curDataIndex = 0;

            //write the start marker 0xDEAD
            BitConverter.GetBytes((UInt16)0xDEAD).CopyTo(messageData, curDataIndex);
            curDataIndex += 2;

            var relocatedCodeSize = (UInt16)(messageData.Length - 6);//minus 6 to skip the start and end marker and the code size
            BitConverter.GetBytes(relocatedCodeSize).CopyTo(messageData, curDataIndex);
            curDataIndex += 2;

            tempIndexTable.CopyTo(messageData, curDataIndex);
            curDataIndex += (uint)tempIndexTable.Length;

            //write the function table max num entries
            messageData[curDataIndex] = mNewFunctionTableMaxEntries;
            curDataIndex++;

            //write the function table current num entries
            messageData[curDataIndex] = mNewFunctionTableNumEntries;

            curDataIndex = mNewFunctionTableAddress - writeMessageAddress;
            newFunctionTableData.CopyTo(messageData, curDataIndex);

            curDataIndex = redirectFunctionAddress - writeMessageAddress;
            patchedRedirectFunctionData.CopyTo(messageData, curDataIndex);

            if (relocatedFunctionData != null)
            {
                curDataIndex = relocatedFunctionStartAddress - writeMessageAddress;
                relocatedFunctionData.CopyTo(messageData, curDataIndex);
            }

            //write the end marker 0xBEEF
            BitConverter.GetBytes((UInt16)0xBEEF).CopyTo(messageData, messageData.Length - 2);

            return messageData;
        }

        private bool LookingForEmptyRAMCallback(uint numRead, uint totalRead, uint totalToRead, ReadMemoryAction runningAction)
        {
            //TODO: don't search the entire range every time, just continue from where we left off....

            for (uint x = 0; x < totalRead; x++)
            {
                uint y = x;

                for (; y < totalRead; y++)
                {
                    if (runningAction.ReadData[y] != 0)
                    {
                        bool foundRelocatedCode = false;

                        if (y < totalRead - 1)
                        {
                            if ((BitConverter.ToUInt16(runningAction.ReadData, (int)y) == 0xDEAD) && (y < totalRead - 3))
                            {
                                var relocatedCodeSize = BitConverter.ToUInt16(runningAction.ReadData, (int)y + 2);
                                var endMarkerAddress = relocatedCodeSize + y + 4;

                                if (endMarkerAddress < totalRead - 1)
                                {
                                    if (BitConverter.ToUInt16(runningAction.ReadData, (int)endMarkerAddress) == 0xBEEF)
                                    {
                                        y = endMarkerAddress + 1;
                                        foundRelocatedCode = true;
                                    }
                                }
                            }
                        }

                        if (!foundRelocatedCode)
                        {
                            break;
                        }
                    }
                }

                var numEmptyBytes = y - x;

                if ((numEmptyBytes > 0) && (numEmptyBytes > mEmptyRAMNumBytes))
                {
                    mEmptyRAMNumBytes = numEmptyBytes;
                    mEmptyRAMStartAddress = runningAction.mStartAddress + x;
                }

                x = y;
            }

            //return true until we have found enough empty RAM to keep the read action from stopping early
            return (mEmptyRAMNumBytes < mRequiredSizeForRelocatedFunctionData);
        }

        protected override void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (action == mMyLastStartedAction)
            {
                switch (mRelocateState)
                {
                    case RelocateState.LocateHandlerIndexAndFunctionTablePointers:
                    {
                        //setzi62's notes:
                        //The message handler table pointer is stored at a fixed address, which only depends on the bootrom version, for 05.XX it is at 0xE228, for 06.xx it is at 0xE226.
                        //At 0xE1B2 is the pointer to the header decoding function for bootrom 05.xx And at 0xE1B0 is the pointer for bootrom version 06.xx
                        //The header decoding function is included in the bootrom and has a fixed address for all ecus with the same bootrom version.
                        //By reading at 0xE1B0 and 0xE1B2 I can determine the used bootrom version of the ecu.

                        success &= (action is ReadMemoryAction);

                        if (success)
                        {
                            var readAction = action as ReadMemoryAction;

                            //does address 0xE1B2 contain the function pointer of the KWP2000 header decoder function?
                            if (BitConverter.ToUInt16(readAction.ReadData, 2) == 0x2B86)
                            {
                                //bootrom 05.xx

                                mCommunicationFlagsAddress = 0xE074;
                                mCurrentMessageNumDataBytesAddress = 0xE1CA;
                                mCurrentMessageFirstDataByteAddress = 0xE1CE;
                                mCurrentServiceIDAddress = 0xE1D0;
                                mEnabledServiceIDsTableAddress = 0xE210;
                                mIndexAndFunctionTablePointerAddress = 0xE228;
                            }
                            //does address 0xE1B0 contain the function pointer of the KWP2000 header decoder function?
                            else if((BitConverter.ToUInt16(readAction.ReadData, 0) == 0x0260) || (BitConverter.ToUInt16(readAction.ReadData, 0) == 0x3D62))
                            {
                                //bootrom 06.xx

                                mCommunicationFlagsAddress = 0xE074;
                                mCurrentMessageNumDataBytesAddress = 0xE1C8;
                                mCurrentMessageFirstDataByteAddress = 0xE1CC;
                                mCurrentServiceIDAddress = 0xE1CE;
                                mEnabledServiceIDsTableAddress = 0xE20E;
                                mIndexAndFunctionTablePointerAddress = 0xE226;
                            }
                            else
                            {
                                CommInterface.DisplayStatusMessage("Unable to determine ME7 boot rom version from KWP2000 address data: 0x" + BitConverter.ToUInt32(readAction.ReadData, 0).ToString("X"), StatusMessageType.USER);
                                success = false;
                            }

                            mRelocateState = RelocateState.ReadHandlerIndexAndFunctionTablePointers;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to locate important KWP2000 addresses in the ECU.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.ReadHandlerIndexAndFunctionTablePointers:
                    {
                        success &= (action is ReadMemoryAction);

                        if (success)
                        {
                            var readAction = action as ReadMemoryAction;

                            var functionTableAddress = BitConverter.ToUInt16(readAction.ReadData, 0);
                            var functionTablePage = BitConverter.ToUInt16(readAction.ReadData, 2);
                            mOriginalFunctionTableAddress = (uint)(functionTablePage << 14) | functionTableAddress;
                            Debug.Assert(mOriginalFunctionTableAddress == 0x81A762);

                            var indexTableAddress = BitConverter.ToUInt16(readAction.ReadData, 4);
                            var indexTablePage = BitConverter.ToUInt16(readAction.ReadData, 6);
                            mOriginalIndexTableAddress = (uint)(indexTablePage << 14) | indexTableAddress;
                            Debug.Assert(mOriginalIndexTableAddress == 0x81A7FA);

                            if ((mOriginalFunctionTableAddress >= 0x380000) && (mOriginalFunctionTableAddress < 0x388000)
                                && (mOriginalIndexTableAddress >= 0x380000) && (mOriginalIndexTableAddress < 0x388000))
                            {
                                CommInterface.DisplayStatusMessage("Failed to relocate KWP2000 message handling configuration because it is already relocated.", StatusMessageType.USER);
                                OperationCompleted(true);
                            }
                            else
                            {
                                mRelocateState = RelocateState.ReadCurrentEnabledServiceIDsTable;
                            }
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to read current KWP2000 message handling configuration in the ECU.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.ReadCurrentEnabledServiceIDsTable:
                    {
                        success &= (action is ReadMemoryAction);

                        if (success)
                        {
                            var readAction = action as ReadMemoryAction;

                            mEnabledServiceIDs = readAction.ReadData;

                            mRelocateState = RelocateState.LocateEmptyRAM;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to read current KWP2000 enabled message types in the ECU.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.LocateEmptyRAM:
                    {
                        success &= (action is ReadMemoryAction);

                        if (success)
                        {
                            CommInterface.DisplayStatusMessage("Located " + mEmptyRAMNumBytes + " bytes of empty RAM at 0x" + mEmptyRAMStartAddress.ToString("X"), StatusMessageType.DEV_USER);

                            mRelocateState = RelocateState.WriteAndVerifyHandlerIndexAndFunctionTablesAndRedirectFunctionToRAM;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to locate empty RAM in the ECU for new KWP2000 configuration.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.WriteAndVerifyHandlerIndexAndFunctionTablesAndRedirectFunctionToRAM:
                    {
                        if (success)
                        {
                            mRelocateState = RelocateState.WriteNewEnabledServiceIDsTable;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to write new KWP2000 message handling configuration to empty RAM.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.WriteNewEnabledServiceIDsTable:
                    {
                        if (success)
                        {
                            mRelocateState = RelocateState.ChangeHandlerIndexAndFunctionTablePointers;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to write new KWP2000 enabled message types in the ECU.", StatusMessageType.USER);
                        }

                        break;
                    }
                    case RelocateState.ChangeHandlerIndexAndFunctionTablePointers:
                    {
                        if (success)
                        {
                            mRelocateState = RelocateState.Finished;
                        }
                        else
                        {
                            CommInterface.DisplayStatusMessage("Failed to change KWP2000 addresses in the ECU to use new message handling configuration.", StatusMessageType.USER);
                        }

                        break;
                    }
                }
            }

            mMyLastStartedAction = null;

            base.OnActionCompleted(action, success);
        }

        private enum RelocateState
        {
            LocateHandlerIndexAndFunctionTablePointers,
            ReadHandlerIndexAndFunctionTablePointers,
            ReadCurrentEnabledServiceIDsTable,
            LocateEmptyRAM,
            WriteAndVerifyHandlerIndexAndFunctionTablesAndRedirectFunctionToRAM,
            WriteNewEnabledServiceIDsTable,
            ChangeHandlerIndexAndFunctionTablePointers,
            Finished
        }

        protected virtual List<byte> GetRelocatedServiceIDs()
        {
            return null;
        }

        protected virtual byte[] GetRelocatedFunctionData(byte serviceID, uint destinationAddress, out uint offsetToFunction)
        {
            offsetToFunction = 0;

            return null;
        }

        private RelocateState mRelocateState;
        private CommunicationAction mMyLastStartedAction;

        private uint mEnabledServiceIDsTableAddress;
        private byte[] mEnabledServiceIDs;
        private uint mIndexAndFunctionTablePointerAddress;

        private uint mOriginalIndexTableAddress;
        private uint mOriginalFunctionTableAddress;

        protected UInt16 mCurrentServiceIDAddress;
        protected UInt16 mCurrentMessageFirstDataByteAddress;
        protected UInt16 mCurrentMessageNumDataBytesAddress;
        protected UInt16 mCommunicationFlagsAddress;

        //TODO: could have a list of empty RAM areas
        private uint mEmptyRAMStartAddress;
        private uint mEmptyRAMNumBytes;
        private uint mRequiredSizeForRelocatedFunctionData;

        private uint mNewIndexTableAddress;
        private uint mNewFunctionTableAddress;
        private byte mNewFunctionTableNumEntries;
        private byte mNewFunctionTableMaxEntries;

        private byte[] mRedirectFunctionData;
    }
}

// vi: set sw=4 ts=8 expandtab:
