using System;
using System.Collections.Generic;
using System.Linq;
using SAE.J2534;

namespace J2534Diag
{
    /// <summary>
    /// Narrow adapter from the pinned J2534-Sharp library to the pure capture
    /// pump. Preserves the raw per-message metadata without interpreting it.
    /// </summary>
    public sealed class J2534CanFrameSource : ICanFrameBatchSource
    {
        private readonly Channel _channel;

        public J2534CanFrameSource(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public IReadOnlyList<CanReceivedFrame> ReadBatch(int requestedMaximum, int timeoutMilliseconds)
        {
            return ProjectBatch(_channel.GetMessages(requestedMaximum, timeoutMilliseconds));
        }

        // Public projection permits offline tests using the *actual pinned*
        // J2534-Sharp Message/GetMessageResults types, without a connected VCI.
        public static IReadOnlyList<CanReceivedFrame> ProjectBatch(GetMessageResults response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (response.Messages == null)
                return Array.Empty<CanReceivedFrame>();

            string result = response.Result.ToString();
            return response.Messages.Select(msg => new CanReceivedFrame
            {
                Data = msg.Data == null ? null : (byte[])msg.Data.Clone(),
                TimestampRaw = msg.Timestamp,
                RxStatusRaw = unchecked((uint)msg.RxStatus),
                ReadResult = result
            }).ToArray();
        }
    }
}
