# R26 actual-dialog validation — thirty-step checkpoint

Independent J2534Diag Windows workstream. R26 remains the delivered runtime; no application source or executable changed and no product rebuild/full regression suite was run in this task. Owner branch was b914ea309bbe48acf918307705614c6891a927bc before documentation. Scoped harness branch:test/r26-dialog-ui-20261125.

## Exact runtime and owner evidence

Runtime source:f0a43d1da720f0fb6261934939bdb1c571c44ef4; original Windows run45652987542; runtime Librarylibfile_7acfc8c14564819188e245f251a0ca74. ZIP SHA25673f01dde73f7099e6f2580a3ef883cd520a5f8bd67e52578ff99f5d2c6e6ef28; EXEe5ba2abf8ee1a2eedc04f088b08d90d44c6ad78b4dbb506e8d825afc95bc3eba. Verified locally and in scoped Windows before launch, along with all seven manifest-listed payload hashes.

Owner Screenshot2026-11-25 161328.png, Librarylibfile_14ce047973288191ab02723269e5e929, SHA25678a1f9b70d9723a6d959ced61631c7beae1654a20d461d914bf3174271b821de, confirms visibleR26/f0a43d1da720,capture-only,originalreceiver,Disconnected. Does not prove process hash or adapter connection. Original remains preserved; no repeat requested.

## Run accounting and exact boundary

- First scoped Windows run45704256174/job139521999345, harnessafb2c561ce0bf11bb62d1c1f41af4e1a3817a447:FAILURE—HARNESS modal lookup. Runtime preflight, empty registered-provider gate and harness compile passed. Standalone exact EXE launched/closed exit0; production MainForm from the same assembly loaded with no provider; synthetic gauge/checkbox/long-counter checks passed. Harness searched desktop UIA children and missed the actual Session report owned dialog, which the screenshot and UI tree show beneath the main window.175 checkpoints reached, mostly individual module-name checks; not175 independent functional scenarios.
- First artifact15063904259:394625bytes,SHA2560b4180dc62263f760772b44173319ecd971a4b75c208e45ba62daf38e46ae1f9; original ZIP/CRC/hash verified and preserved.
- Corrected native-owned-window lookup; second scoped run45704608954/job139523238795, harnessa20c849002786cdaa1124d99353a3d9e094ac8c9:FAILURE—HARNESS Save As lookup. Preflight/compile and earlier checks passed. Actual close dialog defaulted toYes; Escape kept app open and observations new; Enter opened the real Save As dialog; Escape cancelled it without acknowledging observations. A later Export-button invocation visibly opened Save As but native caption/PID lookup did not identify it. The failure screenshot and UIA tree show the actual dialog.181 checkpoints reached, including module checks. No product crash/failed dialog opening is established.
- Second artifact15063854685:564302bytes,SHA2561c09b7fa0cd14b4a8a7919bc5a3f13b60ddeac0572c404c2845909ce154093e0; original ZIP/CRC/hash verified and preserved.
- Prepared correctionf4e11c4b96e039c4987bb0e1738329cbaaa18007 [skip ci]:native caption/PID lookup plus owned UIA Window descendants fallback, with accessible focus fallback. Source/readback and structural checks passed. Windows execution UNVERIFIED; no third run started.

The authoritative CI policy docs/SRX-SUBPROJECT-COORDINATION.md rule5 says **“Stop after two same-step failures: preserve evidence, report the failure class and stop; wait for the owner before another attempt at that step.”** Both failures occurred in Exercise actual R26 dialogs and keyboard without provider. Further scoped Windows execution requires owner approval. This is the project CI gate, not an automatic approval-review rejection. The prepared action is one UI-only run against the same R26 EXE, with no product rebuild or vehicle access.

## Thirty-step status

| Step | Status/evidence |
| --- | --- |
| 1 | DONE:refreshed Shared Indexv213 and main Registry/Cross-Track/CI policy; fresh guarded synchronization follows. |
| 2 | DONE:complete owner handoff/AGENTS, unchanged owner head and stacked DraftPR1–29 inspected. |
| 3 | DONE:R26 owner screenshot recorded. |
| 4 | DONE:source/settings/disconnected state and original screenshot hash preserved. |
| 5 | DONE:owner source unchanged since tested R26; only existing docs after runtime source. |
| 6 | DONE:identified actual native-dialog testing gap remaining after prior injected-decision tests. |
| 7 | DONE:isolated Windows2022 job, both registry views/hives checked for J2534 registrations; none. |
| 8 | DONE:launched standalone preserved J2534Diag.exe; clean no-data close exited0. Dialog tests run production forms from same exact assembly with synthetic observations. |
| 9 | DONE:exact EXE/source/seven payload hashes verified before launch. |
| 10 | DONE:actual health labels and source identity visible. |
| 11 | DONE:actual checkbox toggled Disabled and re-enabled with fresh synthetic observations. |
| 12 | DONE:large synthetic counters fit within narrow720px window bounds; screenshot retained. Counters are intentionally synthesized for layout. |
| 13 | DONE:actual close prompt appeared; screenshot/UI tree retained. |
| 14 | DONE:Escape/Cancel kept app open and observations new. |
| 15 | DONE:defaultYes/Enter opened actual Save As. |
| 16 | DONE:Escape cancelled native Save As, app remained open and observations new. |
| 17 | PENDING:successful dialog-save CSV content verification not reached. |
| 18 | PENDING:no-repeat-prompt after successful actual dialog save not reached. |
| 19 | PENDING:new-observation prompt after actual dialog save not reached. |
| 20 | PENDING:actualNo close without export not reached. |
| 21 | PENDING:actual locked-target save error and successful retry not reached. Prior R26 model/form-delegate tests remain valid, not substitutes for these UI paths. |
| 22 | PARTIAL:defaultYes,Enter and Escape observed; Tab-to-No not reached. |
| 23 | PARTIAL:startup buttons/defaults and Disconnected UI verified; full post-save state checks not reached. |
| 24 | DONE for executed scope:no registered provider; actual manager Api/Device/Channel/native session null at startup; full module-path lists retained and named-vendor matching checks passed. No Connect/capture start or driver-open action invoked. Not a universal publisher audit of all loaded Windows components. |
| 25 | DONE:both original failed artifacts, screenshots,UI trees,logs and hashes preserved. No dialog-generated successful CSV exists yet. |
| 26 | PARTIAL:observed launch/gauge/Cancel/Yes/save-Cancel behavior matches instructions; remaining UI paths unverified. |
| 27 | PREPARED:harness-owned-dialog fallback correction; no app discrepancy requiring product change established. |
| 28 | BLOCKED:two scoped jobs failed the harness UI step; no third run without owner approval; no product rebuild/full suite. |
| 29 | DONE:review checkpoint preserved, not a new runtime release. |
| 30 | DONE:owner and shared records updated with this exact boundary; task not declared complete. |

No physical adapter/vehicle,DTC clear,programming,provider installation or merge. R26 prior6752regressions and native/managed software validation remain established; these two failed harness runs do not erase them. Actual remaining dialog completion is blocked by the explicit CI approval gate.
