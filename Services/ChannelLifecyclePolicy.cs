using System;
using System.Threading;

namespace J2534Diag
{
    /// <summary>
    /// Coordinates local J2534 channel retirement. No device DLL required for
    /// these checks. This does not coordinate with external J2534 processes.
    /// </summary>
    public static class ChannelLifecyclePolicy
    {
        public static void RequireUnowned(ReceiveChannelGate gate)
        {
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            string owner = gate.CurrentOwner;
            if (owner != null)
                throw new InvalidOperationException(
                    "Cannot close or reconnect J2534 channel while " + owner + " owns the receive channel.");
        }

        public static bool RequestStopAndWait(Thread worker, CancellationTokenSource cancellation, int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            cancellation?.Cancel();
            if (worker == null) return true;
            if (Thread.CurrentThread == worker)
                throw new InvalidOperationException("Cannot wait for a J2534 monitor thread from that same thread.");
            // Join is safe even if the worker has already terminated.
            if ((worker.ThreadState & ThreadState.Unstarted) != 0) return true;
            return worker.Join(timeoutMilliseconds);
        }
    }
}
