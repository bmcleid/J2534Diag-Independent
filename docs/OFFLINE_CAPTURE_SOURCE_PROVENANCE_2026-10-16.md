# Independent J2534Diag offline source provenance — 2026-10-16

## Scope
Adds an independent, read-only byte-level SHA-256 comparison on the **already working** offline CSV import path. Does not rerun CAN audit, decode GM signals, open an adapter, transmit data, or replace SRX-PC-Scanner/OneApp/T43 functions.

## Established baseline
- October 16 operator screenshot CONFIRMED 25,791 original SRX six-column CAN rows loaded offline using PR #10. This feature does not repeat the import test.
- Canonical original six-column SRX source SHA-256 from independent Oct 14 original-file audit: `0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B`.
- Original source bytes are **not** included in the recovery ZIP; the new comparison against them is unverified until run on the operator's local file.
- `ReceivedAtUtc` remains blank for native imports, raw timestamp microseconds remain preserved numerically, no invented ECU identity or bus completeness.
- Current branch is stacked atop PR #10 and must stay draft/unmerged.

## Behavior
- After successfully parsing any offline CSV, calculate SHA-256 from the selected original on-disk file, without rewriting it.
- For native six-column imports, report either **CONFIRMED byte match** to the canonical digest or **NOT A BYTE MATCH**, with an explicit caveat that a mismatch does not invalidate other recordings.
- Legacy/J2534Diag eight-column imports show SHA-256 but do not falsely claim equivalence to original six-column data.
- Hash identifies identical file bytes against a preexisting recorded digest; does **not** attest the underlying vehicle origin, timestamps' hardware units, completeness, 97-ID CAN identity, or TFP/ECU mapping.
- The file is parsed and reopened for hashing; concurrent modification between these two reads is an unresolved time-of-check/time-of-use limit, not a forensic secure chain of custody.

## Verification gates
- Regression source test: SHA-256 known `abc` vector, single-byte/line-ending changes, case-insensitive digest comparison, correct native and legacy match/mismatch disclosure.
- GitHub Windows Release build and offline test results must be checked at this branch head. Until completed, this is **implemented, NOT validated**.
- No additional vehicle capture or operator action required to establish software-only build success. Exact source-byte comparison needs operator's original file only once when convenient; do not request another capture.

## Relationship to other lanes
- Native `SRX-PC-Scanner` audit remains canonical for timestamps, frame counts, rejected frame/interval findings.
- OneApp owns native reports/file browsing; T43 owns OEM-defined switch-state/ISO-TP work; Android Vgate owns ELM/BLE.
- This independent utility exists only to make the J2534Diag imported evidence's source identity explicit.
