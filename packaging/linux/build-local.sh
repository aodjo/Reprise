#!/usr/bin/env bash
# Publishes a numbered Debian build without changing the shared folder.
# Usage: build-local.sh [amd64|arm64] [--export-vmshared]
set -euo pipefail

# Repository containing the application and packaging tools.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
architecture="${1:-amd64}"
export_vmshared=false
if [[ "${2:-}" == "--export-vmshared" ]]; then
    export_vmshared=true
elif [[ "$#" -gt 1 ]]; then
    echo "Usage: $0 [amd64|arm64] [--export-vmshared]" >&2
    exit 1
fi
case "$architecture" in
    amd64) runtime=linux-x64 ;;
    arm64) runtime=linux-arm64 ;;
    *) echo "Usage: $0 [amd64|arm64] [--export-vmshared]" >&2; exit 1 ;;
esac

# One identifier is shared by the app version, Debian version, and folder.
export REPRISE_BUILD_NUMBER="${REPRISE_BUILD_NUMBER:-$(date -u +%Y%m%d%H%M%S)}"
case "$REPRISE_BUILD_NUMBER" in
    '' | *[!0-9]*) echo "Build number must contain digits only." >&2; exit 1 ;;
esac
cd "$root"
version="$(python3 - <<'PY'
import xml.etree.ElementTree as ET
root = ET.parse('Directory.Build.props').getroot()
prefix = root.findtext('.//VersionPrefix')
suffix = next(e.text or '' for e in root.findall('.//VersionSuffix') if not e.attrib)
print(f'{prefix}~{suffix}' if suffix else prefix)
PY
)"
output="Artifacts/linux-$architecture/$REPRISE_BUILD_NUMBER"

dotnet publish src/Reprise.Platform.Linux/Reprise.Platform.Linux.csproj \
    --configuration Release --runtime "$runtime" --self-contained true \
    -p:RepriseBuildNumber="$REPRISE_BUILD_NUMBER" --output "$output/app"

docker run --rm --platform "linux/$architecture" \
    -e REPRISE_BUILD_NUMBER -e DPKG_DEB_THREADS_MAX=4 \
    -v "$root:/work" -w /work ubuntu:24.04 \
    bash packaging/linux/build-deb.sh "$output/app" "$architecture" "$version" "$output"

if "$export_vmshared"; then
    for package in "$output"/reprise_*_"$architecture".deb; do
        python3 packaging/linux/export-vmshared.py "$package"
    done
fi
