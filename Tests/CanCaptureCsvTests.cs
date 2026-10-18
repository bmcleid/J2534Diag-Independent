using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CanCaptureCsvTests
    {
        private static int _assertions;

        private static void Assert(bool ok, string reason)
        {
            ++_assertions;
            if (!ok) throw new Exception(reason);
        }

        public static int Main()
        {
            try
            {
                RoundTripWithTimestampAndEscapedFields();
                ReadLegacyCapture();
                ReadReorderedHeader();
                RejectMalformedRows();
                RejectUnterminatedQuotes();
                Console.WriteLine("PASS: " + _assertions + " assertions across five CAN capture regression tests.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static void RoundTripWithTimestampAndEscapedFields()
        {
            var writer = new StringWriter();
            CanCaptureCsv.Write(writer, new[]
            {
                new CanCaptureRecord {
                    Index=1, ReceivedAtUtc="2026-10-12T14:00:00.1234567Z",
                    ArbId="0x7EA", ArbIdDescription="TCM, response",
                    Data="03-62-F1-90"
                },
                new CanCaptureRecord {
                    Index=2, ReceivedAtUtc="2026-10-12T14:00:00.1234570Z",
                    ArbId="0x7E8", ArbIdDescription="ECM \"quoted\"",
                    Data="03-41-0C-00"
                }
            });
            string csv = writer.ToString();
            Assert(csv.StartsWith(CanCaptureCsv.Header), "Header has missing columns");
            Assert(csv.Contains("\"TCM, response\""), "Comma must be escaped");
            Assert(csv.Contains("\"ECM \"\"quoted\"\"\""), "Quotes must be escaped");
            var records = CanCaptureCsv.Read(new StringReader(csv));
            Assert(records.Count == 2, "Lost duplicate/filtered rows");
            Assert(records[0].Index == 1 && records[1].Index == 2, "Wrong frame order");
            Assert(records[0].ReceivedAtUtc == "2026-10-12T14:00:00.1234567Z", "Timestamp changed");
            Assert(records[0].ArbId == "0x7EA", "Arbitration ID changed");
            Assert(records[0].ArbIdDescription == "TCM, response", "Comma round-trip failed");
            Assert(records[1].ArbIdDescription == "ECM \"quoted\"", "Escaped quote round-trip failed");
        }

        private static void ReadLegacyCapture()
        {
            var csv = "Index,ArbId,Data\n1,\"0x7EA\",\"03-62-11-22\"\n2,\"0x7EA\",\"03-62-11-22\"\n";
            var result = CanCaptureCsv.Read(new StringReader(csv));
            Assert(result.Count == 2, "Legacy duplicates must be retained");
            Assert(result[0].ReceivedAtUtc == "", "Unknown legacy timestamp must remain blank");
            Assert(result[1].Index == 2, "Legacy index wrong");
        }

        private static void ReadReorderedHeader()
        {
            var csv = "\uFEFFData,ArbId Description,Index,ArbId,ReceivedAtUtc\nAA-BB,\"ECM, engine\",7,0x7E8,2026-10-12T14:00:01Z\n";
            var result = CanCaptureCsv.Read(new StringReader(csv));
            Assert(result.Count == 1, "Reordered header count");
            Assert(result[0].ArbId == "0x7E8", "Reordered ID");
            Assert(result[0].ArbIdDescription == "ECM, engine", "Reordered quoted description");
            Assert(result[0].ReceivedAtUtc == "2026-10-12T14:00:01Z", "Reordered timestamp");
        }

        private static void RejectMalformedRows()
        {
            try
            {
                CanCaptureCsv.Read(new StringReader("Index,ArbId,Data\nX,0x7EA,01-02\n"));
                throw new Exception("Did not reject invalid index");
            }
            catch (FormatException) { }
            ++_assertions;
        }

        private static void RejectUnterminatedQuotes()
        {
            try
            {
                CanCaptureCsv.Read(new StringReader("Index,ArbId,Data\n1,0x7EA,\"broken\n"));
                throw new Exception("Did not reject invalid quoted field");
            }
            catch (FormatException) { }
            ++_assertions;
        }
    }
}
