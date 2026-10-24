# J2534Diag October 24 — offline capture-mode parity and native 0x12 evidence audit

## Confirmed vehicle data
Oct23 first independent J2534Diag capture: 95 accepted frames and one subsequent J2534 buffer overflow. Two Oct24 disk-log captures: first driver read reported overflow after 46.8294 ms and 42.6851 ms respectively, and J2534Diag wrote zero frames. The latter had channel-to-filter interval 261.3866374 s, filter install 1.103 ms and filter-ready-to-read 3.608 ms. Operator subsequently reports same overflow with minimal connect-to-start delay, but has not uploaded a matching sidecar for that attempt. A separate in-memory/Distinct Only J2534Diag recording reached normal 10,000-record cap with 97 IDs. This does not establish bus completeness.

Parallel SRX-PC-Scanner OneApp native v0.2.2 captured 25,772 accepted + 5 classified rejected messages in 258 batches, no 0x12 overflow, on the same vehicle in a **different implementation and session**. Do not equate conditions.

## Source-level comparison
- J2534Diag CanCapturePump uses GetMessages(200,5) in **both** modes. Disk disabled imposes a 10,000-record cap; Disk enabled uses int.MaxValue, installs a bounded asynchronous writer queue (4096 in GUI). These differences occur **outside** the first native read call. A zero-frame first-read 0x12 error precedes any CSV record handling.
- J2534Diag uses pinned J2534-Sharp v1.x; historical Channel.GetMessages calls PTReadMsgs and throws on all non-TIMEOUT/non-BUFFER_EMPTY errors before projecting native array data. Specific installed binary-source identity is not independently verified. The wrapper therefore may hide frames supplied *alongside* 0x12, and zero CSV records do not prove pNumMsgs returned zero.
- The independent OneApp uses direct native PassThruReadMsgs with request 100 and timeout up to 250ms. It validates returned counts and preserves eligible returned frames on 0x12 as INCOMPLETE before stopping. Different batch/timeout/wrapper are discriminating candidate variables; none alone proved root cause.
- SAE J2534-1 (2004 and 2015) says ERR_BUFFER_OVERFLOW indicates receive loss, **and pNumMsgs can reflect received valid frames**. SAE 2015 section 7.3.10 also says physical CAN channels block received messages until a filter is installed by default. That weakens the earlier unverified assumption that a 261s pre-filter wait necessarily fills driver buffers. Do not assert TOPDON complies without direct evidence. A requested 5ms timeout is not proof of a 5ms total host-call ceiling.

## New synthetic tests
Added to existing CanDiskCaptureLoggerTests (no production behavior change):
1. A single first-batch source runs in 10k memory mode, uncapped memory mode, and uncapped disk mode, with both 2-frame TIMEOUT outcome and a first-call 0x12 overflow. Asserts **every** initial request uses 200/5 and verifies identical accepted counts/overflow classification; disk counts remain separate.
2. A blocked asynchronous disk header writer cannot block a fake first read or nonblocking queue enqueue. This only verifies managed code semantics, **not** the real RLink DLL, Windows scheduling or OneDrive effects.

## Current working conclusion and gate
CONFIRMED source: cap and per-record disk callbacks cannot explain a 0-row first-read overflow. LIKELY J2534-Sharp's exception path loses visibility of native returned count and any partial messages on overflow. UNVERIFIED actual TOPDON count and internal buffer state. Do not repeat vehicle tests or change settings blindly. Next engineering target: design checked native returned-count/0x12 observation behind a read-only, source-verified interop boundary without disturbing other owners of the active channel; test synthetic buffer layouts, status precedence, cancellation, safety and cleanup first.

References:
https://www.normsplash.com/Samples/SAE/114214935/SAE-J-2534-1-2015-en-2.pdf
https://github.com/anctech/J2534-Sharp/blob/master/Src/Channel.cs
https://github.com/bmcleid/SRX-PC-Scanner/blob/feature/srx-oneapp/src/SrxPcScanner.Probe/Program.cs
