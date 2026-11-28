# R31 — bounded, responder-specific Mode06 support discovery

## Acceptance state

**CONFIRMED scoped offline software acceptance:** product `251ea31474de339e754f9057c070e8fe375e365e`; [Windows run 46170048742, job 141056183223](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46170048742/job/141056183223) SUCCESS on November 28, 2026 at 12:22 PM Eastern. **102 focused checks pass** (81 model assertions plus 21 production UI/poll-path checkpoints). Actual logs inspected. Full Windows product build: zero errors, two pre-existing unused-field warnings; harness: zero errors/warnings. One product build/one focused hosted gate, no identical-SHA rerun. Older mode6-review and full baseline jobs were skipped by marker. No historical/native/virtual/R28/R29 suite repeated.

Local model run passed 81 checks; changed form/new harness also compiled against retained R30 dependencies (Mono C#7.2 seam compile, three expected duplicate-source/type warnings). This local compile is not misrepresented as the Windows build.

Rendered `r31-discovery-synthetic-ui.png` independently inspected: title R31/251ea31474de, source 0x7E8, completion/warning text visible, observed zero, missing TID/monitor versus Not advertised cards distinct, Disconnected/blank VIN footer. This is synthetic hosted UI evidence, not an owner-PC R31 launch or a vehicle measurement. No owner launch/scan requested. **R30 remains the accepted owner-PC fallback**; R31 is accepted only at this offline software scope. PR #29 remains open, Draft and unmerged.

## Scope and behavior

Each Misfire tab start creates a fresh support session. One functional `06 00` gathers response-source-specific availability. Only a source advertising the next range receives directed `06 20/40/60/80/A0/C0/E0` continuation requests. A cleared continuation bit ends that source's chain. A missing, negative or malformed response cannot establish absence. The four-byte availability integer is big endian: its most significant bit is page+1, its least significant bit is the next availability range. E0's least significant bit is reserved, not a wrap to 00.

Only enabled, implemented reference MIDs A2–A9 advertised by that same responder can then receive directed result requests. Discovery and measurement payloads cannot substitute for one another. Sources, current request/MID, completeness, duplicates and generation boundaries are checked. Support is never shared between responders. There is one bounded result snapshot, not continuous polling. Changing tabs away/back permits a fresh attempt after the existing reader-ownership cleanup.

UI states distinguish Undiscovered, Not advertised, No result, Unverified, a real measured zero and unknown scaling. Advertised MID support does not prove a particular TID, scaling, cylinder mapping, calibration applicability or diagnosis. The generic eight-slot reference list does not identify the six-cylinder SRX as eight-cylinder. Tooltips retain source bitmap/failure status and existing raw value/limits. Previously loaded saved scalar sessions are unchanged; Mode06 discovery is not a new saved-session CSV schema or export claim.

Application scheduling policy: 300 ms response window, 50 ms request gap, maximum 128 requests and 30 seconds before scheduling another request; at most 16 responder records. These are application limits, not verified OEM timing requirements. A blocked provider call cannot be preempted by this clock. Existing bounded Stop/ownership retention remains in force. NRC 78 does not extend the window; a final response within it may still be accepted. Other negatives terminate that source/request. Stop/error/timeout preserve prior evidence rather than fabricate zeros.

## Primary-source basis

- ISO 15031-5:2006 §7.6.2 (Tables 157–160), Annex A Table A.1: service 06/46 availability versus test-value formats and bit ordering. Original standard PDF mirror: https://share.qclt.com/%E6%B1%BD%E8%BD%A6%E8%AF%8A%E6%96%AD%E5%8D%8F%E8%AE%AE2/ISO-15031-5%5B1%5D.pdf . Used for technical verification only; no licensed PDF redistributed.
- AUTOSAR DCM R23-11, SWS_Dcm_00414/00945/00956/00957/00958, service 06, pp205–206: availability pages and separation from nonavailability result requests. https://www.autosar.org/fileadmin/standards/R23-11/CP/AUTOSAR_CP_SWS_DiagnosticCommunicationManager.pdf
- AUTOSAR DEM R21-11 SWS_Dem_00766, Dem_DcmGetAvailableOBDMIDs: uint32 bit-coded availability. https://www.autosar.org/fileadmin/standards/R21-11/CP/AUTOSAR_SWS_DiagnosticEventManager.pdf
- Existing R30 GM reference review remains qualified; no new vehicle-specific interpretation is inferred from generic standard support.

## Focused acceptance design

Model: bitmap boundary/order/continuation, two-source interleaving, wrong source/page, measured zero, no unsupported requests, timeout, malformed/negative/pending, duplicate/conflicting bitmap, cancellation/restart/stale reply, 29-bit route, request/time caps and send failure.

Windows: full production build once; new actual MainForm/MisfireForm fake-transport polling seam; real production request construction and single-frame receive/source/ISO-TP processing; complete result payload injected at existing parser event boundary. This deliberately does not open a provider or prove driver reassembly. Production-thread marshaling, real card states, source selection, visible/narrow toolbar, negative/malformed/cancel and restart generation are checked. Preflight and before/after assertions require zero registered providers/handles. Save actual rendered UI for inspection. Reuse R30 decoder/transport/lifecycle checks and R28/R29 acceptance; do not rerun old suites or owner launch/vehicle scan.

## Remaining limits

Installed-ECU advertisement, TID/calibration meaning and physical transport remain UNVERIFIED by these offline tests. No RLink/vehicle connection, diagnostic transmission, DTC clear, programming, actuation or merge is authorized or performed. An offline accepted R31 package is a software acceptance only. Keep R30 until R31 passes its focused gate and the exact tested bytes are Git-preserved.

## Exact accepted package and preservation

- [J2534Diag-R31-Windows.zip](https://chatgpt.com/library?file_id=libfile_1700d663efc481918307fd3e66d4cb71&account_id=personal), 1,912,701 bytes; SHA-256 `3331300ff707b3c64e27f5edc662486c15c5f92827d94693f4b59a8f2ebfb267`; EXE SHA-256 `1e88570626de244c7ae1154c4f8c591c4497b35b4807e4a20ffe1bb41c7b5841`. Library version0, backing `file_0000000016b081f59a04cee089290beb`; save/local identity confirmed.
- Package source/run identity, ZIP CRC and all eight manifest payload hashes independently match actual bytes and CI package log. Outer GitHub artifact15273103330 is 1,911,724 bytes, SHA-256 `f905f295de4cc8da1cb70b6c842b7a81ae274cb1f6d2fbabb14ec20b0b8b9c5e`; this differs from the inner delivery ZIP by design.
- [Exact evidence ZIP](https://chatgpt.com/library?file_id=libfile_f3b49917ca988191b55fe8f5c6b5f8f6&account_id=personal), 28,833 bytes, SHA-256 `ea9a233f63e80f5369145248f3e673fe45c49c3dae3c896bb4b83168fa67bf8d` (GitHub artifact15273287867). Includes preflight, actual build/harness/focused logs, package hashes, manifest and rendered UI. Library version0/backing `file_000000004d9481f5b39e6b0df62e2d55`, save/local identity confirmed.
- **Preserved directly on master:** [acc2413b9191753e37fa92d831433d3ab9d3eb16](https://github.com/bmcleid/J2534Diag-Independent/commit/acc2413b9191753e37fa92d831433d3ab9d3eb16), `preserved-builds/j2534diag/vR31/J2534Diag-R31-Windows.zip` plus checksum. Unmodified `tools/preserve_build.py ... --product j2534diag --version R31 --expected-sha256 3331300ff707b3c64e27f5edc662486c15c5f92827d94693f4b59a8f2ebfb267 --push` passed identity/copy/rotation/local commit; CLI push failed for absent credentials. Authenticated Git Data API published identical blob `2725f41bf450b3867f6157c23e60ba1697d79a62`, identical complete script tree `dcd8196339dc9bb7297057aba484d612aec1eab1` and preserve message with a guarded non-force master update. Script local commit `18d000f` differs only by commit metadata. No hash mismatch/bypass or rebuilt substitute.
- Fresh remote Git readback verified exact ZIP/EXE hashes and CRC. Current tree has exactly vR30/vR31, each ZIP/checksum; vR29 remains recoverable at `af83c0188d097885fa2501b3e54023a5c8f240e5`, independently rehashed. The preserve commit is already on master, not stranded on the feature branch. No mirror refresh claimed. Source changes remain only on the Draft feature PR; no runtime merge occurred.

## Thirty-step outcome ledger

| Step | Verified outcome |
|---|---|
| 1 | Shared Index278 read before work; fresh281 reread for writeback; registry/coordination/CI gate refreshed. |
| 2 | Exact owning branch/head, 36 branches and 29 stacked open Draft PRs checked; no discovery competitor. |
| 3 | Current master AGENTS/build preservation script/docs carried into development branch. |
| 4 | Accepted R30 owner-PC screenshot and R30/R29 exact Git backfill retained; no repeat checks. |
| 5 | Discovery acceptance requirements and limits written in this owner record. |
| 6 | Available saved reports/earlier inventory reused; no vehicle-specific support bitmap fixture established; vectors explicitly synthetic. |
| 7 | Primary ISO/AUTOSAR service06 request format inspected. |
| 8 | Positive availability format separated from nine-byte test records. |
| 9 | Big-endian bitmap/continuation/end-of-range semantics verified and tested. |
| 10 | Advertised MID support kept distinct from TID/scaling/application validity. |
| 11 | Per-responder state/bitmaps; interleaving source-isolation test passes. |
| 12 | Discovery cannot publish a measurement, and measurements cannot establish support. |
| 13 | Bounded response/gap/request/time scheduling tested; blocked-call limitation explicit. |
| 14 | Cancellation, stopped-before-start, restart and stale-generation checks pass. |
| 15 | Negative/pending/malformed/missing replies remain inconclusive; focused checks pass. |
| 16 | Only enabled implemented advertised same-source MIDs queried; physical routes checked. |
| 17 | Actual UI distinguishes missing, absent, inconclusive, zero and unknown units. |
| 18 | Reference-list blind repeated polling replaced by discovery plus one bounded snapshot. |
| 19 | Release/source stamping and package/run manifest independently verified. |
| 20 | Existing saved scalar schema/source untouched; no new Mode06 export claim. |
| 21 | Bitmap/order/boundary model assertions pass. |
| 22 | Interleaving/wrong-source/page/duplicate/malformed assertions pass. |
| 23 | Timeout/cancel/restart/request/time caps tested locally and hosted. |
| 24 | Actual production form/polling/request and bitmap receive path tested with fake transport. |
| 25 | Local81 model checks and changed-form/harness seam compile pass. |
| 26 | Single focused Windows gate passes102 checks; old suites reused. |
| 27 | Exact package/logs/manifest/CRC and actual rendered UI inspected and pass. |
| 28 | Accepted exact R31 ZIP preserved/read back on master; two newest folders retained. |
| 29 | Owner record/handoff, shared registry/coordination and guarded Shared Index update/readback recorded at closure. |
| 30 | Software milestone closed with exact remaining qualification boundary; no physical test or merge performed. |

**Overall scanner objective remains open:** installed-ECU Mode06 support/TID/calibration interpretation, sustained live scalar freshness/accuracy, and any later separately authorized exact-final-build physical qualification. These are not failed offline gates and do not justify repeating the completed R27/R28/R29/R30 checks. No owner action is needed to close this R31 software milestone.
