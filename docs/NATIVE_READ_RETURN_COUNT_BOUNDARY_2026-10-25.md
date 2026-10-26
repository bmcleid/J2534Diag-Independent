# Native J2534 return-count validation — offline-only

This isolated boundary validates a returned SAE J2534 status, actual pNumMsgs count, and copied message slots from a future single-owner native provider. It rejects counts beyond requested or supplied slots, rejects wholly unpopulated reported batches, clones payloads, preserves partial TIMEOUT frames and partial BUFFER_OVERFLOW frames, and always throws after an overflow so no complete vehicle-bus capture is claimed.

CONFIRMED previous Cadillac evidence: three independent J2534Diag capture attempts reported overflow, including two zero application-row attempts. A separate in-memory attempt saved 10000 rows. Native OneApp captured 25772 rows without explicit overflow in a separate test. Native pNumMsgs at J2534Diag overflow remains UNVERIFIED.

IMPORTANT: This PR does not call J2534 DLLs, create devices, connect channels, alter the GUI or adjust 200-frame/5ms settings. J2534Diag continues using its original J2534-Sharp channel. This is synthetic preparation for a later independently verified sole-owner physical receive transport, not a live RLink fix. No further vehicle capture yet.
