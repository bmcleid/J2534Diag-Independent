# CAN capture — deterministic fake-source verification

Development branch: `feature/can-capture-fake-transport-tests`.
This is an isolated extension of `feature/can-capture-reliability`.

## Purpose

The production CAN listener previously contained its own receive, frame
interpretation, time stamping and record-count logic. Those branches were
difficult to test without a live J2534 device.

The new `CanCapturePump` is used by the Windows CAN listener and can
alternatively receive frames from a deterministic `ICanFrameBatchSource`
implementation with **no device, vehicle, CAN transmission or programming**.

## Verified by source and automated offline tests (not hardware)

- Reader cancellation is checked before starting a *new* driver read.
  Any batch already returned from the driver is fully processed up to
  the configured capture limit, even when cancellation occurs mid-batch.
- A bounded read asks for no more frames than remaining capacity.
  A source that over-returns frames is counted in
  `ExcessReturnedFrames` (those records are not retained).
- Malformed messages shorter than the 4-byte J2534 CAN-ID prefix are
  reported and not counted as stored records.
- Both 11-bit-sized and 29-bit-sized CAN IDs are formatted from the four
  reported identifier bytes without attributing them to an ECU.
- Host timestamps must originate from a UTC clock and are explicitly
  **not hardware CAN timestamps**.
- Receive exceptions are propagated to the listener, which records a
  driver failure instead of silently treating the session as complete.
- Empty batches do not schedule unnecessary grid refreshes.

## Test suite

`Tests/CanCapturePumpTests.cs` contains seven new offline scenarios:
1. Cancellation after the first frame retains all frames of a returned batch.
2. Capacity reached with source over-return preserves the limit and flags excess.
3. Malformed frame does not consume the accepted-frame limit.
4. ID widths, record bytes, and UTC formatting remain intact.
5. Empty batch plus cancellation terminates without false frames.
6. Injected receive failure is surfaced to the caller.
7. A local-time clock is rejected.

They run alongside the seven prior tests for CSV backward compatibility
and exclusive in-process channel ownership.

The Windows workflow builds the application and the console test runner
and executes those tests on a Microsoft-hosted Windows runner.

## Evidence boundaries

- The fake source is not the installed TOPDON RLink X3 driver.
- A clean run cannot validate J2534 function ABI, driver read semantics,
  CAN hardware timestamp availability, filter behavior, interface reconnect,
  vehicle signal definitions, or the particular 2012 SRX CAN network.
- `GetMessages` status/timeout behavior still requires device-specific
  testing. A genuine driver error remains visible as an error rather than
  being silently reclassified as an empty batch.
- A full returned batch is processed, but any frames still in the driver's
  internal receive queue when the user stops are **not guaranteed recorded**.
- The capture retains the 10,000-frame in-memory cap; it is not a
  continuous disk-backed or lossless logger.
- The vehicle's observed TCM calibration ID `24261871` and OBD response
  ID `0x7EA` remain separate evidence. This feature supplies **no** T43
  enhanced addressing or undocumented diagnostic requests.

## Next engineering gate

Use a controlled RLink X3 Windows bench/vehicle session to verify idle
read returns, response flags, filter start/stop, channel ownership,
reconnect, and actual capture CSV output. Keep diagnostic writes, DTC
clears, security access and module programming out of the discovery test.
