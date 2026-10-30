#!/usr/bin/env python3
"""Publish built Debian packages to the app's update repository without changing the source checkout."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

from update_channels import CHANNELS, for_version, from_filename


# Repository owning both the update packages and the Pages deployment.
REPOSITORY = "aodjo/Reprise"
# Public root used by the application updater.
SITE_URL = "https://aodjo.github.io/Reprise"
# Source checkout containing the packaging scripts and ignored staging directory.
ROOT = Path(__file__).resolve().parents[2]


def run(arguments, *, cwd=ROOT, capture=False):
    """Run one command with literal arguments and stop on any failure."""
    return subprocess.run(
        arguments, cwd=cwd, check=True, text=True,
        stdout=subprocess.PIPE if capture else None,
    ).stdout


def checksum(path):
    """Compute a package's SHA-256 without loading the full file into memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_index(site, channel, packages):
    """Check that every incoming file is selected by its index with the correct size and digest."""
    selected = {}
    for architecture in ("amd64", "arm64"):
        index = site / "dists" / channel / "main" / f"binary-{architecture}" / "Packages"
        for stanza in index.read_text().strip().split("\n\n"):
            fields = dict(
                line.split(": ", 1) for line in stanza.splitlines()
                if ": " in line and not line.startswith(" ")
            )
            if fields.get("Package") != "reprise":
                continue
            if for_version(fields["Version"]) != channel:
                raise ValueError(f"Wrong release stage in {channel}: {fields['Version']}")
            name = Path(fields["Filename"]).name
            selected[name] = fields
            target = site / fields["Filename"]
            if target.stat().st_size != int(fields["Size"]) or checksum(target) != fields["SHA256"]:
                raise ValueError(f"Package index verification failed: {name}")
    for package in packages:
        fields = selected.get(package.name)
        if fields is None or checksum(package) != fields["SHA256"]:
            raise ValueError(f"Package is not the newest published version for its architecture: {package.name}")


def publish(packages, channel, dry_run):
    """Prepare an isolated site checkout, validate its indexes, and push only the package branch."""
    for package in packages:
        if not package.is_file() or package.suffix != ".deb":
            raise ValueError(f"Not a Debian package: {package}")
    stages = {from_filename(package) for package in packages}
    if len(stages) != 1:
        raise ValueError("Packages from different release stages cannot be published together.")
    detected = stages.pop()
    if channel is not None and channel != detected:
        raise ValueError(f"Cannot publish {detected} packages to the {channel} channel.")
    channel = detected
    staging_root = ROOT / "Artifacts" / "linux-publish"
    staging_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="publish-", dir=staging_root) as temporary:
        staging = Path(temporary)
        site = staging / "site"
        incoming = staging / "incoming"
        incoming.mkdir()
        run(["git", "clone", "--depth", "1", "--single-branch", "--branch", "apt-repo",
             f"https://github.com/{REPOSITORY}.git", str(site)])
        for package in packages:
            shutil.copy2(package, incoming / package.name)

        print("Building and validating the update repository…", flush=True)
        platform = run(["docker", "version", "--format", "{{.Server.Os}}/{{.Server.Arch}}"], capture=True).strip()
        run([
            "docker", "run", "--rm", "--platform", platform, "-e", "DEBIAN_FRONTEND=noninteractive",
            "-v", f"{ROOT}:/work", "-w", "/work", "ubuntu:24.04", "bash", "-c",
            'apt-get update -qq && apt-get install -y -qq --no-install-recommends dpkg-dev apt-utils python3 > /dev/null'
            ' && python3 packaging/linux/prepare-update-repo.py "$@"',
            "--", str(incoming.relative_to(ROOT)), str(site.relative_to(ROOT)), SITE_URL, channel,
        ])
        verify_index(site, channel, packages)
        workflows = site / ".github" / "workflows"
        workflows.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / ".github/workflows/apt-pages.yml", workflows / "apt-pages.yml")
        run(["git", "add", "--all"], cwd=site)
        run(["git", "diff", "--cached", "--stat"], cwd=site)
        if dry_run:
            print("Validated; nothing was pushed.")
            return

        policy = json.loads(run([
            "gh", "api", f"repos/{REPOSITORY}/environments/github-pages/deployment-branch-policies",
        ], capture=True))
        if not any(item.get("name") == "apt-repo" and item.get("type") == "branch"
                   for item in policy["branch_policies"]):
            raise ValueError("Allow the apt-repo branch in the github-pages environment before publishing.")
        changes = subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=site)
        if changes.returncode == 0:
            print("The same packages are already committed.")
            return
        if changes.returncode != 1:
            raise RuntimeError("Could not inspect the prepared repository.")
        for key in ("user.name", "user.email"):
            value = run(["git", "config", "--get", key], capture=True).strip()
            run(["git", "config", key, value], cwd=site)
        run(["git", "commit", "-m", f"chore: publish {channel} Linux packages"], cwd=site)
        commit = run(["git", "rev-parse", "HEAD"], cwd=site, capture=True).strip()
        run(["git", "-c", "credential.helper=", "-c",
             "credential.https://github.com.helper=!gh auth git-credential",
             "push", "origin", "HEAD:apt-repo"], cwd=site)
        print(f"Published commit: {commit}")
        print(f"Pages deployment queued: https://github.com/{REPOSITORY}/actions")
        print(f"Update index: {SITE_URL}/dists/{channel}/main/binary-amd64/Packages")


def main():
    """Publish explicitly selected packages to the requested update channel."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="+", type=Path, help="Debian packages produced by build-local.sh")
    parser.add_argument("--channel", choices=CHANNELS, help="Assert the package's release stage; defaults to its version")
    parser.add_argument("--dry-run", action="store_true", help="Prepare and validate without committing or pushing")
    args = parser.parse_args()
    try:
        publish([path.expanduser().resolve() for path in args.packages], args.channel, args.dry_run)
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
