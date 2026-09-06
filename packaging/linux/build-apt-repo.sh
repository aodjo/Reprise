#!/usr/bin/env bash
# Adds a channel of packages to an apt repository.
#
# Adding this repository once is the Linux counterpart of tapping a
# Homebrew formula: from then on "apt upgrade" carries every new build, so
# nobody has to download a file and remember which one they installed.
#
# There are two channels, and a machine follows whichever it subscribed to:
# "stable" comes from the main branch, "develop" from the development one.
# Each run rewrites only its own channel, so publishing a development build
# cannot disturb what stable users are offered.
#
# The repository is unsigned, and the sources line says so with
# "[trusted=yes]". Signing would mean a key that has to live somewhere and
# be rotated; for a personal alpha served over HTTPS from GitHub Pages, the
# transport is already doing the work a signature would.
#
# Usage: build-apt-repo.sh <package-dir> <output-dir> <site-url> [suite]
#   package-dir  Directory holding the .deb files, searched recursively
#   output-dir   Repository tree to write into, kept if it already exists
#   site-url     Public address of the repository, for the instructions page
#   suite        Channel to update: stable or develop. Defaults to stable.
set -euo pipefail

package_dir="${1:?directory of .deb files}"
output_dir="${2:?output directory}"
site_url="${3:?public site url}"
suite="${4:-stable}"

architectures="amd64 arm64"
component=main

case "$suite" in
    stable | develop) ;;
    *) echo "Unknown channel: $suite" >&2; exit 1 ;;
esac

# Only this channel is rebuilt; anything else already published stays.
rm -rf "$output_dir/dists/$suite" "$output_dir/pool/$suite"
install -d "$output_dir/pool/$suite/$component/r/reprise"
find "$package_dir" -name '*.deb' -exec cp {} "$output_dir/pool/$suite/$component/r/reprise/" \;

if ! ls "$output_dir/pool/$suite/$component/r/reprise/"*.deb >/dev/null 2>&1; then
    echo "No .deb files found under $package_dir" >&2
    exit 1
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
<h2>Stable</h2>
<p class="note">Built from the main branch.</p>
<pre>echo "deb [trusted=yes] $site_url stable main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update
sudo apt install reprise</pre>
<h2>Development</h2>
<p class="note">Built from the development branch, updated far more often.</p>
<pre>echo "deb [trusted=yes] $site_url develop main" | sudo tee /etc/apt/sources.list.d/reprise-develop.list
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
