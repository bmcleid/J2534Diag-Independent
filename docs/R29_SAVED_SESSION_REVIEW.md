# R29 saved scalar-session review

## November 27, 2026 Eastern — R29 thirty-step readiness review closed

**Current decision:** no routine installed-RLink retest, rebuild or repeated owner launch/sample check is needed to close R29 saved-session review. [Thirty-step evidence reconciliation](R29_RLINK_READINESS_RECONCILIATION_2026-11-27.md) preserves original direct-native Nov2/Nov9 capture/rejection evidence, contextual R21 managed VIN/scalar and owner-confirmed Disconnect acceptance, and scoped R22–R28 virtual/software coverage. R29 connection/capture/native/shutdown files match R28 exactly; new Saved session navigation uses existing stop/guard behavior and starts no diagnostics.

R29 software/owner-PC saved-review acceptance is complete. Exact R29 physical adapter communication and current driver identity remain UNVERIFIED, as do manufacturer RxStatus0x00400000 meaning, bus completeness and scalar/TCM diagnostic accuracy. These are explicit limits, not requests to repeat completed baseline gates. Conditional minimum future capture procedure is recorded only for a material change/new symptom or separately requested exact-binary hardware qualification; no physical test is pending now. R28 working fallback/R27 older fallback retained; PR29 open/Draft/unmerged. No code, build, CI, provider/vehicle operation or merge in this review. This current entry supersedes historical pending recommendations only within its stated scope.


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

One scoped Windows build and parser/production-GUI acceptance gate for this new artifact. Reuse preserved R28 normal and refusal/recovery reports and a 20,007-record continuity export. Check exact source hashes, observations, error/disconnect events, multiline/quoted fields, zero/unknown values, malformed input, limits, filtering, picker/cancel, failed-load retention, restart and source immutability. Require zero provider registrations/handles. Do not repeat R28 close/dialog/history/vehicle suites. The source-commit results were pending; verified outcomes below supersede that pending status.

A local executor was unavailable at implementation start; code was prepared through repository APIs and will be compiled/tested by the existing hosted Windows route. No local compiler pass is claimed. Product acceptance and package delivery must use the successful exact-source run, not this plan. PR #29 stays Draft/unmerged.

## Verified completion — November 27, 2026 Eastern

**CONFIRMED: R29 saved-session review is implemented, compiled, and accepted offline.** Product source `2aa3d43a0c5e3fe2c913e2a8b96d90f91157c249`; actual parser/production-GUI acceptance ran once in [Windows 46086955097](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46086955097), job `140798643825`. The acceptance step passed **581 checkpoints** including repeated module-name checks, not 581 functional scenarios. App compiled with four existing unused-variable/field warnings; focused harness compiled without new errors. Actual screenshots were inspected.

### Completed scope

1. Refreshed Shared Index v251, owner/registry/coordination/CI policy and PR29.
2. Corrected the old handoff with a current continuation checkpoint; preserved historical notes and R28 fallback.
3. Confirmed the scalar-review gap against the application project, source tree, CAN importer and exporter.
4. Added a separate read-only Saved session tab and complete Schema=2 reader.
5. Added exact event filtering, case-insensitive text search, original source-record numbers and complete field details.
6. Preserved raw cell text, order, missing values, measured zero, error/disconnect evidence, source path and one-snapshot SHA-256. Exporter identity remains a recorded claim.
7. Added explicit malformed/schema/incomplete/size/row-limit rejection and prior-report retention.
8. Verified parser and real production GUI with preserved R28 normal and failure/recovery reports, native file picker/cancel, filter/clear, failed load, clean live report, restart/reopen and unchanged source files.
9. Recovered and verified a distributable ZIP with the exact accepted executable and all nine original payload hashes.
10. Preserved the package as a durable draft release and updated canonical GitHub owner/shared records with readback. Library index writeback is explicitly pending because the local executor and file-write tools are unavailable; no successful Library save or direct chat attachment is claimed.

The 20,007-record continuity report opened completely. Normal report reopened with 110 records and nine Observation rows. Actual GUI loaded all 18 refusal-recovery records; searching R28_REFUSAL_FIXTURE yielded exactly original source record 10 and its full failure detail. Exact event selection yielded nine observations, excluding metadata text. Native picker cancellation and invalid-file parse retained the previous report. Restart/reopen worked; original hashes remained unchanged. Zero registered providers and null API/device/channel/native-session handles were checked. No adapter/vehicle communication or programming, DTC clear, actuation or merge occurred.

### Validation and delivery accounting

The first hosted run's overall conclusion is **FAILURE**, despite compile/parser/GUI/package verification passing: its draft-release upload failed because the workflow accidentally renamed the GH_TOKEN environment key. No product defect was established. The successfully created ZIP was not uploaded by the later success-only artifact step; the transient runner's package could not be recovered directly. This orchestration error is corrected in workflow-only commit `dc29d23bd7f2caff62263b5e7c4146fc5c08d0bc`: correct token key and upload package before external release action.

[Package-only recovery 46087182555](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46087182555), job `140799383256`, **SUCCESS**, harness/workflow commit `185b6c702ef67437162aba83ffa22da39aa9a67d` on `test/r29-package-recovery-20261127`. It rebuilt only the pinned original source, stamping the original product commit, and required the exact previously accepted EXE and **all nine original payload hashes**. Both comparisons passed. No acceptance tests, old suites or vehicle checks were rerun. ZIP entries were reopened and independently compared to payload SHA-256 values within the recovery gate. There were two product compiles total (initial validation plus package recovery), one parser/GUI acceptance run, and zero full/historical/R28-dialog suites. Never describe the first hosted job as entirely successful.

ZIP metadata changed during repackaging: original transient ZIP SHA-256 D85095ADE7C3AB242FB16A86F7D8C78484AA20356307D9B8972FEFECB1075037; delivered ZIP SHA-256 below differs, while all nine payloads and tested EXE are identical.

### Delivered package

- [J2534Diag-R29-Windows.zip](https://github.com/bmcleid/J2534Diag-Independent/releases/download/untagged-993a71743095eeb69d7e/J2534Diag-R29-Windows.zip), **1,897,018 bytes**, SHA-256 `0fdad6d5e480bbe885b21ffc15bf7680fdf78791b29f7fcd6392bde470b644f1`.
- EXE SHA-256 `c4a9b9cf8602d9d3dfcda0cadb29792302e21ea4b62c9f2a7362fff3f126ad0f`.
- [Durable draft release](https://github.com/bmcleid/J2534Diag-Independent/releases/tag/untagged-993a71743095eeb69d7e), release ID `461179973`, tag name `j2534diag-r29-2aa3d43a0c5e`, target original product source. Asset `802563706` independently read back as uploaded, exact byte count and SHA-256 digest match. Draft retained, not publicly published.
- [Windows artifact 15240080062](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46087182555/artifacts/15240080062) wraps that ZIP; wrapper 1,896,130 bytes, SHA-256 `5dd6ebcb76725228b1d22026729910e4cb469f20aafd85a4e72c2e63a3477d94`, expires February 26, 2027. Do not confuse wrapper and inner package hashes.
- First acceptance evidence artifact `15239267830`: 181,270 bytes, SHA-256 `a479d322b2f26bb498c601d1e93f8c8d55951045d905c1ab5e0241c8dade9920`.
- Recovery evidence artifact `15239258129`: 1,354 bytes, SHA-256 `2bdc6c12c20613124d80ff792b31b157e051ebe8ec3a3240e595315a33e736ae`.
- Durable text logs and screenshots are preserved in this branch under docs/evidence/R29-*.

Extract R29 separately; keep R28 unchanged. Choose Saved session, then Open saved session. Example-Synthetic-Session.csv is a labelled demonstration, not vehicle evidence. No owner retest or vehicle scan is requested. Owner-PC R29 launch/physical acceptance remain unverified; R28's owner launch and completed close/recovery evidence remain valid. PR #29 stays Draft/unmerged. The saved-review development scope is closed; the sole administrative follow-up is Library index/direct-file delivery when an executor is available. This is not a scheduled automation.


## November 27, 2026 Eastern — J2534Diag R29 saved-session review and owner-PC acceptance

**CONFIRMED offline software:** R29 at product source `2aa3d43a0c5e3fe2c913e2a8b96d90f91157c249` adds a read-only Saved session tab for this application's 16-column Schema=2 scalar reports, exact event/text filters, original source-record numbers and complete details. Preserves values/missing states/errors/disconnect/counters and one-snapshot file SHA-256; recorded exporter identities are not authenticated. Remains separate from OneApp saved PID, GDT ELM review, Vgate and simulator.

Acceptance run46086955097/job140798643825 passed product compile and 581 parser/production-GUI checkpoints, including repeated module-name checks. Its overall job failed later due a delivery-token configuration error. Package-only recovery46087182555/job140799383256 succeeded, matching the exact tested EXE and all nine payload hashes; acceptance was not rerun. [Owner record](https://github.com/bmcleid/J2534Diag-Independent/blob/feature/native-gui-capture-preview/docs/R29_SAVED_SESSION_REVIEW.md) preserves both outcomes, logs, screenshots and durable draft-release package. ZIP 1,897,018 bytes, SHA-256 `0fdad6d5e480bbe885b21ffc15bf7680fdf78791b29f7fcd6392bde470b644f1`; EXE `c4a9b9cf8602d9d3dfcda0cadb29792302e21ea4b62c9f2a7362fff3f126ad0f`. Preserved R28 remains fallback.

**CONFIRMED owner-PC visual acceptance:** Screenshot `Screenshot 2026-11-27 193051.png`, Library `libfile_2d780cc31644819199a004f75dae9a8b`, SHA-256 `a5931a5de51179557604a16d4350f268a4427eb032b0f1caacf9b72a478461a3`, shows R29 / 2aa3d43a0c5e with Saved session open, `Example-Synthetic-Session.csv` loaded, matching source SHA-256 `95a78c99e4de52843ee067995fa0b5a31ebe98f3db53e2514736f494459662fd`, 23 of 23 records, one synthetic observation and one synthetic disconnect event, error and six PID summary records, and No ECU requests. Displayed exporter EXE hash of repeated a characters is the sample's test claim, not the running executable hash. The image confirms launch and sample opening; it does not independently verify process hash, all filters/restart paths or physical adapter behavior. Prior hosted tests cover those offline paths. Sample delivered unchanged as Library `libfile_feb8d947e4888191925d8ad6b270ca59`.

This entry closes the previously pending Shared Index writeback. R29 package preservation receipt is recorded in the current owner record. No new build/test or owner/vehicle action requested. PR #29 remains Draft/unmerged; no provider/vehicle connection, transmission, clear, programming, actuation or merge. Saved-review scope is complete; no repeat sample check required.


Preservation receipt: [J2534Diag-R29-Windows.zip](https://chatgpt.com/library?file_id=libfile_f65b05c6c35081918a33db0b8e058fe5&account_id=personal), Library version0, backing file `file_000000000e2081f5ba02024ebcb5dd53`, exact 1,897,018-byte ZIP/hash above; saved successfully and local identity applied. Shared Index guarded write v258→v259 succeeded. This supersedes the earlier unavailable-workspace administrative block; no replacement app build was needed.
