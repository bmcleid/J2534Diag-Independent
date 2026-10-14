using System;

namespace J2534Diag
{
    /// <summary>
    /// Fail-closed application-level safety boundary: capture-only CAN
    /// connections must not send diagnostic payloads. This does not control
    /// electrical CAN acknowledgements from the J2534 adapter.
    /// </summary>
    public static class DiagnosticTransmitPolicy
    {
        public static void RequireActiveDiagnostics(bool captureOnlyMode)
        {
            if (captureOnlyMode)
                throw new InvalidOperationException(
                    "Diagnostic transmission is disabled in capture-only mode.");
        }
    }
}
