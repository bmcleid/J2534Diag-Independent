using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using J2534Diag;
using SAE.J2534;

namespace J2534Diag.Tests
{
    /// <summary>
    /// Uses *real NuGet J2534-Sharp classes* with synthetic frame data.
    /// No J2534 DLL, device connection, or vehicle required.
    /// </summary>
    internal static class J2534CanFrameSourceTests
    {
        private sealed class OneBatchSource : ICanFrameBatchSource
        {
            private readonly IReadOnlyList<CanReceivedFrame> _frames;
            public OneBatchSource(IReadOnlyList<CanReceivedFrame> frames) { _frames = frames; }
            public IReadOnlyList<CanReceivedFrame> ReadBatch(int maximum, int timeout)
            {
                if (maximum != _frames.Count) throw new Exception("Incorrect fake batch limit");
                return _frames;
            }
        }

        private static int _assertions;
        private static void Check(bool valid, string reason)
        {
            ++_assertions;
            if (!valid) throw new Exception(reason);
        }

        public static int RunAll()
        {
            ProjectAndCloneActualNuGetObjects();
            PreserveBoundaryValuesEndToEnd();
            EmptyMessageResultsAndNullGuard();
            ClassifyActualNuGetOverflowCodes();
            return _assertions;
        }

        private static void ProjectAndCloneActualNuGetObjects()
        {
            byte[] source = { 0x00, 0x00, 0x07, 0xEA, 0x03, 0x62, 0xF1, 0x90 };
            var msg = new Message(source, (RxFlag)unchecked((int)0x80000100U), 0U);
            var response = new GetMessageResults(new[] { msg }, ResultCode.TIMEOUT);
            var frames = J2534CanFrameSource.ProjectBatch(response);
            Check(frames.Count == 1, "Frame lost in J2534 adapter");
            Check(frames[0].TimestampRaw.HasValue && frames[0].TimestampRaw.Value == 0, "Native timestamp zero lost");
            Check(frames[0].RxStatusRaw.HasValue && frames[0].RxStatusRaw.Value == 0x80000100U, "RxFlag high bit changed");
            Check(frames[0].ReadResult == "TIMEOUT", "J2534 read result changed");
            Check(!ReferenceEquals(frames[0].Data, source), "J2534 payload not cloned");
            source[4] = 0xFF;
            Check(frames[0].Data[4] == 0x03, "J2534 payload clone mutated");
        }

        private static void PreserveBoundaryValuesEndToEnd()
        {
            var msg = new Message(new byte[] {0, 0, 7, 0xE8, 3, 0x41, 0x0C, 0},
                RxFlag.NONE, uint.MaxValue);
            var response = new GetMessageResults(new[] { msg }, ResultCode.STATUS_NOERROR);
            var projected = J2534CanFrameSource.ProjectBatch(response);
            var pump = new CanCapturePump(new OneBatchSource(projected), 1,
                () => new DateTime(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc), null);
            var saved = new List<CanCaptureRecord>();
            pump.Run(CancellationToken.None, saved.Add);
            Check(saved.Count == 1, "End-to-end frame lost");
            Check(saved[0].J2534TimestampRaw == "4294967295", "Unsigned timestamp overflowed");
            Check(saved[0].J2534RxStatusHex == "0x00000000", "Zero flags omitted");
            Check(saved[0].J2534ReadResult == "STATUS_NOERROR", "Status result omitted");
            Check(saved[0].ArbId == "0x7E8", "CAN ID changed during projection");
            Check(saved[0].Data == "03-41-0C-00", "Payload changed during projection");
        }

        private static void ClassifyActualNuGetOverflowCodes()
        {
            var over = new J2534Exception(ResultCode.BUFFER_OVERFLOW, "Test only");
            var timeout = new J2534Exception(ResultCode.TIMEOUT, "Test only");
            var full = new J2534Exception(ResultCode.BUFFER_FULL, "TX side only");
            Check(J2534CanFrameSource.IsReceiveBufferOverflow(over),
                "Pinned library overflow ResultCode not recognized");
            Check(!J2534CanFrameSource.IsReceiveBufferOverflow(timeout),
                "Normal timeout treated as J2534 receive overflow");
            Check(!J2534CanFrameSource.IsReceiveBufferOverflow(full),
                "TX buffer-full incorrectly treated as receive overflow");
            Check(!J2534CanFrameSource.IsReceiveBufferOverflow(null),
                "Unknown exception classified as receive overflow");
        }

        private static void EmptyMessageResultsAndNullGuard()
        {
            var empty = new GetMessageResults(new Message[0], ResultCode.BUFFER_EMPTY);
            Check(J2534CanFrameSource.ProjectBatch(empty).Count == 0,
                "Empty read created a fabricated frame");
            try
            {
                J2534CanFrameSource.ProjectBatch(null);
                throw new Exception("Missing read-result argument was accepted");
            }
            catch (ArgumentNullException) { ++_assertions; }
        }
    }
}
