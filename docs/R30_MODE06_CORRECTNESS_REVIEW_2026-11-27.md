# R30 Mode06 correctness review — November 27, 2026 Eastern

Owner: Part4 Analyze J2534Diag, bmcleid/J2534Diag-Independent/feature/native-gui-capture-preview, PR29 Draft/unmerged. Starting owner head a0749d663567aab1ce4fa00ff2e89ead39ef4958. R29 saved-review acceptance and R28/R27 fallbacks remain preserved. This is the authorized thirty-step diagnostic-feature review, separate from the completed R29 readiness review.

## Requirements and scope

Correctly distinguish monitor ID, test ID and units/scaling ID; reject incomplete records and non-response/indication data; preserve measured zero separately from missing data; isolate responders; display raw limits without invented pass/fail interpretation; marshal UI updates; reject late prior-session updates; expose worker/cleanup errors; cancel peer workers and retain ownership until cleanup. Keep unsupported definitions and physical compatibility explicitly unverified. No new vehicle/adapter action or merge.

## Authoritative findings

- [GM Mode06 index](https://gsi.ext.gm.com/gmspo/mode6/index.html) distinguishes generic CAN definitions from model-year engine diagnostic parameter documents.
- [GM GMLAN reference rev1, pages9–10 and13](https://gsi.ext.gm.com/gmspo/mode6/pdf/Bmode6/GM%20CAN%20mode%20%2406%20data%20final_rev1.pdf) identifies A2–A7 as cylinder1–6 reference monitor IDs, 0B as an EWMA result and0C as last/current driving-cycle count, UASID24 with count scaling. An older-model exception exists; do not erase footnotes or treat this as proof of this ECM's supported results. [Current rev4](https://gsi.ext.gm.com/gmspo/mode6/pdf/GM%20CAN%20mode%20%2406%20data%20final_dm.pdf) contains additional model-dependent reporting-period/calculated-result footnotes. Both are reference definitions, not this vehicle's response evidence.
- [2012 application matrix, page2](https://gsi.ext.gm.com/gmspo/mode6/pdf/2012/2012_OBD_GROUP_SUMMARY.pdf) lists SRX3.6L LFX/VIN-engine3 under12OBDG05B; recorded VIN3GYFNEE31CS519173 agrees with that engine digit. [2012 engine document](https://gsi.ext.gm.com/gmspo/mode6/pdf/2012/12OBDG05B%20Engine%20Diagnostics.pdf) includes P0300 monitoring information but does not establish which Mode06 response records this installed calibration will return.
- **Correction to preliminary wording:** numeric A2/0B/24 etc entries are corroborated as generic GM references. Their legacy field names Tid/Cid/SubId were misleading CAN terminology; this is not evidence that every numeric mapping was wrong. Renamed to MonitorId/TestId/UnitsAndScalingId. Eight reference cylinder slots are generic references, not a claim that this SRX has eight cylinders. Current is relabelled last/current; History is relabelled EWMA. Mode06 is not an instantaneous live misfire counter.

## Source-confirmed defects and correction

The historical parser loop accepted eight remaining bytes although it advances by nine; it could publish a value despite an incomplete max-limit field. Its UI-thread branch did not assign a found control, while the unknown-control branch dereferenced null. All responders wrote to the same controls. Receive exceptions silently ended only one worker; poller failures could escape; checkbox changes were not wired to polling definitions. Fixed-source flow control did not follow the actual sender. The old29-bit filters used different address families from standard normal-fixed response handling.

R30 adds Mode6Response: complete46 application payloads only, exact9-byte records, raw16-bit value/min/max retained; support bitmaps and malformed/truncated replies never become results. Only GM-reference A2–A9/0B or0C/UASID24 are labelled reference counts; other units remain unverified. Source/direction/RxStatus are checked before unchanged shared ISO-TP reassembly. Flow-control target follows the accepted responder. This normal-addressing boundary is explicit; proprietary extended address families are unsupported rather than guessed.

Production UI initializes Unknown, isolates observed records by source selection, exposes raw fields in tooltip/accessibility detail, labels last-observed state and unknown units, and marshals background updates asynchronously with a session-generation guard. No multi-source values are combined. The peer-cancellation helper and existing bounded reply gate are reused; six consecutive unanswered requests stop polling. This is an app pacing policy, not an OEM limit. Duplicate Start cannot replace retained workers. Failed startup/worker/cleanup and pending shutdown are visible; ownership stays until both workers join and filters clear. Direct form close is cancelled when ownership remains. Mode06 support discovery remains absent: the old00 query was not used to discover support, and its unused response is no longer presented as discovery. Existing reference requests remain experimental, not a validated SRX coverage claim.

No shared VIN, native CAN capture, close-report, saved-review parser, fuel-trim formulas or sibling runtime is changed. Two existing FuelTrim integration-test release-string expectations trackR30; the old full suite is not rerun. Baseline workflow skips the explicit mode6-review marker so one focused new-artifact workflow owns validation. This does not disable full validation for other changes.

## Validation accounting — pending hosted results

Local isolated compiler/runtime was restored into scratch without system installation. Changed form/model and both focused harnesses compile against retainedR29 unchanged dependencies. Mono seam compilation reports expected duplicate-type warnings because new form/model types intentionally shadow the retained dependency assembly; this is not a full Windows product compile. XML/YAML parse and Git diff checks pass. Pure model checks:41 PASS, including truncation, zero/high counts, unknown units/test, two records, rejected support/negative replies, clean11/29-bit response routing and indication rejection, actual shared ISO-TP interleaving and bad sequence.

One initial local harness assertion compared the low byte of a flow-control ID to a full integer address; corrected to a byte comparison, then41 checks passed. That was a harness assertion error, not a product routing failure. Earlier local compiler setup required explicit Mono native configuration, and nested tuple syntax was simplified to integer keys for compiler portability. These are local preparation outcomes; no hosted success is claimed here.

The single planned Windows gate will identity-check the original R29 EXE, reproduce three new defect witnesses without rebuildingR29, compileR30 once and exercise focused model/actual production UI with zero registered providers/API/device/channel/native handles. It will not rerun581 saved-review checks, R28 close suites, historical R16/R27 controls, native ABI/virtual-driver suites or any vehicle session. Results must be recorded below before package acceptance.

## Fuel-trim review and physical boundary

Existing R12–R21 scalar validation/formula/source/freshness and R22–R29 lifecycle/export/UI evidence were reviewed and reused. No new fuel-trim source defect was established by this review; no missing deterministic helper gate was identified that warrants repeating old assertions. Supported-PID discovery, sustained live freshness/accuracy and exact current-build hardware behavior remain UNVERIFIED. An offline fixture cannot establish ECU support or physical measurement accuracy.

No hardware check is requested now. After software acceptance, the minimum separately authorized future diagnostic qualification would use the exact hashed build/driver, one selected feature and existing reporting; record support evidence and source-linked response bytes, then stop/disconnect once. Do not combine routine native capture, fuel-trim and misfire retests. Active Mode06 testing must wait for a specific supported-monitor/request plan; this review is not authorization. Keep no-code-clear/no-programming/no-actuation/no-merge boundaries and no new parking-brake procedure.

## Thirty-step accounting

Steps1–10: refreshed Index267/registry/coordination/CI/owner/36branches/29stackedPRs and prior logs; preservedR29/R28/R27; inventoried absent dedicated Mode06 acceptance; traced requests and unused00 probe; reviewed missing discovery, IDs, generic GM references versus exactECU support. Steps11–18: audited single/segmented paths, source isolation, strict lengths, units/raw limits, unknown state, UI exceptions, polling/cancellation and ownership/cleanup. Steps19–23: local checks prepared/passed as above; historical executable witnesses and actual Windows UI acceptance remain pending. Step24: README capability claims qualified. Steps25–26: existing fuel-trim evidence reused, physical gaps retained. Steps27–28: physical evidence needs assessed and conditional boundary recorded, no test authorized. Steps29–30: final records/readback and completion report await hosted results. Do not mark all30complete from this preparatory checkpoint.


## First Windows run — assertions pass, visual layout acceptance fails

Windows46092164755/job140815335851 at ec8c35d2d93138bb5270f1aebb8af610fe5779f9 concluded SUCCESS on its first attempt: three R29 exact-binary defect witnesses reproduced; production compiled with two pre-existing SelectedVehicleControl unused-field warnings; focused62 model/actual-form checkpoints passed with zero providers/handles. No full suites or vehicle actions. Package creation also passed. However independent screenshot inspection found the new toolbar hidden behind the embedded Fill layout: the source selector, unverified-status warning and worker-status label were not visible. Therefore this first package is superseded and is NOT accepted for delivery. Passing value/state assertions did not establish visual usability.

Prepared correction places toolbar and gauges in separate TableLayoutPanel rows. Added visibility/bounds/source-selection checks at wide and narrow sizes. The next gate compiles this changed product once and runs only layout/source-selector acceptance;41 model checks, other lifecycle/GUI assertions and three exact-R29 witnesses are reused, not rerun. First artifact wrapper15242056682 SHA256a9dd16dc4d7a2d4bd87d338babc1c42e6f99ee4386de90be3a32bc0bf37eb680; superseded inner package1,901,612bytes SHA25655ea360361161bc59cde86fbf983f4c69ad256280e13980151b92efc290fea4e; EXE00480476819a5130becafec3d138645f0745102713768cb2ed31af630a3d5fc9. Original evidence artifact15241204269 SHA2566312f6a0a927c97ab63ae2679c7f5411002a3b7b5fc9122000f03499bba5ebf5 retained. This is a production layout defect, not a harness failure.

## Verified completion — November 27, 2026 Eastern

**CONFIRMED software acceptance:** corrected product source `bb602372a5a1e9832b892fadf2cf45279d7f145a`, [Windows46092433553](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46092433553), job140816188544, SUCCESS. This is the second product compile across two different source artifacts; no identical-SHA rerun. The first product/source ec8c35d2d93138bb5270f1aebb8af610fe5779f9 passed62 focused checks and three unchanged-R29 witnesses, but failed independent visual acceptance because the toolbar was hidden. That earlier package remains superseded. Both actual workflow conclusions were SUCCESS; the first product's visual defect is explicitly retained, not hidden by calling the release all green.

The corrected source differs in toolbar layout only in production. The follow-up skipped the R29 download/witnesses and41 model checks and ran **seven layout checkpoints**: empty provider inventory; visible source/status controls; separate non-overlapping toolbar row; controls within wide bounds; actual source selection; narrow wrapping/bounds; zero API/device/channel/native handles. Screenshot inspected: source0x7E9 selector, experimental/unverified reporting-period warning, last-observed status and synthetic42 appear above separated gauges. The reference cylinder7/8 slots are generic GM references, not engine configuration; no real vehicle data were used. The62 earlier checks are reused at their tested scope, not claimed rerun against the final binary. No full/historical/native-ABI/virtual-driver/saved-review/dialog suite repeated.

Final product compiled with two existing unused SelectedVehicleControl fields and no new product warnings/errors. Layout harness has one unused local-variable warning; no errors. Local preparation compiled the changed form/models/harness and executed41 model checks; expected seam-shadowing warnings are not Windows product warnings.

### Exact package and evidence

- Final inner `J2534Diag-R30-Windows.zip`: **1,901,701bytes**, SHA256 `fb0f81ff5549ddab9796643e5e45fabb53f02de17af3ff1c72d3fca64a6ec015`.
- EXE SHA256 `c3a9fe6c3f58e1b35b636538d58e5fa2e55170d16cc5240283cc75932877d30b`.
- Final runtime wrapper artifact15242161796:1,900,752bytes, SHA256 `38409777cfdc9bdea0345cedde7239582ff820c555a92d2f8aacd7b4f78a280c`.
- Final layout evidence artifact15241324576:29,703bytes, SHA256 `81fe009eec55b1fd9cc0a822b9b05cb5279b84073999c9d760e3442158659993`.
- Independent wrapper/inner ZIP CRC, all8 payload hashes and exact source/run manifest PASS. Original first and final screenshots, test/build logs, full hosted logs and artifact bytes preserved. No source CSV or original physical recording modified.
- PR29 remains open/Draft/unmerged. Keep R29 accepted saved-review package and R28/R27 fallbacks; no owner install, scan or reconnection is required to close this plan. R30 is a scoped engineering preview, not a fully qualified SRX scanner.

### Final thirty-step outcomes

|Step|Verified result|
|---|---|
|1|Shared Index267 read at start; refreshed before guarded additive final update.|
|2|Canonical registry/coordination and authoritative CI gate read; claim and results recorded.|
|3|Owner branch and29 stacked Draft PRs/36branches reviewed; no overlapping Mode06 branch.|
|4|Remaining requirements stated above; no expansion into sibling tools.|
|5|Prior tests/evidence inventoried; completed suites reused.|
|6|Requests traced:06 plus monitor ID; old unused00 query did not establish support.|
|7|Supported-monitor discovery reviewed and explicitly remains absent/unverified.|
|8|Legacy eight-cylinder reference table audited; names corrected, no eight-cylinder SRX claim.|
|9|GM generic definitions/current footnotes and2012 SRX engine-document applicability reviewed.|
|10|Generic reference definitions separated from unobserved installed-calibration support.|
|11|Single-frame support/negative replies cannot become test results.|
|12|Shared ISO-TP source/sequence handling checked with new Mode06 payload fixtures; receiver gating reviewed.|
|13|Exact complete nine-byte record boundaries enforced; truncated final record rejects whole reply.|
|14|Raw unsigned value/min/max preserved; only known reference count scaling labelled; no invented pass/fail.|
|15|Missing/unknown-unit states explicit; measured zero retained.|
|16|Three exact-R29 defects reproduced; UI assignment/null handling/marshalling corrected.|
|17|Coupled worker cancellation, visible errors, bounded unanswered polling and checkbox wiring corrected.|
|18|Retained ownership, blocked Stop, retry and duplicate Start checked; no real provider used.|
|19|Unchanged R29 EXE witnesses confirmed at exact hash in first Windows gate.|
|20|Coherent Mode06 correction implemented; screenshot-discovered layout defect corrected separately.|
|21|Focused policy/production-UI and visibility regression checks added.|
|22|Local41 model checks and compilation; Windows62 then scoped7 checks passed, with scopes/failures above.|
|23|First and corrected actual UI screenshots inspected; corrected selector/status visibly accepted.|
|24|README broad compatibility/Mode06/live-counter claims qualified.|
|25|Fuel-trim evidence/source gaps reviewed without repeating accepted session.|
|26|Existing deterministic freshness/source/missing/zero/form gates reused; no new missing offline assertion established.|
|27|Physical support/accuracy/exact-build limits retained; no routine capture retest justified.|
|28|Conditional minimum physical evidence plan recorded; active Mode06 qualification waits for a supported-request plan and separate authorization.|
|29|Owner handoff/shared registry/coordination/index updated and read back before completion claim.|
|30|Review results, package, preserved failure and exact remaining objective gaps reported.|

### What remains for the complete objective

The review/correction plan is complete; the entire scanner objective is **not** declared complete. Remaining requirements are supported-monitor discovery and installed-calibration Mode06 qualification, sustained live scalar support/freshness/accuracy evidence, and eventual exact-final-build hardware qualification if claiming full physical acceptance. Mode06 raw value/limit detail is in memory/tooltips, not added to the fuel-trim scalar CSV schema; persistent Mode06 reporting is not claimed. No validated misfire diagnosis, current cylinder fault or TCM/TFP meaning follows.

First reuse any existing saved Mode06/support responses if available. Do not infer absence of support from no existing record or prescribe a routine scan. Any future active test must specify one selected feature, supported/request scope, exact binary/driver, existing output evidence and Stop/Disconnect conditions; it requires separate authorization. No programming, DTC clear, actuation, parking-brake procedure or merge is part of this completed plan.
