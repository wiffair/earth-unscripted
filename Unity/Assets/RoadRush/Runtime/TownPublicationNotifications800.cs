using System;
using System.Collections.Generic;
using System.Threading;

namespace RoadRush
{
    /// <summary>
    /// Pure notification policy shared by the actual token and its lifetime tests.
    /// This class issues NO town/source/ground authority. The world invalidates its
    /// token before Retire; snapshotting callbacks guarantees one broken consumer
    /// cannot prevent another consumer or physical teardown.
    /// </summary>
    internal sealed class TownPublicationNotifications800
    {
        private const int MaximumSubscribers = 128;
        private const int MaximumCallbackErrors = 16;
        private const int MaximumErrorCharacters = 16384;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<Subscription> subscriptions = new List<Subscription>();
        private readonly List<string> errors = new List<string>();
        private bool retired;
        internal IDisposable Subscribe(Action<string> callback)
        {
            RequireThread();
            if (retired || callback == null || subscriptions.Count >= MaximumSubscribers)
                throw new InvalidOperationException("Live bounded town notification subscription required");
            var subscription = new Subscription(this, callback);
            subscriptions.Add(subscription);
            return subscription;
        }

        internal void Retire(string reason)
        {
            RequireThread();
            if (retired)
                return;
            retired = true;
            var callbacks = new List<Action<string>>();
            foreach (var subscription in subscriptions)
            {
                callbacks.Add(subscription.Callback);
                subscription.Detach();
            }

            subscriptions.Clear();
            foreach (var callback in callbacks)
            {
                try
                {
                    callback(reason);
                }
                catch (Exception exception)
                {
                    if (errors.Count >= MaximumCallbackErrors)
                        continue;
                    string text = exception.ToString();
                    errors.Add(text.Length <= MaximumErrorCharacters ? text : text.Substring(0, MaximumErrorCharacters));
                }
            }
        }

        internal string[] CopyErrors()
        {
            RequireThread();
            return errors.ToArray();
        }

        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Town notification reads/cleanup require issuing main thread");
        }

        private sealed class Subscription : IDisposable
        {
            private TownPublicationNotifications800 owner;
            private readonly int threadId;
            internal Action<string> Callback;
            internal Subscription(TownPublicationNotifications800 owner, Action<string> callback)
            {
                this.owner = owner;
                threadId = owner.threadId;
                Callback = callback;
            }

            internal void Detach()
            {
                owner = null;
                Callback = null;
            }

            public void Dispose()
            {
                if (Thread.CurrentThread.ManagedThreadId != threadId)
                    throw new InvalidOperationException("Town subscription disposal requires issuing main thread");
                owner?.subscriptions.Remove(this);
                Detach();
            }
        }
    }
}
