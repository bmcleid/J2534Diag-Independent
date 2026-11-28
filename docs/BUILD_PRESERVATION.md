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
