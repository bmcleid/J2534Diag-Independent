using System;
using System.IO;
using System.IO.Compression;
using J2534Diag;
namespace J2534Diag.Tests
{
    internal static class CaptureEvidencePackageTests
    {
        public static int RunAll()
        {
            int checks = 0;
            string dir = Path.Combine(Path.GetTempPath(), "j2534-package-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csv = Path.Combine(dir, "source.csv");
                byte[] original = new byte[] { 239, 187, 191, 65, 44, 66, 13, 10, 49, 44, 50, 13, 10 };
                File.WriteAllBytes(csv, original);
                File.WriteAllText(csv + ".integrity.txt", "Original finalized report");
                string sourceHash = OfflineCaptureProvenance.HashFile(csv);
                string reportHash = OfflineCaptureProvenance.HashFile(csv + ".integrity.txt");
                string package = Path.Combine(dir, "saved.zip");
                CaptureEvidencePackage.Create(csv, package);
                CaptureEvidencePackage.Verify(package); checks++;
                using (var z = ZipFile.OpenRead(package))
                {
                    using (var s = z.GetEntry("capture.csv").Open())
                        if (OfflineCaptureProvenance.HashStream(s) != sourceHash) throw new Exception("CSV changed");
                    checks++;
                    using (var s = z.GetEntry("integrity.txt").Open())
                        if (OfflineCaptureProvenance.HashStream(s) != reportHash) throw new Exception("Report changed");
                    checks++;
                    using (var r = new StreamReader(z.GetEntry("README.txt").Open()))
                        if (!r.ReadToEnd().Contains("claims are UNVERIFIED")) throw new Exception("Association overclaimed");
                    checks++;
                }
                if (OfflineCaptureProvenance.HashFile(csv) != sourceHash ||
                    OfflineCaptureProvenance.HashFile(csv + ".integrity.txt") != reportHash)
                    throw new Exception("Original evidence changed");
                checks++;
                string packageHash = OfflineCaptureProvenance.HashFile(package);
                try { CaptureEvidencePackage.Create(csv, package); throw new Exception("Overwrite accepted"); }
                catch (IOException) { checks++; }
                if (OfflineCaptureProvenance.HashFile(package) != packageHash) throw new Exception("Package replaced");
                checks++;
                File.Delete(csv + ".integrity.txt");
                string without = Path.Combine(dir, "without.zip");
                CaptureEvidencePackage.Create(csv, without);
                CaptureEvidencePackage.Verify(without); checks++;
                using (var z = ZipFile.OpenRead(without))
                using (var r = new StreamReader(z.GetEntry("README.txt").Open()))
                    if (z.GetEntry("integrity.txt") != null || !r.ReadToEnd().Contains("CompanionIntegrityReport=MISSING"))
                        throw new Exception("Missing report concealed");
                checks++;
                using (var z = ZipFile.Open(without, ZipArchiveMode.Update))
                {
                    z.GetEntry("capture.csv").Delete();
                    using (var w = new StreamWriter(z.CreateEntry("capture.csv").Open())) w.Write("TAMPERED");
                }
                try { CaptureEvidencePackage.Verify(without); throw new Exception("Tampering accepted"); }
                catch (IOException) { checks++; }
                try { CaptureEvidencePackage.Create(csv, csv); throw new Exception("CSV target accepted"); }
                catch (IOException) { checks++; }
                try { CaptureEvidencePackage.Create(csv, csv + ".integrity.txt"); throw new Exception("Sidecar target accepted"); }
                catch (IOException) { checks++; }
                try { CaptureEvidencePackage.Create(Path.Combine(dir, "missing.csv"), Path.Combine(dir, "failure.zip")); throw new Exception("Missing source accepted"); }
                catch (IOException) { checks++; }
                if (File.Exists(Path.Combine(dir, "failure.zip")) || Directory.GetFiles(dir, "*.tmp").Length != 0)
                    throw new Exception("Failed package published or temporary left");
                checks++;
            }
            finally { Directory.Delete(dir, true); }
            return checks;
        }
    }
}
