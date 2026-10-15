using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace J2534Diag
{
    /// <summary>
    /// Offline, display-only adapter for an existing external J2534 logger's
    /// six-column classical CAN CSV. NOT a forensic auditor or a vehicle
    /// decoder. It never opens a device or transmits diagnostics.
    /// </summary>
    public static class NativeSixColumnCanImport
    {
        public const string Header =
            "timestamp_us,can_id_hex,extended,rx_status_hex,dlc,data_hex";

        public static bool HasNativeHeader(string line)
        {
            return string.Equals((line ?? "").TrimStart('\uFEFF').Trim(),
                Header, StringComparison.Ordinal);
        }

        public static List<CanCaptureRecord> Read(TextReader reader, int maximumRows = 1000000)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (maximumRows < 1) throw new ArgumentOutOfRangeException(nameof(maximumRows));
            if (!HasNativeHeader(reader.ReadLine()))
                throw new FormatException("Expected native six-column CAN CSV header.");

            var records = new List<CanCaptureRecord>();
            string line;
            int lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = line.Split(',');
                if (fields.Length != 6)
                    throw Error(lineNumber, "expected six columns");

                if (!uint.TryParse(fields[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out uint timestampUs))
                    throw Error(lineNumber, "invalid unsigned microsecond timestamp");

                uint arbitrationId = ParseHex(fields[1], lineNumber, "CAN ID");
                bool extended;
                if (fields[2] == "0") extended = false;
                else if (fields[2] == "1") extended = true;
                else throw Error(lineNumber, "extended flag must be 0 or 1");

                uint rxStatus = ParseHex(fields[3], lineNumber, "receive status");
                if ((rxStatus & CanFrameClassifier.ControlOrIndicationMask) != 0)
                    throw Error(lineNumber, "J2534 indication/control record is not a CAN bus frame");
                bool statusExtended = (rxStatus & CanFrameClassifier.ExtendedIdentifierFlag) != 0;
                if (extended != statusExtended)
                    throw Error(lineNumber, "extended column disagrees with J2534 RxStatus");
                if (arbitrationId > (extended ? 0x1FFFFFFFu : 0x7FFu))
                    throw Error(lineNumber, "CAN arbitration ID out of range");

                if (!int.TryParse(fields[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int dlc) || dlc < 0 || dlc > 8)
                    throw Error(lineNumber, "classical CAN DLC must be 0–8");

                string hex = fields[5];
                if (hex.Length != dlc * 2)
                    throw Error(lineNumber, "payload length does not match DLC");
                var bytes = new byte[dlc];
                for (int i = 0; i < dlc; i++)
                {
                    if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out bytes[i]))
                        throw Error(lineNumber, "invalid hex payload");
                }

                if (records.Count >= maximumRows)
                    throw Error(lineNumber, "import row limit exceeded");

                records.Add(new CanCaptureRecord
                {
                    Index = records.Count + 1L, // ordinal, NOT elapsed microseconds
                    ReceivedAtUtc = "",         // native source has NO host UTC
                    ArbId = "0x" + arbitrationId.ToString(extended ? "X8" : "X3",
                        CultureInfo.InvariantCulture),
                    ArbIdDescription = "",     // no inferred ECU mapping
                    Data = BitConverter.ToString(bytes),
                    // Original source explicitly names this timestamp_us.
                    // Preserve its numeric value, not an invented UTC time.
                    J2534TimestampRaw = timestampUs.ToString(CultureInfo.InvariantCulture),
                    J2534RxStatusHex = "0x" + rxStatus.ToString("X8",
                        CultureInfo.InvariantCulture),
                    J2534ReadResult = ""        // source has no per-read status
                });
            }
            return records;
        }

        private static uint ParseHex(string input, int line, string field)
        {
            if (input == null || !input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !uint.TryParse(input.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint value))
                throw Error(line, "invalid " + field + " hex value");
            return value;
        }

        private static FormatException Error(int lineNumber, string message)
        {
            return new FormatException("Native CSV row " +
                lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }
    }
}
