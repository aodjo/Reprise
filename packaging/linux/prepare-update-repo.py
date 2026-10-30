#!/usr/bin/env python3
"""Prepare isolated release-stage indexes and safe aliases for existing updaters."""

import argparse
import filecmp
from pathlib import Path
import shutil
import subprocess

from update_channels import ALIASES, CHANNELS, canonicalize, package_channel


# Repository generator next to this migration helper.
BUILDER = Path(__file__).resolve().with_name("build-apt-repo.sh")


def import_legacy(site, legacy, canonical):
    """Preserve compatible published packages when introducing a canonical stage directory."""
    source = site / "pool" / legacy / "main/r/reprise"
    target = site / "pool" / canonical / "main/r/reprise"
    changed = False
    for package in source.glob("*.deb"):
        if package_channel(package) != canonical:
            raise ValueError(f"Wrong release stage in {legacy}: {package.name}")
        target.mkdir(parents=True, exist_ok=True)
        destination = target / package.name
        if destination.exists():
            if not filecmp.cmp(package, destination, shallow=False):
                raise ValueError(f"Conflicting published package: {package.name}")
        else:
            shutil.copy2(package, destination)
            changed = True
    return changed


def build(packages, site, url, channel):
    """Generate a real index, allowing an intentionally empty release stage."""
    subprocess.run(["bash", str(BUILDER), str(packages), str(site), url, channel, "--initialize"], check=True)


def prepare(packages, site, url, channel):
    """Publish one stage and mirror alpha/release for historical develop/stable clients."""
    channel = canonicalize(channel)
    changed = {canonical: import_legacy(site, legacy, canonical) for legacy, canonical in ALIASES.items()}
    empty = site / ".empty-channel-input"
    empty.mkdir(exist_ok=True)
    try:
        for stage in CHANNELS:
            if stage == channel:
                build(packages, site, url, stage)
            elif changed.get(stage) or not (site / "dists" / stage / "Release").exists():
                build(empty, site, url, stage)
        for legacy, canonical in ALIASES.items():
            pool = site / "pool" / canonical / "main/r/reprise"
            build(pool, site, url, legacy)
    finally:
        empty.rmdir()


def main():
    """Prepare a staged repository without pushing or changing the live server."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", type=Path)
    parser.add_argument("site", type=Path)
    parser.add_argument("url")
    parser.add_argument("channel", choices=CHANNELS)
    args = parser.parse_args()
    try:
        prepare(args.packages.resolve(), args.site.resolve(), args.url, args.channel)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
