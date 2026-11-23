using Core.Bus;
using Core.Ecu;
using Shim.Ipc;
using Common.Signals;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
    Environment.GetEnvironmentVariable("SRX_MANAGED_SIM_CI") != "ALLOW")
    throw new InvalidOperationException("Disposable offline CI only");
var bus = new VirtualBus();
bus.LogJ2534 = line => Console.WriteLine("[WIRE] " + line);
var node = new EcuNode {
    Name = "SYNTHETIC-ECM-NOT-SRX", PersonaId = "gmw3110",
    PhysicalRequestCanId = 0x7E0, UsdtResponseCanId = 0x7E8, UudtResponseCanId = 0x5E8,
    Mode1Supported = new HashSet<byte> { 6, 7, 8, 9, 0x0C, 0x11 }
};
node.EngineModel.SetOverride(SignalId.EngineRpm, 1000);
node.EngineModel.SetOverride(SignalId.ThrottlePosition, 20);
bus.AddNode(node);
await using var server = new NamedPipeServer(bus, Console.WriteLine);
server.Start();
Console.WriteLine("MANAGED_VIRTUAL_PIPE_READY SYNTHETIC_ONLY");
Console.Out.Flush();
for (int i = 0; i < 1200; i++) {
    if (File.Exists(Environment.GetEnvironmentVariable("SRX_MANAGED_SILENCE_FILE"))) {
        node.Mode1Supported = new HashSet<byte>();
        Console.WriteLine("MANAGED_VIRTUAL_REPLIES_DISABLED");
        Console.Out.Flush();
        break;
    }
    await Task.Delay(50);
}
await Task.Delay(TimeSpan.FromSeconds(10));
await server.StopAsync();
