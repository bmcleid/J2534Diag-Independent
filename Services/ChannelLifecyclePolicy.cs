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

        // A reader and its poller share one session token. Any terminal worker
        // exit cancels its peer; the owner still joins both before releasing a lease.
        public static void RunCoupledWorker(CancellationTokenSource cancellation,
            Action work, Action<Exception> onFailure)
        {
            if (cancellation == null) throw new ArgumentNullException(nameof(cancellation));
            if (work == null) throw new ArgumentNullException(nameof(work));
            try { work(); }
            catch (Exception ex) { onFailure?.Invoke(ex); }
            finally { cancellation.Cancel(); }
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

    // Narrow host pacing for FuelTrims, not an OEM timing requirement.
    public sealed class FuelTrimPollingGate : IDisposable
    {
        private readonly object _sync = new object();
        private readonly AutoResetEvent _reply = new AutoResetEvent(false);
        private byte? _pendingPid;
        private readonly int _responseWait;
        private readonly int _minimumGap;
        private readonly int _missLimit;
        public FuelTrimPollingGate(int responseWait = 200, int minimumGap = 50, int missLimit = 6)
        {
            if (responseWait < 1 || minimumGap < 1 || missLimit < 1)
                throw new ArgumentOutOfRangeException();
            _responseWait = responseWait; _minimumGap = minimumGap; _missLimit = missLimit;
        }
        public bool Observe(byte pid)
        {
            lock (_sync)
            {
                if (_pendingPid != pid) return false;
                _reply.Set();
                return true;
            }
        }
        public void Run(Func<byte[]> enabledPids, Action<byte> send, CancellationToken token)
        {
            int misses = 0;
            while (!token.IsCancellationRequested)
            {
                var pids = enabledPids();
                if (pids.Length == 0) return;
                foreach (byte pid in pids)
                {
                    if (token.IsCancellationRequested) return;
                    lock (_sync) { _reply.Reset(); _pendingPid = pid; }
                    int result;
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        send(pid);
                        // Cancellation wins when both handles are signalled.
                        result = WaitHandle.WaitAny(new[] { token.WaitHandle, _reply }, _responseWait);
                    }
                    finally { lock (_sync) { _pendingPid = null; } }
                    if (result == 0 || token.IsCancellationRequested) return;
                    misses = result == 1 ? 0 : misses + 1;
                    if (misses >= _missLimit) return;
                    if (token.WaitHandle.WaitOne(_minimumGap)) return;
                }
            }
        }
        // The owning form disposes only after both workers have joined.
        public void Dispose() { _reply.Dispose(); }
    }
}
