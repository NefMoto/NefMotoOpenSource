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

//#define LOG_RECEIVED_DATA
//#define LOG_RECEIVED_MESSAGE_DATA
//#define LOG_SENT_MESSAGE_DATA
//#define LOG_KWP2000_PERFORMANCE
//#define DEBUG_ECHO
#define CHECK_CABLE_IN_DUMB_MODE
//#define SEND_START_COMM_AFTER_SLOW_INIT //EDC15 gets angry when you send a start communication message after doing a double slow init to start a KWP2000 session

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics;
using System.ComponentModel;
using System.Xml.Serialization;

using Shared;
using FTD2XX_NET;

namespace Communication
{
	public class TimingParameters : ICloneable
	{
        public long P1ECUInterByteTimeMaxMs;
        public long P2ECUResponseTimeMinMs;
        public long P2ECUResponseTimeMaxMs;
        public long P3TesterResponseTimeMinMs;
        public long P3TesterResponseTimeMaxMs;
        public long P4TesterInterByteTimeMinMs;

        public TimingParameters()
        {
            P1ECUInterByteTimeMaxMs = KWP2000Interface.P1_DEFAULT_ECU_INTERBYTE_MAX_TIME;
            P2ECUResponseTimeMinMs = KWP2000Interface.P2_DEFAULT_ECU_RESPONSE_MIN_TIME;
            //changed this to 1000 because if we negotiate fast timings, then don't cleanly disconnect, we would time out on reconnecting
            P2ECUResponseTimeMaxMs = 1000;//KWP2000Interface.P2_DEFAULT_ECU_RESPONSE_MAX_TIME;//max time from timings, or longer if response pending
            P3TesterResponseTimeMinMs = KWP2000Interface.P3_DEFAULT_TESTER_RESPONSE_MIN_TIME;
            P3TesterResponseTimeMaxMs = KWP2000Interface.P3_DEFAULT_TESTER_RESPONSE_MAX_TIME;
            P4TesterInterByteTimeMinMs = KWP2000Interface.P4_DEFAULT_TESTER_INTERBYTE_MIN_TIME;
        }

		public void EnforceTimingIntervalRequirements()
		{
			/*			 
			Requirements checked by the ecu when setting timing values:
			P2 min >= P2 min limit
			*P2 max >= P2 min
			P2 max <= P2 max limit
			*P2 max - 12 > P2 min
			P3 min >= P3 min limit
			*P3 max >= P3 min
			P3 max <= P3 max limit
			*P3 max - 12 > P3 min
			P4 min >= P4 min limit
			P4 min <= P4 max limit
			*/

			//requirements of the KWP2000 protocol
			// ASSERT Pi_MIN < Pi_MAX
			// ASSERT P3_MIN > P4_MIN
            // ASSERT P2_MIN > P1_MAX if end of message is detected by timed out according to ISO14230-2
			// ASSERT P2_MIN > P4_MAX if end of message is detected by timed out according to ISO14230-2
			// ASSERT P3_MIN > P2_MAX if functional addressing or data segmentation is used according to ISO14239-2
			// ASSERT P2_BUSY_MIN = P2_MIN
			// ASSERT P3_BUSY_MAX = P3_MAX

            P2ECUResponseTimeMaxMs = Math.Max(P2ECUResponseTimeMaxMs, P2ECUResponseTimeMinMs + 13);//I think the + 13 is how the ME7 ECU does it
            P3TesterResponseTimeMinMs = Math.Max(P3TesterResponseTimeMinMs, P4TesterInterByteTimeMinMs);
            //P3TesterResponseTimeMinMs = Math.Max(P3TesterResponseTimeMinMs, P2ECUResponseTimeMaxMs);//This should be enabled according spec (if functional addressing or data segmentation is used)
            P3TesterResponseTimeMaxMs = Math.Max(P3TesterResponseTimeMaxMs, P3TesterResponseTimeMinMs + 13);//I think the + 13 is how the ME7 ECU does it			
		}

        public override string ToString()
        {
            return "P1Max: " + P1ECUInterByteTimeMaxMs + "ms P2Min: " + P2ECUResponseTimeMinMs + "ms P2Max: " + P2ECUResponseTimeMaxMs + "ms P3Min: " + P3TesterResponseTimeMinMs + "ms P3Max: " + P3TesterResponseTimeMaxMs + "ms P4Min: " + P4TesterInterByteTimeMinMs + "ms";
        }

        public override bool Equals(object obj)
        {
            if (obj is TimingParameters)
            {
                TimingParameters otherParams = (TimingParameters)obj;

                return (P1ECUInterByteTimeMaxMs == otherParams.P1ECUInterByteTimeMaxMs) 
                        && (P2ECUResponseTimeMinMs == otherParams.P2ECUResponseTimeMinMs) 
                        && (P2ECUResponseTimeMaxMs == otherParams.P2ECUResponseTimeMaxMs)
                        && (P3TesterResponseTimeMinMs == otherParams.P3TesterResponseTimeMinMs) 
                        && (P3TesterResponseTimeMaxMs == otherParams.P3TesterResponseTimeMaxMs)
                        && (P4TesterInterByteTimeMinMs == otherParams.P4TesterInterByteTimeMinMs);
            }

            return base.Equals(obj);
        }

        public object Clone()
        {
            TimingParameters clonedParams = new TimingParameters();
            clonedParams.P1ECUInterByteTimeMaxMs = P1ECUInterByteTimeMaxMs;
            clonedParams.P2ECUResponseTimeMinMs = P2ECUResponseTimeMinMs;
            clonedParams.P2ECUResponseTimeMaxMs = P2ECUResponseTimeMaxMs;
            clonedParams.P3TesterResponseTimeMinMs = P3TesterResponseTimeMinMs;
            clonedParams.P3TesterResponseTimeMaxMs = P3TesterResponseTimeMaxMs;
            clonedParams.P4TesterInterByteTimeMinMs = P4TesterInterByteTimeMinMs;
            return clonedParams;
        }
	};

    public class KWP2000Interface : CommunicationInterface
	{
		///////////////////////////////////////////////////////////////////////////////////////////////////////////
		//PUBLIC TYPES
		///////////////////////////////////////////////////////////////////////////////////////////////////////////

		public enum TimingParameterMode
		{
			Default,//timing params are defaults read from ECU
			Current,//timing params are current read from ECU
			Limits,//timing params are limits read from ECU
			Unknown//timing params are not connected to the ECU
		}

        public const byte TESTER_ADDRESS = 0xF1;

        public const byte DEFAULT_ECU_SLOWINIT_KWP1281_ADDRESS = 0x01;
        public const byte DEFAULT_ECU_SLOWINIT_KWP2000_ADDRESS = 0x11;//KWP2000 physical address when using slow init

        public const byte DEFAULT_ECU_FASTINIT_KWP2000_PHYSICAL_ADDRESS = 0x01;//KWP2000 physical address when using fast init
        public const byte DEFUALT_ECU_FASTINIT_KWP2000_FUNCTIONAL_ADDRESS_EXTERNAL_FLASH = 0xFE;
        public const byte DEFUALT_ECU_FASTINIT_KWP2000_FUNCTIONAL_ADDRESS_EXTERNAL_RAM = 0x02;
        public const byte DEFUALT_ECU_FASTINIT_KWP2000_FUNCTIONAL_ADDRESS_INTERNAL_ROM = 0x10;

		//KWP_1281_KB2_0x0A * 128 + KWP_1281_KB1_0x01 = 1281
        protected const byte KWP_1281_PROTOCOL_KEY_BYTE_1 = 0x01;//seven data bits, odd parity
        protected const byte KWP_1281_PROTOCOL_KEY_BYTE_2 = 0x0A;//seven data bits, odd parity

        public const byte KWP_2000_PROTOCOL_KEY_BYTE_2 = 0x0F;//seven data bits, odd parity

        public const byte DEFAULT_KEY_BYTE_1 = 0x6B;
        public const byte DEFAULT_KEY_BYTE_2 = KWP_2000_PROTOCOL_KEY_BYTE_2;

        public const uint MAX_MESSAGE_DATA_SIZE_WITHOUT_LENGTH_BYTE = 0x3F;//including service id
        public const uint MAX_MESSAGE_DATA_SIZE_WITH_LENGTH_BYTE = 0xFF;//including service id

        // ASSERT Pi_MIN < Pi_MAX
        // ASSERT P3_MIN > P2_MAX
        // ASSERT P3_MIN > P4_MIN
        // ASSERT P2_MIN > P4_MAX
        // ASSERT P2_MIN > P1_MAX
        // ASSERT P2_BUSY_MIN = P2_MIN
        // ASSERT P3_BUSY_MAX = P3_MAX

        //these defaults are set based on defaults in ECU, may be the same as the spec though
        public const long P1_DEFAULT_ECU_INTERBYTE_MIN_TIME = 0;
        public const long P1_DEFAULT_ECU_INTERBYTE_MAX_TIME = 20;
        public const long P2_DEFAULT_ECU_RESPONSE_MIN_TIME = 25;
        public const long P2_DEFAULT_ECU_RESPONSE_MAX_TIME = 50;
        public const long P3_DEFAULT_TESTER_RESPONSE_MIN_TIME = 55;
        public const long P3_DEFAULT_TESTER_RESPONSE_MAX_TIME = 5000;
        public const long P4_DEFAULT_TESTER_INTERBYTE_MIN_TIME = 5;
        public const long P4_DEFAULT_TESTER_INTERBYTE_MAX_TIME = 20;
        
		public enum MessageErrorCode
		{
			MessageOK = 0,
			InvalidChecksum,
			NotEnoughData,
			RequestedTooMuchData,
            MessageContainsNoData,
			UnknownError
		};
        		
		public event MessageChangedDelegate ReceivedMessageEvent;
                
		///////////////////////////////////////////////////////////////////////////////////////////////////////////
		//PUBLIC METHODS
		///////////////////////////////////////////////////////////////////////////////////////////////////////////

		public KWP2000Interface()
		{
			mReceiveBuffer = new byte[RECEIVE_BUFFER_SIZE];
			mMessagesPendingSend = new Queue<KWP2000Message>();
                        
			mP1ECUResponseInterByteTimeOut = new Stopwatch();
			mP2ECUResponseTimeOut = new Stopwatch();
			mP3TesterRequestTimeOut = new Stopwatch();
            mDisconnectTimeOut = new Stopwatch();
		}

        public override Protocol CurrentProtocol
        {
            get
            {
                return CommunicationInterface.Protocol.KWP2000;
            }
        }

        public TimingParameterMode CurrentTimingParameterMode
        {
            get { return _CurrentTimingParameterMode; }
            set { _CurrentTimingParameterMode = value; }
        }
        private TimingParameterMode _CurrentTimingParameterMode = TimingParameterMode.Unknown;

        public TimingParameters CurrentTimingParameters
        {
            get
            {
                if (mCurrentTimingParameters == null)
                {
                    CurrentTimingParameterMode = TimingParameterMode.Unknown;
                    mCurrentTimingParameters = new TimingParameters();
                }

                return (TimingParameters)mCurrentTimingParameters.Clone();
            }
            set
            {
                if ((mCurrentTimingParameters == null) || !mCurrentTimingParameters.Equals(value))
                {
                    long oldP2 = mP2ECUResponseMaxTimeCurrent;

                    if (mCurrentTimingParameters != null)
                    {
                        oldP2 = mCurrentTimingParameters.P2ECUResponseTimeMaxMs;
                    }

                    mCurrentTimingParameters = value;

                    if (((mP2ECUResponseMaxTimeCurrent == P2_DEFAULT_ECU_RESPONSE_MAX_TIME) || (oldP2 == mP2ECUResponseMaxTimeCurrent)) 
                        && (oldP2 != mCurrentTimingParameters.P2ECUResponseTimeMaxMs))
                    {
                        mP2ECUResponseMaxTimeCurrent = mCurrentTimingParameters.P2ECUResponseTimeMaxMs;
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("CurrentTimingParameters"));

                    if (mCurrentTimingParameters.Equals(mDefaultTimingParameters))
                    {
                        DisplayStatusMessage("Set timing parameters to defaults.", StatusMessageType.LOG);
                    }
                    else
                    {
                        DisplayStatusMessage("Set timing parameters to new values.", StatusMessageType.LOG);
                    }
                }
            }
        }
        private TimingParameters mCurrentTimingParameters;
        private long mP2ECUResponseMaxTimeCurrent = P2_DEFAULT_ECU_RESPONSE_MAX_TIME;//max time from timings, or longer if response pending

		//EDC15 ECUs do not seem to work with the min time of 5ms, at least when connecting
		//20, 19, 17 worked, 12 didn't work
		private const long P4TesterInterByteTimeMinMsWhenConnectingDefaultValue = 17;
		[DefaultValue(P4TesterInterByteTimeMinMsWhenConnectingDefaultValue)]
        public long P4TesterInterByteTimeMinMsWhenConnecting
        {
            get
            {
                return mP4TesterInterByteTimeMinMsWhenConnecting;
            }
            set
            {
                if (mP4TesterInterByteTimeMinMsWhenConnecting != value)
                {
                    mP4TesterInterByteTimeMinMsWhenConnecting = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("P4TesterInterByteTimeMinMsWhenConnecting"));
                }
            }            
        }
		private long mP4TesterInterByteTimeMinMsWhenConnecting = P4TesterInterByteTimeMinMsWhenConnectingDefaultValue;

        public TimingParameters DefaultTimingParameters
        {
            get
            {
                if (mDefaultTimingParameters == null)
                {
                    mDefaultTimingParameters = new TimingParameters();
                }

                return (TimingParameters)mDefaultTimingParameters.Clone();
            }
            set
            {
                if ((mDefaultTimingParameters == null) || !mDefaultTimingParameters.Equals(value))
                {
                    mDefaultTimingParameters = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("DefaultTimingParameters"));
                }
            }
        }
        private TimingParameters mDefaultTimingParameters;
        
        public void SetTimingParametersToDefaults()
        {
            DisplayStatusMessage("Setting communication timings to defaults.", StatusMessageType.LOG);

            CurrentTimingParameters = DefaultTimingParameters;

            CurrentTimingParameterMode = TimingParameterMode.Unknown;
        }

        //Tested connection addresses with fast init
        //functional 0x33 NO
        //functional 0x10 NO (configured by internal rom handler)
        //functional 0xFE YES (configured by external flash handler)
        //functional 0x02 UNTESTED (configured by external RAM handler)
        //physical 0x11 NO
        //physical 0x01 YES

        public bool ConnectToECUSlowInit(byte connectAddress)
        {
            return ConnectToECU(KWP2000AddressMode.None, connectAddress, KWP2000ConnectionMethod.SlowInit);
        }

        public bool ConnectToECUFastInit(KWP2000AddressMode addressMode, byte connectAddress)
        {
            return ConnectToECU(addressMode, connectAddress, KWP2000ConnectionMethod.FastInit);
        }

        protected bool ConnectToECU(KWP2000AddressMode addressMode, byte connectAddress, KWP2000ConnectionMethod connectionType)
        {
            bool success = false;

            if (ConnectionStatus == ConnectionStatusType.CommunicationTerminated)
            {
                mConnectAddressMode = addressMode;
                mConnectAddress = connectAddress;
                mConnectionType = connectionType;
                mNumConnectionAttemptsRemaining = NumConnectionAttempts;

                //the address we use shouldn't matter
                SetCommunicationPhysicalAddressAndKeyBytes(mConnectAddress, DEFAULT_KEY_BYTE_1, DEFAULT_KEY_BYTE_2);

                ClearMessageBuffers();
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
                    setupStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);
                    setupStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                    if (setupStatus == FTDI.FT_STATUS.FT_OK)
                    {
                        if (!ShouldVerifyDumbMode)
                        {
                            success = true;
                        }
                        else
                        {
                            byte[] dumbTestMessage = { 0xF0 };//don't use something that could be confused for 0x55 sync byte, incase that will confuse ECUs
                            uint numBytesWritten = 0;
                            setupStatus = mFTDIDevice.Write(dumbTestMessage, dumbTestMessage.Length, ref numBytesWritten, 1);

                            if ((setupStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == dumbTestMessage.Length))
                            {
                                byte[] dumbTestMessageEcho = new byte[dumbTestMessage.Length];
                                uint numEchoBytesRead = 0;
                                FTDI.FT_STATUS echoStatus = mFTDIDevice.Read(dumbTestMessageEcho, (uint)dumbTestMessageEcho.Length, ref numEchoBytesRead, 2);

                                //get rid of all data just in case
                                mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                                if ((echoStatus == FTDI.FT_STATUS.FT_OK) && (numEchoBytesRead == dumbTestMessageEcho.Length))
                                {
                                    success = true;

                                    for (int x = 0; (x < dumbTestMessage.Length) && (x < dumbTestMessageEcho.Length); x++)
                                    {
                                        if (dumbTestMessage[x] != dumbTestMessageEcho[x])
                                        {
                                            DisplayStatusMessage("Failed to read test echo from FTDI device. Make sure the cable is in dumb mode and connected to the OBD port.", StatusMessageType.USER);
                                            success = false;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    DisplayStatusMessage("Failed to read test echo from FTDI device.", StatusMessageType.USER);
                                }
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to write test echo to FTDI device.", StatusMessageType.USER);
                            }

                            if (success)
                            {
                                DisplayStatusMessage("Validated FTDI device is in dumb mode.", StatusMessageType.USER);
                            }
                        }
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

            if (success)
            {
                StartSendReceiveThread(SendReceiveThread);
            }

            return success;
        }

		public void DisconnectFromECU()
		{			
			if (ConnectionStatus == ConnectionStatusType.Connected)
			{
				ConnectionStatus = ConnectionStatusType.DisconnectionPending;
				SendMessage((byte)KWP2000ServiceID.StopCommunication);

                if (!mDisconnectTimeOut.IsRunning)
                {
                    mDisconnectTimeOut.Reset();
                    mDisconnectTimeOut.Start();
                }
			}
		}        

		public uint DiagnosticSessionBaudRate
		{
            get
            {
                return mDiagnosticSessionBaudRate;
            }
            set
            {
                lock (mFTDIDevice)
                {
                    if (IsFTDIDeviceOpen())
                    {
                        mFTDIDevice.SetBaudRate(value);
                    }
                }
                mDiagnosticSessionBaudRate = value;
                OnPropertyChanged(new PropertyChangedEventArgs("DiagnosticSessionBaudRate"));
            }
		}
        private uint mDiagnosticSessionBaudRate = (uint)KWP2000BaudRates.BAUD_UNSPECIFIED;

		private const bool ShouldVerifyDumbModeDefaultValue = true;
		[DefaultValue(ShouldVerifyDumbModeDefaultValue)]
        public bool ShouldVerifyDumbMode
        {
            get
            {
                return mShouldVerifyDumbMode;
            }
            set
            {
                if (mShouldVerifyDumbMode != value)
                {
                    mShouldVerifyDumbMode = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("ShouldVerifyDumbMode"));
                }
            }
        }
		private bool mShouldVerifyDumbMode = ShouldVerifyDumbModeDefaultValue;

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

		private const long TimeBetweenSlowInitForKWP2000MSDefaultValue = 1500;//galletto waits 1.5 seconds after kwp1281
		[DefaultValue(TimeBetweenSlowInitForKWP2000MSDefaultValue)]
        public long TimeBetweenSlowInitForKWP2000MS
        {
            get
            {
                return _TimeBetweenSlowInitForKWP2000MS;
            }
            set
            {
                if (_TimeBetweenSlowInitForKWP2000MS != value)
                {
                    _TimeBetweenSlowInitForKWP2000MS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("TimeBetweenSlowInitForKWP2000MS"));
                }
            }
        }
		private long _TimeBetweenSlowInitForKWP2000MS = TimeBetweenSlowInitForKWP2000MSDefaultValue;

		private const long TimeAfterSlowInitBeforeStartCommMessageMSDefaultValue = 280;
		[DefaultValue(TimeAfterSlowInitBeforeStartCommMessageMSDefaultValue)]
        public long TimeAfterSlowInitBeforeStartCommMessageMS
        {
            get
            {
                return _TimeAfterSlowInitBeforeStartCommMessageMS; 
            }
            set
            {
                if (_TimeAfterSlowInitBeforeStartCommMessageMS != value)
                {
                    _TimeAfterSlowInitBeforeStartCommMessageMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("TimeAfterSlowInitBeforeStartCommMessageMS"));
                }
            }
        }
		private long _TimeAfterSlowInitBeforeStartCommMessageMS = TimeAfterSlowInitBeforeStartCommMessageMSDefaultValue;

		private const double SlowInitFiveBaudBitTimeOffsetMSDefaultValue = -0.6;
		[DefaultValue(SlowInitFiveBaudBitTimeOffsetMSDefaultValue)]
        public double SlowInitFiveBaudBitTimeOffsetMS
        {
            get
            {
                return _SlowInitFiveBaudBitTimeOffsetMS;
            }
            set
            {
                if (_SlowInitFiveBaudBitTimeOffsetMS != value)
                {
                    _SlowInitFiveBaudBitTimeOffsetMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("SlowInitFiveBaudBitTimeOffsetMS"));
                }
            }
        }
		private double _SlowInitFiveBaudBitTimeOffsetMS = SlowInitFiveBaudBitTimeOffsetMSDefaultValue;

		private const double FastInitLowHighTimeOffsetMSDefaultValue = 0.0;
		[DefaultValue(FastInitLowHighTimeOffsetMSDefaultValue)]
        public double FastInitLowHighTimeOffsetMS
        {
            get
            {
                return _FastInitLowHighTimeOffsetMS;
            }
            set
            {
                if (_FastInitLowHighTimeOffsetMS != value)
                {
                    _FastInitLowHighTimeOffsetMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("FastInitLowHighTimeOffsetMS"));
                }
            }
        }
		private double _FastInitLowHighTimeOffsetMS = FastInitLowHighTimeOffsetMSDefaultValue;

		//TODO: get rid of all these send message variations
		public KWP2000Message SendMessage(byte serviceID)
		{
			return SendMessage(serviceID, null);
		}

        public KWP2000Message SendMessage(byte serviceID, uint maxNumRetries)
        {
            return SendMessage(serviceID, maxNumRetries, null);
        }

        public KWP2000Message SendMessage(byte serviceID, uint maxNumRetries, byte[] data)
        {
            var message = new KWP2000Message(KWP2000AddressMode.Physical, TESTER_ADDRESS, CommunicationAddress, serviceID, maxNumRetries, data);

            SendMessage(message);

            return message;
        }

		public KWP2000Message SendMessage(byte serviceID, byte[] data)
		{
            var message = new KWP2000Message(KWP2000AddressMode.Physical, TESTER_ADDRESS, CommunicationAddress, serviceID, data);

			SendMessage(message);

            return message;
		}

		public KWP2000Message SendMessage(KWP2000Message message)
		{
			if (message != null)
			{
				lock (mMessagesPendingSend)
				{
					mMessagesPendingSend.Enqueue(message);
				}
			}

            return message;
		}

        public static byte GetPositiveResponseForRequest(byte request)
		{			
			Debug.Assert(IsServiceIDRequest(request), "request is not a request service id");

			return (byte)(request | 0x40);
		}

        public static bool IsServiceIDRequest(byte serviceID)
		{
			return (serviceID & 0x40) == 0;
		}

        public static bool IsServiceIDResponse(byte serviceID)
        {
            return (serviceID & 0x40) != 0;
        }
        
        public static bool IsPositiveResponseToRequest(byte requestServiceID, KWP2000Message response)
        {
            Debug.Assert(IsServiceIDRequest(requestServiceID));
            Debug.Assert(IsServiceIDResponse(response.mServiceID));

            return (requestServiceID | 0x40) == response.mServiceID;
        }

		public static bool IsNegativeResponseToRequest(byte requestServiceID, KWP2000Message response)
		{
			byte responseCode;
			return IsNegativeResponseToRequest(requestServiceID, response, out responseCode);
		}

        public static bool IsNegativeResponseToRequest(byte requestServiceID, KWP2000Message response, out byte responseCode)
        {
            Debug.Assert(IsServiceIDRequest(requestServiceID));
            Debug.Assert(IsServiceIDResponse(response.mServiceID));			

            bool result = false;
			responseCode = 0;

            if ((response.mServiceID == (byte)KWP2000ServiceID.NegativeResponse) && (response.mData != null) && (response.DataLength > 0))
            {
                result = (requestServiceID == response.mData[0]);

				if (response.DataLength > 1)
				{
					responseCode = response.mData[1];
				}
            }

            return result;
        }

		public static bool IsResponseToRequest(byte requestServiceID, KWP2000Message response)
		{
            return IsPositiveResponseToRequest(requestServiceID, response) || IsNegativeResponseToRequest(requestServiceID, response);
		}

        public static bool DoesRequestResponseUseDataSegmentation(byte serviceID)
        {
            //TODO: need to handle all messages that use data segmentation

            if (serviceID == (byte)KWP2000ServiceID.ReadDiagnosticTroubleCodesByStatus)
            {
                return true;
            }

            return false;
        }        

        [Flags]
        public enum KeyByte1 : byte
        {
            LengthInFormatByteSupported = 0x01,
            AdditionalLengthByteSupported = 0x02,
            OneByteHeaderSupported = 0x04,
            TargetSourceAddressInHeaderSupported = 0x08,
            ExtendedTimingParameterSet = 0x10,
            NormalTimingParameterSet = 0x20,
            UnusedAlwaysOne = 0x40,
            OddParityBit = 0x80//only used when key byte comes from slow init I believe
        }

        protected static bool IsKeyByte1ValidKWP2000(byte keyByte1, bool validateParity)
        {
            byte numSetBits = 0;
            for (int x = 0; x < 8; x++)
            {
                if ((keyByte1 & (1 << x)) != 0)
                {
                    numSetBits++;
                }
            }

            if (validateParity)
            {
                //must be odd parity
                if (numSetBits % 2 == 0)
                {
                    return false;
                }
            }

            //always one bit must be one
            if ((keyByte1 & (byte)KeyByte1.UnusedAlwaysOne) == 0)
            {
                return false;
            }

            //only one of the two timing modes can be used, but we allow no timing mode to be set for EDC15
            if ((keyByte1 & (byte)(KeyByte1.NormalTimingParameterSet & KeyByte1.ExtendedTimingParameterSet)) != 0)
            {
                return false;
            }
            
            //must support address in header or one byte header
            if (((keyByte1 & (byte)KeyByte1.TargetSourceAddressInHeaderSupported) | (keyByte1 & (byte)KeyByte1.OneByteHeaderSupported)) == 0)
            {
                return false;
            }

            return true;
        }

        [Flags]
        public enum KeyByte2 : byte
        {
            OddParityBit = 0x80
        }

        protected static bool IsKeyByte2ValidKWP2000(byte keyByte2, bool validateParity)
        {
            byte numSetBits = 0;
            for (int x = 0; x < 8; x++)
            {
                if ((keyByte2 & (1 << x)) != 0)
                {
                    numSetBits++;
                }
            }

            if (validateParity)
            {
                //must be odd parity
                if (numSetBits % 2 == 0)
                {
                    return false;
                }
            }

            return true;
        }

		public bool IsLengthInfoInFormatByteSupported()
		{
            return (CommunicationKeyByte1 & (byte)KeyByte1.LengthInFormatByteSupported) != 0;
		}
		
		public bool IsAdditionalLengthByteSupported()
		{
            return (CommunicationKeyByte1 & (byte)KeyByte1.AdditionalLengthByteSupported) != 0;
		}

		public bool IsOneByteHeaderSupported()
		{
            return (CommunicationKeyByte1 & (byte)KeyByte1.OneByteHeaderSupported) != 0;
		}

		public bool IsTargetSourceAddressInHeaderSupported()
		{
            return (CommunicationKeyByte1 & (byte)KeyByte1.TargetSourceAddressInHeaderSupported) != 0;
		}

		//todo: respect this setting
		public bool IsExtendedTimingParameterSet()
		{
            bool extendedSet = (CommunicationKeyByte1 & (byte)KeyByte1.ExtendedTimingParameterSet) != 0;

			Debug.Assert(extendedSet != IsNormalTimingParameterSet());

			return extendedSet;
		}

		//todo: respect this setting (code currently only supports this setting)
		public bool IsNormalTimingParameterSet()
		{
            bool normalSet = (CommunicationKeyByte1 & (byte)KeyByte1.NormalTimingParameterSet) != 0;

			Debug.Assert(normalSet != IsExtendedTimingParameterSet());

            if (!normalSet)
            {
                //if normal and extended are both false, then we are normal. EDC15 key bytes have no timing set
                normalSet = (CommunicationKeyByte1 & (byte)(KeyByte1.NormalTimingParameterSet | KeyByte1.ExtendedTimingParameterSet)) == 0;
            }

			return normalSet;
		}

		///////////////////////////////////////////////////////////////////////////////////////////////////////////
		//PROTECTED TYPES
		///////////////////////////////////////////////////////////////////////////////////////////////////////////

		protected const uint MAX_CONNECTION_TIMING_OFFSET = 4;
		protected const uint DEFAULT_COMMUNICATION_BAUD_RATE = 10400;
        
        protected const uint MAX_MESSAGE_SIZE = MAX_MESSAGE_DATA_SIZE_WITH_LENGTH_BYTE + 5;
        protected const uint RECEIVE_BUFFER_SIZE = MAX_MESSAGE_SIZE * 20;
		///////////////////////////////////////////////////////////////////////////////////////////////////////////
		//PROTECTED MEMBERS
		///////////////////////////////////////////////////////////////////////////////////////////////////////////
        
        protected KWP2000AddressMode        mConnectAddressMode = KWP2000AddressMode.Physical;
        protected KWP2000ConnectionMethod	mConnectionType = KWP2000ConnectionMethod.SlowInit;
        protected byte                      mConnectAddress;
        		
		protected byte[] mReceiveBuffer = null;
		protected uint mNumBytesInReceiveBuffer = 0;
        protected bool mReceiveBufferIsDirty = false;		
        protected uint mNumPreceedingEchoBytesToIgnore = 0;
        protected byte[] mOutstandingEchoBytes = null;
        
        //data for message pending send
		protected Queue<KWP2000Message> mMessagesPendingSend;
        protected int mNumSendAttemptsForCurrentMessage = 0;
        protected bool mCurrentMessageSentProperly = false;//this currently just means that all bytes in the message were sent, it does not check anything else
        protected bool mCurrentMessageReceivedAnyResponses = false;
        protected bool mCurrentMessageWaitedForAllReplies = false;
        protected bool mCurrentMessageSentFinishedEvent = false;
        protected int mNumConsecutiveUnsolicitedResponses = 0;
        protected int mNumConsecutiveSentMessagesWithoutResponses = 0;

        private class ReceivedMessageEventHolder : EventHolder
        {
            public ReceivedMessageEventHolder(MulticastDelegate multiDel, KWP2000Message message)
                : base(multiDel)
            {
                mMessage = message;
            }

            protected override void BeginInvoke(CommunicationInterface commInterface, Delegate del, AsyncCallback callback, object invokeParam)
            {
                if (del is MessageChangedDelegate)
                {
                    var receiveMessageDel = del as MessageChangedDelegate;
                    var KwP2000CommInterface = commInterface as KWP2000Interface;

#if DEBUG//cut down on work in non debug builds
                    commInterface.LogProfileEventDispatch("Invoking: " + del.Target.ToString() + "." + del.Method.ToString() + " at " + DateTime.Now.ToString("hh:mm:ss.fff"));
#endif
                    receiveMessageDel.BeginInvoke(KwP2000CommInterface, mMessage, callback, invokeParam);
                }
            }

            protected KWP2000Message mMessage;
        }

        private class FinishedReceivingResponsesEventHolder : EventHolder
        {
            public FinishedReceivingResponsesEventHolder(MulticastDelegate multiDel, KWP2000Message message, bool messageSent, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries)
                : base(multiDel)
            {
                mMessage = message;
                mMessageSent = messageSent;
                mReceivedAnyReplies = receivedAnyReplies;
                mWaitedForAllReplies = waitedForAllReplies;
                mNumRetries = numRetries;
            }

            protected override void BeginInvoke(CommunicationInterface commInterface, Delegate del, AsyncCallback callback, object invokeParam)
            {
                if (del is MessageSendFinishedDelegate)
                {
                    var finishedDel = del as MessageSendFinishedDelegate;
                    var KwP2000CommInterface = commInterface as KWP2000Interface;

#if DEBUG//cut down on work in non debug builds
                    commInterface.LogProfileEventDispatch("Invoking: " + del.Target.ToString() + "." + del.Method.ToString() + " at " + DateTime.Now.ToString("hh:mm:ss.fff"));
#endif
                    finishedDel.BeginInvoke(KwP2000CommInterface, mMessage, mMessageSent, mReceivedAnyReplies, mWaitedForAllReplies, mNumRetries, callback, invokeParam);
                }
            }

            protected KWP2000Message mMessage;
            protected bool mMessageSent;
            protected bool mReceivedAnyReplies;
            protected bool mWaitedForAllReplies;
            protected uint mNumRetries;
        }
        
		protected Stopwatch mP1ECUResponseInterByteTimeOut;
		protected Stopwatch mP2ECUResponseTimeOut;
		protected Stopwatch mP3TesterRequestTimeOut;
        protected Stopwatch mDisconnectTimeOut;
        protected uint mNumConnectionAttemptsRemaining = 0;

		///////////////////////////////////////////////////////////////////////////////////////////////////////////
		//PROTECTED METHODS
		///////////////////////////////////////////////////////////////////////////////////////////////////////////

		protected void SetCommunicationPhysicalAddressAndKeyBytes(byte address, byte keyByte1, byte keyByte2)
		{
            //remove the MSB from the key bytes since they are 7 data with parity
            keyByte1 &= 0x7F;
            keyByte2 &= 0x7F;

			if ((CommunicationAddress != address) || (CommunicationKeyByte1 != keyByte1) || (CommunicationKeyByte2 != keyByte2))
			{
				DisplayStatusMessage("Setting Address: 0x" + address.ToString("X2") + " KeyByte1: 0x" + keyByte1.ToString("X2") + " KeyByte2: 0x" + keyByte2.ToString("X2"), StatusMessageType.LOG);

				if ((CommunicationKeyByte1 != keyByte1) || (CommunicationKeyByte2 != keyByte2))
				{
					if ((keyByte1 == KWP_1281_PROTOCOL_KEY_BYTE_1) && (keyByte2 == KWP_1281_PROTOCOL_KEY_BYTE_2))
					{
						DisplayStatusMessage("Switching to KWP1281 session.", StatusMessageType.USER);
					}
					else if (keyByte2 == KWP_2000_PROTOCOL_KEY_BYTE_2)
					{
						DisplayStatusMessage("Switching to KWP2000 session.", StatusMessageType.USER);
					}
					else
					{
						keyByte2 = KWP_2000_PROTOCOL_KEY_BYTE_2;
						DisplayStatusMessage("Unknown protocol, switching to KWP2000 session.", StatusMessageType.USER);
					}

					CommunicationKeyByte1 = keyByte1;
					CommunicationKeyByte2 = keyByte2;
				}

				CommunicationAddress = address;
			}
		}

        [XmlIgnore]
        public byte CommunicationKeyByte1
        {
            get
            {
                return _CommunicationKeyByte1;
            }
            private set
            {
                if (_CommunicationKeyByte1 != value)
                {
                    _CommunicationKeyByte1 = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("CommunicationKeyByte1"));
                }
            }
        }
        private byte _CommunicationKeyByte1 = DEFAULT_KEY_BYTE_1;

        [XmlIgnore]
        public byte CommunicationKeyByte2
        {
            get
            {
                return _CommunicationKeyByte2;
            }
            private set
            {
                if (_CommunicationKeyByte2 != value)
                {
                    _CommunicationKeyByte2 = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("CommunicationKeyByte2"));
                }
            }
        }
        private byte _CommunicationKeyByte2 = DEFAULT_KEY_BYTE_2;

        [XmlIgnore]
        public byte CommunicationAddress
        {
            get
            {
                return _CommunicationAddress;
            }
            private set
            {
                if (_CommunicationAddress != value)
                {
                    _CommunicationAddress = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("CommunicationAddress"));
                }
            }
        }
        private byte _CommunicationAddress = 0;

        public KWP2000DiagnosticSessionType CurrentDiagnosticSessionType
        {
            get
            {
                return _CurrentDiagnosticSessionType;
            }

            set
            {
                if (_CurrentDiagnosticSessionType != value)
                {
                    _CurrentDiagnosticSessionType = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("CurrentDiagnosticSessionType"));

                    SetTimingParametersToDefaults();//we need to reset timing parameters to defaults any time we change the diagnostic session type

                    DisplayStatusMessage("Changed diagnostic session type to: " + _CurrentDiagnosticSessionType, StatusMessageType.LOG);
                }
            }
        }
        private KWP2000DiagnosticSessionType _CurrentDiagnosticSessionType = KWP2000DiagnosticSessionType.InternalUndefined;

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

						//if no message timers are running, maybe because we didn't start a start communication message, start the message timers
						if (!mP1ECUResponseInterByteTimeOut.IsRunning && !mP2ECUResponseTimeOut.IsRunning && !mP3TesterRequestTimeOut.IsRunning && IsConnectionOpen())
						{							
							mP2ECUResponseTimeOut.Start();
							mP3TesterRequestTimeOut.Start();
						}

//Test hack for EDC15
//StartDiagnosticSessionAction.SendStartDiagnosticSessionMessageData(this, KWP2000DiagnosticSessionType.StandardSession, (uint)KWP2000BaudRates.BAUD_UNSPECIFIED);
                    }                    
                    else if (_ConnectionStatus == ConnectionStatusType.Disconnected)
                    {
                        //we can reset the communication now and not wait for the next 
                        //connection attempt because we were never connected
                        shouldReset = !wasConnected;
                    }

                    if (shouldReset)
                    {   
                        CurrentDiagnosticSessionType = KWP2000DiagnosticSessionType.InternalUndefined;
                        DiagnosticSessionBaudRate = (uint)KWP2000BaudRates.BAUD_UNSPECIFIED;
                        
                        mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);
                        mFTDIDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
                        mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                        SetTimingParametersToDefaults();

                        //reset all timers
                        mP1ECUResponseInterByteTimeOut.Reset();
                        mP2ECUResponseTimeOut.Reset();
                        mP3TesterRequestTimeOut.Reset();
                        mDisconnectTimeOut.Reset();

                        mNumSendAttemptsForCurrentMessage = 0;
                        mNumConsecutiveUnsolicitedResponses = 0;

                        mDisconnectTimeOut.Reset();
                        ClearMessageBuffers();
                    }                    
                }                
            }            
        }

        protected override uint GetConnectionAttemptsRemaining()
        {
            return mNumConnectionAttemptsRemaining;
        }

		protected void ClearMessageBuffers()
		{
			lock (mMessagesPendingSend)
			{
				mMessagesPendingSend.Clear();
			}

            ClearQueuedEvents();
            RemoveAllBytesFromReceiveBuffer();
            mNumPreceedingEchoBytesToIgnore = 0;
            mOutstandingEchoBytes = null;
		}
                
        private enum SlowInitConnectionTiming : uint
        {
            W0Min = 2,//min bus high idle time before address is sent
            W1Min = 60,//min time before sync byte is sent after last bit of the address byte
            W1Max = 300,//max time before sync byte is sent after last bit of the address byte
            W2Min = 5,//min time between sync byte and first key byte
            W2Max = 20,//max time between sync byte and first key byte
            W3Min = 0,//min time between first and second key bytes
            W3Max = 20,//max time between first and second key bytes
            W4Min = 25,//min time between last key byte and tester sending last key byte complement, and time between last key byte completement and ECU sending address complement
            W4Max = 50,//max time between last key byte and tester sending last key byte complement, and time between last key byte completement and ECU sending address complement
            W5Min = 300//min idle time before the tester retransmitting an address byte
        };

        protected enum SlowInitParity
        {
            None = -1,
            Even= 0,
            Odd = 1            
        };

        protected enum SlowInitDataBits
        {
            Seven = 7,
            Eight = 8
        };

        protected bool SendSlowInitAddress_FTDI_UsingBreak(byte connectAddress, SlowInitDataBits numDataBits, SlowInitParity parity, out uint numPreceedingBytesToIgnore)
        {
            bool success = false;
            numPreceedingBytesToIgnore = 0;

            lock (mFTDIDevice)
            {
                DisplayStatusMessage("Sending slow init address byte", StatusMessageType.DEV);

                double TICKS_PER_MS = Stopwatch.Frequency / 1000.0;
                double TICKS_PER_200_MS = Stopwatch.Frequency / 5.0;
                long FIVE_BAUD_BIT_TIME_TICKS = (long)(TICKS_PER_200_MS + (TICKS_PER_MS * SlowInitFiveBaudBitTimeOffsetMS));
                FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

                //low start bit
                Stopwatch watch = new Stopwatch(); watch.Start();

                ftdiStatus |= mFTDIDevice.SetBreak(true);//low
                bool isLow = true;

                long currentBitEndTime = FIVE_BAUD_BIT_TIME_TICKS;
                while (watch.ElapsedTicks < currentBitEndTime) ;//busy loop

                int numHighBits = 0;

                //data bits
                for (int y = 0; y < (int)numDataBits; y++)
                {
                    bool isHighBit = (connectAddress & (0x01 << y)) != 0;

                    if (isHighBit)
                    {
                        if(isLow)
                        {
                            ftdiStatus |= mFTDIDevice.SetBreak(false);//high
                            numPreceedingBytesToIgnore++;
                        }

                        numHighBits++;
                        isLow = false;
                    }
                    else
                    {   
                        if(!isLow)
                        {
                            ftdiStatus |= mFTDIDevice.SetBreak(true);//low
                        }

                        isLow = true;
                    }

                    currentBitEndTime += FIVE_BAUD_BIT_TIME_TICKS;
                    while (watch.ElapsedTicks < currentBitEndTime) ;//busy loop
                }

                if (parity != SlowInitParity.None)
                {
                    if((int)parity != numHighBits % 2)
                    {
                        if (isLow)
                        {
                            ftdiStatus |= mFTDIDevice.SetBreak(false);//high
                            numPreceedingBytesToIgnore++;
                        }

                        isLow = false;
                    }
                    else
                    {
                        if (!isLow)
                        {
                            ftdiStatus |= mFTDIDevice.SetBreak(true);//low
                        }

                        isLow = true;
                    }

                    currentBitEndTime += FIVE_BAUD_BIT_TIME_TICKS;
                    while (watch.ElapsedTicks < currentBitEndTime) ;//busy loop
                }

                //high stop bit
                if (isLow)
                {
                    ftdiStatus |= mFTDIDevice.SetBreak(false);//high
                    numPreceedingBytesToIgnore++;
                }

                //currentBitEndTime += FIVE_BAUD_BIT_TIME_TICKS;
                //while (watch.ElapsedTicks < currentBitEndTime) ;//busy loop

				//don't purge because that can cause us to miss the sync byte, just use the low to high transitions we counted

                success = (ftdiStatus == FTDI.FT_STATUS.FT_OK);

                if (!success)
                {
                    DisplayStatusMessage("Failed to send five baud slow init address.", StatusMessageType.LOG);
                }
            }

            return success;
        }

        protected bool SendSlowInitAddress_FTDI_UsingBitBang(byte connectAddress)
        {
            bool success = false;
            
            const byte HIGH_DATA = 0x01;
            const byte LOW_DATA = 0x00;
            const byte BIT_MODE_MASK = 0xFF;//0xFFall pins output, we don't want to receive any data during bit bang. Use 0xFD if you want all outputs except the receive line
            const uint ADDRESS_BAUD_RATE = 5;
            const uint NUM_ADDRESS_BITS = 10;//1 start bit, 8 data bits, 1 stop bit            
            const uint BIT_BANG_ADDRESS_BAUD_RATE = DEFAULT_COMMUNICATION_BAUD_RATE;//800;//184 is the min FTDI baud rate
            const uint BIT_BANG_ADDRESS_BIT_WIDTH = BIT_BANG_ADDRESS_BAUD_RATE / ADDRESS_BAUD_RATE * 4;//divide by 4 makes the times correct :S      
            const uint TOTAL_NUM_BITS = BIT_BANG_ADDRESS_BIT_WIDTH * NUM_ADDRESS_BITS;
            const uint FIVE_BAUD_SEND_TIME_MS = (uint)((((float)NUM_ADDRESS_BITS) / ((float)ADDRESS_BAUD_RATE)) * 1000);//TOTAL_NUM_BITS / BIT_BANG_ADDRESS_BAUD_RATE * 1000;
            byte[] addressData = new byte[TOTAL_NUM_BITS];
            uint numLowAddressEvents = 0;//used by baud rate detection code

//TODO: need to use 7O1 instead of 8N1

            #region CalculateAddressBits
            {
                bool lastBitLow = false;
                int index = 0;

                //start bit
                numLowAddressEvents++;
                lastBitLow = true;
                for (int z = 0; z < BIT_BANG_ADDRESS_BIT_WIDTH; z++)
                {
                    addressData[index] = LOW_DATA;
                    index++;
                }

                //data bits
                for (int y = 0; y < 8; y++)
                {
                    byte value = HIGH_DATA;

                    if ((connectAddress & (0x01 << y)) == 0)
                    {
                        value = LOW_DATA;

                        if (!lastBitLow)
                        {
                            numLowAddressEvents++;
                        }
                    }

                    lastBitLow = (value == LOW_DATA);

                    for (int z = 0; z < BIT_BANG_ADDRESS_BIT_WIDTH; z++)
                    {
                        addressData[index] = value;
                        index++;
                    }
                }

                //stop bit
                for (int z = 0; z < BIT_BANG_ADDRESS_BIT_WIDTH; z++)
                {
                    addressData[index] = HIGH_DATA;
                    index++;
                }
            }
            #endregion

            lock (mFTDIDevice)
            {
                FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

                ftdiStatus |= mFTDIDevice.SetBitMode(BIT_MODE_MASK, FTDI.FT_BIT_MODES.FT_BIT_MODE_ASYNC_BITBANG);
                ftdiStatus |= mFTDIDevice.SetBaudRate(BIT_BANG_ADDRESS_BAUD_RATE);

                //give it enough write timeout to send, and a super small read timeout so we get a quick response
                ftdiStatus |= mFTDIDevice.SetTimeouts(1, FIVE_BAUD_SEND_TIME_MS * 2);//times 2 to be safe

                if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                {
                    DisplayStatusMessage("Sending slow init address byte", StatusMessageType.DEV);

                    Stopwatch watch = new Stopwatch();
                    watch.Reset(); watch.Start();                    

                    //send the connect address at 5 baud
                    uint numBytesWritten = 0;
                    ftdiStatus |= mFTDIDevice.Write(addressData, addressData.Length, ref numBytesWritten, 1);

                    if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == addressData.Length))
                    {
                        //wait for the minimum time to pass
                        while (watch.ElapsedMilliseconds < FIVE_BAUD_SEND_TIME_MS) ;//busy wait
                        watch.Stop();

                        //wait for the 5 baud address to send
                        uint numBytesInTxBuffer = 0;
                        do
                        {
                            ftdiStatus = mFTDIDevice.GetTxBytesWaiting(ref numBytesInTxBuffer);
                        } while ((numBytesInTxBuffer > 0) && (ftdiStatus == FTDI.FT_STATUS.FT_OK));

                        ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_TX);
                        ftdiStatus |= mFTDIDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
                        ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX);//TODO: this purge will occasionally make us miss the key byte
                        ftdiStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);

                        success = (ftdiStatus == FTDI.FT_STATUS.FT_OK);
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to write connect address.", StatusMessageType.LOG);
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to setup FTDI device.", StatusMessageType.LOG);
                }
            }

            return success;
        }

		//send five baud address byte, receive sync byte, receive key byte 1, receive key byte 2, send last key byte complement, maybe receive address complement
        protected bool SendSlowInit(byte connectAddress, SlowInitDataBits numDataBits, SlowInitParity parity, out byte keyByte1, out byte keyByte2)
        {
            uint PRIMARY_SLOW_INIT_BAUD_RATE = 10400;
            uint SECONDARY_SLOW_INIT_BAUD_RATE = 9600;

            bool result = false;
            keyByte1 = 0; keyByte2 = 0;

            lock (mFTDIDevice)
            {
                uint currentBaudRate = PRIMARY_SLOW_INIT_BAUD_RATE;

                FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;                
                ftdiStatus |= mFTDIDevice.SetBaudRate(currentBaudRate);
                ftdiStatus |= mFTDIDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);
                ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                {
                    Stopwatch watch = new Stopwatch();
                    uint numPreceedingBytesToIgnore = 0;

                    if (SendSlowInitAddress_FTDI_UsingBreak(connectAddress, numDataBits, parity, out numPreceedingBytesToIgnore))
                    {
                        //EDC15 ECUs don't seem to send the sync byte for 405ms when the W1Max limit is 300ms
                        //watch.Start();
                        //uint syncByteReadTimeOut = (uint)SlowInitConnectionTiming.W1Max;
                        //mFTDIDevice.SetTimeouts(syncByteReadTimeOut, FTDIDeviceWriteTimeOutMs);

                        //read the sync byte
                        byte[] syncByte = new byte[1 + numPreceedingBytesToIgnore];
                        uint numSyncBytesRead = 0;
                        ftdiStatus |= mFTDIDevice.Read(syncByte, 1 + numPreceedingBytesToIgnore, ref numSyncBytesRead, 2);

                        watch.Reset(); watch.Start();//start timing for first key byte

                        //TODO: handle multiple sync bytes
                                             
                        
                        bool readSyncByte = false;

                        //check if we read the sync byte
                        if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numSyncBytesRead > numPreceedingBytesToIgnore))
                        {
                            readSyncByte = true;

                            if (syncByte[numPreceedingBytesToIgnore] != 0x55)
                            {
                                currentBaudRate = SECONDARY_SLOW_INIT_BAUD_RATE;
                                ftdiStatus |= mFTDIDevice.SetBaudRate(currentBaudRate);

                                DisplayStatusMessage("Read incorrect sync byte, guessing baud rate is actually " + currentBaudRate + ".", StatusMessageType.LOG);

                                if (ftdiStatus != FTDI.FT_STATUS.FT_OK)
                                {
                                    DisplayStatusMessage("Failed to set new baud rate based on sync byte.", StatusMessageType.LOG);
                                    readSyncByte = false;
                                }
                            }
                        }
                        else
                        {
                            readSyncByte = true;

                            currentBaudRate = SECONDARY_SLOW_INIT_BAUD_RATE;
                            ftdiStatus |= mFTDIDevice.SetBaudRate(currentBaudRate);

                            DisplayStatusMessage("Failed to read sync byte, guessing baud rate is actually " + currentBaudRate + ".", StatusMessageType.LOG);

                            if (ftdiStatus != FTDI.FT_STATUS.FT_OK)
                            {
                                DisplayStatusMessage("Failed to set new baud rate based on sync byte.", StatusMessageType.LOG);
                                readSyncByte = false;
                            }
                        }

                        if (readSyncByte)
                        {
                            ftdiStatus |= mFTDIDevice.SetTimeouts(1, FTDIDeviceWriteTimeOutMs);

                            if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                            {
                                //read the key bytes
                                List<byte> keyBytes = new List<byte>();
                                uint numBytesRead = 0;
                                byte[] keyByteData = new byte[2];
                                uint numKeyBytesToRead = 2;//try to read two key bytes to start

                                long lastReadTime = 0;
								uint keyByteTimeOut = (uint)SlowInitConnectionTiming.W2Max;
                                uint lastChanceKeyByteTimeOut = keyByteTimeOut * 2;
                                //keep reading key bytes until we hit the time out, but if we don't have at least 2 keep reading until double the time out
                                do
                                {
                                    lastReadTime = watch.ElapsedMilliseconds;
                                    ftdiStatus = mFTDIDevice.Read(keyByteData, numKeyBytesToRead, ref numBytesRead, 1);

                                    if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead > 0))
                                    {
                                        //ignore old bit bang data
                                        //if(keyByteData[0] != 0xFF)
                                        {
                                            watch.Reset(); watch.Start();//restart timing after each key byte

                                            //only try to read one byte at a time and change the time outs after the first key byte
                                            if (keyBytes.Count == 0)
                                            {
                                                keyByteTimeOut = (uint)SlowInitConnectionTiming.W3Max;
                                                numKeyBytesToRead = 1;
                                            }

                                            for (int x = 0; x < numBytesRead; ++x)
                                            {
                                                keyBytes.Add(keyByteData[x]);
                                            }
                                        }
                                    }
                                } while ( (lastReadTime < keyByteTimeOut) || ((keyBytes.Count < 2) && (lastReadTime < lastChanceKeyByteTimeOut)) );

                                //did we read enough key bytes?
                                //todo: according to the spec there could be more than two key bytes
                                if (keyBytes.Count >= 2)
                                {
                                    //switch data format
                                    //ftdiStatus |= mFTDIDevice.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8, FTDI.FT_STOP_BITS.FT_STOP_BITS_1, FTDI.FT_PARITY.FT_PARITY_NONE);
                                    mFTDIDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);

                                    //send the complement of the last key byte
                                    byte[] keyByteComp = new byte[1];
                                    keyByteComp[0] = (byte)~keyBytes[keyBytes.Count - 1];

                                    //don't reset timer after trying to read last key byte
                                    while (watch.ElapsedMilliseconds < (uint)SlowInitConnectionTiming.W4Min) ;//busy loop

                                    uint numBytesWritten = 0;
                                    ftdiStatus = mFTDIDevice.Write(keyByteComp, keyByteComp.Length, ref numBytesWritten, 2);

                                    watch.Stop();

                                    if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == keyByteComp.Length))
									{
										if (mConsumeTransmitEcho)
										{
											//read the echo                                        
											byte[] echo = new byte[1];
											ftdiStatus = mFTDIDevice.Read(echo, (uint)echo.Length, ref numBytesRead, 2);

											if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead == echo.Length))
											{
												if (echo[0] == keyByteComp[0])
												{
													result = true;
												}
												else
												{
													DisplayStatusMessage("Key byte complement echo didn't match.", StatusMessageType.LOG);
												}
											}
											else
											{
												DisplayStatusMessage("Failed to read echo of key byte complement.", StatusMessageType.LOG);
											}
										}
										else
										{
											result = true;
										}

										if(result)
										{
											//remove the MSB from the key bytes since they are 7 data with parity
											keyByte1 = (byte)(keyBytes[0] & 0x7F);
											keyByte2 = (byte)(keyBytes[1] & 0x7F);

											DiagnosticSessionBaudRate = currentBaudRate;
										}
									}
                                    else
                                    {
                                        DisplayStatusMessage("Failed to write key byte complement.", StatusMessageType.LOG);
                                    }
                                }
                                else
                                {
                                    DisplayStatusMessage("Not enough key bytes, read: " + keyBytes.Count, StatusMessageType.LOG);
                                }
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to set time outs to read key bytes.", StatusMessageType.LOG);
                            }
                        }
                        else
                        {
//TODO: need to better handle the ftdi status here, multiple errors can send us here depending of if we detect baud rate or not
                            if ((numSyncBytesRead > 0) && (syncByte[0] != 0x55))
                            {
                                DisplayStatusMessage("Read incorrect sync byte: " + syncByte[0].ToString("X2"), StatusMessageType.LOG);
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to read sync byte. Read " + numSyncBytesRead + " bytes.", StatusMessageType.LOG);
                            }
                        }

						//need to read the address complement if this slow init started a KWP2000 session
						if (result && IsKeyByte1ValidKWP2000(keyByte1, false) && IsKeyByte2ValidKWP2000(keyByte2, false))
						{
							//read the complement of the address
                            byte[] addressComplement = new byte[1];
							uint numBytesRead = 0;
                            ftdiStatus = mFTDIDevice.Read(addressComplement, (uint)addressComplement.Length, ref numBytesRead, 2);

							if ((ftdiStatus != FTDI.FT_STATUS.FT_OK) || (numBytesRead != addressComplement.Length) || (addressComplement[0] != (byte)~connectAddress))
							{
								result = false;
								DisplayStatusMessage("Failed to read address complement.", StatusMessageType.LOG);
							}
						}
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to setup FTDI device for slow init.", StatusMessageType.LOG);
                }

                if (!result)
                {
                    //failed to connect so purge the buffers
                    ClearMessageBuffers();                    
                    mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                }

                mFTDIDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);
                mFTDIDevice.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8, FTDI.FT_STOP_BITS.FT_STOP_BITS_1, FTDI.FT_PARITY.FT_PARITY_NONE);                
            }

            return result;
        }

        private enum KWP1281BlockTitle : byte
        {
            RequestClearDTCs = 0x05,
            RequestEndCommunication = 0x06,
            RequestReadDTCs = 0x07,
            Acknowledge = 0x09,
            RequestReadMeasuringGroup = 0x29,
            ReponseReadMeasuringGroup = 0xE7,
            ASCIIData = 0xF6,
            ResponseReadDTCs = 0xFC
        };

		const uint KWP1281_TesterMinTimeToSendByteComplementMSDefaultValue = 2;
		[DefaultValue(KWP1281_TesterMinTimeToSendByteComplementMSDefaultValue)]
        public uint KWP1281_TesterMinTimeToSendByteComplementMS
        {
            get
            {
                return _KWP1281_TesterMinTimeToSendByteComplementMS;
            }
            set
            {
                if (_KWP1281_TesterMinTimeToSendByteComplementMS != value)
                {
                    _KWP1281_TesterMinTimeToSendByteComplementMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("KWP1281_TesterMinTimeToSendByteComplementMS"));
                }
            }

        }
		private uint _KWP1281_TesterMinTimeToSendByteComplementMS = KWP1281_TesterMinTimeToSendByteComplementMSDefaultValue;

		private const uint KWP1281_TesterMinTimeToSendResponseMessageMSDefaultValue = 25;//55
		[DefaultValue(KWP1281_TesterMinTimeToSendResponseMessageMSDefaultValue)]
        public uint KWP1281_TesterMinTimeToSendResponseMessageMS
        {
            get
            {
                return _KWP1281_TesterMinTimeToSendResponseMessageMS;
            }
            set
            {
                if (_KWP1281_TesterMinTimeToSendResponseMessageMS != value)
                {
                    _KWP1281_TesterMinTimeToSendResponseMessageMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("KWP1281_TesterMinTimeToSendResponseMessageMS"));
                }
            }
        }
		private uint _KWP1281_TesterMinTimeToSendResponseMessageMS = KWP1281_TesterMinTimeToSendResponseMessageMSDefaultValue;

		private const uint KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMSDefaultValue = 1;
		[DefaultValue(KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMSDefaultValue)]
        public uint KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS
        {
            get
            {
                return _KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS;
            }
            set
            {
                if (_KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS != value)
                {
                    _KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS"));
                }
            }
        }
		private uint _KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS = KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMSDefaultValue;
        
        private bool KWP1281ReadByte(out byte readByte, bool sendComp)
        {
            bool result = false;
            Stopwatch stopwatch = new Stopwatch();

            //read the byte
            byte[] tempData = new byte[1];
            uint numBytesRead = 0;
            FTDI.FT_STATUS ftdiStatus = mFTDIDevice.Read(tempData, 1, ref numBytesRead, 2);
            
            stopwatch.Start();
                        
            if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead == 1))
            {
                readByte = tempData[0];

                if (sendComp)
                {
                    //send the complement ack
                    byte[] tempDataComplement = new byte[1];
                    tempDataComplement[0] = (byte)~tempData[0];
                    uint numBytesWritten = 0;

                    while (stopwatch.ElapsedMilliseconds < KWP1281_TesterMinTimeToSendByteComplementMS) ;//busy loop
                                        
                    ftdiStatus = mFTDIDevice.Write(tempDataComplement, 1, ref numBytesWritten, 2);

                    if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == 1))
                    {
						if (mConsumeTransmitEcho)
						{
							//read the echo
							byte[] tempDataEcho = new byte[1];
							ftdiStatus = mFTDIDevice.Read(tempDataEcho, 1, ref numBytesRead, 2);

							if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead == 1) && (tempDataEcho[0] == tempDataComplement[0]))
							{
								result = true;
							}
							else
							{
								DisplayStatusMessage("Failed to read KWP1281 byte complement echo.", StatusMessageType.DEV);
							}
						}
						else
						{
							result = true;
						}
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to write KWP1281 byte complement.", StatusMessageType.DEV);
                    }
                }
                else
                {
                    result = true;
                }
            }
            else
            {
                readByte = 0;
                DisplayStatusMessage("Failed to read KWP1281 byte.", StatusMessageType.DEV);
            }

            return result;
        }

        private bool KWP1281SendByte(byte sendByte, bool requireComp)
        {
            bool result = false;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            //write the byte
            byte[] tempData = new byte[1];
            tempData[0] = sendByte;
            uint numBytesWritten = 0;

            while (stopwatch.ElapsedMilliseconds < KWP1281_TesterMinTimeToSendNextByteAfterReceivingComplementMS) ;//busy loop

            FTDI.FT_STATUS ftdiStatus = mFTDIDevice.Write(tempData, 1, ref numBytesWritten, 2);

            if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == 1))
            {
				if(mConsumeTransmitEcho)
				{
					//read the echo
					byte[] tempEchoData = new byte[1];
					uint numBytesRead = 0;
					ftdiStatus = mFTDIDevice.Read(tempEchoData, 1, ref numBytesRead, 2);

					if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead == 1) && (tempData[0] == tempEchoData[0]))
					{
						result = true;
					}
					else
					{
						DisplayStatusMessage("Failed to read byte echo", StatusMessageType.DEV);
					}
				}

				if (requireComp)
				{
					result = false;

					//read the complement response
					byte[] tempDataComplement = new byte[1];
					uint numBytesRead = 0;
					ftdiStatus = mFTDIDevice.Read(tempDataComplement, 1, ref numBytesRead, 2);

					if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead == 1) && (tempData[0] == (byte)~tempDataComplement[0]))
					{
						result = true;
					}
					else
					{
						DisplayStatusMessage("Failed to read byte complement", StatusMessageType.DEV);
					}
				}
				else
				{
					result = true;
				}                
            }
            else
            {
                DisplayStatusMessage("Failed to write byte", StatusMessageType.DEV);
            }

            return result;
        }

        private class KWP1281Block
        {
            public byte mBlockCounter;
            public byte mBlockTitle;
            public byte[] mBlockData;
        }

        private bool KWP1281ReadBlock(out KWP1281Block messageBlock)
        {
            bool result = false;
            messageBlock = null;

            byte blockLength = 0;
            if (KWP1281ReadByte(out blockLength, true))
            {
                byte blockCounter = 0;
                if (KWP1281ReadByte(out blockCounter, true))
                {
                    byte blockTitle = 0;
                    if (KWP1281ReadByte(out blockTitle, true))
                    {
                        bool readAllData = true;                        
                        byte[] blockData = null;
                        int dataSize = Math.Max(0, blockLength - 3);

                        if (dataSize > 0)
                        {
                            blockData = new byte[dataSize];
                            for (int currentBlockDataIndex = 0; currentBlockDataIndex < dataSize; currentBlockDataIndex++)
                            {
                                if (!KWP1281ReadByte(out blockData[currentBlockDataIndex], true))
                                {
                                    DisplayStatusMessage("Failed to read KWP1281 block data byte.", StatusMessageType.LOG);
                                    readAllData = false;
                                    break;
                                }
                            }
                        }

                        if (readAllData)
                        {
                            byte blockEnd = 0;
                            if (KWP1281ReadByte(out blockEnd, false))
                            {
                                if (blockEnd == 0x03)
                                {
                                    result = true;

                                    messageBlock = new KWP1281Block();
                                    messageBlock.mBlockCounter = blockCounter;
                                    messageBlock.mBlockTitle = blockTitle;
                                    messageBlock.mBlockData = blockData;
                                }
                                else
                                {
                                    DisplayStatusMessage("Final byte of KWP1281 message was not block end byte.", StatusMessageType.LOG);
                                }
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to read KWP1281 block end byte.", StatusMessageType.LOG);
                            }
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to read KWP1281 block title byte.", StatusMessageType.LOG);
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to read KWP1281 block counter byte.", StatusMessageType.LOG);
                }
            }
            else
            {
                DisplayStatusMessage("Failed to read KWP1281 block length byte.", StatusMessageType.LOG);
            }

            if (result)
            {
                DisplayStatusMessage("KWP1281 read block", StatusMessageType.LOG);
#if DEBUG
                string statusMessage = "KWP1281 read block data: 0x" + blockLength.ToString("X2") + ", 0x" + messageBlock.mBlockCounter.ToString("X2") + ", 0x" + messageBlock.mBlockTitle.ToString("X2") + ", 0x";

                if (messageBlock.mBlockData != null)
                {
                    foreach (byte curByte in messageBlock.mBlockData)
                    {
                        statusMessage += curByte.ToString("X2") + ", 0x";
                    }
                }

                statusMessage += "03";
                DisplayStatusMessage(statusMessage, StatusMessageType.DEV);

                //check for ASCII block
                if (messageBlock.mBlockTitle == (byte)KWP1281BlockTitle.ASCIIData)
                {
                    string asciiMessage = "KWP1281 block ASCII: \"";

                    if (messageBlock.mBlockData != null)
                    {
                        foreach (char curChar in messageBlock.mBlockData)
                        {
                            asciiMessage += curChar;
                        }
                    }

                    asciiMessage += "\"";

                    DisplayStatusMessage(asciiMessage, StatusMessageType.DEV);
                }
#endif
            }
            else
            {
                DisplayStatusMessage("KWP1281 read block failed.", StatusMessageType.LOG);
            }

            return result;
        }

        private bool KWP1281SendBlock(KWP1281Block messageBlock)
        {
            bool result = false;

            byte blockLength = 3;

            if(messageBlock.mBlockData != null)
            {
                blockLength += (byte)messageBlock.mBlockData.Length;
            }

            if (KWP1281SendByte(blockLength, true))
            {
                if (KWP1281SendByte(messageBlock.mBlockCounter, true))
                {
                    if (KWP1281SendByte(messageBlock.mBlockTitle, true))
                    {
                        bool wroteAllData = true;

                        if (messageBlock.mBlockData != null)
                        {
                            for (int currentBlockDataIndex = 0; currentBlockDataIndex < messageBlock.mBlockData.Length; currentBlockDataIndex++)
                            {
                                if (!KWP1281SendByte(messageBlock.mBlockData[currentBlockDataIndex], true))
                                {
                                    DisplayStatusMessage("Failed to write KWP1281 block data byte.", StatusMessageType.LOG);
                                    wroteAllData = false;
                                    break;
                                }
                            }
                        }

                        if (wroteAllData)
                        {
                            if (KWP1281SendByte(0x03, false))
                            {
                                result = true;
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to write KWP1281 block end byte.", StatusMessageType.LOG);
                            }
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to write KWP1281 block title byte.", StatusMessageType.LOG);
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to write KWP1281 block counter byte.", StatusMessageType.LOG);
                }
            }
            else
            {
                DisplayStatusMessage("Failed to write KWP1281 block length byte.", StatusMessageType.LOG);
            }

#if DEBUG
            string statusMessage = "KWP1281 sending block data: 0x" + blockLength.ToString("X2") + ", 0x" + messageBlock.mBlockCounter.ToString("X2") + ", 0x" + messageBlock.mBlockTitle.ToString("X2") + ", 0x";
            
            if(messageBlock.mBlockData != null)
            {
                foreach(byte curByte in messageBlock.mBlockData)
                {
                    statusMessage += curByte.ToString("X2") + ", 0x";
                }
            }
            
            statusMessage += "03";
            DisplayStatusMessage(statusMessage, StatusMessageType.DEV);
#endif

            if (result)
            {
                DisplayStatusMessage("KWP1281 sent block", StatusMessageType.LOG);
            }
            else
            {
                DisplayStatusMessage("KWP1281 send block FAILED", StatusMessageType.LOG);
            }

            return result;
        }

        private bool KWP1281SendAckBlock(KWP1281Block messageBlock)
        {
            bool result = false;

            KWP1281Block ackBlock = new KWP1281Block();
            ackBlock.mBlockCounter = (byte)(messageBlock.mBlockCounter + 1);
            ackBlock.mBlockTitle = (byte)KWP1281BlockTitle.Acknowledge;
            ackBlock.mBlockData = null;
            result = KWP1281SendBlock(ackBlock);

            return result;
        }

        private bool KWP1281ReadBlockAndAck(out KWP1281Block messageBlock)
        {
            bool result = KWP1281ReadBlock(out messageBlock);

            if (result)
            {
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    while (stopwatch.ElapsedMilliseconds < KWP1281_TesterMinTimeToSendResponseMessageMS) ;//busy loop
                }

                result = KWP1281SendAckBlock(messageBlock);
            }
        
            return result;
        }

        private bool KWP1281RunShortSession()
        {
            bool sendOK = true;
            string ASCIIData = "";

            const uint KWP1281_ECU_MAX_TIME_UNTIL_RESPONSE_MS = 1200;
            mFTDIDevice.SetTimeouts(KWP1281_ECU_MAX_TIME_UNTIL_RESPONSE_MS, FTDIDeviceWriteTimeOutMs);

            while (sendOK)
            {
                byte lastBlockCounter = 0;

                KWP1281Block messageBlock = null;
                sendOK = KWP1281ReadBlock(out messageBlock);

                if (sendOK)
                {
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();
                        while (stopwatch.ElapsedMilliseconds < KWP1281_TesterMinTimeToSendResponseMessageMS) ;//busy loop
                    }

                    lastBlockCounter = messageBlock.mBlockCounter;

                    if (messageBlock.mBlockTitle != (byte)KWP1281BlockTitle.Acknowledge)
                    {
                        sendOK = KWP1281SendAckBlock(messageBlock);
                        if (sendOK)
                        {
                            lastBlockCounter++;
                        }
                        else
                        {
                            DisplayStatusMessage("Failed to send acknowledgement block", StatusMessageType.DEV);
                        }
                    }
                    else
                    {
                        KWP1281Block endBlock = new KWP1281Block();
                        endBlock.mBlockTitle = (byte)KWP1281BlockTitle.RequestEndCommunication;
                        endBlock.mBlockData = null;
                        endBlock.mBlockCounter = (byte)(lastBlockCounter + 1);

                        sendOK = KWP1281SendBlock(endBlock);
                        if (sendOK)
                        {
                            lastBlockCounter++;
                            break;
                        }
                        else
                        {
                            DisplayStatusMessage("Failed to send end block", StatusMessageType.DEV);
                        }
                    }

                    //check for ASCII block
                    if (messageBlock.mBlockTitle == (byte)KWP1281BlockTitle.ASCIIData)
                    {
                        if (messageBlock.mBlockData != null)
                        {
                            foreach (char curChar in messageBlock.mBlockData)
                            {
                                ASCIIData += curChar;
                            }
                        }
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to read block", StatusMessageType.DEV);
                }
            }

            if ((ASCIIData.Length > 0) && sendOK)
            {
                DisplayStatusMessage("KWP1281 connect info: " + ASCIIData, StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Failed to read KWP1281 connect info.", StatusMessageType.USER);
            }

            mFTDIDevice.SetTimeouts(FTDIDeviceReadTimeOutMs, FTDIDeviceWriteTimeOutMs);

            return sendOK;
        }

        protected bool Connect_SlowInit_FTDI()
        {
			bool slowInitSuccess = false;
            byte keyByte1; byte keyByte2;

            DisplayStatusMessage("Starting slow init connection.", StatusMessageType.USER);

            lock(mFTDIDevice)
            {
                //wait for the required idle time
                Stopwatch watch = new Stopwatch(); watch.Start();
                const uint idleTime = 2600;//VW says they need a min of 2.6 seconds between slow inits//(uint)SlowInitConnectionTiming.W0Min + (uint)SlowInitConnectionTiming.W5Min;
                Thread.Sleep((int)idleTime);
                while (watch.ElapsedMilliseconds < idleTime) ;//busy wait
                watch.Stop();

                DisplayStatusMessage("Connecting to address 0x" + mConnectAddress.ToString("X2") + ".", StatusMessageType.USER);
                slowInitSuccess = SendSlowInit(mConnectAddress, SlowInitDataBits.Seven, SlowInitParity.Odd, out keyByte1, out keyByte2);
                                
                if (slowInitSuccess)
                {
                    DisplayStatusMessage("Slow init succeeded.", StatusMessageType.USER);

                    SetCommunicationPhysicalAddressAndKeyBytes(mConnectAddress, keyByte1, keyByte2);

                    //did the slow init result in a KWP1281 session?
                    if((keyByte1 == KWP_1281_PROTOCOL_KEY_BYTE_1) && (keyByte2 == KWP_1281_PROTOCOL_KEY_BYTE_2))
                    {
                        KWP1281RunShortSession();

                        //wait additional time to give the ECU time to switch to KWP2000
                        watch.Reset(); watch.Start();
                        while (watch.ElapsedMilliseconds < TimeBetweenSlowInitForKWP2000MS) ;

                        DisplayStatusMessage("Connecting to address 0x" + mConnectAddress.ToString("X2") + ".", StatusMessageType.USER);
                        slowInitSuccess = SendSlowInit(mConnectAddress, SlowInitDataBits.Seven, SlowInitParity.Odd, out keyByte1, out keyByte2);

                        if (slowInitSuccess)
                        {
                            DisplayStatusMessage("Slow init succeeded.", StatusMessageType.USER);
                        }
                        else
                        {
                            DisplayStatusMessage("Slow init failed.", StatusMessageType.USER);
                        }
                    }
                }
                else
                {
                    DisplayStatusMessage("Slow init failed.", StatusMessageType.USER);
                }

                byte connectAddressOverride = mConnectAddress;
                                
                if (slowInitSuccess)
                {
                    SetCommunicationPhysicalAddressAndKeyBytes(mConnectAddress, keyByte1, keyByte2);

                    //did the second slow init result in another KWP1281 session?
                    if((mConnectAddress == DEFAULT_ECU_SLOWINIT_KWP1281_ADDRESS) && (keyByte1 == KWP_1281_PROTOCOL_KEY_BYTE_1) && (keyByte2 == KWP_1281_PROTOCOL_KEY_BYTE_2))
                    {
                        KWP1281RunShortSession();

                        //wait additional time to give the ECU time to switch to KWP2000                    
                        watch.Reset(); watch.Start();
                        while (watch.ElapsedMilliseconds < TimeBetweenSlowInitForKWP2000MS) ;

                        //we don't set the mConnectAddress here because we are overriding it and don't want to set it
                        connectAddressOverride = DEFAULT_ECU_SLOWINIT_KWP2000_ADDRESS;
                        DisplayStatusMessage("Connecting to address 0x" + connectAddressOverride.ToString("X2") + ".", StatusMessageType.USER);
                        slowInitSuccess = SendSlowInit(connectAddressOverride, SlowInitDataBits.Seven, SlowInitParity.Even, out keyByte1, out keyByte2);

                        if (slowInitSuccess)
                        {
                            DisplayStatusMessage("Slow init succeeded.", StatusMessageType.USER);
                        }
                        else
                        {
                            DisplayStatusMessage("Slow init failed.", StatusMessageType.USER);
                        }
                    }
                }
                                
                if (slowInitSuccess)
                {
                    //need to update the mConnectAddress here because we had to override it to get here
                    mConnectAddress = connectAddressOverride;
                    SetCommunicationPhysicalAddressAndKeyBytes(mConnectAddress, keyByte1, keyByte2);

					slowInitSuccess = false;

                    //did the slow init result in something other than a KWP1281 session?
                    if(keyByte2 != KWP_1281_PROTOCOL_KEY_BYTE_2)//EDC15 sends a strange key byte 2 for KWP2000 fast init it seems
                    {
                        if (IsKeyByte1ValidKWP2000(keyByte1, false) && IsKeyByte2ValidKWP2000(keyByte2, false))
                        {
#if SEND_START_COMM_AFTER_SLOW_INIT
							{
								watch.Reset(); watch.Start();

								var startCommMessage = new KWP2000Message(mConnectAddressMode, TESTER_ADDRESS, mConnectAddress, KWP2000ServiceID.StartCommunication, 0, null);
								SendMessage(startCommMessage);//queue the message, so we know to expect a response

								while (watch.ElapsedMilliseconds < TimeAfterSlowInitBeforeStartCommMessageMS) ;//busy loop, give the ecu time to get ready to receive the message, Galletto waits 280ms

								slowInitSuccess = TransmitMessage(startCommMessage, false, 0);

								if (slowInitSuccess)
								{
									DisplayStatusMessage("Sending start communication request.", StatusMessageType.USER);
								}
								else
								{
									DisplayStatusMessage("Failed to transmit KWP2000 StartCommunication message.", StatusMessageType.LOG);
								}
							}
#else
							{
								slowInitSuccess = true;
								ConnectionStatus = ConnectionStatusType.Connected;
							}
#endif
                        }
                        else
                        {
                            DisplayStatusMessage("Invalid KWP2000 key bytes: 0x" + keyByte1.ToString("X2") + " 0x" + keyByte2.ToString("X2"), StatusMessageType.LOG);
                        }
                    }
                }
                
                if(slowInitSuccess && ((keyByte2 != KWP_1281_PROTOCOL_KEY_BYTE_2) && (keyByte2 != KWP_2000_PROTOCOL_KEY_BYTE_2)))
                {
                    DisplayStatusMessage("Unknown protocol type defined by second key byte: 0x" + keyByte2.ToString("X2"), StatusMessageType.LOG);
                }
            }

			return slowInitSuccess;
        }

		protected void OLD_ConnectionThread_FastInit_FTDI_UsingBitBang()
        {
            const long idleTime = 325;//300 min
            const float lowTime = 0.025f;
            const float highTime = 0.025f;

            const uint BIT_BANG_BAUD_RATE = DEFAULT_COMMUNICATION_BAUD_RATE;
            const uint BIT_WIDTH = 4;//big bang clock is a multiple, supposed to be 16
            const float BAUD_RATE_BIT_TIME = 1.0f / (float)BIT_BANG_BAUD_RATE;

            const uint numLowBits = (uint)(lowTime / BAUD_RATE_BIT_TIME);
            const uint numHighBits = (uint)(highTime / BAUD_RATE_BIT_TIME);

			var startCommMessage = new KWP2000Message(mConnectAddressMode, TESTER_ADDRESS, mConnectAddress, (byte)KWP2000ServiceID.StartCommunication, 0, null);
            byte[] startCommMessageBytes = startCommMessage.GetMessageDataBytes(this);
            const uint MESSAGE_BIT_WIDTH = (BIT_BANG_BAUD_RATE / DEFAULT_COMMUNICATION_BAUD_RATE);
            uint numMessageBits = (uint)startCommMessageBytes.Length * 10 * MESSAGE_BIT_WIDTH;//1 start, 8 data, 1 stop

            uint numInterMessageByteBits = (uint)(((float)P4TesterInterByteTimeMinMsWhenConnecting + 2) / 1000.0f / BAUD_RATE_BIT_TIME);

            byte[] connectionBitData = new byte[(numLowBits + numHighBits + numMessageBits + (numInterMessageByteBits * (startCommMessageBytes.Length - 1))) * BIT_WIDTH];//will be all zeros

#region CalculateConnectionBitData
            const byte HIGH_DATA = 0x01;
            const byte LOW_DATA = 0x00;
            const byte BIT_MODE_MASK = 0xFF;

            int index = 0;

            //low period
            for (int x = 0; x < (numLowBits * BIT_WIDTH); x++)
            {
                connectionBitData[index] = LOW_DATA;
                index++;
            }

            //high period
            for (int x = 0; x < (numHighBits * BIT_WIDTH); x++)
            {
                connectionBitData[index] = HIGH_DATA;
                index++;
            }

            //message bytes
            for (uint x = 0; x < startCommMessageBytes.Length; x++)
            {
                //start bit
                for (int z = 0; z < BIT_WIDTH * MESSAGE_BIT_WIDTH; z++)
                {
                    connectionBitData[index] = LOW_DATA;
                    index++;
                }

                //data bits
                for (int y = 0; y < 8; y++)
                {
                    byte value = LOW_DATA;

                    if ((startCommMessageBytes[x] & (0x01 << y)) != 0)
                    {
                        value = HIGH_DATA;
                    }

                    for (int z = 0; z < BIT_WIDTH * MESSAGE_BIT_WIDTH; z++)
                    {
                        connectionBitData[index] = value;
                        index++;
                    }
                }
                
                //stop bit
                for (int z = 0; z < BIT_WIDTH * MESSAGE_BIT_WIDTH; z++)
                {
                    connectionBitData[index] = HIGH_DATA;
                    index++;
                }

                //inter message byte time
                if(x < startCommMessageBytes.Length - 1)
                {
                    for(int z = 0; z < (BIT_WIDTH * numInterMessageByteBits); z++)
                    {
                        connectionBitData[index] = HIGH_DATA;
                        index++;
                    }
                }
            }
#endregion

            Stopwatch watch = new Stopwatch();
            long offset = 0;
            uint connectionAttempt = 0;
            FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

            while ((Math.Abs(offset) < MAX_CONNECTION_TIMING_OFFSET) && (ConnectionStatus != ConnectionStatusType.Connected) && (ftdiStatus == FTDI.FT_STATUS.FT_OK))
            {
                bool waitingForConnect = false;

                lock (mFTDIDevice)
                {
                    connectionAttempt++;
                    DisplayStatusMessage("Connection attempt: " + connectionAttempt, StatusMessageType.USER);
                    DisplayStatusMessage("Connecting using timing offset: " + offset + "ms", StatusMessageType.LOG);
                    
                    ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                    ftdiStatus |= mFTDIDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
                    ftdiStatus |= mFTDIDevice.SetBaudRate(BIT_BANG_BAUD_RATE);

                    //wait for the required idle time
                    watch.Reset(); watch.Start();
                    Thread.Sleep((int)idleTime);
					while (watch.ElapsedMilliseconds < idleTime);//busy wait
                    watch.Stop();

                    ftdiStatus |= mFTDIDevice.SetBitMode(BIT_MODE_MASK, FTDI.FT_BIT_MODES.FT_BIT_MODE_ASYNC_BITBANG);

                    if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                    {
                        uint numBytesWritten = 0;
                        watch.Reset(); watch.Start();
                        ftdiStatus |= mFTDIDevice.Write(connectionBitData, connectionBitData.Length, ref numBytesWritten, 1);

                        if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesWritten == connectionBitData.Length))
                        {
                            //uint numBytesInTxBuffer = 0;
                            //do
                            //{
                            //    ftdiStatus = mFTDIDevice.GetTxBytesWaiting(ref numBytesInTxBuffer);
                            //} while ((numBytesInTxBuffer > 0) && (ftdiStatus == FTDI.FT_STATUS.FT_OK));

const float messageBitTime = 1000.0f / DEFAULT_COMMUNICATION_BAUD_RATE;
float messageTime = startCommMessageBytes.Length * 10 * messageBitTime;
long interByteTime = (P4TesterInterByteTimeMinMsWhenConnecting + 2) * (startCommMessageBytes.Length - 1);
long totalConTime = 25 + 25 + (long)(messageTime) + interByteTime;
while (watch.ElapsedMilliseconds < totalConTime) ;
                                                        
                            ftdiStatus |= mFTDIDevice.SetBitMode(0xFF, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET);
                            ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX);
                            ftdiStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);

                            if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                            {
                                SendMessage(startCommMessage);//queue the message, so we know to expect a response

                                waitingForConnect = true;
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to get bytes waiting, set baud rate, or purge receive buffer on FTDI device", StatusMessageType.LOG);
                            }
                        }
                        else
                        {
                            DisplayStatusMessage("Failed to write connection data to FTDI device", StatusMessageType.LOG);
                        }

                        watch.Stop();
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to set bit mode or baud rate on FTDI device.", StatusMessageType.LOG);
                    }

                    if (!waitingForConnect)
                    {
                        //failed to connect so purge the buffers
                        ClearMessageBuffers();
                        mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                    }
                }

                if (waitingForConnect)
                {
                    StartSendReceiveThread(SendReceiveThread);

                    while (true)
                    {
                        //wait for response
                        Thread.Sleep((int)mP2ECUResponseMaxTimeCurrent);

                        lock (mFTDIDevice)
                        {
							if (!IsCurrentMessagePendingSend((byte)KWP2000ServiceID.StartCommunication) || (ConnectionStatus != ConnectionStatusType.ConnectionPending))
                            {
                                break;
                            }
                        }
                    }
                }

                if (ConnectionStatus != ConnectionStatusType.Connected)
                {
                    KillSendReceiveThread();

                    DisplayStatusMessage(string.Format("Failed to connect with offset: {0}ms", offset), StatusMessageType.LOG);

                    offset *= -1;

                    if (offset >= 0)
                    {
                        offset++;
                    }
                }
            }

            lock (mFTDIDevice)
            {
                if (ConnectionStatus != ConnectionStatusType.Connected)
                {
                    DisplayStatusMessage("Could not connect.", StatusMessageType.USER);

                    //we need to pretend the status changed so operations can detect the connect failed
                    ConnectionStatus = ConnectionStatusType.Disconnected;
                }
            }
        }
						
		protected void OLD_ConnectionThread_FastInit_FTDI_Using360Baud()
		{
			long offset = 0;
            const long idleTime = 325;//300 min
			const long connectTime = 50;//25ms low, then 25ms high            

			KWP2000Message startCommMessage = new KWP2000Message(mConnectAddressMode, TESTER_ADDRESS, mConnectAddress, (byte)KWP2000ServiceID.StartCommunication, 0, null);

            const UInt32 CONNECTION_BAUD_RATE = 360; //N, 8, 1 (9/360 = 0.025), one low start bit, eight data bits, one high stop bit

			byte[] lowBuffer = { 0x00 };

            uint connectionAttempt = 0;
            FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

			//while ((Math.Abs(offset) < MAX_CONNECTION_TIMING_OFFSET) && (ConnectionStatus != ConnectionStatusType.Connected) && (ftdiStatus == FTDI.FT_STATUS.FT_OK))
			{
				long connectStartTime = 0;
				long connectEndTime = 0;
				long idleEndTime = 0;
                long messageSendTime = 0;
                long messageSendFinishTime = 0;
                DateTime watchStartTime = DateTime.Now;
				bool waitingForConnect = false;
				
				Stopwatch watch = new Stopwatch();
				
				lock (mFTDIDevice)
				{
                    Debug.Assert(!AnyMessagesPendingSend());

                    connectionAttempt++;
                    DisplayStatusMessage("Connection attempt: " + connectionAttempt, StatusMessageType.USER);
                    DisplayStatusMessage("Connecting using timing offset: " + offset + "ms", StatusMessageType.LOG);

                    watch.Reset(); watch.Start();					
                    watchStartTime = DateTime.Now;

					//wait for the required idle time
					idleEndTime = watch.ElapsedMilliseconds + idleTime;
                    Thread.Sleep((int)idleTime);
					while (watch.ElapsedMilliseconds < idleEndTime);//busy wait

                    //set the baud rate and purge the device
                    ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX);
                    ftdiStatus |= mFTDIDevice.SetBaudRate(CONNECTION_BAUD_RATE);

                    if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                    {
                        //connectStartTime = watch.ElapsedMilliseconds;
                        
                        //one byte of zeros at 360 baud should pull the line low for 25ms, one low start bit, eight low data bits, one high stop bit						
                        //bool wroteBytes = TransmitBytesConsumeEcho(lowBuffer);

bool wroteBytes = true;
uint numBytesWritten = 0;
connectStartTime = watch.ElapsedMilliseconds;
ftdiStatus |= mFTDIDevice.Write(lowBuffer, lowBuffer.Length, ref numBytesWritten, 1);


                        if (wroteBytes)
						{
byte[] tempBuffer = new byte[1];
uint numBytesRead = 0;
ftdiStatus |= mFTDIDevice.Read(tempBuffer, 1, ref numBytesRead, 2);

                            SendMessage(startCommMessage);//queue the message so we know we are waiting for a response

                            //wait for the fast init time of 50ms, which results in 25ms of the line being high
//byte[] messageData = startCommMessage.GetMessageDataBytes(this);
connectEndTime = connectStartTime + connectTime + 3;
                            //connectEndTime = connectStartTime + connectTime - 2 + offset;
                            
                            while (watch.ElapsedMilliseconds < connectEndTime) ;//busy wait

                            //change to the communication baud rate and send the connection message
                            ftdiStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);

                            messageSendTime = watch.ElapsedMilliseconds;
                            bool sentMessage = TransmitMessage(startCommMessage, false, (uint)lowBuffer.Length);

//TODO: min inter byte time P4
//ftdiStatus |= mFTDIDevice.Write(messageData, messageData.Length, ref numBytesWritten);
//ftdiStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);
//numBytesWaitingWrite = 255;
//do
//{    
//    messageSendTime = watch.ElapsedMilliseconds;
//    mFTDIDevice.GetTxBytesWaiting(ref numBytesWaitingWrite);
//} while (numBytesWaitingWrite > 0);
//ftdiStatus |= mFTDIDevice.SetBaudRate(DEFAULT_COMMUNICATION_BAUD_RATE);
//bool sentMessage = true;
//mIsWaitingForResponse = true;
//TODO: consume echo


                            if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && sentMessage)
                            {
                                waitingForConnect = true;
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to write connection message to FTDI device", StatusMessageType.LOG);
                            }

                            messageSendFinishTime = watch.ElapsedMilliseconds;
						}
						else
						{
							DisplayStatusMessage("Failed to write low byte to FTDI device", StatusMessageType.LOG);
						}
					}
					else
					{
						DisplayStatusMessage("Failed to set baud rate on FTDI device.", StatusMessageType.LOG);
					}

					if (!waitingForConnect)
					{
						//failed to connect so purge the buffers
                        ClearMessageBuffers();
						mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
					}
				}

                DisplayStatusMessage(string.Format("Idle end: {0}ms Connect start: {1}ms Connect end: {2}ms Message Send Time: {3}ms Message Send Finish Time: {4}ms", idleEndTime, connectStartTime, connectEndTime, messageSendTime, messageSendFinishTime), StatusMessageType.LOG);
                DisplayStatusMessage("Time base: " + watchStartTime.ToString("hh:mm:ss.fff"), StatusMessageType.LOG);

                //if (waitingForConnect)
                //{
                //    StartSendReceiveThread(SendReceiveThread);

                //    while (true)
                //    {
                //        //wait for response
                //        Thread.Sleep((int)mP2ECUResponseMaxTimeCurrent);

                //        lock (mFTDIDevice)
                //        {
                //            if (!IsCurrentMessagePendingSend(KWP2000ServiceID.StartCommunication) || (ConnectionStatus != ConnectionStatusType.ConnectionPending))
                //            {
                //                break;
                //            }
                //        }
                //    }
                //}

                //if (ConnectionStatus != ConnectionStatusType.Connected)
                //{
                //    KillSendReceiveThread();

                //    DisplayStatusMessage(string.Format("Failed to connect with offset: {0}ms", offset), StatusMessageType.LOG);
                    
                //    offset *= -1;

                //    if (offset >= 0)
                //    {
                //        offset++;
                //    }
                //}

				watch.Stop();
			}

            lock (mFTDIDevice)
            {
               mFTDIDevice.SetDTR(true); //enable receive on self powered devices						

                if (ConnectionStatus != ConnectionStatusType.Connected)
                {
                    DisplayStatusMessage("Could not connect.", StatusMessageType.USER);

                    //we need to pretend the status changed so operations can detect the connect failed
                    ConnectionStatus = ConnectionStatusType.Disconnected;
                }
            }
		}

		protected bool Connect_FastInit_FTDI_UsingBreak()
		{
            const long idleTimeMS = 2600;//VW needs 2.6 seconds between inits//325;//300 min
            long lowTimeTicks = (long)(Stopwatch.Frequency * (25.0 + FastInitLowHighTimeOffsetMS) / 1000.0);//25ms
            long highTimeTicks = (long)(Stopwatch.Frequency * (25.0 + FastInitLowHighTimeOffsetMS) / 1000.0);//25ms
            
            bool waitingForConnect = false;

            DisplayStatusMessage("Starting fast init connection.", StatusMessageType.USER);
                        
			lock (mFTDIDevice)
			{
                DisplayStatusMessage("Connecting to address 0x" + mConnectAddress.ToString("X2") + ".", StatusMessageType.USER);

                if (mFTDIDevice.SetBaudRate((uint)KWP2000BaudRates.BAUD_DEFAULT) == FTDI.FT_STATUS.FT_OK)
                {
                    DiagnosticSessionBaudRate = (uint)KWP2000BaudRates.BAUD_DEFAULT;

                    Stopwatch watch = new Stopwatch();
                    //DisplayStatusMessage(string.Format("Timer is high resolution: {0}", Stopwatch.IsHighResolution));
                    //DisplayStatusMessage(string.Format("Timer frequency: {0}", Stopwatch.Frequency));

					var startCommMessage = new KWP2000Message(mConnectAddressMode, TESTER_ADDRESS, mConnectAddress, (byte)KWP2000ServiceID.StartCommunication, 0, null);
                    SendMessage(startCommMessage);//queue the message

                    byte[] startCommMessageDataBytes = startCommMessage.GetMessageDataBytes(this);

                    if (mFTDIDevice.SetBreak(false) == FTDI.FT_STATUS.FT_OK)//let the line return to normal high state
                    {                        
                        watch.Start();
                        long idleEndTime = watch.ElapsedMilliseconds + idleTimeMS;                        
                        Thread.Sleep((int)idleTimeMS);//OK to sleep because we don't care if we wait for too long
                        while (watch.ElapsedMilliseconds < idleEndTime) ;

                        //timing is more accurate when done before setting break
                        //lowEndTime = watch.ElapsedMilliseconds + lowTime + offset;
                        long lowEndTime = watch.ElapsedTicks + lowTimeTicks;

                        if (mFTDIDevice.SetBreak(true) == FTDI.FT_STATUS.FT_OK)//pull the line low
                        {
                            while (watch.ElapsedTicks < lowEndTime) ;

                            //timing is more accurate when done before setting break
                            //highEndTime = lowEndTime + highTime + offset;
                            long highEndTime = lowEndTime + highTimeTicks;

                            if (mFTDIDevice.SetBreak(false) == FTDI.FT_STATUS.FT_OK)//restore the line to high
                            {
                                //don't need to purge since we will just plan on a dirty echo with one byte from the low to high transition from the fast init
                                //mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX);//purge the echo from pulling the line low

                                while (watch.ElapsedTicks < highEndTime) ;

                                if (TransmitMessage(startCommMessageDataBytes, false, 1))
                                {
                                    waitingForConnect = true;
                                }
                                else
                                {
                                    DisplayStatusMessage("Failed to write connection message to FTDI device.", StatusMessageType.LOG);
                                }
                            }
                            else
                            {
                                DisplayStatusMessage("Failed to clear break on FTDI device.", StatusMessageType.LOG);
                            }
                        }
                        else
                        {
                            DisplayStatusMessage("Failed to set break on FTDI device.", StatusMessageType.LOG);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to clear break on FTDI device.", StatusMessageType.LOG);
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to set baud rate for fast init.", StatusMessageType.LOG);
                }

                if (!waitingForConnect)
                {
                    //failed to connect so purge the buffers
                    ClearMessageBuffers();
                    mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);
                }

                mFTDIDevice.SetDTR(true); //enable receive on self powered devices		
			}

            if (waitingForConnect)
            {
                DisplayStatusMessage("Fast init sent, sending start communication request.", StatusMessageType.USER);
            }
            else
            {
                DisplayStatusMessage("Fast init failed.", StatusMessageType.USER);
            }

            return waitingForConnect;
		}

        protected uint GetRemainingSpaceInReceiveBuffer()
        {
            return (uint)(mReceiveBuffer.Length - mNumBytesInReceiveBuffer);
        }

        protected void AppendBytesToReceiveBuffer(byte[] newBytes, uint numBytes)
        {
            uint remainingBufferSpace = GetRemainingSpaceInReceiveBuffer();

            if (remainingBufferSpace < numBytes)
            {
                Debug.Fail("receive buffer is full");
                DisplayStatusMessage("Receive buffer is full, dropping received data", StatusMessageType.LOG);
                
                numBytes = remainingBufferSpace;
            }

#if LOG_RECEIVED_DATA
    		//string fileName = "CommunicationLogReceived.bin";
    		string fileName = "CommunicationLog.bin";
    		System.IO.FileStream stream = null;

    		if (!System.IO.File.Exists(fileName))
    		{
    			stream = System.IO.File.Create(fileName);
    		}
    		else
    		{
    			stream = System.IO.File.OpenWrite(fileName);
    		}

    		stream.Seek(0, System.IO.SeekOrigin.End);
    		stream.Write(bytesReadBuffer, 0, (int)numBytesRead);
    		stream.Close();
#endif
            Buffer.BlockCopy(newBytes, 0, mReceiveBuffer, (int)mNumBytesInReceiveBuffer, (int)numBytes);
            mNumBytesInReceiveBuffer += numBytes;
            mReceiveBufferIsDirty = true;
#if DEBUG
            string statusMessage = "Read data: ";
            for (int x = 0; x < numBytes; x++)
            {
                statusMessage += string.Format("{0:X2}", newBytes[x]) + ", ";
            }
            DisplayStatusMessage(statusMessage, StatusMessageType.DEV);						
#endif
        }

        [Conditional("LOG_KWP2000_PERFORMANCE")]
        protected void LogKWP2000Performance(string perfLog)
        {
            DisplayStatusMessage(perfLog, StatusMessageType.LOG);
        }

        protected bool ReadAndAppendToReceiveBuffer(uint numBytesToReceive)
        {
            uint oldNumBytesInreceiveBuffer = mNumBytesInReceiveBuffer;

            numBytesToReceive = Math.Min(GetRemainingSpaceInReceiveBuffer(), numBytesToReceive);

            if (numBytesToReceive > 0)
            {
    	    	LogKWP2000Performance("Read message bytes");

                uint numBytesRead = 0;
                byte[] bytesReadBuffer = new byte[numBytesToReceive];
                FTDI.FT_STATUS ftdiStatus = mFTDIDevice.Read(bytesReadBuffer, numBytesToReceive, ref numBytesRead, 2);

                if ((ftdiStatus == FTDI.FT_STATUS.FT_OK) && (numBytesRead > 0))
                {
                    AppendBytesToReceiveBuffer(bytesReadBuffer, numBytesRead);
                    ConsumeEchoFromReceiveBuffer();
                }
            }
            else
            {
                Debug.Fail("Receive buffer is full");
                DisplayStatusMessage("Receive buffer is full.", StatusMessageType.LOG);
            }

            bool readMoreData = (mNumBytesInReceiveBuffer > oldNumBytesInreceiveBuffer);

            if (readMoreData)
            {
                //restart the interbyte timer because we read data
                mP1ECUResponseInterByteTimeOut.Reset(); mP1ECUResponseInterByteTimeOut.Start();
            }

            return readMoreData;
        }

		protected void SendReceiveThread()
		{            
            DisplayStatusMessage("Send receive thread now started.", StatusMessageType.LOG);
            ConnectionStatus = ConnectionStatusType.Disconnected;

            bool p1TimerExpiredFlushReceiveBuffer = false;
            
            while ((ConnectionStatus != ConnectionStatusType.Disconnected) || (mNumConnectionAttemptsRemaining > 0))
			{
				lock (mFTDIDevice)
				{
                    #region HandleConnecting
                    while ((ConnectionStatus == ConnectionStatusType.Disconnected) && (mNumConnectionAttemptsRemaining > 0))
                    {
                        mNumConnectionAttemptsRemaining--;

                        //if a connection is open, a fast init will not cause the connection to reset according to spec.
                        //you have to wait for the connection to time out for it to close according to the spec.
                        long testerTimeOut = P3_DEFAULT_TESTER_RESPONSE_MAX_TIME;

                        if (mP3TesterRequestTimeOut.IsRunning && (mP3TesterRequestTimeOut.ElapsedMilliseconds < testerTimeOut))
                        {
                            DisplayStatusMessage("Waiting for the previous tester response timeout to expire.", StatusMessageType.USER);
                            Thread.Sleep((int)(testerTimeOut - mP3TesterRequestTimeOut.ElapsedMilliseconds));
                            mP3TesterRequestTimeOut.Reset();
                        }

                        //when we set the connection status to pending it resets all of the connection state and timers
                        ConnectionStatus = ConnectionStatusType.ConnectionPending;
                        bool sentInit = false;
                                                
                        if(mConnectionType == KWP2000ConnectionMethod.FastInit)
                        {                            
                            sentInit = Connect_FastInit_FTDI_UsingBreak();
                        }
                        else
                        {
                            sentInit = Connect_SlowInit_FTDI();
                        }

                        if (!sentInit)
                        {
                            ConnectionStatus = ConnectionStatusType.Disconnected;
                        }
                    }
                    #endregion

                    if (p1TimerExpiredFlushReceiveBuffer)
                    {
                        //stop the inter byte timer because we are no longer waiting for more bytes
						//we reset it here, because we use this timer running to prevent p2 and p3 from checking time outs
                        mP1ECUResponseInterByteTimeOut.Reset();
                    }

                    //update timers because we are checking for pending data
                    //updated in reverse order of sensitivity                    
                    long p3TimeAtLastRead = mP3TesterRequestTimeOut.IsRunning ? mP3TesterRequestTimeOut.ElapsedMilliseconds : 0;
                    long p2TimeAtLastRead = mP2ECUResponseTimeOut.IsRunning ? mP2ECUResponseTimeOut.ElapsedMilliseconds : 0;
                    long p1TimeAtLastRead = mP1ECUResponseInterByteTimeOut.IsRunning ? mP1ECUResponseInterByteTimeOut.ElapsedMilliseconds : 0;

                    uint numBytesToReceive = 0;
                    FTDI.FT_STATUS ftdiStatus = mFTDIDevice.GetRxBytesAvailable(ref numBytesToReceive, 4);

    				LogKWP2000Performance("Checked read queue, " + numBytesToReceive + " bytes in queue");

                    while ((numBytesToReceive > 0) || mReceiveBufferIsDirty || p1TimerExpiredFlushReceiveBuffer)
    				{
                        //update timers because we are reading
                        //updated in reverse order of sensitivity
                        p3TimeAtLastRead = mP3TesterRequestTimeOut.IsRunning ? mP3TesterRequestTimeOut.ElapsedMilliseconds : 0;
                        p2TimeAtLastRead = mP2ECUResponseTimeOut.IsRunning ? mP2ECUResponseTimeOut.ElapsedMilliseconds : 0;
                        p1TimeAtLastRead = mP1ECUResponseInterByteTimeOut.IsRunning ? mP1ECUResponseInterByteTimeOut.ElapsedMilliseconds : 0;

                        if (numBytesToReceive > 0)
                        {
                            if (ReadAndAppendToReceiveBuffer(numBytesToReceive))
                            {
                                //reset the last read time, because we read data, the timer is internally reset in the ReadAndAppendToReceiveBuffer function                                    
                                p1TimeAtLastRead = 0;                                
                            }
                        }                        
                        numBytesToReceive = 0;

                        if (mP1ECUResponseInterByteTimeOut.IsRunning)
                        {
                            //if the p1 timer restarted again because we received data, then don't flush the buffer
                            p1TimerExpiredFlushReceiveBuffer = false;
                        }

                        if (mReceiveBufferIsDirty || p1TimerExpiredFlushReceiveBuffer)
                        {
                            if (p1TimerExpiredFlushReceiveBuffer)
                            {
                                DisplayStatusMessage("Double checking receive buffer for embedded messages before flushing receive buffer due to P1 ECU response inter byte time out.", StatusMessageType.LOG);
                            }

//Should we even bother checking the receive buffer for messages until P1 times out?

                            //keep processing the receive buffer until we can't find anymore messages.
                            //if a message is received this will handle setting the max p2 time out.
                            //should return true when a message is received even if the message has invalid address, service ID, etc because we need to restart the timers.
                            uint additionalBytesNeededForMessage = 0;
                            while (ProcessReceiveBuffer(out additionalBytesNeededForMessage, p1TimerExpiredFlushReceiveBuffer))
                            {
                                //are there no bytes left in the receive buffer after generating complete messages?
                                if (mNumBytesInReceiveBuffer == 0)
                                {   
                                    //stop the response inter byte timer because we have no partial messages in the receive buffer
                                    mP1ECUResponseInterByteTimeOut.Reset();
                                    p1TimeAtLastRead = 0;

                                    //restart the p3 timer because we just received the last complete message in the buffer,
                                    //unless the timer is already running because the p1 time out restarted it
                                    if (!mP3TesterRequestTimeOut.IsRunning)
                                    {
                                        mP3TesterRequestTimeOut.Reset(); mP3TesterRequestTimeOut.Start();
                                        p3TimeAtLastRead = 0;
                                    }

                                    //restart the p2 timer because we just received the last complete message in the buffer,
                                    //unless the timer is already running because the p1 time out restarted it
                                    if (!mP2ECUResponseTimeOut.IsRunning)
                                    {
                                        mP2ECUResponseTimeOut.Reset(); mP2ECUResponseTimeOut.Start();
                                        p2TimeAtLastRead = 0;
                                    }
                                }

                                Debug.Assert(mP3TesterRequestTimeOut.IsRunning || mP1ECUResponseInterByteTimeOut.IsRunning);
                            }

                            if (p1TimerExpiredFlushReceiveBuffer && (mNumBytesInReceiveBuffer > 0))
                            {
                                DisplayStatusMessage("Flushing " + mNumBytesInReceiveBuffer + " bytes from receive buffer due to P1 ECU response inter byte time out.", StatusMessageType.LOG);
                                RemoveAllBytesFromReceiveBuffer();
                            }

                            //are there still bytes left in the receive buffer after checking for complete messages?
                            if(mNumBytesInReceiveBuffer > 0)
                            {                                
                                Debug.Assert(mP1ECUResponseInterByteTimeOut.IsRunning);

                                //the spec says we should stop p2 and p3 here, but we don't in case we receive garbage that may screw up our timings

                                //we have a partial message, we should try to receive more
                                numBytesToReceive = additionalBytesNeededForMessage;

                                if (numBytesToReceive > 0)
                                {
                                    DisplayStatusMessage("Received incomplete message, reading " + additionalBytesNeededForMessage + " more bytes even though none appear to be waiting.", StatusMessageType.DEV);
                                }
                            }

                            p1TimerExpiredFlushReceiveBuffer = false;
                        }
    				}
    				                                        
                    #region HandleP1TimeOut
                    //we check a copy of the time from when we did the read in case we stalled between the read and this time out check
                    if (p1TimeAtLastRead > CurrentTimingParameters.P1ECUInterByteTimeMaxMs)
                    {
                        Debug.Assert(mP1ECUResponseInterByteTimeOut.IsRunning);
                        
                        DisplayStatusMessage("P1 ecu response inter byte time out expired with: " + p1TimeAtLastRead + " ms.", StatusMessageType.DEV);
                        
						
                        //make sure there isn't any more data coming by trying to read one more byte                        
                        if (ReadAndAppendToReceiveBuffer(1) == false)
                        {
							//we don't stop the p1 timer here because we use it to prevent p2 and p3 from checking time outs
                            p1TimerExpiredFlushReceiveBuffer = true;
                        }
                        
                        //we should restart the p2 and p3 timers according to the spec, but we never stopped them when receiving data
                    }
                    #endregion

                    #region HandleP2TimeOut
                    //The p2 time out should be checked after anything could stop the timer, but before anything could start the timer.
					//This timer starts when we finshed sending or receiving a message and stops when we start sending or receiving a message
					//We use the time of p2 at the last read because that is the last time we actually read from the ecu, incase there is a still in between the read and this check

//TODO: remove the times 2 once we find the issue with response latency
                    if (!mP1ECUResponseInterByteTimeOut.IsRunning && (p2TimeAtLastRead > mP2ECUResponseMaxTimeCurrent * 2))
					{
                        Debug.Assert(!mP1ECUResponseInterByteTimeOut.IsRunning);
                        Debug.Assert(mP2ECUResponseTimeOut.IsRunning);
                        Debug.Assert(mP3TesterRequestTimeOut.IsRunning);

                        if (IsExpectingResponses())
                        {
                            mCurrentMessageWaitedForAllReplies = true;

                            //only finish the message if it received responses, otherwise we need to wait for message retry code to determine if it is sent again or finished
                            if (mCurrentMessageReceivedAnyResponses)
                            {
                                //stop waiting because the ECU has already responded and we have now timed out waiting for more responses
                                FinishedSendingCurrentMessage();
                            }
                            else
                            {
                                DisplayStatusMessage("Message received no replies.", StatusMessageType.LOG);
                            }
                        }
                                                
                        //stop the timer because the ECU didn't respond within the required time out
                        mP2ECUResponseTimeOut.Reset();
                        p2TimeAtLastRead = 0;
                    }
                    #endregion

                    #region HandleSendingMessages
                    //The p3 timeout should be checked after anything can stop the timer, but before the timer could be started
					//p3 is stopped when we start receiving a message, and restarted when we finish sending a message or finish receiving a message

					//don't send messages until we have finished waiting for the ecu to respond, and min time for a response has passed
					//remember it is possible that the p2 timer is using the extra long busy time which is longer than the p3 min time										
                    if (!mP1ECUResponseInterByteTimeOut.IsRunning && (p3TimeAtLastRead >= CurrentTimingParameters.P3TesterResponseTimeMinMs) 
                            && (ConnectionStatus != ConnectionStatusType.Disconnected) && !IsExpectingResponses())
					{
						LogKWP2000Performance("P3 past min time");

                        if (IsOutstandingEcho())
                        {                            
                            DisplayStatusMessage("Failed to read echo within time limit. Clearing remaining expected echo bytes.", StatusMessageType.LOG);

                            mOutstandingEchoBytes = null;
                            mNumPreceedingEchoBytesToIgnore = 0;                            
                        }

                        //if we are approaching the max time between responses, and no messages are pending, insert a tester present, but only if fully connected
						if ((p3TimeAtLastRead > (CurrentTimingParameters.P3TesterResponseTimeMaxMs * 0.5)) && !AnyMessagesPendingSend() && (ConnectionStatus == ConnectionStatusType.Connected))
						{
                            //01 response required, 02 response not required
                            byte[] testerPresentData = { 0x01 };
							SendMessage((byte)KWP2000ServiceID.TesterPresent, testerPresentData);
						}

                        LogKWP2000Performance("Any messages pending send: " + AnyMessagesPendingSend());
                        LogKWP2000Performance("Is expecting responses: " + IsExpectingResponses());

                        //are any messages pending send?
                        while (AnyMessagesPendingSend() && (ConnectionStatus != ConnectionStatusType.Disconnected) && !IsExpectingResponses())
						{
                            var nextMessage = mMessagesPendingSend.Peek();

                            //is it OK to try to send the message (again) ?
                            if (mNumSendAttemptsForCurrentMessage <= nextMessage.mMaxNumRetries)
                            {
                                if (mNumSendAttemptsForCurrentMessage > 0)
                                {
                                    DisplayStatusMessage("Resending message. Send attempts: " + (mNumSendAttemptsForCurrentMessage + 1), StatusMessageType.LOG);
                                }

                                if (TransmitMessage(nextMessage, true, 0))//this will internally reset P2 and P3 timers
                                {
                                    p2TimeAtLastRead = 0;
                                    p3TimeAtLastRead = 0;
                                }

                                //break out of the loop because we sent the message, or we were unable to send the message
                                break;
                            }
                            //can't send the message again, already tried enough times
                            else
                            {
                                DisplayStatusMessage("Failed to send message " + mNumSendAttemptsForCurrentMessage + " times, message send failed.", StatusMessageType.LOG);

                                //We have already tried sending this message enough times, so give up.
                                //The message may have failed to send or may have not received any response.
                                FinishedSendingCurrentMessage();
                            }
						}
                    }
                    #endregion

                    #region HandleP3TimeOut
                    //are we connected and have not successfully sent a message within the max time?
                    if (!mP1ECUResponseInterByteTimeOut.IsRunning && (p3TimeAtLastRead > CurrentTimingParameters.P3TesterResponseTimeMaxMs) && !IsExpectingResponses()
                        && ((ConnectionStatus != ConnectionStatusType.Disconnected) && (ConnectionStatus != ConnectionStatusType.DisconnectionPending)))
                    {
                        Debug.Assert(mP3TesterRequestTimeOut.IsRunning);

                        //try to read one more byte to see if any data is waiting
                        if (ReadAndAppendToReceiveBuffer(1) == false)
                        {
                            DisplayStatusMessage("Disconnecting because there was no successful communication within " + CurrentTimingParameters.P3TesterResponseTimeMaxMs + "ms.", StatusMessageType.USER);
                            ConnectionStatus = ConnectionStatusType.Disconnected;
                        }
                    }
                    #endregion
                }

                #region HandleDisconnectionTimeOut
                //handle disconnection time out
                if (ConnectionStatus == ConnectionStatusType.DisconnectionPending)
                {
                    const long DISCONNECT_TIME_OUT = 5000;

                    if (mDisconnectTimeOut.ElapsedMilliseconds > DISCONNECT_TIME_OUT)
                    {
                        DisplayStatusMessage("Disconnecting because disconnect operation took too long.", StatusMessageType.LOG);
                        ConnectionStatus = ConnectionStatusType.Disconnected;
                    }
                }
                #endregion

                if (!mP1ECUResponseInterByteTimeOut.IsRunning && !mP2ECUResponseTimeOut.IsRunning && !mP3TesterRequestTimeOut.IsRunning && IsConnectionOpen())
                {
                    Debug.Fail("Connection is open and P1, P2, and P3 timers are not running");
                    DisplayStatusMessage("BAD! Communication connection is open and no message timers are running. Please report to NefMoto.", StatusMessageType.USER);

                    mP2ECUResponseTimeOut.Start();//restart the timer to cause a time out
                    mP3TesterRequestTimeOut.Start();//restart the timer to cause a time out
                }

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
        
        protected void FinishedSendingCurrentMessage()
        {
            if (!mCurrentMessageSentFinishedEvent)
            {
                KWP2000Message finishedMessage = null;

                lock (mMessagesPendingSend)
                {
                    if (mMessagesPendingSend.Count > 0)
                    {
                        finishedMessage = mMessagesPendingSend.Dequeue();
                    }
                }

                if(finishedMessage != null)
                {
                    switch (finishedMessage.mServiceID)
                    {
						case (byte)KWP2000ServiceID.StartCommunication:
                        {
                            if (!mCurrentMessageReceivedAnyResponses)
                            {
                                //this is here so that fast init displays something when it fails to connect
                                DisplayStatusMessage("Start communication request did not receive any response.", StatusMessageType.USER);

                                //we didn't receive a response to the start communication message, after sending with no retries
                                ConnectionStatus = ConnectionStatusType.Disconnected;
                            }

                            break;
                        }
						case (byte)KWP2000ServiceID.StopCommunication:
                        {   
                            if (!mCurrentMessageReceivedAnyResponses)
                            {
                                DisplayStatusMessage("Disconnecting because no response was received for the Stop Communication message.", StatusMessageType.USER);

                                //we didn't receive a response to the stop communication message, after sending with the default num retries
                                ConnectionStatus = ConnectionStatusType.Disconnected;
                            }

                            break;
                        }
						case (byte)KWP2000ServiceID.TesterPresent:
                        {
                            if (!mCurrentMessageReceivedAnyResponses)
                            {
                                DisplayStatusMessage("Disconnecting because no response was received for the Tester Present message.", StatusMessageType.USER);
                                //we didn't receive a response to the tester present message, after sending with the default num retries
                                ConnectionStatus = ConnectionStatusType.Disconnected;
                            }

                            break;
                        }
                    }

					var messageResponsesFinishedEvent = finishedMessage.GetResponsesFinishedEvent();

					if (messageResponsesFinishedEvent != null)
					{
						QueueAndTriggerEvent(new FinishedReceivingResponsesEventHolder(messageResponsesFinishedEvent, finishedMessage, mCurrentMessageSentProperly, mCurrentMessageReceivedAnyResponses, mCurrentMessageWaitedForAllReplies, (uint)(mNumSendAttemptsForCurrentMessage - 1)));
					}
                }
                
                if (mCurrentMessageReceivedAnyResponses)
				{
                    mNumConsecutiveSentMessagesWithoutResponses = 0;
                }
				else
                {
					//this will also catch messages that weren't sent properly, but I think that is OK

                    mNumConsecutiveSentMessagesWithoutResponses++;

					if ((mNumConsecutiveSentMessagesWithoutResponses >= 3) && (ConnectionStatus != ConnectionStatusType.Disconnected))
					{
						//for detecting baud rates we send lots of start diagnostic session messages and they may not receive responses
						if (finishedMessage == null || finishedMessage.mServiceID != (byte)KWP2000ServiceID.StartDiagnosticSession)
						{
							DisplayStatusMessage("Disconnecting because there were too many consecutive messages with no response.", StatusMessageType.USER);
							ConnectionStatus = ConnectionStatusType.Disconnected;
						}
					}
                }

                mCurrentMessageSentProperly = false;
                mCurrentMessageReceivedAnyResponses = false;
                mCurrentMessageWaitedForAllReplies = false;
                mNumSendAttemptsForCurrentMessage = 0;
                mCurrentMessageSentFinishedEvent = true;
            }
        }

        protected bool TransmitMessage(KWP2000Message currentMessage, bool checkForBytesPendingReceive, uint preceedingBytesToIgnore)
        {
            LogKWP2000Performance("TransmitMessage Start");

            bool sentProperly = false;

            //we can't send more data if we are still waiting for an echo
            if (!IsOutstandingEcho())
            {
                byte[] currentMessageDataBytes = currentMessage.GetMessageDataBytes(this);

                sentProperly = TransmitMessage(currentMessageDataBytes, checkForBytesPendingReceive, preceedingBytesToIgnore);

                if (sentProperly)
                {
                    DisplayStatusMessage("Sent message with service ID " + GetServiceIDString(currentMessage.mServiceID), StatusMessageType.LOG);
#if DEBUG
                    //wrapped in DEBUG #if so we don't impact performance
                    DisplayStatusMessage("Sent data: " + currentMessage.GetDataString(this), StatusMessageType.DEV);
#endif
                }
                else
                {
                    DisplayStatusMessage("Failed to send message with service ID: " + GetServiceIDString(currentMessage.mServiceID), StatusMessageType.LOG);
                }
            }
            else
            {
                DisplayStatusMessage("Can't transmit message, waiting to read echo.", StatusMessageType.LOG);
            }

            LogKWP2000Performance("TransmitMessage End");

            return sentProperly;
        }

        protected bool TransmitMessage(byte[] currentMessageDataBytes, bool checkForBytesPendingReceive, uint preceedingBytesToIgnore)
        {
            bool sentMessage = false;

            //we can't send more data if we are still waiting for an echo
            if(!IsOutstandingEcho())
            {
                mCurrentMessageWaitedForAllReplies = false;
                mCurrentMessageReceivedAnyResponses = false;
                mCurrentMessageSentProperly = false;
                mCurrentMessageSentFinishedEvent = false;

                uint bytesInReceiveQueue = 0;
                FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

                if (checkForBytesPendingReceive)
                {
                    ftdiStatus = mFTDIDevice.GetRxBytesAvailable(ref bytesInReceiveQueue, 4);
                }

                if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                {
                    //only send if there is nothing waiting in the receive queue, this is a simplex connection remember!
                    if (bytesInReceiveQueue == 0)
                    {
#if LOG_SENT_MESSAGE_DATA
		                //string fileName = "CommunicationLogSent.bin";
		                string fileName = "CommunicationLog.bin";
		                System.IO.FileStream stream = null;

		                if (!System.IO.File.Exists(fileName))
		                {
			                stream = System.IO.File.Create(fileName);
		                }
		                else
		                {
			                stream = System.IO.File.OpenWrite(fileName);
		                }

		                stream.Seek(0, System.IO.SeekOrigin.End);									
		                stream.Write(messageBytes, 0, messageBytes.Length);
		                stream.Close();
#endif
                        uint totalBytesWritten = TransmitBytes(currentMessageDataBytes);

                        mCurrentMessageSentProperly = (totalBytesWritten == currentMessageDataBytes.Length);

                        if (mCurrentMessageSentProperly)
                        {
                            //timers reset in order of reverse sensitivity

                            //restart the timer because we sent a new message
                            mP3TesterRequestTimeOut.Reset(); mP3TesterRequestTimeOut.Start();

                            //restart the timer because we sent a new message												
                            mP2ECUResponseMaxTimeCurrent = CurrentTimingParameters.P2ECUResponseTimeMaxMs;
                            mP2ECUResponseTimeOut.Reset(); mP2ECUResponseTimeOut.Start();

                            DisplayStatusMessage("Setting P2 max to " + mP2ECUResponseMaxTimeCurrent + "ms because we sent a message.", StatusMessageType.DEV);
                        }
                        
                        //we need to consume the echo even if the message is only partially sent
                        if (mConsumeTransmitEcho && (totalBytesWritten > 0))
                        {
                            Debug.Assert(mNumPreceedingEchoBytesToIgnore == 0);
                            Debug.Assert(mOutstandingEchoBytes == null);

                            mNumPreceedingEchoBytesToIgnore = preceedingBytesToIgnore;
                            mOutstandingEchoBytes = new byte[totalBytesWritten];
                            currentMessageDataBytes.CopyTo(mOutstandingEchoBytes, 0);
                        }
                        
                        mNumSendAttemptsForCurrentMessage++;
                    }
                    else
                    {
                        DisplayStatusMessage("Not sending message because the receive buffer contains data.", StatusMessageType.DEV);
                    }
                }
                else
                {
                    DisplayStatusMessage("Failed to get number of bytes pending in receive buffer.", StatusMessageType.LOG);
                    mNumSendAttemptsForCurrentMessage++;
                }

                sentMessage = mCurrentMessageSentProperly;
            }
            else
            {
                DisplayStatusMessage("Can't transmit message, waiting to read echo.", StatusMessageType.LOG);
            }

            return sentMessage;
        }

        protected uint TransmitBytes(byte[] messageBytes)
        {
            LogKWP2000Performance("TransmitBytes Start");

            uint totalNumBytesWritten = 0;
            FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

            long currentP4TesterInterByteTimeMinMs = CurrentTimingParameters.P4TesterInterByteTimeMinMs;

            if(!IsConnected())
            {
                currentP4TesterInterByteTimeMinMs = P4TesterInterByteTimeMinMsWhenConnecting;
            }

            if (currentP4TesterInterByteTimeMinMs > 0)
            {
                LogKWP2000Performance("TransmitBytes WriteStartWithInterByteTime " + currentP4TesterInterByteTimeMinMs);

                byte[] temp = new byte[1];
                uint numBytesWritten = 0;
                for (int x = 0; x < messageBytes.Length; x++)
                {
                    if (x > 0)
                    {
                        Stopwatch interByteTime = new Stopwatch();
                        interByteTime.Start();
                        while (interByteTime.ElapsedMilliseconds < currentP4TesterInterByteTimeMinMs) ;//busy loop
                    }

                    temp[0] = messageBytes[x];
                    ftdiStatus = mFTDIDevice.Write(temp, temp.Length, ref numBytesWritten, 4);

                    LogKWP2000Performance("TransmitBytes WriteStartWithInterByteTime WroteByte");

                    if ((ftdiStatus != FTDI.FT_STATUS.FT_OK) || (numBytesWritten != temp.Length))
                    {
                        break;
                    }

                    totalNumBytesWritten += numBytesWritten;
                }

                LogKWP2000Performance("TransmitBytes WriteEndWithInterByteTime");
            }
            else
            {
                LogKWP2000Performance("TransmitBytes WriteStart");

                ftdiStatus = mFTDIDevice.Write(messageBytes, messageBytes.Length, ref totalNumBytesWritten, 4);

                LogKWP2000Performance("TransmitBytes WriteEnd");
            }

            LogKWP2000Performance("TransmitBytes End");

            if (ftdiStatus != FTDI.FT_STATUS.FT_OK)
            {
                DisplayStatusMessage("Did not transmit bytes properly.", StatusMessageType.LOG);
            }

            return totalNumBytesWritten;
        }

        protected bool IsOutstandingEcho()
        {
            return (mOutstandingEchoBytes != null) || (mNumPreceedingEchoBytesToIgnore > 0);
        }

        //the most we can do in this function is keep reading until we match the first byte, but is possible the first byte is corrupt, so we can't really do that either
        protected bool ConsumeEchoFromReceiveBuffer()
        {
//DO NOT CHECK IN - USED FOR TESTING COMMUNICATION CORRUPTION
//mNumPreceedingEchoBytesToIgnore = 0;

            bool foundEcho = !mConsumeTransmitEcho;

            if ((mNumBytesInReceiveBuffer > 0) && IsOutstandingEcho() && mConsumeTransmitEcho)
            {
                uint totalEchoBytes = mNumPreceedingEchoBytesToIgnore;

                if (mOutstandingEchoBytes != null)
                {
                    totalEchoBytes += (uint)mOutstandingEchoBytes.Length;
                }

                foundEcho = true;
                uint numReceivedEchoBytes = Math.Min(mNumBytesInReceiveBuffer, totalEchoBytes);
                uint numEchoBytesMatched = numReceivedEchoBytes;

                if (mOutstandingEchoBytes != null)
                {
                    for (uint x = mNumPreceedingEchoBytesToIgnore; x < numReceivedEchoBytes; x++)
                    {
                        if (mOutstandingEchoBytes[x - mNumPreceedingEchoBytesToIgnore] != mReceiveBuffer[x])
                        {
                            numEchoBytesMatched = x;
                            foundEcho = false;
                            break;
                        }                        
                    }
                }
                    
                if (!foundEcho)
                {
                    DisplayStatusMessage("Read incorrect echo from ECU while sending message bytes. Matched first " + numEchoBytesMatched + " of " + numReceivedEchoBytes + " bytes.", StatusMessageType.LOG);

                    if (mOutstandingEchoBytes != null)
                    {
                        string expected = "Expected: ";
                        for (int x = 0; x < mOutstandingEchoBytes.Length; ++x) { expected += mOutstandingEchoBytes[x].ToString("X2") + " "; }
                        DisplayStatusMessage(expected, StatusMessageType.LOG);
                    }

                    string read = "Read:     ";
                    for (int x = 0; x < numReceivedEchoBytes; ++x) { read += mReceiveBuffer[x].ToString("X2") + " "; }
                    DisplayStatusMessage(read, StatusMessageType.LOG);

                    if (mNumPreceedingEchoBytesToIgnore > 0)
                    {
                        DisplayStatusMessage("Was ignoring first " + mNumPreceedingEchoBytesToIgnore + " bytes of the echo.", StatusMessageType.LOG);
                    }

                    DisplayStatusMessage("Clearing remaining expected echo bytes.", StatusMessageType.LOG);

                    //echo didn't match, give up on trying to match any more echo
                    mOutstandingEchoBytes = null;
                    mNumPreceedingEchoBytesToIgnore = 0;
                }

                //remove the part of the echo that matched from the receive buffer
                RemoveBytesFromReceiveBuffer(numEchoBytesMatched, false);
                
                if(foundEcho)
                {
                    uint numOutstandingEchoBytesToRemove = numEchoBytesMatched;

                    //remove the echo from the outstanding echo bytes buffer
                    if (numOutstandingEchoBytesToRemove > mNumPreceedingEchoBytesToIgnore)
                    {
                        if (mNumPreceedingEchoBytesToIgnore > 0)
                        {
                            numOutstandingEchoBytesToRemove -= mNumPreceedingEchoBytesToIgnore;
                            mNumPreceedingEchoBytesToIgnore = 0;
                        }

                        if ((mOutstandingEchoBytes != null) && (numOutstandingEchoBytesToRemove < mOutstandingEchoBytes.Length))
                        {
                            uint newNumOutstandingEchoBytes = (uint)mOutstandingEchoBytes.Length - numOutstandingEchoBytesToRemove;
                            byte[] newOutstandingEcho = new byte[newNumOutstandingEchoBytes];
                            Array.Copy(mOutstandingEchoBytes, numOutstandingEchoBytesToRemove, newOutstandingEcho, 0, newNumOutstandingEchoBytes);
                            mOutstandingEchoBytes = newOutstandingEcho;
                        }
                        else
                        {
                            mOutstandingEchoBytes = null;
                        }
                    }
                    else
                    {
                        mNumPreceedingEchoBytesToIgnore -= numOutstandingEchoBytesToRemove;
                    }
                }                
            }

            return foundEcho;
        }

        protected void RemoveAllBytesFromReceiveBuffer()
        {
            RemoveBytesFromReceiveBuffer(mNumBytesInReceiveBuffer, false);
        }

        protected void RemoveBytesFromReceiveBuffer(uint numBytes, bool shouldMarkBufferDirty)
        {
            if (mNumBytesInReceiveBuffer > numBytes)
            {
                Buffer.BlockCopy(mReceiveBuffer, (int)numBytes, mReceiveBuffer, 0, (int)(mNumBytesInReceiveBuffer - numBytes));
                mNumBytesInReceiveBuffer -= numBytes;
            }
            else
            {
                mNumBytesInReceiveBuffer = 0;
            }

            if (mNumBytesInReceiveBuffer > 0)
            {
                mReceiveBufferIsDirty |= shouldMarkBufferDirty;
            }
        }

        public bool AnyMessagesPendingSend()
        {
            return (mMessagesPendingSend.Count > 0);
        }

        protected bool IsCurrentMessagePendingSend(byte serviceID)
        {
            return AnyMessagesPendingSend() && (mMessagesPendingSend.Peek().mServiceID == serviceID);
        }

        protected bool ShouldWaitForMultipleResponses()
        {   
            if(IsExpectingResponses())
            {                
                if(mMessagesPendingSend.Peek().mAddressMode == KWP2000AddressMode.Functional)
                {
                    return true;
                }

                if (DoesExpectedResponseUseDataSegmentation())
                {
                    return true;
                }
            }

            return false;
        }

        protected bool IsExpectingResponses()
        {            
            bool isExpecting = mCurrentMessageSentProperly && !mCurrentMessageSentFinishedEvent && !mCurrentMessageWaitedForAllReplies && AnyMessagesPendingSend() && (ConnectionStatus != ConnectionStatusType.Disconnected);

            Debug.Assert(!isExpecting || mP2ECUResponseTimeOut.IsRunning || mP1ECUResponseInterByteTimeOut.IsRunning);

            return isExpecting;
        }

        protected bool IsExpectingResponsesFor(byte serviceID)
        {
            return IsExpectingResponses() && IsCurrentMessagePendingSend(serviceID);
        }

        protected bool IsExpectedResponse(KWP2000Message responseMessage)
        {
            return IsExpectingResponses() && IsResponseToRequest(mMessagesPendingSend.Peek().mServiceID, responseMessage);
        }

        protected bool DoesExpectedResponseUseDataSegmentation()
        {
            return IsExpectingResponses() && DoesRequestResponseUseDataSegmentation(mMessagesPendingSend.Peek().mServiceID);
        }

        protected bool ProcessReceiveBuffer(out uint additionalBytesNeededToCompleteMessage, bool searchAllOffsets)
		{
            additionalBytesNeededToCompleteMessage = 0;

            bool foundAnyMessage = false;//should be true even if the message is invalid
			bool keepLookingForMessage = true;
            uint receiveBufferOffset = 0;

			while (keepLookingForMessage)
			{
				KWP2000Message message;
                var messageErrorCode = ReadMessageFromReceiveBuffer(receiveBufferOffset, out message, out additionalBytesNeededToCompleteMessage);

                #region receivedNewMessage
                if ((message != null) && (messageErrorCode == MessageErrorCode.MessageOK))
                {
                    foundAnyMessage = true;//need to indicate we found a message so that the P2 and P3 timers can be restarted

                    bool validAddressMode = (message.mAddressMode == KWP2000AddressMode.Physical) || (message.mAddressMode == KWP2000AddressMode.None);
                    bool addressedToTester = (message.mDestination == TESTER_ADDRESS);
                    bool isResponseMessage = IsServiceIDResponse(message.mServiceID);

                    bool receivedMessageOK = validAddressMode && addressedToTester && isResponseMessage;

                    if (receivedMessageOK)
                    {
                        mP2ECUResponseMaxTimeCurrent = CurrentTimingParameters.P2ECUResponseTimeMaxMs;
                        DisplayStatusMessage("Setting P2 max to " + mP2ECUResponseMaxTimeCurrent + "ms because we received a complete message.", StatusMessageType.DEV);
                                                
                        keepLookingForMessage = false;

                        DisplayStatusMessage("Received message with service ID: " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
#if DEBUG
                        //wrapped in DEBUG #if so we don't impact performance
                        DisplayStatusMessage("Received data: " + message.GetDataString(this), StatusMessageType.DEV);
#endif

                        //ignore messages unless they are a response
                        if (IsExpectedResponse(message))
                        {
                            mNumConsecutiveUnsolicitedResponses = 0;

                            bool shouldTriggerMessageHandler = true;
                            bool shouldResendMessage = false;
                            bool isOKToFinishWaitingForResponse = true;

                            switch (message.mServiceID)
                            {
                                #region StartCommunication
								case (byte)KWP2000ServiceID.StartCommunicationPositiveResponse:
                                {
                                    if (ConnectionStatus != ConnectionStatusType.ConnectionPending)
                                    {
                                        DisplayStatusMessage("Connection is not pending, but received a Start Communication Positive Response message.", StatusMessageType.LOG);
                                    }

                                    if (message.DataLength == 2)
                                    {
                                        byte keyByte1 = (byte)(message.mData[0] & 0x7F);//assuming seven data bits and odd parity bit
                                        byte keyByte2 = (byte)(message.mData[1] & 0x7F);//assuming seven data bits and odd parity bit

                                        if (IsKeyByte1ValidKWP2000(keyByte1, false) && IsKeyByte2ValidKWP2000(keyByte2, false))
                                        {
                                            if (keyByte2 != KWP_2000_PROTOCOL_KEY_BYTE_2)
                                            {
                                                DisplayStatusMessage("Unknown protocol type defined by second key byte: 0x" + keyByte2.ToString("X2"), StatusMessageType.LOG);
                                            }

                                            SetCommunicationPhysicalAddressAndKeyBytes(message.mSource, keyByte1, keyByte2);
                                            ConnectionStatus = ConnectionStatusType.Connected;
                                        }
                                        else
                                        {
                                            DisplayStatusMessage("Invalid key bytes: 0x" + keyByte1.ToString("X2") + " 0x" + keyByte2.ToString("X2"), StatusMessageType.LOG);
                                        }
                                    }
                                    else
                                    {
                                        DisplayStatusMessage("Invalid number of data bytes for Start Communication Positive Response message.", StatusMessageType.LOG);
                                    }

                                    break;
                                }
                                #endregion
                                #region StopCommunication
								case (byte)KWP2000ServiceID.StopCommunicationPositiveResponse:
                                {
                                    ConnectionStatus = ConnectionStatusType.Disconnected;
                                    break;
                                }
                                #endregion
                                #region TesterPresent
								case (byte)KWP2000ServiceID.TesterPresentPositiveReponse:
                                {
                                    shouldTriggerMessageHandler = false;
                                    break;
                                }
                                #endregion
                                #region NegativeResponse
								case (byte)KWP2000ServiceID.NegativeResponse:
                                {
                                    if (message.DataLength == 2)
                                    {
                                        DisplayStatusMessage("Received negative response for service ID: " + GetServiceIDString(message.mData[0])
                                            + ", with response code: " + GetResponseCodeString(message.mData[1]), StatusMessageType.LOG);

                                        if (message.mData[1] == (byte)KWP2000ResponseCode.RequestCorrectlyReceived_ResponsePending)
                                        {
                                            //only wait for another message if we are connected
                                            if (ConnectionStatus == ConnectionStatusType.Connected)
                                            {
                                                shouldTriggerMessageHandler = false;//don't remove the message from the queue, we will get another response
                                                isOKToFinishWaitingForResponse = false;//the real response is still coming

                                                mP2ECUResponseMaxTimeCurrent = CurrentTimingParameters.P3TesterResponseTimeMaxMs;//set to busy wait max time
                                                DisplayStatusMessage("Setting P2 max time to " + mP2ECUResponseMaxTimeCurrent + "ms and waiting for another message.", StatusMessageType.DEV);
                                            }
                                        }
                                        else if (message.mData[1] == (byte)KWP2000ResponseCode.Busy_RepeastRequest)
                                        {
                                            shouldTriggerMessageHandler = false;//we will need to resend the message
                                            shouldResendMessage = true;//we will need to resend the message

                                            DisplayStatusMessage("ECU was too busy to receive message, resending.", StatusMessageType.LOG);
                                        }
										else if (message.mData[0] == (byte)KWP2000ServiceID.StartCommunication)
										{
											shouldTriggerMessageHandler = false;
										}
										else if (message.mData[0] == (byte)KWP2000ServiceID.TesterPresent)
										{
											shouldTriggerMessageHandler = false;
										}
										else if (message.mData[0] == (byte)KWP2000ServiceID.StopCommunication)
										{
											shouldTriggerMessageHandler = false;

											//I don't care, disconnect anyway
											DisplayStatusMessage("Received negative response to StopCommunication request, disconnecting anyway.", StatusMessageType.USER);

											ConnectionStatus = ConnectionStatusType.Disconnected;
										}
                                    }
                                    else
                                    {
                                        DisplayStatusMessage("Received invalid message: " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
                                    }

                                    break;
                                }
                                #endregion
                            }

                            Debug.Assert(IsExpectingResponses() || (ConnectionStatus == ConnectionStatusType.Disconnected));

                            if (shouldResendMessage)
                            {
                                //don't count the last send attempt, since we need to resend the request
                                if (mNumSendAttemptsForCurrentMessage > 0)
                                {
                                    mNumSendAttemptsForCurrentMessage--;
                                }
                            }
                            else
                            {
                                //the current message response we are handling is a valid response
                                mCurrentMessageReceivedAnyResponses = true;
                            }

                            if (shouldTriggerMessageHandler && (ReceivedMessageEvent != null))
                            {
                                //must be called before the finish expecting responses handler
                                bool queuedMessage = QueueAndTriggerEvent(new ReceivedMessageEventHolder(ReceivedMessageEvent, message));

                                if (!queuedMessage)
                                {
                                    DisplayStatusMessage("Cannot queue received message, receive message buffer is full, deleting message.", StatusMessageType.LOG);
                                }
                            }

                            //don't keep waiting for more messages if we don't have to
                            if (isOKToFinishWaitingForResponse && !ShouldWaitForMultipleResponses())
                            {
                                //we got one response, and we don't have to wait for anymore, so no need to time out
                                FinishedSendingCurrentMessage();
                            }

                            if (ConnectionStatus == ConnectionStatusType.ConnectionPending)
                            {
                                //if we were trying to connect and we got a message, we consider that a successful connection
                                //don't set the key bytes or address, since they must be right if we got a response
                                ConnectionStatus = ConnectionStatusType.Connected;
                            }
                        }
                        else
                        {
                            mNumConsecutiveUnsolicitedResponses++;
                            DisplayStatusMessage("Ignoring message with service ID " + GetServiceIDString(message.mServiceID) + " because it was unsolicited", StatusMessageType.LOG);

                            if (message.mServiceID == (byte)KWP2000ServiceID.NegativeResponse)
                            {
                                if (message.DataLength >= 2)
                                {
                                    DisplayStatusMessage("Unsolicited negative response, request ID: " + (KWP2000ServiceID)message.mData[0] + " response code: " + (KWP2000ResponseCode)message.mData[1], StatusMessageType.LOG);
                                }
                            }

                            if ((mNumConsecutiveUnsolicitedResponses >= 5) && ((ConnectionStatus != ConnectionStatusType.DisconnectionPending) && (ConnectionStatus != ConnectionStatusType.Disconnected)))
                            {
                                DisplayStatusMessage("Too many consecutive unsolicited messages from ECU, disconnecting.", StatusMessageType.USER);
                                DisconnectFromECU();
                            }
                        }
                    }
                    else
                    {                        
                        if(!validAddressMode)
                        {
                            DisplayStatusMessage("Ignoring received message with invalid address mode " + message.mAddressMode + ". Message service ID was " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
                        }
                        else if(!addressedToTester)
                        {
                            DisplayStatusMessage("Ignoring received message not addressed to tester with address " + message.mDestination.ToString("X2") + ". Message service ID was " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
                        }
                        else if (!isResponseMessage)
                        {
                            DisplayStatusMessage("Ignoring received message that is not a response. Message service ID was " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
                        }
                        else
                        {
                            DisplayStatusMessage("Ignoring received message for unknown reason. Message service ID was " + GetServiceIDString(message.mServiceID), StatusMessageType.LOG);
                            Debug.Fail("Unknown reason for message failure");
                        }
                    }
                }
                #endregion
                #region didntReceiveMessage
                else
				{
					if(messageErrorCode == MessageErrorCode.NotEnoughData)
					{
                        if (searchAllOffsets)
                        {
                            receiveBufferOffset++;
                            keepLookingForMessage = (mNumBytesInReceiveBuffer > receiveBufferOffset);
                        }
                        else
                        {
                            keepLookingForMessage = false;
                        }
					}
                    else if (   (messageErrorCode == MessageErrorCode.InvalidChecksum) 
                            || (messageErrorCode == MessageErrorCode.RequestedTooMuchData)
                            || (messageErrorCode == MessageErrorCode.MessageContainsNoData) )
                    {
                        if (receiveBufferOffset == 0)
                        {
                            DisplayStatusMessage("Could not construct valid message from received data: " + GetMessageErrorCodeString(messageErrorCode), StatusMessageType.LOG);

                            if (mNumBytesInReceiveBuffer > 0)
                            {
                                DisplayStatusMessage("Trying to create a valid message by discarding the first byte of the receive buffer. Removed: 0x" + mReceiveBuffer[0].ToString("X2"), StatusMessageType.LOG);
                                RemoveBytesFromReceiveBuffer(1, true);
                            }

                            keepLookingForMessage = (mNumBytesInReceiveBuffer > 0);
                        }
                        else
                        {
                            if (searchAllOffsets)
                            {
                                receiveBufferOffset++;
                                keepLookingForMessage = (mNumBytesInReceiveBuffer > receiveBufferOffset);
                            }
                            else
                            {
                                keepLookingForMessage = false;
                            }
                        }
                    }
                    else
                    {                        
                        Debug.Fail("Unknown message error code");

                        //removing a byte and continuing seems like all we can do
                        RemoveBytesFromReceiveBuffer(1, true);
                        keepLookingForMessage = (mNumBytesInReceiveBuffer > 0);
                    }
                }
                #endregion
            }

            mReceiveBufferIsDirty = false;

            return foundAnyMessage;
		}        

        protected bool DetermineReceiveBufferMessageRequirements(uint bufferOffset, out byte numHeaderBytes, out byte addressMode, out byte numDataBytes, out byte numChecksumBytes)
        {
            bool dataValid = false;

            numHeaderBytes = 0;
            numDataBytes = 0;
            numChecksumBytes = 0;
            addressMode = 0;

            if (mNumBytesInReceiveBuffer > bufferOffset)
            {
                numHeaderBytes = 1;
                addressMode = (byte)(mReceiveBuffer[bufferOffset] & 0xC0);
                numDataBytes = (byte)(mReceiveBuffer[bufferOffset] & 0x3F);
                numChecksumBytes = 1;
                
                if ((addressMode == (byte)KWP2000AddressMode.Functional)
                    || (addressMode == (byte)KWP2000AddressMode.Physical))
                {
                    numHeaderBytes = 3;
                }
                else
                {
                    numHeaderBytes = 1;
                }

                if (numDataBytes == 0)
                {
                    if (mNumBytesInReceiveBuffer > numHeaderBytes)
                    {
                        numDataBytes = mReceiveBuffer[bufferOffset + numHeaderBytes];
                    }
                    else
                    {
                        //hmm... they didn't tell us how many data bytes they want...
                    }

                    numHeaderBytes++;
                }

                dataValid = true;
            }

            return dataValid;
        }

		protected MessageErrorCode ReadMessageFromReceiveBuffer(uint bufferOffset, out KWP2000Message message, out uint additionalBytesNeeded)
		{
			message = null;
            additionalBytesNeeded = 0;
			MessageErrorCode errorCode = MessageErrorCode.UnknownError;

            byte numHeaderBytes = 0;
            byte numDataBytes = 0;
            byte numChecksumBytes = 0;
            byte addressMode = 0;

            if (DetermineReceiveBufferMessageRequirements(bufferOffset, out numHeaderBytes, out addressMode, out numDataBytes, out numChecksumBytes))
			{
                uint totalDataLength = (uint)(numHeaderBytes + numDataBytes + numChecksumBytes);

                //setup current values in case message doesn't contain address info
                byte destination = TESTER_ADDRESS;
                byte source = CommunicationAddress;
                
                if (mNumBytesInReceiveBuffer >= bufferOffset + numHeaderBytes)
                {
                    if ((addressMode == (byte)KWP2000AddressMode.Physical) || (addressMode == (byte)KWP2000AddressMode.Functional))
                    {
                        destination = mReceiveBuffer[bufferOffset + 1];
                        source = mReceiveBuffer[bufferOffset + 2];
                    }

                    if (mNumBytesInReceiveBuffer > bufferOffset + numHeaderBytes)
                    {
                        if (numDataBytes > 0)
                        {
                            byte serviceID = mReceiveBuffer[bufferOffset + numHeaderBytes];

                            if (totalDataLength <= MAX_MESSAGE_SIZE)
                            {
                                //data length + header length + checksum length
                                if (mNumBytesInReceiveBuffer >= bufferOffset + totalDataLength)
                                {
                                    byte[] messageData = null;

                                    if (numDataBytes >= 2)//service id is first byte, and then any bytes after that we call data
                                    {
                                        messageData = new byte[numDataBytes - 1];//we don't include the service id
                                        //start reading after the header bytes and service ID
                                        Buffer.BlockCopy(mReceiveBuffer, (int)bufferOffset + numHeaderBytes + 1, messageData, 0, numDataBytes - 1);
                                    }

                                    byte checksum = mReceiveBuffer[bufferOffset + numHeaderBytes + numDataBytes];

                                    byte calcChecksum = 0;
                                    for (int x = 0; x < (numHeaderBytes + numDataBytes); x++)
                                    {
                                        calcChecksum += mReceiveBuffer[bufferOffset + x];
                                    }

                                    if (checksum == calcChecksum)
                                    {
                                        message = new KWP2000Message((KWP2000AddressMode)addressMode, source, destination, serviceID, messageData);

    #if LOG_RECEIVED_MESSAGE_DATA
					                                                                                                                                                                                                        //string fileName = "CommunicationLogReceived.bin";
								string fileName = "CommunicationLog.bin";
								System.IO.FileStream stream = null;
					
								if (!System.IO.File.Exists(fileName))
								{
									stream = System.IO.File.Create(fileName);
								}
								else
								{
									stream = System.IO.File.OpenWrite(fileName);
								}
					
								stream.Seek(0, System.IO.SeekOrigin.End);
								stream.WriteByte(0x88);
								stream.WriteByte(0x88);
								stream.Write(mReceiveBuffer, 0, (int)startOfNextMessage);
								stream.Close();
    #endif
                                        if (bufferOffset > 0)
                                        {
                                            DisplayStatusMessage("Discarding first " + bufferOffset + " bytes from the receive buffer because a message starts there.", StatusMessageType.LOG);
                                        }

                                        //throw away the ignored bytes
                                        RemoveBytesFromReceiveBuffer(bufferOffset + totalDataLength, false);
                                    
                                        errorCode = MessageErrorCode.MessageOK;
                                    }
                                    else
                                    {
                                        errorCode = MessageErrorCode.InvalidChecksum;
                                    }
                                }
                                else
                                {
                                    additionalBytesNeeded = totalDataLength - mNumBytesInReceiveBuffer;
                                    errorCode = MessageErrorCode.NotEnoughData;
                                }
                            }
                            else
                            {
                                errorCode = MessageErrorCode.RequestedTooMuchData;
                            }
                        }
                        else
                        {
                            errorCode = MessageErrorCode.MessageContainsNoData;
                        }
                    }
                    else
                    {
                        additionalBytesNeeded = totalDataLength - mNumBytesInReceiveBuffer;
                        errorCode = MessageErrorCode.NotEnoughData;
                    }
                }
                else
                {
                    additionalBytesNeeded = totalDataLength - mNumBytesInReceiveBuffer;
                    errorCode = MessageErrorCode.NotEnoughData;
                }                
			}
			else
			{
                additionalBytesNeeded = 1;//read one more to get a better idea of how much we need
				errorCode = MessageErrorCode.NotEnoughData;
			}

			return errorCode;			
		}

		public string GetServiceIDString(byte serviceID)
		{
			if(Enum.IsDefined(typeof(KWP2000ServiceID), serviceID))
			{
				return Enum.GetName(typeof(KWP2000ServiceID), serviceID);
			}
			else
			{
				return "0x" + serviceID.ToString("X2");
			}
		}

		public string GetResponseCodeString(byte responseCode)
		{
			if (Enum.IsDefined(typeof(KWP2000ResponseCode), responseCode))
			{
				return Enum.GetName(typeof(KWP2000ResponseCode), responseCode);
			}
			else
			{
				return "0x" + responseCode.ToString("X2");
			}
		}

        protected string GetMessageErrorCodeString(MessageErrorCode errorCode)
		{
            //todo: get rid of this function and use an extension method that reads description attributes

			if (Enum.IsDefined(errorCode.GetType(), errorCode))
			{
				return Enum.GetName(errorCode.GetType(), errorCode);
			}
			else
			{
				return "Unknown message error code: 0x" + ((byte)errorCode).ToString("X2");
			}
		}		
	}
}
