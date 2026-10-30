#!/usr/bin/env bash
# Adds a channel of packages to an apt repository.
#
# Adding this repository once is the Linux counterpart of tapping a
# Homebrew formula: from then on "apt upgrade" carries every new build, so
# nobody has to download a file and remember which one they installed.
#
# Release, alpha, and beta are independent channels. Historical stable and
# develop directories remain aliases for release and alpha during migration.
#
# The repository is unsigned, and the sources line says so with
# "[trusted=yes]". Signing would mean a key that has to live somewhere and
# be rotated; for a personal alpha served over HTTPS from GitHub Pages, the
# transport is already doing the work a signature would.
#
# Usage: build-apt-repo.sh <package-dir> <output-dir> <site-url> [suite] [--initialize]
#   package-dir  Directory holding the .deb files, searched recursively
#   output-dir   Repository tree to write into, kept if it already exists
#   site-url     Public address of the repository, for the instructions page
#   suite        Canonical stage or a compatibility alias. Defaults to release.
set -euo pipefail

package_dir="${1:?directory of .deb files}"
output_dir="${2:?output directory}"
site_url="${3:?public site url}"
suite="${4:-release}"
initialize="${5:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$initialize" != "" && "$initialize" != --initialize ]]; then
    echo "Unknown option: $initialize" >&2
    exit 1
fi

architectures="amd64 arm64"
component=main

case "$suite" in
    release | alpha | beta | stable | develop) ;;
    *) echo "Unknown channel: $suite" >&2; exit 1 ;;
esac

# Validate incoming files before changing the existing repository.
incoming=()
while IFS= read -r -d '' package; do
    if [[ "$(dpkg-deb -f "$package" Package)" != reprise ]]; then
        echo "Not a Reprise package: $package" >&2
        exit 1
    fi
    case "$(dpkg-deb -f "$package" Architecture)" in
        amd64 | arm64) incoming+=("$package") ;;
        *) echo "Unsupported package architecture: $package" >&2; exit 1 ;;
    esac
done < <(find "$package_dir" -name '*.deb' -type f -print0)

if [[ "${#incoming[@]}" -eq 0 && "$initialize" != --initialize ]]; then
    echo "No .deb files found under $package_dir" >&2
    exit 1
fi

if [[ "${#incoming[@]}" -gt 0 ]]; then
    python3 "$here/update_channels.py" --expect "$suite" "${incoming[@]}" > /dev/null
fi

# Existing packages must also belong to this stage before they enter an index.
for package in "$output_dir/pool/$suite/$component/r/reprise/"*.deb; do
    if [[ -f "$package" ]]; then
        python3 "$here/update_channels.py" --expect "$suite" "$package" > /dev/null
    fi
done

# A published filename is immutable even if someone reuses a build number.
for package in "${incoming[@]}"; do
    target="$output_dir/pool/$suite/$component/r/reprise/$(basename "$package")"
    if [[ -f "$target" ]] && ! cmp -s "$package" "$target"; then
        echo "A different package is already published as $(basename "$package")" >&2
        exit 1
    fi
done

# Retain packages for other architectures and clients using a cached index.
# dpkg-scanpackages selects the newest version of each architecture below.
rm -rf "$output_dir/dists/$suite"
install -d "$output_dir/pool/$suite/$component/r/reprise"
if [[ "${#incoming[@]}" -gt 0 ]]; then
    cp "${incoming[@]}" "$output_dir/pool/$suite/$component/r/reprise/"
fi

cd "$output_dir"
for architecture in $architectures; do
    install -d "dists/$suite/$component/binary-$architecture"
    dpkg-scanpackages --arch "$architecture" "pool/$suite" 2>/dev/null \
        > "dists/$suite/$component/binary-$architecture/Packages"
    gzip -9nc "dists/$suite/$component/binary-$architecture/Packages" \
        > "dists/$suite/$component/binary-$architecture/Packages.gz"
done

apt-ftparchive \
    -o "APT::FTPArchive::Release::Origin=Reprise" \
    -o "APT::FTPArchive::Release::Label=Reprise" \
    -o "APT::FTPArchive::Release::Suite=$suite" \
    -o "APT::FTPArchive::Release::Codename=$suite" \
    -o "APT::FTPArchive::Release::Architectures=$architectures" \
    -o "APT::FTPArchive::Release::Components=$component" \
    release "dists/$suite" > "dists/$suite/Release"

# Pages will not serve a directory whose name starts with an underscore, and
# Jekyll would otherwise rewrite what it thinks are pages.
touch .nojekyll

cat > index.html <<PAGE
<!doctype html>
<meta charset="utf-8">
<title>Reprise for Linux</title>
<style>
  body { font: 15px/1.6 system-ui, sans-serif; margin: 3rem auto; max-width: 44rem; padding: 0 1.5rem; }
  code, pre { font-family: ui-monospace, monospace; }
  pre { background: #f4f4f6; padding: 1rem; border-radius: 8px; overflow-x: auto; }
  h1 { font-size: 1.6rem; }
  h2 { font-size: 1.1rem; margin-top: 2rem; }
  p.note { color: #555; }
</style>
<h1>Reprise for Linux</h1>
<p>A menu bar music controller for MPRIS players. Add one channel, and every
   later build in it arrives with your usual updates.</p>
<h2>Release</h2>
<p class="note">Stable releases. Alpha and beta builds are excluded.</p>
<pre>echo "deb [trusted=yes] $site_url release main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update
sudo apt install reprise</pre>
<h2>Beta</h2>
<p class="note">Preview builds from the beta stage only.</p>
<pre>echo "deb [trusted=yes] $site_url beta main" | sudo tee /etc/apt/sources.list.d/reprise-beta.list
sudo apt update
sudo apt install reprise</pre>
<h2>Alpha</h2>
<p class="note">Development builds from the alpha stage only.</p>
<pre>echo "deb [trusted=yes] $site_url alpha main" | sudo tee /etc/apt/sources.list.d/reprise-alpha.list
sudo apt update
sudo apt install reprise</pre>
<h2>Update</h2>
<pre>sudo apt update &amp;&amp; sudo apt upgrade</pre>
<h2>Remove</h2>
<pre>sudo apt remove reprise
sudo rm /etc/apt/sources.list.d/reprise*.list</pre>
<p class="note">Packages are unsigned, which is what <code>[trusted=yes]</code>
   allows; they are served over HTTPS from GitHub Pages. An AppImage sits
   beside this page for running Reprise without installing anything.</p>
PAGE
