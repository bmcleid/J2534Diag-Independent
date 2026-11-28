using System;
using J2534Diag;
namespace J2534Diag.Tests
{
    internal static class FuelTrimSampleStateTests
    {
        private static int _checks;
        private static void Check(bool ok, string message)
        {
            _checks++;
            if (!ok) throw new Exception(message);
        }
        private static byte[] Frame(uint id, params byte[] payload)
        {
            var data = new byte[4 + payload.Length];
            data[0] = (byte)(id >> 24); data[1] = (byte)(id >> 16);
            data[2] = (byte)(id >> 8); data[3] = (byte)id;
            Array.Copy(payload, 0, data, 4, payload.Length);
            return data;
        }
        public static int RunAll()
        {
            FuelTrimInput input;
            var rpm = Frame(0x7E8, 4, 0x41, 0x0C, 0x0F, 0xA0);
            Check(FuelTrimScalarFrame.TryRead(rpm, 0, out input) &&
                input.Pid == 0x0C && input.Data.Length == 2 && input.SourceKey.Contains("0x7E8"),
                "Valid RPM/source lost");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 4, 0x41, 0x0C, 0x0F), 0, out input),
                "Truncated RPM accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 3, 0x41, 0x0C, 0x0F, 0), 0, out input),
                "Short declared RPM length accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7DF, 3, 0x41, 6, 128), 0, out input),
                "Request source accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 3, 0x7F, 1, 0x12), 0, out input),
                "Negative response accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 0x10, 9, 0x41, 6, 128, 0, 0, 0), 0, out input),
                "Segmented first frame accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E9, 0x22, 0x41, 6, 128), 0, out input),
                "Out-of-order/cross-source CF accepted");
            Check(!FuelTrimScalarFrame.TryRead(rpm, 1, out input), "Transmit indication accepted");
            Check(!FuelTrimScalarFrame.TryRead(rpm, 0x00400000, out input), "Unknown status accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 3, 0x41, 0x55, 0), 0, out input),
                "Unknown PID accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 0, 0x41, 6, 128), 0, out input),
                "Zero PCI accepted");
            Check(FuelTrimScalarFrame.TryRead(Frame(0x18DAF110, 3, 0x41, 6, 128), 0x100, out input),
                "29-bit response lost");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x18DA10F1, 3, 0x41, 6, 128), 0x100, out input),
                "29-bit request direction accepted");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x18DAF110, 3, 0x41, 6, 128), 0, out input),
                "Extended header without flag accepted");
            Check(FuelTrimScalarFrame.TryRead(Frame(0x7E8, 4, 0x62, 0xF4, 6, 128), 0, out input) &&
                input.SourceKey.Contains("mapping unverified"), "UDS provenance missing");
            Check(!FuelTrimScalarFrame.TryRead(Frame(0x7E8, 4, 0x62, 0x19, 6, 128), 0, out input),
                "Unmapped DID accepted");
            Check(FuelTrimScalarFrame.TryRead(Frame(0x7E8, 3, 0x41, 6, 128, 0, 0, 0, 0), 0, out input),
                "Padded scalar rejected");

            var state = new FuelTrimSampleState(20);
            double throttle, speed, total;
            Check(!state.Snapshot("A", 0).Value(6).HasValue, "Missing value became zero");
            Check(!state.Snapshot("A", 0).TryBank(1, out throttle, out speed, out total),
                "Empty bank became complete");
            Check(state.Update("A", 6, 0, 10), "Real zero rejected");
            Check(state.Update("A", 7, 0, 10), "Second real zero rejected");
            Check(!state.Snapshot("A", 10).TryBank(1, out throttle, out speed, out total),
                "Missing throttle/RPM accepted");
            state.Update("A", 0x11, 0, 10); state.Update("A", 0x0C, 0, 10);
            Check(state.Snapshot("A", 10).TryBank(1, out throttle, out speed, out total) && total == 0,
                "Complete measured zero invalid");
            Check(!state.Snapshot("A", 10).TryBank(2, out throttle, out speed, out total),
                "Absent bank2 fabricated");
            state.Update("B", 8, 12, 10); state.Update("B", 9, 3, 10);
            Check(!state.Snapshot("A", 10).Value(8).HasValue &&
                !state.Snapshot("B", 10).TryBank(2, out throttle, out speed, out total),
                "Responder values mixed");
            state.Update("A/UDS", 8, 12, 10); state.Update("A/UDS", 9, 3, 10);
            Check(!state.Snapshot("A", 10).TryBank(2, out throttle, out speed, out total),
                "Protocols mixed");
            Check(state.Snapshot("A", 30).Value(6).HasValue, "Age boundary wrong");
            Check(!state.Snapshot("A", 31).Value(6).HasValue &&
                !state.Snapshot("A", 31).TryBank(1, out throttle, out speed, out total), "Stale values accepted");
            Check(!state.Update("A", 6, 5, 9) && state.Snapshot("A", 10).Value(6) == 0,
                "Older sample replaced fresh value");
            Check(!state.Snapshot("A", 9).Value(6).HasValue, "Future sample accepted");
            Check(!state.Update("A", 6, double.NaN, 11) &&
                !state.Update("A", 6, double.PositiveInfinity, 11), "Nonfinite input accepted");
            Check(!state.Update("A", 0x11, -1, 11) && !state.Update("A", 0x0C, -1, 11),
                "Invalid bin input accepted");
            state.Update("A", 8, -2, 10);
            Check(!state.Snapshot("A", 10).TryBank(2, out throttle, out speed, out total), "Half bank2 accepted");
            state.Update("A", 9, 4, 10);
            Check(state.Snapshot("A", 10).TryBank(2, out throttle, out speed, out total) && total == 2,
                "Complete independent bank2 wrong");
            state.Clear();
            Check(!state.Snapshot("A", 10).Value(6).HasValue &&
                !state.Snapshot("B", 10).Value(8).HasValue, "Session reset leaked samples");
            return _checks;
        }
    }
}
