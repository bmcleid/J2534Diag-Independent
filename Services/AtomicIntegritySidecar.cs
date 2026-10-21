using System;
using System.IO;
using System.Text;

namespace J2534Diag
{
    // Publish a capture integrity sidecar only after complete serialization
    // and closing the staging file. Never overwrite existing evidence.
    public static class AtomicIntegritySidecar
    {
        public static void Write(string finalPath, Action<TextWriter> serialize)
        {
            if (string.IsNullOrWhiteSpace(finalPath))
                throw new ArgumentException("Sidecar filename is required.", nameof(finalPath));
            if (serialize == null) throw new ArgumentNullException(nameof(serialize));
            if (File.Exists(finalPath))
                throw new IOException("Integrity sidecar already exists; will not overwrite it: " + finalPath);

            string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    serialize(writer);
                // Move within the same directory; destination collision is an error,
                // not permission to overwrite existing evidence.
                File.Move(tempPath, finalPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException) { } // retain original exception
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
