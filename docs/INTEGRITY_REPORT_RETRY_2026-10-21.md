# J2534Diag Part 2 — finalized integrity sidecar retry (October 21, 2026)

## Baseline and scope
Original 25,791-frame SRX native offline UI import confirmed Oct 16. PR #15 atomic sidecar staging confirmed by Windows CI 39601317923: 6,307 assertions / 63 tests. No new CAN capture.

## Implementation
- Retain immutable serialized report text, source CSV pathname and its length/LastWriteTimeUtc when a finalized sidecar cannot be published.
- Retry integrity UI control becomes visible only on publication failure. New capture is refused while pending report must be resolved.
- Retry calls atomic publication without reconnecting J2534, reading the vehicle, rebuilding CAN metadata or overwriting an existing .integrity.txt.
- Reject retry when original CSV disappears, changes length, or changes LastWriteTimeUtc; source metadata is a conservative accidental-change guard, NOT a cryptographic guarantee.
- On success, clear pending state and disclose that vehicle-bus completeness remains UNVERIFIED.

## Verified software test
[Windows CI 39601802086](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39601802086), commit 64825b402b4d59d3f739ab737f1733a4af689552: Release build succeeded, 5 warnings, 0 errors, 6,313 assertions across 64 offline regression tests PASSED. Covers recovery without overwrite, changed/missing source denial, existing sidecar collision, retry success and earlier atomic stages.

## Limitations / new checks
- The retry snapshot is IN MEMORY. Closing/restarting the application after a report publication failure loses retry state; capture CSV remains on disk. A persistent recovery manifest needs separate design and evidence before implementation.
- File length and modification time can be manipulated or restored, so they do not attest byte equality. No source changes were made and no calibration/ECU identity is inferred.
- Physical Windows GUI button positioning and behavior, driver/filter closure, report-retry user workflow remain UNVERIFIED on operator PC. CI uses software-only fake/error paths.
- All stacked PRs remain draft. Do not merge master, re-capture SRX data, or duplicate OneApp/native audit/T43/Android Vgate work.