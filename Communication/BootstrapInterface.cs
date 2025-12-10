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
using System.IO;
using Shared;
using FTD2XX_NET;

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

                    OnPropertyChanged(new PropertyChangedEventArgs("DeviceID"));
                }
            }
        }
        private byte _DeviceID = 0;

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

                if (OpenFTDIDevice(SelectedDeviceInfo))
                {
                    FTDI.FT_STATUS setupStatus = FTDI.FT_STATUS.FT_OK;
                    setupStatus |= mFTDIDevice.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8, FTDI.FT_STOP_BITS.FT_STOP_BITS_1, FTDI.FT_PARITY.FT_PARITY_NONE);
                    setupStatus |= mFTDIDevice.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0);
                    setupStatus |= mFTDIDevice.SetLatency(2);//2 ms is min, this is the max time before data must be sent from the device to the PC even if not a full block

                    //setupStatus |= mFTDIDevice.InTransferSize(64 * ((MAX_MESSAGE_SIZE / 64) + 1) * 10);//64 bytes is min, must be multiple of 64 (this size includes a few bytes of USB header overhead)
                    setupStatus |= mFTDIDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);
                    setupStatus |= mFTDIDevice.SetDTR(true);//enable receive for self powered devices
                    setupStatus |= mFTDIDevice.SetRTS(false);//set low to tell device we are ready to send
                    setupStatus |= mFTDIDevice.SetBreak(false);//set to high idle state
                    setupStatus |= mFTDIDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
                    setupStatus |= mFTDIDevice.SetBaudRate(baudRate);
                    setupStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                    if (setupStatus == FTDI.FT_STATUS.FT_OK)
                    {
                        success = true;

                        StartSendReceiveThread(SendReceiveThread);
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to setup FTDI device.", StatusMessageType.USER);
                    }
                }
                else
                {
                    DisplayStatusMessage("Could not open FTDI device.", StatusMessageType.USER);
                }
            }

            return success;
        }

        public void CloseConnection()
        {
            ConnectionStatus = ConnectionStatusType.DisconnectionPending;

            KillSendReceiveThread();

            CloseFTDIDevice();

            ConnectionStatus = ConnectionStatusType.Disconnected;
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
            uint numBytesWritten = 0;
            var status = mFTDIDevice.Write(data, data.Length, ref numBytesWritten, 1);

            bool success = (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK) && (numBytesWritten == data.Length);

            if (success)
            {
                byte[] echoData = new byte[data.Length];
                uint numEchoBytesRead = 0;
                status = mFTDIDevice.Read(echoData, (uint)echoData.Length, ref numEchoBytesRead, 3);

                success = (status == FTD2XX_NET.FTDI.FT_STATUS.FT_OK) && (numBytesWritten == data.Length);
            }

            return success;
        }

        bool ReceiveBytes(uint numBytes, out byte[] data)
        {
            data = new byte[numBytes];

            uint numBytesRead = 0;
            var status = mFTDIDevice.Read(data, numBytes, ref numBytesRead, 3);

            if ((status != FTD2XX_NET.FTDI.FT_STATUS.FT_OK) || (numBytesRead != numBytes))
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
            byte[] miniMonLoader = { 0xE6, 0x58, 0x01, 0x00, 0x9A, 0xB6, 0xFE, 0x70, 0xE6, 0xF0, 0x60, 0xFA, 0x7E, 0xB7, 0x9A, 0xB7, 0xFE, 0x70, 0xA4, 0x00, 0xB2, 0xFE, 0x86, 0xF0, 0xE7, 0xFB, 0x3D, 0xF8, 0xEA, 0x00, 0x60, 0xFA };

            byte deviceID;
            if (!UploadMiniMonBootstrapLoader(miniMonLoader, out deviceID))
            {
                return false;
            }

            DeviceID = deviceID;

            // Load MiniMon program binary
            byte[] miniMonProgram = LoadMiniMonBinary();

            if (miniMonProgram == null || miniMonProgram.Length == 0)
            {
                DisplayStatusMessage("Failed to load MiniMon binary. Boot mode flash operations will not be available.", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Loaded MiniMon binary: {miniMonProgram.Length} bytes", StatusMessageType.LOG);

            if (!UploadMiniMonProgram(miniMonProgram))
            {
                return false;
            }

            return true;
        }

        byte[] LoadMiniMonBinary()
        {
            // Try to load from file first (for development)
            string[] possiblePaths = {
                Path.Combine("Communication", "Resources", "MINIMONK.bin"),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Resources", "MINIMONK.bin"),
                Path.Combine("C167BootTool", "Minimon", "MINIMONK.bin"),
                Path.Combine("Minimon", "MINIMONK.bin"),
                "MINIMONK.bin"
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(path);
                        DisplayStatusMessage($"Loaded MiniMon binary from: {path}", StatusMessageType.LOG);
                        return data;
                    }
                    catch (System.Exception ex)
                    {
                        DisplayStatusMessage($"Failed to load MiniMon from {path}: {ex.Message}", StatusMessageType.LOG);
                    }
                }
            }

            // Try to load from embedded resource
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetName().Name + ".Resources.MINIMONK.bin";
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        DisplayStatusMessage("Loaded MiniMon binary from embedded resource", StatusMessageType.LOG);
                        return data;
                    }
                }
            }
            catch (System.Exception ex)
            {
                DisplayStatusMessage($"Failed to load MiniMon from resource: {ex.Message}", StatusMessageType.LOG);
            }

            return null;
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

        bool MiniMon_ReadBlock(uint address, ushort numBytes, out byte[] data)
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

        bool MiniMon_ReadWord(uint address, out uint word)
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

        // Flash Driver Addresses - Based on C167BootTool implementation
        private const uint FLASH_DRIVER_LOAD_ADDRESS = 0x00F600;  // Where flash driver binary is loaded
        private const uint FLASH_DRIVER_ENTRY_POINT = 0x00F640;  // Entry point to call flash driver functions
        private const uint DRIVER_COPY_ADDRESS = 0xFC00;         // RAM buffer for flash operations
        private const uint FLASH_BLOCK_LENGTH = 0x200;          // 512 bytes - standard block size
        private const uint EXT_FLASH_READ_ADDRESS = 0x800000;    // External flash read address (ME7)
        private const uint EXT_FLASH_WRITE_ADDRESS_ME7 = 0x800000; // External flash write address (ME7)

        /// <summary>
        /// Loads the flash driver binary into RAM
        /// </summary>
        /// <param name="driverPath">Path to flash driver binary file (e.g., FX00Bx_Driver.bin)</param>
        /// <returns>True if successful</returns>
        public bool LoadFlashDriver(string driverPath = null)
        {
            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot load flash driver: not connected", StatusMessageType.USER);
                return false;
            }

            // Try to find flash driver binary
            string[] possiblePaths = {
                driverPath,
                Path.Combine("Communication", "Resources", "FX00Bx_Driver.bin"),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Resources", "FX00Bx_Driver.bin"),
                Path.Combine("C167BootTool", "Drivers", "FX00Bx_Driver.bin"),
                Path.Combine("Drivers", "FX00Bx_Driver.bin"),
                "FX00Bx_Driver.bin"
            };

            byte[] driverBinary = null;
            foreach (string path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        driverBinary = File.ReadAllBytes(path);
                        DisplayStatusMessage($"Loading flash driver from {path}", StatusMessageType.USER);
                        break;
                    }
                    catch (System.Exception ex)
                    {
                        DisplayStatusMessage($"Failed to load flash driver from {path}: {ex.Message}", StatusMessageType.LOG);
                    }
                }
            }

            if (driverBinary == null || driverBinary.Length == 0)
            {
                DisplayStatusMessage("Flash driver binary not found. Flash operations may not work.", StatusMessageType.USER);
                // Continue anyway - driver might already be loaded or might be loaded later
                return true;
            }

            // Upload driver to RAM at FLASH_DRIVER_LOAD_ADDRESS
            if (!MiniMon_WriteBlock(FLASH_DRIVER_LOAD_ADDRESS, driverBinary, out byte writeResult))
            {
                DisplayStatusMessage("Failed to upload flash driver to RAM", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Flash driver loaded at 0x{FLASH_DRIVER_LOAD_ADDRESS:X6}", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Calls the flash driver function with the specified parameters
        /// Register format matches C167BootTool: [FC, param1, param2, param3, param4, param5, param6, param7]
        /// </summary>
        /// <param name="registerParams">8 register words (16 bytes) in the format expected by the flash driver</param>
        /// <param name="registerResults">Returns the 8 register words (16 bytes) from the flash driver</param>
        /// <returns>True if successful, false otherwise</returns>
        bool MiniMon_CallFlashDriver(ushort[] registerParams, out ushort[] registerResults)
        {
            registerResults = null;

            if (registerParams == null || registerParams.Length != 8)
            {
                DisplayStatusMessage("Flash driver call requires exactly 8 register parameters", StatusMessageType.USER);
                return false;
            }

            // Convert register words to byte array (little endian)
            byte[] registerParamsBytes = new byte[16];
            for (int i = 0; i < 8; i++)
            {
                registerParamsBytes[i * 2] = (byte)(registerParams[i] & 0xFF);
                registerParamsBytes[i * 2 + 1] = (byte)((registerParams[i] >> 8) & 0xFF);
            }

            byte[] registerResultsBytes;
            if (!MiniMon_Call(FLASH_DRIVER_ENTRY_POINT, registerParamsBytes, out registerResultsBytes))
            {
                DisplayStatusMessage($"Failed to call flash driver function", StatusMessageType.USER);
                return false;
            }

            // Convert result bytes back to register words
            registerResults = new ushort[8];
            for (int i = 0; i < 8; i++)
            {
                registerResults[i] = (ushort)(registerResultsBytes[i * 2] | (registerResultsBytes[i * 2 + 1] << 8));
            }

            // Error code is in register 7 (index 7)
            ushort errorCode = registerResults[7];
            if (errorCode != (ushort)CommunicationConstants.FE_NOERROR)
            {
                DisplayStatusMessage($"Flash driver returned error code: 0x{errorCode:X4}", StatusMessageType.USER);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Calculates sector number and sector start address from a given address
        /// For 29F800BB (bottom boot): Sector 0=16KB, 1-2=8KB each, 3=32KB, 4+=64KB each
        /// </summary>
        /// <param name="address">Address within the sector</param>
        /// <param name="sectorNumber">Returns the sector number (0-based)</param>
        /// <param name="sectorStartAddress">Returns the sector's start address</param>
        /// <param name="sectorSize">Returns the sector's size</param>
        /// <returns>True if successful</returns>
        private bool CalculateSectorInfo(uint address, out ushort sectorNumber, out uint sectorStartAddress, out uint sectorSize)
        {
            sectorNumber = 0;
            sectorStartAddress = EXT_FLASH_READ_ADDRESS;
            sectorSize = 0;

            // For 29F800BB (bottom boot sector, 1024KB = 1MB)
            // Sector 0: 0x4000 (16KB) at 0x800000
            // Sector 1: 0x2000 (8KB) at 0x804000
            // Sector 2: 0x2000 (8KB) at 0x806000
            // Sector 3: 0x8000 (32KB) at 0x808000
            // Sectors 4-18: 0x10000 (64KB each) starting at 0x810000

            uint currentAddress = EXT_FLASH_READ_ADDRESS;
            ushort sector = 0;

            // Sector 0: 16KB
            if (address < currentAddress + 0x4000)
            {
                sectorNumber = 0;
                sectorStartAddress = currentAddress;
                sectorSize = 0x4000;
                return true;
            }
            currentAddress += 0x4000;
            sector++;

            // Sector 1: 8KB
            if (address < currentAddress + 0x2000)
            {
                sectorNumber = 1;
                sectorStartAddress = currentAddress;
                sectorSize = 0x2000;
                return true;
            }
            currentAddress += 0x2000;
            sector++;

            // Sector 2: 8KB
            if (address < currentAddress + 0x2000)
            {
                sectorNumber = 2;
                sectorStartAddress = currentAddress;
                sectorSize = 0x2000;
                return true;
            }
            currentAddress += 0x2000;
            sector++;

            // Sector 3: 32KB
            if (address < currentAddress + 0x8000)
            {
                sectorNumber = 3;
                sectorStartAddress = currentAddress;
                sectorSize = 0x8000;
                return true;
            }
            currentAddress += 0x8000;
            sector++;

            // Sectors 4-18: 64KB each
            while (sector < 19)
            {
                if (address < currentAddress + 0x10000)
                {
                    sectorNumber = sector;
                    sectorStartAddress = currentAddress;
                    sectorSize = 0x10000;
                    return true;
                }
                currentAddress += 0x10000;
                sector++;
            }

            // Address is beyond sector 18
            return false;
        }

        /// <summary>
        /// Erases a flash sector by address
        /// Register format: [FC_ERASE, writeAddressLow, writeAddressHigh, readAddressHigh, lastWordAddress, 0x000, sector, 0x0001]
        /// The sector number and sector boundaries are calculated from the address
        /// </summary>
        /// <param name="sectorAddress">Address within the sector to erase (sector will be calculated)</param>
        /// <param name="sectorSize">Size hint (not used, sector size is calculated)</param>
        /// <returns>True if successful</returns>
        public bool EraseFlashSector(uint sectorAddress, uint sectorSize)
        {
            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot erase flash: not connected", StatusMessageType.USER);
                return false;
            }

            // Calculate which sector this address belongs to
            if (!CalculateSectorInfo(sectorAddress, out ushort sectorNumber, out uint actualSectorStartAddress, out uint actualSectorSize))
            {
                DisplayStatusMessage($"Cannot determine sector for address 0x{sectorAddress:X8}", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Erasing flash sector {sectorNumber} at 0x{actualSectorStartAddress:X8}, size: 0x{actualSectorSize:X8}", StatusMessageType.USER);

            // Calculate addresses according to Python code format
            // Use the actual sector start address, not the passed address
            uint writeAddress = EXT_FLASH_WRITE_ADDRESS_ME7 + (actualSectorStartAddress - EXT_FLASH_READ_ADDRESS);
            ushort writeAddressLow = (ushort)(writeAddress & 0xFFFF);
            ushort writeAddressHigh = (ushort)((writeAddress >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((EXT_FLASH_READ_ADDRESS >> 16) & 0xFFFF);
            ushort lastWordAddress = (ushort)((actualSectorStartAddress + actualSectorSize - 2) & 0xFFFF);

            // Register format: [FC_ERASE, writeAddressLow, writeAddressHigh, readAddressHigh, lastWordAddress, 0x000, sector, 0x0001]
            ushort[] registerParams = new ushort[8]
            {
                (ushort)CommunicationConstants.FC_ERASE,  // R4: Function code
                writeAddressLow,                          // R5: Write address low
                writeAddressHigh,                         // R6: Write address high
                readAddressHigh,                          // R7: Read address high
                lastWordAddress,                          // R8: Last word address (sector end - 2)
                0x000,                                    // R9: Reserved
                sectorNumber,                             // R10: Sector number
                0x0001                                    // R11: Flag/parameter
            };

            if (!MiniMon_CallFlashDriver(registerParams, out ushort[] registerResults))
            {
                ushort errorCode = registerResults != null && registerResults.Length > 7 ? registerResults[7] : (ushort)0xFFFF;
                DisplayStatusMessage($"Flash erase failed with error code: 0x{errorCode:X4}", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("Flash sector erased successfully", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Programs a block of flash memory
        /// Register format: [FC_PROG, writesize, DriverCopyAddress, 0x0000, readAddressHigh, writeAddressLow, writeAddressHigh, 0x0001]
        /// </summary>
        /// <param name="flashAddress">Destination address in flash</param>
        /// <param name="data">Data to program</param>
        /// <param name="ramBufferAddress">Address in RAM to use as buffer (defaults to DRIVER_COPY_ADDRESS)</param>
        /// <returns>True if successful</returns>
        public bool ProgramFlashBlock(uint flashAddress, byte[] data, uint ramBufferAddress = 0)
        {
            if (ramBufferAddress == 0)
            {
                ramBufferAddress = DRIVER_COPY_ADDRESS;
            }

            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot program flash: not connected", StatusMessageType.USER);
                return false;
            }

            if (data == null || data.Length == 0)
            {
                DisplayStatusMessage("Cannot program flash: no data provided", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Programming flash at 0x{flashAddress:X8}, size: {data.Length} bytes", StatusMessageType.USER);

            // Program in blocks of FLASH_BLOCK_LENGTH (512 bytes)
            uint offset = 0;
            while (offset < data.Length)
            {
                uint currentSize = (uint)Math.Min(FLASH_BLOCK_LENGTH, data.Length - offset);
                byte[] blockData = new byte[currentSize];
                Array.Copy(data, (int)offset, blockData, 0, (int)currentSize);

                // Check if block is all 0xFF (erased) - skip if so
                bool allFF = true;
                foreach (byte b in blockData)
                {
                    if (b != 0xFF)
                    {
                        allFF = false;
                        break;
                    }
                }

                if (allFF)
                {
                    offset += currentSize;
                    continue; // Skip programming 0xFF blocks
                }

                // Write data to RAM buffer
                if (!MiniMon_WriteBlock(ramBufferAddress, blockData, out byte writeResult))
                {
                    DisplayStatusMessage("Failed to write data to RAM buffer", StatusMessageType.USER);
                    return false;
                }

                // Calculate addresses according to Python code format
                uint writeAddress = EXT_FLASH_WRITE_ADDRESS_ME7 + (flashAddress + offset - EXT_FLASH_READ_ADDRESS);
                ushort writeAddressLow = (ushort)(writeAddress & 0xFFFF);
                ushort writeAddressHigh = (ushort)((writeAddress >> 16) & 0xFFFF);
                ushort readAddressHigh = (ushort)((EXT_FLASH_READ_ADDRESS >> 16) & 0xFFFF);

                // Register format: [FC_PROG, writesize, DriverCopyAddress, 0x0000, readAddressHigh, writeAddressLow, writeAddressHigh, 0x0001]
                ushort[] registerParams = new ushort[8]
                {
                    (ushort)CommunicationConstants.FC_PROG,  // R4: Function code
                    (ushort)currentSize,                     // R5: Write size
                    (ushort)ramBufferAddress,                // R6: RAM buffer address (DriverCopyAddress)
                    0x0000,                                  // R7: Reserved
                    readAddressHigh,                         // R8: Read address high
                    writeAddressLow,                         // R9: Write address low
                    writeAddressHigh,                        // R10: Write address high
                    0x0001                                   // R11: Flag/parameter
                };

                if (!MiniMon_CallFlashDriver(registerParams, out ushort[] registerResults))
                {
                    ushort errorCode = registerResults != null && registerResults.Length > 7 ? registerResults[7] : (ushort)0xFFFF;
                    DisplayStatusMessage($"Flash program failed with error code: 0x{errorCode:X4} at 0x{flashAddress + offset:X8}", StatusMessageType.USER);
                    return false;
                }

                offset += currentSize;
            }

            DisplayStatusMessage("Flash programming completed successfully", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Reads a block of flash memory
        /// </summary>
        /// <param name="address">Address to read from</param>
        /// <param name="size">Number of bytes to read</param>
        /// <param name="data">Output buffer for read data</param>
        /// <returns>True if successful</returns>
        public bool ReadFlashBlock(uint address, uint size, out byte[] data)
        {
            data = null;

            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot read flash: not connected", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Reading flash from 0x{address:X8}, size: {size} bytes", StatusMessageType.USER);

            if (!MiniMon_ReadBlock(address, (ushort)size, out data))
            {
                DisplayStatusMessage("Failed to read flash block", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage("Flash read completed successfully", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Checks if a flash region is blank (all 0xFF)
        /// </summary>
        /// <param name="address">Address to check</param>
        /// <param name="size">Size of region to check</param>
        /// <param name="isBlank">Returns true if region is blank</param>
        /// <returns>True if check completed successfully</returns>
        public bool BlankCheckFlash(uint address, uint size, out bool isBlank)
        {
            isBlank = false;

            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot blank check flash: not connected", StatusMessageType.USER);
                return false;
            }

            DisplayStatusMessage($"Blank checking flash at 0x{address:X8}, size: 0x{size:X8}", StatusMessageType.USER);

            // Register format for FC_BLANKCHECK - format not fully documented in Python code
            // Using a basic format: [FC_BLANKCHECK, addressLow, addressHigh, sizeLow, sizeHigh, 0, 0, 0x0001]
            ushort addressLow = (ushort)(address & 0xFFFF);
            ushort addressHigh = (ushort)((address >> 16) & 0xFFFF);
            ushort sizeLow = (ushort)(size & 0xFFFF);
            ushort sizeHigh = (ushort)((size >> 16) & 0xFFFF);

            ushort[] registerParams = new ushort[8]
            {
                (ushort)CommunicationConstants.FC_BLANKCHECK,  // R4: Function code
                addressLow,                                     // R5: Address low
                addressHigh,                                    // R6: Address high
                sizeLow,                                        // R7: Size low
                sizeHigh,                                       // R8: Size high
                0x0000,                                        // R9: Reserved
                0x0000,                                        // R10: Reserved
                0x0001                                         // R11: Flag/parameter
            };

            if (!MiniMon_CallFlashDriver(registerParams, out ushort[] registerResults))
            {
                ushort errorCode = registerResults != null && registerResults.Length > 7 ? registerResults[7] : (ushort)0xFFFF;
                DisplayStatusMessage($"Blank check failed with error code: 0x{errorCode:X4}", StatusMessageType.USER);
                return false;
            }

            // If no error, region is blank
            ushort errorCode2 = registerResults[7];
            isBlank = (errorCode2 == (ushort)CommunicationConstants.FE_NOERROR);
            DisplayStatusMessage($"Flash blank check: {(isBlank ? "BLANK" : "NOT BLANK")}", StatusMessageType.USER);
            return true;
        }

        /// <summary>
        /// Gets the flash state
        /// </summary>
        /// <param name="state">Returns the flash state</param>
        /// <returns>True if successful</returns>
        public bool GetFlashState(out ushort state)
        {
            state = 0;

            if (!IsConnectionOpen())
            {
                DisplayStatusMessage("Cannot get flash state: not connected", StatusMessageType.USER);
                return false;
            }

            ushort writeAddressHigh = (ushort)((EXT_FLASH_WRITE_ADDRESS_ME7 >> 16) & 0xFFFF);
            ushort readAddressHigh = (ushort)((EXT_FLASH_READ_ADDRESS >> 16) & 0xFFFF);
            ushort addressType = 0x00; // FC_GETSTATE_ADDR_MANUFID

            ushort[] registerParams = new ushort[8]
            {
                (ushort)CommunicationConstants.FC_GETSTATE,  // R4: Function code
                0x0000,                                       // R5: Reserved
                writeAddressHigh,                             // R6: Write address high
                readAddressHigh,                              // R7: Read address high
                0x0000,                                       // R8: Reserved
                0x0000,                                       // R9: Reserved
                addressType,                                  // R10: Address type (MANUFID=0, DEVICEID=1)
                0x0001                                        // R11: Flag/parameter
            };

            if (!MiniMon_CallFlashDriver(registerParams, out ushort[] registerResults))
            {
                return false;
            }

            // State is returned in register 1 (R5) according to Python code
            state = registerResults[1];
            return true;
        }

        protected void SendReceiveThread()
        {
            DisplayStatusMessage("Send receive thread now started.", StatusMessageType.LOG);
            ConnectionStatus = ConnectionStatusType.Disconnected;

            while ((ConnectionStatus != ConnectionStatusType.Disconnected) || (mNumConnectionAttemptsRemaining > 0))
            {
                lock (mFTDIDevice)
                {
                    #region HandleConnecting
                    while ((ConnectionStatus == ConnectionStatusType.Disconnected) && (mNumConnectionAttemptsRemaining > 0))
                    {
                        mNumConnectionAttemptsRemaining--;

                        ConnectionStatus = ConnectionStatusType.ConnectionPending;

                        bool connected = StartMiniMon();

                        if (!connected)
                        {
                            ConnectionStatus = ConnectionStatusType.Disconnected;
                        }
                        else
                        {
                            ConnectionStatus = ConnectionStatusType.Connected;
                            mNumConnectionAttemptsRemaining = 0;
                        }
                    }
                    #endregion
                }

                //TODO: communicate....

                //if we sleep longer, the communication will run slower and could cause timeouts
                //this sleep makes a big difference in system performance
                //sleep zero is dangerous, could cause thread starvation of other lower priority threads
                Thread.Sleep(2);

                //TODO: see if increasing this sleep time reduces the frequenecy of FT_IO_ERROR since FTDI seems to like 5ms sleeps for updating the receive queue status
            }

            ConnectionStatus = ConnectionStatusType.CommunicationTerminated;//we need to do this to ensure any operations or actions fail due to disconnection
            CloseFTDIDevice();
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
                        mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                    }
                }
            }
        }

        protected uint mNumConnectionAttemptsRemaining = 0;

        /// <summary>
        /// Checks if boot mode is ready for flash operations
        /// </summary>
        public bool IsBootModeReady()
        {
            if (!IsConnectionOpen())
            {
                return false;
            }

            // Test communication to verify MiniMon is running
            return MiniMon_TestCommunication();
        }
    }
}