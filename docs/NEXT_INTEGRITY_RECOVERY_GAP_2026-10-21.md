# J2534Diag Part 2 — integrity report retry gap (source inspection)

## Baseline
- Original 25,791 Cadillac native six-column frames loaded offline on October 16; no repeat.
- PR #15 stages integrity-sidecar publication. CI acceptance remains separate.

## Newly identified source-level issue
StopListening() waits for receive worker, disk writer and filter cleanup and then attempts WriteIntegrityReport(). If report serialization or publication fails, the method records the exception locally but clears _diskLogger, disposes _cts and leaves the capture state. On later calls StopListening() immediately returns because _isListening is false. The GUI has no retry operation with preserved finalized metadata for that session. CSV contents are not automatically lost, but a missing sidecar cannot be rebuilt from the cleared status in this workflow.

Status: LIKELY functional recoverability defect based on current source inspection, not a demonstrated TOPDON hardware failure.

## Next development gate (after PR #15 CI)
1. Preserve a finalized immutable integrity-report snapshot if sidecar publication fails, without holding or reopening the J2534 channel.
2. Provide an explicit Retry integrity report action only after CSV writer closure. Never replace an existing sidecar or overwrite CAN raw capture.
3. Test injected serialization failure -> metadata retained -> retry succeeds exactly once; existing sidecar collision -> no overwrite. Verify missing/changed source CSV handling.
4. Keep vehicle-level completeness UNVERIFIED. Do not repeat raw capture, create a new CAN analyzer, decode guessed GM DIDs or issue any module commands.
5. Keep changes on a separate stacked draft branch; Windows CI and later operator GUI verification are distinct.

Native .NET CAN, OneApp, Vgate BLE and T43 ownership remain unchanged.