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

// Comment this out for a real write. While set, a write stops once the baud
// byte is accepted and then closes the connection. Erase is not started.
//#define FIRST_WAKE_STOP_AFTER_BAUD

using System;
using System.Collections.Generic;

using Shared;

namespace Communication
{
    // Full flash transfer for a first-wake KWP2000 connection.
    // 9600 and 10400 request 0x14 (10400). Any higher ceiling requests 0x50
    // (38400), and a refusal of that byte requests 0x14. 0x63 is accepted only
    // when the reply names it. Silence ends the write. A refused key is not
    // retried and the 0x37 delay is not cleared. A 0x12 response stops here.
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
            return (speed == SlowSpeed) ? 10400u : (speed == FastSpeed) ? 38400u : (speed == TopSpeed) ? 56000u : 0u;
        }

        // 0x50 then 0x14 at 38400 and above. Below that, only 0x14.
        public static byte[] SessionSpeeds(uint ceiling)
        {
            if (ceiling >= BaudForSpeedByte(FastSpeed))
            {
                return new byte[] { FastSpeed, SlowSpeed };
            }

            return new byte[] { SlowSpeed };
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

    public class FirstWakeFlashOperation : KWP2000SequencialOperation
    {
        public FirstWakeFlashOperation(KWP2000Interface commInterface, byte[] image)
            : this(commInterface, image, FirstWakeFlashCodec.BaudForSpeedByte(FirstWakeFlashCodec.SlowSpeed))
        {
        }

        public FirstWakeFlashOperation(KWP2000Interface commInterface, byte[] image, uint baudCeiling)
            : this(commInterface, true, image, (uint)((image == null) ? 0 : image.Length), baudCeiling)
        {
        }

        public FirstWakeFlashOperation(KWP2000Interface commInterface, uint size)
            : this(commInterface, false, null, size, FirstWakeFlashCodec.BaudForSpeedByte(FirstWakeFlashCodec.SlowSpeed))
        {
        }

        private FirstWakeFlashOperation(KWP2000Interface commInterface, bool write, byte[] image, uint size, uint baudCeiling)
            : base(commInterface)
        {
            mTransfer = new FirstWakeFlashTransferAction(commInterface, write, image, size, FirstWakeFlashCodec.SessionSpeeds(baudCeiling));
            mTransfer.Progress = OnUpdatePercentComplete;
            mActionArray = new KWP2000Action[] { mTransfer };
        }

        public string ResultText
        {
            get { return mTransfer.ResultText; }
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
            Session,
            Timing,
            Erase,
            Routine,
            Setup,
            Block,
            Exit,
            Checksum
        }

        private static readonly byte[] Idents = { 0x95, 0x9B, 0x9C };

        public FirstWakeFlashTransferAction(KWP2000Interface commInterface, bool write, byte[] image, uint size, byte[] sessionSpeeds)
            : base(commInterface)
        {
            mWrite = write;
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

        public Action<float> Progress { get; set; }

        public override bool Start()
        {
            bool result = base.Start();

            if (!result)
            {
                return false;
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
            string plan = "speed byte 0x" + speed.ToString("X2") + " (" + baud + ").";
            if (mSessionSpeeds.Length > 1)
            {
                plan += " A refusal tries 0x" + mSessionSpeeds[1].ToString("X2") + ".";
            }

            plan += " It has not been sent.";
            if (mWrite)
            {
#if FIRST_WAKE_STOP_AFTER_BAUD
                DisplayStatusMessage("Full write started. " + plan + " It stops after the baud byte and closes the connection.", StatusMessageType.USER);
#else
                DisplayStatusMessage("Full write started. " + plan + " Erase follows an accepted byte.", StatusMessageType.USER);
#endif
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
                if (mStep == Step.Session)
                {
                    Fail("Speed byte 0x" + mSessionSpeed.ToString("X2") + " was sent and got no reply.");
                }
                else
                {
                    Fail("No reply during " + mStep + ".");
                }

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

            byte responseCode;
            bool negative = KWP2000Interface.IsNegativeResponseToRequest(mService, message, out responseCode);
            bool positive = KWP2000Interface.IsPositiveResponseToRequest(mService, message);

            if (!negative && !positive)
            {
                return false;
            }

            if (negative && (responseCode == (byte)KWP2000ResponseCode.RequiredTimeDelayNotExpired) && ((mStep == Step.Seed) || (mStep == Step.Key)))
            {
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
                        BeginSecurity(false);
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

                    if (!mLevel1)
                    {
                        DisplayStatusMessage("OK. Security access granted. The next request is the speed byte.", StatusMessageType.USER);
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
                case Step.Timing:
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

        private bool TryNextSessionSpeed(string reason)
        {
            if ((mSessionSpeedIndex + 1) >= mSessionSpeeds.Length)
            {
                return false;
            }

            byte refused = mSessionSpeed;
            mSessionSpeedIndex++;
            DisplayStatusMessage("First wake flash: speed 0x" + refused.ToString("X2") + " " + reason + ". Trying 0x" + mSessionSpeeds[mSessionSpeedIndex].ToString("X2") + ".", StatusMessageType.USER);
            SendSession(mSessionSpeeds[mSessionSpeedIndex]);
            return true;
        }

        private void SendSession()
        {
            if (mSessionSpeedIndex == 0)
            {
                string tries = "";
                for (int i = 0; i < mSessionSpeeds.Length; i++)
                {
                    if (i > 0)
                    {
                        tries += ", ";
                    }

                    tries += FirstWakeFlashCodec.BaudForSpeedByte(mSessionSpeeds[i]).ToString();
                }

                DisplayStatusMessage("First wake flash baud tries: " + tries + ".", StatusMessageType.USER);
            }

            SendSession(mSessionSpeeds[mSessionSpeedIndex]);
        }

        private void SendSession(byte speed)
        {
            mStep = Step.Session;
            mSessionSpeed = speed;
            mService = (byte)KWP2000ServiceID.StartDiagnosticSession;
            var message = SendMessage(mService, new byte[] { FirstWakeFlashCodec.SessionType, speed });
            DisplayStatusMessage("Speed byte request: session 0x85 at 0x" + speed.ToString("X2") + ". " + message.GetDataString(KWP2000CommInterface), StatusMessageType.USER);
        }

        private bool FinishSession(KWP2000Message message, bool negative, byte responseCode)
        {
            bool speedRefused = (responseCode == (byte)KWP2000ResponseCode.NoProgram)
                || (responseCode == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat);
            if (negative && speedRefused && TryNextSessionSpeed("was refused"))
            {
                return true;
            }

            if (negative && (responseCode == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat))
            {
                Fail("First wake flash is not supported.");
                return true;
            }

            if (negative)
            {
                Fail("First wake flash was refused (" + KWP2000CommInterface.GetResponseCodeString(responseCode) + ").");
                return true;
            }

            byte accepted = mSessionSpeed;
            if ((message.mData != null) && (message.mData.Length >= 2))
            {
                accepted = message.mData[1];
            }

            uint baud = FirstWakeFlashCodec.BaudForSpeedByte(accepted);
            if ((baud != 0) && (accepted != mSpeed))
            {
                mSpeed = accepted;
                KWP2000CommInterface.DiagnosticSessionBaudRate = baud;
                DisplayStatusMessage("First wake flash baud set to " + baud + ".", StatusMessageType.USER);
            }

#if FIRST_WAKE_STOP_AFTER_BAUD
            if (mWrite)
            {
                string rate = (baud != 0) ? baud.ToString() : ("0x" + accepted.ToString("X2"));
                ResultText = "Baud " + rate + " negotiated. Write aborted. Connection closed.";
                DisplayStatusMessage(ResultText, StatusMessageType.USER);
                ActionCompleted(true);
                KWP2000CommInterface.DisconnectFromECU();
                return true;
            }
#endif

            mStep = Step.Timing;
            mService = (byte)KWP2000ServiceID.AccessTimingParameters;
            DisplayStatusMessage("First wake flash: requesting timing parameters.", StatusMessageType.USER);
            SendMessage(mService, new byte[] { 0x00 });
            return true;
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

        private void Fail(string text)
        {
            string speedNote = "";
            if (text.IndexOf("speed byte", StringComparison.OrdinalIgnoreCase) < 0)
            {
                speedNote = (mStep == Step.Session)
                    ? " The speed byte was already sent."
                    : " The speed byte was not sent.";
            }

            ResultText = "FAILURE at " + mStep + ". " + text + speedNote;
            DisplayStatusMessage(ResultText, StatusMessageType.USER);
            ActionCompleted(false);
        }

        private readonly bool mWrite;
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
        private int mSessionSpeedIndex;
        private byte mBlock;
        private int mIdent;
        private int mOffset;
        private int mTries;
        private int mLoggedPercent = -1;
        private ushort mChecksum;
        private bool mLevel1;
        private bool mSessionSecurityUsed;
    }
}

// vi: set sw=4 ts=8 expandtab:
