# R31 — bounded, responder-specific Mode06 support discovery

## Acceptance state

CONFIRMED locally: 81 focused model assertions pass. Changed production form plus new UI harness compile against retained R30 dependencies (Mono C# 7.2 seam compile; three expected duplicate-source/type warnings). This is not the full Windows product build or Windows UI acceptance. Windows focused gate and exact package preservation are PENDING at this checkpoint. R30 remains the accepted owner-PC fallback. PR #29 stays open, Draft, unmerged.

## Scope and behavior

Each Misfire tab start creates a fresh support session. One functional `06 00` gathers response-source-specific availability. Only a source advertising the next range receives directed `06 20/40/60/80/A0/C0/E0` continuation requests. A cleared continuation bit ends that source's chain. A missing, negative or malformed response cannot establish absence. The four-byte availability integer is big endian: its most significant bit is page+1, its least significant bit is the next availability range. E0's least significant bit is reserved, not a wrap to 00.

Only enabled, implemented reference MIDs A2–A9 advertised by that same responder can then receive directed result requests. Discovery and measurement payloads cannot substitute for one another. Sources, current request/MID, completeness, duplicates and generation boundaries are checked. Support is never shared between responders. There is one bounded result snapshot, not continuous polling. Changing tabs away/back permits a fresh attempt after the existing reader-ownership cleanup.

UI states distinguish Undiscovered, Not advertised, No result, Unverified, a real measured zero and unknown scaling. Advertised MID support does not prove a particular TID, scaling, cylinder mapping, calibration applicability or diagnosis. The generic eight-slot reference list does not identify the six-cylinder SRX as eight-cylinder. Tooltips retain source bitmap/failure status and existing raw value/limits. Previously loaded saved scalar sessions are unchanged; Mode06 discovery is not a new saved-session CSV schema or export claim.

Application scheduling policy: 300 ms response window, 50 ms request gap, maximum 128 requests and 30 seconds before scheduling another request; at most 16 responder records. These are application limits, not verified OEM timing requirements. A blocked provider call cannot be preempted by this clock. Existing bounded Stop/ownership retention remains in force. NRC 78 does not extend the window; a final response within it may still be accepted. Other negatives terminate that source/request. Stop/error/timeout preserve prior evidence rather than fabricate zeros.

## Primary-source basis

- ISO 15031-5:2006 §7.6.2 (Tables 157–160), Annex A Table A.1: service 06/46 availability versus test-value formats and bit ordering. Original standard PDF mirror: https://share.qclt.com/%E6%B1%BD%E8%BD%A6%E8%AF%8A%E6%96%AD%E5%8D%8F%E8%AE%AE2/ISO-15031-5%5B1%5D.pdf . Used for technical verification only; no licensed PDF redistributed.
- AUTOSAR DCM R23-11, SWS_Dcm_00414/00945/00956/00957/00958, service 06, pp205–206: availability pages and separation from nonavailability result requests. https://www.autosar.org/fileadmin/standards/R23-11/CP/AUTOSAR_CP_SWS_DiagnosticCommunicationManager.pdf
- AUTOSAR DEM R21-11 SWS_Dem_00766, Dem_DcmGetAvailableOBDMIDs: uint32 bit-coded availability. https://www.autosar.org/fileadmin/standards/R21-11/CP/AUTOSAR_SWS_DiagnosticEventManager.pdf
- Existing R30 GM reference review remains qualified; no new vehicle-specific interpretation is inferred from generic standard support.

## Focused acceptance design

Model: bitmap boundary/order/continuation, two-source interleaving, wrong source/page, measured zero, no unsupported requests, timeout, malformed/negative/pending, duplicate/conflicting bitmap, cancellation/restart/stale reply, 29-bit route, request/time caps and send failure.

Windows: full production build once; new actual MainForm/MisfireForm fake-transport polling seam; real production request construction and single-frame receive/source/ISO-TP processing; complete result payload injected at existing parser event boundary. This deliberately does not open a provider or prove driver reassembly. Production-thread marshaling, real card states, source selection, visible/narrow toolbar, negative/malformed/cancel and restart generation are checked. Preflight and before/after assertions require zero registered providers/handles. Save actual rendered UI for inspection. Reuse R30 decoder/transport/lifecycle checks and R28/R29 acceptance; do not rerun old suites or owner launch/vehicle scan.

## Remaining limits

Installed-ECU advertisement, TID/calibration meaning and physical transport remain UNVERIFIED by these offline tests. No RLink/vehicle connection, diagnostic transmission, DTC clear, programming, actuation or merge is authorized or performed. An offline accepted R31 package is a software acceptance only. Keep R30 until R31 passes its focused gate and the exact tested bytes are Git-preserved.
