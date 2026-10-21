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

## Verified CI execution and downloadable artifact
- **CONFIRMED CI success:** [Windows run 39694987046](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39694987046), build-bearing commit `e3fc416ae43c350e9109dd3b9c4e689ef88f16e1` (later documentation-only commit). Release compilation 0 errors / 5 warnings; test executable reported **6,323 assertions / 64 offline tests PASS**. Counts vary among previous runs; use the actual run log, not a hardcoded baseline.
- [Engineering preview artifact ID 12390555336](https://github.com/bmcleid/J2534Diag-Independent/actions/runs/39694987046/artifacts/12390555336), `J2534Diag-baseline-windows`, ZIP size 1,820,262 bytes. GitHub artifact archive digest `sha256:e1a6870f16f83ebddf88a5187f77d84fa32b680552c2ff47c54df4a1d73ca10f`, as reported by GitHub API. This is the archive digest, not a checksum of individual EXE/DLL files.
- **CONFIRMED payload inspection:** downloaded artifact ZIP was independently opened and inspected. It has eight entries: J2534Diag.exe, J2534Diag.exe.config, J2534Diag.pdb, J2534-Sharp.dll, J2534-Sharp.xml, Newtonsoft.Json.dll, Newtonsoft.Json.xml, ENGINEERING_PREVIEW_MANIFEST.txt. All **seven** payload file SHA-256 hashes listed in the manifest were recomputed from ZIP entries and matched. Independently recomputed archive SHA-256 `e1a6870f16f83ebddf88a5187f77d84fa32b680552c2ff47c54df4a1d73ca10f` matched GitHub artifact metadata. Manifest SourceCommit = `e3fc416ae43c350e9109dd3b9c4e689ef88f16e1`; WorkflowRun = 39694987046. This validates bytes/package integrity but NOT Windows execution or connected RLink operation.
- **Operator action required next:** On Windows, download the artifact ZIP and extract ALL files to one folder; verify manifest/source SHA and launch `J2534Diag.exe` with RLink disconnected. Report any Windows launch error or screenshot. No car or hardware testing until disconnected smoke test passes.
