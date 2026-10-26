# October 26 — J2534 native sole-owner lifecycle and Windows ABI gate

## Applicability / purpose
Target vehicle: 2012 Cadillac SRX Performance AWD 3.6L; TOPDON RLink X3. J2534Diag independently received 10,000 in-memory frames but Disk log suffered explicit native `BUFFER_OVERFLOW` before records were handed to the managed pump. Independent SRX-PC-Scanner OneApp used 100-count direct `PassThruReadMsgs` and recorded 25,772 accepted CAN messages on a separate October 24 capture. **No root cause proven; no change to physical vehicle or installed module.**

## Added code (NOT wired to GUI)
- `NativeOwnedCanSession`: physically inert until an explicit call to Start(true). Requires a managed-J2534-inactive callback and an atomic process-wide gate among native sessions. Owns a single device/channel/filter; fixed one `CAN` (protocol 5), flags zero, 500000 baud, standard wildcard PASS_FILTER from the injected API; no retries with alternate settings. On startup failure stops filter if installed, disconnects and closes remaining handles. On stop failures attempts every remaining cleanup stage, reports failures and retains native ownership while any handles remain potentially active. Retry attempts only unresolved cleanup. Explicit ReadNative requires an active filtered session. No transmit, ioctl buffer-clear, ECU request or programming functions.
- `NativeJ2534StdCallApi`: opt-in Windows native DLL adapter requiring an existing absolute .dll file path; loads ONLY seven functions: PassThruOpen, PassThruClose, PassThruConnect, PassThruDisconnect, PassThruStartMsgFilter, PassThruStopMsgFilter, PassThruReadMsgs, using J2534 StdCall, matches independent OneApp 24-byte header + 4128-byte inline data PASSTHRU_MSG. No PassThruWriteMsgs symbol loaded. Does not unload while known native device/channel remains active. **Its direct physical behavior has NOT been validated against TOPDON.**
- `NativeCanReadBoundary` from draft PR25 continues to validate `pNumMsgs` actual count / stale returned buffers and to preserve any valid 0x12 partial records while always marking driver overflow.

## Offline regression
- Consent refusal and managed-channel exclusivity before any native calls.
- Open, connect, filter, read, stop-filter, disconnect, close exact order and idempotent stop.
- Failure at Open, Connect or StartFilter triggers only safe cleanup stages.
- Two simultaneous native sessions cannot own the same process gate.
- Native Close failure retains lease; subsequent Stop retries only failed close.
- Filter stop failure does not suppress Disconnect/Close; remains an explicit failure.
- Win32 PASSTHRU_MSG layout 4152 bytes, six offsets 0–20, data offset 24, inline 4128; seven StdCall receive-only delegates, no write methods; rejecting non-absolute DLL path.
- NO test opens an actual TOPDON DLL, J2534 channel, RLink or vehicle.

## Remaining safety gates BEFORE hardware
1. Verify exact installed TOPDON DLL path and bitness against the actual user's PC, without guessing vendor variants.
2. Verify J2534-Sharp `J2534Manager` lifecycle is fully offline and coordinate native-vs-managed channel ownership through a single app-wide lock; current callback+process gate protects only own native sessions, **not a race with managed J2534-Sharp**.
3. Run external synthetic native DLL contract tests through real marshaling (status 0x12 nonzero count, partial 0x09, zero count, stale count, false success, startup/cleanup faults). Current tests use injected fake API and ABI reflection, not a native DLL.
4. No GUI integration or new user vehicle capture until independent verification of those gates. Receive-only CAN can still ACK on the bus.

**Unverified:** Why RLink X3 overflows in J2534Diag disk mode; actual hardware 0x12 returned count and any partial native payload; bitness/production DLL marshalling operation; exact prior capture completeness.

## PR / CI
Draft pull request feature/native-single-owner-receive-lifecycle; Windows CI must pass before labeling synthetic checks confirmed.
