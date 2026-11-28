using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace J2534Diag
{
    // GM GMLAN reference definitions; these entries do not establish installed-ECU support.
    public static class Mode6TidMap
    {
        public static readonly List<Mode6Test> MisfireTests = new List<Mode6Test>()
        {
            new Mode6Test { MonitorId = 0xA2, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 1 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA2, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 1 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA3, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 2 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA3, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 2 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA4, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 3 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA4, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 3 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA5, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 4 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA5, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 4 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA6, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 5 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA6, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 5 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA7, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 6 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA7, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 6 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA8, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 7 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA8, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 7 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA9, TestId = 0x0B, UnitsAndScalingId = 0x24, Label = "Cyl 8 EWMA", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
            new Mode6Test { MonitorId = 0xA9, TestId = 0x0C, UnitsAndScalingId = 0x24, Label = "Cyl 8 Last/current", Units = "counts", DecodeValue = d => (ushort)((d[0] << 8) | d[1]) },
        };
    }

}
