# CAN capture CSV version 2

Status: Implemented in `feature/can-capture-v2`; Windows compilation and
offline CSV tests pass. RLink X3 vehicle test is still **UNVERIFIED**.

## New export format

```csv
Index,ReceivedAtUtc,ArbId,ArbId Description,Data
1,"2026-10-12T14:00:00.1234567Z","0x7EA","TCM response","03-62-F1-90"
```

- `Index`: monotonically assigned receive-processing order within the running UI.
- `ReceivedAtUtc`: timestamp captured by the Windows host when the J2534
  message is placed into the application's processing queue. It is NOT a
  hardware or CAN-bus timestamp and does not establish exact signal timing.
  Historical imported records may leave this field empty.
- `ArbId`: CAN identifier reported by the current J2534 handling path.
- `ArbId Description`: optional application lookup label; not proof of ECU identity.
- `Data`: payload bytes as a hyphen-separated hex string.

CSV export uses the stored processed-message collection, **not** a filtered
or distinct-only view. It preserves repeated data rows. The Export button
requires stopping live capture first.

CSV import accepts old three-column files (`Index,ArbId,Data`) without
inventing timestamps and accepts the new five-column format. It retains CSV
field quoting and embedded commas/quotes in a single physical line.

## Known limitations and next validation gates

1. Original capture cap is 10,000 received records. This is not a continuous
   disk-backed logger and must not be described as lossless.
2. Stopping capture can discard frames still pending in the asynchronous queue.
   A drain-and-flush shutdown is required before asserting complete captures.
3. J2534 status flags, DLC, native timestamps and adapter-overflow counters
   are not yet individually preserved in the exported format.
4. Listen/start/stop currently modifies shared channel filters and buffers.
   Channel ownership and listener isolation must be fixed before concurrent
   diagnostic polling is supported.
5. End-to-end RLink X3 driver/frame correctness requires a controlled
   Windows hardware test and actual saved capture.
6. A build pass or offline CSV test pass does not prove T43 enhanced addressing,
   protocol services, calibration compatibility or vehicle safety.

## Offline tests

`Tests/CanCaptureCsvTests.cs` runs on Windows without connecting to a car.
The automated suite verifies timestamp round-trip, duplicate retention,
CSV escaped fields, historical-format import, reordered columns, and rejection
of malformed records.

## Scope and integration

This change is only to the independent J2534Diag product. Compatible capture
definitions may be shared with SRX-PC-Scanner after its own integration tests.
Do not use this branch for ECU programming, module configuration, or live
unverified GM enhanced requests.
