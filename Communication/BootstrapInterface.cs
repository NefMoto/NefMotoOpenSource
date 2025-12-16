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
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using Shared;

namespace Communication
{
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
                        setupSuccess &= mCommunicationDevice.SetDTR(true);//enable receive for self powered devices
                        setupSuccess &= mCommunicationDevice.SetRTS(false);//set low to tell device we are ready to send
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
                return false;

            uint numBytesWritten = 0;
            bool success = mCommunicationDevice.Write(data, data.Length, ref numBytesWritten, 1) && (numBytesWritten == data.Length);

            if (success)
            {
                // Read echo and verify it matches what was sent
                byte[] echoData = new byte[data.Length];
                uint numEchoBytesRead = 0;
                if (mCommunicationDevice.Read(echoData, (uint)echoData.Length, ref numEchoBytesRead, 3) && (numEchoBytesRead == data.Length))
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

            return success;
        }

        bool ReceiveBytes(uint numBytes, out byte[] data)
        {
            data = new byte[numBytes];

            if (mCommunicationDevice == null)
            {
                data = null;
                return false;
            }

            uint numBytesRead = 0;
            bool success = mCommunicationDevice.Read(data, numBytes, ref numBytesRead, 3) && (numBytesRead == numBytes);

            if (!success)
            {
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
                return false;
            }

            byte[] functionCodeAck;
            if (!ReceiveBytes(1, out functionCodeAck) || (functionCodeAck[0] != (byte)CommunicationConstants.A_ACK1))
            {
                return false;
            }

            return true;
        }

        bool MiniMon_GetFunctionRequestAck(out byte result)
        {
            result = (byte)CommunicationConstants.A_ACK2;

            byte[] functionFinishedAck;
            if(!ReceiveBytes(1, out functionFinishedAck))
            {
                return false;
            }

            result = functionFinishedAck[0];

            return true;
        }

        bool MiniMon_WasFunctionRequestSuccessful()
        {
            byte result;
            return MiniMon_GetFunctionRequestAck(out result) && (result == (byte)CommunicationConstants.A_ACK2);
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
            sizeBytes[0] = (byte)(numBytes & 0xFF);
            sizeBytes[1] = (byte)(numBytes >> 8 & 0xFF);

            if (!SendBytes(sizeBytes))
            {
                return false;
            }

            if (!ReceiveBytes((uint)numBytes, out data))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
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

            byte[] wordBytes = new byte[2];
            wordBytes[0] = (byte)(word & 0xFF);
            wordBytes[1] = (byte)(word >> 8 & 0xFF);

            if (!SendBytes(wordBytes))
            {
                return false;
            }

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
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

            byte[] wordData;
            if (!ReceiveBytes(2, out wordData))
            {
                return false;
            }

            word = wordData[1];
            word <<= 8;
            word |= wordData[0];

            if (!MiniMon_WasFunctionRequestSuccessful())
            {
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
                        //reset all of the communication state when we start trying to connect,
                        //this way we can respect the old settings when we start connecting
                        shouldReset = true;
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Connected)
                    {
                        //no more connection attempts
                        mNumConnectionAttemptsRemaining = 0;
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Disconnected)
                    {
                        //we can reset the communication now and not wait for the next
                        //connection attempt because we were never connected
                        shouldReset = !wasConnected;
                    }

                    if (shouldReset)
                    {
                        //TODO: reset any communication state we have cached such as DeviceID

                        //TODO: restore the communication state to default settings
                        mCommunicationDevice?.Purge(PurgeType.RX | PurgeType.TX);
                    }
                }
            }
        }

        protected uint mNumConnectionAttemptsRemaining = 0;
    }
}