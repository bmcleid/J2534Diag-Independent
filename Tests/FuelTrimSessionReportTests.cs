using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        public static int RunAll()
        {
            string directory = Path.Combine(Path.GetTempPath(), "J2534Diag-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
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
