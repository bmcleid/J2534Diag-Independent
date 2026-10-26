# J2534Diag — Shared managed/native connection ownership plan (October 26, 2026)

**Status: DESIGN ONLY — NOT IMPLEMENTED, NOT COMPILED, NOT TESTED.** This workstream first consulted the current 2012 Cadillac SRX Shared Project Index, `SRX-PC-Scanner/main/docs/SRX_ACTIVE_WORK_REGISTRY.md`, the October 26 J2534Diag handoff, OneApp and T43 related branches. An ownership-implementation scope claim was saved and read back in the central registry before source edits. No new vehicle test is justified.

## Specific defect to address
Existing managed `J2534Manager` owns J2534-Sharp channel lifecycle; `ReceiveChannelGate` only coordinates readers on an already-open managed channel. Existing `NativeOwnedCanSession` separately arbitrates native sessions, and checks a managed-inactive callback, but that callback is not atomically coupled with managed connection acquisition. Therefore a time-of-check-to-use race could allow overlapping device/session opens if the new native receiver were wired into the GUI now.

## Planned fix
A single process-local atomic device-connection ownership lease must be acquired before *either* implementation opens its channel, remain held while any channel/API/device handle, VIN monitor or reader remains active, and be released only after confirmed safe closure. Reconnect must retain the lease during transition. Failed native StopFilter/Disconnect/Close or managed Channel/Device/Api Dispose must not release ownership prematurely. The existing ReceiveChannelGate remains a separate reader-level control; the connection lock is not an interprocess mutex. Unconnected GUI native receiver must remain inactive until tested.

## Required distinct offline validations before shipping
- Exactly one of two concurrently attempting managed/native session owners can acquire; losing session performs zero native calls.
- Deny native when managed owns; deny managed when native owns; after clean close other can connect.
- Managed VIN monitor cancellation/join and reconnect cannot create an unowned gap; failed manager initial open without active handles permits safe release.
- Driver cleanup failures retain lease if any open handles remain; retry succeeds without repeating completed cleanup.
- Invalid lease release is rejected; existing capture-only, nontransmit and J2534 0x12 incomplete-capture semantics preserved.
- Use existing fake/native mock tests and one justified Windows CI run after a complete code/test change; no duplicate OneApp/J2534 captures.

## Disposition
Candidate source edits were investigated but not committed because the attempt to create required regression tests could not be completed. No executable ownership code is in this new branch, no GitHub Actions run was triggered, and draft PR #27 is unchanged. The native J2534 transport is not wired to the normal GUI. The cause of TOPDON RLink X3 physical 0x12 overflows remains UNVERIFIED. Do not use a physical Cadillac test as a substitute for the mandatory offline gate.
