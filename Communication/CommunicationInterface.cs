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

//#define PROFILE_EVENT_DISPATCH

using System;
using System.Collections.Generic;
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
    public abstract class CommunicationAction
    {
        public delegate void CompletedActionDelegate(CommunicationAction action, bool success);

        public CommunicationAction(CommunicationInterface commInterface)
        {
            CommInterface = commInterface;

            IsComplete = true;
            CompletedSuccessfully = true;
            CompletedWithoutCommunicationError = true;
        }

        public virtual bool Start()
        {
            bool result = false;

			mActionStartTime = DateTime.Now;
			mActionEndTime = mActionStartTime;

            if (IsComplete)
            {
                if (CommInterface.IsConnected())
                {   
                    IsComplete = false;
                    CompletedSuccessfully = false;
                    CompletedWithoutCommunicationError = true;
                    result = true;
                }
            }

            return result;
        }

        protected void DisplayStatusMessage(string message, StatusMessageType messageType)
        {
            if (CommInterface != null)
            {
                CommInterface.DisplayStatusMessage(message, messageType);
            }
        }

        protected void ActionCompleted(bool success)
        {
            ActionCompletedInternal(success, false);
        }

        protected virtual void ActionCompletedInternal(bool success, bool communicationError)
        {
            if (!IsComplete)
            {
				mActionEndTime = DateTime.Now;

                IsComplete = true;
                CompletedSuccessfully = success;
                CompletedWithoutCommunicationError = !communicationError;

                if (ActionCompletedEvent != null)
                {
                    ActionCompletedEvent(this, success);
                }
            }
        }

        public void Abort()
        {
            ActionCompletedInternal(false, false);
        }

        public bool IsComplete { get; private set; }
        public bool CompletedWithoutCommunicationError { get; private set; }//true if the action completed on it's own, false if the action completed because messages failed to be sent or received
        public bool CompletedSuccessfully { get; private set; }
        
        protected CommunicationInterface CommInterface { get; set; }
        public event CompletedActionDelegate ActionCompletedEvent;

		public TimeSpan ActionElapsedTime
		{
			get
			{
				if (!IsComplete)
				{
					return DateTime.Now - mActionStartTime;
				}
				else
				{
					return mActionEndTime - mActionStartTime;
				}
			}
		}

		private DateTime mActionStartTime;
		private DateTime mActionEndTime;
    }

    public abstract class CommunicationOperation : Operation
    {
        public CommunicationOperation(CommunicationInterface commInterface)
        {
            CommInterface = commInterface;
            CurrentAction = null;
        }

        protected override void OnOperationStart()
        {
            CommInterface.ConnectionStatusChangedEvent += this.ConnectionChangedHandler;

            ResetOperation();

            bool success = StartNextAction();

            if (CurrentAction == null)
            {
                OperationCompleted(success);
            }
        }

        protected void AbortCurrentAction()
        {
            if (CurrentAction != null)
            {
                if (!CurrentAction.IsComplete)
                {
                    CurrentAction.Abort();
                }

                CurrentAction = null;
            }
        }

        protected override bool CanOperationStart()
        {
            return base.CanOperationStart() && CommInterface.IsConnected();
        }

        protected virtual void ResetOperation()
        {
        }

        protected virtual CommunicationAction NextAction()
        {
            if (IsRunning)
            {
                AbortCurrentAction();
            }

            return null;
        }

        //return true unless we try to start an action and fail
        protected bool StartNextAction()
        {
			bool success = false;

			try
			{
				CurrentAction = NextAction();

				success = true;

				if (CurrentAction != null)
				{
					CurrentAction.ActionCompletedEvent += this.ActionCompletedHandler;
					success = CurrentAction.Start();

					if (success)
					{
						OnActionStarted(CurrentAction);
					}
					else
					{
						AbortCurrentAction();
					}
				}				
			}
			catch
			{
				Debug.Fail("Failed to start action");
			}

            return success;
        }

        protected virtual void OnActionStarted(CommunicationAction action)
        {

        }

        protected override bool OnOperationCompleted(bool success)
        {
			bool wasRunning = IsRunning;

            bool result = base.OnOperationCompleted(success);

			if(wasRunning)
			{
				CommInterface.ConnectionStatusChangedEvent -= this.ConnectionChangedHandler;

				AbortCurrentAction();
			}

			return result;
        }

        private void ActionCompletedHandler(CommunicationAction action, bool success)
        {
            lock (this)//lock to ensure we don't accidentally get other callbacks while handling this one
            {
                if (IsRunning)//always need to check this in case we are getting callbacks after we complete
                {
                    CurrentAction = null;

                    action.ActionCompletedEvent -= this.ActionCompletedHandler;

                    OnActionCompleted(action, success);                    
                }
            }
        }

        protected virtual void OnActionCompleted(CommunicationAction action, bool success)
        {
            if (success)
            {
                success = StartNextAction();

                if (CurrentAction == null)
                {
                    OperationCompleted(true);
                }
            }
            else
            {
                OperationCompleted(false);
            }
        }

        private void ConnectionChangedHandler(CommunicationInterface commInterface, CommunicationInterface.ConnectionStatusType status, bool willReconnect)
        {
            lock (this)//lock to ensure we don't accidentally get other callbacks while handling this one
            {
                if (IsRunning)//always need to check this in case we are getting callbacks after we complete
                {
                    OnConnectionChanged(commInterface, status, willReconnect);
                }
            }
        }

        protected virtual void OnConnectionChanged(CommunicationInterface commInteface, CommunicationInterface.ConnectionStatusType status, bool willReconnect)
        {
            if ((status == CommunicationInterface.ConnectionStatusType.CommunicationTerminated) || (status == CommunicationInterface.ConnectionStatusType.Disconnected))
            {
                if (ShouldFailOnDisconnect())
                {
                    OperationCompleted(false);
                }
            }
        }

        protected virtual bool ShouldFailOnDisconnect()
        {
            return true;
        }

        protected CommunicationInterface CommInterface { get; set; }
        protected CommunicationAction CurrentAction { get; private set; }
    }

    public abstract class CommunicationInterface : INotifyPropertyChanged
    {
        public enum ConnectionStatusType
        {
            [Description("Disconnected")]
            Disconnected = 0,
            [Description("Connection Pending")]
            ConnectionPending,
            [Description("Connected")]
            Connected,
            [Description("Disconnection Pending")]
            DisconnectionPending,
            [Description("No Connection")]
            CommunicationTerminated
        }

        public enum Protocol
        {
            [Description("Boot Mode")]
            BootMode,            
            [Description("KWP2000")]
            KWP2000            
        }

        public CommunicationInterface()
        {
            mFTDIDevice = new FTDI(this.DisplayFTDIErrorMessage, this.DisplayFTDIWarningMessage);
            mConsumeTransmitEcho = true;

            mQueuedEvents = new Queue<EventHolder>();
            mQueuedEventTriggerMutex = new Object();            
        }

        public delegate void ConnectionStatusChangedDelegate(CommunicationInterface commInterface, CommunicationInterface.ConnectionStatusType status, bool willReconnect);
        public event ConnectionStatusChangedDelegate ConnectionStatusChangedEvent;

        private void DisplayFTDIErrorMessage(string ftdiMessage)
        {
            DisplayStatusMessage("FTDI Error: " + ftdiMessage, StatusMessageType.USER);
        }

        private void DisplayFTDIWarningMessage(string ftdiMessage)
        {
            DisplayStatusMessage("FTDI Warning: " + ftdiMessage, StatusMessageType.LOG);
        }

		private const uint FTDIDeviceReadTimeOutMsDefaultValue = 1000;
		[DefaultValue(FTDIDeviceReadTimeOutMsDefaultValue)]
        public uint FTDIDeviceReadTimeOutMs
        {
            get
            {
                return _FTDIDeviceReadTimeOutMs;
            }
            set
            {
                if (_FTDIDeviceReadTimeOutMs != value)
                {
                    _FTDIDeviceReadTimeOutMs = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("FTDIDeviceReadTimeOutMs"));
                }
            }
        }
		private uint _FTDIDeviceReadTimeOutMs = FTDIDeviceReadTimeOutMsDefaultValue;

		private const uint FTDIDeviceWriteTimeOutMsDefaultValue = 1000;
		[DefaultValue(FTDIDeviceWriteTimeOutMsDefaultValue)]
        public uint FTDIDeviceWriteTimeOutMs
        {
            get
            {
                return _FTDIDeviceWriteTimeOutMs;
            }
            set
            {
                if (_FTDIDeviceWriteTimeOutMs != value)
                {
                    _FTDIDeviceWriteTimeOutMs = value;

                    OnPropertyChanged(new PropertyChangedEventArgs("FTDIDeviceWriteTimeOutMs"));
                }
            }
        }
		private uint _FTDIDeviceWriteTimeOutMs = FTDIDeviceWriteTimeOutMsDefaultValue;

        public FTDI.FT_DEVICE_INFO_NODE SelectedDeviceInfo { get; set; }

        public virtual ConnectionStatusType ConnectionStatus
        {
            get
            {
                return _ConnectionStatus;
            }

            set
            {
                if (_ConnectionStatus != value)
                {                    
                    _ConnectionStatus = value;

                    if (_ConnectionStatus == ConnectionStatusType.ConnectionPending)
                    {                 
                        DisplayStatusMessage("Connecting...", StatusMessageType.USER);
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Connected)
                    {
                        DisplayStatusMessage("Connected", StatusMessageType.USER);
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.DisconnectionPending)
                    {
                        DisplayStatusMessage("Disconnecting...", StatusMessageType.USER);
                    }
                    else if (_ConnectionStatus == ConnectionStatusType.Disconnected)
                    {
                        DisplayStatusMessage("Disconnected", StatusMessageType.USER);
                    }

                    OnPropertyChanged(new PropertyChangedEventArgs("ConnectionStatus"));

                    OnConnectionStatusChanged();
                }                
            }
        }
        protected ConnectionStatusType _ConnectionStatus = ConnectionStatusType.CommunicationTerminated;

        protected void OnConnectionStatusChanged()
        {
            if (ConnectionStatusChangedEvent != null)
            {
                QueueAndTriggerEvent(new ConnectionStatusEventHolder(ConnectionStatusChangedEvent, ConnectionStatus, GetConnectionAttemptsRemaining() > 0));
            }
        }

        private class ConnectionStatusEventHolder : EventHolder
        {
            public ConnectionStatusEventHolder(MulticastDelegate multiDel, ConnectionStatusType status, bool willReconnect)
                : base(multiDel)
            {
                mStatus = status;
                mWillReconnect = willReconnect;
            }

            protected override void BeginInvoke(CommunicationInterface commInterface, Delegate del, AsyncCallback callback, object invokeParam)
            {
                if (del is ConnectionStatusChangedDelegate)
                {
                    var statusChangedDel = del as ConnectionStatusChangedDelegate;
                    var KwP2000CommInterface = commInterface as KWP2000Interface;

#if DEBUG//cut down on work in non debug builds
                    commInterface.DisplayStatusMessage("Invoking: " + del.Target.ToString() + "." + del.Method.ToString() + " at " + DateTime.Now.ToString("hh:mm:ss.fff"), StatusMessageType.DEV);
#endif
                    statusChangedDel.BeginInvoke(KwP2000CommInterface, mStatus, mWillReconnect, callback, invokeParam);
                }
            }

            protected ConnectionStatusType mStatus;
            protected bool mWillReconnect;
        }

        protected abstract uint GetConnectionAttemptsRemaining();

        public abstract Protocol CurrentProtocol
        {
            get;
        }

        public bool IsConnected()
        {
            return (ConnectionStatus == ConnectionStatusType.Connected);
        }

        public bool IsConnectionOpen()
        {
            return (ConnectionStatus != ConnectionStatusType.Disconnected) && (ConnectionStatus != ConnectionStatusType.CommunicationTerminated);
        }

        protected void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, e);
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected FTD2XX_NET.FTDI mFTDIDevice = null;
        protected bool mConsumeTransmitEcho = true;
        
        protected bool OpenFTDIDevice(FTDI.FT_DEVICE_INFO_NODE deviceToOpen)
        {
            bool connected = false;

            lock (mFTDIDevice)
            {
                if (IsFTDIDeviceOpen())
                {
                    CloseFTDIDevice();
                }

                if (FTDI.IsFTD2XXDLLLoaded())
                {
                    if (deviceToOpen != null)
                    {
                        if (mFTDIDevice.OpenByLocation(deviceToOpen.LocId) == FTDI.FT_STATUS.FT_OK)
                        {
                            FTDI.FT_STATUS ftdiStatus = FTDI.FT_STATUS.FT_OK;

                            ftdiStatus |= mFTDIDevice.ResetDevice();
                            //ftdiStatus |= mFTDIDevice.ResetPort();
                            ftdiStatus |= mFTDIDevice.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

                            if (ftdiStatus == FTDI.FT_STATUS.FT_OK)
                            {
                                DisplayStatusMessage("Opened FTDI device.", StatusMessageType.LOG);
                                DisplayStatusMessage("FTDI device info - Description: " + deviceToOpen.Description
                                    + " Serial Number: " + deviceToOpen.SerialNumber + " Device Type: " + deviceToOpen.Type
                                    + " ID: 0x" + deviceToOpen.ID.ToString("X") + " Device Flags: 0x" + deviceToOpen.Flags.ToString("X"), StatusMessageType.LOG);

                                if (FTDI.IsFTDChipIDDLLLoaded())
                                {
                                    DisplayStatusMessage("FTDI ChipID DLL is loaded, checking chip ID...", StatusMessageType.LOG);

                                    uint chipID = 0;
                                    if (mFTDIDevice.GetChipIDFromCurrentDevice(out chipID) == FTDI.FT_STATUS.FT_OK)
                                    {
                                        DisplayStatusMessage("FTDI device chip ID: 0x" + chipID.ToString("X"), StatusMessageType.LOG);
                                    }
                                    else
                                    {
                                        DisplayStatusMessage("Unable to read FTDI device chip ID", StatusMessageType.LOG);
                                    }
                                }

                                connected = true;
                            }
                            else
                            {
                                mFTDIDevice.Close();
                            }
                        }
                        else
                        {
                            DisplayStatusMessage("Could not open FTDI device", StatusMessageType.LOG);
                        }
                    }
                    else
                    {
                        DisplayStatusMessage("No FTDI device selected", StatusMessageType.LOG);
                    }
                }
                else
                {
                    DisplayStatusMessage("FTDI FTD2XX device driver DLL could not be loaded.", StatusMessageType.USER);
                }
            }

            return connected;
        }

        protected void CloseFTDIDevice()
        {
            if (IsFTDIDeviceOpen())
            {
                DisplayStatusMessage("Closing FTDI device.", StatusMessageType.LOG);
                mFTDIDevice.Close();
            }
        }

        protected bool IsFTDIDeviceOpen()
        {
            return mFTDIDevice.IsOpen;
        }

        public event DisplayStatusMessageDelegate mDisplayStatusMessage;
        public event DisplayUserPrompt mDisplayUserPrompt;

        public void DisplayStatusMessage(string message, StatusMessageType messageType)
		{
			if (mDisplayStatusMessage != null)
			{
				mDisplayStatusMessage(message, messageType);
			}
		}

        public UserPromptResult DisplayUserPrompt(string title, string message, UserPromptType promptType)
        {
            UserPromptResult result = UserPromptResult.NONE;

            if (mDisplayUserPrompt != null)
            {
                result = mDisplayUserPrompt(title, message, promptType);
            }

            return result;
        }

        [Conditional("PROFILE_EVENT_DISPATCH")]
        public void LogProfileEventDispatch(string logMessage)
        {
            DisplayStatusMessage(logMessage, StatusMessageType.LOG);
        }

        protected bool QueueAndTriggerEvent(EventHolder eventHolder)
        {
            LogProfileEventDispatch("Start QueueAndTriggerEvent");

            bool success = false;
            const int MAX_NUM_PENDING_MESSAGE_EVENTS = 10;

            Debug.Assert(mQueuedEvents.Count < MAX_NUM_PENDING_MESSAGE_EVENTS);

            //don't queue anymore if we are backed up with unhandled messages
            if (mQueuedEvents.Count < MAX_NUM_PENDING_MESSAGE_EVENTS)
            {
                lock (mQueuedEvents)
                {
                    mQueuedEvents.Enqueue(eventHolder);
                    success = true;
                }

                //trigger the message received event, or save the message for later if we are busy
                TriggerNextQueuedEvent();
            }

            LogProfileEventDispatch("End QueueAndTriggerEvent");

            return success;
        }

        private bool mIsTriggeringQueuedEvent = false;
        private void TriggerNextQueuedEvent()
        {
            LogProfileEventDispatch("Start TriggerNextQueuedEvent");

            bool canTriggerNextEvent = true;

            while (canTriggerNextEvent)
            {
                LogProfileEventDispatch("Start TriggerNextQueuedEvent, Starting Loop");

                canTriggerNextEvent = false;

                lock (mQueuedEventTriggerMutex)
                {
                    if (!mIsTriggeringQueuedEvent)
                    {
                        Debug.Assert(mNumQueuedEventHandlersInProgress >= 0, "Negative number of event handlers");

                        canTriggerNextEvent = (mNumQueuedEventHandlersInProgress == 0);
                        mIsTriggeringQueuedEvent = canTriggerNextEvent;
                    }
                }

                if (canTriggerNextEvent)
                {
                    EventHolder eventHolder = null;

                    lock (mQueuedEvents)
                    {
                        canTriggerNextEvent = (mQueuedEvents.Count > 0);

                        if (canTriggerNextEvent)
                        {
                            eventHolder = mQueuedEvents.Dequeue();
                        }
                    }

                    Debug.Assert((eventHolder != null) || !canTriggerNextEvent);

                    if ((eventHolder != null) && canTriggerNextEvent)
                    {
                        Delegate[] invocationList = null;

                        lock (mQueuedEventTriggerMutex)
                        {
                            Debug.Assert(mNumQueuedEventHandlersInProgress == 0);

                            invocationList = eventHolder.GetInvocationList();
                            Debug.Assert(invocationList != null);

                            if (invocationList != null)
                            {
								//it is OK to have more than one delegate in the invocation list, specifically for the connection changed events there may be multiple disconnection event listeners
                                Debug.Assert(invocationList.Length > 0);//debug test to make sure nothing weird is happening                                

                                for (int x = 0; x < invocationList.Length; x++)
                                {
                                    Interlocked.Increment(ref mNumQueuedEventHandlersInProgress);
                                }
                            }
                        }

                        Debug.Assert(invocationList != null);

                        //delegates are invoked here so we don't have to invoke them inside a lock statement
                        if (invocationList != null)
                        {
                            LogProfileEventDispatch("Start TriggerNextQueuedEvent, BeginInvoke");

                            eventHolder.BeginInvoke(this, invocationList, this.QueuedEventHandlerComplete, null);

                            LogProfileEventDispatch("Start TriggerNextQueuedEvent, BeginInvoke Finished");
                        }
                    }

                    lock (mQueuedEventTriggerMutex)
                    {
                        mIsTriggeringQueuedEvent = false;
                    }
                }
            }

            LogProfileEventDispatch("End TriggerNextQueuedEvent");
        }

        protected void QueuedEventHandlerComplete(IAsyncResult ar)
        {            
            LogProfileEventDispatch("Start QueuedEventHandlerComplete");

            try
            {
                var result = ar as AsyncResult;

                if (!result.EndInvokeCalled)
                {
					Debug.Assert(result.AsyncDelegate != null);

                    object[] param = { ar };
                    result.AsyncDelegate.GetType().InvokeMember("EndInvoke", System.Reflection.BindingFlags.InvokeMethod, null, result.AsyncDelegate, param);
                }
            }
            catch (Exception e)
            {
                //Note: exceptions thrown by EndInvoke are coming from the async delegate code
                //Exceptions thrown inside the delegate are rethrown when the return value is received with EndInvoke
                Debug.Fail("Event handler EndInvoke threw an exception: " + e.Message);
            }

			if (Interlocked.Decrement(ref mNumQueuedEventHandlersInProgress) == 0)
			{
				//prevent recursion using BeginInvoke
				var triggerNextEvent = (ThreadStart)delegate { TriggerNextQueuedEvent(); };
				triggerNextEvent.BeginInvoke(null, null);
			}

            LogProfileEventDispatch("End QueuedEventHandlerComplete");
        }

        protected abstract class EventHolder
        {
            protected abstract void BeginInvoke(CommunicationInterface commInterface, Delegate del, AsyncCallback callback, object invokeParam);

            public bool BeginInvoke(CommunicationInterface commInterface, Delegate[] dels, AsyncCallback callback, object invokeParam)
            {
                commInterface.LogProfileEventDispatch("EventHolder BeginInvoke");

                bool invokedAny = false;

                if ((dels != null) && (dels.Length > 0))
                {
                    foreach (Delegate del in dels)
                    {
                        BeginInvoke(commInterface, del, callback, invokeParam);
                        invokedAny = true;
                    }
                }

                commInterface.LogProfileEventDispatch("EventHolder BeginInvoke Finished");

                return invokedAny;
            }

            public Delegate[] GetInvocationList()
            {
                if (mDelegate != null)
                {
                    return mDelegate.GetInvocationList();
                }

                return null;
            }

            public Type GetDelegateType()
            {
                return mDelegate.GetType();
            }

            protected EventHolder(MulticastDelegate multiDel)
            {
                mDelegate = multiDel;
            }
            private MulticastDelegate mDelegate;//storing a reference to the delegate will allow us to keep an immutable copy of it
        }

        protected void ClearQueuedEvents()
        {
            lock (mQueuedEvents)
            {
                mQueuedEvents.Clear();
            }
        }

        private Queue<EventHolder> mQueuedEvents;
        private int mNumQueuedEventHandlersInProgress;
        private object mQueuedEventTriggerMutex;

        protected Thread mSendReceiveThread = null;

        protected void StartSendReceiveThread(ThreadStart threadStart)
        {
            Debug.Assert(mSendReceiveThread == null);
            if (mSendReceiveThread == null)
            {
                DisplayStatusMessage("Starting send receive thread.", StatusMessageType.LOG);
                mSendReceiveThread = new Thread(threadStart);
                mSendReceiveThread.Name = "Send Receive Thread";
                mSendReceiveThread.IsBackground = true;
                mSendReceiveThread.Priority = ThreadPriority.AboveNormal;
                mSendReceiveThread.Start();
            }
        }

        protected void KillSendReceiveThread()
        {
            if ((mSendReceiveThread != null) && (mSendReceiveThread.IsAlive))
            {
                DisplayStatusMessage("Aborting send receive thread.", StatusMessageType.LOG);
                mSendReceiveThread.Abort();
            }
            mSendReceiveThread = null;
        }

        protected bool IsSendReceiveThreadRunning()
        {
            bool result = false;

            if (mSendReceiveThread != null)
            {
                result = mSendReceiveThread.IsAlive;
            }

            return result;
        }
    }
}
