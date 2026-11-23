using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class VinStartupTests
    {
        private static int _checks;
        private static void Check(bool ok, string reason)
        {
            _checks++;
            if (!ok) throw new Exception(reason);
        }
        private static byte[] Frame(byte id, params byte[] payload)
        { return new byte[] { 0, 0, 7, id }.Concat(payload).ToArray(); }
        private static byte[] Ff(byte id = 0xE8)
        { return Frame(id, 0x10, 0x14, 0x49, 2, 1, 0x54, 0x45, 0x53); }
        private static byte[] Cf(byte sequence, byte id = 0xE8)
        {
            return Frame(id, new byte[] { (byte)(0x20 | sequence) }.Concat(
                Encoding.ASCII.GetBytes(sequence == 1 ? "TVIN123" : "4567890")).ToArray());
        }
        private static Assembly App(string file = null)
        {
            if (file == null) file = Path.Combine(Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..")), "bin", "Release", "J2534Diag.exe");
            return Assembly.LoadFrom(file);
        }
        private static object Manager(Assembly app)
        { return FormatterServices.GetUninitializedObject(app.GetType("J2534Diag.J2534Manager", true)); }
        private static void Event(Assembly app, object manager, byte id, byte[] payload, bool complete)
        {
            var e = Activator.CreateInstance(app.GetType("J2534Diag.IsoTpFrameEventArgs", true),
                new object[] { Enum.ToObject(app.GetType("J2534Diag.IsoTpFrameType", true), 2),
                    new byte[] { 0, 0, 7, id }, payload, complete });
            manager.GetType().GetMethod("IsoTpParser_FrameReceived", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { null, e });
        }
        private static string Vin(object manager)
        { return (string)manager.GetType().GetProperty("Vin").GetValue(manager); }
        private static byte[] VinPayload()
        { return new byte[] { 0x49, 2, 1 }.Concat(Encoding.ASCII.GetBytes("TESTVIN1234567890")).ToArray(); }

        public static int RunBaseline(string file)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
                throw new InvalidOperationException("Baseline fixture is CI only");
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(file)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            Check(hash == "131F86F7AC39478F75F86C0F7D96E2BD8AF0FACDB96F33A08A6183F21E023AC2",
                "Historical R16 executable hash mismatch");
            var legacy = new LegacyIsoTpParser();
            int completed = 0;
            legacy.FrameReceived += (sender, e) => { if (e.IsComplete) completed++; };
            legacy.ProcessMessage(Ff()); legacy.ProcessMessage(Cf(2)); legacy.ProcessMessage(Cf(1));
            Check(completed == 1, "Historical wrong-sequence defect not reproduced");
            legacy = new LegacyIsoTpParser(); completed = 0;
            legacy.FrameReceived += (sender, e) => { if (e.IsComplete) completed++; };
            legacy.ProcessMessage(Ff()); legacy.ProcessMessage(Cf(1, 0xE9)); legacy.ProcessMessage(Cf(2, 0xE9));
            Check(completed == 1, "Historical mixed-source defect not reproduced");
            Assembly app = App(file);
            object manager = Manager(app);
            Event(app, manager, 0xE8, VinPayload(), false);
            Check(Vin(manager) == "TESTVIN1234567890", "Historical incomplete-event VIN acceptance not reproduced");
            manager = Manager(app);
            Event(app, manager, 0xE9, VinPayload(), true);
            Check(Vin(manager) == "TESTVIN1234567890", "Historical wrong-responder VIN acceptance not reproduced");
            Console.WriteLine("PASS: " + _checks + " historical R16 identity/defect reproduction checks; no driver.");
            return 0;
        }

        public static int RunAll()
        {
            long now = 0;
            var parser = new IsoTpParser(() => now);
            var events = new List<IsoTpFrameEventArgs>();
            parser.FrameReceived += (sender, e) => events.Add(e);
            Action clear = () => { parser.Reset(); events.Clear(); now = 0; };
            Func<int> completed = () => events.Count(e => e.IsComplete);
            parser.ProcessMessage(Ff());
            Check(completed() == 0, "FirstFrame treated as complete");
            parser.ProcessMessage(Cf(1));
            Check(completed() == 0, "Partial VIN treated as complete");
            parser.ProcessMessage(Cf(2));
            Check(completed() == 1 && events.Last().Payload.SequenceEqual(VinPayload()), "Exact VIN reassembly failed");
            Check(events.Count == 3, "Duplicate completion event");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Cf(2)); parser.ProcessMessage(Cf(1));
            Check(completed() == 0, "Wrong/out-of-order sequence accepted");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 0, "Duplicate CF accepted");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Cf(1, 0xE9)); parser.ProcessMessage(Cf(2, 0xE9));
            Check(completed() == 0, "Mixed responder assembled");
            parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 1, "Unrelated responder damaged legitimate assembly");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Ff(0xE9));
            parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(1, 0xE9));
            parser.ProcessMessage(Cf(2)); parser.ProcessMessage(Cf(2, 0xE9));
            Check(completed() == 2, "Concurrent responders not kept separate");
            clear(); parser.ProcessMessage(Ff()); now = 1000; parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 0, "Expired assembly accepted");
            clear(); parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(events.Count == 0, "Orphan CF accepted");
            foreach (byte length in new byte[] { 0, 6, 7 })
            {
                clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Frame(0xE8, 0x10, length, 0x49, 2, 1, 0, 0, 0));
                parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
                Check(completed() == 0, "Malformed replacement FF retained old assembly");
            }
            clear(); parser.ProcessMessage(Frame(0xE8, 0x10, 0x14, 0x49, 2));
            Check(events.Count == 0, "Truncated FirstFrame accepted");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Frame(0xE8, 0x21, 1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 0, "Short nonfinal CF accepted");
            clear(); parser.ProcessMessage(Ff()); parser.Reset(); parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 0, "Previous connection state survived Reset");
            clear(); parser.ProcessMessage(Frame(0xE8, 0)); parser.ProcessMessage(Frame(0xE8, 8, 1, 2, 3, 4, 5, 6, 7));
            Check(events.Count == 0, "Invalid SingleFrame length accepted");
            clear(); parser.ProcessMessage(Frame(0xE8, 3, 0x41, 6, 0x80, 0, 0, 0, 0));
            Check(completed() == 1 && events[0].Payload.Length == 3, "SF padding was not trimmed");
            clear(); parser.ProcessMessage(Ff()); parser.ProcessMessage(Cf(2)); events.Clear();
            parser.ProcessMessage(Ff()); parser.ProcessMessage(Cf(1)); parser.ProcessMessage(Cf(2));
            Check(completed() == 1, "Fresh transaction did not recover after sequence fault");
            // Sequence rollover with a payload longer than sixteen CFs.
            clear(); parser.ProcessMessage(Frame(0xE8, 0x10, 0x7E, 1, 2, 3, 4, 5, 6));
            int left = 120, seq = 1;
            while (left > 0)
            {
                int take = Math.Min(7, left);
                parser.ProcessMessage(Frame(0xE8, new byte[] { (byte)(0x20 | seq) }.Concat(new byte[take]).ToArray()));
                left -= take; seq = (seq + 1) & 15;
            }
            Check(completed() == 1 && events.Last().Payload.Length == 126, "CF sequence rollover failed");
            Assembly app = App();
            object manager = Manager(app);
            Event(app, manager, 0xE8, VinPayload(), false);
            Check(Vin(manager) == null, "Incomplete event became selected VIN");
            Event(app, manager, 0xE9, VinPayload(), true);
            Check(Vin(manager) == null, "Wrong physical responder became selected VIN");
            Event(app, manager, 0xE8, VinPayload().Concat(new byte[] { 0 }).ToArray(), true);
            Check(Vin(manager) == null, "Overlong VIN payload accepted");
            byte[] bad = VinPayload(); bad[19] = 0;
            Event(app, manager, 0xE8, bad, true);
            Check(Vin(manager) == null, "Invalid VIN character accepted");
            bad = VinPayload(); bad[2] = 2;
            Event(app, manager, 0xE8, bad, true);
            Check(Vin(manager) == null, "Wrong Mode09 record number accepted");
            Event(app, manager, 0xE8, VinPayload(), true);
            Check(Vin(manager) == "TESTVIN1234567890", "Complete valid payload not selected");
            // Wrong source/service/length cannot trigger FC, even with no Channel.
            manager = Manager(app);
            object appParser = Activator.CreateInstance(app.GetType("J2534Diag.IsoTpParser", true));
            manager.GetType().GetField("_isoTpParser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, appParser);
            var process = manager.GetType().GetMethod("ProcessVinMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            process.Invoke(manager, new object[] { Ff(0xE9) });
            byte[] unrelated = Ff(); unrelated[6] = 0x62;
            process.Invoke(manager, new object[] { unrelated });
            unrelated = Ff(); unrelated[5] = 0x15;
            process.Invoke(manager, new object[] { unrelated });
            Check(Vin(manager) == null, "Unrelated FirstFrame accepted or sent FC");
            Console.WriteLine("PASS: " + _checks + " VIN parser/manager rejection and recovery assertions; no driver.");
            return _checks;
        }
    }
}
