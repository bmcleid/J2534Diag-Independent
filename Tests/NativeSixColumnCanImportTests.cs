using System;
using System.Globalization;
using System.IO;
using System.Text;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class NativeSixColumnCanImportTests
    {
        private static int _assertions;
        private static void Check(bool valid, string reason)
        {
            ++_assertions;
            if (!valid) throw new Exception(reason);
        }

        public static int RunAll()
        {
            PreserveTimestampUnitsAndMissingHostUtc();
            Read11And29BitClassicalCan();
            FailOnDlcAndStatusDisagreement();
            RejectNonHexAndInvalidId();
            RejectUnknownSchemaAndRowLimit();
            AcceptEmptyNativeCapture();
            HandleSyntheticCaptureAtOriginalFrameCount();
            return _assertions;
        }

        private static string Text(params string[] rows)
        {
            return NativeSixColumnCanImport.Header + "\n" +
                string.Join("\n", rows) + "\n";
        }

        private static void Throws(string input, string explanation, string expectedMessage)
        {
            try
            {
                NativeSixColumnCanImport.Read(new StringReader(input));
                throw new Exception(explanation);
            }
            catch (FormatException ex)
            {
                Check(ex.Message.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Wrong error: " + ex.Message);
            }
        }

        private static void PreserveTimestampUnitsAndMissingHostUtc()
        {
            // The timestamp is the known *shape* of the original native file;
            // this test row's payload is synthetic, not from the Cadillac.
            var result = NativeSixColumnCanImport.Read(new StringReader(
                Text("2160911470,0x0C1,0,0x00000000,3,010203")));
            Check(result.Count == 1, "Expected one view-only row");
            Check(result[0].Index == 1L, "Index must be ordinal, not raw microseconds");
            Check(result[0].ReceivedAtUtc == "", "Host UTC invented from native timestamp");
            Check(result[0].J2534TimestampRaw == "2160911470", "Original timestamp value changed");
            Check(result[0].J2534RxStatusHex == "0x00000000", "RxStatus lost");
            Check(result[0].ArbId == "0x0C1", "Native standard CAN ID lost");
            Check(result[0].Data == "01-02-03", "Payload bytes not preserved");
            Check(result[0].ArbIdDescription == "" && result[0].J2534ReadResult == "",
                "Missing ECU mapping or batch status was invented");
        }

        private static void Read11And29BitClassicalCan()
        {
            var output = NativeSixColumnCanImport.Read(new StringReader(Text(
                "1,0x07E8,0,0x00000000,0,",
                "2,0x18DAF111,1,0x00000100,8,0001020304050607")));
            Check(output.Count == 2, "Mixed standard/extended rows dropped");
            Check(output[0].Data == "" && output[0].ArbId == "0x7E8",
                "Zero-length classical CAN row failed");
            Check(output[1].ArbId == "0x18DAF111", "29-bit ID changed");
            Check(output[1].Data == "00-01-02-03-04-05-06-07",
                "8-byte payload changed");
            Check(output[1].J2534TimestampRaw == "2", "Raw timestamp lost");
        }

        private static void FailOnDlcAndStatusDisagreement()
        {
            Throws(Text("1,0x7EA,0,0x00000000,3,AABB"), "DLC mismatch accepted", "payload length");
            Throws(Text("1,0x18DAF111,1,0x00000000,2,AABB"),
                "Extended flag mismatch accepted", "extended");
            Throws(Text("1,0x7EA,0,0x00000100,2,AABB"),
                "Status mismatch accepted", "extended");
            Throws(Text("1,0x7EA,0,0x00000000,9,010203040506070809"),
                "CAN FD labelled classic", "DLC");
        }

        private static void RejectNonHexAndInvalidId()
        {
            Throws(Text("1,0x7EA,0,0x00000000,2,ZZ11"), "Nonhex accepted", "hex payload");
            Throws(Text("1,0x800,0,0x00000000,1,01"),
                "Out-of-range standard CAN ID accepted", "out of range");
            Throws(Text("1,0x20000000,1,0x00000100,1,01"),
                "Out-of-range extended ID accepted", "out of range");
            Throws(Text("UNKNOWN,0x7EA,0,0x00000000,1,01"),
                "Unrecognized time accepted", "timestamp");
        }

        private static void RejectUnknownSchemaAndRowLimit()
        {
            Throws("Index,ArbId,Data\n1,0x7EA,01\n", "Upstream schema accepted as native", "header");
            Throws(Text("1,0x7EA,0,0x00000000,1,01") +
                "2,0x7E8,0,0x00000000,1,02\n",
                "Additional native rows unexpectedly accepted by one-row limit",
                "row limit", maximumRows: 1);
        }

        private static void Throws(string input, string explanation,
            string expectedMessage, int maximumRows)
        {
            try
            {
                NativeSixColumnCanImport.Read(new StringReader(input), maximumRows);
                throw new Exception(explanation);
            }
            catch (FormatException ex)
            {
                Check(ex.Message.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Wrong limited import error: " + ex.Message);
            }
        }

        private static void AcceptEmptyNativeCapture()
        {
            Check(NativeSixColumnCanImport.Read(new StringReader(
                NativeSixColumnCanImport.Header + "\n")).Count == 0,
                "Empty but correctly labelled file should be viewable");
        }

        private static void HandleSyntheticCaptureAtOriginalFrameCount()
        {
            const int count = 25791;
            const uint first = 2160911470U;
            const uint last = 2171037559U;
            var csv = new StringBuilder(NativeSixColumnCanImport.Header + "\n");
            for (int i = 0; i < count; i++)
            {
                uint timestamp = first + (uint)((last - (ulong)first) * (ulong)i / (count - 1));
                csv.Append(timestamp.ToString(CultureInfo.InvariantCulture));
                csv.Append(",0x0C1,0,0x00000000,1,");
                csv.Append(i % 2 == 0 ? "AA" : "BB");
                csv.Append('\n');
            }
            var output = NativeSixColumnCanImport.Read(new StringReader(csv.ToString()));
            Check(output.Count == count, "Synthetic 25,791-row import truncated");
            Check(output[0].J2534TimestampRaw == first.ToString(CultureInfo.InvariantCulture),
                "Synthetic first timestamp changed");
            Check(output[count - 1].J2534TimestampRaw == last.ToString(CultureInfo.InvariantCulture),
                "Synthetic final timestamp changed");
            Check(output[count - 1].Index == count, "Row ordinal changed at scale");
            Check(output[100].ReceivedAtUtc == "", "Host UTC fabricated in synthetic replay");
        }
    }
}
