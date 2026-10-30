"""Classify versions and reject packages published to the wrong release stage."""

import argparse
from pathlib import Path
import re
import subprocess


# Physical legacy directories remain aliases for one release stage only.
ALIASES = {"stable": "release", "develop": "alpha"}
# Independently published application update channels.
CHANNELS = ("release", "alpha", "beta")


def canonicalize(channel):
    """Return a canonical stage for a supported channel or compatibility alias."""
    channel = ALIASES.get(channel, channel)
    if channel not in CHANNELS:
        raise ValueError(f"Unknown update channel: {channel}")
    return channel


def for_version(version):
    """Classify a semantic or Debian version and fail closed for unknown prereleases."""
    if not version or not version.strip():
        raise ValueError("An update version is required.")
    upstream = version.split("+", 1)[0]
    if "~" in upstream:
        suffix = upstream.split("~", 1)[1]
    elif "-" in upstream:
        suffix = upstream.split("-", 1)[1]
        if suffix and suffix[0].isascii() and suffix[0].isdigit():
            return "release"
    else:
        return "release"
    for channel in ("alpha", "beta"):
        if suffix == channel or suffix.startswith(channel + "."):
            return channel
    raise ValueError(f"Unsupported prerelease stage: {version}")


def from_filename(package):
    """Infer the declared channel from a build-local.sh filename before native validation."""
    match = re.fullmatch(r"reprise_(.+)_(amd64|arm64)\.deb", Path(package).name)
    if match is None:
        raise ValueError(f"Cannot read the version from package filename: {package}")
    return for_version(match.group(1))


def package_channel(package):
    """Read the actual Debian control version for publication validation."""
    result = subprocess.run(["dpkg-deb", "-f", str(package), "Version"],
                            check=True, capture_output=True, text=True)
    return for_version(result.stdout.strip())


def main():
    """Print one stage only when all selected packages belong to that stage."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="+", type=Path)
    parser.add_argument("--expect", help="Reject packages from another canonical stage or legacy alias")
    args = parser.parse_args()
    try:
        stages = {package_channel(package) for package in args.packages}
        if len(stages) != 1:
            raise ValueError("Packages from different release stages cannot be published together.")
        channel = stages.pop()
        if args.expect and channel != canonicalize(args.expect):
            raise ValueError(f"Cannot publish {channel} packages to the {args.expect} channel.")
        print(channel)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
