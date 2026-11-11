using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace J2534Diag
{
    // Device-independent preflight: the user-selected, registered J2534 DLL
    // must actually exist and match this x64 Windows preview. No DLL is loaded.
    public sealed class NativeDriverIdentity
    {
        public string FullPath { get; internal set; }
        public string Sha256 { get; internal set; }
        public string Company { get; internal set; }
        public string Product { get; internal set; }
        public string Version { get; internal set; }
        public long Length { get; internal set; }
        public string Summary =>
            "Driver file: " + FullPath + "\n" +
            "64-bit PE: verified\nCompany (file metadata): " + Company +
            "\nProduct (file metadata): " + Product +
            "\nVersion (file metadata): " + Version +
            "\nFile size: " + Length + " bytes\nSHA-256: " + Sha256 +
            "\n\nFile metadata is not proof of driver authenticity or functionality.";
    }

    public static class NativeJ2534DriverCheck
    {
        public static NativeDriverIdentity Inspect(string registeredAbsolutePath)
        {
            if (!Environment.Is64BitProcess)
                throw new InvalidOperationException(
                    "Native capture requires a 64-bit J2534Diag process (not 32-bit).");
            if (string.IsNullOrWhiteSpace(registeredAbsolutePath) ||
                !Path.IsPathRooted(registeredAbsolutePath))
                throw new ArgumentException("Select a registered J2534 DLL with an absolute path.",
                    nameof(registeredAbsolutePath));
            var full = Path.GetFullPath(registeredAbsolutePath);
            if (!string.Equals(Path.GetExtension(full), ".dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(full), "mock-j2534.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid physical-device J2534 DLL selection.");
            if (!File.Exists(full))
                throw new FileNotFoundException("The selected J2534 driver is not installed here.", full);
            byte[] hash;
            long length;
            using (var file = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.Read, 32768))
            {
                length = file.Length;
                using (var reader = new BinaryReader(file, Encoding.ASCII, true))
                {
                    if (length < 256 || reader.ReadUInt16() != 0x5A4D)
                        throw new InvalidDataException("Not a Windows PE driver DLL.");
                    file.Position = 0x3c;
                    int pe = reader.ReadInt32();
                    if (pe < 64 || pe > length - 24)
                        throw new InvalidDataException("Invalid Windows PE header offset.");
                    file.Position = pe;
                    if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
                        throw new InvalidDataException(
                            "Selected J2534 DLL is not AMD64 (x64); use its x64 registered driver.");
                }
                file.Position = 0;
                using (var sha = SHA256.Create()) hash = sha.ComputeHash(file);
            }
            var version = FileVersionInfo.GetVersionInfo(full);
            return new NativeDriverIdentity
            {
                FullPath = full,
                Sha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(),
                Company = version.CompanyName ?? "",
                Product = version.ProductName ?? "",
                Version = version.FileVersion ?? "",
                Length = length
            };
        }
    }
}