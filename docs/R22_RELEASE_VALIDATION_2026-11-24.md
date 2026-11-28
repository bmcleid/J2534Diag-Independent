
## R22 release closure — owner-approved validation PASS; twelve-step request completed

This additive closure supersedes the R22 checkpoint's pending-validation/no-release status. The two failed attempts remain preserved; no earlier failure is reclassified as success.

### Authorization and validation

Owner explicitly replied **Approved** to one further Windows validation after the two same-stage layout failures. Approved source **26faeb740bb6a80ad7168d345169cef26551104c**, [Windows45532404468](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45532404468), job **138930682746**, **SUCCESS**. Branch feature/native-gui-capture-preview; draft PR29 remains unmerged.

- **6663 regression assertions**, including27 scalar export checks,28 VIN rejection/recovery and30 actual FuelTrimForm checks.
- **5 historical identity/defect witnesses**, **29 native StdCall checks**, and the historical managed startup ownership failure reproduced as expected.
- Missing-VIN7 and interrupted-VIN7 actual managed checks PASS.
- **33 managed normal/VIN/form/lifecycle/export/reconnect checks PASS**: duplicate Start reused its CTS; actual MainForm tab handler left/returned three times; prior workers/lease retired before each new pair; successful Disconnect and export verified.
- **18 broken-driver checks PASS**: virtual pipe interruption canceled peers, workers joined, failed filter cleanup retained receive ownership, Disconnect refused rather than falsely claiming closure, and CSV preserved worker/receive/cleanup errors and failed outcome. Isolated test-process exit retires virtual IPC handles; this does not prove physical-driver recovery.
- Main build4warnings/0errors; virtual DLL0warnings/0errors; host2warnings/0errors.

Three Windows runs in this release cycle:45531068605/job138926205750 FAILURE (initial layout);45531331077/job138927104988 FAILURE (internal viewport);45532404468/job138930682746 SUCCESS (owner-approved further validation). No same-SHA rerun or fourth run. No Android, physical scan, provider registration, DTC clear, programming, purchase or merge.

### Twelve-step completion

1. Shared records/ownership/branch/CI state refreshed; independent scope retained.
2. Accepted R21 runtime/source/test/diagnostics/screenshots/disconnect evidence preserved in verified baseline ZIP (Librarylibfile_4c2dedb8142c8191aa0da61051c2645d).
3–4. Clipped nested layout audited and replaced with three direct client-sized regions. Wide actual test client1360x640 gave grid viewport1348x446, two658x416 banks side by side. Narrow client800x600 gave scrolling/stacked banks; both bottom-row access and second-bank scrolling passed. The wide DrawToBitmap image is constrained/cropped by hosted window capture (1044px image despite1360px test client); full-width evidence is the measured bounds/assertions. Narrow screenshot was visually inspected with bank2 scrolled into view. Do not claim an uncropped full-width pixel acceptance image.
5. Build/source ID visibly present in titles/action row; source stamp and runtime EXE hash verified.
6–8. Session CSV implemented and tested: exact calculated values/zero, response sources, acceptance UTC plus monotonic receipt ticks, fresh/stale/unknown/stopped state, source/mode/VIN/session/build/hash and disconnect outcome. Model capacity20000 rows, discarded-oldest count explicit. UI coalescing means this is not lossless/raw CAN. Unknown/stale/stopped state-transition values remain blank; historical observed values are not invented.
9–10. Actual tab/start/stop/reconnect and virtual driver-interruption tests passed as detailed above.
11. Approved Windows validation and artifact checks completed.
12. Runtime/evidence package saved; owner handoff and canonical registry/index synchronized. R21 remains preserved as accepted physical baseline; R22 is software/virtual validated, not newly vehicle tested.

### Independent artifact and CSV verification

Diagnostics artifact14984988127:334004bytes, original ZIP SHA256 **8a584b382828e84221e28e5708ed71f71ea997714464d08184d3ad2b798655d6**.
App artifact14984943278:1874833bytes, original ZIP SHA256 **46c170bb5f04ae78e4644b831576086901fde28996cce10630b6bf8b40af9e90**.
EXE SHA256 **8b7a73260175b477b4ae53c790571165039034d7e2c3d3f8f0658a0d5ec470e8**.
Downloaded ZIP SHA/CRC, every manifest payload hash, embedded source/run and actual CSV contents independently checked.

Actual normal CSV96rows incl metadata/95retained rows, zero discarded; multiple session IDs preserved, synthetic0x7E8/Mode01 RPM1000 observed, final DisconnectOutcome=Succeeded. Actual broken-pipe CSV31rows incl metadata/30retained, zero discarded; final DisconnectOutcome=Failed and CleanupError present. Every CSV row carries actual release EXE hash/full source; unknown/stale/stopped transition values blank. Normal CSV SHA25620896aaa9d971f8d99989ae9318b02710fd89d8e459156beca637c62c7b74907; brokenCSV SHA256af27f51a6142c33894feba05cf5a1a89aa80f1c2d268a5d2265368588b47e7a1.

Saved **J2534Diag-R22-Windows.zip**, Library **libfile_2d5935213950819183a381144427fedd**,1874833bytes; receipt and local metadata confirmed. Successful full diagnostic ZIP also preserved as J2534Diag-R22-Validation-Evidence.zip. R21 baseline and pre-validation R22 review checkpoint remain historical artifacts; review ZIP is superseded for current release status, not deleted.

### Use and limits

Extract R22 into its own folder, open J2534Diag.exe, and confirm title **R22 /26faeb740bb6**. Fuel Trims now has **Export session…**; it remains available after stopping/disconnecting. CSV is a bounded app observation/session record, not a raw CAN capture. Build identity/export/layout/lifecycle work is complete; no additional vehicle scan is needed to close this task. Physical installed-RLink acceptance remains the recorded R21 session until R22 is separately observed. No TFP or fuel-system fault/repair conclusion follows.
