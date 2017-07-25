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

//#define GENERAL_REJECT_ON_REQUEST_UPLOAD_DOWNLOAD_IS_ECU_LOCK_OUT
//#define PROFILE_TRANSFER_DATA

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

using Shared;

namespace Communication
{
    public abstract class KWP2000Action : CommunicationAction
    {
        public KWP2000Action(KWP2000Interface commInterface)
            : base(commInterface)
        {            
        }       

        public override bool Start()
        {
            bool result = base.Start();

            if (result)
            {
                KWP2000CommInterface.ReceivedMessageEvent += this.ReceivedMessageHandler;
            }

            return result;
        }

        private void FinishedReceivingResponsesHandler(KWP2000Interface commInterface, KWP2000Message message, bool sentProperly, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries)
        {
            lock (this)//lock to ensure we don't accidentally get other callbacks while handling this one
            {
				message.ResponsesFinishedEvent -= this.FinishedReceivingResponsesHandler;

                if (!IsComplete)//always need to check this in case we are getting callbacks after we complete
                {
                    if (!sentProperly)
                    {
                        DisplayStatusMessage("Message failed to send properly.", StatusMessageType.LOG);
                    }
                    else if (!receivedAnyReplies)
                    {
                        DisplayStatusMessage("Did not receive any replies to message.", StatusMessageType.LOG);
                    }

                    ResponsesFinishedHandler(commInterface, message, sentProperly, receivedAnyReplies, waitedForAllReplies, numRetries);
                }
            }
        }

        protected virtual void ResponsesFinishedHandler(KWP2000Interface commInterface, KWP2000Message message, bool sentProperly, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries)
        {
            if (!sentProperly || !receivedAnyReplies)
            {
                ActionCompletedInternal(false, true);
            }
        }

        private void ReceivedMessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {            
            lock (this)//lock to ensure we don't accidentally get other callbacks while handling this one
            {
                if (!IsComplete)//always need to check this in case we are getting callbacks after we complete
                {
					try
					{
						bool handledMessage = MessageHandler(commInterface, message);

						if (!handledMessage)
						{
							DisplayStatusMessage("Received unhandled message with service ID: " + KWP2000CommInterface.GetServiceIDString(message.mServiceID), StatusMessageType.LOG);

							if (message.mServiceID == (byte)KWP2000ServiceID.NegativeResponse)
							{
								if (message.DataLength >= 2)
								{
									DisplayStatusMessage("Unhandled negative response, request ID: " + KWP2000CommInterface.GetServiceIDString(message.mData[0]) + " response code: " + KWP2000CommInterface.GetResponseCodeString(message.mData[1]), StatusMessageType.LOG);
								}
							}
						}
					}
					catch (Exception e)
					{
						Debug.Fail("Exception: " + e.Message);
					}
                }
            }
        }

        protected virtual bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = false;

            switch (message.mServiceID)
            {   
                case (byte)KWP2000ServiceID.StartCommunicationPositiveResponse:
                {
                    handled = true;
                    break;
                }
				case (byte)KWP2000ServiceID.StopCommunicationPositiveResponse:
                {
                    handled = true;
                    break;
                }
				case (byte)KWP2000ServiceID.TesterPresentPositiveReponse:
                {
                    handled = true;
                    break;
                }
				case (byte)KWP2000ServiceID.NegativeResponse:
                {
                    if (message.DataLength >= 2)
                    {                    
                        if (message.mData[1] == (byte)KWP2000ResponseCode.RequestCorrectlyReceived_ResponsePending)                        
                        {
                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.Busy_RepeastRequest)
                        {
                            handled = true;
                        }
                        else if (message.mData[0] == (byte)KWP2000ServiceID.StartCommunication)
                        {
                            handled = true;
                        }
                        else if (message.mData[0] == (byte)KWP2000ServiceID.StopCommunication)
                        {
                            handled = true;
                        }
                    }

                    break;
                }                
            }

            return handled;
        }

        protected override void ActionCompletedInternal(bool success, bool communicationError)
        {
            if (!IsComplete)
            {
                KWP2000CommInterface.ReceivedMessageEvent -= this.ReceivedMessageHandler;                

                base.ActionCompletedInternal(success, communicationError);                
            }
        }

		protected KWP2000Message SendMessage(byte serviceID)
		{
			var message = KWP2000CommInterface.SendMessage(serviceID);

			if (message != null)
			{
				message.ResponsesFinishedEvent += this.FinishedReceivingResponsesHandler;
			}

			return message;
		}

		protected KWP2000Message SendMessage(byte serviceID, byte[] data)
		{
			var message = KWP2000CommInterface.SendMessage(serviceID, data);

			if (message != null)
			{
				message.ResponsesFinishedEvent += this.FinishedReceivingResponsesHandler;
			}

			return message;
		}

		protected KWP2000Message SendMessage(byte serviceID, uint maxNumRetries, byte[] data)
		{
			var message = KWP2000CommInterface.SendMessage(serviceID, maxNumRetries, data);

			if (message != null)
			{
				message.ResponsesFinishedEvent += this.FinishedReceivingResponsesHandler;
			}

			return message;
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
    };

    //NOTE: when a StartDiagnosticSession occurs, it resets the communication timings to defaults
    public class StartDiagnosticSessionAction : KWP2000Action
    {
        public StartDiagnosticSessionAction(KWP2000Interface commInterface, KWP2000DiagnosticSessionType sessionType)
            : this(commInterface, sessionType, (uint)KWP2000BaudRates.BAUD_UNSPECIFIED)
        {
        }

		public StartDiagnosticSessionAction(KWP2000Interface commInterface, KWP2000DiagnosticSessionType sessionType, uint baudRate)
			: this(commInterface, sessionType, new List<uint>(){baudRate})
		{
		}

        public StartDiagnosticSessionAction(KWP2000Interface commInterface, KWP2000DiagnosticSessionType sessionType, IEnumerable<uint> baudRates)
            : base(commInterface)
        {
            mSessionType = sessionType;
            mBaudRates = baudRates;
            mMessageFormatState = MessageFormatState.NoBaudRate;
        }

        public static bool ShouldStartDiagnosticSession(KWP2000Interface commInterface, KWP2000DiagnosticSessionType desiredSessionType, uint desiredBaudRate)
        {
            return (desiredSessionType != commInterface.CurrentDiagnosticSessionType) || ((desiredBaudRate != (uint)KWP2000BaudRates.BAUD_UNSPECIFIED) && (desiredBaudRate != commInterface.DiagnosticSessionBaudRate));
        }

        public override bool Start()
        {
            bool result = base.Start();

            if (result)
            {
				mCurrentBaudRate = mBaudRates.GetEnumerator();
				result = mCurrentBaudRate.MoveNext();//move to the first baud rate

				if (result)
				{
					if (ShouldStartDiagnosticSession(KWP2000CommInterface, mSessionType, mCurrentBaudRate.Current))
					{
						mMessageFormatState = MessageFormatState.SpecificBaudRate;

						uint targetBaudRate = mCurrentBaudRate.Current;

						if (targetBaudRate == (uint)KWP2000BaudRates.BAUD_UNSPECIFIED)
						{
							//Standard sessions have to be started with 10400 baud or no baud.
							//If we aren't connected, use default baud rate.
							if (!CommInterface.IsConnectionOpen() || (mSessionType == KWP2000DiagnosticSessionType.StandardSession))
							{
								targetBaudRate = (uint)KWP2000BaudRates.BAUD_DEFAULT;								
							}
							else
							{
								targetBaudRate = KWP2000CommInterface.DiagnosticSessionBaudRate;

								mMessageFormatState = MessageFormatState.NoBaudRate;
							}
						}

						SendStartDiagnosticSessionMessageData(mSessionType, targetBaudRate);
						DisplayStatusMessage("Starting diagnostic session.", StatusMessageType.USER);
					}
					else
					{
						ActionCompleted(true);
					}

					result = true;
				}
            }

            return result;
        }

        public void SendStartDiagnosticSessionMessageData(KWP2000DiagnosticSessionType sessionType, uint baudRate)
        {
            byte[] messageData = null;           

            //Must always send baud rate byte unless using a standard session. ECU starting programming session doesn't check if it receives it or not before using it
            Debug.Assert((sessionType != KWP2000DiagnosticSessionType.StandardSession) || (baudRate == (uint)KWP2000BaudRates.BAUD_DEFAULT) || (baudRate == (uint)KWP2000BaudRates.BAUD_UNSPECIFIED));

            if (baudRate == (uint)KWP2000BaudRates.BAUD_UNSPECIFIED)
            {
                messageData = new byte[1];
                messageData[0] = (byte)sessionType;

				DisplayStatusMessage("Starting " + sessionType + " diagnostic session without baud rate.", StatusMessageType.LOG);
            }
            else
            {
                messageData = new byte[2];
                messageData[0] = (byte)sessionType;
				messageData[1] = CalculateBaudRateByte(baudRate);

				DisplayStatusMessage("Starting " + sessionType + " diagnostic session with " + baudRate + " baud rate.", StatusMessageType.LOG);
            }
            
            SendMessage((byte)KWP2000ServiceID.StartDiagnosticSession, messageData);
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                bool giveUpAndTryToUseCurrentSession = false;

                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.StartDiagnosticSession, message))
                {
                    bool sessionTypeOK = true;
                    
                    if (message.DataLength > 0)
                    {
                        handled = true;

                        KWP2000CommInterface.CurrentDiagnosticSessionType = (KWP2000DiagnosticSessionType)message.mData[0];

                        //if no baud is specified, we must default to 10400 to match the ECU
                        uint newBaudRate = (uint)KWP2000BaudRates.BAUD_DEFAULT;

                        if (message.DataLength >= 2)
                        {
                            newBaudRate = CalculateBaudRateFromByte(message.mData[1]);

                            DisplayStatusMessage("ECU requesting specific baud rate: " + newBaudRate, StatusMessageType.LOG);                            

                            if (message.DataLength >= 3)
                            {
                                //a non-zero time indicates we need to close and reconnect to communicate?
                                uint waitTimeMS = message.mData[2] * 100U;

                                //TODO: support wait times for starting diagnostic sessions
                                DisplayStatusMessage("ECU requested a wait time of " + waitTimeMS + "ms to start diagnostic session. This is currently unsupported and may caused problems.", StatusMessageType.LOG);
                            }
                        }

                        KWP2000CommInterface.DiagnosticSessionBaudRate = newBaudRate;

                        if (KWP2000CommInterface.CurrentDiagnosticSessionType != mSessionType)
                        {
                            DisplayStatusMessage("Diagnostic session mode does not match requested mode.", StatusMessageType.USER);
                            DisplayStatusMessage("Diagnostic session mode does not match requested mode. Requested: " + mSessionType + " Received: " + KWP2000CommInterface.CurrentDiagnosticSessionType, StatusMessageType.LOG);

                            if (KWP2000CommInterface.CurrentDiagnosticSessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
                            {
                                DisplayStatusMessage("The ECU is in programming mode, and needs the ignition turned off to reset.\nIf this does not end programming mode, the ECU likely believes the flash memory is not programmed correctly.", StatusMessageType.USER);
                            }

                            sessionTypeOK = false;
                        }
                    }

					if (sessionTypeOK)
					{
						DisplayStatusMessage("Successfully started diagnostic session.", StatusMessageType.USER);
						ActionCompleted(true);
					}
					else
					{
						ActionCompleted(false);
					}
                }
				else if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.StopDiagnosticSession, message))
                {
                    KWP2000CommInterface.DiagnosticSessionBaudRate = (uint)KWP2000BaudRates.BAUD_DEFAULT;
                    KWP2000CommInterface.CurrentDiagnosticSessionType = KWP2000DiagnosticSessionType.StandardSession;
                    handled = true;

                    if (KWP2000CommInterface.CurrentDiagnosticSessionType != mSessionType)
                    {
                        DisplayStatusMessage("Diagnostic session mode does not match requested mode.", StatusMessageType.USER);
                        DisplayStatusMessage("Diagnostic session mode does not match requested mode. Requested: " + mSessionType + " Received: " + KWP2000CommInterface.CurrentDiagnosticSessionType, StatusMessageType.LOG);
                        ActionCompleted(false);
                    }
                    else
                    {
                        DisplayStatusMessage("Successfully started diagnostic session.", StatusMessageType.USER);
                        ActionCompleted(true);
                    }
                }
				else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.StartDiagnosticSession, message))
				{
				    if (message.DataLength >= 2)
				    {       
				        if (message.mData[1] == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested)
				        {
				            DisplayStatusMessage("Start diagnostic session failed, ECU reports security access is required.", StatusMessageType.USER);
                            
				            ActionCompleted(false);
				            handled = true;
				        }
				        else if (message.mData[1] == (byte)KWP2000ResponseCode.NoProgram)
				        {
				            if (mSessionType == KWP2000DiagnosticSessionType.StandardSession)
				            {
				                DisplayStatusMessage("Start diagnostic session failed, ECU reports NoProgram. Trying to start diagnostic session by stopping current session.", StatusMessageType.LOG);
				                SendMessage((byte)KWP2000ServiceID.StopDiagnosticSession);
				                mMessageFormatState = MessageFormatState.StopSession;
				                handled = true;
				            }
				            else
				            {
				                giveUpAndTryToUseCurrentSession = true;
				            }
				        }
				        //TODO: should we handle more error responses with different formats?
				        else if ((message.mData[1] == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)//baud rate can cause this
				                || (message.mData[1] == (byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)//not being able to exit programming mode can cause this
				                || (message.mData[1] == (byte)KWP2000ResponseCode.RequestOutOfRange))//baud rate can cause this
				        {
                            switch (mMessageFormatState)
                            {
                                case MessageFormatState.SpecificBaudRate:
                                {
									if (mCurrentBaudRate.MoveNext())
									{
										SendStartDiagnosticSessionMessageData(mSessionType, mCurrentBaudRate.Current);
									}
									else
									{
										mMessageFormatState = MessageFormatState.NoBaudRate;

										SendStartDiagnosticSessionMessageData(mSessionType, (uint)KWP2000BaudRates.BAUD_UNSPECIFIED);										
									}
                                    
                                    handled = true;
                                    break;
                                }
                                case MessageFormatState.NoBaudRate:
                                {
                                    if (mSessionType == KWP2000DiagnosticSessionType.StandardSession)
                                    {
                                        SendMessage((byte)KWP2000ServiceID.StopDiagnosticSession);
                                        mMessageFormatState = MessageFormatState.StopSession;
                                        handled = true;
                                    }
                                    else
                                    {
										giveUpAndTryToUseCurrentSession = true;
                                    }

                                    break;
                                }                                
                            }

							if (!giveUpAndTryToUseCurrentSession)
							{
								if (message.mData[1] == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)
								{
									DisplayStatusMessage("Start diagnostic session failed, ECU reports sub function not supported or invalid format. Trying again with a different format.", StatusMessageType.LOG);
								}
								else if (message.mData[1] == (byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)
								{
									DisplayStatusMessage("Start diagnostic session failed, ECU reports conditions not correct or sequence error. Trying again with a different format.", StatusMessageType.LOG);
								}
								else if (message.mData[1] == (byte)KWP2000ResponseCode.RequestOutOfRange)
								{
									DisplayStatusMessage("Start diagnostic session failed, ECU reports request out of range. Trying again with a different format.", StatusMessageType.LOG);
								}
							}
						}
					}
				}
				else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.StopDiagnosticSession, message))
				{
					giveUpAndTryToUseCurrentSession = true;
				}

                if (giveUpAndTryToUseCurrentSession)
                {
					handled = true;

					DisplayStatusMessage("Unable to properly start diagnostic session, attempting to continue with current undefined session.", StatusMessageType.USER);

					if (message.mData[1] == (byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)
					{
						if (mSessionType == KWP2000DiagnosticSessionType.ProgrammingSession)
						{
							DisplayStatusMessage("This can occur if the security lockout is running, or the engine is running. Please turn off the ignition and retry if this continues to fail.", StatusMessageType.USER);
						}
						else
						{
							DisplayStatusMessage("Please turn off the ignition and retry if this continues to fail.", StatusMessageType.USER);
						}
					}

                    ActionCompleted(true);
                }
                
                if (!handled)
                {
                    DisplayStatusMessage("Failed to start diagnostic session.", StatusMessageType.USER);

                    if (message.mServiceID == (byte)KWP2000ServiceID.NegativeResponse)
                    {
                        if (message.DataLength >= 2)
                        {
                            DisplayStatusMessage("Received negative message response with code: " + Enum.GetName(typeof(KWP2000ResponseCode), message.mData[1]), StatusMessageType.USER);
                        }
                    }

                    ActionCompleted(false);
                }
            }

            return handled;
        }

        public static uint CalculateBaudRateFromByte(byte baudRateByte)
        {
            //TODO: support special baud rates
            //Special Baud Rates for StartDiagnosticSession according to FIAT KWP2000 spec, don't know if S4 ECU supports these
            //0x01 - 9600
            //0x02 - 19200
            //0x03 - 38400
            //0x04 - 57600
            //0x05 - 115200

            byte xUpper = (byte)(baudRateByte >> 0x5);
            xUpper &= 0x7;

            byte yLower = (byte)(baudRateByte & 0x1F);

            byte xPow = (byte)(0x1 << xUpper);

            var baudRate = (uint)((xPow * (yLower + 32) * 6400) / 32);

			//Debug.Assert(CalculateBaudRateByte(baudRate) == baudRateByte);

            return baudRate;
        }

        //TODO: I think baud 0x78 means keep current baud rate, but only when already in programming mode
        public static byte CalculateBaudRateByte(uint baudRate)
        {
            //baud Rate = (((1 << ((rateByte >> 5) & 0x07)) * ((rateByte & 0x1F) + 32) * 6400) >> 5)
            //baud rate = (2^X * (Y + 32) * 6400) / 32
            //X = 0 to 7, Y = 0 to 31
            //baud rate data byte = XXXYYYYY

            uint baseValue = baudRate * 32 / 6400;
            uint bestExpResult = 1;
            uint bestExp = 0;
            double bestScalarDist = 64.0;

            for (int exp = 7; exp >= 0; exp--)
            {
                uint expResult = ((uint)1 << exp);

                if (expResult < baseValue)
                {
                    double scalar = ((double)baseValue) / ((double)expResult);

                    if ((scalar < 64) && (scalar > 32))
                    {
                        double scalarDist = scalar - Math.Floor(scalar);

                        if (scalarDist < bestScalarDist)
                        {
                            bestScalarDist = scalarDist;
                            bestExp = (uint)exp;
                            bestExpResult = expResult;
                        }
                    }
                }
            }

            Debug.Assert(baseValue >= bestExpResult, "Y value is too large");
            uint z = (baseValue / bestExpResult) - 32;

            var baudRateByte = (byte)(((bestExp & 0x7) << 5) | (z & 0x1F));

			//Debug.Assert(CalculateBaudRateFromByte(baudRateByte) == baudRate);

			return baudRateByte;
        }

        public enum MessageFormatState
        {
            SpecificBaudRate,
            NoBaudRate,
            StopSession
        }

        protected MessageFormatState mMessageFormatState;
        protected KWP2000DiagnosticSessionType mSessionType;
        protected IEnumerable<uint> mBaudRates;
		protected IEnumerator<uint> mCurrentBaudRate;
    };	

    public class StopDiagnosticSessionAction : KWP2000Action
    {
        public StopDiagnosticSessionAction(KWP2000Interface commInterface)
            : base(commInterface)
        {
        
		}

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                SendMessage((byte)KWP2000ServiceID.StopDiagnosticSession);
                DisplayStatusMessage("Stopping diagnostic session.", StatusMessageType.USER);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.StopDiagnosticSessionPositiveResponse)
                {
                    DisplayStatusMessage("Successfully stopped diagnostic session.", StatusMessageType.USER);

                    //spec says stopping session switches to standard session
                    KWP2000CommInterface.CurrentDiagnosticSessionType = KWP2000DiagnosticSessionType.StandardSession;

                    ActionCompleted(true);
                    handled = true;
                }

                if (!handled)
                {
                    DisplayStatusMessage("Failed to stop diagnostic session.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }
    }

    public class StopCommunicationAction : KWP2000Action
    {
        public StopCommunicationAction(KWP2000Interface commInterface)
            : base(commInterface)
        {
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                SendMessage((byte)KWP2000ServiceID.StopCommunication);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (message.mServiceID == (byte)KWP2000ServiceID.StopCommunicationPositiveResponse)
            {
                ActionCompleted(true);
                handled = true;
            }
                        
            if (!handled)
            {                 
                ActionCompleted(false);
            }

            return handled;
        }
    }

    public class AccessTimingParameters : KWP2000Action
    {
        public enum TimingParameterIdentifier : byte
        {
            ReadLimits = 0,
            SetToDefaults = 1,
            ReadCurrentValues = 2,
            SetValues = 3
        }

        public AccessTimingParameters(KWP2000Interface commInterface, TimingParameterIdentifier TPI)
            : base(commInterface)
        {
            mTPI = TPI;
            mTimingParams = null;
        }

        public AccessTimingParameters(KWP2000Interface commInterface, TimingParameterIdentifier TPI, TimingParameters param)
            : this(commInterface, TPI)
        {
            mTimingParams = param;
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                Debug.Assert(mTimingParams != null);

                SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, CreateMessageData(mTPI, mTimingParams));

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.AccessTimingParametersPositiveResponse)
                {
                    if (message.DataLength >= 6)
                    {
                        //make sure the TPI matches
                        Debug.Assert(message.mData[0] == (byte)mTPI);

                        GetTimingParamsFromData(message.mData, mTimingParams);
                    }

                    ActionCompleted(true);
                    handled = true;
                }
            }

            return handled;
        }

        public static void GetTimingParamsFromData(byte[] messageData, TimingParameters param)
        {
            Debug.Assert(messageData != null);
            Debug.Assert(messageData.Length == 6);

            param.P2ECUResponseTimeMinMs = (long)(messageData[1] * 0.5);
            param.P2ECUResponseTimeMaxMs = (long)(messageData[2] * 25);//only if less than F0, TODO support this
            param.P3TesterResponseTimeMinMs = (long)(messageData[3] * 0.5);
            param.P3TesterResponseTimeMaxMs = (long)(messageData[4] * 250);//infinity if 255, TODO support this
            param.P4TesterInterByteTimeMinMs = (long)(messageData[5] * 0.5);
        }

        protected static void GetDataFromTimingParams(TimingParameters param, byte[] messageData)
        {
            Debug.Assert(messageData != null);
            Debug.Assert(messageData.Length == 6);

            messageData[1] = (byte)(param.P2ECUResponseTimeMinMs / 0.5);
            messageData[2] = (byte)(param.P2ECUResponseTimeMaxMs / 25); //only if less than F0, TODO support this
            messageData[3] = (byte)(param.P3TesterResponseTimeMinMs / 0.5);
            messageData[4] = (byte)(param.P3TesterResponseTimeMaxMs / 250);//infinity if 255, TODO support this
            messageData[5] = (byte)(param.P4TesterInterByteTimeMinMs / 0.5);
        }

        public static byte[] CreateMessageData(TimingParameterIdentifier tpi, TimingParameters param = null)
        {
			if (param == null)
			{
				param = new TimingParameters();
			}

			//Note: we always need to send 6 bytes, even when reading defaults, reading limits, going to defaults, etc.
			var data = new byte[6];
			GetDataFromTimingParams(param, data);

            data[0] = (byte)tpi;

            return data;
        }

        public TimingParameterIdentifier mTPI;
        public TimingParameters mTimingParams;
    };

    //NOTE: when a StartDiagnosticSession occurs, it resets the communication timings to defaults
    public class NegotiateTimingParameters : KWP2000Action
    {
        public enum NegotiationTarget
        {
            Default,
            Current,
            Limits
        }

        public NegotiateTimingParameters(KWP2000Interface commInterface)
            : this(commInterface, NegotiationTarget.Limits)
        {
        }

        public NegotiateTimingParameters(KWP2000Interface commInterface, NegotiationTarget target)
            : base(commInterface)
        {
            mCurrentTimingParams = new TimingParameters();
            mTarget = target;
        }

        public static bool ShouldNegotiateTimingParameters(KWP2000Interface commInterface, NegotiationTarget targetTimings)
        {
            switch (targetTimings)
            {
                case NegotiationTarget.Default:
                {
                    return (commInterface.CurrentTimingParameterMode != KWP2000Interface.TimingParameterMode.Default);
                }
                case NegotiationTarget.Limits:
                {
					return (commInterface.CurrentTimingParameterMode != KWP2000Interface.TimingParameterMode.Limits);
                }
                case NegotiationTarget.Current:
                {
					return (commInterface.CurrentTimingParameterMode != KWP2000Interface.TimingParameterMode.Current);
                }
            }

            return true;
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                if (ShouldNegotiateTimingParameters(KWP2000CommInterface, mTarget))
                {
                    DisplayStatusMessage("Negotiating communication timings.", StatusMessageType.USER);

                    mState = NegotiationState.READ_CURRENT;

                    DisplayStatusMessage("Reading current communication timings.", StatusMessageType.LOG);

                    SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, AccessTimingParameters.CreateMessageData(AccessTimingParameters.TimingParameterIdentifier.ReadCurrentValues));
                }
                else
                {
                    DisplayStatusMessage("Skipping communication timing negotiation.", StatusMessageType.LOG);
                    ActionCompleted(true);
                }
                
                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.AccessTimingParameters, message))
                {
                    if (mState == NegotiationState.READ_CURRENT)
                    {
                        AccessTimingParameters.GetTimingParamsFromData(message.mData, mCurrentTimingParams);

                        DisplayStatusMessage("Read current timings: " + mCurrentTimingParams.ToString(), StatusMessageType.DEV);

						KWP2000CommInterface.CurrentTimingParameterMode = KWP2000Interface.TimingParameterMode.Current;
                        KWP2000CommInterface.CurrentTimingParameters = mCurrentTimingParams;

                        if (mTarget == NegotiationTarget.Limits)
                        {
                            DisplayStatusMessage("Reading communication timing limits.", StatusMessageType.LOG);

                            mState = NegotiationState.READ_LIMITS;
                            SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, AccessTimingParameters.CreateMessageData(AccessTimingParameters.TimingParameterIdentifier.ReadLimits));
                        }
                        else if (mTarget == NegotiationTarget.Default)
                        {
                            DisplayStatusMessage("Setting communication timing to defaults.", StatusMessageType.LOG);

                            mState = NegotiationState.SET_TO_DEFAULTS;                            
                            SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, AccessTimingParameters.CreateMessageData(AccessTimingParameters.TimingParameterIdentifier.SetToDefaults));
                        }
                        else
                        {
                            DisplayStatusMessage("Timing negotiation complete.", StatusMessageType.USER);
                            ActionCompleted(true);
                        }
                    }
                    else if (mState == NegotiationState.READ_LIMITS)
                    {
                        //not setting P2 max to max causes data transfer problems, default 50ms seems too short
                        AccessTimingParameters.GetTimingParamsFromData(message.mData, mCurrentTimingParams);

                        DisplayStatusMessage("Read timing limits: " + mCurrentTimingParams.ToString(), StatusMessageType.DEV);
                        
                        mCurrentTimingParams.EnforceTimingIntervalRequirements();

                        DisplayStatusMessage("Requesting timing limits: " + mCurrentTimingParams.ToString(), StatusMessageType.DEV);

                        DisplayStatusMessage("Requesting communication at timing limits.", StatusMessageType.LOG);

                        mState = NegotiationState.SET_NEW_TIMING;
                        SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, AccessTimingParameters.CreateMessageData(AccessTimingParameters.TimingParameterIdentifier.SetValues, mCurrentTimingParams));
                    }
                    else if (mState == NegotiationState.SET_TO_DEFAULTS)
                    {
                        DisplayStatusMessage("Reading current communication timings.", StatusMessageType.LOG);

                        mState = NegotiationState.SET_NEW_TIMING;
                        SendMessage((byte)KWP2000ServiceID.AccessTimingParameters, AccessTimingParameters.CreateMessageData(AccessTimingParameters.TimingParameterIdentifier.ReadCurrentValues));
                    }
                    else if (mState == NegotiationState.SET_NEW_TIMING)
                    {
                        DisplayStatusMessage("Set timing: " + mCurrentTimingParams.ToString(), StatusMessageType.DEV);

                        if (mTarget == NegotiationTarget.Default)
                        {
							KWP2000CommInterface.CurrentTimingParameterMode = KWP2000Interface.TimingParameterMode.Default;
                        }
                        else if (mTarget == NegotiationTarget.Limits)
                        {
							KWP2000CommInterface.CurrentTimingParameterMode = KWP2000Interface.TimingParameterMode.Limits;
                        }

                        KWP2000CommInterface.CurrentTimingParameters = mCurrentTimingParams;

                        mState = NegotiationState.FINISHED;

                        DisplayStatusMessage("Successfully changed to new communication timings.", StatusMessageType.USER);
                        ActionCompleted(true);
                    }

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.AccessTimingParameters, message))
                {
                    Debug.Assert(message.DataLength >= 2);
                    DisplayStatusMessage("Timing negotiation failed. Communication timings are unchanged.", StatusMessageType.USER);
                    ActionCompleted(true);

                    handled = true;
                }
            }

            return handled;
        }

        private enum NegotiationState
        {
            READ_CURRENT,
            READ_LIMITS,
            SET_NEW_TIMING,
            SET_TO_DEFAULTS,
            READ_DEFAULTS,
            FINISHED
        }

        private NegotiationTarget mTarget;
        private NegotiationState mState;
        private TimingParameters mCurrentTimingParams;
    }

    public class EraseFlashAction : KWP2000Action
    {
        public readonly uint ENTIRE_FLASH_START_ADDRESS = 0xE001;
        public readonly uint ENTIRE_FLASH_END_ADDRESS = 0xE002;

        public EraseFlashAction(KWP2000Interface commInterface, uint startAddress, uint size, string flashToolCode)
            : base(commInterface)
        {
            Debug.Assert(startAddress % 2 == 0, "start address is not a multiple of 2");
            Debug.Assert(size > 0, "size is zero");
            Debug.Assert(size % 2 == 0, "size is not a multiple of 2");

            mStartAddress = startAddress;
            mEndAddress = startAddress + size - 1;
            mFlashToolCode = flashToolCode;

            Debug.Assert(mFlashToolCode.Length == 6, "Flash tool code must be 6 characters");
            Debug.Assert(mFlashToolCode != "000000", "Flash tool code cannot be all zeros");
        }

        public EraseFlashAction(KWP2000Interface commInterface, string flashToolCode)
            : base(commInterface)
        {
            mStartAddress = ENTIRE_FLASH_START_ADDRESS;
            mEndAddress = ENTIRE_FLASH_END_ADDRESS;
            mFlashToolCode = flashToolCode;

            Debug.Assert(mFlashToolCode.Length == 6, "Flash tool code must be 6 characters");
            Debug.Assert(mFlashToolCode != "000000", "Flash tool code cannot be all zeros");
        }

        public bool FailedBecauseOfPersistentData { get; private set; }

        public override bool Start()
        {
            bool started = false;

            FailedBecauseOfPersistentData = false;
                        
            if (base.Start())
            {
                KWP2000MessageHelpers.SendRequestEraseFlashMessage(KWP2000CommInterface, mStartAddress, mEndAddress, mFlashToolCode);

                if ((mStartAddress == ENTIRE_FLASH_START_ADDRESS) && (mEndAddress == ENTIRE_FLASH_END_ADDRESS))
                {
                    DisplayStatusMessage("Requesting flash memory erase of entire flash memory.", StatusMessageType.USER);
                }
                else
                {
                    DisplayStatusMessage("Requesting flash memory erase for address range 0x" + mStartAddress.ToString("X8") + " to 0x" + mEndAddress.ToString("X8") + ".", StatusMessageType.USER);
                }

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, message))
                {
                    if (message.DataLength >= 1)
                    {
                        if (message.mData[0] == (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash)
                        {
                            if (message.DataLength >= 2)
                            {
                                //if non-zero do we need to close and reopen the connection?
                                uint requestedWaitTimeMS = message.mData[1] * 10U;

                                //TODO: support wait times for erasing flash
                                DisplayStatusMessage("ECU requested a wait time of " + requestedWaitTimeMS + "ms. This not currently supported and may cause problems.", StatusMessageType.LOG);
                            }
                        }
                        else
                        {
                            DisplayStatusMessage("Received notification that the wrong routine was started, assuming flash erasing was started.", StatusMessageType.LOG);
                        }
                    }

                    KWP2000MessageHelpers.SendRequestEraseFlashResultMessage(KWP2000CommInterface);

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, message))
                {   
                    if (message.DataLength >= 2)
                    {
                        if ((message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                            || (message.mData[1] == (byte)KWP2000ResponseCode.DownloadNotAccepted)
                            || (message.mData[1] == (byte)KWP2000ResponseCode.ImproperDownloadType))
                        {
                            DisplayStatusMessage("Erase flash memory routine did not start or complete correctly.", StatusMessageType.USER);
                                                        
                            ActionCompleted(false);

                            handled = true;
                        }
                        else if(message.mData[1] == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)
                        {
                            DisplayStatusMessage("ECU reports sub function not supported or invalid format while attempting to erase flash memory.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                        else if(message.mData[1] == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested)
                        {
                            DisplayStatusMessage("ECU reports security access requested while attempting to erase flash memory.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                        {
                            DisplayStatusMessage("ECU reports routine not complete while attempting to erase flash memory.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                    }
                }
                else if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, message))
                {
                    bool success = false;

                    if (message.DataLength >= 2)
                    {
                        if (message.mData[0] == (byte)KWP2000VAGLocalIdentifierRoutine.EraseFlash)
                        {
                            success = (message.mData[1] == 0);

							if (!success)
							{
								DisplayStatusMessage("Received unsuccessful response to erase flash request: 0x" + message.mData[1].ToString("X2"), StatusMessageType.LOG);
							}
                        }
                        else
                        {
                            DisplayStatusMessage("Received result of routine for wrong routine.", StatusMessageType.LOG);
                        }
                    }

                    if (success)
                    {
                        DisplayStatusMessage("Successfully erased flash memory.", StatusMessageType.USER);
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to erase flash memory.", StatusMessageType.USER);
                    }

                    ActionCompleted(success);

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, message))
                {
                    if (message.DataLength >= 2)
                    {
                        if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                        {
                            KWP2000MessageHelpers.SendRequestEraseFlashResultMessage(KWP2000CommInterface);

                            handled = true;
                        }
                        else if ((message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject) 
                            ||(message.mData[1] == (byte)KWP2000ResponseCode.DownloadNotAccepted))
                        {
                            DisplayStatusMessage("Erase flash memory routine did not start or complete correctly.", StatusMessageType.USER);

                            FailedBecauseOfPersistentData = true;

                            ActionCompleted(false);

                            handled = true;
                        }                        
                    }
                }

                if (!handled)
                {
                    DisplayStatusMessage("Failed to erase flash memory. You may be using the wrong memory layout.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }       

        protected uint mStartAddress;
        protected uint mEndAddress;
        protected string mFlashToolCode;
    }

    public class ValidateFlashChecksumAction : KWP2000Action
    {
        public ValidateFlashChecksumAction(KWP2000Interface commInterface, uint startAddress, byte[] data)
            : base(commInterface)
        {
            mStartAddress = startAddress;
            mEndAddress = startAddress + (uint)data.Length - 1;
            mChecksum = CalculateChecksum(data);

            Debug.Assert((mStartAddress & 0xFF0000000000) == 0);
            Debug.Assert((mEndAddress & 0xFF0000000000) == 0);
        }

        public bool IsFlashChecksumCorrect{ get; private set; }

        public override bool Start()
        {
            bool started = false;

            IsFlashChecksumCorrect = false;

            if (base.Start())
            {
                DisplayStatusMessage("Validating flashed data checksum for address range 0x" + mStartAddress.ToString("X8") + " to 0x" + mEndAddress.ToString("X8") + ".", StatusMessageType.LOG);
                DisplayStatusMessage("Assuming checksum is: 0x" + mChecksum.ToString("X4"), StatusMessageType.DEV);

                KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, mStartAddress, mEndAddress, mChecksum);
                
                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, message))
                {
                    if (message.DataLength >= 1)
                    {
                        if (message.mData[0] == (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum)
                        {
                            if (message.DataLength >= 2)
                            {
                                //if non-zero do we need to close and reopen the connection?
                                uint requestedWaitTimeMS = message.mData[1] * 10U;

                                //TODO: support wait times for erasing flash
                                DisplayStatusMessage("ECU requested a wait time of " + requestedWaitTimeMS + "ms. This not currently supported and may cause problems.", StatusMessageType.LOG);
                            }
                        }
                        else
                        {
                            DisplayStatusMessage("Received notification that the wrong routine was started.", StatusMessageType.LOG);
                        }
                    }

                    KWP2000MessageHelpers.SendRequestValidateFlashChecksumResultMessage(KWP2000CommInterface);

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.StartRoutineByLocalIdentifier, message))
                {
                    if (message.DataLength >= 2)
                    {
                        if ((message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                            || (message.mData[1] == (byte)KWP2000ResponseCode.DownloadNotAccepted))
                        {
                            DisplayStatusMessage("Checksum validation routine did not start or complete correctly.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)
                        {
                            DisplayStatusMessage("ECU reports sub function not supported or invalid format while attempting to validate checksum.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested)
                        {
                            DisplayStatusMessage("ECU reports security access requested while attempting to validate checksum.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                        {
                            DisplayStatusMessage("ECU reports routine not complete while attempting to validate checksum.", StatusMessageType.USER);

                            ActionCompleted(false);

                            handled = true;
                        }
                    }
                }
                else if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, message))
                {
                    if (message.mData[0] == (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum)
                    {
                        IsFlashChecksumCorrect = (message.mData[1] == 0);

                        if (IsFlashChecksumCorrect)
                        {
                            DisplayStatusMessage("Checksum is correct.", StatusMessageType.LOG);
                        }
                        else
                        {
                            DisplayStatusMessage("Checksum is incorrect.", StatusMessageType.LOG);
                        }

                        ActionCompleted(true);

                        handled = true;
                    }
                    else
                    {
                        DisplayStatusMessage("Received result of routine for wrong routine.", StatusMessageType.LOG);
                    }
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifier, message))
                {
                    if (message.DataLength >= 2)
                    {
                        if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                        {
                            KWP2000MessageHelpers.SendRequestValidateFlashChecksumResultMessage(KWP2000CommInterface);

                            handled = true;
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.BlockTransferDataChecksumError)
                        {
                            DisplayStatusMessage("Checksum is incorrect.", StatusMessageType.LOG);

                            IsFlashChecksumCorrect = false;
                            ActionCompleted(true);

                            handled = true;
                        }
                        else if ((message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                            || (message.mData[1] == (byte)KWP2000ResponseCode.DownloadNotAccepted))
                        {
                            DisplayStatusMessage("Checksum validation routine did not start or complete correctly.", StatusMessageType.USER);

                            IsFlashChecksumCorrect = false;//assume it is false
                            ActionCompleted(true);

                            handled = true;
                        }
                    }
                }

                if (!handled)
                {
                    DisplayStatusMessage("Checking flash data checksum failed. Received unhandled response.", StatusMessageType.LOG);
                    ActionCompleted(false);
                }
            }

            return handled;
        }
                        
        protected ushort CalculateChecksum(byte[] data)
        {
            uint[] tempBuffer = new uint[256];

            for (uint x = 0; x < 256; x++)
            {
                uint curVal = x;

                for (uint y = 0; y < 8; y++)
                {
                    if ((curVal & 0x00000001) != 0)
                    {
                        curVal = (curVal >> 1) ^ 0xEDB88320;
                    }
                    else
                    {
                        curVal >>= 1;
                    }
                }

                tempBuffer[x] = curVal;
            }

            uint checksum = 0xFFFFFFFF;

            for (uint x = 0; x < data.Length; x++)
            {
                checksum = (checksum >> 8) ^ tempBuffer[((byte)(checksum & 0xFF)) ^ data[x]];
            }

            checksum ^= 0xFFFFFFFF;

            return (ushort)(checksum & 0xFFFF);
        }

        protected uint mStartAddress;
        protected uint mEndAddress;
        protected ushort mChecksum;
    }

    //this doesn't work, ECU returns checksum not correct errors for out of range requests and for when checksum isn't correct
    //public class ValidateStartAndEndAddressesWithChecksumsAction : KWP2000Action
    //{
    //    public enum Result
    //    {
    //        Valid,
    //        StartInvalid,
    //        EndInvalid,
    //        StartIsntLowest,
    //        EndIsntHighest,
    //        ValidationDidNotComplete
    //    }

    //    public Result ValidationResult{ get; protected set;}

    //    public ValidateStartAndEndAddressesWithChecksumsAction(KWP2000Interface commInterface, uint startAddress, uint endAddress)
    //        : base(commInterface)
    //    {
    //        //TODO: check endaddress > startaddress
    //        mStartAddress = startAddress;
    //        mEndAddress = endAddress;
    //        ValidationResult = Result.ValidationDidNotComplete;
    //    }

    //    public override bool Start()
    //    {
    //        bool started = false;

    //        if (base.Start())
    //        {
    //            mState = ValidationState.Start;
    //            ValidationResult = Result.ValidationDidNotComplete;

    //            DisplayStatusMessage("Validating flash start and end addresses with start: 0x" + mStartAddress.ToString("X8") + " end: 0x" + mEndAddress.ToString("X8"), StatusMessageType.LOG);

    //            SendValidateMessage();

    //            started = true;
    //        }

    //        return started;
    //    }

    //    protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
    //    {
    //         bool handled = base.MessageHandler(commInterface, message);

    //         if (!handled)
    //         {
    //             if (message.mServiceID == (byte)KWP2000ServiceID.StartRoutineByLocalIdentifierPositiveResponse)
    //             {
    //                 KWP2000MessageHelpers.SendRequestValidateFlashChecksumResultMessage(KWP2000CommInterface);

    //                 handled = true;
    //             }
    //             else if (message.mServiceID == (byte)KWP2000ServiceID.RequestRoutineResultsByLocalIdentifierPositiveResponse)
    //             {
    //                 if (message.mData[0] == (byte)KWP2000VAGLocalIdentifierRoutine.ValidateFlashChecksum)
    //                 {
    //                     HandleValidateChecksumResponse(true);
                        
    //                     handled = true;
    //                 }
    //                 else
    //                 {
    //                     DisplayStatusMessage("Received result of routine for wrong routine.", StatusMessageType.LOG);
    //                 }
    //             }
    //             else if (message.mServiceID == (byte)KWP2000ServiceID.NegativeResponse)
    //             {
    //                 if (message.DataLength >= 2)
    //                 {
    //                     handled = true;

    //                     if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
    //                     {
    //                         KWP2000MessageHelpers.SendRequestValidateFlashChecksumResultMessage(KWP2000CommInterface);
    //                     }
    //                     else if (message.mData[1] == (byte)KWP2000ResponseCode.BlockTransferDataChecksumError)
    //                     {
    //                         HandleValidateChecksumResponse(true);                             
    //                     }
    //                     else
    //                     {
    //                         HandleValidateChecksumResponse(false);
    //                     }
    //                 }
    //             }

    //             if (!handled)
    //             {
    //                 DisplayStatusMessage("Validating flash start and end addresses failed. Received unhandled response.", StatusMessageType.LOG);
    //                 ActionCompleted(false);
    //             }
    //         }

    //         return handled;
    //    }

    //    protected void SendValidateMessage()
    //    {
    //        switch (mState)
    //        {
    //            case ValidationState.Start:
    //            {
    //                KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, mStartAddress, mStartAddress + 2, 0);
    //                break;
    //            }
    //            case ValidationState.End:
    //            {
    //                //TODO: check end address >= 2
    //                KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, mEndAddress - 2, mEndAddress, 0);
    //                break;
    //            }
    //            case ValidationState.BeforeStart:
    //            {
    //                //TODO: check start address >= 2
    //                KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, mStartAddress - 2, mStartAddress, 0);
    //                break;
    //            }
    //            case ValidationState.AfterEnd:
    //            {
    //                KWP2000MessageHelpers.SendValidateFlashChecksumMessage(KWP2000CommInterface, mEndAddress, mEndAddress + 2, 0);
    //                break;
    //            }
    //            default:
    //            {
    //                Debug.Fail("Unknown state");
    //                break;
    //            }
    //        }
    //    }

    //    protected void HandleValidateChecksumResponse(bool didValidateChecksumWork)
    //    {
    //        switch (mState)
    //        {
    //            case ValidationState.Start:
    //            {
    //                if (didValidateChecksumWork)
    //                {
    //                    mState = ValidationState.End;
    //                    DisplayStatusMessage("Flash start address is valid.", StatusMessageType.LOG);
    //                    SendValidateMessage();
    //                }
    //                else
    //                {
    //                    ValidationResult = Result.StartInvalid;
    //                    DisplayStatusMessage("Flash start address is invalid.", StatusMessageType.LOG);
    //                    ActionCompleted(false);
    //                }

    //                break;
    //            }
    //            case ValidationState.End:
    //            {
    //                if (didValidateChecksumWork)
    //                {
    //                    mState = ValidationState.BeforeStart;
    //                    DisplayStatusMessage("Flash end address is valid.", StatusMessageType.LOG);
    //                    SendValidateMessage();
    //                }
    //                else
    //                {
    //                    ValidationResult = Result.EndInvalid;
    //                    DisplayStatusMessage("Flash end address is invalid.", StatusMessageType.LOG);
    //                    ActionCompleted(false);
    //                }
                    
    //                break;
    //            }
    //            case ValidationState.BeforeStart:
    //            {
    //                if (!didValidateChecksumWork)
    //                {
    //                    mState = ValidationState.AfterEnd;
    //                    DisplayStatusMessage("Flash start address is the lowest address.", StatusMessageType.LOG);
    //                    SendValidateMessage();
    //                }
    //                else
    //                {
    //                    ValidationResult = Result.StartIsntLowest;
    //                    DisplayStatusMessage("Flash start address isn't the lowest address.", StatusMessageType.LOG);
    //                    ActionCompleted(false);
    //                }
                    
    //                break;
    //            }
    //            case ValidationState.AfterEnd:
    //            {
    //                if (!didValidateChecksumWork)
    //                {
    //                    ValidationResult = Result.Valid;
    //                    DisplayStatusMessage("Flash end address is the highest address.", StatusMessageType.LOG);
    //                    DisplayStatusMessage("Flash start and end addresses are valid", StatusMessageType.LOG);
    //                    ActionCompleted(true);
    //                }
    //                else
    //                {
    //                    ValidationResult = Result.EndIsntHighest;
    //                    DisplayStatusMessage("Flash end address isn't the highest address.", StatusMessageType.LOG);
    //                    ActionCompleted(false);
    //                }
                    
    //                break;
    //            }
    //            default:
    //            {
    //                Debug.Fail("Unknown state");
    //                break;
    //            }
    //        }
    //    }

    //    protected enum ValidationState
    //    {
    //        Start,
    //        End,
    //        BeforeStart,
    //        AfterEnd
    //    }

    //    protected ValidationState mState;
    //    protected uint mStartAddress;
    //    protected uint mEndAddress;
    //}

    public class ValidateStartAndEndAddressesWithRequestUploadDownloadAction : KWP2000Action
    {
        public enum Result
        {
            ValidationRunning,
            Valid,
            StartInvalid,
            EndInvalid,
            StartIsntLowest,
            EndIsntHighest,
            ValidationDidNotComplete
        }

        public Result ValidationResult { get; protected set; }

        public ValidateStartAndEndAddressesWithRequestUploadDownloadAction(KWP2000Interface commInterface, uint startAddress, uint endAddress)
            : base(commInterface)
        {
            //TODO: check endaddress > startaddress
            mStartAddress = startAddress;
            mEndAddress = endAddress;
            ValidationResult = Result.ValidationDidNotComplete;
            mMemoryTestServiceID = (byte)KWP2000ServiceID.RequestUpload;
            //requestdownload doesn't work, because the download operation won't finish or stop until it has had all bytes transfered
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                DisplayStatusMessage("Validating flash memory starts at 0x" + mStartAddress.ToString("X8") + " and ends at 0x" + mEndAddress.ToString("X8") + ".", StatusMessageType.USER);

                mState = ValidationState.Range;
                ValidationResult = Result.ValidationRunning;

                SendRequestMessage();

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsResponseToRequest(mMemoryTestServiceID, message))
                {                    
                    if (KWP2000Interface.IsPositiveResponseToRequest(mMemoryTestServiceID, message))
                    {
                        HandleResponse(true, 0);

                        if (ValidationResult == Result.ValidationRunning)
                        {
                            SendRequestMessage();
                        }
                        else
                        {
                            ActionCompleted(true);
                        }

                        handled = true;
                    }
                    else
                    {
                        if (message.DataLength >= 2)
                        {
                            if((message.mData[1] == (byte)KWP2000ResponseCode.ServiceNotSupported) || (message.mData[1] == (byte)KWP2000ResponseCode.NoProgram))
                            {
								if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Validation failed, ECU reports RequestDownload service is not supported.", StatusMessageType.USER);
                                }
								else if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Validation failed, ECU reports RequestUpload service is not supported. RequestUpload may have been disabled by aftermarket engine software.", StatusMessageType.USER);
                                }

                                ValidationResult = Result.ValidationDidNotComplete;

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                            {
								if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Validation failed, ECU reports RequestDownload was rejected.", StatusMessageType.USER);
                                }
								else if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Validation failed, ECU reports RequestUpload was rejected. RequestUpload may have been disabled by aftermarket engine software.", StatusMessageType.USER);
                                }

                                ValidationResult = Result.ValidationDidNotComplete;

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested)
                            {
                                DisplayStatusMessage("Validation failed, ECU reports that security access is not granted.", StatusMessageType.USER);

                                ValidationResult = Result.ValidationDidNotComplete;

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)
                            {
                                DisplayStatusMessage("Validation failed, ECU reports conditions not correct or sequence error. Please turn off the ignition and retry.", StatusMessageType.USER);

                                ValidationResult = Result.ValidationDidNotComplete;

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                            {
                                DisplayStatusMessage("Validation failed, ECU reports routine not complete or service in progress. Please turn off the ignition and retry.", StatusMessageType.USER);

                                ValidationResult = Result.ValidationDidNotComplete;

                                handled = true;
                                ActionCompleted(false);
                            }
                            else
                            {
                                HandleResponse(false, message.mData[1]);

                                if (ValidationResult == Result.ValidationRunning)
                                {
                                    SendRequestMessage();
                                }
                                else
                                {
                                    ActionCompleted(ValidationResult != Result.ValidationDidNotComplete);
                                }

                                handled = true;
                            }                                                        
                        }
                    }                    
                }
                                
                if (!handled)
                {
                    DisplayStatusMessage("Validating flash start and end addresses failed. Received unhandled response.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected void SendRequestMessage()
        {
            byte dataFormat = 0;

			if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
            {
                dataFormat = TransferDataAction.GetDataFormatByte(TransferDataAction.CompressionType.Bosch, TransferDataAction.EncryptionType.Bosch);
            }
            else
            {
                dataFormat = TransferDataAction.GetDataFormatByte(TransferDataAction.CompressionType.Uncompressed, TransferDataAction.EncryptionType.Unencrypted);
            }

            uint size = mEndAddress - mStartAddress;

            switch (mState)
            {
                case ValidationState.Range:
                {
					if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
                    {
                        KWP2000MessageHelpers.SendRequestDownloadMessage(KWP2000CommInterface, mStartAddress, size, dataFormat);
                    }
                    else
                    {
                        KWP2000MessageHelpers.SendRequestUploadMessage(KWP2000CommInterface, mStartAddress, size, dataFormat);
                    }
                    break;
                }               
                case ValidationState.BeforeStart:
                {
					if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
                    {
                        KWP2000MessageHelpers.SendRequestDownloadMessage(KWP2000CommInterface, mStartAddress - 2, size + 2, dataFormat);
                    }
                    else
                    {
                        KWP2000MessageHelpers.SendRequestUploadMessage(KWP2000CommInterface, mStartAddress - 2, size + 2, dataFormat);
                    }
                    break;
                }
                case ValidationState.AfterEnd:
                {
					if (mMemoryTestServiceID == (byte)KWP2000ServiceID.RequestDownload)
                    {
                        KWP2000MessageHelpers.SendRequestDownloadMessage(KWP2000CommInterface, mStartAddress, size + 2, dataFormat);
                    }
                    else
                    {
                        KWP2000MessageHelpers.SendRequestUploadMessage(KWP2000CommInterface, mStartAddress, size + 2, dataFormat);
                    }
                    break;
                }                
                default:
                {
                    Debug.Fail("Unknown state");
                    break;
                }
            }
        }

        protected void HandleResponse(bool didRequestWork, byte failureResponseCode)
        {
            switch (mState)
            {
                case ValidationState.Range:
                {
                    if (didRequestWork)
                    {
                        if (mStartAddress >= 2)
                        {
                            mState = ValidationState.BeforeStart;
                        }
                        else
                        {
                            mState = ValidationState.AfterEnd;
                        }

                        DisplayStatusMessage("Flash start and end addresses are valid.", StatusMessageType.LOG);                        
                    }
                    else
                    {
                        if ((failureResponseCode == (byte)KWP2000ResponseCode.CanNotUploadFromSpecifiedAddress)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotDownloadToSpecifiedAddress))
                        {
                            ValidationResult = Result.StartInvalid;
                            DisplayStatusMessage("Flash start address is invalid.", StatusMessageType.LOG);
                        }
                        else if ((failureResponseCode == (byte)KWP2000ResponseCode.CanNotUploadNumberOfBytesRequested)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotDownloadNumberOfBytesRequested))
                        {
                            ValidationResult = Result.EndInvalid;
                            DisplayStatusMessage("Flash end address is invalid.", StatusMessageType.LOG);
                        }
                        else
                        {
                            ValidationResult = Result.ValidationDidNotComplete;
                            DisplayStatusMessage("Unknown response code while validating address range.", StatusMessageType.LOG);
                        }
                    }

                    break;
                }                
                case ValidationState.BeforeStart:
                {
                    if (!didRequestWork)
                    {
                        if ((failureResponseCode == (byte)KWP2000ResponseCode.CanNotUploadFromSpecifiedAddress)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotDownloadToSpecifiedAddress))
                        {
                            mState = ValidationState.AfterEnd;
                            DisplayStatusMessage("Flash start address is the lowest address.", StatusMessageType.LOG);                            
                        }
                        else
                        {
                            ValidationResult = Result.ValidationDidNotComplete;
                            DisplayStatusMessage("Unknown response code while validating start address.", StatusMessageType.LOG);
                        }
                    }
                    else
                    {
                        ValidationResult = Result.StartIsntLowest;
                        DisplayStatusMessage("Flash start address isn't the lowest address.", StatusMessageType.LOG);
                    }

                    break;
                }
                case ValidationState.AfterEnd:
                {
                    if (!didRequestWork)
                    {
                        if ((failureResponseCode == (byte)KWP2000ResponseCode.CanNotUploadFromSpecifiedAddress)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotUploadNumberOfBytesRequested)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotDownloadToSpecifiedAddress)
                            || (failureResponseCode == (byte)KWP2000ResponseCode.CanNotDownloadNumberOfBytesRequested))
                        {
                            ValidationResult = Result.Valid;

                            DisplayStatusMessage("Flash end address is the highest address.", StatusMessageType.LOG);

                            DisplayStatusMessage("Flash memory addresses are valid.", StatusMessageType.LOG);
                        }
                        else
                        {
                            ValidationResult = Result.ValidationDidNotComplete;
                            DisplayStatusMessage("Unknown response code while validating end address.", StatusMessageType.LOG);
                        }
                    }
                    else
                    {
                        ValidationResult = Result.EndIsntHighest;
                        DisplayStatusMessage("Flash end address isn't the highest address.", StatusMessageType.LOG);
                    }

                    break;
                }
                default:
                {
                    Debug.Fail("Unknown state");
                    break;
                }
            }
        }

        protected enum ValidationState
        {
            Range,
            BeforeStart,
            AfterEnd
        }

        protected byte mMemoryTestServiceID;
        protected ValidationState mState;
        protected uint mStartAddress;
        protected uint mEndAddress;
    }

    public class RequestTransferExitAction : KWP2000Action
    {
        public RequestTransferExitAction(KWP2000Interface commInterface)
            : base(commInterface)
        {

        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                SendMessage((byte)KWP2000ServiceID.RequestTransferExit);
                DisplayStatusMessage("Requesting data transfer exit.", StatusMessageType.USER);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.RequestTransferExitPositiveResponse)
                {
                    DisplayStatusMessage("Successfully exited data transfer.", StatusMessageType.USER);
                    ActionCompleted(true);
                    handled = true;
                }

                if (!handled)
                {
                    DisplayStatusMessage("Failed to exit data transfer.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }
    }

    public abstract class ReadWriteMemoryActionBase : KWP2000Action
    {
        public ReadWriteMemoryActionBase(KWP2000Interface commInterface, uint startAddress, uint numBytes, byte maxBlockSize)
            : base(commInterface)
        {
            mMaxBlockSize = maxBlockSize;
            mCurrentBlockSize = 0;
            SetStartAddressAndNumBytes(startAddress, numBytes);
        }

        public virtual void SetStartAddressAndNumBytes(uint startAddress, uint numBytes)
        {
            mStartAddress = startAddress;
            mEndAddress = mStartAddress + numBytes;
            mCurrentAddress = mStartAddress;
        }

        public override bool Start()
        {
            bool result = base.Start();

            if (result)
            {
                SendMemoryRequest();

                result = true;
            }

            return result;
        }

        protected abstract void SendMemoryRequest();

        protected byte[] getAddressAndSizeMessageData(uint address, uint size)
        {
            byte[] result = new byte[4];
            result[0] = (byte)((address & 0xFF0000) >> 16);
            result[1] = (byte)((address & 0xFF00) >> 8);
            result[2] = (byte)(address & 0xFF);
            result[3] = (byte)size;

            return result;
        }

        public uint mStartAddress;
        protected uint mEndAddress;
        protected uint mCurrentAddress;

        protected byte mMaxBlockSize;
        protected byte mCurrentBlockSize;
    };

    public class ReadMemoryAction : ReadWriteMemoryActionBase
    {
		//return false to stop the action from reading any further
        public delegate bool NotifyBytesReadDelegate(uint newAmountRead, uint totalRead, uint totalToRead, ReadMemoryAction runningAction);

        public ReadMemoryAction(KWP2000Interface commInterface, uint startAddress, uint numBytes, byte maxBlockSize, NotifyBytesReadDelegate readDel)
            : base(commInterface, startAddress, numBytes, maxBlockSize)
        {
            mNotifyReadDel = readDel;
        }

        public override void SetStartAddressAndNumBytes(uint startAddress, uint numBytes)
        {
            base.SetStartAddressAndNumBytes(startAddress, numBytes);

            if (numBytes > 0)
            {
                ReadData = new byte[numBytes];
            }
            else
            {
                ReadData = null;
            }
        }

        public byte FailureResponseCode
        {
            get;
            private set;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.ReadMemoryByAddress, message))
                {
                    Buffer.BlockCopy(message.mData, 0, ReadData, (int)(mCurrentAddress - mStartAddress), message.mData.Length);

                    mCurrentAddress += (uint)message.mData.Length;

					var keepRunning = true;

                    if (mNotifyReadDel != null)
                    {
                        keepRunning = mNotifyReadDel((uint)message.mData.Length, mCurrentAddress - mStartAddress, (uint)ReadData.Length, this);
                    }

					if (keepRunning)
					{
						SendMemoryRequest();
					}
					else
					{
						ActionCompleted(true);
					}

                    handled = true;
                }
                else 
                {
					byte responseCode;
					if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.ReadMemoryByAddress, message, out responseCode))
                    {
						FailureResponseCode = responseCode;
                    }
                }

                if (!handled)
                {
                    DisplayStatusMessage("Read memory by address failed", StatusMessageType.LOG);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected override void SendMemoryRequest()
        {
			//minus 4 for the address and size data
            mCurrentBlockSize = (byte)Math.Min(mMaxBlockSize - 4, mEndAddress - mCurrentAddress);

            if (mCurrentBlockSize > 0)
            {
                SendMessage((byte)KWP2000ServiceID.ReadMemoryByAddress, getAddressAndSizeMessageData(mCurrentAddress, mCurrentBlockSize));
            }
            else
            {
                ActionCompleted(true);
            }
        }

        public byte[] ReadData
        {
            get;
            private set;
        }

        private NotifyBytesReadDelegate mNotifyReadDel;
    };

    public class WriteMemoryAction : ReadWriteMemoryActionBase
    {
        public delegate void NotifyBytesWrittenDelegate(uint newAmountWritten, uint totalWritten, uint totalToWrite);

        public WriteMemoryAction(KWP2000Interface commInterface, uint startAddress, byte maxBlockSize, byte[] dataToWrite, NotifyBytesWrittenDelegate writeDel)
            : base(commInterface, startAddress, (uint)dataToWrite.Length, maxBlockSize)
        {
            SetStartAddressAndData(startAddress, dataToWrite);

            mNotifyWriteDel = writeDel;
        }

        public WriteMemoryAction(KWP2000Interface commInterface, uint startAddress, byte maxBlockSize)
            : base(commInterface, startAddress, 0, maxBlockSize)
        {
            SetStartAddressAndData(startAddress, null);

            mNotifyWriteDel = null;
        }

        public void SetStartAddressAndData(uint startAddress, byte[] dataToWrite)
        {
            if (dataToWrite == null)
            {
                SetStartAddressAndNumBytes(startAddress, 0);
            }
            else
            {
                SetStartAddressAndNumBytes(startAddress, (uint)dataToWrite.Length);
            }

            mDataToWrite = dataToWrite;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.WriteMemoryByAddressPositiveResponse)
                {
					uint bytesWritten = mCurrentBlockSize;

					mCurrentAddress += bytesWritten;

                    SendMemoryRequest();

                    if (mNotifyWriteDel != null)
                    {
						mNotifyWriteDel(bytesWritten, mCurrentAddress - mStartAddress, (uint)mDataToWrite.Length);
                    }

                    handled = true;
                }
                //else if (message.mServiceID == (byte)KWP2000ServiceID.NegativeResponse)
                //{
                //    KWP2000ResponseCode code = (KWP2000ResponseCode)message.mData[1];

                //    if (code == KWP2000ResponseCode.SubFunctionNotSupported_InvalidFormat)
                //    {
                //        mCurrentAddress += mCurrentBlockSize;

                //        SendMemoryRequest();

                //        handled = true;
                //    }
                //}

                if (!handled)
                {
                    DisplayStatusMessage("Write memory by address failed", StatusMessageType.LOG);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected override void SendMemoryRequest()
        {
			//minus 4 for the address and size data
            mCurrentBlockSize = (byte)Math.Min(mMaxBlockSize - 4, mEndAddress - mCurrentAddress);

            if (mCurrentBlockSize > 0)
            {
                SendMessage((byte)KWP2000ServiceID.WriteMemoryByAddress, getMessageData());
            }
            else
            {
                ActionCompleted(true);
            }
        }

        protected byte[] getMessageData()
        {
            byte[] addressData = getAddressAndSizeMessageData(mCurrentAddress, mCurrentBlockSize);
            byte[] messageData = new byte[addressData.Length + mCurrentBlockSize];

            Buffer.BlockCopy(addressData, 0, messageData, 0, addressData.Length);
            Buffer.BlockCopy(mDataToWrite, (int)(mCurrentAddress - mStartAddress), messageData, addressData.Length, mCurrentBlockSize);

            return messageData;
        }

        private byte[] mDataToWrite;
        private NotifyBytesWrittenDelegate mNotifyWriteDel;

    };

    public class ReadECUIdentificationAction : KWP2000Action
    {
        public ReadECUIdentificationAction(KWP2000Interface commInterface, byte identificationOption)
            : base(commInterface)
        {
            IdentificationOption = identificationOption;
        }

        public override bool Start()
        {
            bool result = base.Start();

            if (result)
            {
                byte[] tempData = { IdentificationOption };
                SendMessage((byte)KWP2000ServiceID.ReadECUIdentification, tempData);
				DisplayStatusMessage("Reading ECU identification option: 0x" + IdentificationOption.ToString("X2"), StatusMessageType.LOG);

                result = true;
            }

            return result;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.ReadECUIdentificationPositiveResponse)
                {
                    if ((message.DataLength > 1) && (message.mData[0] == IdentificationOption))
                    {
                        //we don't copy the first byte, since that is the option ID and not the data
                        IdentificationData = new byte[message.mData.Length - 1];
                        Buffer.BlockCopy(message.mData, 1, IdentificationData, 0, message.mData.Length - 1);

						DisplayStatusMessage("Read ECU identification option: 0x" + IdentificationOption.ToString("X2"), StatusMessageType.LOG);

                        DisplayStatusMessage("Successfully read ECU identification information", StatusMessageType.LOG);
                        ActionCompleted(true);
                    }
                    else
                    {
                        DisplayStatusMessage("Failed to read ECU identification information. Received wrong response.", StatusMessageType.LOG);
                        ActionCompleted(false);
                    }

                    handled = true;
                }

                if (!handled)
                {
                    DisplayStatusMessage("Failed to read ECU identification information.", StatusMessageType.LOG);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        public byte IdentificationOption
        {
            get;
            private set;
        }

        public byte[] IdentificationData
        {
            get;
            private set;
        }
    };

    public class SecurityAccessAction : KWP2000Action
    {
		public class SecurityAccessSettings
		{
			public byte RequestSeed = SecurityAccessAction.DEFAULT_REQUEST_SEED;
			public bool SupportSpecialKey = false;
			public bool UseExtendedSeedRequest = false;
		}

        //external RAM requires 4 byte sendkey (service id, sendkey(2), 4 byte key)
        //internal ROM requires 4 byte sendkey (service id, sendkey(2), 4 byte key)

        //VAG flashing documentation only refers to using seed 0x01
        //2003 Audi A4 would not accept seed 0x7F, but did accept 0x01 and 0x03, but with 0x03 magic key did not work

        public const byte DEFAULT_REQUEST_SEED = 0x01;//0x7F doesn't work for 2003 Audi A4
        public const byte INTERNAL_ROM_REQUEST_SEED = 0x77;//0x77 to 0x7F will work
		public const byte EXTERNAL_RAM_REQUEST_SEED = 0x7F;

        //this does not appear to work with the 2003 A4
		public const byte EXTERNAL_RAM_SPECIAL_REQUEST_SEED = 0x03;//used when trying to login with (seed + 0x11223344)

		public const byte DEFAULT_NUM_LOOPS = 5;//5 should work for internal ROM and external RAM
		public const byte EXTERNAL_RAM_NUM_LOOPS = 5;

		public const Int16 DEFAULT_KEY_INDEX = 0x3F;//This key index appears to work for 2000 to 2003 VW/Audi

        public SecurityAccessAction(KWP2000Interface commInterface, SecurityAccessSettings settings)
            : base(commInterface)
        {
			Debug.Assert(settings != null);

			if (settings == null)
			{
				settings = new SecurityAccessSettings();
			}

			mSupportSpecialSeed = settings.SupportSpecialKey;
			mSendExtendedSeedRequest = settings.UseExtendedSeedRequest;

			mRequestSeed = settings.RequestSeed;
            Debug.Assert(mRequestSeed % 2 == 1, "Request seed must be odd");
			mRequestSeed += (byte)(1 - (mRequestSeed % 2));//if the seed is even, add one
            
			mSendKey = (byte)(mRequestSeed + 1);

            mRequestSeedNumLoops = DEFAULT_NUM_LOOPS;

            mExternalRAMKeyTable = new uint[64]{0x0A221289, 0x144890A1, 0x24212491, 0x290A0285, 0x42145091, 0x504822C1, 0x0A24C4C1, 0x14252229,
			        0x24250525, 0x2510A491, 0x28488863, 0x29148885, 0x422184A5, 0x49128521, 0x50844A85, 0x620CC211,
			        0x124452A9, 0x18932251, 0x2424A459, 0x29149521, 0x42352621, 0x4A512289, 0x52A48911, 0x11891475, 
			        0x22346523, 0x4A3118D1, 0x64497111, 0x0AE34529, 0x15398989, 0x22324A67, 0x2D12B489, 0x132A4A75,
			        0x19B13469, 0x25D2C453, 0x4949349B, 0x524E9259, 0x1964CA6B, 0x24F5249B, 0x28979175, 0x352A5959, 
			        0x3A391749, 0x51D44EA9, 0x564A4F25, 0x6AD52649, 0x76493925, 0x25DE52C9, 0x332E9333, 0x68D64997,
			        0x494947FB, 0x33749ACF, 0x5AD55B5D, 0x7F272A4F, 0x35BD5B75, 0x3F5AD55D, 0x5B5B6DAD, 0x6B5DAD6B, 
			        0x75B57AD5, 0x5DBAD56F, 0x6DBF6AAD, 0x75775EB5, 0x5AEDFED5, 0x6B5F7DD5, 0x6F757B6B, 0x5FBD5DBD};

            mInternalROMKeyTable = new uint[5] { 0x75775EB5, 0x5AEDFED5, 0x6B5F7DD5, 0x6F757B6B, 0x5FBD5DBD };

            mSecurityKeyIndex = DEFAULT_KEY_INDEX;

            //this block of code is not in the external RAM flashing code, but is in the internal ROM flashing code.
            //but it does not appear to work for anything except when using a seed of 0x3F and a key of 0x40
            //mSecurityKeyIndex = mSendKey;
            //mSecurityKeyIndex += 1;
            //mSecurityKeyIndex >>= 1;//ashr
            //mSecurityKeyIndex &= 0x00FF;
            //mSecurityKeyIndex -= 1;//to make it zero based
        }

        public override bool Start()
        {
            bool result = base.Start();

            if (result)
            {
				byte[] tempData = null;

				if (mSendExtendedSeedRequest)
				{
					//The internal ROM wants the num loops and word SECURITY in the message: service id, request seed, num loop, up to 14 bytes of ASCII data
					tempData = new byte[11];
					tempData[0] = mRequestSeed;
					tempData[1] = mRequestSeedNumLoops;
					tempData[2] = (byte)'S';
					tempData[3] = (byte)'E';
					tempData[4] = (byte)'C';
					tempData[5] = (byte)'U';
					tempData[6] = (byte)'R';
					tempData[7] = (byte)'I';
					tempData[8] = (byte)'T';
					tempData[9] = (byte)'Y';
					tempData[10] = 0;
				}
				else
				{
					//The external RAM expects 2 or more bytes in the message: service id, request seed, the rest are ignored
					tempData = new byte[1];
					tempData[0] = mRequestSeed;
				}
                
                SendMessage((byte)KWP2000ServiceID.SecurityAccess, tempData);

                DisplayStatusMessage("Requesting security access.", StatusMessageType.USER);

                result = true;
            }

            return result;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.SecurityAccess, message))
                {
                    byte accessMode = message.mData[0];

                    //check for response to our request seed first message
                    if (accessMode == mRequestSeed)
                    {
                        byte[] seedArray = new byte[message.mData.Length - 1];
                        Buffer.BlockCopy(message.mData, 1, seedArray, 0, message.mData.Length - 1);

                        bool isUnlocked = true;

                        //a response of all zeros means the ecu is already unlocked
                        foreach (byte currByte in seedArray)
                        {
                            if (currByte != 0)
                            {
                                isUnlocked = false;
                                break;
                            }
                        }

                        if (!isUnlocked)
                        {
                            byte[] sendKeyArray = null;

                            sendKeyArray = GenerateVAGExternalRAMSendKey(seedArray);

                            byte[] messageData = new byte[sendKeyArray.Length + 1];
                            messageData[0] = mSendKey;
                            Buffer.BlockCopy(sendKeyArray, 0, messageData, 1, sendKeyArray.Length);

                            SendMessage((byte)KWP2000ServiceID.SecurityAccess, messageData);

                            DisplayStatusMessage("Received security seed, sending security key.", StatusMessageType.LOG);
                        }
                        else
                        {
                            DisplayStatusMessage("Security access granted.", StatusMessageType.USER);
                            ActionCompleted(true);
                        }
                    }
                    //check for a response to our send key second message
                    else if (accessMode == mSendKey)
                    {
                        //we succeed if we get a response 0x34 as defined in the VAG flashing document
                        if (message.mData[1] == 0x34)
                        {
                            DisplayStatusMessage("Security access granted.", StatusMessageType.USER);
                            ActionCompleted(true);
                        }
                        else
                        {
                            DisplayStatusMessage("Security access denied due to invalid key.", StatusMessageType.USER);
                            ActionCompleted(false);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Received message with unknown access mode during security negotiation. Security negotiation failed.", StatusMessageType.USER);
                        ActionCompleted(false);
                    }

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.SecurityAccess, message))
                {
                    if (message.DataLength >= 2)
                    {
                        if (message.mData[1] == (byte)KWP2000ResponseCode.InvalidKey)
                        {
                            DisplayStatusMessage("Security access denied due to invalid key.", StatusMessageType.USER);                            
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.ServiceNotSupported)
                        {
                            DisplayStatusMessage("ECU reports that security access is not supported.", StatusMessageType.USER);
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                        {
                            DisplayStatusMessage("ECU reports that security access request was rejected.", StatusMessageType.USER);
                        }
                        else if (message.mData[1] == (byte)KWP2000ResponseCode.RequiredTimeDelayNotExpired)
                        {
                            DisplayStatusMessage("ECU reports that security access is not allowed until required lockout time has elapsed.", StatusMessageType.USER);
                        }
                        else
                        {
                            DisplayStatusMessage("Security access denied due to unknown reason.", StatusMessageType.USER);
                        }

                        handled = true;
                        ActionCompleted(false);
                    }
                }

                if (!handled)
                {
                    DisplayStatusMessage("Received unknown message during security negotiation. Security negotiation failed.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected byte[] GenerateVAGExternalRAMSendKey(byte[] seedArray)
        {
            //I verified this algorithm in the assembly code, many times

            uint seed = (uint)(seedArray[0] << 24) + (uint)(seedArray[1] << 16) + (uint)(seedArray[2] << 8) + (uint)seedArray[3];
            uint key = 0;

            if (mSupportSpecialSeed && (mRequestSeed == EXTERNAL_RAM_SPECIAL_REQUEST_SEED))
            {
                key = seed + 0x11223344;
            }
            else
            {
                key = seed;

                Debug.Assert(mSecurityKeyIndex < mExternalRAMKeyTable.Length);
                Int16 securityIndex = (byte)Math.Min(mSecurityKeyIndex, mExternalRAMKeyTable.Length - 1);

                Debug.Assert(mRequestSeedNumLoops == EXTERNAL_RAM_NUM_LOOPS);

                for (uint x = 0; x < mRequestSeedNumLoops; x++)
                {
                    if (key >= 0x80000000)
                    {
                        key <<= 1;//in the ECU this is written as: key += key
                        key |= 0x00000001;
                        key ^= mExternalRAMKeyTable[securityIndex];
                    }
                    else
                    {
                        key <<= 1;//in the ECU this is written as: key += key
                    }
                }
            }

            byte[] sendKey = new byte[4];
            sendKey[0] = (byte)((key >> 24) & 0xFF);
            sendKey[1] = (byte)((key >> 16) & 0xFF);
            sendKey[2] = (byte)((key >> 8) & 0xFF);
            sendKey[3] = (byte)((key) & 0xFF);

            return sendKey;
        }
        /*
		protected byte[] GenerateInternetExampleSendKey(byte[] seedArray)
		{
			Debug.Assert(seedArray.Length == 4, "Unexpected length of seed");

            Debug.Assert(mSecurityKeyIndex < mExternalRAMKeyTable.Length);
            byte securityKeyIndex = (byte)Math.Min(mSecurityKeyIndex, mExternalRAMKeyTable.Length - 1);
            
			uint seed = (uint)(seedArray[0] << 24) + (uint)(seedArray[1] << 16) + (uint)(seedArray[2] << 8) + (uint)seedArray[3];

			for (int i = 0; i < mRequestSeedNumLoops; i++)
			{
				if ((seed & 0x80000000) == 0)
				{
					seed = (mExternalRAMKeyTable[securityKeyIndex]) ^ (seed << 1);
				}
				else
				{
					seed <<= 1;
				}
			}

			byte[] sendKey = new byte[4];

			sendKey[0] = (byte)((seed >> 24) & 0xFF);
			sendKey[1] = (byte)((seed >> 16) & 0xFF);
			sendKey[2] = (byte)((seed >> 8) & 0xFF);
			sendKey[3] = (byte)((seed) & 0xFF);
						
			return sendKey;
		}
        */

        //sendkey value from 0x77 to 0x80 would work with the security table, other numbers would go to an algorithm
        protected byte[] GenerateVAGInternalROMSendKey(byte[] seedArray)
        {
            uint seed = (uint)(seedArray[0] << 24) + (uint)(seedArray[1] << 16) + (uint)(seedArray[2] << 8) + (uint)seedArray[3];

            Int16 securityIndex = mSendKey;
            securityIndex += 1;
            securityIndex >>= 1;//ashr
            securityIndex &= 0x00FF;

            //clamping limits on a signed byte after shr?
            if ((securityIndex < 0x3C) || (securityIndex > 0x40))
            {
                securityIndex = 0x41;
            }

            securityIndex -= 0x3C;

            byte numLoops = (byte)(mRequestSeedNumLoops + 0x23);

            //if we rolled over to negative, then set to max
            if (numLoops < 0x23)
            {
                numLoops = 0xFF;
            }

            uint key = seed;

            for (uint x = 0; x < numLoops; x++)
            {
                if (key >= 0x80000000)
                {

                    //there is some logic missing here...
                    Debug.Assert(securityIndex < mInternalROMKeyTable.Length, "Invalid index for internal ROM table");
                    securityIndex = (byte)Math.Min(securityIndex, mInternalROMKeyTable.Length - 1);


                    key <<= 1;
                    key ^= mInternalROMKeyTable[securityIndex];
                }
                else
                {
                    key <<= 1;
                }
            }

            byte[] sendKey = new byte[4];

            sendKey[0] = (byte)((key >> 24) & 0xFF);
            sendKey[1] = (byte)((key >> 16) & 0xFF);
            sendKey[2] = (byte)((key >> 8) & 0xFF);
            sendKey[3] = (byte)((key) & 0xFF);

            return sendKey;
        }

		protected bool mSupportSpecialSeed;
		protected bool mSendExtendedSeedRequest;

        protected byte mRequestSeed;
        protected byte mRequestSeedNumLoops;
        protected byte mSendKey;
        protected Int16 mSecurityKeyIndex;
        protected uint[] mExternalRAMKeyTable;
        protected uint[] mInternalROMKeyTable;
    };

    public abstract class RequestUploadDownloadToECUActionBase : KWP2000Action
    {
        public RequestUploadDownloadToECUActionBase(KWP2000Interface commInterface, uint address, uint size, TransferDataAction.CompressionType compression, TransferDataAction.EncryptionType encryption)
            : base(commInterface)
        {
            Debug.Assert(address % 2 == 0, "address is not an even address");
            Debug.Assert(size % 2 == 0, "size is not an even amount");
            Debug.Assert((address & 0xFF000000) == 0, "address is too large");
            Debug.Assert((size & 0xFF000000) == 0, "size is too large");

            mAddress = address;
            mSize = size;
            mFormat = TransferDataAction.GetDataFormatByte(compression, encryption);
			mOperationType = (byte)KWP2000ServiceID.NegativeResponse;

            WasFailureCausedByPreviousIncompleteDownload = false;
        }

        protected void SetOperationToDownload()
        {
			mOperationType = (byte)KWP2000ServiceID.RequestDownload;
        }

        protected void SetOperationToUpload()
        {
            mOperationType = (byte)KWP2000ServiceID.RequestUpload;
        }

        public override bool Start()
        {
            WasFailureCausedByPreviousIncompleteDownload = false;

            bool started = false;

            if (base.Start())
            {
				if ((mOperationType == (byte)KWP2000ServiceID.RequestDownload) || (mOperationType == (byte)KWP2000ServiceID.RequestUpload))
                {   
                    uint endAddress = mAddress + mSize - 1;

					if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                    {
                        KWP2000MessageHelpers.SendRequestDownloadMessage(KWP2000CommInterface, mAddress, mSize, mFormat);

                        DisplayStatusMessage("Requesting download to ECU for address range 0x" + mAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8") + ".", StatusMessageType.USER);
                    }
					else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                    {
                        KWP2000MessageHelpers.SendRequestUploadMessage(KWP2000CommInterface, mAddress, mSize, mFormat);

                        DisplayStatusMessage("Requesting upload from ECU for address range 0x" + mAddress.ToString("X8") + " to 0x" + endAddress.ToString("X8") + ".", StatusMessageType.USER);
                    }

                    started = true;
                }
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if(KWP2000Interface.IsResponseToRequest(mOperationType, message))
                {
                    if (KWP2000Interface.IsPositiveResponseToRequest(mOperationType, message))
                    {
                        if (message.DataLength >= 1)
                        {
                            //TODO: KWP2000 spec says the 1 byte of data indicates a different use than max block size

                            mMaxBlockSize = message.mData[0];
                            DisplayStatusMessage("ECU reports max block size is: " + mMaxBlockSize, StatusMessageType.DEV);

							if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                            {
                                DisplayStatusMessage("Request download to ECU succeeded.", StatusMessageType.USER);
                            }
							else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                            {
                                DisplayStatusMessage("Request upload from ECU succeeded.", StatusMessageType.USER);
                            }

                            ActionCompleted(true);
                        }
                        else
                        {
							if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                            {
                                DisplayStatusMessage("Request download to ECU failed, received wrong message data size.", StatusMessageType.USER);
                            }
							else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                            {
                                DisplayStatusMessage("Request upload from ECU failed, received wrong message data size.", StatusMessageType.USER);
                            }

                            ActionCompleted(false);
                        }

                        handled = true;
                    }
                    else//must be a negative response to our request
                    {
                        if(message.DataLength >= 2)
                        {
                            if((message.mData[1] == (byte)KWP2000ResponseCode.ServiceNotSupported) || (message.mData[1] == (byte)KWP2000ResponseCode.NoProgram))
                            {
								if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Request download to ECU failed, ECU reports service is not supported.", StatusMessageType.USER);
                                }
								else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Request upload from ECU failed, ECU reports service is not supported. Request upload may have been disabled by aftermarket engine software.", StatusMessageType.USER);
                                }

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.GeneralReject)
                            {
								if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Request download to ECU failed, ECU reports RequestDownload was rejected.", StatusMessageType.USER);
                                }
								else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Request upload from ECU failed, ECU reports RequestUpload was rejected. RequestUpload may have been disabled by aftermarket engine software.", StatusMessageType.USER);
                                }

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.SecurityAccessDenied_SecurityAccessRequested)
                            {
								if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Request download to ECU failed, ECU reports security access has not been granted.", StatusMessageType.USER);
                                }
								else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Request upload from ECU failed, ECU reports security access has not been granted.", StatusMessageType.USER);
                                }

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.ConditionsNotCorrectOrRequestSequenceError)
                            {
								if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    DisplayStatusMessage("Request download to ECU failed, ECU reports conditions not correct or sequence error. Please turn off the ignition and retry.", StatusMessageType.USER);
                                }
								else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Request upload from ECU failed, ECU reports conditions not correct or sequence error. Please turn off the ignition and retry.", StatusMessageType.USER);
                                }

                                handled = true;
                                ActionCompleted(false);
                            }
                            else if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                            {
								if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                                {
                                    WasFailureCausedByPreviousIncompleteDownload = true;
                                    DisplayStatusMessage("Request download to ECU failed, ECU reports routine not complete or service in progress.", StatusMessageType.USER);
                                }
								else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                                {
                                    DisplayStatusMessage("Request upload from ECU failed, ECU reports routine not complete or service in progress. Please turn off the ignition and retry.", StatusMessageType.USER);
                                }

                                handled = true;
                                ActionCompleted(false);                                
                            }
                        }
                    }
                }

                if (!handled)
                {
					if (mOperationType == (byte)KWP2000ServiceID.RequestDownload)
                    {
                        DisplayStatusMessage("Request download to ECU failed.", StatusMessageType.USER);
                    }
					else if (mOperationType == (byte)KWP2000ServiceID.RequestUpload)
                    {
                        DisplayStatusMessage("Request upload from ECU failed.", StatusMessageType.USER);
                    }

                    ActionCompleted(false);
                }
            }

            return handled;
        }

        public bool WasFailureCausedByPreviousIncompleteDownload { get; private set; }

        public byte GetMaxBlockSize()
        {
            return mMaxBlockSize;
        }

        private byte mMaxBlockSize;
        private uint mAddress;
        private uint mSize;
        private byte mFormat;
        private byte mOperationType;
    };

    public class RequestDownloadToECUAction : RequestUploadDownloadToECUActionBase
    {
        public RequestDownloadToECUAction(KWP2000Interface commInterface, uint address, uint size, TransferDataAction.CompressionType compression, TransferDataAction.EncryptionType encryption)
            : base(commInterface, address, size, compression, encryption)
        {
            SetOperationToDownload();
        }
    };

    public class RequestUploadFromECUAction : RequestUploadDownloadToECUActionBase
    {
        public RequestUploadFromECUAction(KWP2000Interface commInterface, uint address, uint size, TransferDataAction.CompressionType compression, TransferDataAction.EncryptionType encryption)
            : base(commInterface, address, size, compression, encryption)
        {
            SetOperationToUpload();
        }
    };

    public class TransferDataAction : KWP2000Action
    {
        public delegate void NotifyBytesWrittenDelegate(uint newAmountWritten, uint totalWritten, uint totalToWrite);

        //high nibble of data format
        [Flags]
        public enum CompressionType : byte
        {
            Uncompressed = 0x00,
            Bosch = 0x10,
            Hitachi = 0x20,
            Marelli = 0x30,
            Lucas = 0x40
        }

        //low nibble of data format
        [Flags]
        public enum EncryptionType : byte
        {
            Unencrypted = 0x00,
            Bosch = 0x01,
            Hitachi = 0x02,
            Marelli = 0x03,
            Lucas = 0x04
        }

        public const byte DEFAULT_MAX_BLOCK_SIZE = 124;//safe default I think, 128 - 4
        private const int ROUTINE_NOT_COMPLETE_MAX_NUM_RETRIES = 5;

        public enum TransferMode
        {
            UploadFromECU,
            DownloadToECU
        }

        //only the first two bits get used
        private enum RepeatMode : byte
        {
            NO_REPEATS = 0x00,
            REPEATING = 0x01,
            REPEATING_ALSO = 0x02,
            SOME_WACKY_MODE = 0x03
        }

        public TransferDataAction(KWP2000Interface commInterface, TransferMode mode, EncryptionType encryption, CompressionType compression, byte maxBlockSize, byte[] data, NotifyBytesWrittenDelegate notifier)
            : base(commInterface)
        {
            if (notifier != null)
            {
                mBytesWrittenHandler += notifier;
            }

            mMaxBlockSize = (byte)(maxBlockSize - 1);//minus one because we aren't counting the service ID
            RawData = data;
            mDataFormat = GetDataFormatByte(compression, encryption);
            mTransferMode = mode;

            mDataIndex = 0;
            mCurrentMessageNumBytes = 0;
            ResetEncryptionState();

            ShouldResumeTransfer = false;
        }

        public byte[] RawData { get; private set;}

        public static byte GetDataFormatByte(CompressionType compression, EncryptionType encryption)
        {
            byte dataFormat = (byte)( (((byte)compression) & 0xF0) | (((byte)encryption) & 0x0F) );

            Debug.Assert(GetCompressionType(dataFormat) == compression);
            Debug.Assert(GetEncryptionType(dataFormat) == encryption);

            return dataFormat;
        }

        public static CompressionType GetCompressionType(byte dataFormat)
        {
            return (CompressionType)(dataFormat & 0xF0);
        }

        public static EncryptionType GetEncryptionType(byte dataFormat)
        {
            return (EncryptionType)(dataFormat & 0x0F);
        }

        protected void ResetEncryptionState()
        {
            mEncryptionIndex = 0;
            mNextEncryptionIndex = mEncryptionIndex;
            mBoschCompressionMode = 1;//1 is compressed, 6 is uncompressed
        }

        public bool ShouldResumeTransfer { get; set; }//set when we want to resume the current transfer instead of starting from the beginning

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                if (ShouldResumeTransfer)
                {
                    ShouldResumeTransfer = false;
                    DisplayStatusMessage("Resuming data transfer.", StatusMessageType.USER);
                }
                else
                {
                    mDataIndex = 0;
                    mCurrentMessageNumBytes = 0;
                    ResetEncryptionState();
                    DisplayStatusMessage("Starting data transfer.", StatusMessageType.USER);
                }

                started = SendNextMessage();
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.TransferData, message))
                {
                    if (mTransferMode == TransferMode.UploadFromECU)
                    {
                        HandleNextUploadMessageData(message.mData, out mCurrentMessageNumBytes);
#if DEBUG
                        DisplayStatusMessage("Received 0x" + mCurrentMessageNumBytes.ToString("X") + " bytes. 0x" + (mDataIndex + mCurrentMessageNumBytes).ToString("X") + " of 0x" + RawData.LongLength.ToString("X") + " received.", StatusMessageType.DEV);
#endif
                        if(mCurrentMessageNumBytes > 0)
                        {
                            HandlePositiveResponseMessage();

                            handled = true;
                        }
                        else
                        {
                            DisplayStatusMessage("Transfer data message did not contain any data.", StatusMessageType.LOG);
                        }
                    }
                    else if (mTransferMode == TransferMode.DownloadToECU)
                    {
#if DEBUG
                        DisplayStatusMessage("Sent 0x" + mCurrentMessageNumBytes.ToString("X") + " bytes. 0x" + (mDataIndex + mCurrentMessageNumBytes).ToString("X") + " of 0x" + RawData.LongLength.ToString("X") + " sent.", StatusMessageType.DEV);
#endif
                        HandlePositiveResponseMessage();

                        handled = true;
                    }                    
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.TransferData, message))
                {
                    if (message.DataLength >= 2)
                    {
                        if (message.mData[1] == (byte)KWP2000ResponseCode.RoutineNotCompleteOrServiceInProgress)
                        {
                            if (mRoutineNotCompleteRetryCount < ROUTINE_NOT_COMPLETE_MAX_NUM_RETRIES)
                            {
                                DisplayStatusMessage("Retrying transfer data message because ECU was not ready.", StatusMessageType.LOG);

                                mRoutineNotCompleteRetryCount++;

                                if (!SendNextMessage())
                                {
                                    DisplayStatusMessage("Data transfer complete.", StatusMessageType.USER);
                                    ActionCompleted(true);
                                }

                                handled = true;
                            }
                        }
                    }
                }

                if (!handled)
                {
                    DisplayStatusMessage("Data transfer failed.", StatusMessageType.USER);
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected void HandlePositiveResponseMessage()
        {
            LogProfile("Start HandlePositiveResponseMessage");

            mDataIndex += mCurrentMessageNumBytes;
            mEncryptionIndex = mNextEncryptionIndex;

            mRoutineNotCompleteRetryCount = 0;

            uint oldCurrentMessageNumBytes = mCurrentMessageNumBytes;
            uint oldDataIndex = mDataIndex;

            bool sentAnotherMessage = SendNextMessage();

            if (mBytesWrittenHandler != null)
            {
                mBytesWrittenHandler(oldCurrentMessageNumBytes, oldDataIndex, (uint)RawData.LongLength);
            }

            if (!sentAnotherMessage)
            {
                DisplayStatusMessage("Data transfer complete.", StatusMessageType.USER);
                ActionCompleted(true);
            }

            LogProfile("End HandlePositiveResponseMessage");
        }

        protected bool AnyDataLeft()
        {
            if (RawData != null)
            {
                return mDataIndex < RawData.LongLength;
            }

            return false;
        }

        protected void HandleNextUploadMessageData(byte[] data, out uint uncompressedSize)
        {
            LogProfile("Start HandleNextUploadMessageData");

            uncompressedSize = 0;

            if (data != null)
            {
                mNextEncryptionIndex = mEncryptionIndex;
                DecryptEncryptByteBuffer(mDataFormat, data, 0, (uint)data.LongLength, ref mNextEncryptionIndex);

                CompressionType compression = GetCompressionType(mDataFormat);

                switch (compression)
                {
                    case CompressionType.Uncompressed:
                    {
                        if (data.LongLength > 0)
                        {
                            Debug.Assert(mDataIndex + data.LongLength <= RawData.LongLength);
                            data.CopyTo(RawData, mDataIndex);//using array copy to handle 64 bit lengths
						}

                        uncompressedSize = (uint)data.Length;

                        break;
                    }
                    default:
                    {
                        Debug.Fail("Unsupported compression type");
                        break;
                    }
                }
            }

            LogProfile("End HandleNextUploadMessageData");
        }

        protected bool SendNextMessage()
        {
            LogProfile("Start SendNextMessage");

            bool sent = false;

            if (AnyDataLeft())
            {
                if (mTransferMode == TransferMode.DownloadToECU)
                {
                    byte[] messageData = GetNextDownloadMessageData();

                    if (messageData != null)
                    {
                        SendMessage((byte)KWP2000ServiceID.TransferData, messageData);
                        sent = true;
                    }
                }
                else
                {
                    SendMessage((byte)KWP2000ServiceID.TransferData);
                    sent = true;
                }
            }

            LogProfile("End SendNextMessage");

            return sent;
        }

        protected byte[] GetNextDownloadMessageData()
        {
            LogProfile("Start GetNextDownloadMessageData");

            byte[] messageData = null;

            mCurrentMessageNumBytes = 0;
            mNextEncryptionIndex = mEncryptionIndex;

            CompressionType compression = GetCompressionType(mDataFormat);

            switch (compression)
            {
                case CompressionType.Uncompressed:
                {
                    messageData = GetNextDownloadMessageDataWithUncompressedMode(out mCurrentMessageNumBytes, ref mNextEncryptionIndex);
                    break;
                }
                case CompressionType.Bosch:
                {
                    messageData = GetNextDownloadMessageDataWithBoschMode(out mCurrentMessageNumBytes, ref mNextEncryptionIndex);
                    break;
                }
                default:
                {
                    Debug.Fail("Unsupported compression type");
                    break;
                }
            }

            LogProfile("End GetNextDownloadMessageData");

            return messageData;
        }

        protected byte[] GetNextDownloadMessageDataWithUncompressedMode(out uint uncompressedSize, ref uint encryptionIndex)
        {
            LogProfile("Start GetNextDownloadMessageDataWithUncompressedMode");

            byte[] encryptedData = null;
            uncompressedSize = Math.Min(mMaxBlockSize, (uint)RawData.LongLength - mDataIndex);

            if (uncompressedSize > 0)
            {
                encryptedData = new byte[uncompressedSize];                
                Array.Copy(RawData, mDataIndex, encryptedData, 0, uncompressedSize);//using array copy to handle 64bit indexes
                DecryptEncryptByteBuffer(mDataFormat, encryptedData, 0, (uint)encryptedData.LongLength, ref encryptionIndex);
            }

            LogProfile("End GetNextDownloadMessageDataWithUncompressedMode");

            return encryptedData;
        }

        protected byte[] GetNextDownloadMessageDataWithBoschMode(out uint uncompressedSize, ref uint encryptionIndex)
        {
            LogProfile("Start GetNextDownloadMessageDataWithBoschMode");

            bool isFirstDataMessage = (mDataIndex == 0);

            byte maxDataLength = (byte)mMaxBlockSize;

            if (isFirstDataMessage)
            {
                maxDataLength -= 2;//0x1A, 0x01
            }

            Debug.Assert(maxDataLength > 0, "max block size is too small");
                        
            uint newTransmitIndex = mDataIndex;
            byte remainingDataLength = maxDataLength;

            var compressedDataBlocks = new List<byte[]>();
            uint compressedDataSize = 0;

            while ((remainingDataLength > 4) && (newTransmitIndex < RawData.LongLength))
            {
                var newCompressedData = GetNextCompressedBlockWithBoschMode(remainingDataLength, ref newTransmitIndex, RawData);

                Debug.Assert(newCompressedData.Length <= 255);//must fit in byte

                remainingDataLength -= (byte)newCompressedData.Length;
                compressedDataSize += (uint)newCompressedData.Length;

                compressedDataBlocks.Add(newCompressedData);
            }

            byte[] compressedData = null;
            uint startOffset = 0;

            if (compressedDataSize > 0)
            {
                Debug.Assert(compressedDataSize <= maxDataLength);
                
                //the first message must start with 0x1A, 0x01
                if (isFirstDataMessage)
                {
                    startOffset = 2;
                }

                compressedData = new byte[compressedDataSize + startOffset];

                //the first message must start with 0x1A, 0x01
                if (isFirstDataMessage)
                {
                    compressedData[0] = 0x1A;
                    compressedData[1] = 0x01;
                }

                long curIndex = startOffset;
                foreach (var compressedBlock in compressedDataBlocks)
                {
                    compressedBlock.CopyTo(compressedData, curIndex);//array copy used to handle 64bit values just in case
                    curIndex += compressedBlock.LongLength;
                }
            }

            Debug.Assert(compressedData != null);

            uncompressedSize = newTransmitIndex - mDataIndex;

            DecryptEncryptByteBuffer(mDataFormat, compressedData, startOffset, (uint)compressedData.LongLength - startOffset, ref encryptionIndex);

            LogProfile("Finish GetNextDownloadMessageDataWithBoschMode");

            return compressedData;
        }

        protected static byte[] GetNextCompressedBlockWithBoschMode(byte maxBlockSize, ref uint currentDataIndex, byte[] data)
        {
            Debug.Assert(data != null, "message data is null");
            Debug.Assert(data.LongLength > 0, "message data is empty");
            Debug.Assert(currentDataIndex % 2 == 0, "must be even");

            //const uint MAX_NUM_BYTE_REPEATS = 0x3FFF;            
            const uint MAX_NUM_BYTE_REPEATS = 0x1000;//0x1000 seems to work, but 0x2000 doesn't, no idea why...
            const uint NUM_REPEATING_BYTES_FOR_RLE = 4;
            const uint NUM_HEADER_BYTES = 2;

            byte[] encryptedData = null;

            if (currentDataIndex < data.LongLength)
            {
                uint maxNumDataBytes = Math.Min(maxBlockSize - NUM_HEADER_BYTES, ((uint)data.LongLength) - currentDataIndex);
                uint maxIndexForNoRepeats = Math.Min(currentDataIndex + maxBlockSize - NUM_HEADER_BYTES, ((uint)data.LongLength));

                uint repeatStartIndex = 0;
                uint repeatEndIndex = 0;
                bool foundRepeat = false;

                //does the data start with a repeat?
                if (maxIndexForNoRepeats > currentDataIndex + 1)
                {
                    for (uint x = currentDataIndex; x < maxIndexForNoRepeats - 1; x += 2)
                    {
                        if (data[x] == data[x + 1])
                        {
                            repeatStartIndex = x;

                            uint maxIndexForRepeats = Math.Min(repeatStartIndex + MAX_NUM_BYTE_REPEATS, (uint)data.LongLength);

                            for (uint y = repeatStartIndex + 1; y < maxIndexForRepeats; y++)
                            {
                                if (data[repeatStartIndex] == data[y])//are they equal
                                {
                                    if (y % 2 == 1)//are we at the end of a word?
                                    {
                                        if (foundRepeat || (y - repeatStartIndex + 1 >= NUM_REPEATING_BYTES_FOR_RLE))
                                        {
                                            repeatEndIndex = y;
                                            foundRepeat = true;
                                        }
                                    }
                                }
                                else//they are not equal
                                {
                                    break;
                                }
                            }

                            if (foundRepeat)
                            {
                                break;
                            }
                        }
                    }
                }

                if (foundRepeat && (repeatStartIndex == currentDataIndex))
                {
                    UInt16 numRepeatedBytes = (UInt16)(repeatEndIndex - repeatStartIndex + 1);
                    Debug.Assert(numRepeatedBytes % 2 == 0, "must have even number of bytes");
                    Debug.Assert(numRepeatedBytes <= MAX_NUM_BYTE_REPEATS, "too many repeats");

                    if (numRepeatedBytes > 0)
                    {   
                        UInt16 repeatMode = (UInt16)RepeatMode.REPEATING;
                        UInt16 secondWord = (UInt16)(((repeatMode & 0x03) << 14) | (0x3FFF & numRepeatedBytes));//repeats, num bytes

                        Debug.Assert((repeatMode & ~0x03) == 0x00, "invalid repeat mode");
                        Debug.Assert((numRepeatedBytes & ~0x3FFF) == 0x00, "invalid number of repeats");

                        encryptedData = new byte[1 + NUM_HEADER_BYTES];
                        encryptedData[0] = (byte)((secondWord >> 8) & 0xFF);
                        encryptedData[1] = (byte)(secondWord & 0xFF);
                        encryptedData[2] = data[repeatStartIndex];

                        currentDataIndex += numRepeatedBytes;
                    }
                }
                else
                {
                    UInt16 numDataBytes = (UInt16)(maxNumDataBytes - (maxNumDataBytes % 2));//force it to be even

                    if (foundRepeat)
                    {
                        numDataBytes = (UInt16)(repeatStartIndex - currentDataIndex);
                        Debug.Assert(numDataBytes % 2 == 0, "must have even number of bytes");
                    }

                    if (numDataBytes > 0)
                    {
                        UInt16 repeatMode = (UInt16)RepeatMode.NO_REPEATS;
                        UInt16 secondWord = (UInt16)(((repeatMode & 0x03) << 14) | (0x3FFF & numDataBytes));//no repeats, num bytes

                        Debug.Assert((repeatMode & ~0x03) == 0x00, "invalid repeat mode");
                        Debug.Assert((numDataBytes & ~0x3FFF) == 0x00, "invalid number of bytes");

                        encryptedData = new byte[numDataBytes + NUM_HEADER_BYTES];
                        encryptedData[0] = (byte)((secondWord >> 8) & 0xFF);
                        encryptedData[1] = (byte)(secondWord & 0xFF);
                        Array.Copy(data, currentDataIndex, encryptedData, 2, numDataBytes);//array copy used to handle 64bit values just in case

                        currentDataIndex += numDataBytes;
                    }
                }
            }

            return encryptedData;
        }

        protected static byte[] DecompressMessageWithBoschMode(byte dataFormat, byte[] messageData, ref uint encryptionIndex, ref ushort boschCompressionMode)
        {
            byte[] decryptedMessage = null;

            UInt16 numBytesToFlashHigh = 0;
            UInt16 numBytesToFlashLow = 0;
            UInt16 needToReadLowByteOfWordToFlash = 1;

            bool writeEraseFailed = false;
            byte twoHighDecryptedBits = 0;
            UInt16 numBytesLeftToDecompress = (UInt16)messageData.Length;
            UInt16 currentIndex = 0;

            UInt16 realData = 0;

            do
            {
                UInt16 workingCompressionMode = (byte)(boschCompressionMode - 1);

                switch (workingCompressionMode)
                {
                    case 0:
                    {
                        if (messageData[currentIndex] == 0x1A)
                        {
                            boschCompressionMode = 2;
                        }

                        break;
                    }
                    case 1:
                    {
                        if (messageData[currentIndex] != 0x01)
                        {
                            writeEraseFailed = true;
                        }

                        boschCompressionMode = 3;

                        break;
                    }
                    case 2:
                    {
                        byte decryptedData = DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);
                        twoHighDecryptedBits = (byte)(0xC0 & decryptedData);
                        numBytesToFlashLow = (byte)(decryptedData & 0x3F);
                        numBytesToFlashLow <<= 8;//set bits 8-13
                        numBytesToFlashHigh = (byte)(((Int16)numBytesToFlashLow) >> 15);//use the sign bit

                        boschCompressionMode = 4;

                        if (twoHighDecryptedBits == 0xC0)//(RepeatMode.SOME_WACKY_MODE << 6)
                        {
                            boschCompressionMode = 5;
                            numBytesToFlashLow = 4;
                            numBytesToFlashHigh = 0;
                        }

                        break;
                    }
                    case 3:
                    {
                        byte decryptedData = DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);

                        //handle a carry from the low word
                        if (numBytesToFlashLow + decryptedData < numBytesToFlashLow)
                        {
                            numBytesToFlashHigh++;
                        }

                        numBytesToFlashLow += decryptedData;//get bits 0 to 7 of the num words to flash low

                        boschCompressionMode = 6;
                        break;
                    }
                    case 4:
                    {
                        if (numBytesToFlashHigh == 0)
                        {
                            if (numBytesToFlashLow == 4)
                            {
                                DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);
                            }
                            else if (numBytesToFlashLow == 3)
                            {
                                DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);
                            }
                            else if (numBytesToFlashLow == 2)
                            {
                                DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);
                            }
                            else if (numBytesToFlashLow == 1)
                            {
                                DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);
                                boschCompressionMode = 3;
                            }
                        }

                        break;
                    }
                    case 5:
                    {
                        UInt16 decryptedData = DecryptEncryptByte(dataFormat, messageData[currentIndex], ref encryptionIndex);

                        do
                        {
                            if (needToReadLowByteOfWordToFlash != 0)
                            {
                                realData = decryptedData;
                                needToReadLowByteOfWordToFlash = 0;
                            }
                            else
                            {
                                realData += (UInt16)((decryptedData << 8) & 0xFF00);
                                needToReadLowByteOfWordToFlash = 1;

                                if (decryptedMessage == null)
                                {
                                    decryptedMessage = new byte[2];
                                }
                                else
                                {
                                    //add two bytes to the end of the array
                                    byte[] oldArray = decryptedMessage;
                                    decryptedMessage = new byte[oldArray.LongLength + 2];
                                    oldArray.CopyTo(decryptedMessage, 0);//array copy used to handle 64bit values just in case
                                }

                                decryptedMessage[decryptedMessage.LongLength - 1] = (byte)((realData >> 8) & 0xFF);
                                decryptedMessage[decryptedMessage.LongLength - 2] = (byte)(realData & 0xFF);
                            }

                            if (numBytesToFlashLow == 0)
                            {
                                numBytesToFlashHigh--;
                            }

                            numBytesToFlashLow--;

                            if ((numBytesToFlashHigh == 0) && (numBytesToFlashLow == 0))
                            {
                                twoHighDecryptedBits = 0;
                                boschCompressionMode = 3;
                            }

                        } while (twoHighDecryptedBits != 0);

                        break;
                    }
                }

                currentIndex++;
                numBytesLeftToDecompress--;
            } while (numBytesLeftToDecompress > 0);

            Debug.Assert(!writeEraseFailed, "failed");

            return decryptedMessage;
        }

        protected static void DecryptEncryptByteBuffer(byte dataFormat, byte[] buffer, uint offset, uint num, ref uint encryptionIndex)
        {
            EncryptionType encryption = GetEncryptionType(dataFormat);

            if (encryption != EncryptionType.Unencrypted)
            {
                for (uint x = offset; x < (offset + num); x++)
                {
                    buffer[x] = DecryptEncryptByte(dataFormat, buffer[x], ref encryptionIndex);
                }
            }
        }

        protected static byte DecryptEncryptByte(byte dataFormat, byte data, ref uint encryptionIndex)
        {
            EncryptionType encryption = GetEncryptionType(dataFormat);
            byte result = data;

            switch (encryption)
            {
                case EncryptionType.Unencrypted:
                {
                    result = data;
                    break;
                }
                case EncryptionType.Bosch:
                {
                    result = DecryptEncryptByteBoschMode(data, ref encryptionIndex);
                    break;
                }
                default:
                {
                    Debug.Fail("Unsupported encryption type");
                    break;
                }
            }

            return result;
        }

        protected static byte DecryptEncryptByteBoschMode(byte data, ref uint encryptionIndex)
        {
            byte[] encryptionKeys = { 0x47, 0x45, 0x48, 0x45, 0x49, 0x4D };

            Debug.Assert(encryptionIndex < encryptionKeys.Length, "encryption index is invalid");

            byte decryptedData = (byte)(data ^ encryptionKeys[encryptionIndex]);

            encryptionIndex++;
            encryptionIndex %= (uint)encryptionKeys.Length;

            return decryptedData;
        }

        [Conditional("PROFILE_TRANSFER_DATA")]
        protected void LogProfile(string logMessage)
        {
            DisplayStatusMessage(logMessage, StatusMessageType.LOG);
        }

        protected TransferMode mTransferMode;

        protected ushort mBoschCompressionMode;
        protected uint mEncryptionIndex;
        protected uint mNextEncryptionIndex;
        
        protected byte mMaxBlockSize;
        protected byte mDataFormat;
        protected ushort mRoutineNotCompleteRetryCount;

        protected NotifyBytesWrittenDelegate mBytesWrittenHandler;

        protected uint mDataIndex;
        protected uint mCurrentMessageNumBytes;        
    };

    public class ReadDiagnosticTroubleCodesByStatusAction : KWP2000Action
    {
        public ReadDiagnosticTroubleCodesByStatusAction(KWP2000Interface commInterface, byte status, ushort group)
            : base(commInterface)
        {
            mStatus = status;
            mGroup = group;
            mDTCs = new Queue<KWP2000DTCInfo>();
        }

        public byte ExpectedNumDTCs { get; private set; }

        public IEnumerable<KWP2000DTCInfo> DTCsRead
        {
            get { return mDTCs; }
        }

        public override bool Start()
        {
            ExpectedNumDTCs = 0;
            mDTCs.Clear();

            bool started = false;

            if (base.Start())
            {
                byte[] messageData = new byte[3];
                messageData[0] = mStatus;
                messageData[1] = (byte)(mGroup >> 8);
                messageData[2] = (byte)(mGroup & 0xFF);
                SendMessage((byte)KWP2000ServiceID.ReadDiagnosticTroubleCodesByStatus, messageData);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (message.mServiceID == (byte)KWP2000ServiceID.ReadDiagnosticTroubleCodesByStatusPositiveResponse)
                {
                    if (message.DataLength > 0)
                    {
                        int startIndex = 0;

                        if (ExpectedNumDTCs == 0)
                        {
                            ExpectedNumDTCs = message.mData[startIndex];
                            startIndex++;
                        }

                        if (ExpectedNumDTCs > 0)
                        {
                            const int dtcDataSize = 3;

                            for (int curIndex = startIndex; (curIndex + dtcDataSize) <= message.mData.Length; curIndex += dtcDataSize)
                            {
                                KWP2000DTCInfo dtcInfo = new KWP2000DTCInfo();
                                dtcInfo.DTC = message.mData[curIndex];
                                dtcInfo.DTC <<= 8;
                                dtcInfo.DTC |= message.mData[curIndex + 1];
                                dtcInfo.Status = message.mData[curIndex + 2];

                                mDTCs.Enqueue(dtcInfo);
                            }
                        }

                        if (mDTCs.Count >= ExpectedNumDTCs)
                        {
                            ActionCompleted(true);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Read diagnostic trouble codes by status response did not contain any data", StatusMessageType.LOG);
                    }

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.ReadDiagnosticTroubleCodesByStatus, message))
                {
                    DisplayStatusMessage("Received negative response to read diagnostic trouble codes by status request.", StatusMessageType.LOG);
                    handled = true;
                    ActionCompleted(false);
                }

                if (!handled)
                {
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        protected override void ResponsesFinishedHandler(KWP2000Interface commInterface, KWP2000Message message, bool sentProperly, bool receivedAnyReplies, bool waitedForAllReplies, uint numRetries)        
        {
            if (message.mServiceID == (byte)KWP2000ServiceID.ReadDiagnosticTroubleCodesByStatus)
            {
                //we succeed if we reaed any DTCs
                ActionCompleted(mDTCs.Any());
            }
        }

        private byte mStatus;
        private ushort mGroup;
        private Queue<KWP2000DTCInfo> mDTCs;
    }

    public class ClearDiagnosticInformationAction : KWP2000Action    
    {
        public ClearDiagnosticInformationAction(KWP2000Interface commInterface, ushort group)
            : base(commInterface)
        {
            mGroup = group;
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                byte[] messageData = new byte[2];
                messageData[0] = (byte)(mGroup >> 8);
                messageData[1] = (byte)(mGroup & 0xFF);
                SendMessage((byte)KWP2000ServiceID.ClearDiagnosticInformation, messageData);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
                if (KWP2000Interface.IsPositiveResponseToRequest((byte)KWP2000ServiceID.ClearDiagnosticInformation, message))
                {
                    if (message.DataLength == 2)
                    {
                        if ((message.mData[0] == (mGroup >> 8)) && (message.mData[1] == (mGroup & 0xFF)))
                        {   
                            ActionCompleted(true);
                        }
                        else
                        {
                            DisplayStatusMessage("Cleared diagnostic information for different group than requested.", StatusMessageType.LOG);
                            ActionCompleted(false);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("Clear diagnostic information response message did not contain a group.", StatusMessageType.LOG);
                        ActionCompleted(false);
                    }

                    handled = true;
                }
                else if (KWP2000Interface.IsNegativeResponseToRequest((byte)KWP2000ServiceID.ClearDiagnosticInformation, message))
                {
                    DisplayStatusMessage("Received negative response to clear diagnostic information request.", StatusMessageType.LOG);
                    ActionCompleted(false);
                    handled = true;
                }

                if (!handled)
                {
                    ActionCompleted(false);
                }
            }

            return handled;
        }

        private ushort mGroup;
    }

    //public class ReadStatusOfDiagnosticTroubleCodesAction : KWP2000Action
    //{

    //}

    //public class ReadFeezeFrameDataAction : KWP2000Action
    //{

    //}

    public class ReadDataByLocalIdentifierAction : KWP2000Action
    {
        public ReadDataByLocalIdentifierAction(KWP2000Interface commInteface, byte localIdentifier)
            : base(commInteface)
        {
            mLocalIdentifier = localIdentifier;
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                byte[] messageData = new byte[1];
                messageData[0] = mLocalIdentifier;
                SendMessage((byte)KWP2000ServiceID.ReadDataByLocalIdentifier, messageData);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
            }

            if (!handled)
            {
                ActionCompleted(false);
            }
            
            return handled;
        }

        private byte mLocalIdentifier;
    }

    public class ReadDataByCommonIdentifierAction : KWP2000Action
    {
        public ReadDataByCommonIdentifierAction(KWP2000Interface commInteface, ushort commonIdentifier)
            : base(commInteface)
        {
            mCommonIdentifier = commonIdentifier;
        }

        public override bool Start()
        {
            bool started = false;

            if (base.Start())
            {
                byte[] messageData = new byte[2];
                messageData[0] = (byte)((mCommonIdentifier >> 8) & 0xFF);
                messageData[1] = (byte)(mCommonIdentifier & 0xFF);
                SendMessage((byte)KWP2000ServiceID.ReadDataByCommonIdentifier, messageData);

                started = true;
            }

            return started;
        }

        protected override bool MessageHandler(KWP2000Interface commInterface, KWP2000Message message)
        {
            bool handled = base.MessageHandler(commInterface, message);

            if (!handled)
            {
            }

            if (!handled)
            {
                ActionCompleted(false);
            }

            return handled;
        }

        private ushort mCommonIdentifier;
    }	
}
