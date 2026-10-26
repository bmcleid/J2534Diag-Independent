using System;

namespace J2534Diag
{
    // Guards one J2534 device connection lifecycle within this process.
    // Unlike ReceiveChannelGate, this protects native AND managed opens.
    // Separate processes and other J2534 clients remain outside this lock.
    public static class J2534ConnectionOwnership
    {
        private static readonly object Sync = new object();
        private static object holder;
        private static string name;

        public static string CurrentOwner
        {
            get { lock (Sync) return name; }
        }

        public static bool TryAcquire(object owner, string ownerName)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(ownerName))
                throw new ArgumentException("Specify a connection owner.", nameof(ownerName));
            lock (Sync)
            {
                if (holder != null) return ReferenceEquals(holder, owner);
                holder = owner;
                name = ownerName;
                return true;
            }
        }

        public static void Release(object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (Sync)
            {
                if (!ReferenceEquals(holder, owner))
                    throw new InvalidOperationException(
                        "J2534 connection owner mismatch; currently " +
                        (name ?? "<none>") + ".");
                holder = null;
                name = null;
            }
        }
    }
}
