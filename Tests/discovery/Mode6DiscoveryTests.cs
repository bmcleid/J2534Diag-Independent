using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using J2534Diag;

public static class Mode6DiscoveryTests
{
    public static int Checks;
    public static void Check(bool value, string message) { Checks++; if (!value) throw new Exception(message); }
    public static byte[] Bitmap(byte page, uint bits) => new byte[] {0x46,page,(byte)(bits>>24),(byte)(bits>>16),(byte)(bits>>8),(byte)bits};
    public static byte[] Result(byte monitor, ushort value) => new byte[] {0x46,monitor,0x0C,0x24,(byte)(value>>8),(byte)value,0,0,0xFF,0xFF};
    public static byte[] Enabled() => new byte[] {0xA2,0xA3,0xA9,0x01};
    public static bool Immediate(int ms, CancellationToken token) => token.IsCancellationRequested;
    public static int Run()
    {
        byte page; uint bits;
        Check(Mode6DiscoverySession.TryBitmap(Bitmap(0xA0,0x40800001),out page,out bits) && page==0xA0 && bits==0x40800001,
            "Big-endian A2/A9/continuation vector");
        for(int bit=0;bit<32;bit++)
            Check(Mode6DiscoverySession.TryBitmap(Bitmap(0,1u<<bit),out page,out bits) && bits==(1u<<bit),"Availability bit lost "+bit);
        Check(!Mode6DiscoverySession.TryBitmap(Bitmap(1,0),out page,out bits),"Nonavailability MID accepted as bitmap");
        Check(!Mode6DiscoverySession.TryBitmap(Bitmap(0,1).Take(5).ToArray(),out page,out bits),"Truncated bitmap");
        Check(!Mode6DiscoverySession.TryBitmap(Bitmap(0,1).Concat(new byte[]{0}).ToArray(),out page,out bits),"Extra byte accepted");
        Check(!Mode6DiscoverySession.TryBitmap(Bitmap(0xE0,1),out page,out bits),"E0 reserved continuation bit");
        Check(Mode6DiscoverySession.TryBitmap(Bitmap(0xE0,2),out page,out bits),"FF final support bit rejected");
        List<Mode6Result> ignored;
        Check(!Mode6Response.TryRead(Bitmap(0,0),out ignored),"Discovery became measurement");
        Check(!Mode6DiscoverySession.TryBitmap(Result(0xA2,0),out page,out bits),"Measurement became discovery");

        var session=new Mode6DiscoverySession(); var sent=new List<Mode6Request>(); int results=0;
        session.Results+=(source,records)=>{Check(source==0x7E8 && records[0].RawValue==0,"Wrong-source or zero result");results++;};
        session.Run(q=>
        {
            sent.Add(q);
            if(!q.ResponseSource.HasValue)
            {session.Accept(0x7E8,Bitmap(0,1));session.Accept(0x7E9,Bitmap(0,0x80000000));}
            else if(q.IsAvailability)
            {
                Check(!session.Accept(0x7E9,Bitmap(q.MonitorId,0xFFFFFFFF)),"Cross-source continuation admitted");
                Check(!session.Accept(0x7E8,Bitmap((byte)(q.MonitorId+32),0xFFFFFFFF)),"Wrong page admitted");
                session.Accept(0x7E8,Bitmap(q.MonitorId,q.MonitorId==0xA0?0x40800000u:1u));
            }
            else
            {
                Check(q.ResponseSource==0x7E8 && (q.MonitorId==0xA2||q.MonitorId==0xA9),"Unadvertised monitor queried");
                Check(session.AllowsFirstFrame(0x7E8,q.MonitorId) && !session.AllowsFirstFrame(0x7E9,q.MonitorId),"Flow-control permission cross-source");
                Check(!session.Accept(0x7E9,Result(q.MonitorId,99)),"Wrong responder measurement admitted");
                session.Accept(0x7E8,Result(q.MonitorId,0));
            }
        },Immediate,Enabled,CancellationToken.None);
        Check(results==2 && sent.Count==8,"Discovery/result request count");
        Check(sent.Select(q=>q.MonitorId).SequenceEqual(new byte[]{0,0x20,0x40,0x60,0x80,0xA0,0xA2,0xA9}),"Continuation chain/order");
        Check(sent[0].ToCanFrame(false).Take(7).SequenceEqual(new byte[]{0,0,7,0xDF,2,6,0}),"Functional discovery wire bytes");
        Check(sent.Last().ToCanFrame(false)[3]==0xE0,"Result must be physically addressed");
        Check(session.SupportFor(0x7E8,0xA2)==Mode6SupportState.Advertised && session.SupportFor(0x7E8,0xA9)==Mode6SupportState.Advertised,"Bit order A2/A9");
        Check(session.SupportFor(0x7E8,0xA3)==Mode6SupportState.NotAdvertised && session.SupportFor(0x7E9,0xA2)==Mode6SupportState.NotAdvertised,"Negative support inference or responder isolation");
        Check(!session.Accept(0x7E8,Bitmap(0,0xFFFFFFFF)),"Late response after session end");

        var timeout=new Mode6DiscoverySession(); int timeoutSent=0;
        timeout.Run(q=>{timeoutSent++;if(!q.ResponseSource.HasValue)timeout.Accept(0x7E8,Bitmap(0,1));},Immediate,Enabled,CancellationToken.None);
        Check(timeoutSent==2 && timeout.SupportFor(0x7E8,0xA2)==Mode6SupportState.Inconclusive,"Timeout fabricated unsupported result");
        var negative=new Mode6DiscoverySession();
        negative.Run(q=>negative.Accept(0x7E8,new byte[]{0x7F,6,0x12}),Immediate,Enabled,CancellationToken.None);
        Check(negative.Requests==1 && negative.Sources[0].Status.Contains("Negative") && negative.SupportFor(0x7E8,0xA2)==Mode6SupportState.Inconclusive,"Negative response handling");
        var pending=new Mode6DiscoverySession();
        pending.Run(q=>pending.Accept(0x7E8,new byte[]{0x7F,6,0x78}),Immediate,Enabled,CancellationToken.None);
        Check(pending.Requests==1 && pending.SupportFor(0x7E8,0xA2)==Mode6SupportState.Inconclusive,"Response-pending extended budget or fabricated support");
        var malformed=new Mode6DiscoverySession();
        malformed.Run(q=>malformed.Accept(0x7E8,Bitmap(0,1).Take(5).ToArray()),Immediate,Enabled,CancellationToken.None);
        Check(malformed.Sources.Length==0 && malformed.Requests==1 && malformed.Status.Contains("unknown"),"Malformed root became support");
        var duplicate=new Mode6DiscoverySession();
        duplicate.Run(q=>{duplicate.Accept(0x7E8,Bitmap(0,0));Check(!duplicate.Accept(0x7E8,Bitmap(0,1)),"Conflicting duplicate overwrote accepted bitmap");},Immediate,Enabled,CancellationToken.None);
        Check(duplicate.Requests==1 && duplicate.IgnoredResponses==1,"Duplicate changed continuation chain");
        var cancel=new Mode6DiscoverySession();using(var cts=new CancellationTokenSource())
        {
            cancel.Run(q=>{cancel.Accept(0x7E8,Bitmap(0,1));cts.Cancel();},Immediate,Enabled,cts.Token);
            Check(cancel.Requests==1 && cancel.Status.Contains("Cancelled"),"Cancellation did not stop continuation");
            Check(!cancel.Accept(0x7E8,Bitmap(0x20,0xFFFFFFFF)),"Cancelled session admitted late result");
        }
        var preCancelled=new Mode6DiscoverySession();using(var cts=new CancellationTokenSource())
        {cts.Cancel();preCancelled.Run(q=>{throw new Exception("Unexpected send");},Immediate,Enabled,cts.Token);Check(preCancelled.Requests==0,"Pre-cancelled send");}
        var fresh=new Mode6DiscoverySession();fresh.Run(q=>{},Immediate,Enabled,CancellationToken.None);
        Check(fresh.Sources.Length==0 && fresh.SupportFor(0x7E8,0xA2)==Mode6SupportState.Undiscovered,"Restart inherited old support");
        var stop=new Mode6DiscoverySession();stop.Stop();stop.Run(q=>{throw new Exception("Stopped send");},Immediate,Enabled,CancellationToken.None);Check(stop.Requests==0,"Stopped before Run");
        bool reused=false;try{fresh.Run(q=>{},Immediate,Enabled,CancellationToken.None);}catch(InvalidOperationException){reused=true;}Check(reused,"Reuse stale session");
        long now=0;var deadline=new Mode6DiscoverySession(false,()=>now);
        deadline.Run(q=>deadline.Accept(0x7E8,Bitmap(0,1)),(ms,token)=>{now=Mode6DiscoverySession.MaximumMilliseconds;return false;},Enabled,CancellationToken.None);
        Check(deadline.Requests==1 && deadline.Status.Contains("limit"),"Scheduling deadline ignored");
        var cap=new Mode6DiscoverySession(true);
        cap.Run(q=>
        {
            if(!q.ResponseSource.HasValue)for(uint id=0x18DAF110;id<=0x18DAF11F;id++)cap.Accept(id,Bitmap(0,0xFFFFFFFF));
            else if(q.IsAvailability)cap.Accept(q.ResponseSource.Value,Bitmap(q.MonitorId,q.MonitorId==0xE0?0xFFFFFFFEu:0xFFFFFFFFu));
        },Immediate,()=>Enumerable.Range(0xA2,8).Select(i=>(byte)i).ToArray(),CancellationToken.None);
        Check(cap.Requests==Mode6DiscoverySession.MaximumRequests && cap.Status.Contains("limit"),"Request cap ignored");
        Check(new Mode6Request(0x18DAF110,0xA2).ToCanFrame(true).Take(7).SequenceEqual(new byte[]{0x18,0xDA,0x10,0xF1,2,6,0xA2}),"29-bit physical request route");
        var throwing=new Mode6DiscoverySession();bool failed=false;
        try{throwing.Run(q=>{throw new IOException("fake send failure");},Immediate,Enabled,CancellationToken.None);}catch(IOException){failed=true;}
        Check(failed && !throwing.Accept(0x7E8,Bitmap(0,1)),"Send failure leaves accepting session");
        Console.WriteLine("PASS discovery model checks="+Checks);return Checks;
    }
}
