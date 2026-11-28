using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using J2534Diag;

public static class DiscoveryUiTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object obj,string field)=>obj.GetType().GetField(field,Flags).GetValue(obj);
    static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Flags).SetValue(obj,value);
    static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,Flags).Invoke(obj,args);
    static void Check(bool value,string message){Mode6DiscoveryTests.Check(value,message);Console.WriteLine("PASS "+message);}
    static void Receive(MisfireForm form,uint source,byte[] payload)
    {
        if(payload.Length<=7)
        {
            var frame=new byte[12];frame[2]=(byte)(source>>8);frame[3]=(byte)source;frame[4]=(byte)payload.Length;
            Array.Copy(payload,0,frame,5,payload.Length);
            Call(form,"ProcessFrame",frame,0u,false,(int)Get(form,"_activeGeneration"));
        }
        else Call(form,"IsoTpParser_FrameReceived",null,new IsoTpFrameEventArgs(IsoTpFrameType.ConsecutiveFrame,
            new byte[]{0,0,(byte)(source>>8),(byte)source},payload,true));
        // Complete multi-frame payload is injected at the existing parser event seam.
        // Bitmap single frames traverse production RxStatus/source/ISO-TP processing.
    }
    static void Poll(MisfireForm form,Action<byte[]> sender,CancellationToken token)
    {
        Set(form,"_sendMode6",sender);Set(form,"_waitMode6",new Func<int,CancellationToken,bool>((ms,t)=>t.IsCancellationRequested));
        Exception failure=null;int generation=(int)Get(form,"_activeGeneration");
        var thread=new Thread(()=>{try{Call(form,"RunMode6Polling",token,generation);}catch(Exception e){failure=e;}});
        thread.Start();var timer=Stopwatch.StartNew();
        while(thread.IsAlive && timer.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(1);}
        Check(thread.Join(100),"Production poller completes within focused-test bound");Application.DoEvents();
        if(failure!=null)throw failure;
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            Mode6DiscoveryTests.Run();Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;
            using(var main=new MainForm())
            {
                main.Show();Application.DoEvents();main.Size=new Size(1100,740);
                var manager=(J2534Manager)Get(main,"j2534Manager");
                Check(manager.Devices.Count==0,"Registered providers=0");
                var form=(MisfireForm)Get(main,"misfireForm");
                var tabs=(TabControl)Get(main,"tabControl1");tabs.SelectedTab=tabs.TabPages["tabMisfireMonitor"];Application.DoEvents();
                var cards=(Dictionary<int,PidDisplayControl>)Get(form,"_misfireControls");
                var sources=(ComboBox)Get(form,"_sourceSelect");var status=(Label)Get(form,"_status");
                Check(cards.Values.All(c=>c.ValueText=="Undiscovered"),"Initial discovery state is explicit");
                Call(form,"BeginMode6Session");var requests=new List<byte[]>();
                Poll(form,frame=>
                {
                    requests.Add((byte[])frame.Clone());byte mid=frame[6];
                    if(mid==0){Receive(form,0x7E8,Mode6DiscoveryTests.Bitmap(0,1));Receive(form,0x7E9,Mode6DiscoveryTests.Bitmap(0,0));}
                    else if((mid&31)==0)Receive(form,0x7E8,Mode6DiscoveryTests.Bitmap(mid,mid==0xA0?0x60000000u:1u));
                    else if(mid==0xA2)
                    {
                        Receive(form,0x7E9,Mode6DiscoveryTests.Result(mid,99));
                        Receive(form,0x7E8,Mode6DiscoveryTests.Result(mid,0));
                    }
                },CancellationToken.None);
                Check(requests.Select(f=>f[6]).SequenceEqual(new byte[]{0,0x20,0x40,0x60,0x80,0xA0,0xA2,0xA3}),"Production loop requests advertised implemented monitors only");
                Check(requests[0][3]==0xDF && requests.Skip(1).All(f=>f[3]==0xE0),"Production functional discovery then source-directed requests");
                Check(sources.Items.Count==2 && sources.Text.Contains("7E8"),"Two distinct responders without duplicate selector entries");
                Check(cards[0xA20C].ValueText=="0" && cards[0xA20B].ValueText=="No result","Observed zero differs from absent TID");
                Check(cards[0xA30C].ValueText=="No result" && cards[0xA90C].ValueText=="Not advertised","Advertised unanswered monitor differs from unadvertised monitor");
                Check(status.Text.Contains("Snapshot finished") && status.AccessibleDescription.Contains("7E8"),"Completion and per-responder evidence visible");
                sources.SelectedIndex=1;Application.DoEvents();
                Check(cards.Values.All(c=>c.ValueText=="Not advertised"),"Responder without continuation cannot inherit first responder support/results");
                sources.SelectedIndex=0;Application.DoEvents();
                var grid=(FlowLayoutPanel)Get(form,"flpMisfires");var bar=(FlowLayoutPanel)sources.Parent;
                Check(sources.Visible && status.Visible && bar.Bottom<=grid.Top,"New discovery toolbar remains visible above cards");
                using(var image=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(args[0],"r31-discovery-synthetic-ui.png"));}
                main.Size=new Size(760,650);Application.DoEvents();
                Check(sources.Visible && status.Visible && bar.Bottom<=grid.Top && status.Right<=bar.ClientSize.Width,"Discovery status wraps inside narrow toolbar");
                main.Size=new Size(1100,740);Application.DoEvents();
                var old=(Mode6DiscoverySession)Get(form,"_mode6Session");int oldGeneration=(int)Get(form,"_generation");
                var queue=new Thread(()=>Call(form,"Post",oldGeneration,new Action(()=>cards[0xA20C].ValueText="stale")));
                queue.Start();queue.Join();old.Stop();Call(form,"BeginMode6Session");Application.DoEvents();
                Check(cards.Values.All(c=>c.ValueText=="Undiscovered") && sources.Items.Count==0,"Restart clears previous support/results and rejects queued old-generation UI update");
                Check(!old.Accept(0x7E8,Mode6DiscoveryTests.Result(0xA2,888)),"Stopped old session rejects late result");
                Poll(form,f=>Receive(form,0x7E8,new byte[]{0x7F,6,0x12}),CancellationToken.None);
                Check(cards.Values.All(c=>c.ValueText=="Unverified") && status.AccessibleDescription.Contains("Negative"),"Production negative response remains inconclusive with reason");
                Call(form,"BeginMode6Session");Poll(form,f=>Receive(form,0x7E8,new byte[]{0x46,0,1}),CancellationToken.None);
                Check(sources.Items.Count==0 && status.Text.Contains("unknown"),"Production malformed discovery never fabricates unsupported/zero values");
                Call(form,"BeginMode6Session");using(var cancel=new CancellationTokenSource())
                {Poll(form,f=>{Receive(form,0x7E8,Mode6DiscoveryTests.Bitmap(0,1));cancel.Cancel();},cancel.Token);Check(status.Text.Contains("Cancelled"),"Production cancellation stops continuation");}
                Check(manager.Api==null && manager.Device==null && manager.Channel==null && manager.NativeSession==null,"Provider handles=0 before and after production-path acceptance");
                main.Close();Application.DoEvents();
            }
            Console.WriteLine("PASS R31 focused checks="+Mode6DiscoveryTests.Checks+"; physical=False; providerHandles=0");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
