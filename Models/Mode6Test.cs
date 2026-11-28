using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace J2534Diag
{
    public class Mode6Test
    {
        public byte MonitorId { get; set; }
        public byte TestId { get; set; }
        public byte UnitsAndScalingId { get; set; }
        public string Label { get; set; }
        public string Units { get; set; }
        public Func<byte[], ushort> DecodeValue { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
