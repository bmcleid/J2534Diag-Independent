using Core.Bus;
using Core.Ecu;
using Shim.Ipc;
using Common.Signals;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
    Environment.GetEnvironmentVariable("SRX_MANAGED_SIM_CI") != "ALLOW")
    throw new InvalidOperationException("Disposable offline CI only");
var bus = new VirtualBus();
string profile = Environment.GetEnvironmentVariable("SRX_MANAGED_PROFILE") ?? "normal";
var node = new EcuNode {
    Name = "SYNTHETIC-ECM-NOT-SRX", PersonaId = "gmw3110",
    PhysicalRequestCanId = 0x7E0, UsdtResponseCanId = 0x7E8, UudtResponseCanId = 0x5E8,
    Mode1Supported = new HashSet<byte> { 6, 7, 8, 9, 0x0C, 0x11 }
};
bus.LogJ2534 = line => {
    Console.WriteLine("[WIRE] " + line);
    // Deliberate host-only fault: retire the synthetic sender after the FF
    // has been read, before the client's FC can release any CFs. No core edits.
    if (profile == "partial" && line.Contains("10 14 49 02 01")) {
        node.State.Fragmenter.OnFlowControl((Common.IsoTp.FlowStatus)2, 0, 0);
        Console.WriteLine("MANAGED_PARTIAL_VIN_CF_WITHHELD");
    }
};
node.EngineModel.SetOverride(SignalId.EngineRpm, 1000);
node.EngineModel.SetOverride(SignalId.ThrottlePosition, 20);
// Deliberately fictional identity; never a vehicle VIN.
node.SetMode09Info(0x02, System.Text.Encoding.ASCII.GetBytes("TESTVIN1234567890"));
if (Environment.GetEnvironmentVariable("SRX_MANAGED_PROFILE") != "silent") bus.AddNode(node);
Console.WriteLine("MANAGED_PROFILE=" + Environment.GetEnvironmentVariable("SRX_MANAGED_PROFILE"));
await using var server = new NamedPipeServer(bus, Console.WriteLine);
server.Start();
Console.WriteLine("MANAGED_VIRTUAL_PIPE_READY SYNTHETIC_ONLY");
Console.Out.Flush();
for (int i = 0; i < 1200; i++) {
    if (File.Exists(Environment.GetEnvironmentVariable("SRX_MANAGED_BREAK_FILE"))) {
        Console.WriteLine("MANAGED_VIRTUAL_PIPE_INTERRUPTED"); Console.Out.Flush();
        await server.StopAsync();
        await Task.Delay(TimeSpan.FromSeconds(10));
        return;
    }
    if (File.Exists(Environment.GetEnvironmentVariable("SRX_MANAGED_SILENCE_FILE"))) {
        node.Mode1Supported = new HashSet<byte>();
        Console.WriteLine("MANAGED_VIRTUAL_REPLIES_DISABLED");
        Console.Out.Flush();
        break;
    }
    await Task.Delay(50);
}
await Task.Delay(TimeSpan.FromSeconds(15));
await server.StopAsync();



