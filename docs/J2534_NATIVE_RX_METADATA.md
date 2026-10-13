# J2534 native receive metadata — development specification

Status: Experimental; isolated in `feature/j2534-native-rx-metadata`. No physical
TOPDON RLink X3 validation has been performed.

## Source-derived interface verification

The application pins NuGet package `J2534-Sharp 1.0.0-CI00036`.
The project's J2534-Sharp *V1-legacy source tree* defines:

- `SAE.J2534.Message.Timestamp` as `uint`.
- `SAE.J2534.Message.RxStatus` as `RxFlag`, backed by `FlagsAsInt`.
- `SAE.J2534.GetMessageResults.Result` as a `ResultCode`.
- `SAE.J2534.GetMessageResults.Messages` as `Message[]`.
- `Channel.GetMessages` returns `TIMEOUT` or `BUFFER_EMPTY` as
  normal results, potentially with messages; other errors propagate as
  `J2534Exception`.

Sources:
- https://github.com/BrianHumlicek/J2534-Sharp/blob/V1-legacy/Src/DataClasses/Message.cs
- https://github.com/BrianHumlicek/J2534-Sharp/blob/V1-legacy/Src/DataClasses/GetMessageResults.cs
- https://github.com/BrianHumlicek/J2534-Sharp/blob/V1-legacy/Src/Channel.cs

**Evidence boundary:** Source compatibility with the pinned NuGet assembly is
checked by our Windows compilation, but the library source inspection alone
does not establish TOPDON RLink X3 native timestamp or RxStatus behavior.

## New per-frame export columns

Added to the existing five-column capture CSV:

```text
J2534TimestampRaw
J2534RxStatusHex
J2534ReadResult
```

- `J2534TimestampRaw` is the *unmodified unsigned 32-bit message timestamp*
  expressed as invariant-culture decimal text. Zero is retained as `0`.
  Empty means unavailable, not zero. Do not assume that a host wall-clock
  conversion, hardware timebase, units, or rollover behavior has been proved.
- `J2534RxStatusHex` preserves the raw unsigned receive-status flags,
  zero-padded as `0x00000000` to `0xFFFFFFFF`. No meaning is assigned
  to undocumented device-specific flag combinations.
- `J2534ReadResult` is the `ResultCode.ToString()` for the containing
  `GetMessages` batch; for example `STATUS_NOERROR` or `TIMEOUT`.
  A read returning *zero frames* creates no capture row. The absence of
  an empty-buffer row is not evidence that the device never timed out.

`ReceivedAtUtc` remains the separate Windows host timestamp captured in
our processing loop. It does NOT become a J2534/device timestamp.

## Import and backward compatibility

The new CSV writer appends columns instead of changing original column names.
The reader accepts the original three-column CSV, the intermediate
five-column CSV and the new eight-column CSV. Missing metadata is represented
as an empty value and is never synthesized as zero.

## Tests

Offline fake-source tests verify unsigned boundary values, zero vs unknown,
timeout-with-message status, high-bit receive flags, and preservation of
native metadata. CSV tests verify round-trip and import compatibility.

## Second verification layer: pinned NuGet types

The production `J2534CanFrameSource` now projects
`SAE.J2534.GetMessageResults` and `SAE.J2534.Message` through a separate
testable adapter. The Windows test project references the same NuGet
`J2534-Sharp 1.0.0-CI00036` assembly as the application.

Three additional offline tests construct actual J2534-Sharp objects and
verify high-bit flags, unsigned timestamps (including zero and `uint.MaxValue`),
per-batch `TIMEOUT` and `STATUS_NOERROR` labels, cloned payload bytes,
end-to-end forwarding into `CanCapturePump`, and null/empty read handling.

The Windows CI run for `8d2709d` completed successfully:
**0 compiler errors, 5 nonblocking warnings, and 84 assertions across
21 regression tests passed.** This proves compilation and the tested
object-projection behavior against the pinned managed dependency; it does
**not** establish compatibility with the TOPDON vendor DLL/driver or vehicle.

## Remaining release gates

1. Verify `J2534-Sharp 1.0.0-CI00036` builds with the exact API signatures
   used here on Windows CI.
2. Verify a controlled RLink X3 session with no other open J2534 client.
3. Compare host UTC vs raw native timestamps across multiple captures,
   ignition transitions and a new interface connection before defining
   relative timing or wrap behavior.
4. Preserve and classify receive-status bits only after OEM/SAE and
   device-specific documentation; verify whether `TxIndication` frames or
   other flags should appear in ordinary data captures.
5. Build a disk-backed bounded-memory logger and explicit overflow reporting
   before describing captures as lossless.
6. Confirm application shutdown, filter ownership and reconnect behavior
   under actual driver conditions. No unverified GM enhanced TCM addressing,
   module programming, DTC clearing, reset, or security access is included.
