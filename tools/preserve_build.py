#!/usr/bin/env python3
"""Preserve an accepted Windows build artifact in this repository.

Owner rule (2026-11-28, see AGENTS.md "Accepted build preservation
gate"): an accepted build is not closed until the artifact that was
actually accepted -- the .exe or its release .zip -- is committed
under preserved-builds/<product>/v<version>/. The preserved-builds/
tree always holds at least the last two preserved versions per
product. GitHub Releases assets and Actions artifacts do NOT count:
they live outside git and are never picked up by the owner's mirrors.

Why preserved-builds/ and not releases/: the standard Visual Studio
.gitignore in this repository ignores any [Rr]eleases/ folder, which
would silently swallow preserved builds. preserved-builds/ is not
ignored (verified with git check-ignore).

Run from a checkout of this repository, wherever the accepted
artifact exists (the build machine, or a machine that downloaded the
exact accepted CI package):

    python tools/preserve_build.py path/to/artifact.zip \
        --product oneapp --version Preview11 \
        --expected-sha256 <sha256 from the acceptance record>

What it does, in order:
  1. Computes the artifact SHA-256 and refuses to continue if it
     does not match --expected-sha256 (when given). An acceptance
     record hash is the identity of the accepted build; preserving
     different bytes under its version is worse than none.
  2. Copies the artifact to preserved-builds/<product>/v<version>/
     and writes SHA256SUMS.txt beside it (sha256sum format). Refuses
     to overwrite a preserved artifact whose hash differs -- a
     preserved build is immutable.
  3. Rotates: removes the oldest preserved version folders for that
     product so exactly --keep (default 2) newest remain in the
     tree. Removed versions stay recoverable from git history.
     Version ordering is a natural sort (R29 < R30, Preview9 <
     Preview11, 0.9 < 0.12).
  4. Stages and commits as
     "release: preserve <product> v<version> build [skip ci]"
     ([skip ci] keeps the preserve commit from triggering an Actions
     run). With --push it also pushes the current branch.

Stdlib only. No network use except the optional git push.
"""

import argparse
import hashlib
import re
import shutil
import subprocess
import sys
from pathlib import Path


def fail(msg):
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def natural_key(name):
    # "vPreview11" / "vR30" / "v0.12.31" -> chunk list so 9 < 11, 29 < 30
    parts = re.split(r"(\d+)", name)
    return [int(p) if p.isdigit() else p.lower() for p in parts]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("artifact", help="Path to the accepted artifact (.exe or release .zip)")
    ap.add_argument("--product", required=True, help="Product slug, e.g. oneapp or j2534diag (folder under preserved-builds/)")
    ap.add_argument("--version", required=True, help="Version label as recorded in the acceptance record, e.g. Preview11, R30, 0.12.33")
    ap.add_argument("--expected-sha256", help="SHA-256 recorded in the acceptance record; preserve refuses on mismatch")
    ap.add_argument("--keep", type=int, default=2, help="Preserved versions per product to keep in the tree (default: 2)")
    ap.add_argument("--push", action="store_true", help="git push the current branch after committing")
    ap.add_argument("--dry-run", action="store_true", help="Verify and report only; copy nothing, commit nothing")
    args = ap.parse_args()

    if args.keep < 2:
        fail("--keep must be at least 2 (owner rule: the last two builds stay in the repo)")
    if not re.fullmatch(r"[A-Za-z0-9._-]+", args.product):
        fail(f"--product must be a plain folder name, got {args.product!r}")
    version = args.version.lstrip("v")
    if not version or "/" in version or "\\" in version:
        fail(f"--version must be a plain label, got {args.version!r}")

    artifact = Path(args.artifact).resolve()
    if not artifact.is_file():
        fail(f"artifact not found: {artifact}")
    if artifact.suffix.lower() not in (".exe", ".zip"):
        fail(f"artifact should be an .exe or a release .zip, got {artifact.name!r}")

    top = run(["git", "rev-parse", "--show-toplevel"])
    if top.returncode != 0:
        fail("not inside a git checkout of this repository")
    repo = Path(top.stdout.strip())

    digest = sha256_of(artifact)
    print(f"Artifact: {artifact} ({artifact.stat().st_size} bytes)")
    print(f"SHA-256:  {digest}")
    if args.expected_sha256:
        if digest.lower() != args.expected_sha256.lower():
            fail(
                "SHA-256 does not match the acceptance record:\n"
                f"  file:   {digest}\n"
                f"  record: {args.expected_sha256.lower()}\n"
                "Refusing to preserve the wrong bytes."
            )
        print("SHA-256 matches the acceptance record.")

    product_dir = repo / "preserved-builds" / args.product
    dest_dir = product_dir / f"v{version}"
    dest_file = dest_dir / artifact.name
    sums_file = dest_dir / "SHA256SUMS.txt"

    if dest_file.exists() and sha256_of(dest_file) != digest:
        fail(f"{dest_file} already exists with a DIFFERENT hash. A preserved build is immutable; refusing to overwrite.")
    if args.dry_run:
        print(f"[dry-run] would preserve to {dest_file}")
    else:
        dest_dir.mkdir(parents=True, exist_ok=True)
        if not dest_file.exists():
            shutil.copy2(artifact, dest_file)
        sums_file.write_text(f"{digest}  {artifact.name}\n", encoding="utf-8")
        print(f"Preserved: {dest_file}")

    existing = []
    if product_dir.is_dir():
        for child in product_dir.iterdir():
            if child.is_dir() and child.name.startswith("v"):
                existing.append(child)
    if dest_dir not in existing:
        existing.append(dest_dir)
    existing.sort(key=lambda p: natural_key(p.name))
    excess = existing[:-args.keep] if len(existing) > args.keep else []
    for old_dir in excess:
        if args.dry_run:
            print(f"[dry-run] would rotate out {old_dir} (stays in git history)")
        else:
            res = run(["git", "-C", str(repo), "rm", "-r", "-q", "--ignore-unmatch", str(old_dir.relative_to(repo))])
            if res.returncode != 0 or old_dir.exists():
                shutil.rmtree(old_dir, ignore_errors=True)
            print(f"Rotated out: {old_dir.name} (recoverable from git history)")

    if args.dry_run:
        print("[dry-run] done; nothing copied, staged, or committed.")
        return

    run(["git", "-C", str(repo), "add", "-A", "-f", "preserved-builds"])
    staged = run(["git", "-C", str(repo), "diff", "--cached", "--name-only"]).stdout.strip()
    if not staged:
        print("Nothing to commit: this build is already preserved and rotation is up to date.")
        return
    msg = f"release: preserve {args.product} v{version} build [skip ci]"
    res = run(["git", "-C", str(repo), "commit", "-q", "-m", msg])
    if res.returncode != 0:
        fail(f"git commit failed:\n{res.stdout}\n{res.stderr}")
    sha = run(["git", "-C", str(repo), "rev-parse", "--short", "HEAD"]).stdout.strip()
    print(f"Committed: {sha} {msg}")
    if args.push:
        res = run(["git", "-C", str(repo), "push"])
        if res.returncode != 0:
            fail(f"git push failed:\n{res.stdout}\n{res.stderr}")
        print("Pushed.")
    else:
        print("Not pushed. Run `git push` so the mirrors pick this up -- an unpushed preserve commit backs up nothing.")


if __name__ == "__main__":
    main()
