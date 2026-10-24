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

        private void StopVinMonitorSafely()
        {
            ChannelLifecyclePolicy.RequireUnowned(ReceiveGate);
            if (!ChannelLifecyclePolicy.RequestStopAndWait(_monitorThread, _cts, 5000))
                throw new InvalidOperationException(
                    "VIN receive monitor has not stopped. J2534 connection is retained; retry after the monitor exits.");
            _monitorThread = null;
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// Close an idle J2534 connection. Refuses if any application capture
        /// or diagnostic reader still holds its receive lease.
        /// </summary>
        public void Disconnect()
        {
            StopVinMonitorSafely();
            CloseChannelAndDevice();
            _mainForm?.UpdateSelectedVehicle(false, null);
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
            StopVinMonitorSafely();
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

                if (Vin == null)
                {
                    Debug.WriteLine($"VIN not received for {VinMode} on {BitMode}.");
                    continue;
                }
                else if (Vin.Length == 17)
                {
                    Debug.WriteLine($"VIN: {Vin} (Time: {DateTime.Now:HH:mm:ss.fff}) via {VinMode} on {BitMode}.");
                    return;
                }
                else
                {
                    Debug.WriteLine($"VIN Incomplete: {Vin} (Time: {DateTime.Now:HH:mm:ss.fff}) via {VinMode} on {BitMode}, trying again.");
                    // Only allow 1 retry here
                    InitializeChannel(dllFileName);
                }
                if(!string.IsNullOrEmpty(Vin))
                {
                    return;
                }
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
            // Reuse identical shutdown conditions when switching modes,
            // reconnecting to the same DLL, or explicitly disconnecting.
            StopVinMonitorSafely();
            CloseChannelAndDevice();

            API pendingApi = null;
            Device pendingDevice = null;
            Channel pendingChannel = null;
            try
            {
                pendingApi = APIFactory.GetAPI(dllFileName);
                pendingDevice = pendingApi.GetDevice();
                pendingChannel = pendingDevice.GetChannel(
                    Protocol.CAN, Baud.CAN_500000,
                    bitMode == BitType.BITS_11 ? ConnectFlag.NONE : ConnectFlag.CAN_29BIT_ID);
                pendingChannel.DefaultTxFlag = bitMode == BitType.BITS_11
                    ? TxFlag.NONE : TxFlag.CAN_29BIT_ID;

                Api = pendingApi;
                Device = pendingDevice;
                Channel = pendingChannel;
                BitMode = bitMode;
                CaptureOnlyMode = true;
                CaptureOnlyChannelOpenedHostTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                // Intentionally no SendMessage, VIN request, flow control,
                // diagnostic poller, or background receive worker here.
            }
            catch
            {
                pendingChannel?.Dispose();
                pendingDevice?.Dispose();
                pendingApi?.Dispose();
                Channel = null;
                Device = null;
                Api = null;
                CaptureOnlyMode = false;
                throw;
            }
        }

        private void InitializeChannel(string dllFileName)
        {
            StopVinMonitorSafely();
            CloseChannelAndDevice();
            Api = APIFactory.GetAPI(dllFileName);
            Device = Api.GetDevice();

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
                            _isoTpParser.ProcessMessage(msg.Data);
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

        private void IsoTpParser_FrameReceived(object sender, IsoTpFrameEventArgs e)
        {
            switch (e.FrameType)
            {
                case IsoTpFrameType.SingleFrame:
                    // Handle single frame (e.Payload)
                    break;

                case IsoTpFrameType.FirstFrame:
                    // Insert a small delay to ensure timing is right for flow control
                    Thread.Sleep(2);

                    // Always send FC to the sender of the First Frame
                    SendFlowControl(
                        BitMode == BitType.BITS_11
                            ? Build11BitFlowControlId(e.CanId)
                            : BuildUdsFlowControlId(e.CanId),
                        blockSize: 0,
                        separationTime: 2
                    );
                    Thread.Sleep(2);

                    // Print partial VIN for debugging
                    Vin = ExtractPartialVin(e.Payload);
                    Debug.WriteLine($"VIN (partial): {Vin} (Time: {DateTime.Now:HH:mm:ss.fff})");

                    break;

                case IsoTpFrameType.ConsecutiveFrame:
                    string newPartialVin = ExtractPartialVin(e.Payload);
                    if (!string.IsNullOrEmpty(newPartialVin) && newPartialVin.Length > 16)
                    {
                        Vin = newPartialVin;
                        //mainForm.UpdateSelectedVehicle(newPartialVin, "", "");
                    }
                    break;

            }
        }

        string ExtractPartialVin(byte[] payload)
        {
            // Handle OBD2 VIN
            if (payload.Length >= 2 && payload[0] == 0x49 && payload[1] == 0x02)
            {
                int vinStart = 3;
                if (payload.Length > vinStart)
                {
                    int vinLen = Math.Min(17, payload.Length - vinStart);
                    if (vinLen > 0)
                    {
                        return System.Text.Encoding.ASCII.GetString(payload, vinStart, vinLen);
                    }
                }
            }
            // Handle UDS VIN
            else if (payload.Length >= 3 && payload[0] == 0x62 && payload[1] == 0xF1 && payload[2] == 0x90)
            {
                int vinStart = 3;
                if (payload.Length > vinStart)
                {
                    int vinLen = Math.Min(17, payload.Length - vinStart);
                    if (vinLen > 0)
                    {
                        return System.Text.Encoding.ASCII.GetString(payload, vinStart, vinLen);
                    }
                }
            }
            return string.Empty;
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
