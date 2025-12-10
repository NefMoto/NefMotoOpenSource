using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Communication
{
    public abstract class EventHolder
    {
        // Asynchronous method for invoking delegates
        // Returns immediately (fire-and-forget) like old BeginInvoke - doesn't wait for handlers
        public Task<bool> BeginInvokeAsync(CommunicationInterface commInterface, Delegate[] dels, object invokeParam)
        {
            commInterface.LogProfileEventDispatch("EventHolder BeginInvoke");

            bool invokedAny = false;

            if (dels != null && dels.Length > 0)
            {
                foreach (Delegate del in dels)
                {
                    // Fire-and-forget: invoke delegate asynchronously, don't wait
                    BeginInvokeDelegateAsync(commInterface, del, invokeParam);
                    invokedAny = true;
                }
            }

            commInterface.LogProfileEventDispatch("EventHolder BeginInvoke Finished");

            // Return completed task immediately - we don't wait for handlers (fire-and-forget)
            return Task.FromResult(invokedAny);
        }

        // Invoke delegate asynchronously (fire-and-forget)
        // Derived classes should override this method to handle their specific delegate signatures
        protected virtual void BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam)
        {
            // Use Task.Run for async execution - fire-and-forget (don't await)
            Task.Run(() =>
            {
                try
                {
                    del?.DynamicInvoke(invokeParam); // Invoke the delegate
                }
                catch (Exception ex)
                {
                    commInterface.LogProfileEventDispatch($"Error invoking delegate: {ex.Message}");
                }
            });
        }

        public Delegate[] GetInvocationList()
        {
            return mDelegate?.GetInvocationList();
        }

        public Type GetDelegateType()
        {
            return mDelegate.GetType();
        }

        protected EventHolder(MulticastDelegate multiDel)
        {
            mDelegate = multiDel;
        }

        private MulticastDelegate mDelegate; // Store a reference to the delegate
    }
    /*protected abstract void BeginInvoke(CommunicationInterface commInterface, Delegate del, AsyncCallback callback, object invokeParam);

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
    */

}
