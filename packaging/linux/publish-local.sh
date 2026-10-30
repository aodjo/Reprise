#!/usr/bin/env bash
# Builds and publishes a numbered Linux development update without VMShared.
# Usage: publish-local.sh [amd64|arm64]
set -euo pipefail

# Source checkout containing both the build and publication tools.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
architecture="${1:-amd64}"
case "$architecture" in
    amd64 | arm64) ;;
    *) echo "Usage: $0 [amd64|arm64]" >&2; exit 1 ;;
esac

# The same identifier names the package, app version, and local artifact folder.
export REPRISE_BUILD_NUMBER="${REPRISE_BUILD_NUMBER:-$(date -u +%Y%m%d%H%M%S)}"
bash "$root/packaging/linux/build-local.sh" "$architecture"
python3 "$root/packaging/linux/publish-local.py" \
    "$root/Artifacts/linux-$architecture/$REPRISE_BUILD_NUMBER"/reprise_*_"$architecture".deb
