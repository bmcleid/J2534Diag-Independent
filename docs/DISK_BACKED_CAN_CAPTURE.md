# Disk-backed CAN capture — development record

Status: EXPERIMENTAL, branch feature/disk-backed-can-logging, based on
feature/j2534-native-rx-metadata. The original master branch is unchanged.

## Windows UI instructions

1. After connecting a J2534 device, open CAN Listener.
2. Check **Disk log** (near the Filter field), then click **Start**.
3. Choose a new CSV filename. FileMode.CreateNew rejects pre-existing paths.
4. Stop when finished, and wait for the writer to flush and close.
5. Inspect both the CSV file and its adjacent .csv.integrity.txt file.
6. The separate Export CSV button saves only the on-screen preview after a
   disk session, not the entire captured session. A warning makes this clear.

Do not connect this experimental build to a live vehicle until controlled
RLink X3 hardware testing has established safe configuration and behavior.

## Implementation

- CanDiskCaptureLogger uses a dedicated CSV writer thread and a bounded
  4,096-record queue. Enqueue is nonblocking on the receive thread.
- The disk writer emits rows as they arrive and flushes every 128 rows,
  and again when normally stopped.
- The UI keeps a rolling preview of about 10,000, transiently up to
  10,500 records, while the disk file receives the full accepted stream.
- In disk mode, the pump limit is Int32.MaxValue rather than the original
  10,000-frame memory-only limit. This is still FINITE.
- A full queue or writer rejection stops capture with an error and records
  evidence of lost data rather than silently continuing.
- The stop path waits for the receiver and then the disk writer. A writer
  that fails to close within 15 seconds leaves the session reserved for
  Stop retry; a completed file is NOT claimed.
- All existing CSV fields including host UTC time and raw J2534 timestamp,
  RxStatus and read result remain intact. Older CSVs remain importable.

## Integrity sidecar

Every normally finalized disk session attempts to create a new
capture.csv.integrity.txt containing:

- FramesProcessedByPump
- FramesQueuedForDisk
- RowsWrittenToWriter
- QueueFullDrops
- RejectedAfterWriterFailure
- AcceptedNotWritten
- InvalidReceivedFrames and SourceOverReturnedFrames
- ReceiveError and DiskWriterError
- DiskWriterClosed, CaptureLimitReached, ApplicationWriteComplete
- VehicleBusLossless=UNVERIFIED
- TimestampClock=J2534NativeUnverified;HostUTCSeparate

ApplicationWriteComplete=true refers only to the application's measured
queue/write/flush handling. It does not prove J2534 driver completeness,
electrical CAN bus completeness, hardware-storage persistence across power
loss, or physical timestamp calibration.

If the program crashes, the integrity sidecar may not exist. Its absence
means unknown session integrity, never a successful capture.

## Offline regression tests

The Windows test suite covers:
- thousands of rows streamed to a real temporary file and read back;
- forced bounded-queue overflow from an intentionally blocked writer;
- simulated disk-full and final-flush exceptions;
- writer stop timeout, retry and no-overwrite protection;
- integrity-report failure labeling;
- partial capture counters retained across driver failures;
- backward-compatible CSV and pinned J2534-Sharp projection tests.

## Remaining verification gates

1. Actual TOPDON RLink X3 Windows driver test (no other J2534 app open).
2. Controlled message-flow, filter lifecycle, reconnect and error response.
3. Independently measured high-rate CAN logging and queue-pressure tests.
4. Device native timestamp timebase/rollover and status flags verification.
5. Device receive overflow and bus-level loss verification.
6. Verify whether the open CAN channel ACKs frames electrically; do not
   market this as guaranteed listen-only.
7. Hardware, recovery and licensing tests before distribution.

No enhanced T43 requests, ECU resets, clears, programming, actuation or
security functions are part of this capture feature. SRX-PC-Scanner remains
an independent, selectively integrated vehicle-specific project.
