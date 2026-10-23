# Oct 23 live-overflow reproduction: synthetic regression

## Vehicle evidence
The user-supplied first live J2534Diag Cadillac capture contains 95 accepted and disk-written rows, one partial `TIMEOUT` read carrying data, then one driver-reported `BUFFER_OVERFLOW`. Integrity status reports zero queue drops, zero unwritten frames, zero malformed records, and `VehicleBusLossless=UNVERIFIED`. These are distinct from the separate native SRX-PC-Scanner recordings.

## Actual receiver implementation
`CanCapturePump` requests up to 200 records with a 5 ms timeout per `GetMessages`. The `J2534CanFrameSource` adapter projects `GetMessageResults.Messages` even if `.Result` says `TIMEOUT`, and throws on explicit `J2534Exception(ResultCode.BUFFER_OVERFLOW)`. The pump retains `LastSummary` on failure. No evidence supports converting `TIMEOUT` into 95 errors or estimating the number of frames lost.

## New hardware-free regression
A deterministic fake returns 95 valid frames with native raw timestamps and `TIMEOUT` metadata, then throws one explicit buffer-overflow exception. Assertions establish all 95 records survive, indices and native metadata stay intact, one timeout and one driver overflow are counted, no false invalid/over-returned frames are counted, and the error is not suppressed.

## Unresolved origin
Whether overflow arose in TOPDON firmware/driver buffering, host scheduling, the pinned J2534-Sharp wrapper or the 200-message/5ms read settings remains **UNVERIFIED**. This test validates error accounting, not throughput. **DO NOT RETUNE POLLING OR RECORDER PARAMETERS based on the short capture alone.** Prior separate RLink native trace recovered far more frames, which establishes potential device throughput under a different implementation but does not establish equivalent configurations.

## Next verification gate
Run Windows CI on this source-bearing branch. Then investigate pinned J2534-Sharp `GetMessages` timeout/batching implementation and native code with authoritative source before changing fetch size or timeout; if no proven defect, seek one controlled A/B hardware capture that changes a single variable and preserves both CSV and integrity report. No ECU transmit/active queries or vehicle repair actions.
