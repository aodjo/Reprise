#!/usr/bin/env bash
# Builds a Debian package around an already published Reprise application.
#
# The application is self-contained, so the package carries its own .NET
# runtime and depends only on the system libraries Avalonia links against.
# Run it inside a Debian or Ubuntu environment, which is where dpkg-deb
# lives; the CI workflow and the local Docker build both do.
#
# Usage: build-deb.sh <published-app-dir> <arch> <version> <output-dir>
#   published-app-dir  Output of "dotnet publish --self-contained"
#   arch               Debian architecture: amd64 or arm64
#   version            Package version, for example 2.0.0~alpha.1. A build
#                      stamp is appended so that two packages built from the
#                      same source still compare as newer and older: without
#                      it apt sees the same version and skips the install,
#                      leaving the previous build running.
#   output-dir         Where the .deb is written
set -euo pipefail

app_dir="${1:?published app directory}"
arch="${2:?debian architecture}"
version="${3:?package version}"
output_dir="${4:?output directory}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

version="$version+$(date -u +%Y%m%d%H%M%S)"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT

install -d "$staging/DEBIAN" \
    "$staging/opt/reprise" \
    "$staging/usr/bin" \
    "$staging/usr/share/applications" \
    "$staging/usr/share/gnome-shell/extensions/reprise@junx.dev" \
    "$staging/usr/share/icons/hicolor/128x128/apps" \
    "$staging/usr/share/doc/reprise"

cp -r "$app_dir" "$staging/opt/reprise/app"
chmod +x "$staging/opt/reprise/app/reprise"
ln -s /opt/reprise/app/reprise "$staging/usr/bin/reprise"

sed -e "s/@VERSION@/$version/" -e "s/@ARCH@/$arch/" "$here/deb/control.in" > "$staging/DEBIAN/control"
install -m 755 "$here/deb/postinst" "$staging/DEBIAN/postinst"
install -m 755 "$here/deb/prerm" "$staging/DEBIAN/prerm"
install -m 644 "$here/deb/reprise.desktop" "$staging/usr/share/applications/dev.junx.Reprise.desktop"
install -m 644 "$here/gnome-extension/metadata.json" "$here/gnome-extension/extension.js" \
    "$staging/usr/share/gnome-shell/extensions/reprise@junx.dev/"
install -m 644 "$root/BrowserExtension/Chromium/icons/icon128.png" \
    "$staging/usr/share/icons/hicolor/128x128/apps/dev.junx.Reprise.png"
install -m 644 "$here/README.md" "$staging/usr/share/doc/reprise/README.md"

mkdir -p "$output_dir"
dpkg-deb --root-owner-group --build "$staging" "$output_dir/reprise_${version}_${arch}.deb"
