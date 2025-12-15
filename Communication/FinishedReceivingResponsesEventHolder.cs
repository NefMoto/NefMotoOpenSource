using System;
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

        protected override void BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam, Action onHandlerComplete)
        {
            if (del is MessageSendFinishedDelegate finishedDel)
            {
                if (commInterface is KWP2000Interface kwp2000CommInterface)
                {
#if DEBUG
                    commInterface.LogProfileEventDispatch($"Invoking: {del.Target}.{del.Method} at {DateTime.Now:hh:mm:ss.fff}");
#endif
                    // Fire-and-forget: use Task.Run but don't await
                    Task.Run(() =>
                    {
                        try
                        {
                            finishedDel.Invoke(
                                kwp2000CommInterface,
                                mMessage,
                                mMessageSent,
                                mReceivedAnyReplies,
                                mWaitedForAllReplies,
                                mNumRetries
                            );
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
                    commInterface.LogProfileEventDispatch("Invalid communication interface type.");
                    onHandlerComplete?.Invoke();
                }
            }
            else
            {
                commInterface.LogProfileEventDispatch("Delegate type mismatch in BeginInvokeDelegateAsync.");
                onHandlerComplete?.Invoke();
            }
        }

        protected KWP2000Message mMessage;
        protected bool mMessageSent;
        protected bool mReceivedAnyReplies;
        protected bool mWaitedForAllReplies;
        protected uint mNumRetries;
    }
}
