# Offline source mutation guard — J2534Diag Part 2 (2026-10-19)

## Source and scope
This branch builds atop draft PR #12's single-handle, read-only offline CSV importer. The October 16 original SRX 25,791-frame six-column UI import is **CONFIRMED** and not repeated here. Canonical October 12 source SHA-256 `0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B` is unchanged.

## Narrow improvement
Even if the importer opens a file with `FileShare.Read`, an already-open writer may change the file during parsing. The importer now:
1. Hashes all original source bytes before parsing.
2. Rewinds and parses the same source stream, holding the handle throughout.
3. Rewinds and rehashes the source before publishing parsed rows to the UI.
4. Throws an explicit `IOException` if SHA-256 differs, leaving the previous UI results unchanged rather than presenting a misleading source association.

Test fixtures mutate a seekable stream during import and verify failure, while unchanged streams pass and produce the expected digest. This is an offline evidence-consistency measure only, not an original-file audit or live CAN diagnostic feature.

## Limitations
Two equal hashes do not exclude a reversible alteration between the reads, malicious tampering, device-level changes, or an unexpected file replacement. The comparison establishes consistent visible file bytes at the two hash points, not a forensic chain of custody. Operator testing of this new guard against the actual Windows GUI remains **UNVERIFIED**; no additional physical vehicle capture is needed.

## Verification
**CONFIRMED software CI:** [Windows Actions run 39343193004](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39343193004) at commit `c9b11c44` passed: Windows Release build 0 errors, 5 warnings; **6,296 assertions across 62 offline tests passed**. Final documentation-only commit requires independent status verification if needed.
No DIDs, transmit routines, module programming, GM signal decoding, CAN source collection, or other project responsibilities are modified.
