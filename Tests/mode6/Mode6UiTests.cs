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

public static class Mode6UiTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Get(object value,string field) => value.GetType().GetField(field,Flags).GetValue(value);
    static void Set(object value,string field,object data) => value.GetType().GetField(field,Flags).SetValue(value,data);
    static object Call(object value,string method,params object[] args) => value.GetType().GetMethod(method,Flags).Invoke(value,args);
    static void Check(bool value,string message) { Mode6PolicyTests.Check(value,message); Console.WriteLine("PASS " + message); }
    static void Event(MisfireForm form,uint source,byte[] payload,bool complete=true)
    {
        Call(form,"IsoTpParser_FrameReceived",null,new IsoTpFrameEventArgs(IsoTpFrameType.ConsecutiveFrame,
            new[] {(byte)(source>>24),(byte)(source>>16),(byte)(source>>8),(byte)source},payload,complete));
        Application.DoEvents();
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
            bool layoutOnly = args.Length > 1 && args[1] == "--layout-only";
            if (!layoutOnly) Mode6PolicyTests.Run();
            using(var main = new MainForm())
            {
                main.Show(); Application.DoEvents();
                var manager = (J2534Manager)Get(main,"j2534Manager");
                Check(manager.Devices.Count == 0,"No registered providers");
                var form = (MisfireForm)Get(main,"misfireForm");
                var tabs = (TabControl)Get(main,"tabControl1");
                tabs.SelectedTab = tabs.TabPages["tabMisfireMonitor"]; Application.DoEvents();
                var controls = (Dictionary<int,PidDisplayControl>)Get(form,"_misfireControls");
                var source = (ComboBox)Get(form,"_sourceSelect"); var status = (Label)Get(form,"_status");
                if (layoutOnly)
                {
                    main.Size = new Size(1100,740); Application.DoEvents();
                    var grid = (FlowLayoutPanel)Get(form,"flpMisfires");
                    var table = (TableLayoutPanel)Get(form,"tlpRight");
                    var bar = (FlowLayoutPanel)source.Parent;
                    Check(source.Visible && status.Visible && bar.Height >= source.Height,
                        "Source selector and status toolbar are visible");
                    Check(bar.Bottom <= grid.Top && bar.Width > 600,"Toolbar has separate non-overlapping row");
                    Check(source.Bounds.Right <= bar.ClientSize.Width && status.Bounds.Right <= bar.ClientSize.Width,
                        "Source and status fit inside toolbar width");
                    Event(form,0x7E8,Mode6PolicyTests.Payload(value:0));
                    Event(form,0x7E9,Mode6PolicyTests.Payload(value:42));
                    source.SelectedIndex=1; Application.DoEvents();
                    Check(controls[0xA20C].ValueText=="42" && source.Text.Contains("7E9"),
                        "Visible selector selects independent source");
                    main.Size = new Size(760,650); Application.DoEvents();
                    Check(source.Visible && status.Visible && bar.Bottom <= grid.Top &&
                        source.Bounds.Right <= bar.ClientSize.Width && status.Bounds.Right <= bar.ClientSize.Width,
                        "Narrow toolbar wraps without hiding source/status or overlapping grid");
                    main.Size = new Size(1100,740); Application.DoEvents();
                    using(var bitmap=new Bitmap(main.Width,main.Height)) { main.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(args[0],"mode6-layout-reviewed-ui.png")); }
                    Check(manager.Api==null && manager.Device==null && manager.Channel==null && manager.NativeSession==null,
                        "Layout acceptance used zero provider handles");
                    main.Close(); Application.DoEvents();
                    Console.WriteLine("PASS focused Mode06 layout checkpoints="+Mode6PolicyTests.Checks+"; prior62checks reused; physical=False");
                    return 0;
                }
                Check(controls.Values.All(c => c.ValueText == "Unknown"),"Initial missing data never zero");
                Event(form,0x7E8,Mode6PolicyTests.Payload(value:0));
                Check(controls[0xA20C].ValueText == "0","Actual UI accepts measured zero on UI thread");
                Check(controls[0xA20B].ValueText == "Unknown","Missing EWMA remains Unknown");
                Check(controls[0xA20C].AccessibleDescription.Contains("RawMax=65535"),"Actual UI retains raw limits");
                Event(form,0x7E9,Mode6PolicyTests.Payload(value:77));
                Check(controls[0xA20C].ValueText == "0","Other responder cannot overwrite selected source");
                source.SelectedIndex=1; Application.DoEvents();
                Check(controls[0xA20C].ValueText == "77","Source selection displays second responder");
                Event(form,0x7E9,Mode6PolicyTests.Payload(units:0x25,value:55));
                Check(controls[0xA20C].ValueText == "Unknown units" && controls[0xA20C].Unit == "unverified","Unknown scaling never labelled counts");
                Event(form,0x7E9,Mode6PolicyTests.Payload(mid:0xB1,tid:0xFF,value:99));
                Check(controls[0xA20C].ValueText == "Unknown units","Unknown test safe and does not replace count");
                Event(form,0x7E9,Mode6PolicyTests.Payload(value:6).Take(9).ToArray());
                Check(controls[0xA20C].ValueText == "Unknown units","Truncated record cannot publish value");
                Event(form,0x7E9,Mode6PolicyTests.Payload(value:6),false);
                Check(controls[0xA20C].ValueText == "Unknown units","Incomplete transport cannot publish value");
                Exception workerError=null;
                var worker=new Thread(()=>{try{Event(form,0x7E9,Mode6PolicyTests.Payload(value:42));}catch(Exception e){workerError=e;}});
                worker.Start(); worker.Join(2000); Application.DoEvents();
                Check(!worker.IsAlive && workerError==null && controls[0xA20C].ValueText=="42","Background update marshalled without blocking UI");
                int generation=(int)Get(form,"_generation");
                var queued=new Thread(()=>Call(form,"Post",generation,new Action(()=>controls[0xA20C].ValueText="stale")));
                queued.Start(); queued.Join(); Set(form,"_generation",generation+1); Application.DoEvents();
                Check(controls[0xA20C].ValueText=="42","Old-generation queued update rejected");
                using(var cancel=new CancellationTokenSource())
                {
                    int errors=0; ChannelLifecyclePolicy.RunCoupledWorker(cancel,()=>{throw new IOException("synthetic receive failure");},e=>errors++);
                    Check(errors==1 && cancel.IsCancellationRequested,"Terminal worker failure cancels peer");
                }
                Call(form,"WorkerFailed",new IOException("synthetic receive failure"),(int)Get(form,"_generation"));
                Check(status.Text.Contains("synthetic receive failure"),"Worker failure visible in actual UI");
                // Valid logical receive ownership, no channel/API/device. Exercise production Stop.
                Check(manager.ReceiveGate.TryAcquire("MISFIRE"),"Acquire logical receive ownership");
                Set(form,"_ownsReceiveChannel",true); var cancellation=new CancellationTokenSource(); Set(form,"_cts",cancellation);
                var blocker=new ManualResetEvent(false); var blocked=new Thread(()=>blocker.WaitOne()); blocked.Start();
                Set(form,"_monitorThread",blocked); form.StartListening();
                Check(ReferenceEquals(Get(form,"_cts"),cancellation),"Retained session cannot be replaced by Start");
                form.StopListening();
                Check(manager.ReceiveGate.CurrentOwner=="MISFIRE" && status.Text.Contains("Shutdown pending"),"Blocked shutdown retains ownership");
                blocker.Set(); blocked.Join(2000); form.StopListening(); blocker.Dispose();
                Check(manager.ReceiveGate.CurrentOwner==null && Get(form,"_cts")==null,"Released reader allows safe cleanup retry");
                Check(controls[0xA20C].ValueText=="42" && status.Text.Contains("Stopped after error"),"Stop preserves last value with error state");
                main.Size=new Size(1100,740); Application.DoEvents();
                using(var bitmap=new Bitmap(main.Width,main.Height)) { main.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(args[0],"mode6-reviewed-ui.png")); }
                Check(manager.Api==null && manager.Device==null && manager.Channel==null && manager.NativeSession==null,
                    "No provider handles before or after UI acceptance");
                main.Close(); Application.DoEvents();
            }
            Console.WriteLine("PASS focused Mode06 checkpoints="+Mode6PolicyTests.Checks+"; physical=False; providerHandles=0"); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
