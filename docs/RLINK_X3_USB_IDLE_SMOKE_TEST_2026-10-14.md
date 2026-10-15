# TOPDON RLink X3 — first physical-interface smoke test (2026-10-14)

Source: user-observed Windows screenshots and files collected on
2026-10-14 (local EDT); raw files retained by the vehicle project owner.
This report records findings, not a certification of complete CAN-bus capture.

## Physical interface and application

- Windows application J2534Diag launches (user screenshot).
- Registered J2534 driver is listed as `Artdiag RLink` (exact UI spelling).
- Capture-only setting checked; 11-bit CAN identifier, 500 kbps selected.
- TOPDON RLink X3 connected to Windows PC over USB; Cadillac OBD not
  connected for this stage.
- On clicking Connect, screenshot shows:
  `Capture-only CAN channel opened. No application VIN/PID queries will be
  sent by this mode.`
- User reports Disconnect and subsequent Connect both succeeded. This
  establishes a successful application-level idle channel reconnect.
- User performed opt-in Disk log capture and Stop, which displayed:
  `Disk capture closed and application-level writes accounted for.`

## Uploaded CSV and integrity sidecar (directly examined)

File: `CAN-session-20261014-221644.csv`
- UTF-8 CSV file size: **101 bytes**.
- Exactly the 8-field header:
  `Index,ReceivedAtUtc,ArbId,ArbId Description,Data,J2534TimestampRaw,J2534RxStatusHex,J2534ReadResult`
- **0 data rows**; expected without a vehicle CAN bus connected.

File: `CAN-session-20261014-221644.csv.integrity.txt`
- `FinishedAtUtc=2026-10-15T02:18:06.7321790Z`
- `FramesProcessedByPump=0`
- `FramesQueuedForDisk=0`
- `RowsWrittenToWriter=0`
- `QueueFullDrops=0`
- `RejectedAfterWriterFailure=0`
- `AcceptedNotWritten=0`
- `DiskWriterClosed=True`
- `DiskWriterError=`
- `InvalidReceivedFrames=0`
- `SourceOverReturnedFrames=0`
- `CaptureLimitReached=False`
- `ReceiveError=`
- `ApplicationWriteComplete=True`
- `ReadHealthObserved=True`
- `J2534ReadStatusSuccess=0`
- `J2534ReadStatusTimeout=0`
- `J2534ReadStatusBufferEmpty=2385`
- `J2534ReadStatusOther=0`
- `J2534ReadStatusUnavailable=0`
- `J2534DriverReadErrors=0`
- `J2534BufferOverflowEvents=0`
- `J2534OverflowFramesLost=UNKNOWN`
- `VehicleBusLossless=UNVERIFIED`

## Technical conclusions and evidence levels

**CONFIRMED by user screenshots/files:** This Windows J2534Diag build
enumerates the registered Artdiag RLink J2534 interface, opens an
application CAN channel in capture-only mode, disconnects/reconnects as
reported by the user, performs repeated J2534 reads reporting
`BUFFER_EMPTY`, writes the correct eight-field CSV header, and cleanly
flushes and closes the empty capture file without reported application errors.

**UNVERIFIED:** Receiving even one genuine Cadillac CAN frame, timing
of device-side timestamps, driver receive-buffer overflow under load,
power/ignition-dependent interface behavior, filter lifecycle under bus load,
physical bus ACK/listen-only behavior, diagnostics/ECU responses, sustained
vehicle capture, or any module-specific enhanced protocol.

`ApplicationWriteComplete=True` describes application-side accounting
only; no vehicle data were available to assess true network completeness.

## Next discriminating test

Perform one short **stationary** capture connected to the SRX at
11-bit CAN / 500 kbps in capture-only mode. Keep Techline, SPS2/GDS2 and
all other J2534 clients closed. Use ignition ON / engine OFF only if
vehicle battery voltage is suitable; no module programming, actuation
or other diagnostic requests. Save and provide both CSV and integrity
sidecar and any errors. Stop and Disconnect before any further tests.

No physical automotive-network behavior should be considered confirmed
until that separate session.
