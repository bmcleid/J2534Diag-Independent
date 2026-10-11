# Independent baseline and verification gates

Upstream: https://github.com/gabehunt/J2534Diag
Baseline upstream commit: d8cca62ebc86c059c9b2c062655123a597380a59
Upstream prerelease: BETA v1.0.6 (published 2026-05-08)
Independent development branch: dev/independent-baseline

## Scope
Preserve J2534Diag as an independent, multi-manufacturer Windows drivability scanner.
SRX-PC-Scanner is a separate vehicle-specific project. Exchange proven transport,
logging and diagnostic components through deliberate, reviewed changes—not by
merging repositories.

## Source-derived build requirements
- Windows and .NET Framework 4.8.1 targeting pack
- Visual Studio/MSBuild with Windows Forms support
- NuGet packages.config restore
- J2534-Sharp 1.0.0-CI00036
- Newtonsoft.Json 13.0.3

The Windows GitHub Actions workflow performs a source build. It does not prove
RLink X3 interoperability or safe operation on a vehicle.

## Source audit findings requiring regression tests
1. CAN CSV export writes Index,ArbId,Data, omitting timestamps.
2. Export iterates displayed _messages rather than the complete retained capture.
3. CAN listener clears shared channel filters when starting.
4. CAN listener clears channel buffers when stopping.
5. The UDS live-data lookup keys only on the low byte of the DID.
6. Protocol selection may persist in OBD mode after seeing an OBD response.
7. ISO-TP reassembly must be isolated per ECU/addressing context and timed out.
8. VIN identification and frame-flow-control routing require recorded-traffic tests.

## Safety and evidence
- No live ECU write, clear-DTC, reset, actuation, security access or programming
  operations are authorized by this baseline.
- A successful compile is not a successful hardware test.
- Generic J2534 support is not proof of a particular device-driver behavior.
- Vehicle-specific GM/T43 request addresses and enhanced PIDs remain UNVERIFIED.

## Completion gates
1. CI successfully restores packages and builds Release.
2. Capture and replay fixtures pass isolated parser and correlation tests.
3. RLink X3 connection and channel lifecycle are verified on the actual device.
4. Raw CAN capture preserves timestamps, bytes and loss indicators.
5. Identification-only diagnostics are tested on a known-safe address pair.
6. Vehicle-specific modules/signals are enabled only after trace-based verification.

## Licensing
The upstream README says MIT; the examined source tree did not show a separate
LICENSE file. Confirm distribution requirements before shipping binaries.
