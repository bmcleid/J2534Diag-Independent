using System;
using System.Collections.Generic;
using System.Linq;
using J2534Diag;

public static class Mode6PolicyTests
{
    public static int Checks;
    public static void Check(bool condition, string message)
    { Checks++; if (!condition) throw new Exception(message); }
    public static byte[] Payload(byte mid = 0xA2, byte tid = 0x0C, byte units = 0x24, ushort value = 0)
    { return new byte[] { 0x46, mid, tid, units, (byte)(value >> 8), (byte)value, 0, 0, 0xFF, 0xFF }; }
    static byte[] Frame(uint source, params byte[] bytes)
    { return new[] { (byte)(source >> 24), (byte)(source >> 16), (byte)(source >> 8), (byte)source }.Concat(bytes).ToArray(); }
    public static int Run()
    {
        List<Mode6Result> records;
        Check(Mode6Response.TryRead(Payload(), out records) && records.Count == 1 &&
            records[0].IsReferenceMisfireCount && records[0].RawValue == 0 && records[0].RawMaximum == 65535,
            "Measured zero or raw limits lost");
        Check(Mode6Response.TryRead(Payload(value: 65535), out records) && records[0].RawValue == 65535, "Unsigned high count");
        foreach (int size in Enumerable.Range(0, 10))
            Check(!Mode6Response.TryRead(Payload().Take(size).ToArray(), out records), "Truncation accepted: " + size);
        Check(!Mode6Response.TryRead(null, out records), "Null payload");
        Check(!Mode6Response.TryRead(new byte[] {0x46,0,0,0,0,0}, out records), "Support bitmap mistaken for a result");
        Check(!Mode6Response.TryRead(new byte[] {0x7F,6,0x12}, out records), "Negative reply mistaken for measurement");
        Check(Mode6Response.TryRead(Payload(units: 0x25), out records) && !records[0].IsReferenceMisfireCount, "Unknown unit scaled");
        Check(Mode6Response.TryRead(Payload(mid: 0x80), out records) == false, "Support-page MID used as result");
        Check(Mode6Response.TryRead(Payload(tid: 0xFF), out records) && !records[0].IsReferenceMisfireCount, "Unknown test decoded as counts");
        var two = Payload(value: 5).Concat(Payload(mid: 0xA3, value: 9).Skip(1)).ToArray();
        Check(Mode6Response.TryRead(two, out records) && records.Count == 2 && records[1].RawValue == 9, "Second record framing");
        Check(!Mode6Response.TryRead(two.Take(two.Length-1).ToArray(), out records) && records.Count == 0, "Partial reply published prefix");
        uint source; byte[] fc;
        for (uint id = 0x7E8; id <= 0x7EF; id++)
            Check(Mode6Response.TrySource(Frame(id, 3,0x46,0xA2,0),0,false,out source,out fc) && source == id && fc[3] == (byte)(id-8),
                "11-bit response/flow-control source");
        Check(!Mode6Response.TrySource(Frame(0x7E0,3,0x46,0xA2,0),0,false,out source,out fc), "Request echo accepted");
        Check(Mode6Response.TrySource(Frame(0x18DAF110,3,0x46,0xA2,0),0x100,true,out source,out fc) &&
            fc.SequenceEqual(new byte[] {0x18,0xDA,0x10,0xF1}), "29-bit source route");
        Check(!Mode6Response.TrySource(Frame(0x18DAF110,3,0x46,0xA2,0),0,true,out source,out fc), "Extended status mismatch");
        foreach (uint flags in new uint[] {1,2,4,8,0x400000})
            Check(!Mode6Response.TrySource(Frame(0x7E8,3,0x46,0xA2,0),flags,false,out source,out fc), "Indication accepted");
        Check(!Mode6Response.TrySource(new byte[13],0,false,out source,out fc), "Oversize classical frame");
        Check(!Mode6Response.TrySource(null,0,false,out source,out fc), "Null frame");

        // Actual shared reassembler with interleaved sources and Mode06 payloads.
        var parser = new IsoTpParser(); var completed = new Dictionary<byte, byte[]>();
        parser.FrameReceived += (s,e) => { if(e.IsComplete) completed[e.CanId[3]] = e.Payload; };
        parser.ProcessMessage(Frame(0x7E8,0x10,10,0x46,0xA2,0x0C,0x24,0,7));
        parser.ProcessMessage(Frame(0x7E9,0x10,10,0x46,0xA2,0x0C,0x24,0,9));
        parser.ProcessMessage(Frame(0x7E8,0x21,0,0,0xFF,0xFF,0,0,0));
        parser.ProcessMessage(Frame(0x7E9,0x21,0,0,0xFF,0xFF,0,0,0));
        Check(completed.Count == 2 && Mode6Response.TryRead(completed[0xE8],out records) && records[0].RawValue == 7,
            "Interleaved Mode06 source corrupted");
        Check(Mode6Response.TryRead(completed[0xE9],out records) && records[0].RawValue == 9, "Second source corrupted");
        completed.Clear(); parser.Reset();
        parser.ProcessMessage(Frame(0x7E8,0x10,10,0x46,0xA2,0x0C,0x24,0,7));
        parser.ProcessMessage(Frame(0x7E8,0x22,0,0,0xFF,0xFF,0,0,0));
        Check(completed.Count == 0, "Out-of-sequence Mode06 reply completed");
        Console.WriteLine("PASS Mode06 model checks=" + Checks);
        return Checks;
    }
}
