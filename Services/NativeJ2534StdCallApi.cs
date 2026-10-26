using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace J2534Diag
{
    /// <summary>
    /// SAE J2534-1 PASSTHRU_MSG: six UINT32 headers followed by 4128 inline
    /// data bytes. Match independent OneApp native receiver structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativePassThruMsg
    {
        public uint ProtocolID;
        public uint RxStatus;
        public uint TxFlags;
        public uint Timestamp;
        public uint DataSize;
        public uint ExtraDataIndex;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
        public byte[] Data;
    }

    /// <summary>
    /// Explicitly constructed, Windows-only J2534 receive API owner.
    /// The application GUI does NOT reference or instantiate this adapter.
    /// Loads only seven receive/lifecycle exports; NO PassThruWriteMsgs.
    /// Caller must own the device exclusively and dispose only after Close.
    /// </summary>
    public sealed class NativeJ2534StdCallApi : INativeJ2534CanApi, IDisposable
    {
        private const uint CanProtocol = 0x05;
        private const uint PassFilter = 0x01;
        private const uint Baud500000 = 500000;
        private IntPtr _library;
        private readonly POpen _open;
        private readonly PClose _close;
        private readonly PConnect _connect;
        private readonly PDisconnect _disconnect;
        private readonly PStartFilter _start;
        private readonly PStopFilter _stop;
        private readonly PRead _read;
        private bool _deviceOpen, _channelOpen;

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr library);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint POpen(IntPtr name, out uint device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PClose(uint device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PConnect(uint device, uint protocol, uint flags,
            uint baudRate, out uint channel);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PDisconnect(uint channel);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PStartFilter(uint channel, uint type,
            ref NativePassThruMsg mask, ref NativePassThruMsg pattern,
            IntPtr flowControl, out uint filter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PStopFilter(uint channel, uint filter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint PRead(uint channel,
            [In, Out] NativePassThruMsg[] messages, ref uint count, uint timeout);

        public NativeJ2534StdCallApi(string verifiedDllAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(verifiedDllAbsolutePath) ||
                !Path.IsPathRooted(verifiedDllAbsolutePath))
                throw new ArgumentException("Require an explicit absolute J2534 driver DLL path.",
                    nameof(verifiedDllAbsolutePath));
            string path = Path.GetFullPath(verifiedDllAbsolutePath);
            if (!string.Equals(Path.GetExtension(path), ".dll",
                StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new FileNotFoundException("A verified J2534 driver DLL is required.", path);
            _library = LoadLibrary(path);
            if (_library == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot load J2534 DLL with current process bitness.");
            try
            {
                _open = Resolve<POpen>("PassThruOpen");
                _close = Resolve<PClose>("PassThruClose");
                _connect = Resolve<PConnect>("PassThruConnect");
                _disconnect = Resolve<PDisconnect>("PassThruDisconnect");
                _start = Resolve<PStartFilter>("PassThruStartMsgFilter");
                _stop = Resolve<PStopFilter>("PassThruStopMsgFilter");
                _read = Resolve<PRead>("PassThruReadMsgs");
            }
            catch
            {
                FreeLibrary(_library);
                _library = IntPtr.Zero;
                throw;
            }
        }

        private T Resolve<T>(string name) where T : class
        {
            IntPtr ptr = GetProcAddress(_library, name);
            if (ptr == IntPtr.Zero)
                throw new EntryPointNotFoundException("Required J2534 receive export missing: " + name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
        }

        public uint Open(out uint device)
        {
            EnsureLoaded();
            if (_deviceOpen) throw new InvalidOperationException("Device already opened by native API.");
            uint status = _open(IntPtr.Zero, out device);
            if (status == 0) _deviceOpen = true;
            return status;
        }

        public uint ConnectCan500k(uint device, out uint channel)
        {
            EnsureLoaded();
            if (!_deviceOpen || _channelOpen)
                throw new InvalidOperationException("Native CAN requires one open device and no existing channel.");
            // Same CAN=5, flags=0 and 500 kbps as independent verified OneApp.
            uint status = _connect(device, CanProtocol, 0U, Baud500000, out channel);
            if (status == 0) _channelOpen = true;
            return status;
        }

        public uint StartWildcardPassFilter(uint channel, out uint filter)
        {
            EnsureLoaded();
            if (!_channelOpen)
                throw new InvalidOperationException("Install filter only on an owned native CAN channel.");
            // 4-byte arbitration ID all-zero mask/pattern: wildcard receive-all.
            var mask = BlankFilterMessage();
            var pattern = BlankFilterMessage();
            return _start(channel, PassFilter, ref mask, ref pattern, IntPtr.Zero, out filter);
        }

        private static NativePassThruMsg BlankFilterMessage()
        {
            return new NativePassThruMsg
            {
                ProtocolID = CanProtocol,
                DataSize = 4,
                Data = new byte[4128]
            };
        }

        public NativeCanReadResult ReadCan(uint channel, int max, int timeout)
        {
            EnsureLoaded();
            if (!_channelOpen)
                throw new InvalidOperationException("Read requested without an active native CAN channel.");
            if (max < 1 || max > 200) throw new ArgumentOutOfRangeException(nameof(max));
            if (timeout < 0) throw new ArgumentOutOfRangeException(nameof(timeout));
            var buffer = new NativePassThruMsg[max];
            for (int i = 0; i < max; i++)
                buffer[i] = new NativePassThruMsg
                {
                    ProtocolID = CanProtocol, Data = new byte[4128]
                };
            uint nativeCount = (uint)max;
            uint result = _read(channel, buffer, ref nativeCount, (uint)timeout);
            // Preserve invalid/overlarge count to let the independent boundary
            // reject it; never read beyond the allocated array.
            int returned = (int)Math.Min(nativeCount, (uint)max);
            var frames = new List<CanReceivedFrame>(returned);
            for (int i = 0; i < returned; i++)
            {
                var item = buffer[i];
                int bytes = item.DataSize <= 4128U ? (int)item.DataSize : 0;
                var raw = new byte[bytes];
                if (bytes != 0 && item.Data != null && item.Data.Length >= bytes)
                    Array.Copy(item.Data, raw, bytes);
                frames.Add(new CanReceivedFrame
                {
                    Data = raw,
                    TimestampRaw = item.Timestamp,
                    RxStatusRaw = item.RxStatus
                });
            }
            return new NativeCanReadResult(result, nativeCount, frames);
        }

        public uint StopFilter(uint channel, uint filter)
        {
            EnsureLoaded();
            if (!_channelOpen) throw new InvalidOperationException("No channel available for filter cleanup.");
            return _stop(channel, filter);
        }

        public uint Disconnect(uint channel)
        {
            EnsureLoaded();
            if (!_channelOpen) throw new InvalidOperationException("No native channel to disconnect.");
            uint status = _disconnect(channel);
            if (status == 0) _channelOpen = false;
            return status;
        }

        public uint Close(uint device)
        {
            EnsureLoaded();
            if (!_deviceOpen) throw new InvalidOperationException("No native device to close.");
            uint status = _close(device);
            if (status == 0) _deviceOpen = false;
            return status;
        }

        private void EnsureLoaded()
        {
            if (_library == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(NativeJ2534StdCallApi));
        }

        public void Dispose()
        {
            if (_library == IntPtr.Zero) return;
            if (_deviceOpen || _channelOpen)
                throw new InvalidOperationException(
                    "Do not unload native J2534 DLL while device/channel may remain open.");
            if (!FreeLibrary(_library))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "FreeLibrary failed; native DLL handle is retained.");
            _library = IntPtr.Zero;
        }
    }
}
