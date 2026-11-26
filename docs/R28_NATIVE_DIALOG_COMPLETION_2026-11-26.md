# R28 exact-executable native-dialog acceptance — November 26, 2026 Eastern

**CONFIRMED: all twenty authorized steps complete for this offline plan.** [Windows run 45908418443](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45908418443), job `140210647329`, SUCCESS on the first scoped attempt. Harness `ca82eb1d011167f2fad40cce2e06608280ef7ffd`, branch `test/r28-close-dialog-ui-20261126`. Only the harness compiled. Product source `03d8a2a611953ff912f79a2e0d3af056b3194a1f`, existing runtime from run `45904637124`, remained unchanged.

This supersedes **pending R28 native-dialog acceptance** in earlier owner/shared records. It does not supersede physical vehicle limits or R27 fallback. Prior R27 6,774 regressions, historical R16/R27 controls, R26/R27 native-dialog suites and owner launch checks were reused, not rerun.

## Twenty-step completion map

| Step | Result / evidence |
| --- | --- |
| 1 Refresh owner/shared records and PR | Current owner R28 record, Shared Index v243, registry and coordination read; PR29 verified open/Draft at 4a884294f5ce0bcc7f8b222c994353cb2429e2a5 before harness work |
| 2 Preserve exact R28 identity | Manifest source/run and all seven payload hashes verified; compiled source constant and EXE before/after match |
| 3 Carry forward owner launch | Screenshot libfile_76a2af0b10408191b57a76523f89c6ff remains accepted; not repeated |
| 4 Preserve R27 fallback | libfile_9c6d9759e9ac819199b187f83eac9a3e unchanged |
| 5 Separate evidence scopes | Previous injected-policy acceptance distinguished from this actual-dialog acceptance; physical adapter behavior remains unverified |
| 6 Reuse dialog harness | Reused exact-R27 UIA/native-window lookup and proven filename keyboard/Tab-commit helpers; removed unrelated event/counter/scalar suite |
| 7 Focused no-rebuild gate | Exact downloaded R28 runtime; compile Tests/ui/R28Dialogs.csproj only; inherited baseline excluded on test branch to avoid duplicate product build |
| 8 Zero-provider gate | Both HKLM/HKCU and 32/64 views checked for PassThru04.04/05.00; inventory zero. Actual manager Devices empty; API/Device/Channel/native session null. Valid global logical lease only; no Initialize/Connect |
| 9 Untouched disconnected close | Production MainForm OnLoad, clean zero-data report closes without prompt |
| 10 Clean retained logical connection | Actual close prompt appears even with clean report; valid logical ownership lease, no provider handles |
| 11 Actual Cancel | Real Cancel button retains app and logical lease; LastDisconnectOutcome stays Not attempted |
| 12 Actual Save As cancellation | Real Yes opens Save As; actual Cancel retains lease and no disconnect |
| 13 Destination before disconnect | Lease/outcome inspected while native destination dialog open, before Save command |
| 14 Successful final CSV | Actual Save closes app; one Pending/Succeeded pair and final Succeeded metadata; zero observations |
| 15 Locked destination | Real overwrite confirmation followed by real sharing-violation Export dialog; underlying disconnect already Succeeded |
| 16 Retained report and UI refresh | Original target unchanged, report dirty/in memory, app open; Disconnect disabled, Connect text restored, receiver/capture/device controls restored. Connect stays disabled with zero providers, as required by inventory |
| 17 Retry without another disconnect | Unlock, actual close prompt/Save As/overwrite; successful CSV has exactly one Pending/Succeeded pair; app closes; no tmp leftovers |
| 18 Actual No | Retained logical connection disconnects and closes; prior export hash unchanged; no additional CSV |
| 19 Independent evidence review | Artifact size/SHA/CRC, both parsed CSVs, provenance/event rows and screenshots/UI trees independently checked |
| 20 Preserve and synchronize | Original artifact, full log, harness/project/workflow and independent analysis preserved; owner/shared records updated with readback |

## Exact results and limits

Output reports **1,471 checkpoints**, including repeated loaded-module filename checks, **not 1,471 distinct functional scenarios**. Four production-window lifecycles exercised clean close, retained Cancel/destination cancellation/success, locked-target failure/retry, and No. Real dialogs/buttons were used; filename entry used real keyboard text and Tab commit. Logical ownership is simulated, provider discovery and native forms are production paths. Module lists reject known vendor-name patterns, not a universal publisher audit. Nothing connects to an adapter/vehicle, polls, transmits, clears codes, actuates or programs.

Real failed-save dialog reads: “Session export failed: The process cannot access the file because it is being used by another process.” Screenshot and UI tree inspected. Vehicle controls screenshot inspected. Fuel report SessionState=Running is set by synthetic Begin; it does not indicate a live provider/session. Zero observations remained throughout.

Original artifact `15158901731`, `R28-exact-binary-dialog-evidence`: **677,594 bytes**, SHA-256 `60a634502d3d6c5c9b3797241350bb4f3762c481321971ace124f3e034320c51`; exact size/hash and ZIP CRC passed.

Both files have 16 columns and 16 data rows: one ExportMetadata, one SessionStart, six State rows, exactly two Disconnect rows (Pending then Succeeded), and six PID summaries; zero Observation rows. Metadata DisconnectOutcome=Succeeded. Every row carries exact R28 source and EXE hash.
- `r28-close-success.csv` SHA-256 `68769382d75d41a13c9cf7bb73d14419670cbf92523e699d11badb1a638581e1`.
- `r28-close-retry.csv` SHA-256 `6e076a13e29e0d0f843b65121f09b1cd1ab05ee1ee761818baefe62d73e63d8c`.

Unchanged EXE SHA-256 `632714d1350ddae4b2c64756231d37babfc3ddec3dfce88506f6d1083faf9160`. Delivered ZIP SHA-256 `8c59ceb5c14c7ceb2275c5abffb9b781fdfca9360a0a88d229de725870463b65`, 1,883,312 bytes. [Existing R28 package](https://chatgpt.com/library?file_id=libfile_20f867919ea48191b78304d0e3fc6288&account_id=personal) stays current; **no replacement download/install or product rebuild needed**. R28 now has focused offline native-dialog acceptance plus owner-PC visual launch; physical acceptance remains unverified.

Owner branch remains feature/native-gui-capture-preview; harness-only test branch is separate. PR #29 stays Draft/unmerged; no merge or ready-for-review action. No sibling runtime change. Bounded report/no autosave/crash recovery/evicted-detail recovery limits remain. Need from owner: none for this completed plan.
