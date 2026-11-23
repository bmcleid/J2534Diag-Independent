# J2534Diag R21 — targeted RLink readiness review

Date: November 23, 2026. Owner: independent J2534Diag, feature/native-gui-capture-preview, draft PR #29.

## Decision

**READY for one controlled managed RLink/vehicle compatibility check.** This is a readiness decision based on completed software gates, not a claim that the installed TOPDON driver or the SRX has passed. R21 supersedes R16 for this check because genuine VIN acceptance and failed-start cleanup defects were demonstrated and corrected.

The check fills one remaining gap: actual J2534-Sharp managed-driver communication with the installed RLink provider and this vehicle. Earlier direct-native 7E2→7EA handshakes remain accepted and are not repeated. This check does not read enhanced TCM definitions, TFP status or repair the historical transmission faults.

## Exact candidate

Source 69cd5f000c814543f5b8ba1841175b9650e78228, Windows run45240442863/job137923302938 SUCCESS. ZIP J2534Diag-R21-Windows.zip, 1,864,055 bytes, SHA256 9ace6d0bb51068f267e7a8784e4786a84fd60e77eb0f8a517b7f7e85e2ccaf1f. EXE SHA256 0ecd786f10cb8792c531af8fea1ae25d5e9efa8cbc653118bcdb7916575e9080. Saved package Library libfile_d4cbd4ed4e888191bcff6c04fc6eb0a8. Keep R16 separately as historical evidence; do not use it for this new active VIN gate.

## One bounded check

1. Extract R21 to its own folder and run its J2534Diag.exe. Close other applications using the RLink.
2. With the vehicle stationary in Park and engine idling outdoors, use the already-installed RLink connection and its actual listed provider. No driver replacement, provider registration or programming is needed.
3. On Vehicle, choose **Original J2534-Sharp receiver**, clear **Capture-only (no app diagnostic requests)**, and click Connect once. This actively requests VIN: first Mode09; if unavailable, the existing bounded UDS F190 fallback uses 11-bit then 29-bit addressing. VIN flow control is also possible. It is not an application-wide Mode01-only operation.
4. If Connect succeeds, compare the displayed VIN to the established vehicle VIN 3GYFNEE31CS519173. If it differs, disconnect and stop this check. If Connect fails, preserve the exact error, do not repeat Connect, and report it.
5. If VIN matches, open **Fuel Trims** for about ten seconds. Record the response-source selection and the six displayed values or Unknown/stale states. Fuel Trims requests only Mode01 PIDs 0C,06,07,08,09,11; unsupported values may remain unknown. Source addresses are not confirmed ECU identities. A live screenshot here would be new physical evidence; another disconnected screenshot would not.
6. Return to Vehicle and click Disconnect. Record whether disconnect completes and whether any error appears. Close the tool normally. Do not open Misfire Monitor or start other diagnostic functions during this gate.

No driving, DTC clearing, enhanced TCM sweep, programming, repeated handshake, new adapter purchase or parking-brake procedure is part of this check. The ten-second observation does not establish accuracy or PID support across all driving conditions.

## Acceptance and next branch

Success means the correct vehicle VIN arrives through the installed managed provider, at least the supported scalar responses reach the actual form with their source recorded, and shutdown completes without a retained session/error. RPM changing in plausible fashion while idling supports freshness; it does not calibrate fuel-trim accuracy. Missing individual PIDs do not prove a fault in that sensor or module.

Failure means preserve the first exact error/state and stop. Distinguish provider load/open/connect, VIN identification, scalar replies and cleanup. No blind retry or replacement recommendation follows. Actual RLink, GUI provider discovery/selection, all three successful VIN response modes and vehicle scalar support remain UNVERIFIED until directly observed. The existing virtual tests establish absence/partial cleanup and normal 11-bit Mode09 success; they do not prove successful 29-bit VIN retrieval.

## Offline verification already completed

One Windows validation, no rerun: 6617 regression assertions including 28 new VIN rejection/recovery and 21 actual form checks; 29 isolated native StdCall checks; five historical identity/defect reproduction checks; historical real-managed retained-resource defect reproduction; seven missing-VIN cleanup checks; seven interrupted-VIN cleanup checks; 21 normal managed VIN/form/reconnect checks. Full logs and normalized wire traces are in the owning handoff/evidence folder. None are measurements from the real SRX.
