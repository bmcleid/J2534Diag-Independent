using System;
using System.IO;

namespace J2534Diag
{
    // Immutable report text and source snapshot retained after publication failure.
    // No J2534 device or writer thread is needed for retry.
    public sealed class PendingIntegritySidecar
    {
        private readonly long _sourceLength;
        private readonly DateTime _sourceWriteUtc;
        private readonly string _sourceSha256;
        public string CapturePath { get; }
        public string SidecarPath { get; }
        public string Contents { get; }
        public bool IsPreserved { get; private set; }

        public PendingIntegritySidecar(string capturePath, string sidecarPath, string contents)
        {
            if (string.IsNullOrWhiteSpace(capturePath) ||
                string.IsNullOrWhiteSpace(sidecarPath) || contents == null)
                throw new ArgumentException("Capture, sidecar and report text must be provided.");
            var source = new FileInfo(capturePath);
            if (!source.Exists) throw new FileNotFoundException("Capture CSV missing", capturePath);
            CapturePath = capturePath;
            SidecarPath = sidecarPath;
            Contents = contents;
            _sourceLength = source.Length;
            _sourceWriteUtc = source.LastWriteTimeUtc;
            _sourceSha256 = OfflineCaptureProvenance.HashFile(capturePath);
        }

        public void Publish()
        {
            var source = new FileInfo(CapturePath);
            source.Refresh();
            if (!source.Exists || source.Length != _sourceLength ||
                source.LastWriteTimeUtc != _sourceWriteUtc ||
                !string.Equals(OfflineCaptureProvenance.HashFile(CapturePath),
                    _sourceSha256, StringComparison.Ordinal))
                throw new IOException("Capture CSV has been removed or changed since report finalization; report retry refused.");
            AtomicIntegritySidecar.Write(SidecarPath, writer => writer.Write(Contents));
            IsPreserved = true;
        }
        // A recovery copy preserves the historical report even if its CSV is
        // missing/changed. It must never masquerade as the original sidecar.
        public void SaveRecoveryCopy(string path)
        {
            string destination = Path.GetFullPath(path);
            if (string.Equals(destination, Path.GetFullPath(CapturePath), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(destination, Path.GetFullPath(SidecarPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a separate recovery report filename.");
            AtomicIntegritySidecar.Write(destination, writer =>
            {
                writer.WriteLine("RECOVERY COPY OF FINALIZED REPORT");
                writer.WriteLine("This preserves historical report text; it does not validate the current CSV.");
                writer.WriteLine("OriginalCaptureSHA256=" + _sourceSha256);
                writer.WriteLine("OriginalCaptureLength=" + _sourceLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteLine("OriginalSidecarPath=" + SidecarPath);
                writer.WriteLine("--- Original finalized report follows unchanged ---");
                writer.Write(Contents);
            });
            IsPreserved = true;
        }
    }
}
