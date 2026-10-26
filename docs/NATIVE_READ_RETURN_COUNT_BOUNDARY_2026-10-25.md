# Native J2534 return-count validation — offline-only

This isolated boundary validates a returned SAE J2534 status, actual pNumMsgs count, and copied message slots from a future single-owner native provider. It rejects counts beyond requested or supplied slots, rejects wholly unpopulated reported batches, clones payloads, preserves partial TIMEOUT frames and partial BUFFER_OVERFLOW frames, and always throws after an overflow so no complete vehicle-bus capture is claimed.

CONFIRMED previous Cadillac evidence: three independent J2534Diag capture attempts reported overflow, including two zero application-row attempts. A separate in-memory attempt saved 10000 rows. Native OneApp captured 25772 rows without explicit overflow in a separate test. Native pNumMsgs at J2534Diag overflow remains UNVERIFIED.

IMPORTANT: This PR does not call J2534 DLLs, create devices, connect channels, alter the GUI or adjust 200-frame/5ms settings. J2534Diag continues using its original J2534-Sharp channel. This is synthetic preparation for a later independently verified sole-owner physical receive transport, not a live RLink fix. No further vehicle capture yet.

## Offline CI gate completed
CONFIRMED: [Windows Actions run 40255470042](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/40255470042) completed successfully for source commit dde2551182d7b1049f6ae5cdafca7de96c5b59a2. Windows build 0 errors, 5 warnings; 6,391 assertions across 64 offline tests PASS. Artifact ID 12622276786, GitHub-reported ZIP SHA-256 5b49364785a0e6e94fa428cdad1690b2862e29dfd4bdb9c7025268d416738f38. ZIP has NOT been independently downloaded/inspected here; source/build is CONFIRMED, package contents not independently verified this turn. No new vehicle or native driver access was involved.

Next safe development gate: sole-owner native PassThruOpen/Connect/StartMsgFilter/ReadMsgs/StopFilter/Disconnect/Close transport with 100-message baseline and overflow return count on the same API instance; prove struct/calling convention against the known OneApp implementation and CI simulator, ensure cleanup after partial failures. Do not integrate with an existing managed Channel or assume physically silent CAN. An operator vehicle test should only follow a separate verified build and justified test plan.
