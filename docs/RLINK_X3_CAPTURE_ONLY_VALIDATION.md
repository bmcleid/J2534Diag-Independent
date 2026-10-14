# Controlled TOPDON RLink X3 capture-only validation

Status: **procedure for a future supervised stationary test**.
The experimental Windows application has NOT yet been validated against the
actual RLink X3 driver or 2012 Cadillac SRX communication network.

## What capture-only means

The Vehicle screen now defaults to an explicit **Capture-only** choice.
That path opens a J2534 Protocol.CAN channel at 500 kbps, in user-selected
11-bit or 29-bit mode, without asking for a VIN, starting the VIN monitor,
sending ISO-TP flow control, or running the fuel-trim/misfire pollers.

Application diagnostic requests are blocked centrally in capture-only mode,
and selecting the fuel-trim or misfire tabs cannot automatically start polls.
The traditional active VIN/OBD mode remains available only if the capture-only
check box is explicitly unchecked.

**Critical distinction:** a J2534 CAN adapter may still acknowledge CAN frames
at the electrical/protocol level. Capture-only does NOT establish a
physically isolated, galvanically silent, passive, or listen-only interface.
Do not attach to sensitive live programming sessions.

## Current vehicle evidence and what it does NOT establish

- Vehicle: 2012 Cadillac SRX Performance AWD 3.6L V6.
- Car Scanner previously used 11-bit CAN at 500 kbps; this is the grounded
  starting mode for the first controlled capture. Do NOT infer that 29-bit
  traffic will be present, nor that every vehicle module is on this bus.
- Previously observed TCM OBD response ID: 0x7EA.
- Previously observed TCM calibration ID: 24261871.
- Neither value proves an enhanced diagnostic request ID or DID.
- No active TCM queries, DTC clearing, programming, actuation or security
  operations are part of this test.

## Preparations

1. Finish and close Techline Connect/SPS2/GDS2 and all other software using
   J2534. Never run this test concurrently with GM programming.
2. Use a Windows machine with the manufacturer-supported TOPDON RLink X3
   J2534 driver installed. Verify the interface is visible to Windows.
3. Use manufacturer-approved USB and vehicle/OBD connections. Some interfaces
   need vehicle power for particular functions; do not assume USB powers
   every circuit.
4. Keep the car parked, parking brake applied, and all programming/repair
   operations stopped. Begin with short, supervised sessions.
5. Confirm no relevant current vehicle warning, suspected low battery, or
   other condition that makes experiments inadvisable.

## Proposed controlled test sequence

1. Launch an experimental build only after its Windows CI has passed.
2. On Vehicle, select the TOPDON J2534 device. Leave **Capture-only** checked.
3. Choose **11-bit CAN identifier, 500 kbps** for the first 2012 SRX test.
4. Click Connect. It should report capture-only connection. Absence of an
   automatically discovered VIN is expected, NOT proof of an ECU problem.
5. Open CAN Listener, choose Disk log, and select a NEW filename.
6. Record roughly 20–30 seconds while the vehicle is stationary. You may
   need ignition ON / engine OFF to produce network traffic; use only when
   the vehicle and battery are in suitable condition. Do not start driving.
7. Click Stop and wait until the app reports the writer has closed.
8. Preserve both the .csv file and its adjacent .integrity.txt sidecar.
9. Review the integrity sidecar:
   - ApplicationWriteComplete=True is the desired application-level result.
   - QueueFullDrops=0 and AcceptedNotWritten=0.
   - DiskWriterError and ReceiveError empty.
   - J2534DriverReadErrors=0 and J2534BufferOverflowEvents=0.
   - Normal TIMEOUT or BUFFER_EMPTY reads do NOT alone indicate a fault.
   - VehicleBusLossless remains UNVERIFIED even for a clean capture.
10. Shut down cleanly. If Stop retains channel ownership, do NOT force a
    reconnect or launch another J2534 tool until the driver/session can
    safely be closed.

Repeat connection/capture/stop/reconnection only after reviewing the first
log for exceptions, output truncation, driver status and unexpected traffic.

## Interpretation restrictions

- With no diagnostic requests sent, 0x7EA may be absent; absence does NOT
  mean the transmission module is disconnected.
- No frames captured may mean the bus is asleep, incorrect adapter mode,
  bus/filter conditions or driver limitations; don't immediately conclude
  the car has a CAN failure.
- J2534TimestampRaw is an unverified native counter, NOT Windows UTC.
- J2534RxStatusHex is raw vendor/SAE receive-status information; don't
  reinterpret values without checking their application.
- Buffer overflow reports mean loss occurred or may have occurred; the
  actual number of missed CAN frames remains UNKNOWN.

## Evidence to retain after first test

- Exact Windows version, TOPDON J2534 driver and RLink firmware versions
  if available; adapter selection shown by application.
- Connection choice (capture-only), bit width, baud rate and test conditions.
- CSV and integrity sidecar, plus any exact exception/window text.
- Whether Connect/Start/Stop/reconnect worked and how long each took.
- Whether any vehicle warnings appeared; immediately abort testing if they do.

## Not yet verified by automated tests

The source-level checks verify that the capture-only method contains no
application diagnostic SendMessage calls and that known polling pages use the
transmit guard. They are NOT live UI/hardware tests. RLink device electrical
behavior, CAN ACK, internal driver transmissions, module sleep behavior,
native timestamps and channel/filter lifecycle remain UNVERIFIED.
