using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace J2534Diag
{
    public static class BuildIdentity
    {
        public const string Release = "R28";
        public const string SourceCommit = "workspace"; // Stamped by the existing Windows job before compilation.
        public static string Description => Release + " / " + SourceCommit.Substring(0, Math.Min(12, SourceCommit.Length));
        public static string ExecutableSha256()
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(typeof(BuildIdentity).Assembly.Location))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
