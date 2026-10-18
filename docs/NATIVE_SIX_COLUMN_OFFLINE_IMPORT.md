# Offline six-column native CAN import — cross-project interoperability

Branch: `feature/native-six-column-offline-import`.
Base: `feature/native-can-rejection-accounting`.
Scope: an **independent J2534Diag display adapter** for previously
captured CAN frames. **No hardware access, no driver open, no CAN TX,
no GM/TCM enhanced-data decoding.**

## Why this exists

A separate SRX-PC-Scanner .NET implementation has already proven actual
RLink X3 vehicle reception with 25,791 accepted standard classical-CAN
frames and 97 IDs over 10.126089 seconds. The original Oct 12 data
were independently audited. Asking for another generic proof would
duplicate completed work.

The independent J2534Diag application previously supported its own
3-/5-/8-column CSV imports, but could not display the other application's
original native six-column CAN capture. This adds **interoperability
and offline source comparison**, not another live recorder or auditor.

Canonical source:
[real SRX CAN capture](https://github.com/bmcleid/SRX-PC-Scanner/blob/sandbox/j2534-regression/docs/REAL-SRX-CAN-CAPTURE-2026-10-12.md).
Canonical independent audit:
[October 14 validated audit](https://github.com/bmcleid/SRX-PC-Scanner/blob/feature/rlink-original-capture-audit-20261014/docs/SRX_RLINK_CAN_AUDIT_VALIDATED_2026-10-14.md).

## Source format

Exact header:

```csv
timestamp_us,can_id_hex,extended,rx_status_hex,dlc,data_hex
```

Import is allowed **only when this header is present**. We do not
guess whether unrelated CSV files are native CAN captures.

Conservative translation to J2534Diag's existing eight-column model:

| Native six-column field | J2534Diag field / handling |
| --- | --- |
| `timestamp_us` | `J2534TimestampRaw`, decimal microsecond value preserved |
| `can_id_hex` | `ArbId`, standard or 29-bit hex |
| `extended` | strict 0/1; checked against RxStatus CAN_29BIT_ID bit |
| `rx_status_hex` | `J2534RxStatusHex`, raw hex preserved |
| `dlc` | must agree with 0–8 byte `data_hex` |
| `data_hex` | `Data`, hyphen-separated hex bytes for GUI |
| no host UTC field | `ReceivedAtUtc` remains EMPTY |
| no read-result field | `J2534ReadResult` remains EMPTY |
| no original GUI row index | `Index` becomes a new 1-based ordinal, **not a time** |
| no ECU identification | `ArbIdDescription` EMPTY; no inferred module or GM signal |

The import rejects J2534 control/indication status bits, invalid
11-/29-bit IDs, bad DLC/hex, and malformed headers. This ensures the
UI does not silently fabricate accepted classical-CAN data. It is
**not** a second independent forensic or source-integrity audit.

### Important provenance boundary

If imported data are exported again as a J2534Diag eight-column CSV,
the result is **DERIVED** and no longer contains the original
`timestamp_us` header that explicitly documented microsecond units.
The UI warns about this. Keep the source six-column CSV and its audit
report unchanged for evidence.

The six-column importer does not recompute source SHA-256, verify original
rejected-record counts, reproduce CAN ID interval tables, or assert
capture completeness. Those remain the responsibility of the existing
SRX-PC-Scanner audit and original SHA-256 evidence record.

## Using the offline import

1. Launch J2534Diag; **no RLink or vehicle required**.
2. Open **CAN Listener** and click **Load CSV**.
3. Select the original six-column native CAN CSV.
4. The loader displays CAN IDs, raw J2534 timestamp, RxStatus and
   payload. Host UTC is blank, as expected.
5. Keep the source CSV unchanged; any manual J2534Diag Export is a
   separately derived view, not a replacement for the original file.
6. For completeness, gap or rejection analysis, continue to use
   the existing dedicated original-capture auditor.

## Automated proof versus vehicle proof

Offline CI tests cover strict schema detection, byte-length and
identifier consistency, status rejection, missing data and a **synthetic
25,791-row** native-format input. The synthetic payloads are NOT the
actual October 12 vehicle bytes.

- CONFIRMED from other project: original TOPDON physical CAN reception
  and independent SHA-verified original audit.
- CONFIRMED only after CI passes: this independent J2534Diag import
  compiles and handles tested synthetic source formats.
- UNVERIFIED: J2534Diag UI performance on the exact original operator
  file and physical SRX CAN reception with this product.

No new GM enhanced T43 request ID or sensor/TFP switch decoding is
introduced. Keep the T43 and Vgate Android development tracks separate.
