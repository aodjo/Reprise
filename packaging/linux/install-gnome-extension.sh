#!/usr/bin/env bash
# Installs and switches on the Reprise GNOME Shell extension.
#
# Only needed for the archive build; the Debian package does this itself,
# and so does Reprise when it starts. The extension draws Reprise's entry in
# the top bar, which lets the track title scroll smoothly rather than
# stepping a character at a time as a plain tray label must.
set -euo pipefail

uuid="reprise@junx.dev"
source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/gnome-extension"
target_dir="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$uuid"

if [[ ! -d "$source_dir" ]]; then
    echo "Cannot find the extension next to this script." >&2
    exit 1
fi

mkdir -p "$target_dir"
cp "$source_dir"/metadata.json "$source_dir"/extension.js "$target_dir/"
echo "Installed to $target_dir"

if command -v gnome-extensions >/dev/null 2>&1 && gnome-extensions enable "$uuid" 2>/dev/null; then
    echo "Enabled."
    exit 0
fi

echo "The shell has not seen the extension yet."
echo "Log out and back in, or press Alt+F2 and type r on Xorg, then start Reprise."
