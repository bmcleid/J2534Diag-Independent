using System;
using System.Collections.Generic;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanFrameClassifierTests
    {
        private static int _checks;
        private static void Check(bool valid, string reason)
        {
            ++_checks;
            if (!valid) throw new Exception(reason);
        }

        public static int RunAll()
        {
            IgnoreJ2534ControlAndIndicationFlags();
            AcceptStandardAndExtendedCanOnlyWithMatchingStatus();
            RejectBadHeadersAndOversizePayloads();
            MaintainDistinctPumpRejectionCounters();
            ReturnAcceptedBusDataOnly();
            return _checks;
        }

        private static CanReceivedFrame Raw(uint id, uint? status, params byte[] payload)
        {
            var bytes = new byte[4 + payload.Length];
            bytes[0] = (byte)(id >> 24);
            bytes[1] = (byte)(id >> 16);
            bytes[2] = (byte)(id >> 8);
            bytes[3] = (byte)id;
            Array.Copy(payload, 0, bytes, 4, payload.Length);
            return new CanReceivedFrame { Data = bytes, RxStatusRaw = status, ReadResult = "STATUS_NOERROR" };
        }

        private static void IgnoreJ2534ControlAndIndicationFlags()
        {
            foreach (uint bit in new uint[] { 1, 2, 4, 8 })
            {
                var msg = Raw(0x7EA, bit, 0x01);
                Check(!CanFrameClassifier.TryValidate(msg, out _, out var reason),
                    "Status/indication record was treated as bus frame, bit=" + bit);
                Check(reason == CanFrameRejectionKind.J2534Indication,
                    "Incorrect indication classification, bit=" + bit);
            }
            var highBit = Raw(0x7EA, 0x80000001U, 0x01);
            Check(!CanFrameClassifier.TryValidate(highBit, out _, out var kind) &&
                kind == CanFrameRejectionKind.J2534Indication,
                "Unknown status high bits must not mask known TX indication");
        }

        private static void AcceptStandardAndExtendedCanOnlyWithMatchingStatus()
        {
            Check(CanFrameClassifier.TryValidate(Raw(0x7EA, 0x00000000U, 0x01),
                out var standardId, out var standardStatus) &&
                standardId == 0x7EA && standardStatus == CanFrameRejectionKind.None,
                "Known standard CAN ID was rejected");
            Check(CanFrameClassifier.TryValidate(Raw(0x7E8, null),
                out var noFlagId, out _) && noFlagId == 0x7E8,
                "Status-unavailable standard frame should remain eligible");
            Check(CanFrameClassifier.TryValidate(Raw(0x18DAF111, 0x00000100, 0x03),
                out var extendedId, out _) && extendedId == 0x18DAF111,
                "J2534 CAN_29BIT_ID frame incorrectly rejected");
            Check(!CanFrameClassifier.TryValidate(Raw(0x18DAF111, 0, 0x03),
                out _, out var missing29Bit) &&
                missing29Bit == CanFrameRejectionKind.ArbitrationIdOutOfRange,
                "Extended CAN ID without status bit was misidentified as standard");
            Check(!CanFrameClassifier.TryValidate(Raw(0x800, 0, 0x03),
                out _, out var tooLargeStandard) &&
                tooLargeStandard == CanFrameRejectionKind.ArbitrationIdOutOfRange,
                "11-bit arbitration-ID limit not enforced");
            Check(!CanFrameClassifier.TryValidate(Raw(0x20000000, 0x100, 0x03),
                out _, out var tooLargeExtended) &&
                tooLargeExtended == CanFrameRejectionKind.ArbitrationIdOutOfRange,
                "29-bit arbitration-ID limit not enforced");
        }

        private static void RejectBadHeadersAndOversizePayloads()
        {
            Check(!CanFrameClassifier.TryValidate(null, out _, out var missing) &&
                missing == CanFrameRejectionKind.MissingOrShortHeader, "Null frame accepted");
            Check(!CanFrameClassifier.TryValidate(new CanReceivedFrame
                { Data = new byte[] { 0x00, 0x01 } }, out _, out var shortKind) &&
                shortKind == CanFrameRejectionKind.MissingOrShortHeader, "Short header accepted");
            Check(!CanFrameClassifier.TryValidate(new CanReceivedFrame
                { Data = null }, out _, out var nullBuffer) &&
                nullBuffer == CanFrameRejectionKind.MissingOrShortHeader, "Null buffer accepted");
            Check(!CanFrameClassifier.TryValidate(Raw(0x7E8, 0, new byte[9]),
                out _, out var oversize) &&
                oversize == CanFrameRejectionKind.ExceedsClassicalCanLength,
                "Nine-byte nonclassical payload silently accepted");
            Check(CanFrameClassifier.TryValidate(Raw(0x7E8, 0, new byte[8]),
                out _, out _), "Eight-byte classical payload rejected");
        }

        private sealed class OneBatch : ICanFrameBatchSource
        {
            private readonly IReadOnlyList<CanReceivedFrame> _frames;
            public OneBatch(params CanReceivedFrame[] frames) { _frames = frames; }
            public IReadOnlyList<CanReceivedFrame> ReadBatch(int requestedMaximum, int timeoutMilliseconds)
            {
                return _frames;
            }
        }

        private static void MaintainDistinctPumpRejectionCounters()
        {
            var source = new OneBatch(
                Raw(0x7EA, 0, 0x01),
                Raw(0x7EA, 1, 0x02),
                new CanReceivedFrame { Data = new byte[] { 0x01 } },
                Raw(0x7E8, 0, new byte[9]),
                Raw(0x800, 0, 0x03),
                Raw(0x7E8, 0, 0x04));
            var stored = new List<CanCaptureRecord>();
            var pump = new CanCapturePump(source, 2,
                () => new DateTime(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc), null);
            var result = pump.Run(CancellationToken.None, stored.Add);
            Check(result.StoredFrames == 2 && stored.Count == 2, "Accepted CAN count wrong");
            Check(result.IgnoredIndicationRecords == 1, "Status indicator incorrectly counted");
            Check(result.InvalidFrames == 3, "Malformed frame total incorrectly counted");
            Check(result.RejectedMissingOrShortRecords == 1, "Short frame was not classified");
            Check(result.RejectedOversizeRecords == 1, "Oversize payload was not classified");
            Check(result.RejectedArbitrationIds == 1, "Invalid arbitration ID not classified");
            Check(result.ExcessReturnedFrames == 0, "Rejected data incorrectly consumed the frame cap");
            Check(stored[0].ArbId == "0x7EA" && stored[1].ArbId == "0x7E8",
                "Rejected records changed accepted CAN order");
        }

        private static void ReturnAcceptedBusDataOnly()
        {
            var source = new OneBatch(
                Raw(0x7EA, 2, 0x11),
                Raw(0x7EA, 0x100, 0x12),
                Raw(0x7EA, 0, 0x13));
            var saved = new List<CanCaptureRecord>();
            var result = new CanCapturePump(source, 1,
                () => new DateTime(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc), null)
                .Run(CancellationToken.None, saved.Add);
            Check(result.IgnoredIndicationRecords == 1, "START_OF_MESSAGE not excluded");
            Check(result.StoredFrames == 1 && saved[0].Data == "12",
                "Valid extended-status CAN payload not recorded");
            Check(result.ExcessReturnedFrames == 1,
                "Actual extra returned frame past cap should be accounted");
        }
    }
}
