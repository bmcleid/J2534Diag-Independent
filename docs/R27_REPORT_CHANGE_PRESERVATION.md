# R27 — protect unsaved report events and counters

Problem confirmed in R26 source: ConfirmReportBeforeClose tested only ObservationsSinceExport. After a successful export, a Disconnect event, no-response/error status or counter-only update could leave new report evidence without a close prompt.

R27 adds a report revision captured atomically with each export snapshot. Rows and nonzero counter updates mark the report dirty. Only a successful atomic save acknowledges that snapshot; changes arriving afterward stay dirty. The existing new-observation count remains a count of observations. The health label adds Unsaved report: Yes/No; close protection uses all report changes. Cancel and failed/cancelled save keep the app open, No explicitly discards, Yes saves. An empty untouched report and zero-count updates stay clean.

Focused model/form tests cover event-only/no-response sessions, Disconnect-only changes, counter-only changes, locked target failure and retry, zero updates, unchanged status, and Cancel/No/Yes with zero new observations. Existing concurrent-observation export verifies new revision remains dirty after its snapshot. Validation pending at source commit; record exact Windows run and artifact hashes after success.

Review of independent CAN capture/recovery boundaries found existing worker/writer join guards, retained ownership on pending cleanup, atomic integrity sidecar recovery and original-source verification already covered by earlier tests. No new capture transport defect was established in this review. Report event loss was the selected actionable gap.

R26 runtime and actual-dialog evidence remain intact. New R27 gets one first Windows build/regression/native/managed validation; no repeated R26 dialog workflow or physical vehicle test. Draft PR29 remains unmerged. This is independent general J2534Diag, not OneApp, Android Vgate, simulator development or a Cadillac TCM decoder.

Remaining limits: bounded accepted-observation report, no autosave/crash recovery or reconstruction of evicted details. App-close export still precedes final manager disposal, so later disposal events cannot be included in that already-saved snapshot. Export explicitly after disconnect to retain that outcome. R27 physical behavior remains UNVERIFIED.
