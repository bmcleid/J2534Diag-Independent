using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class PendingIntegritySidecarTests
    {
        public static int RunAll()
        {
            int checks = 0;
            string dir = Path.Combine(Path.GetTempPath(), "j2534-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csv = Path.Combine(dir, "session.csv");
                string target = csv + ".integrity.txt";
                File.WriteAllText(csv, "original-capture");
                var pending = new PendingIntegritySidecar(csv, target,
                    "ApplicationWriteComplete=True\nVehicleBusLossless=UNVERIFIED\n");
                pending.Publish();
                if (!File.ReadAllText(target).Contains("VehicleBusLossless=UNVERIFIED"))
                    throw new Exception("Recovered sidecar content changed");
                checks++;
                try { pending.Publish(); throw new Exception("Overwrite accepted"); }
                catch (IOException) { checks++; }
                File.Delete(target);
                File.AppendAllText(csv, "-changed");
                try { pending.Publish(); throw new Exception("Changed capture accepted"); }
                catch (IOException ex)
                {
                    if (!ex.Message.Contains("changed")) throw;
                    checks++;
                }
                if (File.Exists(target)) throw new Exception("Published sidecar for changed CSV");
                checks++;
                string csv2 = Path.Combine(dir, "session2.csv");
                File.WriteAllText(csv2, "stable-capture");
                string target2 = csv2 + ".integrity.txt";
                var retry = new PendingIntegritySidecar(csv2, target2, "RETRIED=TRUE");
                // Failed final target publication can be retried without recreating snapshot.
                File.WriteAllText(target2, "EXISTING");
                try { retry.Publish(); throw new Exception("Collision overwritten"); }
                catch (IOException) { checks++; }
                if (File.ReadAllText(target2) != "EXISTING")
                    throw new Exception("Collision damaged existing evidence");
                checks++;
                File.Delete(target2);
                retry.Publish();
                if (File.ReadAllText(target2) != "RETRIED=TRUE")
                    throw new Exception("Retry content incorrect");
                checks++;
                File.Delete(csv2);
                File.Delete(target2);
                try { retry.Publish(); throw new Exception("Missing CSV accepted"); }
                catch (IOException) { checks++; }
            }
            finally { Directory.Delete(dir, true); }
            return checks;
        }
    }
}
