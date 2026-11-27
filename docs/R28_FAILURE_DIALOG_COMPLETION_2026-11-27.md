# R28 disconnect-refusal and retained-reader dialog acceptance — November 27, 2026 Eastern

**CONFIRMED: all twenty authorized steps complete for this failure/recovery plan.**

[Windows run 46040117261](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/46040117261), job `140645403345`, **SUCCESS on the first scoped attempt**, completed at 12:15 PM Eastern. Harness source `a0f9db7a315764d4dd994557afcfd3f6ae9b10d6`, branch `test/r28-close-failure-ui-20261127`, based on owner checkpoint `b208527e1e7c6e9992e7afef490e7349d76f00ad`. The unchanged exact R28 executable comes from existing Windows run `45904637124`, product source `03d8a2a611953ff912f79a2e0d3af056b3194a1f`.

## What was already established

Prior policy/form tests already covered injected disconnect refusal/manual export and retained-reader guards. Actual normal R28 close/Cancel/Save As/locked-file/retry/No dialogs passed run `45908418443`. The new gap was actual failure-dialog navigation and recovery using those real forms. Prior R27/R28 normal suites, historical R16/R27 controls, full regression runs and owner launch screenshots were **reused, not repeated**. Accepted R27 remains unchanged as fallback; R28 remains the current delivered preview.

## Twenty-step completion map

| Step | Confirmed result |
| --- | --- |
| 1 Refresh owner/shared records and PR | Shared Index v248, owner R28 records, registry and coordination read; owner PR29 verified open/Draft/unmerged at b208527 before work |
| 2 Carry forward R28 acceptance | Prior normal-dialog run and owner screenshot libfile_76a2af0b10408191b57a76523f89c6ff reused |
| 3 Preserve runtime/fallback | Exact R28 ZIP/EXE hashes matched locally and on Windows; accepted R27 item unchanged |
| 4 Review refusal evidence | Existing form policy tests found; not repeated as a separate suite |
| 5 Review reader evidence | Existing injected gate checks found; not repeated as a separate suite |
| 6 Identify actual-dialog gaps | Refused disconnect through Save As/Export error/manual export and pre-close retained-reader Close incomplete dialog |
| 7 Focused matrix | Two production-window lifecycles: post-destination refusal/export/recovery and pre-close reader block/recovery |
| 8 Reuse harness/exact EXE | Existing UIA/native-window and filename keyboard/Tab-commit helpers; harness compile only |
| 9 Zero-provider requirement | Registry HKLM/HKCU, 32/64 views checked; inventory zero; actual manager Devices empty, API/Device/Channel/native session null |
| 10 Refusal after destination | While native Save As is open, no refusal attempt/receive owner; hook acquires logical receive ownership only after Save/overwrite selection, then calls actual production disconnect callback |
| 11 Preserve retained state | Actual manager emits Pending and Failed, throws InvalidOperationException, retains global logical connection/receive ownership; app/report remain open |
| 12 Destination unchanged | Original target hash/content unchanged through refusal, manual export and recovery |
| 13 Explain failure | Actual Export error names retained receive owner; actual Close incomplete dialog explains app/report retention and reader ownership. Screenshots and UI trees inspected |
| 14 Actual manual Export | Real Export button/Save As produces a separate failure CSV while connection/receive owner remain retained |
| 15 Verify failure CSV | Independently parsed exact Pending/Failed rows, Failed metadata, zero observations, six summaries and exact runtime provenance |
| 16 Recover close | Release synthetic receive gate, restore original disconnect callback, actual Yes/Save As closes successfully; prior failure remains in recovered CSV before one Pending/Succeeded pair |
| 17 Actual retained-reader close | Receive gate held before close; production pre-close guard displays Close incomplete before Session report/Save As/disconnect; outcome remains Not attempted |
| 18 Reader-release retry | Release logical gate; actual Yes/Save As closes, yields one Pending/Succeeded pair with no false earlier attempt |
| 19 Independent verification | Download size/SHA/CRC, three parsed CSVs, event order, original target, screenshots/UI trees and unchanged EXE checked |
| 20 Preserve/synchronize | Full hosted log/original artifact/harness/source/checks saved; owner/shared record writes verified by readback |

## Fixture fidelity and limits

Output reports **1,218 checkpoints**, including repeated loaded-module filename checks, **not 1,218 functional scenarios**. Real production MainForm.OnLoad and native Windows dialogs/buttons were used. The late-conflict fixture replaces only the private close-disconnect delegate with a wrapper that acquires a valid synthetic receive lease and calls the original production callback. The actual manager's `ChannelLifecyclePolicy.RequireUnowned` refuses the disconnect and emits real Pending/Failed evidence. It does not fake a driver exception or manually inject the failure row. No provider/API/device/channel is opened. The second fixture holds the receive gate before ordinary close, exercising the actual parent guard.

Synthetic receive ownership has no actual worker thread or provider behind it; this establishes application handling of ownership refusal and recovery, **not a physical driver timeout, failed USB disconnect or stuck hardware reader**. The screenshot's status bar remains Disconnected because no physical connection was initialized. Synthetic Begin sets report SessionState=Running but starts no acquisition. Module-name checks exclude known vendor names; they are not a universal publisher audit.

The Yes/Save path reports the manager refusal through the existing **Export** error dialog:
“Session export failed: Cannot close or reconnect J2534 channel while R28_REFUSAL_FIXTURE owns the receive channel.”

The earlier pre-close reader guard reports **Close incomplete**, explicitly saying the application and report remain open and naming `R28_PENDING_READER_FIXTURE`. Both actual messages were inspected. Retained connection controls remained locked after disconnect refusal. Manual export did not re-run disconnect: refusal attempt count remained one. The original close callback was restored and the gate released before recovery.

No product defect requiring a code change was established. No product rebuild, full suite, normal-dialog suite, historical control, owner screenshot or physical test repeated. No provider registration, RLink/vehicle connection, diagnostic transmission, DTC clear, actuation, programming, merge or sibling runtime change occurred.

## Exact evidence

Original artifact `15219915972`, `R28-exact-binary-failure-dialog-evidence`: **441,070 bytes**, SHA-256 `fc793af88538bd37b71adda60af94e3d897fa6070568b437ae81e3913d8996ee`. Exact size/hash and ZIP CRC passed; harness stderr empty.

| CSV | Data rows | SHA-256 |
| --- | ---: | --- |
| r28-refusal-manual-export.csv | 16 | fc6707de639f6f72c16074e531f8c15a12d66e22598cc57d856a4703b49d2a3e |
| r28-refusal-recovered.csv | 18 | f8261129ccdf51fecf6edeb439487f401e1a5df830f1f0b3b4ac2bef015fd9d5 |
| r28-reader-recovered.csv | 16 | 8fa7f92d913ad45c9d6d47cd5ed742578d376116647a7646de26e350612dbaa8 |

Each report has 16 columns, six State rows and six PidSummary rows, one SessionStart and one ExportMetadata, zero Observation rows. Manual export has two Disconnect rows: Pending then the exact Failed: InvalidOperationException ownership message. Refusal recovery retains those two, then adds only Pending/Succeeded; final metadata is Succeeded. Reader recovery has only Pending/Succeeded, proving no earlier disconnect was attempted by the reader guard. Every row carries exact R28 source and EXE identity.

Original target content remains `ORIGINAL BEFORE REFUSED DISCONNECT`, SHA-256 `edcff8a403eaca68bac687167e24e22e80ec562b7ac15a7a42b75ac58e692414`. No final export temporary files remained.

Unchanged EXE SHA-256 `632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160`; ZIP SHA-256 `8c59ceb5c14c7ceb2275c5abffb9b781fdfca9360a0a88d229de725870463b65`, 1,883,312 bytes. [Existing R28 package](https://chatgpt.com/library?file_id=libfile_20f867919ea48191b78304d0e3fc6288&account_id=personal) stays current. **No replacement download/install or owner test is needed.**

The test-only branch excludes inherited baseline auto-build and runs only the new scoped workflow; product owner branch remains feature/native-gui-capture-preview. PR #29 remains Draft/unmerged. Keep [R27 fallback](https://chatgpt.com/library?file_id=libfile_9c6d9759e9ac819199b187f83eac9a3e&account_id=personal). Bounded report, no autosave/crash recovery or reconstruction of evicted detail remain established limits. Need from owner: none for this completed plan.
