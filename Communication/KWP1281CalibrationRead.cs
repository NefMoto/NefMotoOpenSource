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
using System.Threading;

using Shared;

namespace Communication
{
    // Calibration read is 0x0000-0xBFFF. The 24C02 is the separate eeprom read.
    public static class KWP1281CalibrationRead
    {
        public const int CalibrationSize = 0xC000;
        public const int EepromSize = 256;
        public const int ImmoOffset = 0x0C;
        public const byte ImmoEnabled = 0xA0;
        public const byte ImmoDisabled = 0x50;
        public static readonly byte[] EepromMarker = { 0x12, 0x68, 0x30, 0x12 };

        public static byte[] CopyEeprom(byte[] window)
        {
            int at = IndexOf(window, EepromMarker);
            if (at < 0)
            {
                return null;
            }

            var eeprom = new byte[EepromSize];
            for (int i = 0; i < EepromSize; i++)
            {
                eeprom[i] = 0xFF;
            }

            int count = Math.Min(EepromSize, window.Length - at);
            Buffer.BlockCopy(window, at, eeprom, 0, count);
            return eeprom;
        }

        public static string Describe(byte[] eeprom)
        {
            if ((eeprom == null) || (eeprom.Length <= ImmoOffset))
            {
                return "24C02 marker 12 68 30 12 was not found.";
            }

            byte immo = eeprom[ImmoOffset];
            string state = "unrecognized";
            if (immo == ImmoEnabled)
            {
                state = "enabled";
            }
            else if (immo == ImmoDisabled)
            {
                state = "disabled";
            }

            return "24C02 marker found. Immo byte 0x" + immo.ToString("X2") + " (" + state + ").";
        }

        // One 0xFC block. FF FF is the empty list. Each fault is code high, code low, status.
        public static string FormatFaultBlock(byte[] data)
        {
            if ((data == null) || (data.Length == 0))
            {
                return "empty";
            }

            if ((data.Length >= 2) && (data[0] == 0xFF) && (data[1] == 0xFF))
            {
                return "none";
            }

            string text = "";
            int groups = data.Length / 3;
            for (int i = 0; i < groups; i++)
            {
                int at = i * 3;
                int code = (data[at] << 8) | data[at + 1];
                if (text.Length > 0)
                {
                    text += ", ";
                }

                text += code.ToString("X4") + "/" + code.ToString() + " status " + data[at + 2].ToString("X2");
            }

            if ((data.Length % 3) != 0)
            {
                if (text.Length > 0)
                {
                    text += ", ";
                }

                text += "trailing";
            }

            return text;
        }

        // 18010 is P1626. 16507 is P0123. The four digits of (word - 16384) are the P-code.
        public static bool TryPCode(ushort vag, out ushort pcode)
        {
            pcode = vag;
            int body = vag - 0x4000;
            if ((body < 0) || (body > 9999))
            {
                return false;
            }

            int value = 0;
            for (int place = 1; body > 0; body /= 10, place *= 16)
            {
                value += (body % 10) * place;
            }

            pcode = (ushort)value;
            return true;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if ((haystack == null) || (needle == null) || (haystack.Length < needle.Length))
            {
                return -1;
            }

            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public class KWP1281CalibrationReadAction : KWP2000Action
    {
        public KWP1281CalibrationReadAction(KWP2000Interface commInterface)
            : this(commInterface, false)
        {
        }

        public KWP1281CalibrationReadAction(KWP2000Interface commInterface, bool eepromOnly)
            : base(commInterface)
        {
            mEepromOnly = eepromOnly;
        }

        public byte[] Calibration { get; private set; }
        public byte[] Eeprom { get; private set; }
        public Action<float> Progress { get; set; }

        public override bool Start()
        {
            if (!base.Start())
            {
                return false;
            }

            if (!KWP2000CommInterface.IsHeldKWP1281Session)
            {
                DisplayStatusMessage(mEepromOnly ? "Read EEPROM needs the held KWP1281 session." : "Read Calibration needs the held KWP1281 session.", StatusMessageType.USER);
                ActionCompleted(false);
                return true;
            }

            if (mEepromOnly)
            {
                KWP2000CommInterface.HeldKWP1281Read.RequestKWP1281EepromRead(ReadFinished, Progress);
            }
            else
            {
                KWP2000CommInterface.HeldKWP1281Read.RequestKWP1281CalibrationRead(ReadFinished, Progress);
            }

            return true;
        }

        private void ReadFinished(bool success, byte[] calibration, byte[] eeprom)
        {
            if (IsComplete)
            {
                return;
            }

            if (mEepromOnly)
            {
                if (!success || (eeprom == null))
                {
                    ActionCompleted(false);
                    return;
                }

                Eeprom = eeprom;
                ActionCompleted(true);
                return;
            }

            if (!success || (calibration == null))
            {
                ActionCompleted(false);
                return;
            }

            Calibration = calibration;
            Eeprom = eeprom;
            ActionCompleted(true);
        }

        private readonly bool mEepromOnly;
    }

    public class KWP1281CalibrationReadOperation : KWP2000SequencialOperation
    {
        public KWP1281CalibrationReadOperation(KWP2000Interface commInterface)
            : this(commInterface, false)
        {
        }

        public KWP1281CalibrationReadOperation(KWP2000Interface commInterface, bool eepromOnly)
            : base(commInterface)
        {
            mRead = new KWP1281CalibrationReadAction(commInterface, eepromOnly);
            mRead.Progress = OnUpdatePercentComplete;
            mActionArray = new KWP2000Action[] { mRead };
        }

        public byte[] Calibration
        {
            get { return mRead.Calibration; }
        }

        public byte[] Eeprom
        {
            get { return mRead.Eeprom; }
        }

        public override void Abort()
        {
            KWP2000CommInterface.HeldKWP1281Read.CancelKWP1281CalibrationRead();
            base.Abort();
        }

        private readonly KWP1281CalibrationReadAction mRead;
    }

    // Calibration and 24C02 reads on a held KWP1281 session.
    internal sealed class KWP1281Read
    {
        internal KWP1281Read(KWP2000Interface host)
        {
            mHost = host;
        }

        internal void RequestKWP1281CalibrationRead(Action<bool, byte[], byte[]> finished, Action<float> progress)
        {
            BeginKWP1281Read(finished, progress, false);
        }

        internal void RequestKWP1281EepromRead(Action<bool, byte[], byte[]> finished, Action<float> progress)
        {
            BeginKWP1281Read(finished, progress, true);
        }

        internal void RequestDtcs(bool clearAfterRead, Action<bool, string> finished)
        {
            mDtcClear = clearAfterRead;
            mDtcFinished = finished;
            mDtcRequested = true;
        }

        private void BeginKWP1281Read(Action<bool, byte[], byte[]> finished, Action<float> progress, bool eepromOnly)
        {
            mKWP1281ReadCancel = false;
            mKWP1281EepromOnly = eepromOnly;
            mKWP1281ReadFinished = finished;
            mKWP1281ReadProgress = progress;
            mKWP1281ReadDone = 0;
            mKWP1281ReadPercent = -1;
            mKWP1281ReadTotal = eepromOnly ? 0x1000 : KWP1281CalibrationRead.CalibrationSize;
            mKWP1281ReadRequested = true;
        }

        internal void CancelKWP1281CalibrationRead()
        {
            mKWP1281ReadCancel = true;
        }

        internal bool Execute()
        {
            if (mDtcRequested)
            {
                mDtcRequested = false;
                Action<bool, string> dtcFinished = mDtcFinished;
                mDtcFinished = null;
                bool live = false;
                string report = "not the held KWP1281 session";
                bool dtcOk = false;
                if (mHost.IsHeldKWP1281Session)
                {
                    dtcOk = ExchangeDtcs(mDtcClear, out live, out report);
                }

                if (live)
                {
                    EndHeldKWP1281Dialogue();
                }

                if (dtcFinished != null)
                {
                    dtcFinished(dtcOk, report);
                }

                return true;
            }

            if (!mKWP1281ReadRequested)
            {
                return false;
            }

            mKWP1281ReadRequested = false;
            Action<bool, byte[], byte[]> readFinished = mKWP1281ReadFinished;
            mKWP1281ReadFinished = null;
            bool readOk = false;
            byte[] calibration = null;
            byte[] eeprom = null;
            if (mHost.IsHeldKWP1281Session && !mKWP1281ReadCancel)
            {
                readOk = ReadKWP1281Calibration(out calibration, out eeprom);
            }
            else if (!mHost.IsHeldKWP1281Session)
            {
                mHost.DisplayStatusMessage("This read needs the held KWP1281 session.", StatusMessageType.USER);
            }

            mKWP1281ReadProgress = null;
            if (readFinished != null)
            {
                readFinished(readOk, calibration, eeprom);
            }

            CloseHeldKWP1281();
            return true;
        }


        private bool mDtcRequested;
        private bool mDtcClear;
        private Action<bool, string> mDtcFinished;

        // The held connect has already gone idle. Wake again, then read faults with block 0x07.
        // Clear is block 0x05, and only after the first read answered.
        private bool ExchangeDtcs(bool clearAfterRead, out bool live, out string report)
        {
            live = false;
            report = "";
            if (!WakeForKWP1281Read())
            {
                report = "wake did not stay KWP1281";
                return false;
            }

            live = true;
            if (!FinishGreeting())
            {
                report = "ident did not finish";
                return false;
            }

            PauseBeforeTesterBlock();
            if (!SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.RequestReadDTCs))
            {
                report = "read request was not sent";
                return false;
            }

            if (!CollectFaults(out string before))
            {
                report = "read " + before;
                return false;
            }

            if (!clearAfterRead)
            {
                report = "read " + before;
                return true;
            }

            PauseBeforeTesterBlock();
            if (!SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.RequestClearDTCs))
            {
                report = "read " + before + "; clear was not sent";
                return false;
            }

            if (!ReadUntilAck(out string clearText))
            {
                report = "read " + before + "; clear " + clearText;
                return false;
            }

            PauseBeforeTesterBlock();
            if (!SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.RequestReadDTCs))
            {
                report = "read " + before + "; clear " + clearText + "; second read was not sent";
                return false;
            }

            if (!CollectFaults(out string after))
            {
                report = "read " + before + "; clear " + clearText + "; after " + after;
                return false;
            }

            report = "read " + before + "; clear " + clearText + "; after " + after;
            return true;
        }

        private bool FinishGreeting()
        {
            if (!AckCurrent())
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                KWP2000Interface.KWP1281Block block;
                if (!mHost.KWP1281ReadBlock(out block))
                {
                    return false;
                }

                mHost.mKWP1281BlockCounter = block.mBlockCounter;
                if (block.mBlockTitle == (byte)KWP2000Interface.KWP1281BlockTitle.Acknowledge)
                {
                    return true;
                }

                if (!AckCurrent())
                {
                    return false;
                }
            }

            return false;
        }

        private bool CollectFaults(out string text)
        {
            text = "";
            for (int i = 0; i < 8; i++)
            {
                KWP2000Interface.KWP1281Block block;
                if (!mHost.KWP1281ReadBlock(out block))
                {
                    text = "no reply";
                    return false;
                }

                mHost.mKWP1281BlockCounter = block.mBlockCounter;
                if (block.mBlockTitle == (byte)KWP2000Interface.KWP1281BlockTitle.Acknowledge)
                {
                    if (text.Length == 0)
                    {
                        text = "none";
                    }

                    return true;
                }

                if (block.mBlockTitle != (byte)KWP2000Interface.KWP1281BlockTitle.ResponseReadDTCs)
                {
                    text = "title 0x" + block.mBlockTitle.ToString("X2");
                    return false;
                }

                string formatted = KWP1281CalibrationRead.FormatFaultBlock(block.mBlockData);
                text = (text.Length == 0) ? formatted : text + "; " + formatted;
                if (!AckCurrent())
                {
                    text += " ack failed";
                    return false;
                }
            }

            text = "too many blocks";
            return false;
        }

        private bool ReadUntilAck(out string text)
        {
            text = "";
            for (int i = 0; i < 8; i++)
            {
                KWP2000Interface.KWP1281Block block;
                if (!mHost.KWP1281ReadBlock(out block))
                {
                    text = "no reply";
                    return false;
                }

                mHost.mKWP1281BlockCounter = block.mBlockCounter;
                if (block.mBlockTitle == (byte)KWP2000Interface.KWP1281BlockTitle.Acknowledge)
                {
                    if (text.Length == 0)
                    {
                        text = "answered";
                    }

                    return true;
                }

                string piece = "title 0x" + block.mBlockTitle.ToString("X2");
                if (block.mBlockTitle == (byte)KWP2000Interface.KWP1281BlockTitle.ResponseReadDTCs)
                {
                    piece = KWP1281CalibrationRead.FormatFaultBlock(block.mBlockData);
                }

                text = (text.Length == 0) ? piece : text + "; " + piece;
                if (!AckCurrent())
                {
                    text += " ack failed";
                    return false;
                }
            }

            text = "too many blocks";
            return false;
        }

        private bool SendTitle(byte title)
        {
            mHost.mKWP1281BlockCounter++;
            var request = new KWP2000Interface.KWP1281Block();
            request.mBlockCounter = mHost.mKWP1281BlockCounter;
            request.mBlockTitle = title;
            request.mBlockData = null;
            return mHost.KWP1281SendBlock(request);
        }

        private bool AckCurrent()
        {
            PauseBeforeTesterBlock();
            return SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.Acknowledge);
        }

        private void PauseBeforeTesterBlock()
        {
            Thread.Sleep((int)mHost.KWP1281_TesterMinTimeToSendResponseMessageMS);
        }

        private bool mKWP1281ReadRequested;
        private bool mKWP1281EepromOnly;
        private volatile bool mKWP1281ReadCancel;
        private int mKWP1281ReadDone;
        private int mKWP1281ReadTotal;
        private int mKWP1281ReadPercent;
        private Action<bool, byte[], byte[]> mKWP1281ReadFinished;
        private Action<float> mKWP1281ReadProgress;

        private void NoteKWP1281ReadBlock()
        {
            mKWP1281ReadDone += 0x10;
            if ((mKWP1281ReadTotal <= 0) || (mKWP1281ReadProgress == null))
            {
                return;
            }

            int percent = (mKWP1281ReadDone * 100) / mKWP1281ReadTotal;
            if (percent > 100)
            {
                percent = 100;
            }

            if (percent == mKWP1281ReadPercent)
            {
                return;
            }

            mKWP1281ReadPercent = percent;
            mKWP1281ReadProgress(percent);
        }

        // The connect session goes idle before the click, and the ECU drops it.
        // The read wakes again and sends the first block as soon as the greeting arrives.
        private bool WakeForKWP1281Read()
        {
            if (mKWP1281ReadCancel)
            {
                return false;
            }

            mHost.DisplayStatusMessage("KWP1281 read: waking at 0x01.", StatusMessageType.USER);
            mHost.ClearMessageBuffers();
            mHost.mP1ECUResponseInterByteTimeOut.Reset();
            mHost.mP2ECUResponseTimeOut.Reset();
            mHost.mP3TesterRequestTimeOut.Reset();
            Thread.Sleep((int)mHost.TimeBetweenSlowInitForKWP2000MS);
            if (mKWP1281ReadCancel)
            {
                return false;
            }

            byte keyByte1;
            byte keyByte2;
            bool woke = mHost.SendSlowInit(KWP2000Interface.DEFAULT_ECU_SLOWINIT_KWP1281_ADDRESS, KWP2000Interface.SlowInitDataBits.Eight, KWP2000Interface.SlowInitParity.None, out keyByte1, out keyByte2);
            if (!woke || (keyByte1 != KWP2000Interface.KWP_1281_PROTOCOL_KEY_BYTE_1) || (keyByte2 != KWP2000Interface.KWP_1281_PROTOCOL_KEY_BYTE_2))
            {
                mHost.DisplayStatusMessage("KWP1281 read wake did not stay KWP1281.", StatusMessageType.USER);
                return false;
            }

            KWP2000Interface.KWP1281Block greeting;
            if (!mHost.KWP1281ReadBlock(out greeting))
            {
                mHost.DisplayStatusMessage("KWP1281 greeting block was not received.", StatusMessageType.USER);
                return false;
            }

            mHost.mKWP1281BlockCounter = greeting.mBlockCounter;
            return true;
        }

        // Calibration is title 3 over 0x0000-0xBFFF. The 24C02 is a separate read of the 0xC000 window.
        // A missed block is 0x10 bytes of 0xFF. Three tries, then the hole.
        private bool ReadKWP1281Calibration(out byte[] calibration, out byte[] eeprom)
        {
            calibration = null;
            eeprom = null;
            if (!WakeForKWP1281Read())
            {
                return false;
            }

            mHost.mKWP1281QuietBlocks = true;
            mKWP1281RecoveryWakes = 0;
            mKWP1281DeadBlocks = 0;
            try
            {
                if (mKWP1281EepromOnly)
                {
                    var window = new byte[0x1000];
                    for (int i = 0; i < window.Length; i++)
                    {
                        window[i] = 0xFF;
                    }

                    mHost.DisplayStatusMessage("Reading 24C02 from the 0xC000 window.", StatusMessageType.USER);
                    if (!ReadKWP1281Window(window, 0xC000, 0xCFFF, 1))
                    {
                        return false;
                    }

                    eeprom = KWP1281CalibrationRead.CopyEeprom(window);
                    mHost.DisplayStatusMessage(KWP1281CalibrationRead.Describe(eeprom), StatusMessageType.USER);
                    return (eeprom != null) && !mKWP1281ReadCancel;
                }

                calibration = new byte[KWP1281CalibrationRead.CalibrationSize];
                for (int i = 0; i < calibration.Length; i++)
                {
                    calibration[i] = 0xFF;
                }

                mHost.DisplayStatusMessage("Reading calibration 0x0000-0xBFFF. This is not a full flash read.", StatusMessageType.USER);
                if (!ReadKWP1281Window(calibration, 0, 0xBFFF, 3))
                {
                    calibration = null;
                    return false;
                }
            }
            finally
            {
                mHost.mKWP1281QuietBlocks = false;
            }

            return !mKWP1281ReadCancel;
        }

        private int mKWP1281RecoveryWakes;
        private int mKWP1281DeadBlocks;

        // A lost end byte leaves the ECU silent. Wake again and retry this address.
        // Stop when the wake itself fails, or when three addresses in a row still do not answer.
        private bool RecoverKWP1281Read(int addr)
        {
            if (mKWP1281ReadCancel)
            {
                return false;
            }

            mKWP1281RecoveryWakes++;
            mHost.DisplayStatusMessage("KWP1281 read lost the session at 0x" + addr.ToString("X4") + ". Waking again (" + mKWP1281RecoveryWakes + ").", StatusMessageType.USER);
            if (!WakeForKWP1281Read())
            {
                mHost.DisplayStatusMessage("KWP1281 read stopped. The session stopped answering.", StatusMessageType.USER);
                return false;
            }

            return true;
        }

        private bool ReadKWP1281Window(byte[] dest, int origin, int limit, byte title)
        {
            for (int addr = origin; addr < limit; addr += 0x10)
            {
                if (mKWP1281ReadCancel)
                {
                    return false;
                }

                byte[] chunk = ReadKWP1281DataBlock(title, addr);
                if ((chunk == null) && !RecoverKWP1281Read(addr))
                {
                    return false;
                }

                if (chunk == null)
                {
                    chunk = ReadKWP1281DataBlock(title, addr);
                }

                if (chunk != null)
                {
                    mKWP1281DeadBlocks = 0;
                    int at = addr - origin;
                    int n = Math.Min(0x10, Math.Min(chunk.Length, dest.Length - at));
                    if (n > 0)
                    {
                        Buffer.BlockCopy(chunk, 0, dest, at, n);
                    }
                }
                else
                {
                    mKWP1281DeadBlocks++;
                    mHost.DisplayStatusMessage("KWP1281 block 0x" + addr.ToString("X4") + " was not answered. Filling 0xFF.", StatusMessageType.USER);
                    if (mKWP1281DeadBlocks >= 3)
                    {
                        mHost.DisplayStatusMessage("KWP1281 read stopped. The session stopped answering.", StatusMessageType.USER);
                        return false;
                    }
                }

                NoteKWP1281ReadBlock();
                if (((addr - origin) & 0x3FF) == 0)
                {
                    mHost.DisplayStatusMessage("KWP1281 read 0x" + addr.ToString("X4") + ".", StatusMessageType.LOG);
                }
            }

            return !mKWP1281ReadCancel;
        }

        private byte[] ReadKWP1281DataBlock(byte title, int addr)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (mKWP1281ReadCancel)
                {
                    return null;
                }

                mHost.mKWP1281BlockCounter++;
                var request = new KWP2000Interface.KWP1281Block();
                request.mBlockCounter = mHost.mKWP1281BlockCounter;
                request.mBlockTitle = title;
                request.mBlockData = new byte[] { 0x10, (byte)(addr >> 8), (byte)addr };
                if (!mHost.KWP1281SendBlock(request))
                {
                    continue;
                }

                KWP2000Interface.KWP1281Block response;
                if (!mHost.KWP1281ReadBlock(out response))
                {
                    continue;
                }

                mHost.mKWP1281BlockCounter = response.mBlockCounter;
                if ((response.mBlockData == null) || (response.mBlockData.Length < 0x10))
                {
                    return null;
                }

                return response.mBlockData;
            }

            return null;
        }

        // End the block dialogue and leave the app connected. The next fault
        // request slow-inits on this port. Tester present is not sent here.
        private void EndHeldKWP1281Dialogue()
        {
            SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.RequestEndCommunication);
            mHost.ClearMessageBuffers();
            mHost.mP2ECUResponseTimeOut.Reset();
            mHost.mP2ECUResponseTimeOut.Start();
            mHost.mP3TesterRequestTimeOut.Reset();
            mHost.mP3TesterRequestTimeOut.Start();
            mHost.DisplayStatusMessage("Fault exchange ended. The connection stays open.", StatusMessageType.USER);
        }

        private void CloseHeldKWP1281()
        {
            if (!mHost.IsHeldKWP1281Session)
            {
                return;
            }

            SendTitle((byte)KWP2000Interface.KWP1281BlockTitle.RequestEndCommunication);
            mHost.DisplayStatusMessage("KWP1281 session closed.", StatusMessageType.USER);
            mHost.ConnectionStatus = CommunicationInterface.ConnectionStatusType.Disconnected;
        }

        private readonly KWP2000Interface mHost;
    }

}

// vi: set sw=4 ts=8 expandtab:
