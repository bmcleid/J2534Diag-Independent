# R23 twelve-step validation

Owner: bmcleid/J2534Diag-Independent, feature/native-gui-capture-preview, draft PR29; independent Windows J2534Diag. No sibling scanner implementation or merge.

1. Refreshed Shared Index, canonical registry, cross-track ownership, handoff, CI gate and open stacked PRs. R21 physical acceptance and R22 offline full-width acceptance remain closed; no repeated vehicle test.
2. Audited scalar callbacks, queue acceptance, display state and export. CSV remains bounded app-level evidence, not raw CAN or bus-losslessness proof.
3. Added forward/backward UTC-jump tests; freshness remains monotonic. Retained rows preserve event order.
4. Added explicit eligible/ignored/coalesced/capacity/inactive/unavailable/decode/report counters. Retired callbacks are rejected before poll acknowledgement.
5. Added six per-PID accepted/stale-transition summaries surviving row eviction and session restarts.
6. Recorded source-selection changes.
7. Recorded Clear Charts without deleting accepted observations or sending an ECU command.
8. Added visible report health and accepted/coalesced/discarded totals.
9. Distinguished unanswered requests, disabled PIDs, cancellation, driver failure and pending cleanup; stop does not hide the fault cause.
10. Tested failed locked-target overwrite, original-file/buffer retention, temporary-file cleanup and successful retry.
11. Windows validation outcomes and exact artifact identities are recorded in the accompanying release validation record. First attempt compiled and passed focused export/VIN/form checks but failed an older literal-source guard assertion; corrected assertion plus behavioral cancellation test were submitted once.
12. Runtime, focused source, failed/successful diagnostics and verification metadata preserved; owner handoff, canonical registry/cross-track and Shared Index synchronized with readback in the delivery closeout.

No driver installation/registration, physical adapter, vehicle scan, DTC clear, programming or repository merge was performed. R23 physical RLink/vehicle behavior remains unverified.
