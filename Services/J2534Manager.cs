// cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Threading;
using System.Windows.Forms;
using SAE.J2534;

namespace J2534Diag
{
    public enum BitType
    {
        BITS_11 = 0,
        BITS_29 = 1
    }

    public enum VinRequestMode
    {
        ObdMode09 = 0,
        UdsJ1979_2 = 1
    }

    public class J2534Manager: IDisposable
    {
        private static readonly byte[] ObdFunctionalRequestId11Bit = { 0x00, 0x00, 0x07, 0xDF };
        private static readonly byte[] ObdPhysicalRequestId11Bit = { 0x00, 0x00, 0x07, 0xE0 };
        private static readonly byte[] UdsVinRequestId29Bit = { 0x18, 0xDB, 0x33, 0xF1 };
        private static readonly byte[] UdsVinResponseMask29Bit = { 0xFF, 0xFF, 0xFF, 0x00 };
        private static readonly byte[] UdsVinResponsePattern29Bit = { 0x18, 0xDA, 0xF1, 0x00 };

        public ReceiveChannelGate ReceiveGate { get; } = new ReceiveChannelGate();

        public API Api { get; private set; }
        public Device Device { get; private set; }
        public Channel Channel { get; private set; }
        public List<APIInfo> Devices { get; private set; }
        public byte[] EcmRxId { get; private set; }
        public string Vin { get; private set; }
        public BitType BitMode { get; set; } = BitType.BITS_11; // Default to 11-bit mode
        public VinRequestMode VinMode { get; set; } = VinRequestMode.ObdMode09;
        // Application-level capture-only mode: no VIN, PID, routine, or
        // ISO-TP flow-control transmissions from this application's code.
        // A J2534 CAN channel may still electrically ACK frames.
        public bool CaptureOnlyMode { get; private set; }

        private IsoTpParser _isoTpParser;
        private Thread _monitorThread;
        private CancellationTokenSource _cts;
        private MainForm _mainForm;
        public J2534Manager(MainForm mainForm)
        {
            // Initialize J2534Manager here with your preferred DLL and settings
            Devices = APIFactory.GetAPIinfo().ToList();
            _isoTpParser = new IsoTpParser();
            _isoTpParser.FrameReceived += IsoTpParser_FrameReceived;
            _mainForm = mainForm;
            //var vciApi = vciApis.First(x => x.Name == "SuperGoose-Plus");
            //Initialize(vciApi.Filename, Protocol.CAN, Baud.CAN_500000, ConnectFlag.NONE);
        }

        // Distinct from ReceiveGate: this lease must cover the entire
        // managed device lifecycle, including reconnect and VIN monitoring.
        private bool _managedLeaseHeld;

        private void RequireManagedConnectionLease()
        {
            if (_managedLeaseHeld) return;
            if (!J2534ConnectionOwnership.TryAcquire(this, "J2534-Sharp managed connection"))
                throw new InvalidOperationException(
                    "J2534 connection already owned by " +
                    J2534ConnectionOwnership.CurrentOwner + ".");
            _managedLeaseHeld = true;
        }

        private void ReleaseManagedLeaseIfClosed()
        {
            if (!_managedLeaseHeld || Channel != null || Device != null ||
                Api != null || _monitorThread != null ||
                ReceiveGate.CurrentOwner != null)
                return;
            J2534ConnectionOwnership.Release(this);
            _managedLeaseHeld = false;
        }

        private NativeJ2534StdCallApi _nativeApi;
        private NativeDriverIdentity _nativeDriver;
        public NativeOwnedCanSession NativeSession { get; private set; }
        public bool NativeModeSelected => _nativeDriver != null;
        public bool NativeCaptureActive => NativeSession != null && NativeSession.IsReceiving;
        public bool HasConnectionOrSelection =>
            NativeModeSelected || NativeSession != null ||
            Channel != null || Device != null || Api != null || _managedLeaseHeld ||
            _nativeApi != null || _monitorThread != null || _cts != null;
        public NativeDriverIdentity SelectedNativeDriver => _nativeDriver;

        // PREPARE ONLY. The actual native channel is opened immediately
        // before CAN Listener starts reading, not minutes earlier on Connect.
        // Prevents pre-capture driver receive-queue accumulation.
        public NativeDriverIdentity PrepareNativeCaptureOnly(string registeredDriverPath)
        {
            if (HasConnectionOrSelection || ReceiveGate.CurrentOwner != null)
                throw new InvalidOperationException(
                    "Disconnect and fully stop the current J2534 connection before selecting a new receiver.");
            var identity = NativeJ2534DriverCheck.Inspect(registeredDriverPath);
            _nativeDriver = identity;
            CaptureOnlyMode = true;
            BitMode = BitType.BITS_11;
            return identity;
        }

        public void StartNativeCapture()
        {
            if (_nativeDriver == null)
                throw new InvalidOperationException("Choose native receiver and a verified DLL first.");
            if (ReceiveGate.CurrentOwner != "CAN_CAPTURE")
                throw new InvalidOperationException("Native start requires the CAN Listener receive lease.");
            if (NativeSession != null || _nativeApi != null)
                throw new InvalidOperationException("Prior native channel needs cleanup before restart.");
            // Verify that same *bytes* selected earlier still exist immediately
            // before opening a device. Not an OS/driver signature guarantee.
            var identity = NativeJ2534DriverCheck.Inspect(_nativeDriver.FullPath);
            if (!string.Equals(identity.Sha256, _nativeDriver.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "J2534 driver file changed since selection. Disconnect and reselect.");
            _nativeApi = new NativeJ2534StdCallApi(identity.FullPath);
            NativeSession = new NativeOwnedCanSession(_nativeApi,
                () => Channel == null && Device == null && Api == null && _monitorThread == null);
            try
            {
                NativeSession.Start(true);
            }
            catch
            {
                // Start() handles partial channel cleanup; never hide a failed
                // close by discarding a still-owned session.
                StopNativeCapture();
                throw;
            }
        }

        public void StopNativeCapture()
        {
            if (NativeSession != null)
            {
                NativeSession.Stop();    // Throws and retains ownership on unsafe cleanup.
                NativeSession.Dispose();
                NativeSession = null;
            }
            if (_nativeApi != null)
            {
                _nativeApi.Dispose();   // Does not unload while handles may remain.
                _nativeApi = null;
            }
        }

        private void StopVinMonitorSafely()
        {
            ChannelLifecyclePolicy.RequireUnowned(ReceiveGate);
            if (!ChannelLifecyclePolicy.RequestStopAndWait(_monitorThread, _cts, 5000))
                throw new InvalidOperationException(
                    "VIN receive monitor has not stopped. J2534 connection is retained; retry after the monitor exits.");
            _monitorThread = null;
            _cts?.Dispose();
            _cts = null;
            _isoTpParser?.Reset();
        }

        /// <summary>
        /// Close an idle J2534 connection. Refuses if any application capture
        /// or diagnostic reader still holds its receive lease.
        /// </summary>
        public string LastDisconnectOutcome { get; private set; } = "Not attempted";
        public event Action<string> DisconnectOutcomeChanged;
        private void ReportDisconnect(string outcome)
        {
            LastDisconnectOutcome = outcome;
            // An evidence/UI subscriber must never change driver cleanup behavior.
            var handlers = DisconnectOutcomeChanged;
            if (handlers != null) foreach (Action<string> handler in handlers.GetInvocationList())
                try { handler(outcome); } catch (Exception ex) { Debug.WriteLine(ex); }
        }
        public void Disconnect()
        {
            ReportDisconnect("Pending");
            try
            {
            StopVinMonitorSafely();
            if (NativeModeSelected || NativeSession != null || _nativeApi != null)
            {
                StopNativeCapture();    // Refuses to discard handles on failure.
                _nativeDriver = null;
                CaptureOnlyMode = false;
            }
            CloseChannelAndDevice();
            ReleaseManagedLeaseIfClosed();
            _mainForm?.UpdateSelectedVehicle(false, null);
                ReportDisconnect("Succeeded");
            }
            catch (Exception ex)
            {
                ReportDisconnect("Failed: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        }

        private void CloseChannelAndDevice()
        {
            // Preserve the remaining handles if a driver rejects a close:
            // never claim a successful disconnection prematurely.
            Channel?.Dispose();
            Channel = null;
            Device?.Dispose();
            Device = null;
            Api?.Dispose();
            Api = null;
            Vin = null;
            EcmRxId = null;
            CaptureOnlyMode = false;
        }

        public void Initialize(string dllFileName)
        {
            RequireManagedConnectionLease();  // Acquire BEFORE touching the driver.
            try
            {
            StopVinMonitorSafely();
            CloseChannelAndDevice();
            // A refused reconnect still belongs to the retained old connection.
            LastDisconnectOutcome = "Not attempted for current connection";
            CaptureOnlyMode = false;
            Vin = null;

            var attempts = new (BitType BitMode, VinRequestMode VinMode)[]
            {
                (BitType.BITS_11, VinRequestMode.ObdMode09),
                (BitType.BITS_11, VinRequestMode.UdsJ1979_2),
                (BitType.BITS_29, VinRequestMode.UdsJ1979_2)
            };

            foreach (var attempt in attempts)
            {
                BitMode = attempt.BitMode;
                VinMode = attempt.VinMode;
                InitializeChannel(dllFileName);

                if (Vin != null && Vin.Length == 17) return;
            }
            throw new InvalidOperationException("No complete VIN received. Active connection was closed; use capture-only explicitly if identification is unavailable.");
            }
            catch (Exception startup)
            {
                try
                {
                    StopVinMonitorSafely();
                    CloseChannelAndDevice();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("VIN startup and cleanup failed; connection ownership retained.", startup, cleanup);
                }
                finally { ReleaseManagedLeaseIfClosed(); }
                throw;
            }
        }

        /// <summary>
        /// Open CAN only for the independent CAN capture listener. Unlike
        /// Initialize(), this path does NOT send VIN requests, start a VIN
        /// monitor, run ISO-TP, or create any diagnostic transmit messages.
        /// Filters are installed later by CanListenerForm under its lease.
        /// Not guaranteed physically silent: J2534 CAN may ACK frames.
        /// </summary>
        public long CaptureOnlyChannelOpenedHostTicks { get; private set; }

        public void InitializeCaptureOnly(string dllFileName, BitType bitMode)
        {
            if (string.IsNullOrWhiteSpace(dllFileName))
                throw new ArgumentException("Select an installed J2534 DLL.", nameof(dllFileName));
            if (bitMode != BitType.BITS_11 && bitMode != BitType.BITS_29)
                throw new ArgumentOutOfRangeException(nameof(bitMode));
            RequireManagedConnectionLease();
            try
            {
                // Keep the same owner across a managed reconnect.
                StopVinMonitorSafely();
                CloseChannelAndDevice();
                LastDisconnectOutcome = "Not attempted for current connection";
                // Register each acquired handle immediately so that any
                // failed next step can be safely retried on cleanup.
                Api = APIFactory.GetAPI(dllFileName);
                Device = Api.GetDevice();
                Channel = Device.GetChannel(
                    Protocol.CAN, Baud.CAN_500000,
                    bitMode == BitType.BITS_11 ? ConnectFlag.NONE : ConnectFlag.CAN_29BIT_ID);
                Channel.DefaultTxFlag = bitMode == BitType.BITS_11
                    ? TxFlag.NONE : TxFlag.CAN_29BIT_ID;
                BitMode = bitMode;
                CaptureOnlyMode = true;
                CaptureOnlyChannelOpenedHostTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                // No diagnostic transmit, VIN monitor or background RX here.
            }
            catch (Exception startup)
            {
                // Never clear handle fields blindly if Dispose fails.
                try
                {
                    if (_monitorThread == null && ReceiveGate.CurrentOwner == null)
                        CloseChannelAndDevice();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(
                        "Capture-only initialization and cleanup both failed; ownership retained.",
                        startup, cleanup);
                }
                finally { ReleaseManagedLeaseIfClosed(); }
                throw;
            }
        }

        private void InitializeChannel(string dllFileName)
        {
            StopVinMonitorSafely();
            // One API/device lifetime covers the bounded VIN attempts. Only the
            // channel changes between address modes; no driver unload/reopen race.
            Channel?.Dispose();
            Channel = null;
            Vin = null;
            if (Api == null) Api = APIFactory.GetAPI(dllFileName);
            if (Device == null) Device = Api.GetDevice();

            EcmRxId = GetRequestCanId();
            Channel = Device.GetChannel(Protocol.CAN, Baud.CAN_500000, BitMode == BitType.BITS_11 ? ConnectFlag.NONE : ConnectFlag.CAN_29BIT_ID);

            MessageFilter passFilter = BitMode == BitType.BITS_11 ? new MessageFilter()
            {
                FilterType = Filter.PASS_FILTER,
                Mask = new byte[] { 0xF0, 0xFF, 0xFF, 0x00 },
                Pattern = new byte[] { 0x00, 0x00, 0x07, 0xD0 },
                FlowControl = ObdPhysicalRequestId11Bit,
                TxFlags = TxFlag.NONE
            } : new MessageFilter()
            {
                FilterType = Filter.PASS_FILTER,
                Mask = UdsVinResponseMask29Bit,
                Pattern = UdsVinResponsePattern29Bit,
                FlowControl = new byte[] { 0x00, 0x00, 0x00, 0x00 },
                TxFlags = TxFlag.CAN_29BIT_ID
            };

            Channel.DefaultTxFlag = BitMode == BitType.BITS_11 ? TxFlag.NONE : TxFlag.CAN_29BIT_ID;

            Channel.ClearMsgFilters();
            var filterId = Channel.StartMsgFilter(passFilter);

            _cts = new CancellationTokenSource(3000);

            //Start background CAN monitor
            _monitorThread = new Thread(() =>
            {
                while (!_cts.IsCancellationRequested && Channel != null && !Channel.IsDisposed)
                {
                    try
                    {
                        var resp = Channel?.GetMessages(200, 5);
                        //Debug.WriteLine($"Received {resp.Messages.Length} messages at {DateTime.Now:HH:mm:ss.fff}");
                        foreach (var msg in resp.Messages)
                        {
                            // Process immediately
                            ProcessVinMessage(msg.Data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in Vehicle Tab: {ex.Message}");
                        //StopListening();
                    }
                    Thread.Sleep(1);
                }
            })
            { IsBackground = true };

            _monitorThread.Start();

            // Send VIN request
            var vinRequest = new byte[12];
            if (BitMode == BitType.BITS_11)
            {
                var requestCanId = GetRequestCanId();
                Array.Copy(requestCanId, 0, vinRequest, 0, requestCanId.Length);

                if (VinMode == VinRequestMode.ObdMode09)
                {
                    vinRequest[4] = 0x02;
                    vinRequest[5] = 0x09;
                    vinRequest[6] = 0x02;
                }
                else
                {
                    vinRequest[4] = 0x03;
                    vinRequest[5] = 0x22;
                    vinRequest[6] = 0xF1;
                    vinRequest[7] = 0x90;
                }
            }
            else
            {
                Array.Copy(UdsVinRequestId29Bit, 0, vinRequest, 0, UdsVinRequestId29Bit.Length);
                vinRequest[4] = 0x03;
                vinRequest[5] = 0x22;
                vinRequest[6] = 0xF1;
                vinRequest[7] = 0x90;
            }
            try
            {
                SendDiagnosticMessage(vinRequest);
                Thread.Sleep(2); // Give some time for the request to be sent

                // Preserve the original VIN response window before shutdown.
                // Joining for up to three seconds allows the monitor to collect
                // a reply; a timed join alone does not prove thread exit.
                _monitorThread.Join(3000);
            }
            finally
            {
                // Even a failed transmit must stop the reader BEFORE removing
                // filters or attempting another J2534 channel connection.
                StopVinMonitorSafely();
                Channel.StopMsgFilter(filterId);
                Channel.ClearMsgFilters();
            }
        }

        // Central gate for all application-originated diagnostic frames.
        // This is independent of CAN electrical acknowledgements by the VCI.
        public void SendDiagnosticMessage(IEnumerable<byte> message)
        {
            DiagnosticTransmitPolicy.RequireActiveDiagnostics(CaptureOnlyMode);
            if (Channel == null || Channel.IsDisposed)
                throw new InvalidOperationException("A J2534 diagnostic channel must be connected before transmitting.");
            Channel.SendMessage(message);
        }

        public void SendFlowControl(byte[] canId, int blockSize = 0, int separationTime = 0)
        {
            DiagnosticTransmitPolicy.RequireActiveDiagnostics(CaptureOnlyMode);
            var fc = new byte[12];
            Array.Copy(canId, 0, fc, 0, 4);
            fc[4] = 0x30;

            //if(BitMode == BitType.BITS_29)
            //{
            //    fc[7] = 0x55;
            //    fc[8] = 0x55;
            //    fc[9] = 0x55;
            //    fc[10] = 0x55;
            //    fc[11] = 0x55;
            //}

            SendDiagnosticMessage(fc);

            //Debug.WriteLine($"Flow Control sent: {BitConverter.ToString(fc)} (Time: {DateTime.Now:HH:mm:ss.fff})");
        }

        private bool IsVinResponseId(byte[] canId)
        {
            if (canId == null || canId.Length != 4) return false;
            if (BitMode == BitType.BITS_29)
                return canId[0] == 0x18 && canId[1] == 0xDA && canId[2] == 0xF1;
            return canId[0] == 0 && canId[1] == 0 && canId[2] == 7 &&
                (VinMode == VinRequestMode.ObdMode09 ? canId[3] == 0xE8 :
                    canId[3] >= 0xE8 && canId[3] <= 0xEF);
        }

        private bool HasVinPrefix(byte[] payload)
        {
            return payload != null && payload.Length >= 3 &&
                (VinMode == VinRequestMode.ObdMode09 ?
                    payload[0] == 0x49 && payload[1] == 0x02 && payload[2] == 0x01 :
                    payload[0] == 0x62 && payload[1] == 0xF1 && payload[2] == 0x90);
        }

        private void ProcessVinMessage(byte[] data)
        {
            if (data == null || data.Length < 5 || data.Length > 12 ||
                !IsVinResponseId(data.Take(4).ToArray())) return;
            if ((data[4] >> 4) == 1)
            {
                // VIN prefix plus seventeen characters: unrelated FFs never trigger FC.
                if (data.Length != 12 || (((data[4] & 15) << 8) | data[5]) != 20 ||
                    !HasVinPrefix(data.Skip(6).ToArray()))
                {
                    _isoTpParser.Reset();
                    return;
                }
            }
            _isoTpParser.ProcessMessage(data);
        }

        private void IsoTpParser_FrameReceived(object sender, IsoTpFrameEventArgs e)
        {
            if (!IsVinResponseId(e.CanId) || !HasVinPrefix(e.Payload)) return;
            if (e.FrameType == IsoTpFrameType.FirstFrame)
            {
                SendFlowControl(BitMode == BitType.BITS_11 ? Build11BitFlowControlId(e.CanId) :
                    BuildUdsFlowControlId(e.CanId), blockSize: 0, separationTime: 2);
                return;
            }
            if (!e.IsComplete || e.Payload.Length != 20 || Vin != null) return;
            byte[] characters = e.Payload.Skip(3).ToArray();
            if (characters.Any(c => !((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')))) return;
            Vin = System.Text.Encoding.ASCII.GetString(characters);
        }

        private byte[] GetRequestCanId()
        {
            if (BitMode == BitType.BITS_29)
            {
                return UdsVinRequestId29Bit;
            }

            return VinMode == VinRequestMode.UdsJ1979_2
                ? ObdFunctionalRequestId11Bit
                : ObdPhysicalRequestId11Bit;
        }

        private static byte[] Build11BitFlowControlId(byte[] responseCanId)
        {
            if (responseCanId == null || responseCanId.Length < 4)
            {
                return ObdPhysicalRequestId11Bit;
            }

            return new byte[]
            {
                responseCanId[0],
                responseCanId[1],
                responseCanId[2],
                (byte)(responseCanId[3] - 0x08)
            };
        }

        private static byte[] BuildUdsFlowControlId(byte[] responseCanId)
        {
            if (responseCanId == null || responseCanId.Length < 4)
            {
                return UdsVinRequestId29Bit;
            }

            return new byte[]
            {
                responseCanId[0],
                responseCanId[1],
                responseCanId[3],
                responseCanId[2]
            };
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
