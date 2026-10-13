# J2534 channel and CAN capture reliability — verification record

Branch: `feature/can-capture-reliability` (based on `feature/can-capture-v2`).
Status: code under Windows CI and offline tests; RLink X3 hardware behavior UNVERIFIED.

## Source-confirmed initial defects

- The original CAN listener had one thread collecting messages and a second
  thread processing a pending queue. Stopping cancelled both, leaving the
  potential for accepted but unprocessed queued frames.
- The listener could call `StopListening()` from its own receive thread on
  reaching 10,000 frames, where Stop attempted to join that same thread.
- Start/Stop performed `ClearMsgFilters`, `ClearTxBuffer`, and
  `ClearRxBuffer` on the shared J2534 channel.
- Fuel-trim and misfire forms both use the same `Channel.GetMessages`; the
  original code did not coordinate receive ownership.
- The misfire form started polling on Load even when not selected.

## Changes implemented in this branch

1. Exclusive `ReceiveChannelGate` for **in-process** listeners. All three
   read-consuming screens request a lease before starting.
2. CAN capture now processes and stores each received frame on a **single
   receive thread**, with asynchronous UI display refresh. There is no
   second application processing queue to abandon when stopping.
3. The capture reader stops on a bounded 10,000 accepted frames and requests
   finalization from the UI thread; it does not self-join.
4. The capture starts a pass-all filter and stops only its own filter by ID.
   CAN capture stop no longer clears the shared transmit/receive buffers.
5. Capture stop joins its worker before releasing channel ownership, allows
   export only when stopped, and warns of limit reached, driver exception,
   or malformed frames.
6. Fuel-trim and misfire workers also retain ownership until both polling
   and receiving threads have stopped. Misfire no longer auto-starts on Load.
7. Main window stops readers before attempting to dispose the J2534 manager
   and cancels exit if receive ownership remains active.

## Limitations and verification still necessary

- The gate coordinates **this process only**. It cannot prevent GM Techline,
  another J2534 application, or another operating-system process from using
  the interface. Never use parallel tools during the controlled test.
- Capture is bounded to 10,000 **accepted and processed** frames, not a
  continuous lossless recorder. Messages held in J2534 driver buffers
  when stopped are not guaranteed to be captured.
- Host `ReceivedAtUtc` is **not** a hardware CAN-bus timestamp. The
  J2534 message timestamp, status flags, device overflow counters, and
  channel diagnostics remain to be separately preserved.
- CAN filter starting/stopping has not been tested against the installed
  TOPDON RLink X3 driver. Actual J2534 adapter behavior may vary.
- Fuel-trim and misfire still call `ClearMsgFilters` while they are sole
  in-process channel owner; an eventual centrally owned transport should
  replace this pattern.
- Device-level read timeout and cancellation responsiveness are unverified.
  If stop times out, lease remains reserved; do not force another reader.
- No driver/vehicle tests, continuous heavy-load captures, or
  physical T43 identification have been completed.

## Release gate

1. CI builds the Windows application and all offline CSV/gate tests.
2. Validate start/stop, capacity stop, filter lifecycle, tab switching,
   cancellation, reconnect, and multi-minute capture with a fake transport.
3. Validate actual RLink X3 driver in a controlled Windows environment.
4. Only then describe CAN capture as hardware-verified; no GM enhanced
   identifiers or ECU commands should be inferred from a build pass.
