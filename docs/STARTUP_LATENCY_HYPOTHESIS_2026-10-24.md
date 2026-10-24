# Oct 24 J2534Diag first-read overflow startup telemetry

## CONFIRMED vehicle-specific evidence
Oct 24 uploaded zero-frame CSV and integrity sidecar for CAN-session-20261024-141546.csv. Sidecar: pump 0, disk queue/writer 0, read errors 1, buffer overflow 1. Host tick frequency 10,000,000 per second, first failed read 468,294 ticks = 46.8294 ms, processing 0. This does not show when the buffer filled. Oct 23 earlier recording contained 95 valid returned TIMEOUT-labelled frames then one explicit overflow; no number of lost frames proven.

## Source inspection and uncertainty
J2534Diag opens capture-only CAN channel on Connect. On Start the user may have spent time selecting a disk filename; application then creates a catch-all PASS_FILTER and disk logger, starts worker and first GetMessages(200,5). It is POSSIBLE native driver buffers during part of that interval; queue onset and native driver behavior are UNVERIFIED. Neither application disk writer nor post-read frame processing explains the immediate Oct 24 failure.

## Draft PR #22
Add host Stopwatch timing for channel-open to filter-start, filter install duration, and filter-ready to first GetMessages entry. Retain existing read call and processing durations, original native J2534 time and all overflow caveats. This is read-only instrumentation, not filter tuning or buffer clearing.

## Verification
Windows CI for source-bearing PR22 head required before distributing revised ZIP. Synthetic tests do not verify live device performance. No new capture yet; previous recordings and integrity sidecars must remain preserved. If build passes, inspect ZIP/checksums and conduct one controlled vehicle capture only after installation. Never claim host intervals prove native buffering onset.