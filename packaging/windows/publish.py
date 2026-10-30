#!/usr/bin/env python3
"""Publish verified Windows installers to isolated architecture and release-stage feeds."""

import argparse
import hashlib
import html
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import re


# Source checkout and canonical publication destination.
ROOT = Path(__file__).resolve().parents[2]
REPOSITORY = "aodjo/Reprise"
SITE = "https://aodjo.github.io/Reprise"
sys.path.insert(0, str(ROOT / "packaging/linux"))
from update_channels import CHANNELS, for_version


def version_order(version, build):
    """Order supported release versions before build timestamps, matching the application updater."""
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta)(?:\.(\d+(?:\.\d+)*))?)?(?:\+[^ ]+)?", version)
    if not match:
        raise ValueError("Unsupported Windows package version.")
    major, minor, patch, stage, identifiers = match.groups()
    return (int(major), int(minor), int(patch), {"alpha": 0, "beta": 1, None: 2}[stage],
            tuple(int(part) for part in (identifiers or "").split(".") if part), int(build))


def run(arguments, cwd=ROOT, capture=False):
    """Run one command with literal arguments and no credential interpolation."""
    return subprocess.run(arguments, cwd=cwd, check=True, text=True,
                          stdout=subprocess.PIPE if capture else None).stdout


def checksum(path):
    """Read an artifact's SHA-256 without loading the complete installer into memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate(directory):
    """Verify stage, platform, payload architecture, installer size, digest, and immutable URL."""
    metadata = json.loads((directory / "update.json").read_text())
    channel = metadata["channel"]
    architecture = metadata["architecture"]
    if metadata["schemaVersion"] != 1 or metadata["platform"] != "windows" or channel not in CHANNELS:
        raise ValueError("Invalid Windows update manifest.")
    if for_version(metadata["version"]) != channel or architecture not in ("x64", "arm64", "x86"):
        raise ValueError("The package's release stage or architecture does not match its manifest.")
    build = metadata["buildNumber"]
    if len(build) != 14 or not build.isascii() or not build.isdigit():
        raise ValueError("Invalid build number.")
    version_order(metadata["version"], build)
    name = metadata["fileName"]
    if Path(name).name != name or not name.endswith("-Setup.exe"):
        raise ValueError("Invalid installer filename.")
    installer = directory / name
    expected = f"{SITE}/windows/{channel}/{architecture}/packages/{name}"
    if metadata["url"] != expected or installer.stat().st_size != metadata["size"] or checksum(installer) != metadata["sha256"]:
        raise ValueError("Installer address, size, or digest verification failed.")
    if installer.stat().st_size >= 100 * 1024 * 1024:
        raise ValueError("Installer exceeds the package repository's per-file limit.")
    with (directory / "app/Reprise.exe").open("rb") as stream:
        header = stream.read(64)
        if header[:2] != b"MZ":
            raise ValueError("Missing Windows application payload.")
        stream.seek(struct.unpack_from("<I", header, 60)[0])
        pe = stream.read(6)
    if pe[:4] != b"PE\0\0" or struct.unpack_from("<H", pe, 4)[0] != {"x64": 0x8664, "arm64": 0xAA64, "x86": 0x14C}[architecture]:
        raise ValueError("Application executable architecture does not match the update feed.")
    payload = json.loads((directory / "app/reprise-build.json").read_text())
    for key in ("platform", "channel", "architecture", "version", "buildNumber"):
        if payload[key] != metadata[key]:
            raise ValueError(f"Payload metadata mismatch: {key}")
    return metadata, installer


def write_index(site):
    """Generate download links only for real published Windows installers."""
    rows = []
    for channel in CHANNELS:
        for architecture in ("x64", "arm64", "x86"):
            path = site / "windows" / channel / architecture / "update.json"
            if not path.exists():
                continue
            metadata = json.loads(path.read_text())
            rows.append(f'<li><a href="{html.escape(metadata["url"], quote=True)}">Reprise {channel} — {architecture}</a> '
                        f'({html.escape(metadata["version"])})</li>')
    (site / "windows/index.html").write_text(
        '<!doctype html><meta charset="utf-8"><title>Reprise for Windows</title>'
        '<style>body{font:16px/1.7 system-ui;max-width:56rem;margin:3rem auto;padding:0 1rem}</style>'
        '<h1>Reprise for Windows</h1><p>Choose x64 for Intel/AMD or ARM64 for Windows on Arm.</p>'
        '<p>Release, beta, and alpha updates are isolated. These development installers are unsigned.</p><ul>'
        + "".join(rows) + '</ul><p>Install for your account, then use Settings → System info → Check for updates.</p>'
        '<p><a href="https://github.com/aodjo/Reprise/tree/apt-repo/windows">Archived installers</a></p>')


def sparse_paths(metadata):
    """Check out feed metadata and requested installers while leaving archived binaries in Git's existing tree."""
    return ["/windows/*/*/update.json", "/windows/index.html", "/.github/workflows/",
            *(f'/windows/{item["channel"]}/{item["architecture"]}/packages/{item["fileName"]}' for item in metadata)]


def publish(directories, dry_run):
    """Publish validated Windows artifacts without deleting prior installers from the artifact repository."""
    packages = [(directory, *validate(directory)) for directory in directories]
    staging = ROOT / "Artifacts/windows-publish"
    staging.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="publish-", dir=staging) as temporary:
        site = Path(temporary) / "site"
        run(["git", "clone", "--depth", "1", "--single-branch", "--filter=blob:none", "--sparse", "--branch", "apt-repo",
             f"https://github.com/{REPOSITORY}.git", str(site)])
        run(["git", "sparse-checkout", "set", "--no-cone", *sparse_paths([metadata for _, metadata, _ in packages])], cwd=site)
        changed = ["windows/index.html", ".github/workflows/apt-pages.yml"]
        for directory, metadata, installer in packages:
            feed = site / "windows" / metadata["channel"] / metadata["architecture"]
            previous = feed / "update.json"
            if previous.exists():
                old = json.loads(previous.read_text())
                if version_order(old["version"], old["buildNumber"]) > version_order(metadata["version"], metadata["buildNumber"]):
                    raise ValueError("A newer Windows build is already published.")
            target = feed / "packages" / installer.name
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists() and checksum(target) != metadata["sha256"]:
                raise ValueError("A different installer already exists under that immutable filename.")
            shutil.copy2(installer, target)
            shutil.copy2(directory / "update.json", previous)
            if checksum(target) != metadata["sha256"]:
                raise ValueError("Copied installer digest mismatch.")
            changed.extend([previous.relative_to(site).as_posix(), target.relative_to(site).as_posix()])
        write_index(site)
        workflows = site / ".github/workflows"
        workflows.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / ".github/workflows/apt-pages.yml", workflows / "apt-pages.yml")
        run(["git", "add", "--", *changed], cwd=site)
        run(["git", "diff", "--cached", "--stat"], cwd=site)
        if dry_run:
            print("Validated; nothing was pushed.")
            return
        changes = subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=site)
        if changes.returncode == 0:
            print("These Windows packages are already committed.")
            return
        if changes.returncode != 1:
            raise RuntimeError("Could not inspect staged publication.")
        for key in ("user.name", "user.email"):
            run(["git", "config", key, run(["git", "config", "--get", key], capture=True).strip()], cwd=site)
        run(["git", "commit", "-m", "chore: publish isolated Windows updates"], cwd=site)
        run(["git", "-c", "http.version=HTTP/1.1", "-c", "http.postBuffer=268435456", "-c", "pack.window=0",
             "-c", "credential.helper=", "-c", "credential.https://github.com.helper=!gh auth git-credential",
             "push", "origin", "HEAD:apt-repo"], cwd=site)
        print(f"Windows installers: {SITE}/windows/")


def main():
    """Publish built artifact directories after verification, optionally without changing the server."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", nargs="+", type=Path, help="Numbered directories produced by build.py")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    try:
        publish([path.resolve() for path in args.directories], args.dry_run)
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
