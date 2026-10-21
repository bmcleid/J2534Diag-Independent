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
        public string CapturePath { get; }
        public string SidecarPath { get; }
        public string Contents { get; }

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
        }

        public void Publish()
        {
            var source = new FileInfo(CapturePath);
            source.Refresh();
            if (!source.Exists || source.Length != _sourceLength ||
                source.LastWriteTimeUtc != _sourceWriteUtc)
                throw new IOException("Capture CSV has been removed or changed since report finalization; report retry refused.");
            AtomicIntegritySidecar.Write(SidecarPath, writer => writer.Write(Contents));
        }
    }
}
