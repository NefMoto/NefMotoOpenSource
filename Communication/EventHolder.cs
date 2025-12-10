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
        public async Task<bool> BeginInvokeAsync(CommunicationInterface commInterface, Delegate[] dels, object invokeParam)
        {
            commInterface.LogProfileEventDispatch("EventHolder BeginInvoke");

            bool invokedAny = false;

            if (dels != null && dels.Length > 0)
            {
                // Create a list of tasks for each delegate invocation
                var tasks = new List<Task>();

                foreach (Delegate del in dels)
                {
                    // Asynchronously invoke the delegate
                    tasks.Add(BeginInvokeDelegateAsync(commInterface, del, invokeParam));
                    invokedAny = true;
                }

                // Await all tasks to complete
                await Task.WhenAll(tasks);
            }

            commInterface.LogProfileEventDispatch("EventHolder BeginInvoke Finished");

            return invokedAny;
        }

        // Refactor for individual delegate invocation
        // Derived classes should override this method to handle their specific delegate signatures
        // This base implementation will fail for delegates that don't take exactly one parameter
        protected virtual async Task BeginInvokeDelegateAsync(CommunicationInterface commInterface, Delegate del, object invokeParam)
        {
            await Task.Run(() =>
            {
                del?.DynamicInvoke(invokeParam); // Invoke the delegate
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
