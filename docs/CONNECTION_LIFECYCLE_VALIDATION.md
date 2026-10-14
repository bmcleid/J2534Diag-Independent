# Connection lifecycle and RLink X3 verification matrix

Status: experimental software-only improvements on
feature/connection-lifecycle-guards. Inherits the capture-only connection
feature; no physical RLink X3 testing has been performed by this branch.

## Confirmed issues addressed in source

- Previously the J2534 manager could directly dispose Channel, Device,
  and API without independently checking its in-process receive-owner gate.
- The active VIN identification monitor could remain in flight during an
  attempted channel reset if a timed thread join expired.
- The Vehicle tab had no explicit Disconnect command and the selected
  vehicle connection banner did not clear on disconnection.

## New behavior

1. ChannelLifecyclePolicy.RequireUnowned refuses disconnect/reconnect while
   CAN_CAPTURE, FUEL_TRIM, or MISFIRE owns a receive lease.
2. ChannelLifecyclePolicy.RequestStopAndWait cancels a previous VIN monitor
   and waits for the background reader to terminate. An unresolved worker
   blocks channel disposal rather than allowing a concurrent replacement.
3. Active VIN identification retains its three-second receive window,
   followed by explicit stop/join. This cleanup is placed in a finally
   block so a failed VIN request cannot quietly leave the worker alive.
4. The manager routes explicit Disconnect, mode switching and repeated
   channel initialization through the same shutdown conditions.
5. The Vehicle tab now exposes Disconnect. It refuses to close a channel
   while an active capture is still running or its disk writer is pending.
6. The connection banner distinguishes a capture-only connection (VIN
   intentionally not requested), an active connection without a VIN, and
   a disconnected interface.

## Software validation (not live hardware)

Offline tests verify exclusive receive ownership, cooperative VIN monitor
shutdown, a simulated blocked thread timeout and subsequent retry, the
unstarted monitor case, and source guards on reconnect/disconnect.
The Windows CI also builds the actual application and runs prior capture,
CSV, disk-write, and J2534 metadata tests.

## Future controlled RLink X3 hardware test (stationary only)

Prerequisite: Read docs/RLINK_X3_CAPTURE_ONLY_VALIDATION.md. Shut down
Techline Connect, SPS2/GDS2 and other programs using the interface; never
share the hardware during programming. Do not assume CAN is electrically
silent: ACK of frames may still occur.

| Step | Conditions/action | Accept/reject result |
| --- | --- | --- |
| 1 | Windows, driver installed; app starts with no RLink selected | Device enumeration and UI work; no fake VIN shown |
| 2 | Capture-only selected; 11-bit CAN at 500 kbps; Connect | No application OBD/UDS request; VIN not queried |
| 3 | Open CAN Listener; start short supervised disk capture | CSV saved; no unreported writer failure |
| 4 | Click Stop, wait for CSV and integrity sidecar | Capture finishes before changing connection; check for writer errors and missing rows |
| 5 | Return to Vehicle and click Disconnect | Closes the idle channel safely; connection banner clears |
| 6 | If Stop reports a blocked reader or disk writer, do not force disconnect | Receive lease must remain reserved; retry Stop only after the blocker is resolved |
| 7 | Reconnect capture-only; short capture; Stop; Disconnect | Filters/resources can be reacquired; no stuck busy handle |
| 8 | View fuel-trim and misfire tabs while capture-only active | No application diagnostics sent; pages refuse polling |
| 9 | Record full native timestamp and RxStatus values | Preserve raw values; do not infer clock units/flag meaning |

The application's current tab-switch handler attempts to stop capture when
leaving CAN Listener. Therefore selecting the Vehicle tab is NOT a reliable
way to test a simultaneous disconnect attempt: capture may already be stopped.
The blocked-disconnect behavior is verified by isolated lease-policy tests,
and the live test should first verify clean Stop then Disconnect.

Do NOT exercise unverified enhanced TCM requests, DTC clears, routines,
actuator commands, programming or security access during this validation.
Do not start active diagnostics mode as part of the initial hardware smoke test.

## Evidence after the session

Retain exact Windows build/driver version, selected J2534 device and mode,
the CSV plus integrity sidecar, error messages, idle bus conditions, whether
the UI accurately showed Connect and Disconnect state, and whether the
interface reconnected without a restart.

A clean application-side report is not proof of lossless CAN traffic.
Actual driver/vehicle behavior remains UNVERIFIED until measured.
