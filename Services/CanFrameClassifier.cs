using System;

namespace J2534Diag
{
    /// <summary>
    /// Strict classical-CAN validation for messages returned by J2534-Sharp.
    /// Based on the separately tested SRX-PC-Scanner native receive decoder,
    /// but implemented against the independent scanner's managed frame type.
    /// This is framing validation, not GM/TCM signal decoding.
    /// </summary>
    public enum CanFrameRejectionKind
    {
        None = 0,
        J2534Indication = 1,
        MissingOrShortHeader = 2,
        ExceedsClassicalCanLength = 3,
        ArbitrationIdOutOfRange = 4
    }

    public static class CanFrameClassifier
    {
        // Native J2534-Sharp RxFlag labels:
        // TX_MSG_TYPE=1, START_OF_MESSAGE=2, RX_BREAK=4,
        // TX_INDICATION=8, CAN_29BIT_ID=0x100.
        // Status/indication records are not received CAN-bus data frames.
        public const uint ControlOrIndicationMask = 0x0000000F;
        public const uint ExtendedIdentifierFlag = 0x00000100;

        public static bool TryValidate(
            CanReceivedFrame frame, out uint arbitrationId,
            out CanFrameRejectionKind rejection)
        {
            arbitrationId = 0;
            rejection = CanFrameRejectionKind.None;

            if (frame?.RxStatusRaw is uint status &&
                (status & ControlOrIndicationMask) != 0)
            {
                rejection = CanFrameRejectionKind.J2534Indication;
                return false;
            }

            var data = frame?.Data;
            if (data == null || data.Length < 4)
            {
                rejection = CanFrameRejectionKind.MissingOrShortHeader;
                return false;
            }

            // J2534 CAN Data contains 4 arbitration-ID bytes followed by
            // 0–8 classical CAN payload bytes. Do not label longer input
            // as a valid classical CAN frame.
            if (data.Length > 12)
            {
                rejection = CanFrameRejectionKind.ExceedsClassicalCanLength;
                return false;
            }

            uint id = ((uint)data[0] << 24) | ((uint)data[1] << 16) |
                      ((uint)data[2] << 8) | data[3];
            bool extended = frame.RxStatusRaw.HasValue &&
                            (frame.RxStatusRaw.Value & ExtendedIdentifierFlag) != 0;
            uint maximumId = extended ? 0x1FFFFFFF : 0x7FF;
            if (id > maximumId)
            {
                rejection = CanFrameRejectionKind.ArbitrationIdOutOfRange;
                return false;
            }

            arbitrationId = id;
            return true;
        }
    }
}
