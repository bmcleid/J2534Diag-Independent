using System;
using System.Collections.Generic;
using System.Threading;

namespace J2534Diag
{
    /// <summary>
    /// All calls belong to ONE device opened by this API instance. A native
    /// implementation must not borrow Channel handles from J2534-Sharp.
    /// No write/transmit, buffer clear, or programming functions exist here.
    /// </summary>
    public interface INativeJ2534CanApi
    {
        uint Open(out uint device);
        uint ConnectCan500k(uint device, out uint channel);
        uint StartWildcardPassFilter(uint channel, out uint filter);
        NativeCanReadResult ReadCan(uint channel, int requestedMaximum, int timeoutMilliseconds);
        uint StopFilter(uint channel, uint filter);
        uint Disconnect(uint channel);
        uint Close(uint device);
    }

    /// <summary>
    /// Separate, currently UNWIRED sole-owner receiver lifecycle. The caller
    /// must explicitly authorize use and prove J2534-Sharp is not connected.
    /// Process-wide gate covers sessions of this class only, not other apps.
    /// </summary>
    public sealed class NativeOwnedCanSession : INativeCanReadProvider, IDisposable
    {
        private static int _sessionHeld;
        private readonly INativeJ2534CanApi _api;
        private readonly Func<bool> _managedConnectionInactive;
        private readonly object _sync = new object();
        private bool _lease, _deviceOpened, _connected, _filtered, _disposed;
        private uint _device, _channel, _filter;

        public NativeOwnedCanSession(INativeJ2534CanApi api,
            Func<bool> managedConnectionInactive)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _managedConnectionInactive = managedConnectionInactive ??
                throw new ArgumentNullException(nameof(managedConnectionInactive));
        }

        public bool IsReceiving { get { lock (_sync) return _filtered; } }

        public void Start(bool operatorApproved)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(NativeOwnedCanSession));
                if (!operatorApproved)
                    throw new InvalidOperationException("Explicit operator consent required before native CAN.");
                if (_lease || _deviceOpened || _connected || _filtered)
                    throw new InvalidOperationException("Native channel already started or awaiting cleanup.");
                if (!_managedConnectionInactive())
                    throw new InvalidOperationException("Managed J2534-Sharp connection is active; native start refused.");
                if (Interlocked.CompareExchange(ref _sessionHeld, 1, 0) != 0)
                    throw new InvalidOperationException("Another native receive session owns the process gate.");
                _lease = true;
                try
                {
                    RequireSuccess(_api.Open(out _device), "PassThruOpen");
                    _deviceOpened = true;
                    RequireSuccess(_api.ConnectCan500k(_device, out _channel),
                        "PassThruConnect(CAN,flags=0,500000)");
                    _connected = true;
                    RequireSuccess(_api.StartWildcardPassFilter(_channel, out _filter),
                        "PassThruStartMsgFilter(PASS_FILTER)");
                    _filtered = true;
                }
                catch (Exception opening)
                {
                    try { StopInternal(); }
                    catch (Exception cleanup)
                    {
                        throw new AggregateException(
                            "Native startup and cleanup both failed; connection gate retained if needed.",
                            opening, cleanup);
                    }
                    throw;
                }
            }
        }

        public NativeCanReadResult ReadNative(int requestedMaximum, int timeoutMilliseconds)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(NativeOwnedCanSession));
                if (!_lease || !_deviceOpened || !_connected || !_filtered)
                    throw new InvalidOperationException("Native receive requires an opened, filtered, solely-owned channel.");
                if (requestedMaximum < 1 || requestedMaximum > 200)
                    throw new ArgumentOutOfRangeException(nameof(requestedMaximum));
                if (timeoutMilliseconds < 0)
                    throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
                return _api.ReadCan(_channel, requestedMaximum, timeoutMilliseconds);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_disposed) return;
                StopInternal();
            }
        }

        private void StopInternal()
        {
            var failures = new List<string>();
            if (_filtered)
            {
                try
                {
                    uint status = _api.StopFilter(_channel, _filter);
                    if (status == 0) _filtered = false;
                    else failures.Add("PassThruStopMsgFilter:0x" + status.ToString("X8"));
                }
                catch (Exception e) { failures.Add("PassThruStopMsgFilter:" + e.GetType().Name); }
            }
            if (_connected)
            {
                try
                {
                    uint status = _api.Disconnect(_channel);
                    if (status == 0)
                    {
                        _connected = false;
                        // A successful disconnect retires its associated filter.
                        _filtered = false;
                    }
                    else failures.Add("PassThruDisconnect:0x" + status.ToString("X8"));
                }
                catch (Exception e) { failures.Add("PassThruDisconnect:" + e.GetType().Name); }
            }
            if (_deviceOpened)
            {
                try
                {
                    uint status = _api.Close(_device);
                    if (status == 0) _deviceOpened = false;
                    else failures.Add("PassThruClose:0x" + status.ToString("X8"));
                }
                catch (Exception e) { failures.Add("PassThruClose:" + e.GetType().Name); }
            }
            if (!_deviceOpened && !_connected && !_filtered && _lease)
            {
                _lease = false;
                Interlocked.Exchange(ref _sessionHeld, 0);
            }
            if (failures.Count != 0)
                throw new InvalidOperationException(
                    "Native cleanup was not entirely successful: " + string.Join("; ", failures));
        }

        private static void RequireSuccess(uint result, string call)
        {
            if (result != 0)
                throw new InvalidOperationException(call + " returned 0x" +
                    result.ToString("X8") + "; no alternate settings attempted.");
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                StopInternal();
                _disposed = true;
            }
        }
    }
}
