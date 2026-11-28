using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class FuelTrimSessionReportTests
    {
        private static int _checks;
        private static void Check(bool ok, string reason) { _checks++; if (!ok) throw new Exception(reason); }
        private static List<string[]> Read(string file)
        {
            var rows = new List<string[]>();
            using (var parser = new TextFieldParser(file))
            {
                parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
                while (!parser.EndOfData) rows.Add(parser.ReadFields());
            }
            return rows;
        }
        private static void ExtendedContinuity(string directory, DateTime utc)
        {
            string file = Path.Combine(directory, "continuity.csv");
            var report = new FuelTrimSessionReport(1000);
            report.Begin("connection-A", "TESTVIN0000000001", utc);
            report.Observe("source-A", 6, 1, 1, utc, 1); report.Observe("source-A", 7, 2, 2, utc, 2);
            report.Stop(3, utc, "tab departed");
            report.Begin("connection-B", "TESTVIN0000000002", utc);
            report.SelectSource("source-B", utc);
            report.Observe("source-B", 6, 3, 4, utc, 4); report.Observe("source-B", 7, 4, 5, utc, 5);
            report.Export(file, "fixture", "hash", "Not attempted for current connection");
            var context = Read(file);
            Check(context.Where(r => r[2] == "Observation").Select(r => r[1]).Distinct().Count() == 2,
                "Connection contexts share a report session identity");
            Check(context.Where(r => r[2] == "Observation").All(r =>
                (r[5] == "connection-A" && r[6] == "TESTVIN0000000001" && r[7] == "source-A") ||
                (r[5] == "connection-B" && r[6] == "TESTVIN0000000002" && r[7] == "source-B")), "VIN/mode/source crossed sessions");
            byte[] pids = { 0x0C, 6, 7, 8, 9, 0x11 };
            for (int i = 0; i < 60000; i++)
                if (!report.Observe("source-B", pids[i % 6], i % 100, i + 1000, utc.AddTicks(i), i + 1000))
                    throw new Exception("Extended fixture observation rejected");
            Check(report.RetainedRows == 20000 && report.DiscardedRows > 40000 && report.AcceptedObservations == 60004,
                "Long synthetic session violated capacity or lifetime totals");
            long produced = 0; Exception failure = null;
            using (var stop = new ManualResetEvent(false))
            using (var started = new ManualResetEvent(false))
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        while (!stop.WaitOne(0))
                        {
                            long tick = 62000 + produced;
                            if (!report.Observe("source-B", 6, 5, tick, utc, tick)) throw new Exception("Concurrent fixture rejected");
                            Interlocked.Increment(ref produced); started.Set();
                            if (produced % 2000 == 0) Thread.Sleep(1);
                        }
                    }
                    catch (Exception ex) { failure = ex; started.Set(); }
                });
                worker.Start();
                try
                {
                    Check(started.WaitOne(2000), "Concurrent observation worker did not start");
                    for (int i = 0; i < 3; i++)
                    {
                        report.Export(file, "fixture", "hash", "Not attempted for current connection");
                        Check(SpinWait.SpinUntil(() => report.ObservationsSinceExport > 0 && report.HasUnexportedChanges, 2000), "Export acknowledged observations arriving after its snapshot");
                        var rows = Read(file);
                        int retained = int.Parse(Regex.Match(rows[1][15], @"(?:^|;)RetainedRows=(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                        var summaries = rows.Where(r => r[2] == "PidSummary").ToArray();
                        Check(rows.Count == retained + 8 && retained == 20000 && summaries.Length == 6 && rows.All(r => r.Length == 16),
                            "Concurrent export snapshot row count/shape inconsistent");
                        foreach (var summary in summaries)
                        {
                            long total = long.Parse(Regex.Match(summary[15], @"AcceptedObservations=(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                            Check(total >= rows.LongCount(r => r[2] == "Observation" && r[8] == summary[8]),
                                "Snapshot summary smaller than its retained PID observations");
                        }
                    }
                }
                finally { stop.Set(); if (!worker.Join(3000)) throw new Exception("Concurrent fixture did not retire"); }
            }
            Check(failure == null && report.AcceptedObservations == 60004 + produced,
                "Concurrent observations lost, duplicated or failed: " + failure);
            report.Export(file, "fixture", "hash", "Not attempted for current connection");
            var final = Read(file);
            Check(report.ObservationsSinceExport == 0 && !report.HasUnexportedChanges, "Final export left unchanged snapshot unacknowledged");
            Check(final.Where(r => r[2] == "PidSummary").Sum(r => long.Parse(Regex.Match(r[15], @"AcceptedObservations=(\d+)").Groups[1].Value,
                CultureInfo.InvariantCulture)) == report.AcceptedObservations, "Final lifetime summary total mismatch");
            Check(!Directory.GetFiles(directory, "*.tmp").Any(), "Concurrent exports left temporary files");
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
                File.Copy(file, Path.Combine(root, "diagnostics", "report-extended-continuity.csv"), true);
            }
            using (var gate = new FuelTrimPollingGate(5, 1, 2))
            using (var cancellation = new CancellationTokenSource())
            {
                byte[] enabled = { 6 }; var sent = new List<byte>();
                gate.Run(() => enabled, pid =>
                {
                    sent.Add(pid); gate.Observe(pid);
                    if (sent.Count == 1) enabled = new byte[] { 6, 7 };
                    if (pid == 7) cancellation.Cancel();
                }, cancellation.Token);
                Check(sent.SequenceEqual(new byte[] { 6, 6, 7 }) && gate.LastStopReason == "Cancelled",
                    "Enabled-set refresh or cancellation-over-reply behavior changed");
                Check(!gate.Observe(7), "Retired pending PID accepted acknowledgement");
                enabled = new byte[] { 6 }; sent.Clear();
                gate.Run(() => enabled, pid => { sent.Add(pid); gate.Observe(pid); enabled = new byte[0]; }, CancellationToken.None);
                Check(sent.SequenceEqual(new byte[] { 6 }) && gate.LastStopReason == "No enabled PIDs", "Disabling all PIDs did not terminate next polling cycle");
            }
        }

        public static int RunAll()
        {
            string directory = Path.Combine(Path.GetTempPath(), "J2534Diag-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                ExtendedContinuity(directory, new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc));
                string file = Path.Combine(directory, "session.csv");
                var report = new FuelTrimSessionReport(1000);
                DateTime utc = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
                report.Begin("Managed active diagnostics", "TESTVIN1234567890", utc);
                string source = "0x7E8 / Mode01, \"fixture\"\nsource";
                Check(report.Observe(source, 0x0C, 0, 100, utc, 100), "Measured zero rejected");
                Check(report.Observe(source, 6, -1.5625, 100, utc, 100), "Negative trim rejected");
                Check(!report.Observe(source, 6, double.NaN, 101, utc, 101), "NaN accepted");
                Check(!report.Observe(source, 6, double.PositiveInfinity, 101, utc, 101), "Infinity accepted");
                Check(!report.Observe(source, 6, 1, 99, utc, 100), "Older observation accepted");
                Check(!report.Observe(source, 6, 1, 102, utc, 101), "Future arrival accepted");
                Check(!report.Observe(source, 0xFF, 1, 100, utc, 100), "Unknown PID accepted");
                report.Refresh(2101, utc.AddSeconds(3)); report.Refresh(2200, utc.AddSeconds(4));
                report.Event("Disconnect", "Failed: \"test\", retained\nnext line", utc.AddSeconds(4));
                report.Stop(2300, utc.AddSeconds(5), "Stopped for export");
                int retained = report.RetainedRows;
                report.Export(file, "R22 / fixture", new string('a', 64), "Failed: test");
                var rows = Read(file);
                Check(rows.All(r => r.Length == 16), "CSV shape/quoting invalid");
                Check(rows[0][0] == "Utc" && rows[0][15] == "Details", "CSV header mismatch");
                Check(rows.Skip(1).All(r => r[3] == "R22 / fixture" && r[4] == new string('a', 64)), "Build/hash provenance absent");
                Check(rows.Any(r => r[2] == "Observation" && r[8] == "0C" && r[11] == "0" && r[12] == "Fresh"), "Measured zero lost");
                Check(rows.Any(r => r[2] == "Observation" && r[8] == "06" && r[11] == "-1.5625" && r[7] == source), "Decimal/source text changed");
                Check(rows.Where(r => r[12] == "Unknown" || r[12] == "Stale" || r[12] == "Stopped").All(r => r[11] == ""), "Missing/stale/stopped fabricated a value");
                Check(rows.Count(r => r[12] == "Stale") == 2, "Repeated stale transition or stale values absent");
                Check(rows.Any(r => r[12] == "Stale" && r[13] == "2001" && r[14] == "100"), "Monotonic age lost");
                Check(rows.Any(r => r[2] == "Disconnect" && r[15] == "Failed: \"test\", retained\nnext line"), "Multiline error changed");
                Check(rows.Where(r => r[2] == "Observation").All(r => r[5] == "Managed active diagnostics" && r[6] == "TESTVIN1234567890"), "Connection context lost");
                Check(rows.Any(r => r[2] == "ExportMetadata" && r[15].Contains("DisconnectOutcome=Failed: test")), "Disconnect outcome omitted");
                Check(report.RetainedRows == retained, "Export altered evidence buffer");
                Check(!report.Observe(source, 6, 1, 2400, utc, 2400), "Stopped session accepted late observation");
                report.Export(file, "R22 / fixture", new string('a', 64), "Succeeded");
                Check(Read(file).Count == rows.Count, "Atomic overwrite duplicated content");
                Check(!Directory.GetFiles(directory, "*.tmp").Any(), "Successful export left temp file");
                bool failed = false;
                try { report.Export(directory, "fixture", "hash", "Not attempted"); } catch (IOException) { failed = true; }
                Check(failed && !Directory.GetFiles(Path.GetDirectoryName(directory), Path.GetFileName(directory) + ".*.tmp").Any(), "Failed export not surfaced/cleaned");
                report.Begin("Second session", null, utc.AddMinutes(1));
                report.Observe("0x7E9 / Mode01", 0x11, 12, 3000, utc, 6001);
                report.Export(file, "R22", "hash", "Not attempted"); rows = Read(file);
                Check(rows.Where(r => r[2] == "SessionStart").Select(r => r[1]).Distinct().Count() == 2, "Session restart overwrote prior identity");
                Check(rows.Any(r => r[2] == "Observation" && r[12] == "Stale" && r[13] == "3001"), "Delayed acceptance mislabeled fresh");
                var bounded = new FuelTrimSessionReport(1000, 12); bounded.Begin("test", "", utc);
                for (int i = 0; i < 30; i++) bounded.Event("Test", i.ToString(CultureInfo.InvariantCulture), utc);
                Check(bounded.RetainedRows == 12 && bounded.DiscardedRows == 25, "Buffer limit/count wrong");
                bounded.Export(file, "R22", "hash", "Not attempted");
                Check(Read(file)[1][15].Contains("DiscardedOldestRows=25"), "Truncation hidden in export");
                Check(bounded.HealthText().Contains("Rows evicted: 25") && bounded.HealthText().Contains("Sample drops: 0"), "Health conflated row eviction with queue loss");
                var audit = new FuelTrimSessionReport(1000, 12);
                audit.Begin("clock fixture", "", utc);
                audit.Observe("A", 6, 1, 100, utc, 100);
                audit.Refresh(101, utc.AddDays(1));
                audit.Export(file, "R23", "hash", "Not attempted");
                Check(!Read(file).Any(r => r[12] == "Stale"), "Wall-clock forward jump expired fresh monotonic sample");
                audit.Refresh(2101, utc.AddDays(-1));
                audit.SelectSource("A", utc); audit.SelectSource("B", utc);
                audit.ChartsCleared(utc);
                audit.Account(FuelTrimEvidenceCounter.CoalescedBeforeAcceptance, 4);
                audit.Account(FuelTrimEvidenceCounter.InactiveOrGenerationDiscarded, 2);
                audit.SetStatus("Unanswered requests", "six misses", utc);
                audit.Stop(2200, utc, "owner stop");
                Check(audit.SessionState == "Unanswered requests", "Quiet stop reason overwritten");
                audit.Export(file, "R23", "hash", "Succeeded"); rows = Read(file);
                Check(rows.Any(r => r[2] == "PidSummary" && r[8] == "06" && r[15].Contains("AcceptedObservations=1;StaleTransitions=1")), "Eviction lost per-PID totals or backward UTC jump prevented expiry");
                Check(rows.Any(r => r[2] == "SourceSelected") && rows.Any(r => r[2] == "ChartsCleared") && audit.AcceptedObservations == 1, "Display events altered accepted evidence");
                Check(rows[1][15].Contains("CoalescedBeforeAcceptance=4") && rows[1][15].Contains("InactiveOrGenerationDiscarded=2"), "Queue accounting absent");
                string original = File.ReadAllText(file); int beforeFailure = audit.RetainedRows;
                bool lockedFailed = false;
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    try { audit.Export(file, "R23", "hash", "Succeeded"); } catch (IOException) { lockedFailed = true; }
                Check(lockedFailed && File.ReadAllText(file) == original && audit.RetainedRows == beforeFailure, "Failed overwrite damaged saved or buffered evidence");
                Check(!Directory.GetFiles(directory, "*.tmp").Any(), "Failed overwrite left temp file");
                audit.Export(file, "R23", "hash", "Succeeded");
                Check(Read(file).Count == rows.Count, "Retry changed evidence row count");
                audit.Begin("fault fixture", "", utc);
                audit.SetStatus("Driver error", "fixture", utc);
                audit.SetStatus("Cleanup pending", "fixture", utc);
                audit.Stop(3000, utc, "stop");
                Check(audit.SessionState == "Driver error; cleanup pending", "Cleanup hid driver failure");
                audit.Begin("disabled fixture", "", utc);
                audit.SetStatus("No enabled PIDs", "all disabled", utc);
                audit.Stop(4000, utc, "timer stop");
                Check(audit.SessionState == "No enabled PIDs", "Stop hid disabled-PID reason");
                audit.Export(file, "R24", "hash", "Not attempted");
                Check(Read(file)[1][15].Contains("SessionState=No enabled PIDs"), "Disabled reason absent from saved metadata");
                var watermark = new FuelTrimSessionReport(1000); watermark.Begin("watermark", "", utc);
                watermark.Observe("A", 6, 1, 1, utc, 1);
                Check(watermark.ObservationsSinceExport == 1, "New observation not marked unsaved");
                watermark.Export(file, "R26", "hash", "Not attempted");
                Check(watermark.ObservationsSinceExport == 0, "Successful export did not acknowledge snapshot");
                watermark.Observe("A", 6, 2, 2, utc, 2); bool watermarkFailed = false;
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    try { watermark.Export(file, "R26", "hash", "Not attempted"); } catch (IOException) { watermarkFailed = true; }
                Check(watermarkFailed && watermark.ObservationsSinceExport == 1, "Failed export acknowledged unsaved observation");
                watermark.Begin("new session", "", utc); watermark.Observe("B", 6, 3, 3, utc, 3);
                Check(watermark.ObservationsSinceExport == 2, "Restart lost unexported lifetime observations");
                watermark.Export(file, "R26", "hash", "Not attempted");
                Check(watermark.ObservationsSinceExport == 0, "Retry did not acknowledge retained observations");

                var eventsOnly = new FuelTrimSessionReport(1000);
                Check(!eventsOnly.HasUnexportedChanges, "Fresh empty report falsely dirty");
                eventsOnly.Account(FuelTrimEvidenceCounter.QueueCapacityDiscarded, 0);
                Check(!eventsOnly.HasUnexportedChanges, "Zero counter update falsely dirty");
                eventsOnly.Begin("event-only", "", utc);
                eventsOnly.SetStatus("Unanswered requests", "no reply", utc);
                Check(eventsOnly.HasUnexportedChanges && eventsOnly.ObservationsSinceExport == 0,
                    "No-response session events were not protected");
                eventsOnly.Export(file, "R27", "hash", "Not attempted");
                Check(!eventsOnly.HasUnexportedChanges, "Successful event-only save stayed dirty");
                eventsOnly.Event("Disconnect", "Failed: fixture", utc);
                Check(eventsOnly.HasUnexportedChanges && eventsOnly.ObservationsSinceExport == 0 &&
                    eventsOnly.HealthText().Contains("Unsaved report: Yes"), "Disconnect-only change was hidden after export");
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool eventFailed = false;
                    try { eventsOnly.Export(file, "R27", "hash", "Failed: fixture"); } catch (IOException) { eventFailed = true; }
                    Check(eventFailed && eventsOnly.HasUnexportedChanges, "Failed event-only save acknowledged changes");
                }
                eventsOnly.Export(file, "R27", "hash", "Failed: fixture");
                Check(!eventsOnly.HasUnexportedChanges && Read(file).Any(r => r[2] == "Disconnect" && r[15] == "Failed: fixture"),
                    "Event-only retry lost disconnect evidence");
                eventsOnly.Account(FuelTrimEvidenceCounter.QueueCapacityDiscarded, 3);
                Check(eventsOnly.HasUnexportedChanges && eventsOnly.ObservationsSinceExport == 0,
                    "Counter-only change not protected");
                eventsOnly.Export(file, "R27", "hash", "Failed: fixture");
                Check(!eventsOnly.HasUnexportedChanges && Read(file)[1][15].Contains("QueueCapacityDiscarded=3"),
                    "Counter-only save lost metadata");
                eventsOnly.SetStatus("Unanswered requests", "same status", utc);
                Check(!eventsOnly.HasUnexportedChanges, "Unchanged status prompted repeatedly");

                using (var gate = new FuelTrimPollingGate(1, 1, 1))
                {
                    gate.Run(() => new byte[] { 6 }, pid => { }, System.Threading.CancellationToken.None);
                    Check(gate.LastStopReason == "Unanswered requests", "Unanswered polling not distinguished");
                    gate.Run(() => new byte[0], pid => { }, System.Threading.CancellationToken.None);
                    Check(gate.LastStopReason == "No enabled PIDs", "Disabled polling not distinguished");
                    try { gate.Run(() => new byte[] { 6 }, pid => { throw new IOException("fixture"); }, System.Threading.CancellationToken.None); } catch (IOException) { }
                    Check(gate.LastStopReason == "Worker error", "Worker failure mislabeled unanswered");
                }
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                {
                    string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
                    File.Copy(file, Path.Combine(root, "diagnostics", "report-bounded-fixture.csv"), true);
                }
            }
            finally { Directory.Delete(directory, true); }
            Console.WriteLine("PASS: " + _checks + " scalar export provenance/missing-state/bounds/atomicity assertions.");
            return _checks;
        }
    }
}
