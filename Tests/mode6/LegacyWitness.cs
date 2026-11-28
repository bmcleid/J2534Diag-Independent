using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

public static class LegacyWitness
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Flags).Invoke(value,args);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("WITNESS " + message); }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += (s,e) => Assembly.LoadFrom(Path.Combine(root,new AssemblyName(e.Name).Name + ".dll"));
            var assembly = Assembly.LoadFrom(Path.Combine(root,"J2534Diag.exe"));
            object manager = Activator.CreateInstance(assembly.GetType("J2534Diag.J2534Manager"),new object[] {null});
            Require(((IList)manager.GetType().GetProperty("Devices").GetValue(manager)).Count == 0,"Baseline provider inventory empty");
            using (var form = (Form)Activator.CreateInstance(assembly.GetType("J2534Diag.MisfireForm"),new[] {manager}))
            {
                form.Show(); Application.DoEvents();
                var controls = (IDictionary)form.GetType().GetField("_misfireControls",Flags).GetValue(form);
                object control = controls.Values.Cast<object>().First(); var text = control.GetType().GetProperty("ValueText");
                text.SetValue(control,"marker"); Call(form,"UpdatePidDisplay",(byte)0xA2,(byte)0x0B,0.0,"counts");
                Require((string)text.GetValue(control) == "marker","R29 UI-thread valid value silently not displayed");
                bool nullFailure = false;
                try { Call(form,"UpdatePidDisplay",(byte)0xFE,(byte)0xFF,1.0,"counts"); }
                catch(TargetInvocationException e) { nullFailure = e.InnerException is NullReferenceException; }
                Require(nullFailure,"R29 unknown test dereferences null control");
                Exception error = null;
                var worker = new Thread(() => { try { Call(form,"ParseMode6Response",new byte[] {0xA2,0x0B,0x24,0,7,0,0,0xFF}); } catch(Exception e) { error=e; } });
                worker.Start(); var deadline = DateTime.UtcNow.AddSeconds(5);
                while(worker.IsAlive && DateTime.UtcNow<deadline) { Application.DoEvents(); Thread.Sleep(5); }
                Require(!worker.IsAlive && error == null && (string)text.GetValue(control) == "7",
                    "R29 accepts truncated eight-byte record as value7");
                foreach(string property in new[] {"Api","Device","Channel","NativeSession"})
                    Require(manager.GetType().GetProperty(property).GetValue(manager) == null,"Baseline no " + property);
                form.Close();
            }
            Console.WriteLine("PASS three R29 defect witnesses; zero provider handles"); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
