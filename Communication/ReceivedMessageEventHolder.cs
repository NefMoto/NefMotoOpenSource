
using System;
using System.Threading.Tasks;

namespace Communication
{
    public  class ReceivedMessageEventHolder : EventHolder
    {
        public ReceivedMessageEventHolder(MulticastDelegate multiDel, KWP2000Message message)
            : base(multiDel)
        {
            mMessage = message;
        }

        protected override async Task BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam)
        {
            if (del is MessageChangedDelegate receiveMessageDel)
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
                            receiveMessageDel.Invoke(kwp2000CommInterface, mMessage);
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
                commInterface.LogProfileEventDispatch("Delegate type mismatch in BeginInvokeDelegateAsync.");
            }
        }

        protected KWP2000Message mMessage;
    }
}