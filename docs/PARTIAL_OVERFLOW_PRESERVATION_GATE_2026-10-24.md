# Partial return on J2534 BUFFER_OVERFLOW: offline-safe receiver groundwork

## Actual source limitation
J2534Diag pins J2534-Sharp 1.0.0-CI00036. Its current Channel.GetMessages throws on BUFFER_OVERFLOW before a GetMessageResults response is exposed to J2534CanFrameSource. **The production RLink reader still cannot recover native pNumMsgs/frames accompanying an overflow.** This branch does not change that or claim to have repaired the vehicle-connected overflow.

## Implemented lower-level safe behavior
CanReceiveBufferOverflowException can optionally carry an explicitly supplied partial returned frame collection. Legacy construction defaults to an empty collection. CanCapturePump counts the reported overflow, preserves valid partial rows (including raw metadata), rejects malformed rows and overlarge return lists, stores accepted records, then always rethrows the original overflow to preserve incomplete capture status. The integrity sidecar distinguishes frames supplied by the source from accepted pump records; the number of lost vehicle frames remains UNKNOWN and VehicleBusLossless remains UNVERIFIED.

J2534CanFrameSource.FromNativeOverflowResult provides a projection using real pinned NuGet GetMessageResults when a future source actually supplies a valid BUFFER_OVERFLOW result. This is a synthetic/offline-only entry point today. No native DLL calls, receive settings, filters, buffer clears or transmissions have been modified. No additional hardware test is justified until a source-to-binary verified native boundary exposes actual returned counts.

## Required regression coverage
- Synthetic overflow with 2 valid partial frames: both saved, metadata preserved, overflow still thrown.
- Synthetic overflow with an invalid short record and a valid record: only validated data accepted.
- Synthetic over-returned 201 frames despite 200 requested: all rejected, overflow still reported.
- Legacy zero-frame exception preserves historical reporting.
- Projection using real pinned J2534-Sharp Message/GetMessageResults objects.
- Incomplete sidecar even if disk writer successfully persisted every available partial row.
- Existing 10k in-memory, unbounded disk and first-read parity suites unchanged.

## Vehicle-specific established facts
Oct23 J2534Diag captured 95 frames before an explicit buffer overflow. Oct24 multiple Disk log tests returned zero application-accepted frames on first-read overflows (46.8294ms and 42.6851ms), while one in-memory test reached its 10,000-record cap. The independent OneApp native receiver recorded 25,772 accepted frames without reported 0x12 on another session. None of these observations establishes the native 0x12 pNumMsgs value in the J2534Diag failure.

## Next engineering gate
Run Windows CI once, review the actual binary-specific J2534-Sharp receive method, and only then investigate a separately testable native read transport. Avoid mixing independent OneApp handles with J2534-Sharp channel handles; do not guess DLL export names, driver internals or header structs.