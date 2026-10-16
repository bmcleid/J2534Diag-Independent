using System;
using System.IO;
using System.Text;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class OfflineCaptureProvenanceTests
    {
        public static int RunAll()
        {
            int assertions = 0;
            using (var source = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            {
                string hash = OfflineCaptureProvenance.HashStream(source);
                if (hash != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                    throw new Exception("SHA-256 known vector failed");
                assertions++;
                if (OfflineCaptureProvenance.IsOriginalSrxCan(hash))
                    throw new Exception("Synthetic input must not match the original SRX source");
                assertions++;
            }

            if (!OfflineCaptureProvenance.IsOriginalSrxCan(
                OfflineCaptureProvenance.OriginalSrxCanSha256.ToLowerInvariant()))
                throw new Exception("Canonical digest comparison must ignore case");
            assertions++;
            if (!OfflineCaptureProvenance.Describe(
                OfflineCaptureProvenance.OriginalSrxCanSha256, true).Contains("CONFIRMED byte match"))
                throw new Exception("Canonical match disclosure missing");
            assertions++;
            if (!OfflineCaptureProvenance.Describe(
                new string('0', 64), true).Contains("NOT A BYTE MATCH"))
                throw new Exception("Mismatch must be explicit");
            assertions++;
            if (OfflineCaptureProvenance.Describe(
                OfflineCaptureProvenance.OriginalSrxCanSha256, false).Contains("CONFIRMED byte match"))
                throw new Exception("Non-native formats must not claim original SRX source match");
            assertions++;

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
                if (OfflineCaptureProvenance.HashFile(path) !=
                    "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                    throw new Exception("File must hash exact bytes");
                assertions++;
                File.AppendAllText(path, "\n");
                if (OfflineCaptureProvenance.HashFile(path) ==
                    "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                    throw new Exception("Byte change was not detected");
                assertions++;
            }
            finally { File.Delete(path); }

            // Hash and parser must observe the same exact bytes; callback begins at byte zero.
            using (var singleSource = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            {
                string observedHash;
                string decoded = OfflineCaptureProvenance.ReadConsistentStream(
                    singleSource, source =>
                    {
                        if (source.Position != 0) throw new Exception("Parser did not start at byte zero");
                        using (var textReader = new StreamReader(source, Encoding.ASCII, false, 1024, true))
                            return textReader.ReadToEnd();
                    }, out observedHash);
                if (decoded != "abc") throw new Exception("Parsed source bytes changed");
                assertions++;
                if (observedHash != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                    throw new Exception("Single-handle hash mismatch");
                assertions++;
                if (!singleSource.CanRead) throw new Exception("Caller stream was disposed");
                assertions++;
            }
            try
            {
                string ignored;
                OfflineCaptureProvenance.ReadConsistentStream(
                    new MemoryStream(Encoding.ASCII.GetBytes("abc")), (Stream _) =>
                    { throw new FormatException("Bad input"); return ""; }, out ignored);
                throw new Exception("Parser exception swallowed");
            }
            catch (FormatException ex)
            {
                if (ex.Message != "Bad input") throw;
                assertions++;
            }

            return assertions;
        }
    }
}
