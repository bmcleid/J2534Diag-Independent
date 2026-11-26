# R27 release validation — November 25, 2026

Independent J2534Diag Windows; owner branch feature/native-gui-capture-preview, draftPR29 unmerged. Allfive requested review/fix/verify/deliver/synchronize steps completed. Source commit6d8b7541d91f687e28d24cfa91d8f7e4ed7d12db.

## Confirmed behavior and review

R26's close decision only tested ObservationsSinceExport. Disconnect/error events or counter-only changes after export could remain unsaved without a prompt. R27 tracks report revision alongside the observation watermark. A snapshot includes rows/counters and its revision under the existing lock. Only successful atomic publication acknowledges it. Later changes remain dirty. The health line adds Unsaved report: Yes/No; the close guard protects all report changes. Cancel and failed/cancelled save retain memory, No explicitly discards, Yes exports. Empty untouched report, zero-count updates and unchanged status stay clean.

Reviewed CAN Listener stop/close, bounded disk writer and native session cleanup. Existing joins/retained ownership on pending shutdown, finalized integrity recovery and retry controls already cover those boundaries; no additional transport defect was established. Report change loss was the selected confirmed gap. No sibling-runtime change.

## Verification

Windows [45732398202](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45732398202), job139617345802: SUCCESS first run; no retry. New R27 runtime used its first normal Windows validation, not a second R26 build.

- 6774 total regression assertions, including92report and86actualform checks.
- 28VIN parser/manager checks;5historicalR16 reproduction checks.
- 29real-DLL StdCall native synthetic checks.
- Managed virtual profiles:historical retained-resource reproduction,7silent,7partial,35normal,19broken-pipe checks.
- Focused new tests cover no-response/event-only sessions, Disconnect after export with zero new observations, counter-only changes, failed atomic save/retry, Cancel/No/Yes and no-repeat prompt. Concurrent observation tests also require the revision to remain dirty after a snapshot.
- Application compile4existing warnings/0errors; test compile existing unreachable-code warning. Virtual native build0warnings/0errors; host2existing timer-nullability warnings/0errors.
- Local source/readback/brace review completed; no local C# compiler was installed. Executable verification took place on Windows.
- R27 narrow/wide and font-scaled form checks passed; narrow layout image inspected with Unsaved report: No visible.
- Prior exact-R26 native-dialog/keyboard plan remains complete; it was not rerun. R27 close-policy tests inject choices and save delegates; no claim of a new R27 native-dialog automation run.

Runtime artifact15077157137:1882367bytes; ZIP SHA256e047682e62d81ab01fdf75f85f636bfdc51e73edfda76f649718c919e8ac32ca. EXE SHA256cf5d699d92e7f1d8969b6f1b873c9169b906765eb0aff85222fe218b541755e9.
Diagnostics artifact15077840576:400965bytes; SHA2562371a5b5a276447182b3cdfbd8a26fdda1d919b5961bc0e3db891c0576038961.
Downloaded size/hash/ZIP CRC, all7manifest-listed runtime hashes and embeddedR27/source identity verified. Normal CSV111data rows/10observations includes DisconnectPending/Succeeded; broken-pipe CSV41/6 includes Pending/Failed retained-ownership detail. Both have16columns and exactR27source/EXEprovenance on all data rows. Synthetic test data only; observation counts can vary with scheduling.

## Use

Extract J2534Diag-R27-Windows.zip into a separate R27 folder, keeping the R26 folder. Launch J2534Diag.exe with its accompanying files. Title identifies R27/6d8b7541d91f. Save an existing session with Export session. Unsaved report can show Yes even with0newobservations because events/counters changed. Before closing:Yes saves,No discards,Cancel keepsopen. A failed/cancelled save keepsopen. No new vehicle test is required for this delivery.

## Evidence boundaries

No physical adapter/vehicle, provider registration, programming, DTC clear or branch merge. Managed virtual shim loaded only within explicitly gated CI. R21physical/R22wideGUI and R26actual-dialog evidence remain preserved. R26 package remains unchanged fallback. R27 owner-PC/physical behavior UNVERIFIED.

Bounded report remains limited to retained accepted observations/events/counters; no autosave/crash recovery or reconstruction of evicted detail. App-close export still occurs before final manager disposal, so subsequent disposal events cannot be in the already-saved file; export after an explicit disconnect to retain that outcome. This repair protects changes already present at the close decision; it does not claim persistence after process termination.


## November 25, 2026 — J2534Diag R27 report-change preservation completed

CONFIRMED software: source6d8b7541d91f687e28d24cfa91d8f7e4ed7d12db, ownerfeature/native-gui-capture-preview, draftPR29unmerged. R26guardonlycheckednewobservations; Disconnect/error/counter-only evidence afterexportcouldclosewithoutprompt. R27capturesreportrevisionwithsnapshot,marksrows/nonzerocounterupdatesdirty,acknowledgesonlyafteratomicpublicationandprotectsallreportchangesonclose. HealthaddsUnsavedreportYes/No;observationcountunchanged. Emptyuntouched/zerocount/noopstatusstayclean;Cancel/failedsavekeepopen,Noexplicitdiscard,Yesexport. Newtestscoverevent-onlyno-response/Disconnect/counterupdateswith0observations,failedsave/retryandclosechoices. ExistingCANjoin/retainedownership/integrityrecoveryguardsreviewed;noadditionaltransportdefectestablished.

[Windows45732398202](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/45732398202),job139617345802 SUCCESS firstR27run/noretry:6774regressionsincluding92report/86form/28VIN;5historicalR16,29realStdCallnative;managedhistoricaldefectreproduction,7silent/7partial/35normal/19brokenpipe. App4existingwarnings0errors;testsoneexistingunreachablewarning;virtualnative0/0,host2existingtimernullabilitywarnings0errors. Localstructural/source/readbackchecks,Windowscompile/execution. R26actualnative-dialogplanwasnotrepeated;R27close-policyformtestsinjectedchoices/save,notnewnative-dialogautomation. NoC#compilerlocal.

Runtime [J2534Diag-R27-Windows.zip](https://chatgpt.com/library?file_id=libfile_9c6d9759e9ac819199b187f83eac9a3e&account_id=personal),1882367bytes,ZIPe047682e62d81ab01fdf75f85f636bfdc51e73edfda76f649718c919e8ac32ca;EXEcf5d699d92e7f1d8969b6f1b873c9169b906765eb0aff85222fe218b541755e9. Originalartifact15077157137;diagnostics15077840576,400965bytes,SHA2371a5b5a276447182b3cdfbd8a26fdda1d919b5961bc0e3db891c0576038961. Downloadsize/hash/CRC/all7payloadhashes/embeddedR27sourceverified. NormalCSV111rows/10observations,DisconnectPending/Succeeded;brokenpipe41/6,Pending/Failedretainedownershipdetail;16columnsandexactR27source/EXEprovenancechecked. Syntheticcountsnotphysicalevidence. Narrowlayoutinspected;wide/narrow/font125checks passed.

Evidence [J2534Diag-R27-Validation-Evidence.zip](https://chatgpt.com/library?file_id=libfile_9e71393287188191b9f499c4975087ea&account_id=personal),483876bytes,SHA936bc85488c197191320bc0838c9d2b202c91369579ad992e6e1d1e3667c9b41,containsoriginaldiagnosticsZIP,fullhostedlog,focusedsource/tests/review/validation. Savereceipts/localidentityconfirmed. UsercanextractR27separatelyandkeepR26fallbackunchanged. Allfiveauthorizedstepscomplete;noownervehicle/screenshotrepeat.

No physicaladapter/vehicle/providerregistration/programming/DTCclear/merge/siblingruntimechange. R21physical,R22GUI,R26dialogresultsremainestablished;R27ownerPC/physicalUNVERIFIED. Boundedreport/noautosave/crashrecovery/evicted-detailreconstructionremain. Closeexportstillprecedesfinalmanagerdisposal;exportafterexplicitDisconnecttoretainthatoutcome. Repairprotectschangespresentatclosedecision,notpostterminationpersistence.
