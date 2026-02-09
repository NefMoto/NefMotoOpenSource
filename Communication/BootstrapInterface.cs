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
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using Shared;

namespace Communication
{
    /// <summary>
    /// Bootmode protocol for ME7/Simos3/EDC15 ECUs. Reference: C167BootTool/ME7BootTool.py (https://github.com/EcuProg7/C167BootTool, not in this repo).
    /// Connection sequence: Send 0x00 -> receive device ID -> if != 0xAA upload loader (32B) + runtime -> C_TEST_COMM.
    /// C# does not send 0x55 autobaud; MiniMon runs at host baud (9600, 19200, 38400, 57600). Default 57600; CH340: use 38400 if 57600 fails.
    /// </summary>
    public class BootstrapInterface : CommunicationInterface
    {
        private enum CommunicationConstants : byte
        {
            //--------------------- Initialisation Acknowledges --------------

            I_LOADER_STARTED        =   0x001,  //Loader successfully launched
            I_APPLICATION_LOADED	=	0x002,	//Application succ. loaded
            I_APPLICATION_STARTED	=	0x003,	//Application succ. launched
            I_AUTOBAUD_ACKNOWLEDGE  =   0x004,  //Autobaud detection acknowledge

            //---------------------  Function Codes ----------------------------

            C_WRITE_BLOCK		    =   0x084,	//Write memory block to target mem
            C_READ_BLOCK		    =	0x085,	//Read memory block from target mem
            C_EINIT			        =	0x031,	//Execute Einit Command, no params, no return values
            C_SWRESET		        =	0x032,	//Execute Software Reset
            C_GO			        =	0x041,  //Jump to user program
            C_GETCHECKSUM		    =	0x033,  //get checksum of previous sent block
            C_TEST_COMM		        =	0x093,	//Test communication
            C_CALL_FUNCTION		    =	0x09F,	//Mon. extension interface: call driver
            C_WRITE_WORD		    =	0x082,	//write word to memory/register
            C_MON_EXT		        =	0x09F,	//call driver routine
            C_ASC1_CON		        =	0x0CC,	//Connection over ASC1
            C_READ_WORD		        =	0x0CD,	//Read Word from memory/register
            C_WRITE_WORD_SEQENCE	=	0x0CE,	//Write Word Sequence
            C_CALL_MEMTOOL_DRIVER	=	0x0CF,	//Call memtool driver routine
            C_AUTOBAUD		        =	0x0D0,	//Call autobaud routine
            C_SETPROTECTION  	    =	0x0D1,	//Call security function

            //---------------------  MiniMon Acknowledges --------------------

            A_ACK1			        =	0x0AA,  //1st Acknowledge to function code
            A_ACK2			        =	0x0EA,	//2nd Acknowledge (last byte)
            A_ASC1_CON_INIT		    =	0x0CA,	//ASC1 Init OK
            A_ASC1_CON_OK		    =	0x0D5,	//ASC1 Connection OK

            //--------------------- Error Values -----------------------------

            E_WRITE			        =	0x011,	//Write Error

            //---------------------  Function Codes ----------------------------

            FC_PROG					=	0x000,	//Program Flash
            FC_ERASE				=	0x001,	//Erase Flash
            FC_SETTIMING			=	0x003,	//Set Timing
            FC_GETSTATE				=	0x006,	//Get State
            FC_LOCK					=	0x010,	//Lock Flash bank
            FC_UNLOCK				=	0x011,	//Unlock Flash bank
            FC_PROTECT				=	0x020,	//Protect entire Flash
            FC_UNPROTECT			=	0x021,	//Unprotect Flash
            FC_BLANKCHECK			=	0x034,	//OTP/ Flash blankcheck
            FC_GETID				=	0x035,	//Get Manufacturer ID/ Device ID

            //--------------------- Function Error Values -----------------------------

            FE_NOERROR				=	0x000,	//No error
            FE_UNKNOWN_FC			=	0x001,	//Unknown function code
            FE_PROG_NO_VPP			=	0x010,	//No VPP while programming
            FE_PROG_FAILED			=	0x011,	//Programming failed
            FE_PROG_VPP_NOT_CONST	=	0x012,	//VPP not constant while programming
            FE_INVALID_BLOCKSIZE	=	0x01B,	//Invalid blocksize
            FE_INVALID_DEST_ADDR	=   0x01C,	//Invalid destination address
            FE_ERASFE_NO_VPP		=	0x030,	//No VPP while erasing
            FE_ERASFE_FAILED		=	0x031,	//Erasing failed
            FE_ERASFE_VPP_NOT_CONST	=	0x032,	//VPP not constant while erasing
            FE_INVALID_SECTOR		=	0x033,	//Invalid sector number
            FE_Sector_LOCKED		=	0x034,	//Sector locked
            FE_FLASH_PROTECTED		=	0x035,	//Flash protected
        }

        public enum DeviceIDTypes : byte
        {
            C166 = 0x55, //8xC166.
            PreC166 = 0xA5, //Previous versions of the C167 (obsolete).
            PreC165 = 0xB5, //Previous versions of the C165.
            C167 = 0xC5, //C167 derivatives.
            Other = 0xD5, //All devices equipped with identification registers.
        }

        public enum ECUFlashVariant
        {
            ME7,        // ME7.x.x variant - 0x800000 base address
            Simos3,     // Simos 3.x variant - 0x400000 base address
            EDC15       // EDC15 variant - 0x400000 base address (top boot)
        }

        // Register addresses
        private const uint SYSCON_Addr = 0x00FF12;
        private const uint BUSCON0_Addr = 0x00FF0C;
        private const uint BUSCON1_Addr = 0x00FF14;
        private const uint BUSCON2_Addr = 0x00FF16;
        private const uint BUSCON3_Addr = 0x00FF18;
        private const uint BUSCON4_Addr = 0x00FF1A;
        private const uint ADDRSEL1_Addr = 0x00FE18;
        private const uint ADDRSEL2_Addr = 0x00FE1A;
        private const uint ADDRSEL3_Addr = 0x00FE1C;
        private const uint ADDRSEL4_Addr = 0x00FE1E;

        // Register values for external flash read (ME7 variant)
        private const ushort SYSCON_Data_ext = 0xE204;  // ROM mapped at >0x000000, ROMEN=0, BYTDIS=1
        private const ushort BUSCON0_Data = 0x04AD;     // 2 wait states, 16-bit demux bus, external bus active
        private const ushort BUSCON0_WriteData = 0x04AD;  // Same as BUSCON0_Data for ME7 write (Python)
        private const ushort BUSCON1_Data = 0x040D;    // 2 wait states, 8-bit demux bus, external bus inactive
        private const ushort BUSCON2_Data = 0x04AD;     // 2 wait states, 16-bit demux bus, external bus active
        private const ushort ADDRSEL1_Data = 0x3803;    // 32KB window starting at 0x380000
        private const ushort ADDRSEL2_Data = 0x2008;    // 1024KB window starting at 0x200000

        // Register values for Simos3 variant
        private const ushort BUSCON0_Simos3_Data = 0x44BE;
        private const ushort BUSCON1_Simos3_Data = 0x848E;
        private const ushort ADDRSEL1_Simos3_Data = 0x4008;  // 1024KB window starting at 0x400000

        // Register values for EDC15 variant
        private const ushort BUSCON3_EDC15_Data = 0x848E;
        private const ushort ADDRSEL3_EDC15_Data = 0x4008;  // 1024KB window starting at 0x400000

        // Flash address mappings
        private const uint ExtFlashAddress_ME7 = 0x800000;
        private const uint ExtFlashAddress_Simos3 = 0x400000;
        private const uint ExtFlashAddress_EDC15 = 0x400000;

        // Flash write address mappings (same as read addresses)
        private const uint ExtFlashWriteAddress_ME7 = 0x800000;
        private const uint ExtFlashWriteAddress_Simos3 = 0x400000;
        private const uint ExtFlashWriteAddress_EDC15 = 0x400000;

        // Flash driver addresses
        private const uint FlashDriverEntryPoint = 0x00F640;
        private const uint DriverCopyAddress = 0xFC00;
        private const uint DriverAddress = 0x00F600;  // Where driver is loaded

        // Flash driver function code addresses
        private const byte FC_GETSTATE_ADDR_MANUFID = 0x00;
        private const byte FC_GETSTATE_ADDR_DEVICEID = 0x01;

        // Flash device IDs (FC_GETSTATE); see GetFlashSizeFromDeviceID, GenerateMemoryLayoutFromDeviceID
        private const ushort DEV_ID_F400BB = 0x22AB;  // 512KB bottom boot
        private const ushort DEV_ID_F800BB = 0x2258;  // 1024KB bottom boot
        private const ushort DEV_ID_F400BT = 0x2223;  // 512KB top boot
        private const ushort DEV_ID_F800BT = 0x22D6;  // 1024KB top boot

        // Block size for reading/writing
        private const ushort DefaultBlockLength = 0x200;  // 512 bytes

        public byte DeviceID
        {
            get
            {
                return _DeviceID;
            }
            set
            {
                if (_DeviceID != value)
                {
                    _DeviceID = value;

                    // Store the actual device ID (not 0xAA) for later use
                    const byte CORE_ALREADY_RUNNING = 0xAA;
                    if (value != CORE_ALREADY_RUNNING)
                    {
                        LastKnownDeviceID = value;
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("DeviceID"));
                }
            }
        }
        private byte _DeviceID = 0;

        /// <summary>
        /// The last known actual device ID (excluding 0xAA status code).
        /// This is stored when we get a real device ID and can be used when core is already running.
        /// </summary>
        public byte LastKnownDeviceID
        {
            get
            {
                return _LastKnownDeviceID;
            }
            private set
            {
                if (_LastKnownDeviceID != value)
                {
                    _LastKnownDeviceID = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("LastKnownDeviceID"));
                }
            }
        }
        private byte _LastKnownDeviceID = 0;

        /// <summary>
        /// The last known flash device ID (from FC_GETSTATE).
        /// This is stored when we successfully read the flash device ID and can be used when core is already running.
        /// </summary>
        public ushort LastKnownFlashDeviceID
        {
            get
            {
                return _LastKnownFlashDeviceID;
            }
            private set
            {
                if (_LastKnownFlashDeviceID != value)
                {
                    _LastKnownFlashDeviceID = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("LastKnownFlashDeviceID"));
                }
            }
        }
        private ushort _LastKnownFlashDeviceID = 0;

        // Layout cache: updated by GetBootmodeFlashLayout on success (layout) or failure (error); cleared by ResetBootmodeConnectionState on disconnect.
        private MemoryLayout mLastBootmodeLayout;
        private string mLastBootmodeLayoutError;

        /// <summary>
        /// Bootmode connection state. Single source for UI bindings and layout resolution.
        /// ConnectionPhase: ConnectionStatusType. CoreStatus: derived from DeviceID (0xAA = AlreadyRunning).
        /// FlashLayoutStatus: Unknown until GetBootmodeFlashLayout runs; Available/Unavailable from layout cache.
        /// FlashLayout non-null when Available; FlashLayoutUnavailableReason non-null when Unavailable.
        /// Variant is ME7 (hardcoded); future: from ECU info.
        /// </summary>
        public struct BootmodeConnectionState
        {
            public ConnectionStatusType ConnectionPhase;
            public BootmodeCoreStatus CoreStatus;
            public byte DeviceID;
            public byte LastKnownDeviceID;
            public ushort LastKnownFlashDeviceID;
            public ECUFlashVariant Variant;
            public BootmodeFlashLayoutStatus FlashLayoutStatus;
            public MemoryLayout FlashLayout;           // Non-null when FlashLayoutStatus == Available
            public string FlashLayoutUnavailableReason; // Non-null when FlashLayoutStatus == Unavailable
        }

        public enum BootmodeCoreStatus
        {
            Unknown,
            Uploaded,       // We uploaded MINIMONK; DeviceID is 0x55, 0xA5, 0xC5, etc.
            AlreadyRunning  // DeviceID == 0xAA; core was already in bootmode
        }

        public enum BootmodeFlashLayoutStatus
        {
            Unknown,    // GetBootmodeFlashLayout not yet called or cache cleared
            Available,  // Layout obtained successfully
            Unavailable // GetBootmodeFlashLayout failed; see FlashLayoutUnavailableReason
        }

        /// <summary>
        /// Returns bootmode connection state. Reads layout from cache (mLastBootmodeLayout / mLastBootmodeLayoutError).
        /// Call GetBootmodeFlashLayout first to populate cache if needed.
        /// </summary>
        public BootmodeConnectionState GetBootmodeConnectionState()
        {
            const byte CORE_ALREADY_RUNNING = 0xAA;
            var state = new BootmodeConnectionState
            {
                ConnectionPhase = ConnectionStatus,
                DeviceID = _DeviceID,
                LastKnownDeviceID = _LastKnownDeviceID,
                LastKnownFlashDeviceID = _LastKnownFlashDeviceID,
                Variant = ECUFlashVariant.ME7
            };
            state.CoreStatus = (_DeviceID == CORE_ALREADY_RUNNING) ? BootmodeCoreStatus.AlreadyRunning : BootmodeCoreStatus.Uploaded;
            if (_DeviceID == 0 && ConnectionStatus != ConnectionStatusType.Connected)
            {
                state.CoreStatus = BootmodeCoreStatus.Unknown;
            }
            if (mLastBootmodeLayout != null)
            {
                state.FlashLayoutStatus = BootmodeFlashLayoutStatus.Available;
                state.FlashLayout = mLastBootmodeLayout;
            }
            else if (mLastBootmodeLayoutError != null)
            {
                state.FlashLayoutStatus = BootmodeFlashLayoutStatus.Unavailable;
                state.FlashLayoutUnavailableReason = mLastBootmodeLayoutError;
            }
            else
            {
                state.FlashLayoutStatus = BootmodeFlashLayoutStatus.Unknown;
            }
            return state;
        }

        public override Protocol CurrentProtocol
        {
            get
            {
                return CommunicationInterface.Protocol.BootMode;
            }
        }

        public bool OpenConnection(uint baudRate)
        {
            bool success = false;

            if (ConnectionStatus == ConnectionStatusType.CommunicationTerminated)
            {
                mNumConnectionAttemptsRemaining = NumConnectionAttempts;

                KillSendReceiveThread();

                if (OpenCommunicationDevice(SelectedDeviceInfo))
                {
                    if (mCommunicationDevice != null)
                    {
                        bool setupSuccess = true;
                        setupSuccess &= mCommunicationDevice.SetDataCharacteristics(DataBits.Bits8, StopBits.Bits1, Parity.None);
                        setupSuccess &= mCommunicationDevice.SetFlowControl(FlowControl.None);
                        // SetLatency is USB-specific (FTDI feature), optional for other devices like CH340
                        mCommunicationDevice.SetLatency(2);//2 ms is min, this is the max time before data must be sent from the device to the PC even if not a full block

                        //setupSuccess &= mCommunicationDevice.InTransferSize(64 * ((MAX_MESSAGE_SIZE / 64) + 1) * 10);//64 bytes is min, must be multiple of 64 (this size includes a few bytes of USB header overhead)
                        setupSuccess &= mCommunicationDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);
                        // CH340/KKL cables need DTR=0,RTS=0 (matches Python SetAdapterKKL); FTDI uses DTR=1 for self-powered
                        if (mCommunicationDevice.Type == DeviceType.CH340)
                        {
                            mCommunicationDevice.SetDTR(true);
                            Thread.Sleep(100);
                            mCommunicationDevice.SetDTR(false);
                            Thread.Sleep(100);
                            setupSuccess &= mCommunicationDevice.SetDTR(false);
                            setupSuccess &= mCommunicationDevice.SetRTS(false);
                        }
                        else
                        {
                            setupSuccess &= mCommunicationDevice.SetDTR(true);
                            setupSuccess &= mCommunicationDevice.SetRTS(false);
                        }
                        setupSuccess &= mCommunicationDevice.SetBreak(false);//set to high idle state
                        // SetBitMode is FTDI-specific (bit-bang mode), optional for other devices like CH340
                        mCommunicationDevice.SetBitMode(0xFF, BitMode.Reset); // Don't fail if not supported
                        setupSuccess &= mCommunicationDevice.SetBaudRate(baudRate);
                        setupSuccess &= mCommunicationDevice.Purge(PurgeType.RX | PurgeType.TX);

                        if (setupSuccess)
                        {
                            success = true;

                            StartSendReceiveThread(SendReceiveThread);
                        }
                        else
                        {
                            DisplayStatusMessage("Failed to setup communication device.", StatusMessageType.USER);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to create communication device instance.", StatusMessageType.USER);
                    }
                }
                else
                {
                    DisplayStatusMessage("Could not open communication device.", StatusMessageType.USER);
                }
            }

            return success;
        }

        public void CloseConnection()
        {
            ConnectionStatus = ConnectionStatusType.DisconnectionPending;

            KillSendReceiveThread();

            CloseCommunicationDevice();

            // Set to Disconnected first to show proper status message
            ConnectionStatus = ConnectionStatusType.Disconnected;

            // Then set to CommunicationTerminated to allow reconnection
            // ConnectCommand checks for CommunicationTerminated status to enable the button
            ConnectionStatus = ConnectionStatusType.CommunicationTerminated;
        }

		private const uint NumConnectionAttemptsDefaultValue = 3;
		[DefaultValue(NumConnectionAttemptsDefaultValue)]
        public uint NumConnectionAttempts
        {
            get
            {
                return _NumConnectionAttempts;
            }
            set
            {
                if (_NumConnectionAttempts != value)
                {
                    _NumConnectionAttempts = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("NumConnectionAttempts"));
                }
            }
        }
		private uint _NumConnectionAttempts = NumConnectionAttemptsDefaultValue;

        protected override uint GetConnectionAttemptsRemaining()
        {
            return 0;
        }

        bool SendBytes(byte[] data)
        {
            if (mCommunicationDevice == null)
            {
                DisplayStatusMessage("SendBytes: mCommunicationDevice is null", StatusMessageType.LOG);
                return false;
            }

            uint numBytesWritten = 0;
            // Write timeout must allow physical transmission: at 9600 baud ~1ms/byte
            uint writeTimeoutMs = (uint)Math.Max(1, data.Length * 2);
            bool success = mCommunicationDevice.Write(data, data.Length, ref numBytesWritten, writeTimeoutMs) && (numBytesWritten == data.Length);

            if (success)
            {
                // Read echo and verify it matches what was sent.
                // Read may return incomplete data; loop until we have all bytes or timeout.
                uint echoTimeoutMs = (uint)Math.Max(10, data.Length * 5);
                byte[] echoData = new byte[data.Length];
                uint numEchoBytesRead = 0;
                var totalDeadline = Stopwatch.StartNew();
                while (numEchoBytesRead < echoData.Length && totalDeadline.ElapsedMilliseconds < echoTimeoutMs)
                {
                    uint remaining = (uint)echoData.Length - numEchoBytesRead;
                    uint remainingTimeout = (uint)Math.Max(1, echoTimeoutMs - totalDeadline.ElapsedMilliseconds);
                    byte[] chunk = new byte[remaining];
                    uint chunkRead = 0;
                    if (!mCommunicationDevice.Read(chunk, remaining, ref chunkRead, remainingTimeout))
                        break;
                    if (chunkRead > 0)
                    {
                        Array.Copy(chunk, 0, echoData, (int)numEchoBytesRead, (int)chunkRead);
                        numEchoBytesRead += chunkRead;
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }
                bool readOk = numEchoBytesRead > 0;
                if (readOk && (numEchoBytesRead == data.Length))
                {
                    // Verify each byte of echo matches what was sent
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] != echoData[i])
                        {
                            DisplayStatusMessage($"Echo error: sent 0x{data[i]:X2}, received 0x{echoData[i]:X2} at position {i}", StatusMessageType.LOG);
                            return false;
                        }
                    }
                }
                else
                {
                    DisplayStatusMessage($"Echo error: expected {data.Length} bytes, received {numEchoBytesRead}", StatusMessageType.LOG);
                    return false;
                }
            }

            if (!success)
                DisplayStatusMessage($"SendBytes: Write failed or partial write (requested {data.Length}, wrote {numBytesWritten})", StatusMessageType.LOG);
            return success;
        }

        bool ReceiveBytes(uint numBytes, out byte[] data)
        {
            data = new byte[numBytes];

            if (mCommunicationDevice == null)
            {
                data = null;
                DisplayStatusMessage("ReceiveBytes: mCommunicationDevice is null", StatusMessageType.LOG);
                return false;
            }

            // Read may return incomplete data; loop until we have all bytes or timeout.
            // Minimum 1000ms so CH340 has time for ECU responses.
            uint readTimeout = (uint)Math.Max(1000, numBytes * 3);
            uint numBytesRead = 0;
            var totalDeadline = Stopwatch.StartNew();
            while (numBytesRead < numBytes && totalDeadline.ElapsedMilliseconds < readTimeout)
            {
                uint remaining = numBytes - numBytesRead;
                uint remainingTimeout = (uint)Math.Max(1, readTimeout - totalDeadline.ElapsedMilliseconds);
                byte[] chunk = new byte[remaining];
                uint chunkRead = 0;
                if (!mCommunicationDevice.Read(chunk, remaining, ref chunkRead, remainingTimeout))
                {
                    DisplayStatusMessage($"ReceiveBytes: [VERIFY] Read returned false, got {numBytesRead}/{numBytes}, device={mCommunicationDevice.Type}", StatusMessageType.LOG);
                    break;
                }
                if (chunkRead > 0)
                {
                    Array.Copy(chunk, 0, data, (int)numBytesRead, (int)chunkRead);
                    numBytesRead += chunkRead;
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            if (numBytesRead != numBytes)
            {
                DisplayStatusMessage($"ReceiveBytes: [VERIFY] FAILED got {numBytesRead}/{numBytes} in {totalDeadline.ElapsedMilliseconds}ms (timeout={readTimeout}ms, device={mCommunicationDevice.Type})", StatusMessageType.LOG);
                data = null;
                return false;
            }

            return true;
        }

        bool UploadBootstrapLoader(byte[] loaderProgram, out byte deviceID)
        {
            Debug.Assert(loaderProgram.Length == 32);
            const int NUM_CONNECTION_ATTEMPTS = 3;

            deviceID = 0;

            DisplayStatusMessage("Starting bootstrap loader upload.", StatusMessageType.USER);

            uint connectionCount = 0;
            byte[] deviceIDData = null;

            while ((connectionCount < NUM_CONNECTION_ATTEMPTS) && (deviceIDData == null))
            {
                byte[] zeroByte = { 0 };
                if (SendBytes(zeroByte))
                {
                    DisplayStatusMessage("Sent bootstrap init zero byte.", StatusMessageType.USER);
                }
                else
                {
                    DisplayStatusMessage("Bootstrap loader upload failed. Failed to send init zero byte.", StatusMessageType.USER);
                    return false;
                }

                var deviceIDWaitTime = new Stopwatch();
                deviceIDWaitTime.Start();

                do
                {
                    if (!ReceiveBytes(1, out deviceIDData))
                    {
                        deviceIDData = null;
                    }
                } while ((deviceIDData == null) && (deviceIDWaitTime.ElapsedMilliseconds < 2000));

                connectionCount++;
            }

            if (deviceIDData != null)
            {
                DisplayStatusMessage("Received device ID response for init zero byte.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootstrap loader upload failed. Failed to receive device ID response for init zero byte.", StatusMessageType.USER);
                return false;
            }

            deviceID = deviceIDData[0];

            if (SendBytes(loaderProgram))
            {
                DisplayStatusMessage("Successfully uploaded bootstrap loader.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootstrap loader upload failed. Failed to send bootstrap loader data.", StatusMessageType.USER);
                return false;
            }

            return true;
        }

        bool UploadMiniMonBootstrapLoader(byte[] loaderProgram, out byte deviceID)
        {
            if (UploadBootstrapLoader(loaderProgram, out deviceID))
            {
                byte[] loaderResult;
                if (ReceiveBytes(1, out loaderResult) && (loaderResult[0] == (byte)CommunicationConstants.I_LOADER_STARTED))
                {
                    DisplayStatusMessage("Received bootstrap loader started status message.", StatusMessageType.USER);

                    return true;
                }
                else
                {
                    DisplayStatusMessage("Failed to receive bootstrap loader started status message.", StatusMessageType.USER);
                }
            }

            DisplayStatusMessage("UploadBootstrapLoader: failed after retries or invalid response", StatusMessageType.LOG);
            return false;
        }

        bool UploadMiniMonProgram(byte[] miniMonProgram)
        {
            DisplayStatusMessage("Starting upload of bootmode runtime.", StatusMessageType.USER);

            if (SendBytes(miniMonProgram))
            {
                DisplayStatusMessage("Uploaded bootmode runtime data.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootmode runtime upload failed. Failed to send bootmode runtime data.", StatusMessageType.USER);
                return false;
            }

            byte[] autoBaudPattern = { 0x55 };
            if (SendBytes(autoBaudPattern))
            {
                DisplayStatusMessage("Sent baud rate detection byte.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootmode runtime upload failed. Failed to send baud rate detection byte.", StatusMessageType.USER);
                return false;
            }

            byte[] autoBaudAck;
            if (ReceiveBytes(1, out autoBaudAck) && (autoBaudAck[0] == (byte)CommunicationConstants.I_AUTOBAUD_ACKNOWLEDGE))
            {
                DisplayStatusMessage("Received baud rate detection response message.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootmode runtime upload failed. Failed to receive baud rate detection response message.", StatusMessageType.USER);
                return false;
            }

            byte[] programStatus;
            if (ReceiveBytes(1, out programStatus) && (programStatus[0] == (byte)CommunicationConstants.I_APPLICATION_STARTED))
            {
                DisplayStatusMessage("Received bootmode runtime started status message.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Bootmode runtime upload failed. Failed to receive bootmode runtime started status message.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("Successfully uploaded bootmode runtime.", StatusMessageType.USER);
            return true;
        }

        // Connection protocol (see BOOTMODE.md, ME7BootTool.py): 1) Send 0x00 2) Receive device ID 3) If != 0xAA: upload loader (BootmodeLoader), wait I_LOADER_STARTED, upload runtime (BootmodeMiniMon), wait I_APPLICATION_STARTED 4) C_TEST_COMM
        bool StartMiniMon()
        {
            // Load binary resources
            byte[] miniMonLoader = Properties.Resources.BootmodeLoader;
            byte[] miniMonProgram = Properties.Resources.BootmodeMiniMon;

            if (miniMonLoader == null || miniMonLoader.Length == 0)
            {
                DisplayStatusMessage("Failed to load bootmode loader from resources.", StatusMessageType.USER);
                return false;
            }

            if (miniMonProgram == null || miniMonProgram.Length == 0)
            {
                DisplayStatusMessage("Failed to load bootmode runtime from resources.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("Initializing bootmode connection...", StatusMessageType.USER);

            // Send init byte to check device status (say hello)
            byte[] zeroByte = { 0 };
            if (!SendBytes(zeroByte))
            {
                DisplayStatusMessage("Failed to send bootstrap init zero byte.", StatusMessageType.USER);
                return false;
            }

            byte[] deviceIDData;
            if (!ReceiveBytes(1, out deviceIDData))
            {
                DisplayStatusMessage("Failed to receive device ID response. Make sure device is in bootmode.", StatusMessageType.USER);
                return false;
            }

            byte deviceID = deviceIDData[0];
            DeviceID = deviceID;

            DisplayStatusMessage($"Received device ID: 0x{deviceID:X2}", StatusMessageType.USER);

            // 0xAA means core is already running, skip loader/core upload
            const byte CORE_ALREADY_RUNNING = 0xAA;
            if (deviceID == CORE_ALREADY_RUNNING)
            {
                DisplayStatusMessage("Bootmode runtime is already running. Skipping upload.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Uploading bootmode loader and runtime...", StatusMessageType.USER);

                // Upload loader (32 bytes)
                DisplayStatusMessage($"Uploading loader ({miniMonLoader.Length} bytes)...", StatusMessageType.USER);
                if (!SendBytes(miniMonLoader))
                {
                    DisplayStatusMessage("Failed to send bootstrap loader data.", StatusMessageType.USER);
                    return false;
                }

                // Wait for loader started confirmation
                byte[] loaderResult;
                if (!ReceiveBytes(1, out loaderResult) || (loaderResult[0] != (byte)CommunicationConstants.I_LOADER_STARTED))
                {
                    DisplayStatusMessage($"Failed to receive loader started confirmation. Expected 0x{(byte)CommunicationConstants.I_LOADER_STARTED:X2}, got 0x{(loaderResult != null && loaderResult.Length > 0 ? loaderResult[0] : 0):X2}", StatusMessageType.USER);
                    return false;
                }
                DisplayStatusMessage("Loader started successfully.", StatusMessageType.USER);

                // Upload core runtime
                DisplayStatusMessage($"Uploading bootmode runtime ({miniMonProgram.Length} bytes)...", StatusMessageType.USER);
                if (!SendBytes(miniMonProgram))
                {
                    DisplayStatusMessage("Failed to send bootmode runtime data.", StatusMessageType.USER);
                    return false;
                }

                // Wait for application started confirmation (I_APPLICATION_STARTED = 0x03)
                byte[] programStatus;
                if (!ReceiveBytes(1, out programStatus))
                {
                    DisplayStatusMessage("Failed to receive application started confirmation.", StatusMessageType.USER);
                    return false;
                }

                if (programStatus[0] != (byte)CommunicationConstants.I_APPLICATION_STARTED)
                {
                    DisplayStatusMessage($"Unexpected response after core upload. Expected 0x{(byte)CommunicationConstants.I_APPLICATION_STARTED:X2} (I_APPLICATION_STARTED), got 0x{programStatus[0]:X2}", StatusMessageType.USER);
                    return false;
                }
                DisplayStatusMessage("Bootmode runtime started successfully.", StatusMessageType.USER);
            }

            // Test communication to verify connection
            DisplayStatusMessage("Testing communication...", StatusMessageType.USER);
            if (!MiniMon_TestCommunication())
            {
                DisplayStatusMessage("Communication test failed.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("Communication test successful. Bootmode connection established.", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Test reading registers to verify MINIMON is responding correctly.
        /// This is a lightweight test that reads accessible registers without requiring setup.
        /// </summary>
        public bool TestReadRegisters()
        {
            DisplayStatusMessage("Testing register read operations...", StatusMessageType.USER);

            // Test 1: Read SYSCON register (0x00FF12) - always accessible
            const uint SYSCON_Address = 0x00FF12;
            uint sysconValue;
            if (MiniMon_ReadWord(SYSCON_Address, out sysconValue))
            {
                DisplayStatusMessage($"SYSCON register (0x{SYSCON_Address:X6}) = 0x{sysconValue:X4}", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Failed to read SYSCON register.", StatusMessageType.USER);
                return false;
            }

            // Test 2: Read Port 2 register (0xFFC0) - always accessible
            const uint Port2_Address = 0xFFC0;
            uint port2Value;
            if (MiniMon_ReadWord(Port2_Address, out port2Value))
            {
                DisplayStatusMessage($"Port 2 register (0x{Port2_Address:X6}) = 0x{port2Value:X4}", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Failed to read Port 2 register.", StatusMessageType.USER);
                return false;
            }

            // Test 3: Read a small block from internal RAM (0xF600 - driver area)
            const uint InternalRAM_Address = 0x00F600;
            byte[] ramData;
            if (MiniMon_ReadBlock(InternalRAM_Address, 16, out ramData))
            {
                string hexData = BitConverter.ToString(ramData).Replace("-", " ");
                DisplayStatusMessage($"Internal RAM (0x{InternalRAM_Address:X6}, 16 bytes) = {hexData}", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Failed to read internal RAM block.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("All register read tests passed successfully.", StatusMessageType.USER);
            return true;
        }

        bool MiniMon_RequestFunction(byte functionCode)
        {
            byte[] functionCodeData = { functionCode };
            if (!SendBytes(functionCodeData))
            {
                DisplayStatusMessage($"MiniMon_RequestFunction: SendBytes failed for function 0x{functionCode:X2}", StatusMessageType.LOG);
                return false;
            }

            byte[] functionCodeAck;
            if (!ReceiveBytes(1, out functionCodeAck))
            {
                DisplayStatusMessage($"MiniMon_RequestFunction: ReceiveBytes(1) failed for function 0x{functionCode:X2}", StatusMessageType.LOG);
                return false;
            }
            if (functionCodeAck[0] != (byte)CommunicationConstants.A_ACK1)
            {
                DisplayStatusMessage($"MiniMon_RequestFunction: wrong ack for function 0x{functionCode:X2}, got 0x{functionCodeAck[0]:X2} (expected A_ACK1=0x{(byte)CommunicationConstants.A_ACK1:X2})", StatusMessageType.LOG);
                return false;
            }

            return true;
        }

        bool MiniMon_GetFunctionRequestAck(out byte result)
        {
            result = (byte)CommunicationConstants.A_ACK2;

            byte[] functionFinishedAck;
            if (!ReceiveBytes(1, out functionFinishedAck))
            {
                DisplayStatusMessage("MiniMon_GetFunctionRequestAck: [VERIFY] ReceiveBytes(1) failed", StatusMessageType.LOG);
                return false;
            }

            result = functionFinishedAck[0];
            return true;
        }

        bool MiniMon_WasFunctionRequestSuccessful()
        {
            byte result;
            if (!MiniMon_GetFunctionRequestAck(out result))
                return false;
            if (result != (byte)CommunicationConstants.A_ACK2)
            {
                DisplayStatusMessage($"MiniMon_WasFunctionRequestSuccessful: wrong ack 0x{result:X2} (expected A_ACK2=0xEA), device={mCommunicationDevice?.Type}", StatusMessageType.LOG);
                return false;
            }
            return true;
        }

        bool MiniMon_EndInitialization()
        {
            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_EINIT))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_TestCommunication()
        {
            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_TEST_COMM))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_SoftwareReset()
        {
            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_SWRESET))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_GetChecksum(out byte checksum)
        {
            checksum = 0;

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_GETCHECKSUM))
            {
                return false;
            }

            byte[] checksumData;
            if (!ReceiveBytes(1, out checksumData))
            {
                return false;
            }

            checksum = checksumData[0];

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_WriteBlock(uint address, byte[] data, out byte functionResult)
        {
            Debug.Assert((0xFF000000 & address) == 0);
            Debug.Assert((0xFFFF0000 & data.Length) == 0);

            functionResult = (byte)CommunicationConstants.A_ACK2;

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_WRITE_BLOCK))
            {
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                return false;
            }

            byte[] sizeBytes = new byte[2];
            sizeBytes[0] = (byte)(data.Length & 0xFF);
            sizeBytes[1] = (byte)(data.Length >> 8 & 0xFF);

            if (!SendBytes(sizeBytes))
            {
                return false;
            }

            if (!SendBytes(data))
            {
                return false;
            }

            if (!MiniMon_GetFunctionRequestAck(out functionResult) || (functionResult != (byte)CommunicationConstants.A_ACK2))
            {
                DisplayStatusMessage($"MiniMon_WriteBlock: wrong ack 0x{functionResult:X2} (expected 0xEA) at 0x{address:X6}", StatusMessageType.LOG);
                return false;
            }

            return true;
        }

        public bool MiniMon_ReadBlock(uint address, ushort numBytes, out byte[] data)
        {
            Debug.Assert((0xFF000000 & address) == 0);
            Debug.Assert((0xFFFF0000 & numBytes) == 0);

            data = null;

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_READ_BLOCK))
            {
                DisplayStatusMessage("MiniMon_ReadBlock: Failed to request C_READ_BLOCK function", StatusMessageType.LOG);
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                DisplayStatusMessage("MiniMon_ReadBlock: Failed to send address bytes", StatusMessageType.LOG);
                return false;
            }

            byte[] sizeBytes = new byte[2];
            sizeBytes[0] = (byte)(numBytes & 0xFF);
            sizeBytes[1] = (byte)(numBytes >> 8 & 0xFF);

            if (!SendBytes(sizeBytes))
            {
                DisplayStatusMessage("MiniMon_ReadBlock: Failed to send size bytes", StatusMessageType.LOG);
                return false;
            }

            if (!ReceiveBytes((uint)numBytes, out data))
            {
                DisplayStatusMessage($"MiniMon_ReadBlock: Failed to receive {numBytes} bytes of data", StatusMessageType.LOG);
                return false;
            }

            if (data == null || data.Length != numBytes)
            {
                DisplayStatusMessage($"MiniMon_ReadBlock: Received data length mismatch: expected {numBytes}, got {(data?.Length ?? 0)}", StatusMessageType.LOG);
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                DisplayStatusMessage("MiniMon_ReadBlock: Function request was not successful", StatusMessageType.LOG);
                return false;
            }

            byte receivedChecksum;
            if (!MiniMon_GetChecksum(out receivedChecksum))
            {
                DisplayStatusMessage("MiniMon_ReadBlock: Failed to get checksum from ECU", StatusMessageType.LOG);
                return false;
            }

            // Calculate checksum (XOR of all bytes)
            byte calculatedChecksum = 0;
            foreach (byte b in data)
            {
                calculatedChecksum = (byte)(calculatedChecksum ^ b);
            }

            if (calculatedChecksum != receivedChecksum)
            {
                DisplayStatusMessage($"MiniMon_ReadBlock: Checksum mismatch at address 0x{address:X6}! Received: 0x{receivedChecksum:X2}, Calculated: 0x{calculatedChecksum:X2}", StatusMessageType.USER);
                DisplayStatusMessage($"MiniMon_ReadBlock: Checksum verification failed - data may be corrupted", StatusMessageType.LOG);
                return false;
            }

            return true;
        }

        bool MiniMon_Go(uint address)
        {
            Debug.Assert((0xFF000000 & address) == 0);

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_GO))
            {
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_WriteWord(uint address, ushort word)
        {
            Debug.Assert((0xFF000000 & address) == 0);
            Debug.Assert((0xFFFF0000 & word) == 0);

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_WRITE_WORD))
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Failed to request C_WRITE_WORD function for address 0x{address:X6}", StatusMessageType.LOG);
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Failed to send address bytes for 0x{address:X6}", StatusMessageType.LOG);
                return false;
            }

            byte[] wordBytes = new byte[2];
            wordBytes[0] = (byte)(word & 0xFF);
            wordBytes[1] = (byte)(word >> 8 & 0xFF);

            if (!SendBytes(wordBytes))
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Failed to send word bytes for 0x{word:X4}", StatusMessageType.LOG);
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Function request failed for address 0x{address:X6}", StatusMessageType.LOG);
                return false;
            }

            uint readBackValue;
            if (!MiniMon_ReadWord(address, out readBackValue))
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Failed to read back value from 0x{address:X6}", StatusMessageType.LOG);
                return false;
            }

            ushort readBackWord = (ushort)(readBackValue & 0xFFFF);
            if (readBackWord != word)
            {
                DisplayStatusMessage($"MiniMon_WriteWord: Write verification failed at 0x{address:X6}! Wrote: 0x{word:X4}, Read back: 0x{readBackWord:X4}", StatusMessageType.LOG);
                return false;
            }

            return true;
        }

        public bool MiniMon_ReadWord(uint address, out uint word)
        {
            Debug.Assert((0xFF000000 & address) == 0);
            word = 0;

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_READ_WORD))
            {
                DisplayStatusMessage("MiniMon_ReadWord: Failed to request C_READ_WORD function", StatusMessageType.LOG);
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                DisplayStatusMessage("MiniMon_ReadWord: Failed to send address bytes", StatusMessageType.LOG);
                return false;
            }

            byte[] wordData;
            if (!ReceiveBytes(2, out wordData))
            {
                DisplayStatusMessage("MiniMon_ReadWord: Failed to receive word data", StatusMessageType.LOG);
                return false;
            }

            word = wordData[1];
            word <<= 8;
            word |= wordData[0];

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                DisplayStatusMessage("MiniMon_ReadWord: request not successful", StatusMessageType.LOG);
                return false;
            }

            return true;
        }

        bool MiniMon_Call(uint address, byte[] registerParams, out byte[] registerResults)
        {
            Debug.Assert((0xFF000000 & address) == 0);
            Debug.Assert(registerParams.Length == 16);
            registerResults = null;

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_CALL_FUNCTION))
            {
                DisplayStatusMessage("MiniMon_Call: Failed to request C_CALL_FUNCTION function", StatusMessageType.LOG);
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(address & 0xFF);
            addressBytes[1] = (byte)(address >> 8 & 0xFF);
            addressBytes[2] = (byte)(address >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                return false;
            }

            if (!SendBytes(registerParams))
            {
                return false;
            }

            if (!ReceiveBytes(16, out registerResults))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        bool MiniMon_WriteWordSequence(uint sequenceAddress, ushort numSequenceEntries)
        {
            Debug.Assert((0xFF000000 & sequenceAddress) == 0);
            Debug.Assert((0xFFFF0000 & numSequenceEntries) == 0);

            if (!MiniMon_RequestFunction((byte)CommunicationConstants.C_WRITE_WORD_SEQENCE))
            {
                return false;
            }

            byte[] addressBytes = new byte[3];
            addressBytes[0] = (byte)(sequenceAddress & 0xFF);
            addressBytes[1] = (byte)(sequenceAddress >> 8 & 0xFF);
            addressBytes[2] = (byte)(sequenceAddress >> 16 & 0xFF);

            if (!SendBytes(addressBytes))
            {
                return false;
            }

            byte[] numEntriesBytes = new byte[2];
            numEntriesBytes[0] = (byte)(numSequenceEntries & 0xFF);
            numEntriesBytes[1] = (byte)(numSequenceEntries >> 8 & 0xFF);

            if (!SendBytes(numEntriesBytes))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
                return false;
            }

            return true;
        }

        protected void SendReceiveThread()
        {
            DisplayStatusMessage("Send receive thread now started.", StatusMessageType.LOG);
            ConnectionStatus = ConnectionStatusType.Disconnected;

            while ((ConnectionStatus != ConnectionStatusType.Disconnected)
                && (ConnectionStatus != ConnectionStatusType.DisconnectionPending)
                && (ConnectionStatus != ConnectionStatusType.CommunicationTerminated)
                || (mNumConnectionAttemptsRemaining > 0))
            {
                // Check for disconnection request first
                if (ConnectionStatus == ConnectionStatusType.DisconnectionPending)
                {
                    DisplayStatusMessage("Disconnection requested, exiting thread.", StatusMessageType.LOG);
                    break;
                }

                if (mCommunicationDevice != null)
                {
                    bool shouldAttemptConnection = false;

                    // Check if we should attempt connection (minimal lock time)
                    lock (mCommunicationDevice)
                    {
                        shouldAttemptConnection = (ConnectionStatus == ConnectionStatusType.Disconnected) && (mNumConnectionAttemptsRemaining > 0);
                        if (shouldAttemptConnection)
                        {
                            mNumConnectionAttemptsRemaining--;
                            ConnectionStatus = ConnectionStatusType.ConnectionPending;
                        }
                    }

                    if (shouldAttemptConnection)
                    {
                        // Perform connection attempt OUTSIDE the lock to avoid blocking other threads
                        // This prevents the lock from being held during blocking I/O operations
                        bool connected = StartMiniMon();

                        // Check again for disconnection request after potentially long I/O operation
                        if (ConnectionStatus == ConnectionStatusType.DisconnectionPending)
                        {
                            DisplayStatusMessage("Disconnection requested during connection attempt, exiting thread.", StatusMessageType.LOG);
                            break;
                        }

                        // Update status after connection attempt
                        if (!connected)
                        {
                            ConnectionStatus = ConnectionStatusType.Disconnected;
                        }
                        else
                        {
                            ConnectionStatus = ConnectionStatusType.Connected;

                            lock (mCommunicationDevice)
                            {
                                mNumConnectionAttemptsRemaining = 0;
                            }
                        }
                    }
                }

                // Once connected, reduce CPU usage by sleeping longer
                // Bootmode doesn't need continuous message handling like KWP2000
                if (ConnectionStatus == ConnectionStatusType.Connected)
                {
                    // Sleep longer when connected to reduce CPU usage
                    // But check for disconnection more frequently
                    for (int i = 0; i < 10; i++)
                    {
                        if (ConnectionStatus == ConnectionStatusType.DisconnectionPending)
                        {
                            break;
                        }
                        Thread.Sleep(10);
                    }
                }
                else
                {
                    // Shorter sleep when attempting connection
                    Thread.Sleep(10);
                }
            }

            // Only set to CommunicationTerminated if CloseConnection hasn't already done it
            // This prevents race conditions where CloseConnection sets it after the thread exits
            if (ConnectionStatus != ConnectionStatusType.CommunicationTerminated)
            {
                ConnectionStatus = ConnectionStatusType.CommunicationTerminated;
            }

            // Only close device if CloseConnection hasn't already done it
            // CloseConnection() will handle device cleanup
            if (mCommunicationDevice != null)
            {
                try
                {
                    CloseCommunicationDevice();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            mSendReceiveThread = null;

            DisplayStatusMessage("Send receive thread now terminated.", StatusMessageType.LOG);
        }

        public override ConnectionStatusType ConnectionStatus
        {
            get
            {
                return _ConnectionStatus;
            }

            set
            {
                if (_ConnectionStatus != value)
                {
                    bool wasConnected = IsConnectionOpen() && (_ConnectionStatus != ConnectionStatusType.ConnectionPending);
                    bool shouldReset = false;

                    base.ConnectionStatus = value;

                    if (_ConnectionStatus == ConnectionStatusType.ConnectionPending)
                    {
                        shouldReset = true;
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Connected)
                    {
                        mNumConnectionAttemptsRemaining = 0;
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Disconnected ||
                             _ConnectionStatus == ConnectionStatusType.DisconnectionPending ||
                             _ConnectionStatus == ConnectionStatusType.CommunicationTerminated)
                    {
                        shouldReset = true;
                    }

                    if (shouldReset)
                    {
                        // Clears layout cache; FlashingControl.CommInterface_PropertyChanged clears FlashMemoryLayout on disconnect
                        ResetBootmodeConnectionState();
                        mCommunicationDevice?.Purge(PurgeType.RX | PurgeType.TX);
                    }
                }
            }
        }

        protected uint mNumConnectionAttemptsRemaining = 0;

        #region External Flash Read Operations

        /// <summary>
        /// Configures registers for external flash read operations.
        /// </summary>
        /// <param name="variant">ECU/Flash variant (ME7, Simos3, or EDC15)</param>
        /// <returns>True if configuration successful, false otherwise</returns>
        public bool ConfigureRegistersForExternalFlashRead(ECUFlashVariant variant)
        {
            DisplayStatusMessage($"Configuring registers for external flash read ({variant} variant)...", StatusMessageType.LOG);

            if (!MiniMon_WriteWord(SYSCON_Addr, SYSCON_Data_ext))
            {
                DisplayStatusMessage("Failed to configure SYSCON register for external flash.", StatusMessageType.USER);
                return false;
            }

            ushort buscon0Value = variant == ECUFlashVariant.Simos3 ? BUSCON0_Simos3_Data : BUSCON0_Data;
            if (!MiniMon_WriteWord(BUSCON0_Addr, buscon0Value))
            {
                DisplayStatusMessage("Failed to configure BUSCON0 register.", StatusMessageType.USER);
                return false;
            }

            // Configure ADDRSEL registers based on variant
            if (variant == ECUFlashVariant.ME7)
            {
                // ME7: ADDRSEL1 and ADDRSEL2
                if (!MiniMon_WriteWord(ADDRSEL1_Addr, ADDRSEL1_Data))
                {
                    DisplayStatusMessage("Failed to configure ADDRSEL1 register.", StatusMessageType.USER);
                    return false;
                }
                if (!MiniMon_WriteWord(ADDRSEL2_Addr, ADDRSEL2_Data))
                {
                    DisplayStatusMessage("Failed to configure ADDRSEL2 register.", StatusMessageType.USER);
                    return false;
                }
                // Clear ADDRSEL3 and ADDRSEL4
                if (!MiniMon_WriteWord(ADDRSEL3_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear ADDRSEL3 register.", StatusMessageType.USER);
                    return false;
                }
                if (!MiniMon_WriteWord(ADDRSEL4_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear ADDRSEL4 register.", StatusMessageType.USER);
                    return false;
                }
            }
            else if (variant == ECUFlashVariant.Simos3)
            {
                // Simos3: ADDRSEL1
                if (!MiniMon_WriteWord(ADDRSEL1_Addr, ADDRSEL1_Simos3_Data))
                {
                    DisplayStatusMessage("Failed to configure ADDRSEL1 register for Simos3.", StatusMessageType.USER);
                    return false;
                }
                // Clear other ADDRSEL registers
                if (!MiniMon_WriteWord(ADDRSEL2_Addr, 0) ||
                    !MiniMon_WriteWord(ADDRSEL3_Addr, 0) ||
                    !MiniMon_WriteWord(ADDRSEL4_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear ADDRSEL registers.", StatusMessageType.USER);
                    return false;
                }
            }
            else if (variant == ECUFlashVariant.EDC15)
            {
                // EDC15: ADDRSEL3
                if (!MiniMon_WriteWord(ADDRSEL3_Addr, ADDRSEL3_EDC15_Data))
                {
                    DisplayStatusMessage("Failed to configure ADDRSEL3 register for EDC15.", StatusMessageType.USER);
                    return false;
                }
                // Clear other ADDRSEL registers
                if (!MiniMon_WriteWord(ADDRSEL1_Addr, 0) ||
                    !MiniMon_WriteWord(ADDRSEL2_Addr, 0) ||
                    !MiniMon_WriteWord(ADDRSEL4_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear ADDRSEL registers.", StatusMessageType.USER);
                    return false;
                }
            }

            // Configure BUSCON registers
            ushort buscon1Value = variant == ECUFlashVariant.Simos3 ? BUSCON1_Simos3_Data : BUSCON1_Data;
            if (!MiniMon_WriteWord(BUSCON1_Addr, buscon1Value))
            {
                DisplayStatusMessage("Failed to configure BUSCON1 register.", StatusMessageType.USER);
                return false;
            }

            if (variant == ECUFlashVariant.ME7)
            {
                if (!MiniMon_WriteWord(BUSCON2_Addr, BUSCON2_Data))
                {
                    DisplayStatusMessage("Failed to configure BUSCON2 register.", StatusMessageType.USER);
                    return false;
                }
            }

            if (variant == ECUFlashVariant.EDC15)
            {
                if (!MiniMon_WriteWord(BUSCON3_Addr, BUSCON3_EDC15_Data))
                {
                    DisplayStatusMessage("Failed to configure BUSCON3 register for EDC15.", StatusMessageType.USER);
                    return false;
                }
            }

            // Clear unused BUSCON registers
            if (variant != ECUFlashVariant.ME7)
            {
                if (!MiniMon_WriteWord(BUSCON2_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear BUSCON2 register.", StatusMessageType.USER);
                    return false;
                }
            }
            if (variant != ECUFlashVariant.EDC15)
            {
                if (!MiniMon_WriteWord(BUSCON3_Addr, 0))
                {
                    DisplayStatusMessage("Failed to clear BUSCON3 register.", StatusMessageType.USER);
                    return false;
                }
            }
            if (!MiniMon_WriteWord(BUSCON4_Addr, 0))
            {
                DisplayStatusMessage("Failed to clear BUSCON4 register.", StatusMessageType.USER);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Configures registers for external flash write. Different from read: zeros ADDRSEL/BUSCON first, then variant-specific.
        /// Reference: ME7BootTool.py jobWriteExtFlash lines 887-919.
        /// </summary>
        public bool ConfigureRegistersForExternalFlashWrite(ECUFlashVariant variant)
        {
            if (!MiniMon_WriteWord(SYSCON_Addr, SYSCON_Data_ext))
            {
                DisplayStatusMessage("Failed to configure SYSCON register for external flash write.", StatusMessageType.USER);
                return false;
            }

            // Zero ADDRSEL and BUSCON (Python write path)
            if (!MiniMon_WriteWord(ADDRSEL1_Addr, 0) || !MiniMon_WriteWord(ADDRSEL2_Addr, 0) ||
                !MiniMon_WriteWord(ADDRSEL3_Addr, 0) || !MiniMon_WriteWord(ADDRSEL4_Addr, 0) ||
                !MiniMon_WriteWord(BUSCON1_Addr, 0) || !MiniMon_WriteWord(BUSCON2_Addr, 0) ||
                !MiniMon_WriteWord(BUSCON3_Addr, 0) || !MiniMon_WriteWord(BUSCON4_Addr, 0))
            {
                DisplayStatusMessage("Failed to clear ADDRSEL/BUSCON registers for write.", StatusMessageType.USER);
                return false;
            }

            if (variant == ECUFlashVariant.Simos3)
            {
                if (!MiniMon_WriteWord(ADDRSEL1_Addr, ADDRSEL1_Simos3_Data) ||
                    !MiniMon_WriteWord(BUSCON1_Addr, BUSCON1_Simos3_Data) ||
                    !MiniMon_WriteWord(BUSCON0_Addr, BUSCON0_Simos3_Data))
                {
                    DisplayStatusMessage("Failed to configure Simos3 write registers.", StatusMessageType.USER);
                    return false;
                }
            }
            else if (variant == ECUFlashVariant.ME7)
            {
                if (!MiniMon_WriteWord(ADDRSEL1_Addr, 0) || !MiniMon_WriteWord(BUSCON1_Addr, 0) ||
                    !MiniMon_WriteWord(BUSCON0_Addr, BUSCON0_WriteData))
                {
                    DisplayStatusMessage("Failed to configure ME7 write registers.", StatusMessageType.USER);
                    return false;
                }
            }
            else if (variant == ECUFlashVariant.EDC15)
            {
                if (!MiniMon_WriteWord(ADDRSEL3_Addr, ADDRSEL3_EDC15_Data) ||
                    !MiniMon_WriteWord(BUSCON3_Addr, BUSCON3_EDC15_Data) ||
                    !MiniMon_WriteWord(BUSCON0_Addr, BUSCON0_Data))
                {
                    DisplayStatusMessage("Failed to configure EDC15 write registers.", StatusMessageType.USER);
                    return false;
                }
            }

            DisplayStatusMessage("Registers configured successfully for external flash write.", StatusMessageType.LOG);
            return true;
        }

        /// <summary>
        /// Erases one sector via FC_ERASE. Call ConfigureRegistersForExternalFlashWrite and LoadFlashDriver first.
        /// </summary>
        /// <param name="writeAddressBase">Write base (ExtFlashWriteAddress for variant)</param>
        /// <param name="readAddressBase">Read base (ExtFlashAddress for variant)</param>
        /// <param name="sectorOffset">Byte offset of sector from flash base</param>
        /// <param name="sectorIndex">0-based sector number (passed to FC_ERASE)</param>
        /// <param name="sectorSize">Sector size in bytes</param>
        public bool EraseSector(uint writeAddressBase, uint readAddressBase, uint sectorOffset, int sectorIndex, uint sectorSize)
        {
            uint writeAddr = writeAddressBase + sectorOffset;
            uint readAddr = readAddressBase + sectorOffset;
            ushort writeAddressLow = (ushort)(writeAddr & 0xFFFF);
            ushort writeAddressHigh = (ushort)((writeAddr >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((readAddr >> 16) & 0xFFFF);
            ushort lastWordAddress = (ushort)((readAddr + sectorSize - 2) & 0xFFFF);

            byte[] regParams = new byte[16];
            PutU16LE(regParams, 0, (ushort)CommunicationConstants.FC_ERASE);
            PutU16LE(regParams, 2, writeAddressLow);
            PutU16LE(regParams, 4, writeAddressHigh);
            PutU16LE(regParams, 6, readAddressHigh);
            PutU16LE(regParams, 8, lastWordAddress);
            PutU16LE(regParams, 10, 0);
            PutU16LE(regParams, 12, (ushort)sectorIndex);
            PutU16LE(regParams, 14, 1);

            if (!MiniMon_Call(FlashDriverEntryPoint, regParams, out byte[] regResults))
            {
                DisplayStatusMessage($"EraseSector: FC_ERASE sector {sectorIndex} failed", StatusMessageType.LOG);
                return false;
            }
            ushort errWord = (ushort)(regResults[14] | (regResults[15] << 8));
            if (errWord != (ushort)CommunicationConstants.FE_NOERROR)
            {
                DisplayStatusMessage($"EraseSector: FC_ERASE sector {sectorIndex} returned error 0x{errWord:X4}", StatusMessageType.LOG);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Programs one block via FC_PROG. Writes data to DriverCopyAddress first, then calls driver.
        /// </summary>
        public bool ProgramBlock(ECUFlashVariant variant, uint writeAddressBase, uint readAddressBase, uint offset, byte[] blockData)
        {
            if (blockData == null || blockData.Length == 0 || blockData.Length > DefaultBlockLength)
            {
                DisplayStatusMessage($"ProgramBlock: invalid block size {blockData?.Length ?? 0}", StatusMessageType.LOG);
                return false;
            }

            if (!MiniMon_WriteBlock(DriverCopyAddress, blockData, out byte functionResult))
            {
                DisplayStatusMessage($"ProgramBlock: WriteBlock to 0x{DriverCopyAddress:X4} failed (result=0x{functionResult:X2})", StatusMessageType.LOG);
                return false;
            }

            uint writeAddr = writeAddressBase + offset;
            uint readAddr = readAddressBase + offset;
            ushort writesize = (ushort)blockData.Length;
            ushort writeAddressLow = (ushort)(writeAddr & 0xFFFF);
            ushort writeAddressHigh = (ushort)((writeAddr >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((readAddr >> 16) & 0xFFFF);

            byte[] regParams = new byte[16];
            PutU16LE(regParams, 0, (ushort)CommunicationConstants.FC_PROG);
            PutU16LE(regParams, 2, writesize);
            PutU16LE(regParams, 4, (ushort)(DriverCopyAddress & 0xFFFF));
            PutU16LE(regParams, 6, 0);
            PutU16LE(regParams, 8, readAddressHigh);
            PutU16LE(regParams, 10, writeAddressLow);
            PutU16LE(regParams, 12, writeAddressHigh);
            PutU16LE(regParams, 14, 1);

            if (!MiniMon_Call(FlashDriverEntryPoint, regParams, out byte[] regResults))
            {
                DisplayStatusMessage($"ProgramBlock: FC_PROG at 0x{writeAddr:X6} failed", StatusMessageType.LOG);
                return false;
            }
            ushort errWord = (ushort)(regResults[14] | (regResults[15] << 8));
            if (errWord != (ushort)CommunicationConstants.FE_NOERROR)
            {
                DisplayStatusMessage($"ProgramBlock: FC_PROG at 0x{writeAddr:X6} returned error 0x{errWord:X4}", StatusMessageType.LOG);
                return false;
            }
            return true;
        }

        private static void PutU16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)(value >> 8);
        }

        /// <summary>
        /// Lightweight check: whether bootmode flash layout can be obtained without performing I/O.
        /// Use for button enablement. Does not load driver or query flash.
        /// </summary>
        public bool CanGetBootmodeFlashLayout()
        {
            if (!IsConnected())
            {
                return false;
            }

            const byte CORE_ALREADY_RUNNING = 0xAA;
            if (DeviceID == CORE_ALREADY_RUNNING)
            {
                return LastKnownFlashDeviceID != 0;
            }

            return true;
        }

        /// <summary>
        /// Returns a user-facing reason why read/write flash is unavailable when CanGetBootmodeFlashLayout() is false.
        /// Use for button tooltips and status. Returns null when not connected or when layout is available.
        /// </summary>
        public string GetBootmodeFlashLayoutUnavailableReason()
        {
            if (!IsConnected())
            {
                return null;
            }

            const byte CORE_ALREADY_RUNNING = 0xAA;
            if (DeviceID == CORE_ALREADY_RUNNING && LastKnownFlashDeviceID == 0)
            {
                return "Disconnect, power-cycle ECU to full boot mode, then reconnect to detect flash type.";
            }

            return null;
        }

        /// <summary>
        /// Gets bootmode flash layout. Single entry point for UI, read, and write.
        /// If DeviceID == 0xAA (core running): uses LastKnownFlashDeviceID; fails if 0.
        /// Otherwise: loads driver, reads flash device ID, generates layout.
        /// Updates mLastBootmodeLayout / mLastBootmodeLayoutError on success/failure. Cache cleared by ResetBootmodeConnectionState.
        /// </summary>
        /// <param name="layout">Output layout, or null on failure</param>
        /// <param name="errorMessage">Error description on failure</param>
        /// <returns>True if layout was obtained successfully</returns>
        public bool GetBootmodeFlashLayout(out MemoryLayout layout, out string errorMessage)
        {
            layout = null;
            errorMessage = null;

            if (!IsConnected())
            {
                errorMessage = "Not connected to ECU.";
                mLastBootmodeLayout = null;
                mLastBootmodeLayoutError = errorMessage;
                return false;
            }

            ECUFlashVariant variant = ECUFlashVariant.ME7;
            uint baseAddress = GetFlashBaseAddress(variant);
            ushort deviceID = 0;
            const byte CORE_ALREADY_RUNNING = 0xAA;

            if (DeviceID == CORE_ALREADY_RUNNING)
            {
                if (LastKnownFlashDeviceID != 0)
                {
                    deviceID = LastKnownFlashDeviceID;
                    DisplayStatusMessage($"Using stored flash device ID: 0x{deviceID:X4} (core already running)", StatusMessageType.USER);
                }
                else
                {
                    errorMessage = "Core is already running and no stored flash device ID available. Disconnect, power-cycle ECU to full boot mode, reconnect to detect flash type.";
                    mLastBootmodeLayout = null;
                    mLastBootmodeLayoutError = errorMessage;
                    return false;
                }
            }
            else
            {
                DisplayStatusMessage("Loading flash driver for device identification...", StatusMessageType.USER);
                if (!LoadFlashDriver(variant))
                {
                    errorMessage = "Flash driver load failed. Check ECU variant (ME7/Simos3/EDC15).";
                    mLastBootmodeLayout = null;
                    mLastBootmodeLayoutError = errorMessage;
                    return false;
                }

                if (!GetFlashDeviceID(variant, out deviceID))
                {
                    errorMessage = "Failed to read flash device ID. Wrong variant, bad connection, or unsupported flash chip.";
                    mLastBootmodeLayout = null;
                    mLastBootmodeLayoutError = errorMessage;
                    return false;
                }
            }

            if (!GenerateMemoryLayoutFromDeviceID(deviceID, baseAddress, out layout))
            {
                errorMessage = $"Unknown flash device ID: 0x{deviceID:X4}. Cannot auto-detect layout.";
                mLastBootmodeLayout = null;
                mLastBootmodeLayoutError = errorMessage;
                return false;
            }

            mLastBootmodeLayout = layout;
            mLastBootmodeLayoutError = null;
            return true;
        }

        /// <summary>
        /// Clears connection-specific bootmode state: DeviceID and layout cache.
        /// LastKnownDeviceID and LastKnownFlashDeviceID are preserved across disconnect so that
        /// reconnect (same app session) with core already running (0xAA) can still resolve layout.
        /// Called from ConnectionStatus setter when transitioning to ConnectionPending, Disconnected, DisconnectionPending, or CommunicationTerminated.
        /// </summary>
        private void ResetBootmodeConnectionState()
        {
            mLastBootmodeLayout = null;
            mLastBootmodeLayoutError = null;
            if (_DeviceID != 0)
            {
                _DeviceID = 0;
                OnPropertyChanged(new PropertyChangedEventArgs("DeviceID"));
            }
        }

        /// <summary>
        /// Gets the base flash address for the specified ECU variant.
        /// </summary>
        public uint GetFlashBaseAddress(ECUFlashVariant variant)
        {
            switch (variant)
            {
                case ECUFlashVariant.ME7:
                    return ExtFlashAddress_ME7;
                case ECUFlashVariant.Simos3:
                    return ExtFlashAddress_Simos3;
                case ECUFlashVariant.EDC15:
                    return ExtFlashAddress_EDC15;
                default:
                    return ExtFlashAddress_ME7;  // Default to ME7
            }
        }

        /// <summary>
        /// Reads external flash memory.
        /// </summary>
        /// <param name="variant">ECU/Flash variant (ME7, Simos3, or EDC15)</param>
        /// <param name="startAddress">Start address offset from flash base (0-based)</param>
        /// <param name="size">Number of bytes to read</param>
        /// <param name="data">Output buffer for read data</param>
        /// <param name="progressCallback">Optional callback for progress reporting (bytesRead, totalBytes)</param>
        /// <param name="sectorNumber">Current sector number (for progress display, 0 to skip)</param>
        /// <param name="totalSectors">Total number of sectors (for progress display, 0 to skip)</param>
        /// <returns>True if read successful, false otherwise</returns>
        public bool ReadExternalFlash(ECUFlashVariant variant, uint startAddress, uint size, out byte[] data, Action<uint, uint> progressCallback = null, int sectorNumber = 0, int totalSectors = 0)
        {
            data = null;

            if (!IsConnected())
            {
                DisplayStatusMessage("Not connected to ECU. Cannot read flash.", StatusMessageType.USER);
                return false;
            }

            if (size == 0)
            {
                DisplayStatusMessage("Invalid read size: 0 bytes.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Starting external flash read ({variant} variant, {size} bytes from offset 0x{startAddress:X6})...", StatusMessageType.LOG);

            if (!ConfigureRegistersForExternalFlashRead(variant))
            {
                DisplayStatusMessage("Failed to configure registers for external flash read.", StatusMessageType.USER);
                return false;
            }

            uint flashBaseAddress = GetFlashBaseAddress(variant);
            uint actualStartAddress = flashBaseAddress + startAddress;

            if (actualStartAddress < flashBaseAddress)
            {
                DisplayStatusMessage($"Start address overflow: 0x{actualStartAddress:X6}", StatusMessageType.USER);
                return false;
            }

            data = new byte[size];
            uint totalBytesRead = 0;
            uint offset = 0;
            ushort blockSize = DefaultBlockLength;
            int totalBlocks = (int)((size + blockSize - 1) / blockSize);

            // Read flash in blocks
            int blockNumber = 0;
            while (offset < size)
            {
                blockNumber++;
                // Adjust block size for last block
                ushort currentBlockSize = blockSize;
                if ((size - offset) < blockSize)
                {
                    currentBlockSize = (ushort)(size - offset);
                }

                uint currentAddress = actualStartAddress + offset;
                byte[] blockData;

                if (!MiniMon_ReadBlock(currentAddress, currentBlockSize, out blockData))
                {
                    DisplayStatusMessage($"Failed to read flash block {blockNumber} at address 0x{currentAddress:X6} (offset 0x{offset:X6})", StatusMessageType.USER);
                    DisplayStatusMessage($"ReadExternalFlash: Block {blockNumber} read failed at address 0x{currentAddress:X6}", StatusMessageType.LOG);
                    return false;
                }

                if (blockData == null || blockData.Length != currentBlockSize)
                {
                    DisplayStatusMessage($"ReadExternalFlash: Block {blockNumber} data length mismatch: expected {currentBlockSize}, got {(blockData?.Length ?? 0)}", StatusMessageType.LOG);
                    DisplayStatusMessage($"Failed to read flash block {blockNumber}: data length mismatch", StatusMessageType.USER);
                    return false;
                }

                Array.Copy(blockData, 0, data, (int)offset, currentBlockSize);
                totalBytesRead += currentBlockSize;
                offset += currentBlockSize;

                // Report progress
                if (progressCallback != null)
                {
                    progressCallback(totalBytesRead, size);
                }

                // Update status message periodically (no % symbol - that's reserved for overall progress)
                // Show sector progress and bytes read in current sector
                if (offset % (blockSize * 10) == 0 || offset >= size)
                {
                    if (sectorNumber > 0 && totalSectors > 0)
                    {
                        DisplayStatusMessage($"Reading flash: {sectorNumber}/{totalSectors} sectors, {offset}/{size} bytes in sector", StatusMessageType.LOG);
                    }
                    else
                    {
                        DisplayStatusMessage($"Reading flash: {offset}/{size} bytes", StatusMessageType.LOG);
                    }
                }
            }

            DisplayStatusMessage($"External flash read completed successfully: {size} bytes read from offset 0x{startAddress:X6}", StatusMessageType.LOG);
            return true;
        }

        #endregion

        #region External Flash Write Operations

        /// <summary>
        /// Loads a flash driver into ECU memory.
        /// </summary>
        /// <param name="variant">ECU/Flash variant (ME7, Simos3, or EDC15)</param>
        /// <returns>True if driver loaded successfully, false otherwise</returns>
        public bool LoadFlashDriver(ECUFlashVariant variant)
        {
            if (!IsConnected())
            {
                DisplayStatusMessage("Not connected to ECU. Cannot load flash driver.", StatusMessageType.USER);
                return false;
            }

            byte[] driverData = null;
            switch (variant)
            {
                case ECUFlashVariant.ME7:
                case ECUFlashVariant.EDC15:
                    driverData = Properties.Resources.BootmodeFlashDriverME7;
                    break;
                case ECUFlashVariant.Simos3:
                    driverData = Properties.Resources.BootmodeFlashDriverSimos3;
                    break;
                default:
                    DisplayStatusMessage($"Unknown flash variant: {variant}", StatusMessageType.USER);
                    return false;
            }

            if (driverData == null || driverData.Length == 0)
            {
                DisplayStatusMessage($"Flash driver data is null or empty for variant {variant}", StatusMessageType.USER);
                DisplayStatusMessage($"LoadFlashDriver: FAILED - Driver data is null or empty", StatusMessageType.LOG);
                return false;
            }

            byte functionResult;
            if (!MiniMon_WriteBlock(DriverAddress, driverData, out functionResult))
            {
                DisplayStatusMessage("Failed to upload flash driver to ECU memory.", StatusMessageType.USER);
                DisplayStatusMessage($"LoadFlashDriver: FAILED - MiniMon_WriteBlock failed, functionResult=0x{functionResult:X2}", StatusMessageType.LOG);
                return false;
            }

            DisplayStatusMessage($"Flash driver loaded successfully ({driverData.Length} bytes)", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Gets the flash manufacturer ID using FC_GETSTATE.
        /// </summary>
        /// <param name="variant">ECU/Flash variant</param>
        /// <param name="manufacturerID">Output manufacturer ID</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool GetFlashManufacturerID(ECUFlashVariant variant, out ushort manufacturerID)
        {
            manufacturerID = 0;

            if (!IsConnected())
            {
                DisplayStatusMessage("Not connected to ECU. Cannot read manufacturer ID.", StatusMessageType.USER);
                return false;
            }

            // Configure registers for external flash access (required before calling FC_GETSTATE)
            if (!ConfigureRegistersForExternalFlashRead(variant))
            {
                DisplayStatusMessage("Failed to configure registers for flash manufacturer ID read.", StatusMessageType.USER);
                return false;
            }

            uint writeAddressBase = GetFlashWriteBaseAddress(variant);
            uint readAddressBase = GetFlashBaseAddress(variant);

            ushort writeAddressHigh = (ushort)((writeAddressBase >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((readAddressBase >> 16) & 0xFFFF);

            // FC_GETSTATE register array: [FC_GETSTATE, 0x0000, writeAddressHigh, readAddressHigh, 0x000, 0x000, FC_GETSTATE_ADDR_MANUFID, 0x0001]
            ushort[] register = new ushort[]
            {
                (ushort)CommunicationConstants.FC_GETSTATE,
                0x0000,
                writeAddressHigh,
                readAddressHigh,
                0x0000,
                0x0000,
                FC_GETSTATE_ADDR_MANUFID,
                0x0001
            };

            // Convert ushort[] to byte[] for MiniMon_Call
            byte[] registerBytes = new byte[16];
            for (int i = 0; i < register.Length && i < 8; i++)
            {
                registerBytes[i * 2] = (byte)(register[i] & 0xFF);
                registerBytes[i * 2 + 1] = (byte)(register[i] >> 8);
            }

            byte[] retRegisterBytes;
            if (!MiniMon_Call(FlashDriverEntryPoint, registerBytes, out retRegisterBytes))
            {
                DisplayStatusMessage("Call FC_GETSTATE failed (manufacturer ID)", StatusMessageType.USER);
                return false;
            }

            // Convert byte[] back to ushort[]
            ushort[] retRegister = new ushort[8];
            for (int i = 0; i < 8; i++)
            {
                retRegister[i] = (ushort)(retRegisterBytes[i * 2] | (retRegisterBytes[i * 2 + 1] << 8));
            }

            if (retRegister[7] != (ushort)CommunicationConstants.FE_NOERROR)
            {
                DisplayStatusMessage($"Call FC_GETSTATE failed with error code 0x{retRegister[7]:X4} (manufacturer ID)", StatusMessageType.USER);
                return false;
            }

            manufacturerID = retRegister[1];

            // Handle Simos3 bit-crossing
            if (variant == ECUFlashVariant.Simos3)
                manufacturerID = GetCrossedWord(manufacturerID);

            DisplayStatusMessage($"Flash manufacturer ID: 0x{manufacturerID:X4}", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Gets the flash device ID using FC_GETSTATE.
        /// </summary>
        /// <param name="variant">ECU/Flash variant</param>
        /// <param name="deviceID">Output device ID</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool GetFlashDeviceID(ECUFlashVariant variant, out ushort deviceID)
        {
            deviceID = 0;

            if (!IsConnected())
            {
                DisplayStatusMessage("Not connected to ECU. Cannot read device ID.", StatusMessageType.USER);
                return false;
            }

            if (!ConfigureRegistersForExternalFlashRead(variant))
            {
                DisplayStatusMessage("Failed to configure registers for flash device ID read.", StatusMessageType.USER);
                return false;
            }

            uint writeAddressBase = GetFlashWriteBaseAddress(variant);
            uint readAddressBase = GetFlashBaseAddress(variant);

            ushort writeAddressHigh = (ushort)((writeAddressBase >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((readAddressBase >> 16) & 0xFFFF);

            // FC_GETSTATE register array: [FC_GETSTATE, 0x0000, writeAddressHigh, readAddressHigh, 0x000, 0x000, FC_GETSTATE_ADDR_DEVICEID, 0x0001]
            ushort[] register = new ushort[]
            {
                (ushort)CommunicationConstants.FC_GETSTATE,
                0x0000,
                writeAddressHigh,
                readAddressHigh,
                0x0000,
                0x0000,
                FC_GETSTATE_ADDR_DEVICEID,
                0x0001
            };

            // Convert ushort[] to byte[] for MiniMon_Call
            byte[] registerBytes = new byte[16];
            for (int i = 0; i < register.Length && i < 8; i++)
            {
                registerBytes[i * 2] = (byte)(register[i] & 0xFF);
                registerBytes[i * 2 + 1] = (byte)(register[i] >> 8);
            }

            byte[] retRegisterBytes;
            if (!MiniMon_Call(FlashDriverEntryPoint, registerBytes, out retRegisterBytes))
            {
                DisplayStatusMessage("Call FC_GETSTATE failed (device ID)", StatusMessageType.USER);
                return false;
            }

            // Convert byte[] back to ushort[]
            ushort[] retRegister = new ushort[8];
            for (int i = 0; i < 8; i++)
            {
                retRegister[i] = (ushort)(retRegisterBytes[i * 2] | (retRegisterBytes[i * 2 + 1] << 8));
            }

            if (retRegister[7] != (ushort)CommunicationConstants.FE_NOERROR)
            {
                DisplayStatusMessage($"Call FC_GETSTATE failed with error code 0x{retRegister[7]:X4} (device ID)", StatusMessageType.USER);
                return false;
            }

            deviceID = retRegister[1];
            if (variant == ECUFlashVariant.Simos3)
                deviceID = GetCrossedWord(deviceID);

            DisplayStatusMessage($"Flash device ID: 0x{deviceID:X4}", StatusMessageType.USER);

            // Store the flash device ID for reuse when core is already running
            LastKnownFlashDeviceID = deviceID;

            return true;
        }

        /// <summary>
        /// Gets the flash write base address for the specified variant.
        /// </summary>
        public uint GetFlashWriteBaseAddress(ECUFlashVariant variant)
        {
            switch (variant)
            {
                case ECUFlashVariant.ME7:
                    return ExtFlashWriteAddress_ME7;
                case ECUFlashVariant.Simos3:
                    return ExtFlashWriteAddress_Simos3;
                case ECUFlashVariant.EDC15:
                    return ExtFlashWriteAddress_EDC15;
                default:
                    return ExtFlashWriteAddress_ME7;  // Default to ME7
            }
        }

        /// <summary>
        /// Applies Simos3 bit-crossing transformation to a word.
        /// </summary>
        private ushort GetCrossedWord(ushort inputData)
        {
            ushort output = 0;
            output |= (ushort)((inputData & 0x0001) << 15);
            output |= (ushort)((inputData & 0x0002) << 12);
            output |= (ushort)((inputData & 0x0004) << 9);
            output |= (ushort)((inputData & 0x0008) << 6);
            output |= (ushort)((inputData & 0x0010) << 3);
            output |= (ushort)((inputData & 0x0020) >> 0);
            output |= (ushort)((inputData & 0x0040) >> 3);
            output |= (ushort)((inputData & 0x0080) >> 6);
            output |= (ushort)((inputData & 0x0100) << 6);
            output |= (ushort)((inputData & 0x0200) << 3);
            output |= (ushort)((inputData & 0x0400) << 0);
            output |= (ushort)((inputData & 0x0800) >> 3);
            output |= (ushort)((inputData & 0x1000) >> 6);
            output |= (ushort)((inputData & 0x2000) >> 9);
            output |= (ushort)((inputData & 0x4000) >> 12);
            output |= (ushort)((inputData & 0x8000) >> 15);
            return output;
        }

        /// <summary>
        /// Determines if a flash device ID represents a bottom boot or top boot chip.
        /// </summary>
        /// <param name="deviceID">Flash device ID</param>
        /// <returns>True if bottom boot, false if top boot</returns>
        public static bool IsBottomBoot(ushort deviceID)
        {
            // F400BB and F800BB are bottom boot
            // F400BT and F800BT are top boot
            return (deviceID == DEV_ID_F400BB || deviceID == DEV_ID_F800BB);
        }

        /// <summary>
        /// Gets the flash size in bytes for a given device ID.
        /// </summary>
        /// <param name="deviceID">Flash device ID</param>
        /// <returns>Flash size in bytes, or 0 if unknown</returns>
        public static uint GetFlashSizeFromDeviceID(ushort deviceID)
        {
            switch (deviceID)
            {
                case DEV_ID_F400BB:
                case DEV_ID_F400BT:
                    return 512 * 1024;  // 512KB
                case DEV_ID_F800BB:
                case DEV_ID_F800BT:
                    return 1024 * 1024;  // 1024KB
                default:
                    return 0;  // Unknown
            }
        }

        /// <summary>
        /// Calculates the sector size for a given sector number, device ID, and flash size.
        /// Based on Python reference implementation.
        /// </summary>
        /// <param name="sector">Sector number (0-based)</param>
        /// <param name="deviceID">Flash device ID</param>
        /// <param name="flashSize">Total flash size in bytes</param>
        /// <returns>Sector size in bytes</returns>
        public static uint CalculateSectorSize(int sector, ushort deviceID, uint flashSize)
        {
            bool bottomBoot = IsBottomBoot(deviceID);
            bool is1024KB = (flashSize == (1024 * 1024));

            if (!bottomBoot)  // Top boot sector
            {
                if (is1024KB)  // F800BT - 1024KB
                {
                    if (sector == 18)
                        return 0x4000;  // 16KB
                    else if (sector == 17 || sector == 16)
                        return 0x2000;  // 8KB
                    else if (sector == 15)
                        return 0x8000;  // 32KB
                    else
                        return 0x10000;  // 64KB
                }
                else  // F400BT - 512KB
                {
                    if (sector == 10)
                        return 0x4000;  // 16KB
                    else if (sector == 9 || sector == 8)
                        return 0x2000;  // 8KB
                    else if (sector == 7)
                        return 0x8000;  // 32KB
                    else
                        return 0x10000;  // 64KB
                }
            }
            else  // Bottom boot sector (F400BB, F800BB)
            {
                if (sector == 0)
                    return 0x4000;  // 16KB
                else if (sector == 1 || sector == 2)
                    return 0x2000;  // 8KB
                else if (sector == 3)
                    return 0x8000;  // 32KB
                else
                    return 0x10000;  // 64KB
            }
        }

        /// <summary>
        /// Generates a MemoryLayout from flash device ID.
        /// Auto-detects sector sizes based on device ID.
        /// </summary>
        /// <param name="deviceID">Flash device ID</param>
        /// <param name="baseAddress">Base address for flash (from variant)</param>
        /// <param name="layout">Output MemoryLayout</param>
        /// <returns>True if successful, false if device ID is unknown</returns>
        public static bool GenerateMemoryLayoutFromDeviceID(ushort deviceID, uint baseAddress, out MemoryLayout layout)
        {
            layout = null;

            uint flashSize = GetFlashSizeFromDeviceID(deviceID);
            if (flashSize == 0)
            {
                return false;  // Unknown device ID
            }

            var sectorSizes = new List<uint>();
            uint totalSize = 0;
            int sector = 0;

            // Calculate all sector sizes until we reach the flash size
            while (totalSize < flashSize)
            {
                uint sectorSize = CalculateSectorSize(sector, deviceID, flashSize);
                sectorSizes.Add(sectorSize);
                totalSize += sectorSize;
                sector++;

                // Safety check to prevent infinite loop
                if (sector > 100)
                {
                    break;
                }
            }

            layout = new MemoryLayout(baseAddress, totalSize, sectorSizes);
            return true;
        }

        #endregion
    }
}