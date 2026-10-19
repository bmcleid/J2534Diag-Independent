using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace J2534Diag
{
    /// <summary>
    /// Read-only, byte-for-byte source identification. Hash is of the ORIGINAL file,
    /// not parsed CAN records, a normalized CSV, or a derived export.
    /// No vehicle interface is accessed.
    /// </summary>
    public static class OfflineCaptureProvenance
    {
        public const string OriginalSrxCanSha256 =
            "0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B";

        public static string HashFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A source path is required.", nameof(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return HashStream(stream);
        }

        // One open, read-only file handle for hashing and parsing. FileShare.Read
        // prevents ordinary concurrent writers on Windows throughout both steps.
        // The importer must consume the stream synchronously and leave it open.
        public static T ReadConsistentFile<T>(string path, Func<Stream, T> importer,
            out string sourceSha256)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A source path is required.", nameof(path));
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ReadConsistentStream(stream, importer, out sourceSha256);
        }

        public static T ReadConsistentStream<T>(Stream stream, Func<Stream, T> importer,
            out string sourceSha256)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("Source must be readable and seekable.", nameof(stream));
            stream.Seek(0, SeekOrigin.Begin);
            sourceSha256 = HashStream(stream);
            stream.Seek(0, SeekOrigin.Begin);
            T records = importer(stream);
            // An already-open writer may modify a file despite this read handle's
            // sharing policy. Detect a changed source before publishing rows.
            stream.Seek(0, SeekOrigin.Begin);
            string afterParseSha256 = HashStream(stream);
            if (!string.Equals(sourceSha256, afterParseSha256,
                StringComparison.Ordinal))
                throw new IOException("Offline source changed during import. Nothing was imported; close other writers and retry.");
            return records;
        }

        public static string HashStream(Stream source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(source);
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) hex.Append(value.ToString("X2"));
                return hex.ToString();
            }
        }

        public static bool IsOriginalSrxCan(string hash)
        {
            return string.Equals(hash, OriginalSrxCanSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        public static string Describe(string sha256, bool nativeSixColumn)
        {
            if (!nativeSixColumn)
                return "SHA-256 (source bytes): " + sha256 +
                    "\nFormat: J2534Diag/legacy CSV. No original SRX six-column equivalence claimed.";
            return "SHA-256 (source bytes): " + sha256 +
                (IsOriginalSrxCan(sha256)
                    ? "\nCONFIRMED byte match: canonical October 12 SRX CAN source."
                    : "\nNOT A BYTE MATCH to the canonical October 12 SRX CAN source. This does not invalidate another recording.") +
                "\nThe hash authenticates file equality to the recorded digest, not vehicle origin, clock calibration, bus completeness, or ECU mapping.";
        }
    }
}
