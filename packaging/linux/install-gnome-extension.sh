#!/usr/bin/env bash
# Installs and switches on the Reprise GNOME Shell extension.
#
# Bundled with every published application. Debian calls this with --system
# for logged-in users; Reprise also checks the bundle whenever it starts.
set -euo pipefail

# Identifier shared by the package and the application.
uuid="reprise@junx.dev"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_dir="$here/gnome-extension"
target_dir="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$uuid"
system_install=false
changed=false

if [[ "${1:-}" == "--system" ]]; then
    system_install=true
elif [[ -n "${1:-}" ]]; then
    echo "Usage: $0 [--system]" >&2
    exit 1
fi

# The archive also carries this helper one level above app/.
if [[ ! -d "$source_dir" && -d "$here/app/gnome-extension" ]]; then
    source_dir="$here/app/gnome-extension"
fi

if [[ ! -f "$source_dir/metadata.json" || ! -f "$source_dir/extension.js" ]]; then
    echo "Cannot find the extension next to this script." >&2
    exit 1
fi

# A package has already installed its system copy. Only replace user copies
# that would shadow it; archives and AppImages need a per-user installation.
if [[ "$system_install" == false || -d "$target_dir" ]]; then
    mkdir -p "$target_dir"
    for file in metadata.json extension.js; do
        if ! cmp -s "$source_dir/$file" "$target_dir/$file"; then
            cp "$source_dir/$file" "$target_dir/$file"
            changed=true
        fi
    done
fi

if command -v gnome-extensions >/dev/null 2>&1 && gnome-extensions enable "$uuid" 2>/dev/null; then
    if [[ "$changed" == true ]]; then
        echo "Reprise: GNOME extension installed or updated. Log out and back in to load the new code."
    else
        echo "Reprise: GNOME extension enabled."
    fi
    exit 0
fi

# GNOME may not know a newly installed extension until the next session.
# Remember the request now so that no manual enable command is needed then.
if command -v gsettings >/dev/null 2>&1; then
    enabled="$(gsettings get org.gnome.shell enabled-extensions 2>/dev/null || true)"
    enabled="${enabled#@as }"
    case "$enabled" in
        *"'$uuid'"*) ;;
        '[]') gsettings set org.gnome.shell enabled-extensions "['$uuid']" 2>/dev/null || true ;;
        \[*\]) gsettings set org.gnome.shell enabled-extensions "${enabled%]}, '$uuid']" 2>/dev/null || true ;;
    esac
fi

echo "Reprise: GNOME extension is bundled and ready. Log out and back in, then start Reprise."
