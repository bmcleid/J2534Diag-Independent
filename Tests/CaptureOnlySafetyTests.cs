using System;
using System.IO;
using J2534Diag;

namespace J2534Diag.Tests
{
    internal static class CaptureOnlySafetyTests
    {
        private static int _assertions;
        private static void Check(bool value, string reason)
        {
            ++_assertions;
            if (!value) throw new Exception(reason);
        }

        private static string Source(string relative)
        {
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string path = Path.Combine(root, relative);
            if (!File.Exists(path))
                throw new FileNotFoundException("Safety source inspection requires repository sources.", path);
            return File.ReadAllText(path);
        }

        public static int RunAll()
        {
            DiagnosticTransmitPolicyBlocksCaptureOnly();
            CaptureOnlyConnectionSourceHasNoDiagnosticTX();
            UIAndOtherDiagnosticPagesRespectCaptureOnly();
            EntireApplicationUsesCentralTXGate();
            return _assertions;
        }

        private static void DiagnosticTransmitPolicyBlocksCaptureOnly()
        {
            DiagnosticTransmitPolicy.RequireActiveDiagnostics(false);
            Check(true, "Active diagnostics policy should permit requested transmissions");
            try
            {
                DiagnosticTransmitPolicy.RequireActiveDiagnostics(true);
                throw new Exception("Capture-only diagnostic request was permitted");
            }
            catch (InvalidOperationException e)
            {
                Check(e.Message.Contains("capture-only"), "Safety guard message was lost");
            }
        }

        private static void CaptureOnlyConnectionSourceHasNoDiagnosticTX()
        {
            string source = Source(Path.Combine("Services", "J2534Manager.cs"));
            int start = source.IndexOf("public void InitializeCaptureOnly(", StringComparison.Ordinal);
            int end = source.IndexOf("private void InitializeChannel(", start + 1, StringComparison.Ordinal);
            Check(start >= 0 && end > start, "Capture-only connection implementation not located");
            string method = source.Substring(start, end - start);
            Check(method.Contains("Protocol.CAN") && method.Contains("Baud.CAN_500000"),
                "Capture-only method did not configure CAN/500k");
            Check(method.Contains("CaptureOnlyMode = true;"), "Capture-only state is not committed on success");
            Check(!method.Contains(".SendMessage(") && !method.Contains("SendDiagnosticMessage("),
                "Capture-only connection code directly transmits diagnostic messages");
            Check(!method.Contains("InitializeChannel(") && !method.Contains("_monitorThread.Start("),
                "Capture-only connection unexpectedly starts the VIN interrogation path");
            Check(!method.Contains(".StartMsgFilter(") && !method.Contains(".ClearMsgFilters("),
                "Capture-only connect unexpectedly installs or clears channel filters");
            Check(source.Contains("DiagnosticTransmitPolicy.RequireActiveDiagnostics(CaptureOnlyMode);"),
                "Central manager diagnostic transmit gate is missing");
        }

        private static void UIAndOtherDiagnosticPagesRespectCaptureOnly()
        {
            string vehicle = Source(Path.Combine("Forms", "VehicleTabForm.cs"));
            string fuel = Source(Path.Combine("Forms", "FuelTrimForm.cs"));
            string misfire = Source(Path.Combine("Forms", "MisfireForm.cs"));
            string main = Source(Path.Combine("Forms", "MainForm.cs"));
            Check(vehicle.Contains("Checked = true") && vehicle.Contains("InitializeCaptureOnly("),
                "Capture-only was not offered as default connection choice");
            Check(fuel.Contains("if (j2534Manager.CaptureOnlyMode) return;"),
                "Fuel-trim polling is not capture-only guarded");
            Check(misfire.Contains("if (j2534Manager.CaptureOnlyMode) return;"),
                "Misfire polling is not capture-only guarded");
            Check(main.Contains("j2534Manager?.CaptureOnlyMode == true"),
                "Diagnostic tab selection does not respect capture-only");
            Check(!HasDirectChannelSend(fuel) && !HasDirectChannelSend(misfire),
                "Diagnostic screen bypasses central transmit guard");
            Check(fuel.Contains("j2534Manager.SendDiagnosticMessage(") &&
                  misfire.Contains("j2534Manager.SendDiagnosticMessage("),
                "Diagnostic screen is not using the central transmit gate");
        }

        private static void EntireApplicationUsesCentralTXGate()
        {
            string root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            string[] folders = { "Forms", "Services", "Controls", "Models", "Maps" };
            int approvedSends = 0;
            foreach (var folder in folders)
            {
                string directory = Path.Combine(root, folder);
                foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal))
                            continue;
                        if (trimmed.Contains(".SendMessage(") ||
                            trimmed.Contains(".SendMessages(") ||
                            trimmed.Contains(".StartPeriodicMessage("))
                        {
                            if (Path.GetFileName(file) == "J2534Manager.cs" &&
                                trimmed == "Channel.SendMessage(message);")
                            {
                                approvedSends++;
                                continue;
                            }
                            throw new Exception("Unreviewed direct J2534 transmit path: " +
                                Path.GetFileName(file) + ": " + trimmed);
                        }
                    }
                }
            }
            Check(approvedSends == 1, "Application transmit paths are not centralized through J2534Manager");
        }

        private static bool HasDirectChannelSend(string source)
        {
            using (var reader = new StringReader(source))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (line.Contains("Channel.SendMessage(") ||
                        line.Contains("channel.SendMessage("))
                        return true;
                }
            }
            return false;
        }
    }
}
