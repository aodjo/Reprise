#!/usr/bin/env bash
# Installs the Reprise GNOME Shell extension for the current user.
#
# The extension draws Reprise's entry in the top bar itself, so the track
# title scrolls smoothly rather than stepping a character at a time as a
# plain tray label must. Without it Reprise still appears in the tray.
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

if command -v gnome-extensions >/dev/null 2>&1; then
    gnome-extensions enable "$uuid" 2>/dev/null \
        && echo "Enabled." \
        || echo "Enable it with: gnome-extensions enable $uuid"
else
    echo "Enable it from the Extensions app, or: gnome-extensions enable $uuid"
fi

echo
echo "On Xorg press Alt+F2, type r, and press Enter to reload the shell."
echo "On Wayland log out and back in."
