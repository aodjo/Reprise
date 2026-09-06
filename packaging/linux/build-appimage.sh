#!/usr/bin/env bash
# Builds an AppImage: one file the user marks executable and runs.
#
# Nothing is installed and no package manager is involved, which suits
# trying a build out. Reprise still finds its shell extension, because it
# looks beside its own executable and copies it into the user's extension
# directory on first run.
#
# Needs curl and mksquashfs, both of which the Debian and Ubuntu images
# used for packaging already have or can install.
#
# Usage: build-appimage.sh <published-app-dir> <arch> <version> <output-dir>
#   published-app-dir  Output of "dotnet publish --self-contained"
#   arch               AppImage architecture: x86_64 or aarch64
#   version            Version shown in the file name
#   output-dir         Where the .AppImage is written
set -euo pipefail

app_dir="${1:?published app directory}"
arch="${2:?appimage architecture}"
version="${3:?version}"
output_dir="${4:?output directory}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
appdir="$staging/Reprise.AppDir"

install -d "$appdir/usr/bin" "$appdir/usr/share/applications" \
    "$appdir/usr/share/icons/hicolor/128x128/apps"

cp -r "$app_dir" "$appdir/usr/bin/app"
chmod +x "$appdir/usr/bin/app/reprise"
cp -r "$here/gnome-extension" "$appdir/usr/bin/gnome-extension"

cat > "$appdir/AppRun" <<'LAUNCHER'
#!/bin/sh
# Runs Reprise from wherever the AppImage was mounted.
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/bin/app/reprise" "$@"
LAUNCHER
chmod +x "$appdir/AppRun"

cat > "$appdir/dev.junx.Reprise.desktop" <<'ENTRY'
[Desktop Entry]
Type=Application
Name=Reprise
Comment=Control MPRIS-compatible music players from one panel
Exec=AppRun
Icon=dev.junx.Reprise
Terminal=false
Categories=AudioVideo;Audio;Player;
StartupNotify=true
ENTRY
cp "$appdir/dev.junx.Reprise.desktop" "$appdir/usr/share/applications/"
cp "$root/BrowserExtension/Chromium/icons/icon128.png" "$appdir/dev.junx.Reprise.png"
cp "$appdir/dev.junx.Reprise.png" "$appdir/usr/share/icons/hicolor/128x128/apps/"

# An AppImage is its runtime followed by a squashfs of the AppDir, so it is
# assembled here rather than with appimagetool: the tool is itself an
# AppImage for the target architecture, which a cross build cannot run.
runtime="$staging/runtime"
curl -fsSL -o "$runtime" \
    "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$arch"

image="$staging/image.squashfs"
mksquashfs "$appdir" "$image" -root-owned -noappend -no-progress -quiet -comp zstd

mkdir -p "$output_dir"
output="$output_dir/Reprise-$version-$arch.AppImage"
cat "$runtime" "$image" > "$output"
chmod +x "$output"
