using System;

namespace J2534Diag
{
    /// <summary>
    /// Coordinates receive-loop ownership inside this application.
    /// This does not coordinate separate processes or other J2534 clients.
    /// </summary>
    public sealed class ReceiveChannelGate
    {
        private readonly object _sync = new object();
        private string _owner;

        public string CurrentOwner
        {
            get { lock (_sync) return _owner; }
        }

        public bool TryAcquire(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner must be specified.", nameof(owner));
            lock (_sync)
            {
                if (_owner != null) return false;
                _owner = owner;
                return true;
            }
        }

        public void Release(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner must be specified.", nameof(owner));
            lock (_sync)
            {
                if (_owner != owner)
                    throw new InvalidOperationException("Receive ownership mismatch. Current owner: " + (_owner ?? "<none>"));
                _owner = null;
            }
        }
    }
}
