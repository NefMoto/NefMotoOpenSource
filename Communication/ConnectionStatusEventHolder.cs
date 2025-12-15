using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Communication.CommunicationInterface;

namespace Communication
{
    public class ConnectionStatusEventHolder : EventHolder
    {
        public ConnectionStatusEventHolder(MulticastDelegate multiDel, ConnectionStatusType status, bool willReconnect)
            : base(multiDel)
        {
            mStatus = status;
            mWillReconnect = willReconnect;
        }

        protected override void BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam, Action onHandlerComplete)
        {
            if (del is ConnectionStatusChangedDelegate statusChangedDel)
            {
                var kwp2000CommInterface = commInterface as KWP2000Interface;

#if DEBUG // Cut down on work in non-debug builds
                commInterface.DisplayStatusMessage(
                    $"Invoking: {del.Target}.{del.Method} at {DateTime.Now:hh:mm:ss.fff}",
                    StatusMessageType.DEV);
#endif

                // Fire-and-forget: use Task.Run but don't await
                Task.Run(() =>
                {
                    try
                    {
                        statusChangedDel?.Invoke(kwp2000CommInterface, mStatus, mWillReconnect);
                    }
                    catch (Exception ex)
                    {
                        commInterface.LogProfileEventDispatch($"Error during delegate invocation: {ex.Message}");
                    }
                    finally
                    {
                        onHandlerComplete?.Invoke();
                    }
                });
            }
            else
            {
                onHandlerComplete?.Invoke();
            }
        }

        protected ConnectionStatusType mStatus;
        protected bool mWillReconnect;
    }

}
