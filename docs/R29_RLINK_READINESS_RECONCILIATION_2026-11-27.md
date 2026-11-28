# R29 installed-RLink readiness reconciliation — November 27, 2026 Eastern

Owner: Part 4 - Analyze J2534Diag, 2012 Cadillac SRX project. Repository bmcleid/J2534Diag-Independent, branch feature/native-gui-capture-preview, PR #29 open, Draft and unmerged. Reviewed head 17f45bc65ccb9f3f1d44e951679355a89cfe59ee; product source 2aa3d43a0c5e3fe2c913e2a8b96d90f91157c249. This is a documentation-only audit, not a new physical or automated acceptance run.

## Decision

**CONFIRMED:** the authorized thirty-step readiness review is complete. R29 saved-session review is accepted offline and visually accepted on the owner PC. **No additional build, full regression, owner launch/sample check or routine RLink/vehicle retest is warranted to close that feature.** Preserve R28 as the working fallback and R27 as the older accepted fallback. Do not merge PR #29.

**CONFIRMED source finding:** R28-to-R29 production changes add the saved-report parser/form and its tab, Microsoft.VisualBasic reference and release identity. MainForm changes only add the saved form field and constructor controls. OnLoad, OnFormClosing, navigation guard and selected-tab handler are byte-for-byte unchanged in that comparison. Selecting Saved session follows the existing stop-listener path and starts no diagnostic listener. The new form/parser have no J2534Manager or PassThru dependency. This supports reuse of R28 evidence; it does not turn source inspection into an R29 physical test.

**UNVERIFIED:** exact R29 physical adapter communication, present installed driver/firmware identity, manufacturer meaning of RxStatus 0x00400000, all VIN fallback modes on this car, dynamic scalar accuracy/cadence, complete vehicle-bus capture and proprietary TCM/TFP interpretations. These are limits, not failed R29 saved-review requirements. No new vehicle diagnostic or repair conclusion follows.

## Requirement and evidence reconciliation

| Requirement | Evidence | Status and scope |
| --- | --- | --- |
| Independent ownership/current baseline | Current owner handoff, PR29 and canonical registry/coordination; Shared Index v261 read during review | CONFIRMED; separate from OneApp, Vgate, GDT and simulator |
| Original USB lifecycle | OPERATOR_USB_LIFECYCLE_CONFIRMED_2026-10-22.md records owner connection/disconnect/reconnect/second disconnect with OBD disconnected | CONFIRMED owner report; USB-only channel lifecycle, not live ECU reception |
| Installed native preflight | Oct29 screenshot record: PassThru464.dll, TOPDON metadata, V1.17, 13,917,032 bytes, x64 PE, prepared/cancelled state | CONFIRMED historical UI evidence; not current publisher/firmware certification |
| Original managed overflow | Handoff preserves repeated original managed disk first-read 0x12 versus successful 10k in-memory capture | CONFIRMED historical behavior; cause remains unverified; do not repeat |
| Direct-native physical reception | Nov2 original CSV libfile_64b966d4cecc8191b5db0f3ec4528306 plus matching sidecar libfile_9fa6d49eb5c88191be1290f84755a1b3: 55,192 rows, DirectNativeJ2534, 100/250ms, zero reported overflow | CONFIRMED application-reported native session supported by original CSV; exact EXE linkage unverified |
| Rejected-record retention | Nov9 CSV libfile_5908e3de28708191abdcf02061bfa262 and sidecar libfile_cbb493c74834819182e3486ccdd1c03a: 57,467 accepted rows plus all 33 rejected records retained | CONFIRMED physical R5-format behavior; ApplicationWriteComplete=False preserved; flag meaning unknown |
| Managed physical connection/display | Nov24 R21-context Vehicle/FuelTrims screenshots show Original J2534-Sharp, active diagnostics, correct VIN, 0x7E8/Mode01 and six values | CONFIRMED observed UI; contextual R21 identity, not independently hashed running process |
| Managed physical disconnect | Owner explicitly reported “Yes. No error on disconnect.” after that session | CONFIRMED owner-report result; no repeat required |
| Managed normal/fault lifecycle | R22 release validation45532404468 and retained later R28 managed artifacts | CONFIRMED virtual-driver/software evidence; not a physical failure/recovery experiment |
| R28 close ordering/report CSV/UI refresh |45904637124 baseline;45908418443 actual native dialogs;46040117261 refusal/reader recovery | CONFIRMED scoped software acceptance; no new run needed |
| R29 parser/production GUI |46086955097/job140798643825 compile and581 checkpoints passed; later delivery step failed | CONFIRMED acceptance, while overall first job remains FAILURE |
| R29 package recovery |46087182555/job140799383256 SUCCESS, exact EXE and all nine original payload hashes matched | CONFIRMED recovery; acceptance not rerun |
| R29 owner-PC sample opening | Screenshot2026-11-27 193051.png, libfile_2d780cc31644819199a004f75dae9a8b; R29/2aa3d43a0c5e,23 of23 records, matching sample hash | CONFIRMED visual acceptance, not process hashing or physical communication |
| R29 transport continuity | Git comparison against R28 source03d8a2a; exact file comparisons below | CONFIRMED source continuity; inherited evidence retains original scope |
| Exact R29 physical acceptance | No new physical session supplied | UNVERIFIED; not required for the completed additive offline feature |

R21 screenshots: libfile_8f5d8c64126c819198c26605b9872876 (Fuel Trims), SHA256 6a1471bef1837aec7296971a6230e981455a693c266289d0de8843894e37f266; libfile_46b5320bc0d08191b5e24301fc5f69c0 (Vehicle), SHA256 03c55c617bfd7a62846731c3d28966baafff814bcff265a8990618f2154b72c1. Displayed VIN 3GYFNEE31CS519173. RPM559.5; STFT1 3.125%, LTFT1 7.031%, STFT2 -1.563%, LTFT2 13.281%, throttle16.863%. These displayed observations do not establish fuel-system condition or accuracy.

The historical native driver SHA256 was 0f72cc582305d358aa935ff5fca7264bfebbae7636891f24755bc0fcbdf12b95. Nov2 CSV SHA2568954903fc768300715f9c326200d818ef0e7fd80d49c0b80a27021d72c83ce76. Nov9 CSV SHA2566163437138d095cd5e1827757a74b033f0b47c03ba9be61761875bc9af5d3c65; matching sidecar abef3545bf843022ee0cac823de2c09cbd37b1cd7c177072d4cf20d261b6dcde. The earlier October pending-native notes are superseded for those later observed sessions; they must not reappear as demands to repeat first native reception. Exact binary identity remains limited.

## Source continuity and evidence identity

R28-to-R29 identical files independently compared from Git blobs:

| File | SHA256 at both product sources |
| --- | --- |
| Forms/CanListenerForm.cs |85f845b310b65b0a89c21cdbd0f7e2e8855c7eb7afc4eb6e86eb7bb2d9f54f20|
| Services/J2534Manager.cs |d0a859440161382af5c4c26a18a5d635da6e6339016871c26fcd8b1f007f707a|
| Services/ChannelLifecyclePolicy.cs |be61b8dc3570c27a7e58171569b52306e9613d1d64b9edc99245f5836d98cd2e|
| Forms/VehicleTabForm.cs |9322f3dc2ff1aebfbd9be08c7ac17c753c2d3674134e8b87b3936dcb1f9c0085|
| Services/NativeOwnedCanSession.cs |ba719abe0ff5d41ea12eb248f791db4f1eb322c4d75b6f8a00fff09b956f7a39|
| Services/NativeCanReadBoundary.cs |44f1a553199dfa6661e7415b30789bf086483b83326f9a9c8a608ad7e5c2161d|
| Services/NativeJ2534StdCallApi.cs |797e853c0514718ff5cfad43b933ae93ebf42bd229515a9198fb88dd68c3c236|
| Services/NativeJ2534DriverCheck.cs |28d7bbe5df87df1ec0af9cdeff1bc155761086a79026530ae34b8fdfbd8c37fa|

Do not claim R21 and R29 transports are wholly identical: intervening work added disconnect-outcome reporting, retained-resource detection, stop-reason reporting, report/close policy and Vehicle control refresh. Those changes are covered by retained R22–R28 software evidence. R29 adds no new transport change.

R29 ZIP:1,897,018 bytes, SHA2560fdad6d5e480bbe885b21ffc15bf7680fdf78791b29f7fcd6392bde470b644f1. Extracted EXE bytes independently rehashed during this review: c4a9b9cf8602d9d3dfcda0cadb29792302e21ea4b62c9f2a7362fff3f126ad0f. Package preserved as libfile_f65b05c6c35081918a33db0b8e058fe5; draft release retained.

R28 fallback ZIP SHA2568c59ceb5c14c7ceb2275c5abffb9b781fdfca9360a0a88d229de725870463b65, EXE632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160; libfile_20f867919ea48191b78304d0e3fc6288. R27 fallback libfile_9c6d9759e9ac819199b187f83eac9a3e unchanged.

Sample independently parsed again as an evidence identity check, not an acceptance rerun:6,552 bytes, SHA25695a78c99e4de52843ee067995fa0b5a31ebe98f3db53e2514736f494459662fd,16 columns and23 data records:1 ExportMetadata,1 SessionStart,12 State,1 Observation,1 WorkerError,1 Disconnect,6 PidSummary. Header: Utc,SessionId,Event,Build,ExecutableSHA256,ConnectionMode,VIN,ResponseSource,PID,Name,Unit,Value,Freshness,AgeMs,ReceivedHostTicks,Details. Sample data are synthetic. All-a exporter hash is an unauthenticated fixture claim.

The R28 owner records preserve exact success/retry/failure CSV hashes and counts. In particular the native-dialog success and post-disconnect/save-failure retry exports have16 columns,16 data records,six summaries,exactly one Pending/Succeeded pair and finalSucceeded metadata. Retry refreshed Vehicle controls and did not disconnect a second time. Refusal/manual export retains failure before later recovery success. No need to regenerate these files.

## Conditional smallest future check — not requested or authorized now

No physical check is pending for this review. Reconsider only after a transport/driver/firmware change, a reproducible new adapter symptom, or an explicit requirement to qualify the exact current executable on hardware. First reuse the existing evidence and select the affected path; do not run both receiver paths routinely.

If a newly justified direct-native capture check is separately authorized: use one stationary capture-only11-bit500kbps session, existing100-frame/250ms read settings and one new disk filename, about10 seconds, then Stop and Disconnect once. Preserve existing app-generated CSV, matching integrity/rejected-record evidence, exact EXE/source hash, installed DLL hash/version and exact errors. Expected evidence is reconciled accepted/queued/written counts, explicit rejected/overflow/drop accounting, writer closure and released ownership. Invalid records require preserved detail and qualified completeness; do not erase them or assert lossless capture. Stop on driver error/overflow, blocked cleanup, writer failure or new vehicle warning; retain evidence and do not force reconnect or repeat. Capture-only may electrically ACK; it is not isolated listen-only.

If the changed behavior instead concerns managed VIN/scalar communication, define a separate bounded test from the affected change; the historical R21 settings are evidence, not a standing request to repeat active diagnostics. No active query, vehicle hookup, DTC clear, programming, actuation, new parking-brake procedure or merge is authorized here. No new logger or package is needed.

## Thirty-step completion accounting

| Step | Completed outcome |
| --- | --- |
|1|Read current Shared Index; v261 preserved before additive update.|
|2|Read canonical registry and coordination; refreshed before guarded writes.|
|3|Verified owner branch/head and PR29 open/Draft/unmerged.|
|4|Verified R29 product source, ZIP and extracted EXE hashes.|
|5|Reconciled preserved owner-PC R29 screenshot and sample identity.|
|6|Retained R28 working fallback and older R27 fallback.|
|7|Read original USB, capture and owner physical records.|
|8|Separated contextual R21, R5-format and exact software build identities.|
|9|Reused managed virtual-driver normal/fault validation.|
|10|Recovered direct-native Nov2/Nov9 physical results; did not leave superseded hardware gap open.|
|11|Kept OneApp physical results separate.|
|12|Classified physical, synthetic, virtual and source evidence separately.|
|13|Reconciled R21 VIN/six-scalar connection observations and their limits.|
|14|Preserved managed overflow history and native invalid-record/completeness findings.|
|15|Preserved owner-confirmed error-free Disconnect.|
|16|Recorded historical installed-driver metadata/hash and current-identity limit.|
|17|Compared R21-to-R28 and R28-to-R29 connection changes.|
|18|Compared receive/capture/native code.|
|19|Compared shutdown/ownership and existing test coverage.|
|20|Reviewed saved-tab construction and existing guarded navigation behavior.|
|21|Completed requirement/evidence table.|
|22|Applied CONFIRMED/UNVERIFIED scope labels.|
|23|Removed completed checks from pending work; preserved historical evidence.|
|24|Decided no routine R29 hardware retest is warranted for saved-review closure.|
|25|Defined a conditional minimum check only if a future material trigger exists.|
|26|Recorded conditional settings, expected evidence and stopping conditions.|
|27|Used existing CSV/sidecar/report logging; no instrumentation or build.|
|28|Recorded owner readiness decision in this document and current handoff.|
|29|Updated canonical shared records and Shared Index; read back before reporting synchronization.|
|30|Reported closure and remaining limits; no owner action required.|

No code changes, CI/build dispatch, provider registration, RLink/vehicle connection, diagnostic transmission, clear, programming, actuation or merge occurred in this review. This closes the thirty-step review; it does not declare the entire vehicle diagnostic project or all future tool development finished.

References: R29_SAVED_SESSION_REVIEW.md; R28_FINAL_DISCONNECT_CLOSE.md; R28_NATIVE_DIALOG_COMPLETION_2026-11-26.md; R28_FAILURE_DIALOG_COMPLETION_2026-11-27.md; R22_RELEASE_VALIDATION_2026-11-24.md; R21_RLINK_READINESS_2026-11-23.md; NATIVE_GUI_PREVIEW_HANDOFF_2026-10-26.md (later dated entries override historical pending notes).
