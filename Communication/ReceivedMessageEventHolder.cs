
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

        protected override void BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam, Action onHandlerComplete)
        {
            if (del is MessageChangedDelegate receiveMessageDel)
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
                            receiveMessageDel.Invoke(kwp2000CommInterface, mMessage);
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
    }
}