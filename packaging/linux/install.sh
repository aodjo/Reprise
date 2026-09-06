#!/usr/bin/env bash
# Installs the Reprise package that matches this machine.
#
# The release carries one package per architecture, and apt refuses the
# wrong one with a wall of unsatisfiable dependencies rather than a plain
# "wrong architecture", so pick the right file here instead of leaving it
# to the reader.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v dpkg >/dev/null 2>&1; then
    echo "This installer is for Debian and Ubuntu. Use the .tar.gz archive instead." >&2
    exit 1
fi

architecture="$(dpkg --print-architecture)"
package="$(ls "$here"/reprise_*_"$architecture".deb 2>/dev/null | sort | tail -1 || true)"

if [[ -z "$package" ]]; then
    echo "No package here for $architecture. Available:" >&2
    ls "$here"/reprise_*.deb 2>/dev/null >&2 || echo "  (none)" >&2
    exit 1
fi

echo "Installing $(basename "$package") for $architecture."
if [[ "$(id -u)" -eq 0 ]]; then
    apt-get install -y "$package"
else
    sudo apt-get install -y "$package"
fi
