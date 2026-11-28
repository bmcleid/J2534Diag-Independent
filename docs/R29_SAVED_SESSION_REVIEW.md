# R29 saved scalar-session review

Owner: Part 4 - Analyze J2534Diag, bmcleid/J2534Diag-Independent, feature/native-gui-capture-preview. R28 remains the accepted fallback; its close/recovery work is complete.

## Scope

R28 can export bounded 16-column scalar session CSVs but cannot reopen them: the existing CAN importer accepts raw CAN formats. R29 adds a separate Saved session tab. It opens this application's Schema=2 scalar reports, keeps all original field text and record order, and shows the source path and SHA-256 of the exact parsed bytes. Recorded exporter build/EXE identities are displayed as unauthenticated claims, not verified installed software.

The grid is read-only. Choose an exact event type or find text across the original fields. Source record identifies the original data-record position, including when a quoted field spans physical lines. Selecting a record shows every field in full. Metadata/counters, unknown/missing values, measured zero, errors and disconnect outcomes are preserved. Opening a file does not restore a live acquisition, import into its report, connect to a provider or issue ECU requests.

## Boundaries

Only exact 16-column Schema=2 reports are accepted; raw CAN, external PID/ELM logs, unknown schemas, malformed/incomplete exports, invalid UTF-8, inconsistent retained-row counts or missing six summaries are rejected. Input limit: 32 MiB and 25,000 data records; rejection is explicit, never silent truncation. Source is opened read-only with write/delete sharing denied on Windows, then hashed and parsed from one byte snapshot. Filtering does not rewrite or sort source records. A failed import or cancelled picker retains the prior displayed report.

R29 does not authenticate exporter claims, reconstruct evicted detail, add autosave/crash recovery, decode proprietary TCM fields or infer current vehicle state. Keep separate from OneApp saved PID and GDT saved ELM review.

## Usage

Extract R29 in a separate folder, preserving R28. Run J2534Diag.exe, choose Saved session, then Open saved session. Choose a J2534Diag scalar CSV; a clearly labelled synthetic example is included for demonstration. Do not connect an adapter for offline review.

## Validation plan

One scoped Windows build and parser/production-GUI acceptance gate for this new artifact. Reuse preserved R28 normal and refusal/recovery reports and a 20,007-record continuity export. Check exact source hashes, observations, error/disconnect events, multiline/quoted fields, zero/unknown values, malformed input, limits, filtering, picker/cancel, failed-load retention, restart and source immutability. Require zero provider registrations/handles. Do not repeat R28 close/dialog/history/vehicle suites. Results are pending until actual Windows output is inspected.

A local executor was unavailable at implementation start; code was prepared through repository APIs and will be compiled/tested by the existing hosted Windows route. No local compiler pass is claimed. Product acceptance and package delivery must use the successful exact-source run, not this plan. PR #29 stays Draft/unmerged.
