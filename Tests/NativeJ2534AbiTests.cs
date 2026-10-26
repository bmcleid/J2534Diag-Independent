using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class NativeJ2534AbiTests
    {
        private static int _assertions;
        private static void Check(bool ok, string message)
        {
            _assertions++;
            if (!ok) throw new Exception(message);
        }

        public static int RunAll()
        {
            Check(Marshal.SizeOf(typeof(NativePassThruMsg)) == 4152,
                "Native PASSTHRU_MSG must occupy 24+4128 bytes");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "ProtocolID").ToInt32() == 0,
                "ProtocolID native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "RxStatus").ToInt32() == 4,
                "RxStatus native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "TxFlags").ToInt32() == 8,
                "TxFlags native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "Timestamp").ToInt32() == 12,
                "Timestamp native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "DataSize").ToInt32() == 16,
                "DataSize native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "ExtraDataIndex").ToInt32() == 20,
                "ExtraDataIndex native offset wrong");
            Check(Marshal.OffsetOf(typeof(NativePassThruMsg), "Data").ToInt32() == 24,
                "Native inline bytes do not start at offset 24");
            var data = typeof(NativePassThruMsg).GetField("Data");
            var inline = (MarshalAsAttribute)Attribute.GetCustomAttribute(data,
                typeof(MarshalAsAttribute));
            Check(inline != null && inline.Value == UnmanagedType.ByValArray &&
                inline.SizeConst == 4128, "Unmanaged data field must contain 4128 inline bytes");

            var delegates = typeof(NativeJ2534StdCallApi).GetNestedTypes(
                BindingFlags.NonPublic).Where(x => typeof(MulticastDelegate).IsAssignableFrom(x));
            Check(delegates.Count() == 7, "Expected exactly seven J2534 receive-only exports");
            foreach (var type in delegates)
            {
                var calling = (UnmanagedFunctionPointerAttribute)Attribute.GetCustomAttribute(
                    type, typeof(UnmanagedFunctionPointerAttribute));
                Check(calling != null && calling.CallingConvention == CallingConvention.StdCall,
                    "J2534 stdcall calling convention not used");
                Check(!type.Name.ToLowerInvariant().Contains("write"),
                    "Native adapter unexpectedly includes write transmit export");
            }

            try
            {
                new NativeJ2534StdCallApi("relative-not-a-driver.dll");
                throw new Exception("Native adapter accepted an unverified relative DLL path");
            }
            catch (ArgumentException)
            {
                ++_assertions;
            }
            return _assertions;
        }
    }
}
