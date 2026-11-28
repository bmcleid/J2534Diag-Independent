using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace J2534Diag
{
    // Reads this application's scalar report, never a raw-CAN or external PID log.
    public sealed class SavedSessionRow
    {
        private readonly string[] _cells;
        public int SourceRecord { get; private set; }
        internal SavedSessionRow(int sourceRecord, string[] cells)
        { SourceRecord = sourceRecord; _cells = (string[])cells.Clone(); }
        public string Cell(int column) { return _cells[column]; }
        public string[] Values { get { return (string[])_cells.Clone(); } }
        public bool Matches(string text)
        { return string.IsNullOrEmpty(text) || _cells.Any(c => c.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0); }
    }

    public sealed class SavedSessionReport
    {
        public const int MaximumBytes = 32 * 1024 * 1024;
        public const int MaximumRows = 25000;
        private static readonly string[] Fields =
        {
            "Utc","SessionId","Event","Build","ExecutableSHA256","ConnectionMode","VIN","ResponseSource",
            "PID","Name","Unit","Value","Freshness","AgeMs","ReceivedHostTicks","Details"
        };
        private static readonly string[] SummaryPids = { "0C", "06", "07", "08", "09", "11" };
        public static string[] Headers { get { return (string[])Fields.Clone(); } }
        public string SourcePath { get; private set; }
        public string SourceSha256 { get; private set; }
        public ReadOnlyCollection<SavedSessionRow> Rows { get; private set; }
        public string ExportMetadata { get { return Rows[0].Cell(15); } }
        public int ObservationRows { get { return Rows.Count(r => r.Cell(2) == "Observation"); } }
        public int DisconnectRows { get { return Rows.Count(r => r.Cell(2) == "Disconnect"); } }

        public static SavedSessionReport Read(string path)
        {
            string full = Path.GetFullPath(path);
            byte[] bytes;
            // Deny concurrent write/delete on Windows. Parse and hash the same byte snapshot.
            using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > MaximumBytes)
                    throw new FormatException("Choose a nonempty session report no larger than 32 MiB.");
                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new IOException("The report changed while it was being read.");
                    offset += count;
                }
                if (stream.ReadByte() != -1) throw new IOException("The report changed while it was being read.");
            }
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new FormatException("The report is not valid UTF-8.", ex); }
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var rows = new List<SavedSessionRow>();
            try
            {
                using (var parser = new TextFieldParser(new StringReader(text)))
                {
                    parser.TextFieldType = FieldType.Delimited;
                    parser.SetDelimiters(",");
                    parser.HasFieldsEnclosedInQuotes = true;
                    parser.TrimWhiteSpace = false;
                    string[] header = parser.ReadFields();
                    if (header == null || !header.SequenceEqual(Fields))
                        throw new FormatException("This file is not a J2534Diag 16-column scalar session report.");
                    while (!parser.EndOfData)
                    {
                        var values = parser.ReadFields();
                        if (values == null || values.Length != Fields.Length)
                            throw new FormatException("Incorrect column count at data record " + (rows.Count + 1) + ".");
                        if (rows.Count == MaximumRows)
                            throw new FormatException("The saved report exceeds the 25,000-record review limit.");
                        rows.Add(new SavedSessionRow(rows.Count + 1, values));
                    }
                }
            }
            catch (MalformedLineException ex) { throw new FormatException("Malformed quoted CSV in saved report.", ex); }
            if (rows.Count < 7 || rows[0].Cell(2) != "ExportMetadata" ||
                rows.Count(r => r.Cell(2) == "ExportMetadata") != 1 ||
                !rows[0].Cell(15).StartsWith("Schema=2;", StringComparison.Ordinal))
                throw new FormatException("A complete Schema=2 export metadata record is required.");
            long retained;
            string retainedText = rows[0].Cell(15).Split(';').FirstOrDefault(v => v.StartsWith("RetainedRows=", StringComparison.Ordinal));
            if (retainedText == null || !long.TryParse(retainedText.Substring("RetainedRows=".Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out retained) || retained != rows.Count - 7)
                throw new FormatException("The saved report is incomplete or its retained-row count is inconsistent.");
            var summaries = rows.Skip(rows.Count - 6).ToArray();
            if (rows.Count(r => r.Cell(2) == "PidSummary") != 6 ||
                summaries.Any(r => r.Cell(2) != "PidSummary") ||
                !summaries.Select(r => r.Cell(8)).SequenceEqual(SummaryPids))
                throw new FormatException("The report must end with the six original PID summary records.");
            return new SavedSessionReport { SourcePath = full, SourceSha256 = hash, Rows = rows.AsReadOnly() };
        }
    }
}
