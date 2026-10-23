# J2534-Sharp GetMessages source audit, October 23 2026

## Independent external source observations
Studied historical C# source from anctech/J2534-Sharp (fork of BrianHumlicek/J2534-Sharp), `Src/Channel.cs`, `Src/DataClasses/HeapMessageArray.cs`, and `Src/DataClasses/GetMessageResults.cs`. The project's pinned dependency is NuGet J2534-Sharp 1.0.0-CI00036 (2023). These files are *family-level source* rather than independently verified package-exact binary-source correspondence.

- Historical `Channel.GetMessages(int NumMsgs,int Timeout)` sets the unmanaged buffer length to requested NumMsgs, calls `API.PTReadMsgs(channelId, buffer, lengthPointer, Timeout)` under an instance lock, tolerates TIMEOUT and BUFFER_EMPTY, and calls `API.CheckResult` for other statuses.
- `GetMessageResults` returns both Message[] and ResultCode; HeapMessageArray.ToMessageArray builds entries from the output `Length` updated by the native driver. Thus a partial TIMEOUT carrying frames is consistent with wrapper semantics.
- Historical API comments say read request must fit buffer (default 200), and nonzero timeout waits until requested count or expiry. These comments do not certify TOPDON's implementation or determine appropriate operating point.
- Newer J2534-Sharp 2.x uses renamed API and different memory management; DO NOT substitute its code behavior for our pinned 1.x package.

## October 23 vehicle evidence and limitation
The independent J2534Diag capture yielded 95 frames in a TIMEOUT-labelled batch, then one BUFFER_OVERFLOW error. Disk status reconciles all 95 accepted rows. The driver reported the overflow, but **its buffer depth, actual lost-frame count and root performance constraint remain UNVERIFIED**. Confirming CAN receiver function does not establish capture completeness. No inference of Cadillac module failure.

## Best next software measurement
On a separate draft branch, instrument receive-call host elapsed time and post-read frame processing time and make their totals/maxima visible in integrity sidecar. Tests must use synthetic controlled timing, preserve raw native timestamps separately, and introduce no transmitted messages. This would discriminate blocked driver waits from host/application stall during next vehicle capture. Do not change `GetMessages(200,5)` before evidence or declare the driver defective. Operator A/B capture may be justified **after** measurement instrumentation passes Windows CI.

## References
https://github.com/anctech/J2534-Sharp/blob/master/Src/Channel.cs
https://github.com/anctech/J2534-Sharp/blob/master/Src/DataClasses/HeapMessageArray.cs
https://www.nuget.org/packages/J2534-Sharp/1.0.0-CI00036
https://quantexlab.ru/en/develop/j2534/pt_readmsg.html