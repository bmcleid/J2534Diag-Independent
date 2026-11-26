# R28 final disconnect reporting at application close

R27 saves before disposing/disconnecting its manager. That close-time snapshot therefore omits final disconnect events/outcome. The exact R27 binary is retained for an offline control reproduction.

R28 stops readers and checks retained receive ownership first, then offers Yes/No/Cancel. Cancel keeps the app/connection retained (readers have already been stopped by the existing close guard). Yes opens Save As first; cancelling the destination does not disconnect. After a path is chosen, disconnect completes before report snapshot/export. No disconnects and closes without saving. A disconnect refusal keeps the application/report open with retained resources. A failed save after successful disconnect keeps the app open and the report in memory, with the connection already disconnected. Retry saves those final events without another disconnect. Ordinary Export remains available after a failed close to preserve failure evidence.

No redundant manager Dispose/Disconnect occurs after the final snapshot. Untouched disconnected reports close without a prompt; a retained connection prompts even when its report is clean, because closing creates final evidence. The connection/selection predicate also includes retained native API, VIN monitor and cancellation source state. Existing CAN capture finalization and reader/writer guards remain first.

Focused tests use actual MainForm/FuelTrimForm/manager with a valid logical ownership lease and zero provider handles. They cover Cancel, save-path cancellation, original-target retention after failed write, retry, successful final-outcome/event CSV, no duplicate disconnect events, repeated clean close, No, injected disconnect refusal, manual export after failure and retained receive ownership. An exact-hash R27 control verifies its missing final Disconnect CSV and later dirty in-memory report. These are offline fixtures, not physical adapter results. Windows validation was pending in the source commit; the verified Part 4 results below supersede that pending status.

R28 remains bounded report evidence, not automatic recording, crash recovery or reconstruction of evicted detail. Closing can disconnect the device after a destination is selected even if the subsequent file write fails; keep the app open and retry/export. No vehicle test, provider registration, programming, DTC clear or branch merge is part of this task.

## Part 4 reconciliation — November 26, 2026 Eastern

**CONFIRMED, offline software gate PASS.** Tested source `03d8a2a611953ff912f79a2e0d3af056b3194a1f` (parent `331a02b6a3fc180ce03b6999e352085beaa7b62a`), owner `feature/native-gui-capture-preview`. [Windows run 45904637124](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45904637124), job `140198370679`, completed successfully November 26 at 5:27 PM Eastern. Actual job output, source call path and downloaded diagnostics were reconciled; this conclusion does not rely on the run summary alone. Superseded parent run `45904449678`, job `140197714852`, was cancelled during managed checks; it produced no final runtime package. No new CI, product build, R27 suite, historical control, dialog suite or vehicle test was run during Part 4 reconciliation.

### Focused evidence mapping

`RunAll` calls `AuditCloseOrdering` in `Tests/FuelTrimFormIntegrationTests.cs`. The successful output reports **107 actual-form assertions** (all form suites combined, not 107 R28 scenarios) and **6,805 total regression assertions**, including 92 scalar-report and 28 VIN assertions. Existing exact-R27 omission control passed six checks. The existing run also passed five historical R16 checks, 29 synthetic StdCall checks, managed legacy reproduction, 7 silent/7 partial/36 normal/19 broken-pipe checks. These results were reused, not repeated.

| R28 behavior | Verified evidence |
| --- | --- |
| Cancel retains connection; destination cancellation does not disconnect | Logical lease remains held, no save or disconnect outcome; injected choices/save cancellation in actual production forms |
| Disconnect precedes export; failed file write preserves target and report | Real locked-file write failure leaves ORIGINAL unchanged, dirty report retained, manager outcome Succeeded and lease released |
| Vehicle tab refresh after disconnect/save failure | Exact-head assertion checks Connect enabled, Disconnect disabled, capture-mode selection enabled after the failed write; production callback refreshes controls in finally |
| Retry preserves final disconnect without duplicate attempt | First retry CSV contains exactly one Pending/Succeeded pair; clean repeated close stays clean without prompt |
| No discards export and disconnects | No additional save; prior CSV unchanged; connection retired |
| Disconnect refusal retains evidence and app | Injected close refusal for No and Yes, dirty report/logical lease retained; manual export preserves failure event |
| Retained reader blocks close | Real receive ownership gate prevents disconnect/export; release permits final retry |
| Untouched disconnected close | Clean predicate returns before prompt; earlier actual-dialog acceptance remains applicable to unchanged prompt machinery |
| No redundant dispose after snapshot | Production OnFormClosing code inspection; repeated-close/no-duplicate assertions |

The R28-focused fixture bypasses provider discovery and uses a valid logical lease with null provider/API/device/channel handles. Its synthetic device-list entry exists only to enable control-state assertions. It neither opens nor registers a provider. The separate managed/native simulator steps in the existing run used disposable virtual drivers; do not describe those as zero virtual handles.

**Scope limit:** close decisions and save-path cancellation are injected delegates, not new R28 native-dialog automation. The file-lock failure and production form/control paths are real. No new R28 native-dialog/keyboard acceptance, owner-PC launch or physical adapter result is claimed. Existing R26/R27 dialog evidence is preserved and was not repeated. No missing focused policy/UI gate was found; another hosted run would duplicate passing coverage.

### Independent CSV and artifact checks

All CSV columns are: Utc, SessionId, Event, Build, ExecutableSHA256, ConnectionMode, VIN, ResponseSource, PID, Name, Unit, Value, Freshness, AgeMs, ReceivedHostTicks, Details.

- `r27-close-baseline.csv`: 14 data rows, 16 columns, six PID summaries, zero Observation or Disconnect rows; metadata DisconnectOutcome=Not attempted despite later successful in-memory disconnect. SHA-256 `898a9ee4238dcc95ae90a180353aa954bc8d531c91845b1c2e0924a7c5849286`; exact R27 EXE provenance `cf5d699d92e7f1d8969b6f1b873c9169b906765eb0aff85222fe218b541755e9`.
- `r28-final-close.csv`: 22 data rows, 16 columns, six State rows, six PID summaries, one SessionStart, one ExportMetadata, eight Disconnect rows, zero Observation rows. SHA-256 `eb528d94d0f066654c16535a34acd3646133b067e4c3297676ed9207fae2dbf1`. Disconnect details are Pending/Succeeded, Pending/Succeeded, two separately injected Failed: injected close events, Pending/Succeeded. This is the cumulative multi-scenario fixture after three distinct logical connections, **not eight events from one disconnect or proof of a duplicate retry**. The first-retry two-event assertion passed before later scenarios added events.
- Final R28 metadata has DisconnectOutcome=Succeeded; every row identifies exact R28 source and EXE. SessionState=Running is the synthetic report's Begin state with no actual reader session; it is not a claim that a provider remains connected.
- Existing managed-normal CSV: 110 rows, nine observations, two disconnect events; SHA-256 `5d129d81a964f2b43754a8430583164c5207bf1edd7193c77504eb162db5a16b`. Existing broken-pipe CSV: 41 rows, six observations, failed disconnect/cleanup evidence; SHA-256 `78305826a5d1355bb25ae2a881d39cd9e60b60a3572c7a137e0731b4aef20815`.

Diagnostics artifact `15157716732`: 403,493 bytes, SHA-256 `adcb8a3d6f510343b72cb0e5546185de0f9719dbf16adabd42a0e90b79d167bd`. Runtime artifact `15157422284`: 1,883,312 bytes, SHA-256 `8c59ceb5c14c7ceb2275c5abffb9b781fdfca9360a0a88d229de725870463b65`. Both downloaded; ZIP CRC passed. All seven declared runtime payload hashes passed, exact source/run manifest passed, and EXE SHA-256 is `632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160`.

### Continuation evidence and decision

All three Part 3 ending screenshots were inspected and retained unchanged:
- Screenshot 2026-11-26 173729.png — Library `libfile_af8fb4ec4c388191983b8174761ba03c`, SHA-256 `c3d8a7a34c48d7942680b9f286407ad368a151e7ae5d99c71514cd59ae66bc7e`.
- Screenshot 2026-11-26 173748.png — Library `libfile_ab6bc587ca3c8191a6e115c28e2288e4`, SHA-256 `d45a55f46c74797b3f941bf099c52191aa8bea77de4df45bdc940321cadec886`.
- Screenshot 2026-11-26 173758.png — Library `libfile_5d8bb69439d08191ad0fc28853760a45`, SHA-256 `ebccbb674161c9119e8ebe1eb1927f6474fcf870d01fa7a442f2495ab184de59`.

They establish completed R27 twenty-step review, separate authorization for the ten close-time correction steps, and the final UI-refresh correction. The earlier “no new defect / keep R27” statement belongs to the completed R27 review; the subsequently reproduced close-order omission is a separately authorized correction, not a reason to erase that prior acceptance.

**Decision:** an R28 engineering-preview package is warranted for the verified close-time reporting correction. Reuse the exact tested Windows artifact; no rebuild is required. R28 passes the scoped offline release gate. Preserve accepted R27 package `libfile_9c6d9759e9ac819199b187f83eac9a3e` unchanged as known fallback. New R28 owner-PC/physical acceptance remains UNVERIFIED; no owner action or vehicle test is requested. PR #29 was verified open, Draft and unmerged at the tested head; do not merge. Bounded-report/no autosave/crash recovery/evicted-detail reconstruction limits remain.


### Preserved delivery receipts

- [J2534Diag-R28-Windows.zip](https://chatgpt.com/library?file_id=libfile_20f867919ea48191b78304d0e3fc6288&account_id=personal): Library create succeeded, 1,883,312 bytes, version 0; backing file `file_00000000ffdc81f5bdbb59ac601fe7d0`. Exact bytes/hashes above.
- [J2534Diag-R28-Validation-Evidence.zip](https://chatgpt.com/library?file_id=libfile_74ba6aa216fc8191bfecf71af174d2e2&account_id=personal): Library create succeeded, 1,008,085 bytes, version 0; backing file `file_00000000ac2481f5bbe5ff0b77d9dd81`; SHA-256 `dc4743a619b3ead67a8cc777be8c254ef0326c8a429a2ba1823b168b6e4a5a54`. ZIP CRC passed and local identity metadata applied.
- Canonical registry and coordination updated with guarded current blob SHAs. Shared Index updated with expected version 240 to version 241. Readbacks are required before reporting synchronization.


## November 26, 2026 Eastern — R28 owner-PC launch visually confirmed

CONFIRMED visual evidence: owner screenshot `Screenshot 2026-11-26 175605.png`, Library `libfile_76a2af0b10408191b57a76523f89c6ff`, SHA-256 `3f5073a143d708d2f83de67cf993ca1ad37a48950e7fd406e3391b2d5cfe1d9b`, shows J2534Diag R28 / 03d8a2a61195 running on the owner PC. Fuel Trims tab displays six Unknown / stale gauges, empty bank histories, Session: Idle, Accepted: 0, New since export: 0 and Unsaved report: No. Status bar reads Disconnected; VIN is blank. This closes the pending **visual owner-PC launch** check. Screenshot alone does not verify process executable hash, physical adapter disconnection, native close dialogs, disconnect/save failure or retry. Those software behaviors retain the previously verified offline-test scope; no new physical or dialog acceptance is claimed. Original screenshot remains preserved at its Library ID. No retest, new build, connection or owner action requested. Keep R27 fallback and PR #29 Draft/unmerged.


## November 26, 2026 Eastern — J2534Diag R28 twenty-step native-dialog acceptance complete

**CONFIRMED offline native-dialog acceptance PASS**, superseding earlier R28 native-dialog pending status. First scoped [Windows run45908418443](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45908418443), job140210647329, SUCCESS; harness ca82eb1d011167f2fad40cce2e06608280ef7ffd on test/r28-close-dialog-ui-20261126. Exact R28 product source03d8a2a611953ff912f79a2e0d3af056b3194a1f/runtime45904637124 reused without rebuilding. 1,471 checkpoints include repeated module checks, not1,471 functional scenarios. Production OnLoad/native Yes-No-Cancel/SaveAs/overwrite/error dialogs exercised with valid synthetic logical connection ownership and zero registered providers/API-device-channel-native handles. Actual Cancel and destination cancellation retain connection; destination appears before disconnect; successful CSV includes final disconnect; locked-file error retains original/report/app and restores Vehicle controls; actual retry saves exactly one Pending/Succeeded pair; actual No closes/disconnects without replacing export. No tmp leftovers; unchanged EXE verified. No full/historical/R27 dialog reruns or physical actions.

Independent CSVs:16columns/16data rows each, six summaries, zero observations, exactlytwoDisconnect rows and finalSucceeded metadata; success SHA25668769382d75d41a13c9cf7bb73d14419670cbf92523e699d11badb1a638581e1;retry6e076a13e29e0d0f843b65121f09b1cd1ab05ee1ee761818baefe62d73e63d8c. Originalartifact15158901731,677594bytes,SHA60a634502d3d6c5c9b3797241350bb4f3762c481321971ace124f3e034320c51;CRC/hash/sizepassed. Screenshots/UI trees inspected. [Full twenty-step owner record](https://github.com/bmcleid/J2534Diag-Independent/blob/098e66311ab3034107fdf4ce8c0dca6bd03a13d2/docs/R28_NATIVE_DIALOG_COMPLETION_2026-11-26.md).

[Preserved native-dialog evidence](https://chatgpt.com/library?file_id=libfile_309fa55eca34819195439d7e6a4a28f3&account_id=personal),J2534Diag-R28-Native-Dialog-Evidence.zip,1385843bytes,SHA2568d04b7a014da53ea89c39c00069918ca9470e0973f9543488f15f599220130a4,includesoriginalartifact/log/harness/screens/trees/CSVs/independentchecks;save receipt and local identityconfirmed. Existing R28 runtime libfile_20f867919ea48191b78304d0e3fc6288 unchanged,EXE632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160. No new download/install needed. Keep acceptedR27fallbacklibfile_9c6d9759e9ac819199b187f83eac9a3e. Owner-PC visual launch alreadyconfirmed;physicaladapteracceptanceremainsUNVERIFIED. Boundedreport/noautosave/crashrecovery/evicteddetailrecoverylimitsremain. PR29Draft/unmerged;no providerregistration/connection/vehicletransmission/clear/programming/actuation/merge/siblingruntimechange. All20authorizedstepscomplete;needfromowner:none.
