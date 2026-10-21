# J2534Diag PR17 release-readiness verification — October 21, 2026

## Scope
Stacked draft PR #17 based on PR #16. SHA-256 of finalized source CSV is recorded with the pending in-memory integrity report and rechecked before sidecar retry, along with existing length and mtime checks. Recovery dialog literal newline formatting corrected.

## Verified
[Windows Release CI run 39694553848](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39694553848) at source commit `93c953c102891ae69fecddc0b2301680199da72a`: build succeeded with 0 errors and 5 warnings. **6,307 assertions across 64 offline tests passed**, including same-size source modification with restored file mtime.

## Boundaries
Original Oct 16 25,791-frame offline import remains confirmed; it was not rerecorded. Current CI result is not proof of Windows GUI position/usability, RLink driver/runtime compatibility or live car capture. In-memory recovery does not survive application restart. SHA-256 addresses accidental/observable file mismatch, not a full adversarial chain of custody or changes made between file rehash and publication. All 17 PRs remain draft; no branch merge or ECU command.

## Next release gate
Prepare a source-SHA-pinned engineering preview artifact manifest with dependencies and checksums; independently inspect GUI disconnected, then consider capture-only RLink test without ECU transmission. Do not label an Actions artifact as a production installer.