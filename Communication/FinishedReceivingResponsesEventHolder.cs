﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Communication
{
    public class FinishedReceivingResponsesEventHolder : EventHolder
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

        public async Task BeginInvokeAsync(CommunicationInterface commInterface, Delegate del, object invokeParam)
        {
            if (del is MessageSendFinishedDelegate finishedDel)
            {
                if (commInterface is KWP2000Interface kwp2000CommInterface)
                {
#if DEBUG
                    commInterface.LogProfileEventDispatch($"Invoking: {del.Target}.{del.Method} at {DateTime.Now:hh:mm:ss.fff}");
#endif
                    try
                    {
                        // Execute the delegate asynchronously
                        await Task.Run(() =>
                        {
                            finishedDel.Invoke(
                                kwp2000CommInterface,
                                mMessage,
                                mMessageSent,
                                mReceivedAnyReplies,
                                mWaitedForAllReplies,
                                mNumRetries
                            );
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        commInterface.LogProfileEventDispatch($"Error during delegate invocation: {ex.Message}");
                    }
                }
                else
                {
                    commInterface.LogProfileEventDispatch("Invalid communication interface type.");
                }
            }
            else
            {
                commInterface.LogProfileEventDispatch("Delegate type mismatch in BeginInvokeAsync.");
            }
        }

        protected KWP2000Message mMessage;
        protected bool mMessageSent;
        protected bool mReceivedAnyReplies;
        protected bool mWaitedForAllReplies;
        protected uint mNumRetries;
    }
}
