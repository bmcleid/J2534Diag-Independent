using System;
using System.Collections.Generic;
using System.Threading;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class NativeCanReadBoundaryTests
    {
        static int checks;
        static void Check(bool ok, string msg) { checks++; if (!ok) throw new Exception(msg); }
        sealed class Fake : INativeCanReadProvider
        {
            public NativeCanReadResult Result;
            public int Calls, Requested, Timeout;
            public NativeCanReadResult ReadNative(int max, int timeout)
            { Calls++; Requested = max; Timeout = timeout; return Result; }
        }
        static CanReceivedFrame Frame(byte value, uint t)
        { return new CanReceivedFrame { Data = new byte[]{0,0,1,0xF1,value},TimestampRaw=t,RxStatusRaw=0 }; }
        public static int RunAll()
        {
            var a = Frame(0x31,301);
            var source = new Fake { Result = new NativeCanReadResult(0,1,
                new[]{a,Frame(0x99,999)}) };
            var boundary = new NativeCanReadBoundary(source);
            var accepted = boundary.ReadBatch(200,5);
            Check(source.Calls == 1 && source.Requested == 200 && source.Timeout == 5,
                "Native request parameters were changed");
            Check(accepted.Count == 1 && accepted[0].Data[4] == 0x31,
                "Native returned count ignored or stale slot accepted");
            a.Data[4] = 0;
            Check(accepted[0].Data[4] == 0x31, "Native data not cloned");
            Check(accepted[0].ReadResult == "STATUS_NOERROR", "Native result lost");

            var partial = new NativeCanReadBoundary(new Fake { Result =
                new NativeCanReadResult(0x09,1,new[]{Frame(0x42,400)}) });
            Check(partial.ReadBatch(200,5)[0].ReadResult == "TIMEOUT",
                "Partial timeout dropped");

            var overflow = new Fake { Result = new NativeCanReadResult(0x12,2,
                new[]{Frame(0x51,501),Frame(0x52,502)}) };
            var pump = new CanCapturePump(new NativeCanReadBoundary(overflow),10000,
                () => new DateTime(2026,10,25,18,0,0,DateTimeKind.Utc),null);
            var rows = new List<CanCaptureRecord>();
            bool thrown = false;
            try { pump.Run(CancellationToken.None,rows.Add); }
            catch (CanReceiveBufferOverflowException) { thrown = true; }
            Check(thrown && rows.Count == 2 && pump.LastSummary.StoredFrames == 2,
                "Overflow partially returned rows lost or overflow hidden");
            Check(pump.LastSummary.DriverBufferOverflowEvents == 1 &&
                pump.LastSummary.OverflowFramesProvidedBySource == 2 &&
                pump.LastSummary.OverflowFramesAcceptedByPump == 2,
                "Native partial overflow accounting incorrect");
            Check(rows[0].J2534TimestampRaw == "501" &&
                rows[1].J2534ReadResult == "BUFFER_OVERFLOW",
                "Raw metadata lost");

            foreach (var r in new[] {
                new NativeCanReadResult(0x12,0,new[]{Frame(0xAA,2)}),
                new NativeCanReadResult(0x12,201,new CanReceivedFrame[201]),
                new NativeCanReadResult(0x12,200,new CanReceivedFrame[4]),
                new NativeCanReadResult(0x12,200,new CanReceivedFrame[200])
            })
            {
                var test = new NativeCanReadBoundary(new Fake { Result=r });
                try { test.ReadBatch(200,5); throw new Exception("Bad native result accepted"); }
                catch (CanReceiveBufferOverflowException ex)
                {
                    Check(ex.ReturnedFrames.Count == 0,
                        "Zero or bad returned count accepted synthetic data");
                }
            }

            var invalid = new CanReceivedFrame { Data=new byte[]{0x1},TimestampRaw=1,RxStatusRaw=0 };
            var reject = new CanCapturePump(new NativeCanReadBoundary(new Fake {
                Result = new NativeCanReadResult(0x12,2,new[]{invalid,Frame(0x61,601)})
            }),10,() => new DateTime(2026,10,25,18,0,0,DateTimeKind.Utc),null);
            rows.Clear();
            try { reject.Run(CancellationToken.None,rows.Add); }
            catch (CanReceiveBufferOverflowException) { }
            Check(rows.Count == 1 && reject.LastSummary.InvalidFrames == 1,
                "Malformed partial frame accepted");

            var unknown = new NativeCanReadBoundary(new Fake {
                Result=new NativeCanReadResult(0x99,1,new[]{Frame(1,1)}) });
            try { unknown.ReadBatch(200,5); throw new Exception("Unknown status accepted"); }
            catch (InvalidOperationException ex) {
                Check(ex.Message.Contains("Unknown"),"Unknown status not rejected");
            }
            return checks;
        }
    }
}
