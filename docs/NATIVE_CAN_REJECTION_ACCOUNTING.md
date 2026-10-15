# J2534 CAN indication and native-frame classification

This is a **J2534Diag-specific capture correctness gate**, not a new
vehicle-frame decoder. Source reference:
[SRX-PC-Scanner native frame classifier](https://github.com/bmcleid/SRX-PC-Scanner/blob/sandbox/j2534-regression/src/SrxPcScanner.Probe/CanFrameDecoder.cs).
J2534-Sharp's `RxFlag` definitions were independently reviewed at
[BrianHumlicek/J2534-Sharp V1-legacy](https://github.com/BrianHumlicek/J2534-Sharp/blob/V1-legacy/Src/Definitions/RxFlag.cs).

## Why the change was necessary

The independent scanner previously accepted every J2534 message with at
least four Data bytes as an ordinary CAN frame. This was insufficient
because J2534 read calls can return indication/control records in addition
to bus traffic. The separate SRX-PC-Scanner first physical CAN capture
from 2026-10-12 reported **25,791 accepted frames** and **9 rejected native
records**, but those nine original raw records and reasons are unavailable.
We cannot infer their reason or recover them from the saved CSV.

The observed physical capture does prove the *owned TOPDON RLink X3* can
receive 11-bit classical CAN from this Cadillac, but it does **not** prove
any particular diagnostic module ID, TFP bitfield or J2534Diag software
capture under actual vehicle load.

## Classifier rules

Our independent J2534-Sharp managed `Message` exposes `RxStatus`,
`Timestamp` and `Data`, **not the full native PASSTHRU_MSG fields**.

`CanFrameClassifier.TryValidate` applies:

1. If `RxStatus` contains `TX_MSG_TYPE` (0x1),
   `START_OF_MESSAGE` (0x2), `RX_BREAK` (0x4) or
   `TX_INDICATION` (0x8), treat the record as a J2534
   **indication/control**, not accepted vehicle CAN data.
2. Require a four-byte big-endian arbitration-ID prefix.
3. Require 0–8 classical CAN data bytes after the four-byte prefix.
   Do not mislabel CAN-FD/non-classical records as classical CAN.
4. If `CAN_29BIT_ID` (0x100) is reported, require ID <= 0x1FFFFFFF.
   Otherwise require a standard CAN ID <= 0x7FF.
5. Leave other `RxStatus` bits uninterpreted and preserve raw status
   in accepted CSV rows; unknown bits alone do not prove a fault.
6. Preserve the original sequence of accepted records. Rejected/control
   records do not consume an accepted-frame capacity slot.

Classification outcomes are accounted for in `CanCaptureSummary` and
in the disk integrity report as:
- `J2534IndicationRecordsIgnored`
- `InvalidShortOrNullCanRecords`
- `InvalidOversizeClassicalCanRecords`
- `InvalidArbitrationIdRecords`.

The original `InvalidReceivedFrames` field retains the total of
structurally invalid classical-CAN messages. Expected J2534 indications
do **not** make `ApplicationWriteComplete=False` by themselves, because
the application deliberately does not store them as bus data.
They are **not** evidence that a CAN frame was physically lost.

## Important outstanding limitations

- The current J2534-Sharp `Message` abstraction does not expose
  native `ProtocolID`, independent `DataSize`, and
  `ExtraDataIndex` in this API. The separate native logger can
  perform validation that this independent wrapper cannot.
- Only counts, not the complete rejected-message raw fields, are saved
  by this classifier version. The integrity report explicitly says
  `RejectedRecordContentsRetained=NO`.
- No source-specific first-TOPDON rejected records can be recovered.
  The original nine from Oct 12 remain **UNCLASSIFIED**.
- The observed native timestamp (microseconds in the SRX raw logger)
  and J2534Diag's separately recorded host UTC are different
  clock domains and cannot be equated without dedicated validation.
- No signal decoding, data-ID scanning, T43 enhanced service or
  diagnostic transmission is introduced in this branch.
- Successful Windows CI proves managed code behavior against synthetic
  fixtures and pinned library objects, **not** the behavior of every
  TOPDON/RLink firmware revision on a live vehicle.

## Cross-track testing and next evidence

Do not request another generic physical CAN proof solely to reproduce the
already-established Oct 12 reception. The independent J2534Diag app
has its own confirmed USB-only idle read/close/reconnect test, but its
**actual on-vehicle** frame classification remains to be checked when a
targeted, nonredundant comparison is justified.

Future native-device regression should compare this independent app's
capture completeness/integrity report against its own recorded vehicle
frames, retaining the established native C# capture as separate
reference—not as a synthetic test claiming hardware success.

The T43, six-column forensic audit and Android/ELM projects keep their
own implementation ownership; see
[shared cross-project record](https://github.com/bmcleid/SRX-PC-Scanner/blob/main/docs/SRX-SCANNER-CROSS-TRACK-COORDINATION.md).
