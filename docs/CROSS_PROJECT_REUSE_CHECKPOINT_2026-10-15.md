# Cross-subproject evidence and reuse checkpoint — 2026-10-15

Scope: **Independent J2534Diag Windows scanner**. This document was
prepared after checking live GitHub branches and evidence from the other
2012 Cadillac SRX sub-projects. It is a handoff, not an automatic merge or
a claim that branches synchronize themselves.

## Sources checked and their ownership

| Development lane | Canonical source | Confirmed value | Do not duplicate |
| --- | --- | --- | --- |
| SRX native .NET logger | [SRX-PC-Scanner / sandbox/j2534-regression](https://github.com/bmcleid/SRX-PC-Scanner/blob/sandbox/j2534-regression/docs/REAL-SRX-CAN-CAPTURE-2026-10-12.md) | Actual RLink X3 CAN RX from 2012 SRX, 25,791 accepted standard 11-bit frames and 97 IDs in 10.126089 s | Raw vehicle-bus proof, six-column native capture, per-batch and reject sidecars, source-specific forensic tooling |
| Independent six-column audit | [original Oct 12 audit](https://github.com/bmcleid/SRX-PC-Scanner/blob/feature/rlink-original-capture-audit-20261014/docs/SRX_RLINK_CAN_AUDIT_VALIDATED_2026-10-14.md) | Original raw CSV SHA256 matches; two gaps >100 ms; nine software-rejected native records unavailable | Repeat decoding/timing audits of the same original file |
| T43 research | [T43 cross-track checkpoint](https://github.com/bmcleid/SRX-PC-Scanner/blob/feature/t43-readonly-research/docs/T43_CROSS_PROJECT_REUSE_2026-10-15.md) | Offline ISO-TP/GM response tooling, conservative T43 candidate transport | Another generic ISO-TP reassembler or speculative enhanced DID scanner |
| Python RLink alternative | [SRX-PC-Scanner/read-only-tcm-probe](https://github.com/bmcleid/SRX-PC-Scanner/blob/feature/read-only-tcm-probe/docs/python-rlink-hardware-validation-2026-10-15.md) | DLL load/open/version/close on real RLink; vehicle CAN channel with Python remains unverified | A second Python driver probe without a new question |
| Portable Android scanner | [SRX-Data-Logger/portable-diagnostics-sandbox](https://github.com/bmcleid/SRX-Data-Logger/tree/codex/portable-diagnostics-sandbox) | Vgate iCar2 BLE / ELM command log, portable CSV, mode-specific polling | Bluetooth stack, ELM AT-command queue, device resume/reconnect handling |
| OEM interface | Techline Connect and GM-specific evidence | Previous SPS2 EBCM actions separate from scanner development; GDS2 access is not established as available/paid | Treating SPS2 programming as TCM live-data proof |
| This J2534Diag fork | [J2534Diag-Independent](https://github.com/bmcleid/J2534Diag-Independent) | Windows build, independent capture-only Connect/Disconnect, USB idle read with 2,385 BUFFER_EMPTY responses and no errors, eight-column CAN CSV + integrity sidecar | Do not pretend USB idle reads prove successful physical SRX bus capture by THIS application |

## Hardware identifiers preserved from the vehicle project

- TOPDON RLink X3 installed x64 J2534 DLL:
  `C:\Program Files (x86)\TOPDON\ArtiDiagVci\PassThru464.dll`.
- x86 DLL: `PassThru432.dll`; former spelling `PassThru64.dll`
  was invalid for the actual installation.
- J2534 API `04.04`; manufacturer driver library:
  `Nov  8 2025 V1.17`; interface firmware:
  `Rlink X3 VciV4.1 May 15 2026 V1.04`.
- The Oct 12 capture used *normal* CAN with no application diagnostic
  writes. CAN adapter electrical ACK behavior is not established as silent.
- Original frame capture:
  `SRX-CAN-20261012-175140-993.csv` SHA-256
  `0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B`.
  All saved receive statuses were `0x00000000`; no 0x7E8–0x7EF
  passive responses were observed. This does not imply that the TCM is absent.
- That logger's acquisition manifest contains **nine software-rejected
  native J2534 records** without retained contents/reasons. Their
  classification cannot be recovered retrospectively.

## Vehicle-specific boundary

VIN: `3GYFNEE31CS519173` (2012 SRX Performance, AWD, LFX 3.6).
Recurring observed DTCs: `P0877-00`, `P0989` (earlier `P0872`).
Car Scanner reported TCM calibration `24261871` and OBD response
identifier `0x7EA`. Neither is proof of proprietary T43 request
address `0x7E2`, enhanced DIDs, pressure-switch bitfields or the
cause of the recurring transmission faults.

Do **not** add blind DID scans, seed/key, actuator control, reset,
DTC clears, software download/programming or a replacement recommendation
to an observational capture tool.

## Source and schema distinctions

- Native SRX .NET capture is six columns:
  `timestamp_us,can_id_hex,extended,rx_status_hex,dlc,data_hex`.
  Its timestamps are labeled microseconds and must not silently be
  reinterpreted as host UTC milliseconds.
- The independent J2534Diag enhanced CSV is eight columns:
  `Index,ReceivedAtUtc,ArbId,ArbId Description,Data,J2534TimestampRaw,J2534RxStatusHex,J2534ReadResult`.
  `Index` is **record ordinal**, not elapsed time; `ReceivedAtUtc`
  is host time; native raw timestamp clock origin is unverified here.
- T43 branch's `tools/j2534diag_csv_import.py` was originally written
  for the three-column upstream J2534Diag GUI export. It can read
  original field names but discards later host/native timing and status
  columns; its documentation saying all J2534Diag CSV exports have
  **no timestamp** and may be GUI-filtered does not apply to our
  disk-backed eight-column fork. Review/upgrade that import only in
  the owning T43 lane, preserving raw values and source provenance.
- Android/ELM command/response CSVs are *not* equivalent to raw J2534
  bus frames or capture samples; do not directly replay as CAN traffic.
- J2534-Sharp `SAE.J2534.Message` exposes `Data`, `Timestamp`,
  `RxStatus`; it does not expose every field of the original native
  J2534 `PASSTHRU_MSG` (e.g. protocol ID/data-size metadata), so the
  independent listener cannot implement all native classifier conditions.

## Development decision in this branch

A **narrow software safety correction**, not duplicate analysis:

1. The existing J2534Diag pump accepted *any* Data length >=4, so
   J2534 `TX_MSG_TYPE`, `START_OF_MESSAGE`, `RX_BREAK` and
   `TX_INDICATION` could be mislabelled as received bus frames.
2. Cross-reference the existing native .NET classifier and the pinned
   J2534-Sharp `RxFlag` definitions; separately count skipped
   indication/control records, short/missing CAN messages, invalid
   11-/29-bit arbitration IDs and >8-byte non-classical payloads.
3. Write those counts into J2534Diag's own integrity sidecar. An
   ignored indication is **not** a malformed CAN frame or proof of
   bus traffic loss. The original nine unknown rejected records stay unknown.
4. Test with synthetic frames and actual pinned J2534-Sharp managed
   message objects. Do not overwrite the other native logger's audit.
5. Keep a draft PR and do not declare actual J2534Diag-on-SRX frame
   reception without a file from this application. **But do not ask
   the user to repeat a generic RLink physical-CAN proof already
   completed in the other lane solely for that purpose.**

## Cross-chat update discipline

At the beginning of consequential new work: read
[central SRX scanner coordination](https://github.com/bmcleid/SRX-PC-Scanner/blob/main/docs/SRX-SCANNER-CROSS-TRACK-COORDINATION.md)
and inspect branches, active PRs, tests and new vehicle observations.
After meaningful findings, update this handoff and the owning
coordination record. Separate chats and branches do not receive
automatic commits, and no monitoring occurs between user turns unless
an explicit scheduled task is configured.

**Evidence states:** CONFIRMED = actual test/source; LIKELY/POSSIBLE =
pending application-specific validation; UNVERIFIED = no hardware or
source corroboration. This fork's current classifier CI status must be
checked from its Actions run; a committed test alone is not a pass.
