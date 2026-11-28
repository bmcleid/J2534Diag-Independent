# R26 everyday use

Extract the complete Windows ZIP into its own folder and open J2534Diag.exe. The title identifies R26 and the tested source prefix. Use the same Windows/.NET Framework4.8.1 installation and separately installed J2534 driver as before; the ZIP does not install or register a driver.

Connection settings lock while a connection or receiver selection is retained. Stop the active receiver and Disconnect before changing mode, receiver or CAN width. A failed cleanup keeps Disconnect available and does not pretend the channel has closed. A cancelled connection attempt returns the action to Connect; no-provider state keeps it disabled. Capture-only remains the default and does not enable diagnostic polling.

Fuel Trims shows Disabled for unchecked PIDs. Enabled-set changes follow the existing polling-cycle semantics; the display refreshes on the existing timer. Disabling does not delete prior observations or charts. Source changes and Clear Charts affect displayed history, while report evidence remains available within its bound.

Health counters have distinct meanings:

- Accepted: report-lifetime accepted scalar observations.
- Coalesced: queued values replaced by a newer value before acceptance.
- Sample drops: valid-scalar callback/queue/state/report rejections; see CSV counters for reasons. Ignored non-scalar/invalid inputs are separate.
- Rows evicted: old event rows removed from the bounded20,000-row report. This is not a count of vehicle-bus loss.
- New since export: accepted observations newer than the last successfully saved report snapshot. A failed save does not reset it; observations arriving after a snapshot remain new.

Export session saves the current bounded report with lifetime PID summaries, explicit eviction/callback counters and build provenance. A successful export acknowledges that snapshot, including its eviction disclosure; it cannot restore evicted detailed rows. DisconnectOutcome metadata describes the manager's current connection at export; retained rows/events preserve each prior session's original context.

When closing with new accepted observations, choose Yes to export, No to close without exporting, or Cancel to keep the app open. Cancelling the save dialog or encountering a save error keeps the app open. The normal receive-shutdown guard runs first; an owned channel cannot be force-closed by choosing a report option. The saved close-time report precedes the subsequent manager disposal, so it does not promise to contain that later final disconnect event.

This prompt is based on new accepted observations, not event-only changes after a save. It is not crash recovery or automatic disk recording. Export before closing when the evidence is needed; No, forced termination or process failure can lose remaining in-memory data.

Prior R21 physical acceptance, R22 full-width offline acceptance and R23–R25 owner launch evidence remain established. R26 physical adapter/vehicle behavior is unverified. No repeated owner screenshot, adapter connection or vehicle scan is needed for this delivery.
