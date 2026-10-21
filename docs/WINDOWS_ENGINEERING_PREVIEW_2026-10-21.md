# J2534Diag engineering preview: Windows artifact verification

## What this is
Offline-first engineering build of stacked J2534Diag work through PR #17, NOT a production diagnostic release, installer, or proof of RLink live capture.

## Build chain
The Windows Actions workflow restores NuGet packages, compiles .NET Framework 4.8.1 WinExe and 64-test offline suite, then computes SHA-256 for every file under bin/Release before writing ENGINEERING_PREVIEW_MANIFEST.txt into the downloadable artifact. The manifest includes the exact Git commit and GitHub Actions run number.

## Requirements and safe first use
Windows computer with compatible .NET Framework 4.8.1; do not install/replace RLink drivers solely on this recommendation. Unzip the ENTIRE artifact together (EXE plus packaged DLLs, configs). Inspect manifest and compare SHA-256 of extracted files. Start with RLink disconnected, open GUI, load a known original CSV and verify file digest and metadata presentation. Do not start transmission-capable modes or connect vehicle until offline smoke-test results are recorded.

## Exclusions and remaining issues
In-memory integrity retry state is lost on app restart; runtime DLL binding / host architecture and WinForms layout have not been operator-tested. Any Windows-origin CAN receive or transmit remains UNVERIFIED in this independent application. Neither the known 25,791-row offline import nor separate newer native CAN captures are a new hardware test of this build. No merge to baseline, no ECU programming or change to T43/OneApp/Android.

## Verification gates
Review final workflow run conclusion, specific commit, checksum manifest presence in resulting artifact, and test count. Do not claim the artifact includes a useful installer. Only operator PC offline UI smoke test can clear next gate.