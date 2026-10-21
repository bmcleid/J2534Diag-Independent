# Atomic integrity sidecar finalization — October 21, 2026

## Evidence baseline
October 16 original Cadillac 25,791 native six-column frames imported successfully in the independent J2534Diag GUI. The separate native .NET logger remains the canonical vehicle source. PR #14 tab shutdown guard is the immediate parent branch.

## Source-inspection finding
Previous WriteIntegrityReport directly opened the final `.integrity.txt` with `FileMode.CreateNew` and serialized into it. A write failure could leave a truncated final filename. Subsequent retries could not replace that file, complicating traceability. This is a code-level fault scenario, not an observed failure on the physical RLink.

## Change
Write all report bytes to a uniquely named `.pending` file in the same directory, close the stream successfully, then `File.Move` to the requested final filename. Never overwrite existing final reports. Remove temp files on failed serialization/move where possible. Existing report fields and conservative `VehicleBusLossless=UNVERIFIED` remain unchanged. New tests cover successful publication, existing-file preservation, injected mid-write exception, temp cleanup and retry after failure.

## Limitations
Atomic rename does not guarantee persistence across power loss, storage crash, antivirus interference, or remote filesystem semantics; no disk flush-to-media durability claim. On failure, an OS-held or undeletable staging file may remain; no original evidence is overwritten. The receive gate and actual J2534 hardware shutdown timing remain independently unverified in this new release.

## Verification
Verify the Windows Actions run for the final source-bearing commit before reporting success. The current milestone is software-only; no new vehicle capture, ECU commands, or duplicate CAN analytics.

## Confirmed source-bearing CI acceptance (2026-10-21)
Windows [Actions run 39601317923](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39601317923) at commit `68427ddb0653d4a693ef1ff4effad79c2222b328`: Release build succeeded with 0 errors, 5 warnings; **6,307 assertions across 63 offline tests PASS**. Includes real CanCaptureIntegrityReport serialization through staging/publish code. This is software-only verification, NOT proof of Windows operator UI/hardware behavior.
The follow-on retry gap is separately recorded in `docs/NEXT_INTEGRITY_RECOVERY_GAP_2026-10-21.md`; no new live capture required.
