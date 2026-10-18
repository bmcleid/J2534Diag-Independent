using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace J2534Diag
{
    // Plain data model: usable by the application and by offline regression tests.
    public class CanCaptureRecord
    {
        public long Index { get; set; }
        // Host UTC time at J2534 receive/queue; not a verified bus-level timestamp.
        // Empty for imports from historical captures without timestamps.
        public string ReceivedAtUtc { get; set; }
        public string ArbId { get; set; }
        public string ArbIdDescription { get; set; }
        public string Data { get; set; }
    }

    public static class CanCaptureCsv
    {
        public const string Header = "Index,ReceivedAtUtc,ArbId,ArbId Description,Data";

        public static void Write(TextWriter writer, IEnumerable<CanCaptureRecord> records)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (records == null) throw new ArgumentNullException(nameof(records));
            writer.WriteLine(Header);
            foreach (var record in records)
            {
                if (record == null) throw new ArgumentException("Capture contains a null record.", nameof(records));
                writer.WriteLine(
                    record.Index.ToString(CultureInfo.InvariantCulture) + "," +
                    Quote(record.ReceivedAtUtc) + "," +
                    Quote(record.ArbId) + "," +
                    Quote(record.ArbIdDescription) + "," +
                    Quote(record.Data));
            }
        }

        public static List<CanCaptureRecord> Read(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            string firstLine = reader.ReadLine();
            if (firstLine == null) throw new FormatException("The capture is empty.");
            var headers = ParseCsvLine(firstLine.TrimStart('\uFEFF')).Select(x => x.Trim()).ToArray();

            int index = Find(headers, "Index");
            int arbId = Find(headers, "ArbId");
            int data = Find(headers, "Data");
            int receivedAtUtc = Find(headers, "ReceivedAtUtc");
            int description = Find(headers, "ArbId Description");
            if (index < 0 || arbId < 0 || data < 0)
                throw new FormatException("Required capture columns: Index, ArbId, Data.");

            var records = new List<CanCaptureRecord>();
            string line;
            int lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = ParseCsvLine(line);
                if (parts.Count != headers.Length)
                    throw new FormatException("Incorrect column count at line " + lineNumber.ToString(CultureInfo.InvariantCulture));
                long recordIndex;
                if (!long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out recordIndex))
                    throw new FormatException("Invalid capture index at line " + lineNumber.ToString(CultureInfo.InvariantCulture));
                records.Add(new CanCaptureRecord
                {
                    Index = recordIndex,
                    ReceivedAtUtc = receivedAtUtc >= 0 ? parts[receivedAtUtc] : "",
                    ArbId = parts[arbId],
                    ArbIdDescription = description >= 0 ? parts[description] : "",
                    Data = parts[data]
                });
            }
            return records;
        }

        private static int Find(string[] headers, string field)
        {
            return Array.FindIndex(headers, h => string.Equals(h, field, StringComparison.OrdinalIgnoreCase));
        }

        private static string Quote(string value)
        {
            var text = value ?? "";
            // This capture format intentionally uses one physical line per CAN record.
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                throw new FormatException("Capture values cannot contain line breaks.");
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; ++i)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        ++i;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(c);
                }
            }
            if (quoted) throw new FormatException("Unterminated quoted CSV field.");
            fields.Add(value.ToString());
            return fields;
        }
    }
}
