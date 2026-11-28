using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace J2534Diag
{
    public static class CaptureEvidencePackage
    {
        public static void Create(string csvPath, string outputPath)
        {
            string csv = Path.GetFullPath(csvPath);
            string output = Path.GetFullPath(outputPath);
            string sidecar = csv + ".integrity.txt";
            if (string.Equals(output, csv, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(output, sidecar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a separate ZIP filename.");
            if (File.Exists(output)) throw new IOException("The package already exists; choose a new filename.");
            string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var report = File.Exists(sidecar)
                    ? new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read) : null)
                {
                    string csvHash = OfflineCaptureProvenance.HashStream(source);
                    source.Position = 0;
                    string reportHash = report == null ? null : OfflineCaptureProvenance.HashStream(report);
                    if (report != null) report.Position = 0;
                    var hashes = new Dictionary<string, string>();
                    using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    using (var zip = new ZipArchive(target, ZipArchiveMode.Create))
                    {
                        AddStream(zip, "capture.csv", source);
                        hashes.Add("capture.csv", csvHash);
                        if (report != null)
                        {
                            AddStream(zip, "integrity.txt", report);
                            hashes.Add("integrity.txt", reportHash);
                        }
                        string readme = "J2534Diag saved capture evidence package\n" +
                            "capture.csv contains the original source bytes, not filtered/exported preview rows.\n" +
                            "CompanionIntegrityReport=" + (report == null ? "MISSING" : "INCLUDED") + "\n" +
                            "A companion is selected by filename only. Its association and claims are UNVERIFIED.\n" +
                            "Hashes establish packaged byte consistency, not vehicle origin, authenticity or bus completeness.\n" +
                            "Original files were opened read-only and are not modified.\n" +
                            "Verify MANIFEST.sha256 before analysis; retain the original package.\n";
                        byte[] readmeBytes = new UTF8Encoding(false).GetBytes(readme);
                        using (var input = new MemoryStream(readmeBytes))
                        {
                            hashes.Add("README.txt", OfflineCaptureProvenance.HashStream(input));
                            input.Position = 0;
                            AddStream(zip, "README.txt", input);
                        }
                        string manifest = string.Join("\n", hashes.OrderBy(x => x.Key, StringComparer.Ordinal)
                            .Select(x => x.Value + "  " + x.Key)) + "\n";
                        using (var writer = new StreamWriter(zip.CreateEntry("MANIFEST.sha256").Open(), new UTF8Encoding(false)))
                            writer.Write(manifest);
                    }
                    source.Position = 0;
                    if (OfflineCaptureProvenance.HashStream(source) != csvHash)
                        throw new IOException("Source CSV changed during packaging.");
                    if (report != null)
                    {
                        report.Position = 0;
                        if (OfflineCaptureProvenance.HashStream(report) != reportHash)
                            throw new IOException("Companion report changed during packaging.");
                    }
                    Verify(temp);
                    // Same-directory publication; never replace an existing package.
                    File.Move(temp, output);
                }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static void AddStream(ZipArchive zip, string name, Stream source)
        {
            using (var destination = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
                source.CopyTo(destination);
        }

        public static void Verify(string packagePath)
        {
            using (var file = File.OpenRead(packagePath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in zip.Entries)
                    if (!names.Add(entry.FullName)) throw new IOException("Duplicate package entry.");
                var manifest = zip.GetEntry("MANIFEST.sha256");
                if (manifest == null) throw new IOException("Package manifest missing.");
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var reader = new StreamReader(manifest.Open()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length != 0)
                        {
                            if (line.Length < 67 || line.Substring(64, 2) != "  ")
                                throw new IOException("Malformed package manifest.");
                            string hash = line.Substring(0, 64);
                            string name = line.Substring(66);
                            if (hash.Any(c => !Uri.IsHexDigit(c)) ||
                                (name != "capture.csv" && name != "integrity.txt" && name != "README.txt") ||
                                expected.ContainsKey(name))
                                throw new IOException("Invalid package manifest entry.");
                            expected.Add(name, hash);
                        }
                    }
                }
                if (!expected.ContainsKey("capture.csv") || !expected.ContainsKey("README.txt") ||
                    zip.Entries.Count != expected.Count + 1)
                    throw new IOException("Package entries do not match manifest.");
                foreach (var item in expected)
                {
                    var entry = zip.GetEntry(item.Key);
                    if (entry == null) throw new IOException("Package payload missing.");
                    using (var input = entry.Open())
                        if (!string.Equals(OfflineCaptureProvenance.HashStream(input), item.Value,
                            StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Package payload hash mismatch: " + item.Key);
                }
            }
        }
    }
}
