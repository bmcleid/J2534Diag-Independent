# October 26 — Real Windows StdCall synthetic-native regression gate

## Purpose
Test the previously unverified ABI seam of J2534Diag's draft native receive foundation **without opening an RLink X3 or vehicle communication channel**. The original GM 2012 SRX findings stand: managed J2534Diag returned first-read explicit buffer overflows with zero application-accepted rows, in contrast with 10k in-memory data and a separate OneApp native 25,772-frame capture. The true driver-side cause and actual pNumMsgs values remain UNVERIFIED.

## Controlled Windows CI
- Compiles `Tests/native-j2534-mock/mock.cpp` with Microsoft Visual C++ x64 into `mock-j2534.dll`. This is a local synthetic J2534 provider, NOT a copy of any TOPDON or vehicle driver.
- The test harness is reachable only through `--ci-native-sim` while `GITHUB_ACTIONS=true`, under a strict `mock-j2534.dll` filename and *before calling any J2534 exports* requires a separately exported `SrxSimulatorIdentity()` marker with exactly 0x53525853. There is no hardware discovery or fallback DLL path.
- Against the compiled DLL, creates `NativeJ2534StdCallApi` and `NativeOwnedCanSession` using the same Windows native marshaller that a future live implementation would use. Tests success with two actual returned messages; TIMEOUT with one; BUFFER_OVERFLOW with two partial returned records; 0x12 with native count zero; stale requested count with no populated data; over-requested return count; BUFFER_EMPTY; Open, Connect and Filter failures; Close failure followed by successful cleanup retry; StopFilter failure followed by remaining independent cleanup.
- Asserts status, actual returned message count, native timestamp, CAN ID/payload, overflow persistence and incomplete classification. Unknown/invalid counts remain untrusted.
- Compiles Tests executable with Prefer32Bit=false to match x64 native test DLL. The normal app is unaffected.

## Release/vehicle gate
**NOT A LIVE RLINK FIX.** This test does not verify TOPDON DLL binary bitness, signing, native compliance or actual overflow mechanism. The GUI still runs the original pinned J2534-Sharp channel, not the new native transport. Before vehicle use coordinate a single app-wide native/managed connection lock, settle native DLL path/bitness through actual installed RLink driver evidence, and require separately operator-authorized physical test. Native CAN may send ACKs.

## Verification
Windows CI is pending; do not claim simulation success until workflow reports PASS.

## Verified Windows CI
First native simulator run [40364645189](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/40364645189) FAILED the cleanup_close_error scenario: the C++ mock used CRT getenv(), which did not observe a scenario change applied after DLL load via .NET Environment.SetEnvironmentVariable. The regular build passed and 6,437 offline regression assertions passed before that mock issue. **Correction:** the mock now uses GetEnvironmentVariableA (Win32 process environment), maintaining dynamic fault injection while the DLL remains loaded. This modified **only the synthetic test DLL**, not J2534Diag physical read settings or native production transport.

**CONFIRMED** [Windows Actions run 40364945314](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/40364945314) on code commit `b6a1f9c74669fb5614ead079a235237c4948b15b`: Windows Release build succeeded with 0 errors, 5 warnings; **6,445 assertions across 64 offline tests PASS**, plus **29 additional isolated real-DLL StdCall synthetic assertions PASS**. The native mock verifies actual x64 marshaling of messages/counts and the fault paths listed above. GitHub-reported ZIP artifact ID `12670186299`, digest `sha256:d95ecdf28acbfd74cd5e529adf00cc1fd62883ccd6ea9b7dabae3250ad22dbfa`; not independently downloaded or unpacked in this turn. Neither GitHub Actions run opened or communicated with a physical TOPDON adapter.

**Remaining mandatory gate:** integrate one mutually exclusive native-versus-J2534-Sharp connection owner into the GUI with tested acquire/release/cancel/failure paths; verify exact TOPDON DLL path and bitness from operator's PC before an operator-authorized physical capture. Do not call native adapter a production fix or identify GM modules from synthetic frames.
