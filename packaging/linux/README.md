# Linux packaging

GitHub Actions publishes a Debian package, an AppImage, and a self-contained
archive for `linux-x64` and `linux-arm64`. All of them carry the whole
application, including its .NET runtime, the Pretendard typeface, and the
GNOME top bar extension. The extension is part of `dotnet publish` output,
so local publishes and all package formats include the same files.

## Publish a local update

Build AMD64 and publish it to the release stage declared by its version:

```bash
bash packaging/linux/publish-local.sh
```

This uses .NET, Docker, Python 3, Git, and an authenticated GitHub CLI with
repository and workflow access. It builds a numbered package, clones only
`apt-repo` into an isolated staging directory, updates the package indexes,
and pushes that package branch. The source checkout and its uncommitted work
are preserved. The **Apt Pages** workflow then deploys the update repository.
After that deployment succeeds, Reprise's **Check for updates** button can
find the new version and offer **Install**. No shared-folder copy is made.

The `github-pages` environment must allow deployments from the `apt-repo`
branch. Its existing `main` and `develop` deployment rules remain in place.
The publisher checks this before pushing. The deployment workflow is carried
on the package branch as well as in this source tree.

To publish an already built package instead:

```bash
python3 packaging/linux/publish-local.py Artifacts/linux-amd64/<build-number>/reprise_*_amd64.deb
```

Pass `--dry-run` to build and validate the repository without committing or
pushing. `--channel release`, `--channel alpha`, or `--channel beta` asserts
the expected stage; it cannot relabel a package from another stage. An AMD64
update preserves the existing ARM64 package and the other stages; prior package
files remain downloadable for clients with a cached index. A published
filename cannot be replaced with different bytes.

## Local builds and optional VM sharing

Local builds target `linux-x64` and use the Debian `amd64` architecture.
Build without publishing or copying to the shared folder:

```bash
bash packaging/linux/build-local.sh
```

This requires .NET, Docker, and Python 3 on the host. The build number is
included in the app's displayed version, the Debian package version, and
`Artifacts/linux-amd64/<build-number>/`. Pass `arm64` only for an ARM guest.
Unique versions do not work around App Center's local-package update bug
described below.

Shared-folder export is available only when explicitly requested:

```bash
bash packaging/linux/build-local.sh amd64 --export-vmshared
```

To export an existing package separately:

```bash
python3 packaging/linux/export-vmshared.py Artifacts/linux-amd64/<build-number>/reprise_*_amd64.deb
```

For example, a package whose version ends in `+20261028120006` is copied to
`~/Documents/VMShared/reprise/20261028120006/`. Existing build directories
are retained, and the copy is verified with SHA-256. Do not place loose
packages directly under `VMShared` or `VMShared/reprise`.

Use `--directory <path>` to select a different shared folder.

## Apt repository

Pushes publish packages as an apt repository on GitHub Pages. There are three
independent canonical channels:

| Version suffix in Directory.Build.props | Update channel | Packages offered |
| --- | --- | --- |
| Empty | `release` | Stable releases only |
| `alpha.1`, `alpha.2`, etc. | `alpha` | Alpha builds only |
| `beta.1`, `beta.2`, etc. | `beta` | Beta builds only |

The actual package version determines its stage, including in CI. A branch
name or publisher flag never promotes an alpha package to beta or release.
Unknown prerelease labels are rejected rather than being treated as release.
Publishing validates the native Debian control version before changing an
index, and the app independently rejects another stage even if its version
number is greater. Channels do not automatically promote users to another
stage.

```bash
echo "deb [trusted=yes] https://aodjo.github.io/Reprise release main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update && sudo apt install reprise
```

Choose `alpha` or `beta` instead to follow that stage. Subscribe to one
stage. An unpublished stage has an empty package index, not packages borrowed
from another stage. The site lives on `apt-repo`; publication preserves other
stages and prior download URLs.

Historical `develop` URLs remain alpha-only compatibility aliases so existing
apps can install the migration update automatically. Historical `stable`
URLs likewise alias release only. `prepare-update-repo.py` imports compatible
legacy files, initializes missing canonical indexes, and maintains those
aliases. New app builds read `alpha`, `beta`, or `release` directly.

`build-apt-repo.sh` assembles that tree and can be run locally against a
directory of `.deb` files to try it out.

## Debian package

The easiest way in. It puts the application in `/opt/reprise`, a launcher on
the path, a desktop entry, and the GNOME extension where the shell looks for
it.

```bash
sudo apt install ./reprise_2.0.0~alpha.1.20261028132951+20261028132951_amd64.deb
```

Use the exact filename of the downloaded build. Each build carries a unique
version, so apt can upgrade an older installation in place.

Some Ubuntu App Center versions disable installation of a local `.deb` as
soon as a package with the same name is installed, even when the file has a
newer version. This is tracked by
[Ubuntu App Center issue #1712](https://github.com/ubuntu/app-center/issues/1712)
and fixed upstream by
[PR #2217](https://github.com/ubuntu/app-center/pull/2217). The fix must also
reach the installed App Center snap; changing Reprise's package version or
metadata cannot repair that button. If it only says **Installed**, use the
update controls in **Reprise Settings → System info → Updates**. Click
**Check for updates** to look for a newer package. **Install** appears only
when a newer version is available. Clicking it downloads the package, checks
its size and SHA-256 against the published index, and opens Ubuntu's
authentication dialog to install it. No terminal or file picker is opened.

The checker reads the official apt repository at
`https://aodjo.github.io/Reprise/`. Alpha builds follow `alpha`, beta builds
follow `beta`, and stable builds follow `release`. It uses the native Debian architecture and Debian
version ordering, including build numbers, and never offers an older build.
New packages must be published to that channel by the Linux workflow or the
local publisher before they can be discovered; a shared-folder export alone
is not a publication.
Checks run only when the button is pressed. Merely opening Settings does
not perform a network request or install anything.

Download progress, unavailable publication, network errors, authentication
cancellation, and installation failures are shown in Settings. The installer
validates the downloaded package's name, architecture, and version again and
checks the installed version after apt finishes. Quit and reopen Reprise
after installation; log out and back in if the GNOME extension also changed.

For an older build without these controls, install one such build with the
apt command above first. The initial installation on a machine without
Reprise can still use App Center.

The Debian package depends on `pkexec` so Ubuntu's normal polkit agent can
request administrator authentication. Reprise never collects the password
or launches its own UI with root privileges.

Removing it again is by package name, not by file:

```bash
sudo apt remove reprise
```

The GNOME extension is installed or updated with the package. For logged-in
users, the install hook also refreshes old per-user copies that could hide
the system extension and requests activation. Reprise checks again whenever
it starts, covering installations made while nobody was logged in. There is
no separate ZIP to download or enable command to run. A shell that was
already running loads changed code after a log out and back in.

Removing it takes the extension with it:

```bash
sudo apt remove reprise
```

## AppImage

One file, nothing installed. Mark it executable once and run it, from the
file manager or the terminal:

```bash
chmod +x Reprise-2.0.0-alpha.1-x86_64.AppImage
./Reprise-2.0.0-alpha.1-x86_64.AppImage
```

Updating means replacing the file. The GNOME extension travels inside the
image, and Reprise installs or refreshes the user's copy and requests
activation when it starts. If an identical system extension is already
installed, it uses that copy instead.

## Archive

Each archive contains the application, its bundled GNOME extension, and the
desktop entry. Starting `app/reprise` installs or updates the extension
automatically. The application talks to MPRIS players over the D-Bus session bus
that the desktop session already provides, so it has no other runtime
dependency.

## GNOME top bar

The tray protocol carries a label as a plain string, so the desktop draws it
and a long title can only step a character at a time. The optional GNOME
extension in `gnome-extension/` draws Reprise's entry itself, which lets the
title scroll smoothly as it does in the macOS menu bar, and hides nothing
else: without the extension Reprise still appears in the tray.

Installation and updates are automatic through the package or the app.
The bundled `install-gnome-extension.sh` is also available for recovery, but
is not required during normal installation. Log out and back in when GNOME
Shell needs to load newly installed or updated code.

To try a CI archive, extract it and run `app/reprise`. The `.desktop` file can
be copied to `~/.local/share/applications`, and the application directory can
be placed under `~/.local/lib/reprise` with a launcher symlink in
`~/.local/bin/reprise`.
