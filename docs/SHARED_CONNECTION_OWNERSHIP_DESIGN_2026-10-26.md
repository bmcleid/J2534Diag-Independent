# J2534Diag — Shared managed/native connection ownership plan (October 26, 2026)

**Status: IMPLEMENTED IN DRAFT PR #28; SYNTHETIC WINDOWS CI PASS; NOT LIVE-RLINK VERIFIED.** This workstream first consulted the current 2012 Cadillac SRX Shared Project Index, `SRX-PC-Scanner/main/docs/SRX_ACTIVE_WORK_REGISTRY.md`, the October 26 J2534Diag handoff, OneApp and T43 related branches. An ownership-implementation scope claim was saved and read back in the central registry before source edits. No new vehicle test is justified.

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
Previous candidate edits were not committed due to blocked tests. This branch now contains a new atomic cross-path connection lease, managed Initialize/InitializeCaptureOnly/Disconnect guards, native Start/Stop guards, and targeted new deterministic regression tests. Build and test results must be verified before promoting status; the existing GUI still does not invoke native Start. No physical RLink test or ECU operation. The native J2534 transport is not wired to the normal GUI. The cause of TOPDON RLink X3 physical 0x12 overflows remains UNVERIFIED. Do not use a physical Cadillac test as a substitute for the mandatory offline gate.


## Implementation notes and evidence distinction
A process-local `J2534ConnectionOwnership` atomically assigns an owner-instance token. `J2534Manager` acquires before both managed open paths, holds across reconnect, and releases on successful Disconnect after the VIN reader and all stored managed handles retire. Failures retain the lease when any handle/thread/reader remains, while safe unsuccessful startup with no resources releases it. Capture-only now tracks each managed handle immediately and does not blindly null fields if cleanup throws. `NativeOwnedCanSession` takes the SAME token gate before opening the device and releases only after Stop/Disconnect/Close retire all its handles; the managed-inactive callback remains an additional check, not the lock.

**Important limits:** A process-local lock does not coordinate other Windows programs or VCI firmware clients. There is no GUI native-mode selection or live TOPDON adapter test. Test cases exercise shared gate behavior via real native-session fake API and simulated managed token; Windows app compilation checks actual J2534Manager references, but actual managed driver operations are NOT executed offline. DLL-specific handle semantics/clean closure are not independently verified on the user's PC. Do not claim a corrected original 0x12 overflow.

**CI checkpoint: CONFIRMED** [Windows run 40410510093](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/40410510093) on code commit `f60b8c34e04710a395404928d89b7b88996c6fda`: 6,464 assertions across 64 standard offline regression tests PASSED, plus 29 isolated real-StdCall synthetic-DLL assertions PASSED, 0 compile errors; this is NOT TOPDON hardware validation. The GitHub-reported build ZIP artifact `12693850191` has SHA256 `d2675561ef73faf3a570d2cce2ccbe2b312007ea8355e65a72bd7cdb67864866`, not independently downloaded/rehashed. No diagnostic transmit, CAN bus test or programming.

## Remaining engineering gate — after verified CI
The process-local lock is integrated into managed `J2534Manager` and `NativeOwnedCanSession`; both compile. The 19 newly introduced counted assertions test fake managed ownership tokens and actual native-session fake API, including concurrent acquisition and failed native Close. There is **no direct offline invocation of `J2534Manager.Initialize()` against a real driver** and no dual GUI integration. Before enabling native selection, verify managed error recovery and UI disconnect/error messaging using a controlled device-free API adapter or agreed real TOPDON setup; check native driver identity and bitness on the user's PC. Do not infer that CI has fixed native J2534 `BUFFER_OVERFLOW` or that other programs are locked out.
