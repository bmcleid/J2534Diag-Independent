# R23 session evidence

Extract the complete Windows ZIP and launch J2534Diag.exe. R23 and the source prefix appear in the window and Fuel Trims actions area.

The session-health line distinguishes Running, Unanswered requests, Driver error, Cleanup pending and Stopped, with report-lifetime accepted, coalesced and discarded counts. Unanswered requests describes the app's polling termination; it does not diagnose the vehicle or prove a driver failure. Driver faults remain visible through cleanup.

Export session writes schema-2 CSV with the existing 16 columns. Metadata counts eligible scalar callbacks, ignored non-scalar/invalid inputs, replaced queue entries, capacity/inactive/unavailable discards, decode/state rejection and report rejection. These are app-boundary counts, not a lossless CAN recording or proof of bus loss. The bounded event buffer separately reports evicted rows. Six PID summary rows retain accepted-observation and fresh-to-stale transition totals across report sessions, sources and row eviction. Initially stale observations remain observations; they are not counted as fresh-to-stale transitions.

UTC records acceptance/event time and may move backward or forward with the system clock. Retained rows remain in event order. Age and the two-second app freshness rule use monotonic host ticks. Export does not invent values for missing, expired or stopped readings.

Response-source changes and Clear Charts are recorded. Clear Charts clears displayed observed history; it sends no ECU command and retains accepted observations in the report. Addresses identify response sources, not verified module identity.

Save errors preserve the existing target and in-memory evidence. Choose an available destination or release the file lock, then retry Export session. All counts cover the current form/report lifetime; closing the form ends that in-memory lifetime. Export before closing when the evidence is needed.

R21's owner-confirmed physical VIN/scalars/error-free Disconnect and R22's owner-confirmed offline full-width GUI remain established baselines. R23 Windows simulation results do not establish physical R23 RLink or vehicle behavior. No repeat vehicle test is needed for this delivery.
