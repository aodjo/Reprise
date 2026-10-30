#!/usr/bin/env python3
"""Copy Debian builds into VMShared/reprise/<build-number>/ directories."""

import argparse
import hashlib
from pathlib import Path
import re
import shutil


# The numeric suffix is assigned by build-deb.sh and is part of apt's version.
PACKAGE_NAME = re.compile(r"^reprise_.+\+([0-9]+)_(amd64|arm64)\.deb$")


def checksum(path):
    """Return a file's SHA-256 without loading the package into memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").digest()


def export_package(package, destination):
    """Copy a package under its build number and preserve existing build files."""
    match = PACKAGE_NAME.fullmatch(package.name)
    if match is None:
        raise ValueError(f"Cannot read build number from package name: {package.name}")
    if not package.is_file():
        raise FileNotFoundError(package)

    target = destination / match.group(1) / package.name
    source_checksum = checksum(package)
    if target.exists():
        if checksum(target) != source_checksum:
            raise FileExistsError(f"A different package already exists: {target}")
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(package, target)
        if checksum(target) != source_checksum:
            raise OSError(f"Copied package checksum mismatch: {target}")
    return target


def main():
    """Export the requested packages to the shared build archive."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="+", type=Path, help="Debian packages produced by build-deb.sh")
    parser.add_argument(
        "--directory", type=Path,
        default=Path.home() / "Documents" / "VMShared" / "reprise",
        help="Shared build archive (default: ~/Documents/VMShared/reprise)",
    )
    args = parser.parse_args()
    try:
        for package in args.packages:
            print(export_package(package, args.directory.expanduser()))
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
