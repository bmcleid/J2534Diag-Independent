## J2534Diag host receive timing, October 23

New read-only instrumentation records monotonic Stopwatch ticks spent inside each J2534 GetMessages call separately from ticks spent validating/storing returned batches. Both totals/maxima and tick frequency appear in integrity sidecar; **not** vehicle bus timestamps, not calibrated RLink clock, and not evidence of lost-frame counts. Read failure/overflow time remains counted. Fake transport tests check retained frame counts and overflow handling. Original capture 95 frames and one overflow remains historical evidence. Do not change requested 200 messages/5 ms or retest vehicle until a new CI-verified build is available. These metrics cannot identify native driver buffer depth or prove whether an overflow originated during GetMessages or between calls.


## CI verification
**CONFIRMED** [Windows run 39967444589](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39967444589) on code-bearing commit `cae3874fd1c349d0d2f1c48c36e9e9af1f17f3a4`: Windows Release 0 errors, 5 warnings, **6,328 assertions across 64 offline regression tests PASS**. No actual fresh live timing measurements have yet been obtained.
