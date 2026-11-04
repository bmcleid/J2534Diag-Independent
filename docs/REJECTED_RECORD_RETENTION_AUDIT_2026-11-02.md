# J2534Diag rejected-record retention audit — 2026-11-02 / JDIAG-P3-R4

Owner: independent J2534Diag, feature/native-gui-capture-preview, PR29 head at review 1f1852f3ca2b5e5fe7c77a45ce6f627f8b4c3e98. Review only; production source unchanged.

## Established original evidence
Nov2 CSV 55,192 accepted rows; matching original integrity sidecar DirectNativeJ2534, 552 successful reads, no reported overflow/driver errors; eight InvalidShortOrNullCanRecords, zero recognized indication records; rejected contents not retained. Counts reconcile accepted disk writes but ApplicationWriteComplete=False. Original CSV SHA256 8954903fc768300715f9c326200d818ef0e7fd80d49c0b80a27021d72c83ce76; sidecar SHA256 737fa96cda002841b749f6734cfe4c2368e79bff5a5ee3c3e3297fa9d85ea59b. No repeat vehicle trial.

## CONFIRMED source findings
1. Services/NativeJ2534StdCallApi.cs ReadCan creates a CanReceivedFrame with Data/TimestampRaw/RxStatusRaw only. Native ProtocolID, TxFlags, original DataSize and ExtraDataIndex do not survive this conversion.
2. Its expression item.DataSize <= 4128U ? (int)item.DataSize : 0 converts an oversized declared native record to a zero-length managed Data array. Subsequent classifier can categorize it MissingOrShortHeader. Thus eight short/null counts do NOT uniquely prove eight genuinely short native DataSize values. This is a possible mechanism, NOT a measured explanation of this capture.
3. NativeCanReadBoundary clones only Data/timestamp/status/read-result; new native metadata must propagate through this copy too.
4. CanFrameClassifier excludes low-nibble indication bits before length checks. RxStatus 0x00400000 alone is NOT covered by that mask. OneApp historical zero-length records with that status are analogous only; this capture did not retain rejected status, so cannot match them or call harmless.
5. CanCapturePump increments rejection-category counters then continues, with no rejection sink. Integrity report explicitly stamps RejectedRecordContentsRetained=NO.
6. CanCaptureIntegrityReport applicationComplete requires invalidFrames==0 as well as clean disk accounting. False therefore does not uniquely signify a failed CSV write.
7. Existing classifier/boundary tests already cover null/short/oversize/ID rejection, indications, partial timeout, native return counts and partial overflow. Reuse these; do not build a competing decoder or replace evidence limits.
8. Separate incidental source issue: CanCapturePump overflow catch calls AccumulateReadElapsed, then normal continuation calls it again before processing partial frames. Overflow read duration can be double-counted. This does not affect Nov2 session (zero reported overflows); keep distinct from rejection evidence.

## Required narrow implementation
- Preserve native metadata before classification: reported DataSize, ProtocolID, TxFlags, ExtraDataIndex, timestamp, RxStatus, read status, batch ordinal and slot index. Keep actual copied length distinct from reported size; never dereference beyond 4128 or returned/allocated count.
- Add a rejection evidence sink to the EXISTING pump. Preserve exact raw available bytes and rejection reason for both malformed and indication records; no acceptance-policy relaxation or speculative manufacturer status mapping.
- Disk sidecar must be CreateNew, bounded, linked to source CSV and driver identity, closed/accounted on Stop. Explicitly record caps, truncation, queue drops and evidence-writer failure; never claim full rejection retention from a limited sample. Avoid synchronous per-record disk I/O on receive thread.
- Maintain eight-column accepted CSV unchanged and keep original capture bytes unchanged.
- Show separate accepted-record disk accounting and record-validation status, while retaining conservative incomplete verdict. Never change historical sidecar values.
- Treat duration double count as a separate focused fix/regression, not reason for a vehicle retest.

## Validation gates before distribution
Extend current C# tests with original metadata surviving native conversion and boundary cloning; genuinely short vs impossible DataSize; unknown 0x00400000 with zero data remains unclassified/invalid; partial-overflow rejection retention without accepting stale slots; capped/dropped evidence explicitly incomplete; writer/flush/create-existing failure; original accepted order/count/CSV format unchanged. Extend existing Windows native mock to emit known rejected records and verify exact persisted metadata without TOPDON access. Do not fabricate the eight historical records as fixtures.

No C# compiler (dotnet/mcs/csc) present in current execution environment. No new C# runtime tests, build or hosted Actions run claimed. This audit is source inspection, not an implemented fix. A coherent implementation and actual software validation are still required; no user vehicle work is required for them.

## Continuity
Current Index, registry, cross-track coordination, branch AGENTS and all open stacked PR heads reviewed. No competing rejection-retention PR found among PR1–29. OneApp rejection evidence exists separately and should inform schema, not be copied into this app's receiver. No source changes, ECU requests, merges, or build minutes used.
