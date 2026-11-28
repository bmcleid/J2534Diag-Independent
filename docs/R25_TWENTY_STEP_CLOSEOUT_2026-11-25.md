# R25 — twenty-step session-continuity audit

Independent J2534Diag Windows workstream, bmcleid/J2534Diag-Independent/feature/native-gui-capture-preview, draft PR29. Exact validation and artifact identities are recorded in R25_RELEASE_VALIDATION_2026-11-25.md.

| Step | Work and evidence |
| --- | --- |
| 1 | Refreshed Shared Indexv191, main Registry/Cross-Track/CI gate, AGENTS and full owner handoff. |
| 2 | Recorded supplied R24 launch screenshot, existing Librarylibfile_cc9baa731c088191b2818de25d748dd2. |
| 3 | Preserved hash and visible R24/91305fe49ac0, capture-only/original receiver/disconnected state. No adapter or process-hash inference. |
| 4 | Branch unchanged atce572725569ac29da90c21cf0e2125779115e73f; stacked PR1–29 Draft/unmerged; no open non-PR issue returned. |
| 5 | Source audit prioritized one established managed reconnect evidence defect. |
| 6 | Existing managed tab/start/stop/reconnect scenarios retained; added failed/refused reconnect→lease retirement→successful disconnect retry. |
| 7 | Distinct synthetic VIN/mode/source/session context verified in report rows; existing virtual VIN bootstrap and reconnect retained. |
| 8 | Moved disconnect-outcome reset after safe old-connection retirement so refused reconnect preserves the old failure. New successful connection still resets outcome. |
| 9 | Report Begin and actual form restart clear live/source state while retaining prior report rows and lifetime totals; source-selection tests retained. |
| 10 | Tested enabling a PID for the next cycle and disabling all PIDs; enabled-set snapshots are refreshed between cycles. |
| 11 | Tested simultaneous reply/cancellation: cancellation wins and retired pending PID rejects acknowledgement. |
| 12 | Verified refused cleanup preserves outcome/VIN/lease and avoids driver opening; cleanup retry succeeds after synthetic receive-lease retirement. Broken-driver retained-ownership scenario retained. |
| 13 | Added60000-observation synthetic report fixture against20000rowcapacity, plus concurrent production. This is event-count stress, not a timed vehicle soak. |
| 14 | Checked bounded rows/eviction, snapshot PID totals and final lifetime total reconciliation. |
| 15 | Three exports run with a concurrent observation worker; row shape/count metadata and PID summary bounds checked, final totals reconciled. |
| 16 | Reviewed release/timestamp filename, CSV filter/default extension, normal overwrite prompt and failure message. Existing original-file/buffer/locked-save retry protections retained. |
| 17 | One runtime correction grouped with source identity and focused tests. No driver/transmit/lease behavior change intended. |
| 18 | XML/C# structural checks locally; executable model/form/native/managed checks in Windows. No local C# execution claimed. |
| 19 | One Windows validation, artifact/manifest/CSV checks and focused source/evidence preservation; details in release record. |
| 20 | Owner handoff, canonical Registry/Cross-Track and Shared Index synchronized with readback; runtime/evidence delivered. |

DisconnectOutcome export metadata is the manager's current connection outcome at export; retained rows/events can span earlier report sessions. Values preserve each row's VIN/mode/source/session context. App evidence is bounded and can precede pending UI acceptance; it is not a lossless raw CAN record.

No physical adapter/vehicle test, provider registration, DTC clear, programming, sibling runtime change or merge. R21 physical acceptance, R22 offline full-width acceptance and owner R23/R24 launch remain preserved. R25 physical behavior remains UNVERIFIED. No repeat screenshot or scan is required for delivery.
