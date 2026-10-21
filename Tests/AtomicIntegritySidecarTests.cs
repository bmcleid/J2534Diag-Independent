using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class AtomicIntegritySidecarTests
    {
        public static int RunAll()
        {
            int checks = 0;
            string directory = Path.Combine(Path.GetTempPath(), "j2534-sidecar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string target = Path.Combine(directory, "capture.csv.integrity.txt");
                AtomicIntegritySidecar.Write(target, writer => writer.Write("COMPLETE=TRUE"));
                if (File.ReadAllText(target) != "COMPLETE=TRUE")
                    throw new Exception("Complete sidecar not published");
                checks++;
                try
                {
                    AtomicIntegritySidecar.Write(target, writer => writer.Write("overwritten"));
                    throw new Exception("Existing sidecar overwritten");
                }
                catch (IOException) { checks++; }
                if (File.ReadAllText(target) != "COMPLETE=TRUE")
                    throw new Exception("Original sidecar changed");
                checks++;
                string failed = Path.Combine(directory, "failed.integrity.txt");
                try
                {
                    AtomicIntegritySidecar.Write(failed, writer =>
                    {
                        writer.Write("PARTIAL");
                        throw new IOException("Injected serialization error");
                    });
                    throw new Exception("Serialization failure ignored");
                }
                catch (IOException ex)
                {
                    if (!ex.Message.Contains("Injected")) throw;
                    checks++;
                }
                if (File.Exists(failed)) throw new Exception("Published partial sidecar");
                checks++;
                if (Directory.GetFiles(directory, "*.pending").Length != 0)
                    throw new Exception("Temporary sidecar leaked");
                checks++;
                AtomicIntegritySidecar.Write(failed, writer => writer.Write("RETRIED=TRUE"));
                if (File.ReadAllText(failed) != "RETRIED=TRUE")
                    throw new Exception("Cannot retry after failed write");
                checks++;
            }
            finally { Directory.Delete(directory, true); }
            return checks;
        }
    }
}
