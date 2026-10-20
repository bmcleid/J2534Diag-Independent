# J2534Diag capture tab shutdown guard — October 20, 2026

## Baseline
- Original October 16 native six-column Cadillac **25,791-frame OFFLINE GUI import CONFIRMED**; do not repeat.
- Receive lease, StopListening worker join, writer flush and pass-filter cleanup already exist in stacked draft branches.
- Previous MainForm.SelectedIndexChanged invokes StopListening but ignores its boolean return; switching tabs during an uncompleted capture stop leaves the capture panel while the receive gate remains owned. This is an **inspection-based software risk**, not a demonstrated TOPDON failure.

## Narrow change
MainForm subscribes to cancellable TabControl.Selecting after the embedded forms are created. When leaving CAN Listener, call StopListening **before** navigation. On false (driver receive thread still running, disk writer still draining, or filter cleanup failed), set TabControlCancelEventArgs.Cancel=true to retain the capture tab. Existing StopListening notifications describe retry and preserve channel ownership. A successful stop preserves existing tab switching behavior. Other scanning transports and T43 request paths are unchanged.

## Safety limits
The application is not assured electrically listen-only. No J2534 PassThruWriteMsgs, OEM service 22, DTC clear, programming, actuation, new receiver, native CAN analyzer, or Android BLE modification is added. Existing MainForm.OnFormClosing receive gate also continues to guard application shutdown. **Physical RLink driver timeout behavior and interactive tab selection remain UNVERIFIED** without an operator session.

## Verification
Windows Release build and regression test outcome must be checked at branch head. Added static-source offline checks ensure Selecting event registration, StopListening gate and cancellation are not accidentally removed. Synthetic/static CI cannot prove WinForms operator interaction or blocked physical driver timing. Keep stacked draft; preserve all historical capture evidence.
