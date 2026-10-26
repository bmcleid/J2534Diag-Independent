# Operator evidence — canonical SRX native offline CSV import, 2026-10-22

User screenshot `Screenshot 2026-10-22 125705.png` shows J2534Diag engineering-preview Windows CAN Listener with rows populated and modal `Native CAN recording imported`.

**CONFIRMED direct on-PC evidence:** modal states 25,791 native six-column CAN rows loaded for OFFLINE viewing; original timestamp_us preserved as raw; no host UTC synthesized; SHA-256 source digest exactly `0C57F4F2E13025E873480766CD63A907E01F11074863A2A5068A77169A734D9B`; message reports confirmed byte match against canonical October 12 source. Visible grid includes raw timestamp, RxStatus, arbitration ID and payload. Bottom status Disconnected/VIN blank. Hardware connection not required.

**Important limitations:** A file digest verifies equality to the prior canonical bytes, NOT completeness of original CAN-bus traffic, source ECU identity, clock calibration, module mapping, or live RLink driver communication. CSV source file and timestamp captured on operator PC; no new recording or outgoing vehicle commands.

**Next appropriate offline UI gate:** dismiss modal and test `Distinct Only` toggle / `Filter` control without rewriting the original source; if desired test `Export CSV` to fresh output and inspect its generated format. No `Connect` or `Start` yet. New persistent report-state work is separate and not necessary for current recording verification.
