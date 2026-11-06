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

Contact by Email: nyet@nyet.org
*/

using System;
using System.Collections.Generic;

using Shared;

namespace Communication
{
    // Full flash transfer for a first-wake KWP2000 connection.
    // The connect-bar baud selects the speed byte. The write requests the
    // fastest bench-accepted rate at or below that baud, and no other. The key is sent only when the session or the erase
    // answers 0x33. After that key the accepted speed byte is sent again,
    // then timing, then erase. A silent speed byte stops the write. ECU reset
    // is not sent, erase does not start, and 0x82 is not sent. A refused key
    // is not retried and the 0x37 delay is not cleared.
    // CreateProbe sends one 0x14 and stops. It does not erase, send 0x82, or
    // reset the ECU. A refused or silent 0x14 does not read 0x1A 0x9C.
    // RequestTimingAfterAccept sends 0x83 0x00 once after an accepted speed
    // byte, then stops. A byte other than 0x14 is sent at its decoded baud.
    // Erase does not start.
    public static class FirstWakeFlashCodec
    {
        public const uint FirstWakeFlashPolynomial = 0x18932250;
        public const uint Level1Polynomial = 0x5FBD5DBD;
        public const int Rounds = 5;
        public const byte SessionType = 0x85;
        public const byte FastSpeed = 0x50;
        public const byte SlowSpeed = 0x14;
        public const byte TopSpeed = 0x63;
        public const byte TransferFormat = 0x11;
        public static readonly byte[] TransferKey = System.Text.Encoding.ASCII.GetBytes("DISPOSITION");

        public static uint Key(uint seed, uint polynomial)
        {
            uint key = seed;

            for (int round = 0; round < Rounds; round++)
            {
                if ((key & 0x80000000) != 0)
                {
                    key = ((key << 1) | 1) ^ polynomial;
                }
                else
                {
                    key <<= 1;
                }
            }

            return key;
        }

        public static uint Checksum32(byte[] data)
        {
            uint sum = 0;

            if (data != null)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    sum += data[i];
                }
            }

            return sum;
        }

        public static uint BaudForSpeedByte(byte speed)
        {
            return StartDiagnosticSessionAction.CalculateBaudRateFromByte(speed);
        }

        // One speed byte: 0x50 at 38400 or higher, otherwise 0x14.
        public static byte[] SessionSpeeds(uint ceiling)
        {
            uint limit = (ceiling == 0) ? (uint)KWP2000BaudRates.BAUD_10400 : ceiling;
            return new byte[] { (limit >= (uint)KWP2000BaudRates.BAUD_38400) ? FastSpeed : SlowSpeed };
        }

        // Zeros 0x000-0x1FF and 0x400-0x4FF, then fixes the calibration word at
        // 0xBF04 and the summary word at 0xBF00 when a marker is present.
        public static string PrepareImage(byte[] image)
        {
            if ((image == null) || (image.Length < 0xBF06))
            {
                return "Image is shorter than 0xBF06. Checksums were left unchanged.";
            }

            int zeroEnd = Math.Min(image.Length, 0x200);
            for (int i = 0; i < zeroEnd; i++)
            {
                image[i] = 0xFF;
            }

            int holeEnd = Math.Min(image.Length, 0x500);
            for (int i = 0x400; i < holeEnd; i++)
            {
                image[i] = 0xFF;
            }

            bool marked = ((image[0x6900] == 0x00) && (image[0x6901] == (byte)'i'))
                || ((image[0x7400] == 0x00) && (image[0x7401] == (byte)'t'))
                || ((image[0x6C00] == 0x00) && (image[0x6C01] == (byte)'l'));

            if (!marked)
            {
                return "No calibration marker at 0x6900, 0x7400, or 0x6C00. Checksums were left unchanged.";
            }

            uint sum = 0;
            for (int i = 0x6900; i < 0xBF00; i++)
            {
                sum += image[i];
            }

            sum &= 0xFFFF;
            image[0xBF04] = (byte)(sum >> 8);
            image[0xBF05] = (byte)sum;

            uint summary = (sum + image[0xBF03] + ((uint)image[0xBF02] << 8)) & 0xFFFF;
            image[0xBF00] = (byte)(summary >> 8);
            image[0xBF01] = (byte)summary;

            return "Calibration checksum " + sum.ToString("X4") + ", summary " + summary.ToString("X4") + ".";
        }

        public static byte[] Compress(byte[] source)
        {
            var output = new List<byte>();
            output.Add(0x1A);
            output.Add(0x01);

            int literal = 0;
            int index = 0;
            int length = (source == null) ? 0 : source.Length;

            while (true)
            {
                if (index >= length)
                {
                    if (literal != 0)
                    {
                        FlushLiterals(source, output, ref literal, index);
                    }

                    uint sum = Checksum32(source);
                    output.Add(0xC0);
                    output.Add((byte)(sum >> 24));
                    output.Add((byte)(sum >> 16));
                    output.Add((byte)(sum >> 8));
                    output.Add((byte)sum);
                    return output.ToArray();
                }

                if (literal > 0x3FEF)
                {
                    FlushLiterals(source, output, ref literal, index);
                }

                if ((index + 1 < length) && (source[index] == source[index + 1]))
                {
                    int run = 1;
                    while ((run < 0x3FF0) && (index + run < length) && (source[index] == source[index + run]))
                    {
                        run++;
                    }

                    if (run < 5)
                    {
                        literal++;
                    }
                    else
                    {
                        if (literal != 0)
                        {
                            FlushLiterals(source, output, ref literal, index);
                        }

                        ushort code = (ushort)(run + 0x4000);
                        output.Add((byte)(code >> 8));
                        output.Add((byte)code);
                        output.Add(source[index]);
                        index += run - 1;
                    }
                }
                else
                {
                    literal++;
                }

                index++;
            }
        }

        public static void ApplyTransferCipher(byte[] data)
        {
            if ((data == null) || (data.Length == 0))
            {
                return;
            }

            int offset = 0;
            int length = data.Length;
            if ((length >= 2) && (data[0] == 0x1A) && (data[1] == 0x01))
            {
                offset = 2;
                length -= 2;
            }

            int keyIndex = 0;
            for (int i = 0; i < length; i++)
            {
                if (keyIndex >= TransferKey.Length)
                {
                    keyIndex = 0;
                }

                data[offset + i] ^= TransferKey[keyIndex];
                keyIndex++;
            }
        }

        // Same 13-byte 0x31 0xC4 layout as EraseFlashAction. The last six bytes are the programming-session trailer, not "NEFMTO".
        public static byte[] EraseRequest(uint start, uint end)
        {
            return new byte[]
            {
                (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash,
                (byte)((start >> 16) & 0xFF), (byte)((start >> 8) & 0xFF), (byte)(start & 0xFF),
                (byte)((end >> 16) & 0xFF), (byte)((end >> 8) & 0xFF), (byte)(end & 0xFF),
                0x00, 0x01, 0x01, 0x0A, 0x81, 0xFE
            };
        }

        private static void FlushLiterals(byte[] source, List<byte> output, ref int literal, int index)
        {
            output.Add((byte)((literal >> 8) & 0xFF));
            output.Add((byte)(literal & 0xFF));

            int start = index - literal;
            for (int i = 0; i < literal; i++)
            {
                output.Add(source[start + i]);
            }

            literal = 0;
        }
    }

    public enum FirstWakeProbeResult
    {
        None,
        Accepted,
        Refused,
        Silent,
        Locked,
        WakeFail
    }

    public static class FirstWakeProbeReport
    {
        public static string ResultLine(FirstWakeProbeResult result)
        {
            return "result " + Word(result);
        }

        public static string NextLine(FirstWakeProbeResult result)
        {
            if (result == FirstWakeProbeResult.Accepted)
            {
                return "next power-off-seconds 5";
            }

            if (result == FirstWakeProbeResult.Locked)
            {
                return "next leave-on-minutes 15";
            }

            return "next power-off-minutes 15";
        }

        public static string Word(FirstWakeProbeResult result)
        {
            switch (result)
            {
                case FirstWakeProbeResult.Accepted:
                    return "accepted";
                case FirstWakeProbeResult.Refused:
                    return "refused";
                case FirstWakeProbeResult.Silent:
                    return "silent";
                case FirstWakeProbeResult.Locked:
                    return "locked";
                default:
                    return "wake-fail";
            }
        }
    }

    public class FirstWakeFlashOperation : KWP2000SequencialOperation
    {
        public FirstWakeFlashOperation(KWP2000Interface commInterface, byte[] image)
            : this(commInterface, image, FirstWakeFlashCodec.BaudForSpeedByte(FirstWakeFlashCodec.SlowSpeed))
        {
        }

        public FirstWakeFlashOperation(KWP2000Interface commInterface, byte[] image, uint baudCeiling)
            : this(commInterface, true, image, (uint)((image == null) ? 0 : image.Length), baudCeiling, false)
        {
        }

        public FirstWakeFlashOperation(KWP2000Interface commInterface, uint size)
            : this(commInterface, false, null, size, FirstWakeFlashCodec.BaudForSpeedByte(FirstWakeFlashCodec.SlowSpeed), false)
        {
        }

        public static FirstWakeFlashOperation CreateProbe(KWP2000Interface commInterface)
        {
            return CreateProbe(commInterface, false);
        }

        public static FirstWakeFlashOperation CreateProbe(KWP2000Interface commInterface, bool clearOnSilent)
        {
            return CreateProbe(commInterface, FirstWakeFlashCodec.SlowSpeed, clearOnSilent);
        }

        public static FirstWakeFlashOperation CreateProbe(KWP2000Interface commInterface, byte speed, bool clearOnSilent)
        {
            return new FirstWakeFlashOperation(commInterface, true, null, 1, new byte[] { speed }, true, clearOnSilent);
        }

        private FirstWakeFlashOperation(KWP2000Interface commInterface, bool write, byte[] image, uint size, uint baudCeiling, bool probe)
            : this(commInterface, write, image, size, baudCeiling, probe, false)
        {
        }

        private FirstWakeFlashOperation(KWP2000Interface commInterface, bool write, byte[] image, uint size, uint baudCeiling, bool probe, bool clearOnSilent)
            : this(commInterface, write, image, size, FirstWakeFlashCodec.SessionSpeeds(baudCeiling), probe, clearOnSilent)
        {
        }

        private FirstWakeFlashOperation(KWP2000Interface commInterface, bool write, byte[] image, uint size, byte[] sessionSpeeds, bool probe, bool clearOnSilent)
            : base(commInterface)
        {
            mTransfer = new FirstWakeFlashTransferAction(commInterface, write, image, size, sessionSpeeds, probe, clearOnSilent);
            mTransfer.Progress = OnUpdatePercentComplete;
            mActionArray = new KWP2000Action[] { mTransfer };
        }

        public string ResultText
        {
            get { return mTransfer.ResultText; }
        }

        public bool ProbeStopped
        {
            get { return mTransfer.ProbeStopped; }
        }

        public FirstWakeProbeResult ProbeResult
        {
            get { return mTransfer.ProbeResult; }
        }

        public void StopBeforeSpeedByte()
        {
            mTransfer.StopBeforeSpeedByte = true;
        }

        public void RequestStandardSession()
        {
            mTransfer.StandardSessionInstead = true;
        }

        public void SendInsteadOfSpeedByte(byte[] request)
        {
            mTransfer.InsteadOfSpeedByte = request;
        }

        public void SendInsteadAtBaud(uint baud)
        {
            mTransfer.InsteadBaud = baud;
        }

        public void SendFollowAfterSpeed(byte[] request)
        {
            mTransfer.FollowAfterSpeed = request;
        }

        public void RequestBeforeKey()
        {
            mTransfer.BeforeKey = true;
        }

        public void RequestKeyFirst()
        {
            mTransfer.KeyFirst = true;
        }

        public void SkipSessionProof()
        {
            mTransfer.SkipProof = true;
        }

        public void SendThenChain(System.Collections.Generic.List<byte[]> chain)
        {
            mTransfer.ThenChain = chain;
        }

        public void RequestTimingAfterAccept()
        {
            mTransfer.TimingAfterAccept = true;
        }

        public string TimingText
        {
            get { return mTransfer.TimingText; }
        }

        public byte[] ReadData
        {
            get { return mTransfer.ReadData; }
        }

        private readonly FirstWakeFlashTransferAction mTransfer;
    }

    // One exchange. Write erases and downloads. Read uses the same session and
    // the write's address/size bytes with service 0x35, then pulls 0x36 blocks.
    public class FirstWakeFlashTransferAction : KWP2000Action
    {
        private enum Step
        {
            Wake,
            Ident,
            Seed,
            Key,
            Release,
            Session,
            ClearStatus,
            ClearReset,
            Timing,
            Erase,
            Routine,
            Setup,
            Block,
            Exit,
            Checksum
        }

        private static readonly byte[] Idents = { 0x95, 0x9B, 0x9C };

        public FirstWakeFlashTransferAction(KWP2000Interface commInterface, bool write, byte[] image, uint size, byte[] sessionSpeeds, bool probe, bool clearOnSilent)
            : base(commInterface)
        {
            mWrite = write;
            mProbe = probe;
            mClearOnSilent = probe && clearOnSilent;
            mSize = size;
            mSessionSpeeds = ((sessionSpeeds != null) && (sessionSpeeds.Length > 0))
                ? sessionSpeeds
                : FirstWakeFlashCodec.SessionSpeeds(FirstWakeFlashCodec.BaudForSpeedByte(FirstWakeFlashCodec.SlowSpeed));

            if (image != null)
            {
                mImage = new byte[image.Length];
                Buffer.BlockCopy(image, 0, mImage, 0, image.Length);
            }
        }

        public byte[] ReadData { get; private set; }

        public string ResultText { get; private set; }

        public bool ProbeStopped { get; private set; }

        public bool StopBeforeSpeedByte { get; set; }

        public bool StandardSessionInstead { get; set; }

        public byte[] InsteadOfSpeedByte { get; set; }

        public uint InsteadBaud { get; set; }

        public byte[] FollowAfterSpeed { get; set; }

        public bool BeforeKey { get; set; }

        public bool KeyFirst { get; set; }

        public bool SkipProof { get; set; }

        public System.Collections.Generic.List<byte[]> ThenChain { get; set; }

        private int mThenIndex;

        private bool mExitQueued;

        private int mProof;

        private uint mProofBaud;

        public bool TimingAfterAccept { get; set; }

        public string TimingText { get; private set; }

        public FirstWakeProbeResult ProbeResult { get; private set; }

        public Action<float> Progress { get; set; }

        public override bool Start()
        {
            bool result = base.Start();

            if (!result)
            {
                return false;
            }

            if (mProbe)
            {
                byte probeSpeed = mSessionSpeeds[0];
                uint probeBaud = FirstWakeFlashCodec.BaudForSpeedByte(probeSpeed);
                DisplayStatusMessage("Baud probe. speed byte 0x" + probeSpeed.ToString("X2") + " (" + probeBaud + "). It has not been sent. Erase does not start.", StatusMessageType.USER);
                KWP2000CommInterface.FirstWakeFlashUseThreeByteHeader = true;
                mIdent = 0;
                SendIdent();
                return true;
            }

            if (mWrite && ((mImage == null) || (mImage.Length == 0) || (mSize > 0xFFFFFF)))
            {
                Fail("Full write needs a flash file.");
                return true;
            }

            if (!mWrite && ((mSize == 0) || (mSize > 0xFFFFFF)))
            {
                Fail("Full read size is 0.");
                return true;
            }

            byte speed = mSessionSpeeds[0];
            uint baud = FirstWakeFlashCodec.BaudForSpeedByte(speed);
            string plan = "speed byte 0x" + speed.ToString("X2") + " (" + baud + "). It has not been sent.";
            if (mWrite)
            {
                DisplayStatusMessage("Full write started. " + plan + " Erase follows an accepted byte.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Full read started. " + plan, StatusMessageType.USER);
            }

            KWP2000CommInterface.FirstWakeFlashUseThreeByteHeader = true;
            mStep = Step.Wake;
            KWP2000CommInterface.RequestFirstWakeFlashWake(WakeFinished);
            return true;
        }

        protected override void ActionCompletedInternal(bool success, bool communicationError)
        {
            KWP2000CommInterface.FirstWakeFlashUseThreeByteHeader = false;
            base.ActionCompletedInternal(success, communicationError);
        }

        private void WakeFinished(bool success)
        {
            if (!success)
            {
                Fail("0x11 wake did not finish.");
                return;
            }

            DisplayStatusMessage("OK. 0x11 wake was sent. Waiting for start communication. The speed byte has not been sent.", StatusMessageType.USER);
            mService = (byte)KWP2000ServiceID.StartCommunication;
            KWP2000Message fastStart = KWP2000CommInterface.TakeFirstWakeFlashFastStartMessage();
            if (fastStart != null)
            {
                AttachResponsesFinishedHandler(fastStart);
                return;
            }

            DisplayStatusMessage("First wake flash: starting communication.", StatusMessageType.USER);
            SendMessage(mService, 0, null);
        }

        protected override void ResponsesFinishedHandler(KWP2000Interface commInterface, KWP2000Message message, bool sentProperly, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries)
        {
            if (!sentProperly || !receivedAnyReplies)
            {
                if (mStep == Step.ClearStatus)
                {
                    DisplayStatusMessage("Flash status 0x1A 0x9C got no reply.", StatusMessageType.USER);
                    SendClearReset();
                    return;
                }

                if (mStep == Step.ClearReset)
                {
                    FinishClear("ECU reset 0x11 0x01 got no reply.");
                    return;
                }

                if (mStep == Step.Release)
                {
                    if (SendThenIfPending("got no reply"))
                    {
                        return;
                    }

                    if (mProof == 1)
                    {
                        DisplayStatusMessage("Timing got no reply at " + mProofBaud + ". Trying 10400.", StatusMessageType.USER);
                        mProof = 2;
                        KWP2000CommInterface.DiagnosticSessionBaudRate = 10400;
                        SendRaw(new byte[] { 0x83, 0x00 });
                        return;
                    }

                    if (mProof == 2)
                    {
                        FinishProbe(FirstWakeProbeResult.Accepted, "Proof silent at " + mProofBaud + " and at 10400. Speed byte 0x" + mSessionSpeed.ToString("X2") + " was accepted. Erase did not start.");
                        return;
                    }

                    FinishProbe(FirstWakeProbeResult.None, ReleaseNote("got no reply."));
                    return;
                }

                if ((mStep == Step.Timing) && mProbe)
                {
                    FinishProbeTiming("silent", "was sent and got no reply.");
                    return;
                }

                if (mStep == Step.Session)
                {
                    if (mProbe && (FollowAfterSpeed != null) && (FollowAfterSpeed.Length > 0))
                    {
                        DisplayStatusMessage("Speed byte 0x" + mSessionSpeed.ToString("X2") + " got no reply. Follow-up goes out at 10400. Erase does not start.", StatusMessageType.USER);
                        SendFollow(FollowAfterSpeed);
                        return;
                    }

                    if (mProbe && !mClearOnSilent)
                    {
                        FinishProbe(FirstWakeProbeResult.Silent, "Speed byte 0x" + mSessionSpeed.ToString("X2") + " was sent and got no reply.");
                        return;
                    }

                    if (mProbe)
                    {
                        ProbeResult = FirstWakeProbeResult.Silent;
                        BeginClear("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was sent and got no reply.");
                        return;
                    }

                    Fail("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was sent and got no reply. ECU reset was not sent. Erase does not start.");
                    return;
                }

                Fail("No reply during " + mStep + ".");
                return;
            }

            if (mStep == Step.Wake)
            {
                DisplayStatusMessage("OK. Start communication was answered. The speed byte has not been sent.", StatusMessageType.USER);
                mIdent = 0;
                SendIdent();
                return;
            }

            base.ResponsesFinishedHandler(commInterface, message, sentProperly, receivedAnyReplies, waitedForAllReplies, numRetries);
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            if (base.MessageHandler(commInterface, message))
            {
                return true;
            }

            if ((mStep == Step.ClearStatus) || (mStep == Step.ClearReset))
            {
                return HandleClearReply(message);
            }

            byte responseCode;
            bool negative = KWP2000Interface.IsNegativeResponseToRequest(mService, message, out responseCode);
            bool positive = KWP2000Interface.IsPositiveResponseToRequest(mService, message);

            if (!negative && !positive)
            {
                return false;
            }

            if (negative && (responseCode == (byte)KWP2000ResponseCode.RequiredTimeDelayNotExpired) && ((mStep == Step.Seed) || (mStep == Step.Key)))
            {
                if (mProbe)
                {
                    ProbeResult = FirstWakeProbeResult.Locked;
                }

                Fail("Security access is locked. Leave ignition on for 15 minutes and try again.");
                return true;
            }

            switch (mStep)
            {
                case Step.Ident:
                    mIdent++;
                    if (mIdent < Idents.Length)
                    {
                        SendIdent();
                    }
                    else
                    {
                        DisplayStatusMessage("OK. Idents finished. The speed byte has not been sent.", StatusMessageType.USER);
                        if (mProbe && BeforeKey && (InsteadOfSpeedByte != null) && (InsteadOfSpeedByte.Length > 0))
                        {
                            DisplayStatusMessage("The request goes out before the key.", StatusMessageType.USER);
                            SendInstead(InsteadOfSpeedByte);
                            return true;
                        }

                        if (WantsKeyBeforeSession())
                        {
                            BeginSecurity(false);
                        }
                        else
                        {
                            DisplayStatusMessage("The speed byte goes out before the key.", StatusMessageType.USER);
                            SendSession();
                        }
                    }

                    return true;
                case Step.Seed:
                    if (negative || !positive)
                    {
                        Fail("Security seed was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + "). Key was not sent.");
                        return true;
                    }

                    return SendKey(message);
                case Step.Key:
                    if (!positive)
                    {
                        Fail((mLevel1 ? "Session security key" : "First wake flash security key") + " was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                        return true;
                    }

                    if (!mLevel1 && !mResumeErase)
                    {
                        DisplayStatusMessage("OK. Security access granted. The next request is the speed byte.", StatusMessageType.USER);
                    }

                    if (mResumeErase)
                    {
                        mResumeErase = false;
                        DisplayStatusMessage("OK. Security access granted. The speed byte goes out again.", StatusMessageType.USER);
                        mTries = 0;
                        SendSession(mSpeed);
                        return true;
                    }

                    if (mProbe && (InsteadOfSpeedByte != null) && (InsteadOfSpeedByte.Length > 0))
                    {
                        SendInstead(InsteadOfSpeedByte);
                        return true;
                    }

                    if (mProbe && StandardSessionInstead)
                    {
                        DisplayStatusMessage("Sending standard session 0x10 0x81. No speed byte is sent.", StatusMessageType.USER);
                        mStep = Step.Release;
                        mService = (byte)KWP2000ServiceID.StartDiagnosticSession;
                        SendMessage(mService, 0, new byte[] { 0x81 });
                        return true;
                    }

                    if (mProbe && StopBeforeSpeedByte)
                    {
                        DisplayStatusMessage("Sending 0x82 StopCommunication. No speed byte is sent.", StatusMessageType.USER);
                        mStep = Step.Release;
                        mService = (byte)KWP2000ServiceID.StopCommunication;
                        SendMessage(mService, 0, null);
                        return true;
                    }

                    SendSession();
                    return true;
                case Step.Session:
                    if (negative && (responseCode == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested))
                    {
                        if (mSessionSecurityUsed)
                        {
                            Fail("First wake flash was refused after the level 1 key.");
                            return true;
                        }

                        mSessionSecurityUsed = true;
                        BeginSecurity(true);
                        return true;
                    }

                    return FinishSession(message, negative, responseCode);
                case Step.Release:
                    if ((mService == (byte)KWP2000ServiceID.RequestUpload)
                        && KWP2000Interface.IsPositiveResponseToRequest(mService, message))
                    {
                        QueueTransferExit();
                    }

                    if (SendThenIfPending("reply received. Data " + PayloadText(message)))
                    {
                        return true;
                    }

                    if (mProof > 0)
                    {
                        uint at = (mProof == 1) ? mProofBaud : 10400;
                        FinishProbe(FirstWakeProbeResult.Accepted, "Proof answered at " + at + ". Data " + PayloadText(message) + ". Erase did not start.");
                        return true;
                    }

                    if (TryStartProof(message))
                    {
                        return true;
                    }

                    FinishProbe(FirstWakeProbeResult.None, ReleaseNote("reply received.") + " Data " + PayloadText(message) + ".");
                    return true;
                case Step.Timing:
                    if (mProbe)
                    {
                        if (!positive)
                        {
                            FinishProbeTiming("refused", "was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                            return true;
                        }

                        FinishProbeTiming("answered", "was answered. Data " + PayloadText(message) + ".");
                        return true;
                    }

                    if (!positive)
                    {
                        Fail("Timing parameters were refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                        return true;
                    }

                    if (mWrite)
                    {
                        BeginErase();
                    }
                    else
                    {
                        mTries = 0;
                        SendSetup();
                    }

                    return true;
                case Step.Erase:
                    if (negative && (responseCode == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested))
                    {
                        if (mEraseSecurityUsed)
                        {
                            Fail("Erase was refused after the security key (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + "). The speed byte was already sent.");
                            return true;
                        }

                        mEraseSecurityUsed = true;
                        mResumeErase = true;
                        DisplayStatusMessage("Erase asked for security. The speed byte was already sent.", StatusMessageType.USER);
                        BeginSecurity(false);
                        return true;
                    }

                    if (negative)
                    {
                        mTries++;
                        if (mTries > 5)
                        {
                            Fail("Erase was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                            return true;
                        }

                        SendErase();
                        return true;
                    }

                    mTries = 0;
                    mRoutine = (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash;
                    mStep = Step.Routine;
                    SendRoutineResult();
                    return true;
                case Step.Routine:
                    return PollRoutine(message, negative, responseCode);
                case Step.Setup:
                    return AcceptTransferSetup(message, negative, responseCode);
                case Step.Block:
                    return AcceptBlock(message, negative, responseCode);
                case Step.Exit:
                    if (negative)
                    {
                        Fail("Transfer exit was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                        return true;
                    }

                    if (mWrite)
                    {
                        BeginChecksum();
                    }
                    else
                    {
                        ReadData = mRead.ToArray();
                        DisplayStatusMessage("First wake flash read finished. " + ReadData.Length.ToString("X") + " bytes.", StatusMessageType.USER);
                        ActionCompleted(true);
                    }

                    return true;
                case Step.Checksum:
                    if (negative)
                    {
                        Fail("Checksum request was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                        return true;
                    }

                    mTries = 0;
                    mRoutine = (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum;
                    mStep = Step.Routine;
                    SendRoutineResult();
                    return true;
                default:
                    return false;
            }
        }

        private bool WantsKeyBeforeSession()
        {
            if (!mProbe)
            {
                return false;
            }

            if (KeyFirst)
            {
                return true;
            }

            if (BeforeKey)
            {
                return false;
            }

            return (InsteadOfSpeedByte != null)
                || StandardSessionInstead
                || StopBeforeSpeedByte;
        }

        private void SendIdent()
        {
            mStep = Step.Ident;
            mService = (byte)KWP2000ServiceID.ReadECUIdentification;
            DisplayStatusMessage("First wake flash: requesting 0x1A 0x" + Idents[mIdent].ToString("X2") + ".", StatusMessageType.USER);
            SendMessage(mService, new byte[] { Idents[mIdent] });
        }

        private void BeginSecurity(bool level1)
        {
            mLevel1 = level1;
            mStep = Step.Seed;
            mService = (byte)KWP2000ServiceID.SecurityAccess;
            DisplayStatusMessage(level1 ? "First wake flash: session asked for security. Requesting the level 1 seed." : "First wake flash: requesting security seed.", StatusMessageType.USER);
            SendMessage(mService, new byte[] { 0x01 });
        }

        private bool SendKey(KWP2000Message message)
        {
            if ((message.mData == null) || (message.mData.Length < 5))
            {
                Fail("Security seed was too short. Key was not sent.");
                return true;
            }

            uint seed = ((uint)message.mData[1] << 24) | ((uint)message.mData[2] << 16) | ((uint)message.mData[3] << 8) | message.mData[4];
            uint key = FirstWakeFlashCodec.Key(seed, mLevel1 ? FirstWakeFlashCodec.Level1Polynomial : FirstWakeFlashCodec.FirstWakeFlashPolynomial);
            byte mode = (byte)(message.mData[0] + 1);

            DisplayStatusMessage("First wake flash seed " + seed.ToString("X8") + ", key " + key.ToString("X8") + ".", StatusMessageType.USER);
            mStep = Step.Key;
            SendMessage(mService, new byte[] { mode, (byte)(key >> 24), (byte)(key >> 16), (byte)(key >> 8), (byte)key });
            return true;
        }

        private void SendSession()
        {
            byte speed = mSessionSpeeds[0];
            DisplayStatusMessage("First wake flash baud tries: " + FirstWakeFlashCodec.BaudForSpeedByte(speed) + ".", StatusMessageType.USER);
            SendSession(speed);
        }

        private void SendSession(byte speed)
        {
            mSpeedByteSent = true;
            mStep = Step.Session;
            mSessionSpeed = speed;
            mService = (byte)KWP2000ServiceID.StartDiagnosticSession;
            var message = SendMessage(mService, 0, new byte[] { FirstWakeFlashCodec.SessionType, speed });
            DisplayStatusMessage("Speed byte request: session 0x85 at 0x" + speed.ToString("X2") + ". " + message.GetDataString(KWP2000CommInterface), StatusMessageType.USER);
        }

        private bool FinishSession(KWP2000Message message, bool negative, byte responseCode)
        {
            if (mProbe)
            {
                if ((FollowAfterSpeed != null) && (FollowAfterSpeed.Length > 0))
                {
                    string speedOutcome = negative ? "was refused" : "was accepted";
                    if (negative)
                    {
                        speedOutcome = "was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ")";
                    }

                    DisplayStatusMessage("Speed byte 0x" + mSessionSpeed.ToString("X2") + " " + speedOutcome + ". Follow-up goes out at 10400. Erase does not start.", StatusMessageType.USER);
                    SendFollow(FollowAfterSpeed);
                    return true;
                }

                if (negative)
                {
                    if (mClearOnSilent)
                    {
                        ProbeResult = FirstWakeProbeResult.Refused;
                        BeginClear("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                        return true;
                    }

                    FinishProbe(FirstWakeProbeResult.Refused, "Speed byte 0x" + mSessionSpeed.ToString("X2") + " was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                    return true;
                }

                byte acceptedSpeed = AcceptedSpeed(message, mSessionSpeed);

                if (TimingAfterAccept)
                {
                    uint rateBaud = StartDiagnosticSessionAction.CalculateBaudRateFromByte(acceptedSpeed);
                    string at = "10400";
                    if ((rateBaud != 0) && (rateBaud != 10400))
                    {
                        KWP2000CommInterface.DiagnosticSessionBaudRate = rateBaud;
                        at = rateBaud.ToString();
                    }

                    mStep = Step.Timing;
                    mService = (byte)KWP2000ServiceID.AccessTimingParameters;
                    DisplayStatusMessage("Speed byte 0x" + acceptedSpeed.ToString("X2") + " was accepted. Timing 0x83 0x00 goes out at " + at + ". Erase does not start.", StatusMessageType.USER);
                    SendMessage(mService, 0, new byte[] { 0x00 });
                    return true;
                }

                FinishProbe(FirstWakeProbeResult.Accepted, "Baud " + FirstWakeFlashCodec.BaudForSpeedByte(acceptedSpeed) + " accepted. Erase did not start.");
                return true;
            }

            if (negative)
            {
                Fail("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + "). ECU reset was not sent. Erase does not start.");
                return true;
            }

            byte accepted = AcceptedSpeed(message, mSessionSpeed);
            uint baud = FirstWakeFlashCodec.BaudForSpeedByte(accepted);
            if (accepted != mSpeed)
            {
                mSpeed = accepted;
                KWP2000CommInterface.DiagnosticSessionBaudRate = baud;
                DisplayStatusMessage("First wake flash baud set to " + baud + ".", StatusMessageType.USER);
            }

            mStep = Step.Timing;
            mService = (byte)KWP2000ServiceID.AccessTimingParameters;
            DisplayStatusMessage("First wake flash: requesting timing parameters.", StatusMessageType.USER);
            SendMessage(mService, new byte[] { 0x00 });
            return true;
        }

        private void BeginClear(string reason)
        {
            mClearReason = reason;
            DisplayStatusMessage(reason + " Reading flash status 0x1A 0x9C, then one ECU reset 0x11 0x01. Erase does not start.", StatusMessageType.USER);
            mStep = Step.ClearStatus;
            mService = (byte)KWP2000ServiceID.ReadECUIdentification;
            SendMessage(mService, 0, new byte[] { 0x9C });
        }

        private void SendClearReset()
        {
            mStep = Step.ClearReset;
            mService = (byte)KWP2000ServiceID.EcuReset;
            DisplayStatusMessage("First wake flash: ECU reset 0x11 0x01.", StatusMessageType.USER);
            SendMessage(mService, 0, new byte[] { 0x01 });
        }

        private bool HandleClearReply(KWP2000Message message)
        {
            byte responseCode;
            bool negative = KWP2000Interface.IsNegativeResponseToRequest(mService, message, out responseCode);
            string text = negative
                ? ("refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ")")
                : PayloadText(message);

            if (mStep == Step.ClearStatus)
            {
                DisplayStatusMessage("Flash status 0x1A 0x9C: " + text + FlashStatusNote(message, negative) + ".", StatusMessageType.USER);
                SendClearReset();
                return true;
            }

            FinishClear("ECU reset 0x11 0x01 reply: " + text + ".");
            return true;
        }

        private static string FlashStatusNote(KWP2000Message message, bool negative)
        {
            if (negative || (message.mData == null) || (message.mData.Length < 5) || (message.mData[0] != 0x9C))
            {
                return "";
            }

            return " (status 0x" + message.mData[1].ToString("X2")
                + ", attempts 0x" + message.mData[2].ToString("X2")
                + ", successful 0x" + message.mData[3].ToString("X2")
                + ", preconditions 0x" + message.mData[4].ToString("X2") + ")";
        }

        private static byte AcceptedSpeed(KWP2000Message message, byte sent)
        {
            return ((message.mData != null) && (message.mData.Length >= 2)) ? message.mData[1] : sent;
        }

        private static string PayloadText(KWP2000Message message)
        {
            return ((message.mData == null) || (message.mData.Length == 0)) ? "no data" : HexText(message.mData);
        }

        private void FinishClear(string resetNote)
        {
            ResultText = "FAILURE at Session. " + mClearReason + " " + resetNote
                + " Reconnect and try the speed byte again. A long power-off has cleared this when the reset did not. Leaving the ECU powered does not. Erase did not start.";
            DisplayStatusMessage(ResultText, StatusMessageType.USER);
            ActionCompleted(false);
            KWP2000CommInterface.ConnectionStatus = CommunicationInterface.ConnectionStatusType.Disconnected;
        }

        private void BeginErase()
        {
            DisplayStatusMessage(FirstWakeFlashCodec.PrepareImage(mImage), StatusMessageType.USER);
            mPayload = FirstWakeFlashCodec.Compress(mImage);
            FirstWakeFlashCodec.ApplyTransferCipher(mPayload);
            mChecksum = (ushort)(FirstWakeFlashCodec.Checksum32(mImage) & 0xFFFF);
            mOffset = 0;
            mTries = 0;
            mStep = Step.Erase;
            mService = (byte)KWP2000ServiceID.StartRoutineByLocalIdentifier;
            DisplayStatusMessage("First wake flash: erasing 0 through " + (mSize - 1).ToString("X") + ".", StatusMessageType.USER);
            SendErase();
        }

        private void SendErase()
        {
            SendMessage(mService, FirstWakeFlashCodec.EraseRequest(0, mSize - 1));
        }

        private bool PollRoutine(KWP2000Message message, bool negative, byte responseCode)
        {
            bool erase = mRoutine == (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash;
            if (negative && (responseCode != (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress))
            {
                Fail((erase ? "Erase" : "Checksum") + " result was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                return true;
            }

            if ((message.mData != null) && (message.mData.Length > 0) && (message.mData[0] == mRoutine))
            {
                if (erase)
                {
                    DisplayStatusMessage("Erase complete.", StatusMessageType.USER);
                    mTries = 0;
                    SendSetup();
                }
                else
                {
                    DisplayStatusMessage("First wake flash checksum matched.", StatusMessageType.USER);
                    ActionCompleted(true);
                }

                return true;
            }

            mTries++;
            if (mTries >= 100)
            {
                Fail((erase ? "Erase" : "Checksum") + " routine did not finish.");
                return true;
            }

            SendRoutineResult();
            return true;
        }

        private bool AcceptTransferSetup(KWP2000Message message, bool negative, byte responseCode)
        {
            if (negative && (responseCode != (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress))
            {
                Fail((mWrite ? "Download" : "Upload") + " was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                return true;
            }

            if (!negative && (message.mData != null) && (message.mData.Length > 0) && (message.mData[0] > 1))
            {
                mBlock = (byte)(message.mData[0] - 1);
                mTries = 0;
                mOffset = 0;
                mLoggedPercent = -1;
                mStep = Step.Block;
                mService = (byte)KWP2000ServiceID.TransferData;
                if (mWrite)
                {
                    SendNextChunk();
                }
                else
                {
                    mRead = new List<byte>();
                    SendMessage(mService);
                }

                return true;
            }

            mTries++;
            if (mTries > 10)
            {
                Fail((mWrite ? "Download" : "Upload") + " stayed busy.");
                return true;
            }

            SendSetup();
            return true;
        }

        private void SendNextChunk()
        {
            int remain = mPayload.Length - mOffset;
            int count = (remain > mBlock) ? mBlock : remain;
            byte[] chunk = new byte[count];
            Buffer.BlockCopy(mPayload, mOffset, chunk, 0, count);
            LogPercent(mOffset, mPayload.Length);
            mService = (byte)KWP2000ServiceID.TransferData;
            SendMessage(mService, chunk);
        }

        private bool AcceptBlock(KWP2000Message message, bool negative, byte responseCode)
        {
            if (negative)
            {
                mTries++;
                if (mTries > 1)
                {
                    Fail("Transfer block was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                    return true;
                }

                if (mWrite)
                {
                    SendNextChunk();
                }
                else
                {
                    SendMessage(mService);
                }

                return true;
            }

            mTries = 0;
            if (mWrite)
            {
                int count = (mPayload.Length - mOffset > mBlock) ? mBlock : (mPayload.Length - mOffset);
                mOffset += count;
                if (mOffset >= mPayload.Length)
                {
                    LogPercent(mPayload.Length, mPayload.Length);
                    SendExit();
                }
                else
                {
                    SendNextChunk();
                }

                return true;
            }

            int before = mRead.Count;
            if ((message.mData != null) && (message.mData.Length > 0))
            {
                int take = Math.Min(message.mData.Length, (int)mSize - before);
                for (int i = 0; i < take; i++)
                {
                    mRead.Add(message.mData[i]);
                }
            }

            LogPercent(mRead.Count, (int)mSize);
            if ((mRead.Count >= mSize) || (mRead.Count == before))
            {
                SendExit();
            }
            else
            {
                SendMessage(mService);
            }

            return true;
        }

        private void SendSetup()
        {
            mStep = Step.Setup;
            mService = mWrite ? (byte)KWP2000ServiceID.RequestDownload : (byte)KWP2000ServiceID.RequestUpload;
            DisplayStatusMessage(mWrite ? "First wake flash: requesting download." : ("First wake flash: requesting upload of " + mSize.ToString("X") + " bytes."), StatusMessageType.USER);
            Track(mWrite
                ? KWP2000MessageHelpers.SendRequestDownloadMessage(KWP2000CommInterface, 0x600000, mSize, FirstWakeFlashCodec.TransferFormat)
                : KWP2000MessageHelpers.SendRequestUploadMessage(KWP2000CommInterface, 0x600000, mSize, FirstWakeFlashCodec.TransferFormat));
        }

        private void SendRoutineResult()
        {
            mService = (byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier;
            Track((mRoutine == (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash)
                ? KWP2000MessageHelpers.SendRequestEraseFlashResultMessage(KWP2000CommInterface)
                : KWP2000MessageHelpers.SendRequestValidateFlashChecksumResultMessage(KWP2000CommInterface));
        }

        private void SendExit()
        {
            mStep = Step.Exit;
            mService = (byte)KWP2000ServiceID.RequestTransferExit;
            DisplayStatusMessage("First wake flash: requesting transfer exit.", StatusMessageType.USER);
            SendMessage(mService);
        }

        private void BeginChecksum()
        {
            mTries = 0;
            mStep = Step.Checksum;
            mService = (byte)KWP2000ServiceID.StartRoutineByLocalIdentifier;
            DisplayStatusMessage("First wake flash: checking checksum " + mChecksum.ToString("X4") + ".", StatusMessageType.USER);
            Track(KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, 0, mSize - 1, mChecksum));
        }

        private void Track(KWP2000Message message)
        {
            AttachResponsesFinishedHandler(message);
        }

        private void LogPercent(int done, int total)
        {
            int percent = (total <= 0) ? 100 : (int)((done * 100L) / total);
            if (percent == mLoggedPercent)
            {
                return;
            }

            int previous = mLoggedPercent;
            mLoggedPercent = percent;
            if (Progress != null)
            {
                Progress(percent);
            }

            if ((percent == 100) || (previous < 0) || (percent / 10 != previous / 10))
            {
                DisplayStatusMessage("First wake flash transfer " + percent + "%.", StatusMessageType.USER);
            }
        }

        private void SendInstead(byte[] request)
        {
            string at = "10400";
            if ((InsteadBaud != 0) && (InsteadBaud != 10400))
            {
                KWP2000CommInterface.DiagnosticSessionBaudRate = InsteadBaud;
                at = InsteadBaud.ToString();
            }

            DisplayStatusMessage("Sending " + HexText(request) + " at " + at + ". No speed byte is sent.", StatusMessageType.USER);
            SendRaw(request);
        }

        private void SendFollow(byte[] request)
        {
            DisplayStatusMessage("Sending " + HexText(request) + " at 10400 after speed byte 0x" + mSessionSpeed.ToString("X2") + ".", StatusMessageType.USER);
            SendRaw(request);
        }

        private void SendRaw(byte[] request)
        {
            mStep = Step.Release;
            mService = request[0];
            byte[] data = null;
            if (request.Length > 1)
            {
                data = new byte[request.Length - 1];
                Buffer.BlockCopy(request, 1, data, 0, data.Length);
            }

            SendMessage(mService, 0, data);
        }

        private bool SendThenIfPending(string firstOutcome)
        {
            if ((ThenChain == null) || (mThenIndex >= ThenChain.Count))
            {
                return false;
            }

            bool once = ThenChain.Count == 1;
            if (!once && ((firstOutcome.IndexOf("got no reply", StringComparison.Ordinal) >= 0)
                || ((mThenIndex == 0) && (firstOutcome.IndexOf("Data 85", StringComparison.Ordinal) < 0))))
            {
                return false;
            }

            byte[] next = ThenChain[mThenIndex];
            mThenIndex++;
            string lead = once ? ("First request " + firstOutcome + ". Sending ") : ("Previous " + firstOutcome + ". Sending ");
            string tail = once ? " at 10400." : " at 10400. Erase does not start.";
            DisplayStatusMessage(lead + HexText(next) + tail, StatusMessageType.USER);
            SendRaw(next);
            return true;
        }

        private void QueueTransferExit()
        {
            if (mExitQueued || (ThenChain == null) || (ThenChain.Count == 1))
            {
                return;
            }

            mExitQueued = true;
            ThenChain.Add(new byte[] { (byte)KWP2000ServiceID.RequestTransferExit });
            DisplayStatusMessage("Upload was accepted. Transfer exit 0x37 closes it. Erase does not start.", StatusMessageType.USER);
        }

        private static string HexText(byte[] request)
        {
            string text = "";
            for (int i = 0; i < request.Length; i++)
            {
                if (i > 0)
                {
                    text += " ";
                }

                text += request[i].ToString("X2");
            }

            return text;
        }

        private static uint FiatOrBoschBaud(byte speed)
        {
            switch (speed)
            {
                case 0x02: return 19200;
                case 0x03: return 38400;
                case 0x04: return 57600;
                case 0x05: return 115200;
                case 0x50: return 38400;
                case 0x62: return 54400;
                case 0x63: return 56000;
                default: return 0;
            }
        }

        private bool TryStartProof(KWP2000Message message)
        {
            if (SkipProof || !BeforeKey || (mProof != 0) || (InsteadOfSpeedByte == null) || (InsteadOfSpeedByte.Length < 3))
            {
                return false;
            }

            if ((InsteadOfSpeedByte[0] != (byte)KWP2000ServiceID.StartDiagnosticSession) || (InsteadOfSpeedByte[1] != FirstWakeFlashCodec.SessionType))
            {
                return false;
            }

            if (!KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.StartDiagnosticSession, message))
            {
                return false;
            }

            uint baud = FiatOrBoschBaud(InsteadOfSpeedByte[2]);
            if (baud == 0)
            {
                return false;
            }

            mSessionSpeed = InsteadOfSpeedByte[2];
            mProofBaud = baud;
            mProof = 1;
            KWP2000CommInterface.DiagnosticSessionBaudRate = baud;
            DisplayStatusMessage("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was accepted. Timing 0x83 0x00 goes out at " + baud + ". Erase does not start.", StatusMessageType.USER);
            SendRaw(new byte[] { 0x83, 0x00 });
            return true;
        }

        private string ReleaseNote(string outcome)
        {
            string what = (mService == (byte)KWP2000ServiceID.StopCommunication) ? "0x82" : "0x10 0x81";
            if ((FollowAfterSpeed != null) && (FollowAfterSpeed.Length > 0))
            {
                what = "follow-up";
                return what + " " + outcome + " Speed byte 0x" + mSessionSpeed.ToString("X2") + " was sent.";
            }

            if ((InsteadOfSpeedByte != null) && (InsteadOfSpeedByte.Length > 0))
            {
                what = "request";
            }

            return what + " " + outcome + " No speed byte was sent.";
        }

        private void FinishProbeTiming(string outcome, string detail)
        {
            TimingText = outcome;
            FinishProbe(FirstWakeProbeResult.Accepted, "Speed byte 0x" + mSessionSpeed.ToString("X2") + " was accepted. Timing 0x83 0x00 " + detail + " Erase did not start.");
        }

        private void FinishProbe(FirstWakeProbeResult result, string text)
        {
            ProbeResult = result;
            ResultText = text;
            ProbeStopped = true;
            DisplayStatusMessage(text, StatusMessageType.USER);
            ActionCompleted(result == FirstWakeProbeResult.Accepted);
        }

        private void Fail(string text)
        {
            if (mProbe && (ProbeResult == FirstWakeProbeResult.None))
            {
                ProbeResult = FirstWakeProbeResult.WakeFail;
            }

            string speedNote = "";
            if (text.IndexOf("speed byte", StringComparison.OrdinalIgnoreCase) < 0)
            {
                speedNote = mSpeedByteSent
                    ? " The speed byte was already sent."
                    : " The speed byte was not sent.";
            }

            ResultText = "FAILURE at " + mStep + ". " + text + speedNote;
            DisplayStatusMessage(ResultText, StatusMessageType.USER);
            ActionCompleted(false);
        }

        private readonly bool mWrite;
        private readonly bool mProbe;
        private readonly bool mClearOnSilent;
        private readonly uint mSize;
        private byte[] mImage;
        private byte[] mPayload;
        private List<byte> mRead;
        private Step mStep;
        private byte mService;
        private byte mRoutine;
        private byte mSpeed = FirstWakeFlashCodec.SlowSpeed;
        private byte mSessionSpeed = FirstWakeFlashCodec.SlowSpeed;
        private byte[] mSessionSpeeds = new byte[] { FirstWakeFlashCodec.SlowSpeed };
        private bool mSpeedByteSent;
        private byte mBlock;
        private int mIdent;
        private int mOffset;
        private int mTries;
        private int mLoggedPercent = -1;
        private ushort mChecksum;
        private bool mLevel1;
        private bool mSessionSecurityUsed;
        private bool mEraseSecurityUsed;
        private bool mResumeErase;
        private string mClearReason = "";
    }
}

// vi: set sw=4 ts=8 expandtab:
