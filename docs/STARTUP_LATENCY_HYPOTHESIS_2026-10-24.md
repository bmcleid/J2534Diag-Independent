# Oct 24 J2534Diag first-read overflow startup telemetry

## CONFIRMED vehicle-specific evidence
Oct 24 uploaded zero-frame CSV and integrity sidecar for CAN-session-20261024-141546.csv. Sidecar: pump 0, disk queue/writer 0, read errors 1, buffer overflow 1. Host tick frequency 10,000,000 per second, first failed read 468,294 ticks = 46.8294 ms, processing 0. This does not show when the buffer filled. Oct 23 earlier recording contained 95 valid returned TIMEOUT-labelled frames then one explicit overflow; no number of lost frames proven.

## Source inspection and uncertainty
J2534Diag opens capture-only CAN channel on Connect. On Start the user may have spent time selecting a disk filename; application then creates a catch-all PASS_FILTER and disk logger, starts worker and first GetMessages(200,5). It is POSSIBLE native driver buffers during part of that interval; queue onset and native driver behavior are UNVERIFIED. Neither application disk writer nor post-read frame processing explains the immediate Oct 24 failure.

## Draft PR #22
Add host Stopwatch timing for channel-open to filter-start, filter install duration, and filter-ready to first GetMessages entry. Retain existing read call and processing durations, original native J2534 time and all overflow caveats. This is read-only instrumentation, not filter tuning or buffer clearing.

## Verification
Windows CI for source-bearing PR22 head required before distributing revised ZIP. Synthetic tests do not verify live device performance. No new capture yet; previous recordings and integrity sidecars must remain preserved. If build passes, inspect ZIP/checksums and conduct one controlled vehicle capture only after installation. Never claim host intervals prove native buffering onset.

## Verified Windows engineering-preview artifact
[Windows CI run 40120424765](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/40120424765) source SHA `1546f5dbc74a0b2d28d3c6c8a5bca79ad2d6fd49`: **6,333 assertions / 64 offline tests PASS**, release build 0 errors, 5 warnings. ZIP artifact ID `12570234929`, 1,823,892 bytes. Downloaded archive SHA-256 independently verified `0740428ecacb0bf3ee5e40a0db348088040ca6453fda7f9c28f46abf2af100f7` matching GitHub artifact digest. ZIP contains 8 expected files. All 7 manifest file digests were independently recomputed and matched. Manifest source commit and workflow run match. This validates package bytes, NOT in-vehicle measured startup timings. Current preview still has generic manifest label 'VehicleCapture=UNVERIFIED' even though earlier releases had CONFIRMED live reception; interpret as new build not yet tested on car.

Next gate: operator installs updated ZIP into distinct folder, keeps prior raw captures, conducts at most one controlled ignition-ON/engine-OFF capture-only session with new CSV and sidecar. If overflow occurs immediately, preserve logs and do not repeat. New fields HostChannelOpenToFilterStartTicks, HostFilterInstallElapsedTicks, HostFilterReadyToFirstReadTicks distinguish startup intervals from prior HostReadElapsedTicksTotal / ProcessingElapsedTicksTotal. Any buffer-clearing operation remains OUT OF SCOPE until manufacturer-confirmed semantics and safety.
