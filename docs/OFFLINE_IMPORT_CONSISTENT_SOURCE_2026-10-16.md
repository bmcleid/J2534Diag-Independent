# Part 2 J2534Diag — consistent-source offline import (October 16, 2026)

## Purpose and existing evidence
The original Cadillac 25,791-frame native six-column offline GUI import was confirmed by the operator before this branch. Its canonical source SHA-256 remains `0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B`. No new hardware or original-file re-import is needed for this software change.

## Distinct reliability gap
Prior provenance PR #11 parsed the selected file using one handle, then reopened it for SHA-256. The digest and displayed rows might refer to different revisions if another program changed the file in between.

## New implementation
- `OfflineCaptureProvenance.ReadConsistentFile` opens the original CSV once with `FileAccess.Read` and `FileShare.Read`. On Windows ordinary conflicting write opens are denied while this handle is held.
- The same seekable stream is hashed byte-for-byte, rewound, and parsed; no source rewrite, conversion, repair, sidecar output or diagnostic transmission.
- Both header recognition and row import run on the same stream via non-owning `StreamReader` instances. The handle closes on successful completion or exceptions.
- Preserves all original native/legacy timestamp handling, row representations, previous UI labels, and import failure behavior.
- Does not prevent external disk tampering outside Windows file-share semantics or establish forensic authenticity; original file's provenance still depends on the external Oct 14 audit.

## Verification
- Added deterministic stream tests for hash/parsed-byte agreement, rewind, non-disposal of caller stream and parser exception propagation.
- Windows Release compilation and full regression suite pending GitHub Actions run 38939461878 as of initial document creation.
- GUI confirmation against operator's original file remains unverified, and previously confirmed 25,791-row offline import is not retested.
- Draft stacked branch; no automatic merge into master or change to OneApp/native audit/Android Vgate/T43 ownership.
