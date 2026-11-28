# Accepted build preservation — J2534Diag

Owner rule, 2026-11-28. The standing gate text lives in `AGENTS.md`
("Accepted build preservation gate"); this document is the how and
why. The same rule was adopted the same day for the Android Data
Logger APK and for SRX-PC-Scanner's OneApp; this is the J2534Diag
counterpart.

## Why

J2534Diag.exe is built in CI and accepted from CI packages, but no
build artifact has ever been committed to this repository — a full
history audit on 2026-11-28 found zero .exe, .zip, or .dll files in
any commit on any branch. The "preserved binaries" the R26–R30
acceptance work reused live in CI/test storage outside git, and
Actions artifacts expire. The owner's git mirrors — the independent,
zero-cost backup of this project — therefore held the full source
and the acceptance hashes, but not one copy of the program itself. A
recorded hash can prove a file genuine if you still have it; it
cannot bring the file back.

GitHub Releases attachments and Actions artifacts do **not** fix
this: both are stored outside git, next to the repository rather than
inside it, and a `git clone --mirror` fetch never sees them. An
artifact committed into `preserved-builds/` is a file inside a
commit, so every mirror fetch brings it down automatically and
`git fsck` verifies it with everything else.

Folder name: `preserved-builds/`, **not** `releases/` — the Visual
Studio `.gitignore` in this repository ignores any `[Rr]eleases/`
folder, which would silently swallow preserved builds.
`preserved-builds/` is not ignored (verified with `git check-ignore`
for .exe, .zip, and .dll).

## The rule

- Every accepted J2534Diag build is preserved in this repository, in
  the same acceptance that approves it, before the acceptance is
  closed. For CI builds, preserve the exact accepted package: download
  that run's artifact first, then run the script — the acceptance
  record's hash alone is not a preserved build.
- Preserved artifacts live at
  `preserved-builds/j2534diag/v<version>/<artifact>` with a
  `SHA256SUMS.txt` beside them, where `<version>` is the label the
  acceptance record uses (for example `R30`).
- The tree always holds at least the **last two** preserved versions.
  Older folders are removed from the tree when a newer one is
  preserved; they remain recoverable from git history.
- A preserved build is immutable: never overwrite one with different
  bytes under the same version.
- A branch carrying a preserve commit is not deleted until that
  commit has reached `master`.
- Preserve commits carry `[skip ci]` and never trigger an Actions
  run.

## How

Wherever the accepted artifact exists, from a checkout of this
repository, as the final acceptance step:

```text
python tools/preserve_build.py <path-to-accepted-artifact> \
    --product j2534diag --version <label from the acceptance record> \
    --expected-sha256 <sha256 from the acceptance record> --push
```

The script verifies the hash, copies the artifact into place, writes
`SHA256SUMS.txt`, rotates out the oldest version beyond the last
two, commits, and (with `--push`) pushes. See the script header for
all options, including `--dry-run`.

## Status at adoption (2026-11-28)

- Preserved: none. No build of J2534Diag.exe has ever been committed.
- Backfill, in order: the newest accepted build on the
  `feature/native-gui-capture-preview` line (the R30 build, whose
  executable hash is recorded in that line's acceptance records),
  then the prior accepted build (R29 package), restoring last-two
  depth. Use the exact accepted CI packages, verified against their
  recorded hashes — do not preserve a fresh rebuild under an old
  version label.

## Verification on the receiving side

The owner's mirror refresh (Muse's machine) audits preservation on
every refresh: it lists the preserved builds under
`preserved-builds/`, compares them with the newest acceptance
records, and reports any accepted build that has no preserve commit.


## November 28, 2026 Eastern — accepted R30/R29 package backfill verified on master

The new gate from merged PR30 is satisfied for the last two accepted packages. Work was performed on local **master**, fast-forwarded to merged gate fa5d2902c70b84b5366e00e8799e1cc3cac80897; no development/feature branch carries the only remote copy.

|Order|Version|Master preservation commit|Package bytes|Package SHA256|
|---|---|---|---|---|
|1|R30|[876923cce5a6b7147bebcb67294f7a0d474d6035](https://github.com/bmcleid/J2534Diag-Independent/commit/876923cce5a6b7147bebcb67294f7a0d474d6035)|1901701|fb0f81ff5549ddab9796643e5e45fabb53f02de17af3ff1c72d3fca64a6ec015|
|2|R29|[af83c0188d097885fa2501b3e54023a5c8f240e5](https://github.com/bmcleid/J2534Diag-Independent/commit/af83c0188d097885fa2501b3e54023a5c8f240e5)|1897018|0fdad6d5e480bbe885b21ffc15bf7680fdf78791b29f7fcd6392bde470b644f1|

Exact accepted packages recovered from Librarylibfile_4f18c58c0de88191a8770ff153f8c657 (R30) andlibfile_f65b05c6c35081918a33db0b8e058fe5 (R29). R29 acceptance record confirms the ZIP identity; its draft-release/package-recovery history is unchanged. No fresh build was substituted.

**Actual script accounting:** ran the unmodified master tools/preserve_build.py with --product j2534diag, --version R30 then R29, the exact expected ZIP hashes, and --push. Both hash gates passed and each script copied the ZIP, generated SHA256SUMS.txt and committed locally. Both CLI pushes failed because this executor has no command-line GitHub credentials. Authenticated Git Data publication then created the exact same binary blobs, checksum blobs and entire tree as each script-generated commit, with identical preserve messages. Guarded non-force ref updates placed them directly on master. Remote commit metadata/SHAs differ from local script commits ef7d0cd1a363eddcade684de3f8a8fb8bfbe7251 and3b2524a098288b0006cb4c724117f3e44e04a2a6; report/use the two remote SHAs above. This was an authentication fallback, never a hash-mismatch bypass.

Remote Git fetch/readback independently verified exact ZIP bytes/SHA256, checksum-file text, ZIP CRC and embedded EXE SHA256: R30c3a9fe6c3f58e1b35b636538d58e5fa2e55170d16cc5240283cc75932877d30b; R29c4a9b9cf8602d9d3dfcda0cadb29792302e21ea4b62c9f2a7362fff3f126ad0f. R29 commit's parent is the R30 preserve commit. Final preserved-builds/j2534diag tree contains exactly vR29 andvR30, each with its ZIP andSHA256SUMS.txt. Mirrors can recover these blobs through Git; no mirror refresh on another machine is claimed.

No rebuild, vehicle/provider operation, acceptance rerun or PR29 merge. This completes binary-preservation backfill, not the remaining scanner functional qualification.


## November 28, 2026 — R31 scoped offline acceptance preserved

Exact tested R31 package (1912701bytes, SHA256 `3331300ff707b3c64e27f5edc662486c15c5f92827d94693f4b59a8f2ebfb267`; EXE `1e88570626de244c7ae1154c4f8c591c4497b35b4807e4a20ffe1bb41c7b5841`) is now on master at `acc2413b9191753e37fa92d831433d3ab9d3eb16`. One focused Windows run46170048742 passed102 checks with zero handles; actual UI and package inspected. This is offline software acceptance; R30 remains owner-PC fallback, physical installed-SRX qualification remains separate.

Unmodified preserve_build.py with --push passed hash/copy/checksum/rotation/local commit18d000f; CLI push failed absent credentials. Authenticated Git Data API published identical ZIP blob2725f41bf450b3867f6157c23e60ba1697d79a62 and whole script-generated treedcd8196339dc9bb7297057aba484d612aec1eab1, preserve message and guarded non-force master update. No substitute build/hash bypass. Remote Git ZIP/EXE hash and CRC readback pass. The tree now holds exactly vR30/vR31 ZIPs/checksums; R29 rotated only from the tree, retained in history at af83c0188d097885fa2501b3e54023a5c8f240e5 and independently rehashed. Runtime feature PR29 remains Draft/unmerged; no mirror refresh claimed. Full owner acceptance/receipt is on feature/native-gui-capture-preview; use the explicit [owning-branch link](https://github.com/bmcleid/J2534Diag-Independent/blob/feature/native-gui-capture-preview/docs/R31_MODE06_DISCOVERY_2026-11-28.md).
