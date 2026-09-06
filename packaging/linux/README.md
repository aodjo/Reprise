# Linux packaging

GitHub Actions publishes a Debian package, an AppImage, and a self-contained
archive for `linux-x64` and `linux-arm64`. All of them carry the whole
application, including its .NET runtime and the Pretendard typeface.

## Apt repository

Pushes publish the packages as an apt repository on GitHub Pages, so a
machine that adds it once keeps up with every later build. There are two
channels, and a machine follows only the one it subscribed to: `stable`
comes from `main`, `develop` from `develop`.

```bash
echo "deb [trusted=yes] https://aodjo.github.io/Reprise stable main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update && sudo apt install reprise
```

Swap `stable` for `develop` to follow development builds instead. The site
itself lives on the `apt-repo` branch, which each run updates in place,
because a channel has to survive the other channel being republished.

`build-apt-repo.sh` assembles that tree and can be run locally against a
directory of `.deb` files to try it out.

## Debian package

The easiest way in. It puts the application in `/opt/reprise`, a launcher on
the path, a desktop entry, and the GNOME extension where the shell looks for
it.

```bash
sudo apt install ./reprise_2.0.0~alpha.1+*_amd64.deb
```

Each build carries a stamp after the version, so installing a newer file over
an older one upgrades it rather than being skipped as already installed.
Removing it again is by package name, not by file:

```bash
sudo apt remove reprise
```

The GNOME extension is switched on as part of the install, and Reprise checks
again whenever it starts, so there is nothing else to run. A shell that was
already running loads it after a log out and back in, or after Alt+F2 and `r`
on Xorg.

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
image, and Reprise copies it into the user's extension directory and enables
it on first run, so the top bar entry works here too.

## Archive

Each archive contains the application and the desktop entry in this
directory. The application talks to MPRIS players over the D-Bus session bus
that the desktop session already provides, so it has no other runtime
dependency.

## GNOME top bar

The tray protocol carries a label as a plain string, so the desktop draws it
and a long title can only step a character at a time. The optional GNOME
extension in `gnome-extension/` draws Reprise's entry itself, which lets the
title scroll smoothly as it does in the macOS menu bar, and hides nothing
else: without the extension Reprise still appears in the tray.

```bash
./install-gnome-extension.sh
```

Then reload the shell: on Xorg press Alt+F2, type `r`, Enter; on Wayland log
out and back in.

To try a CI archive, extract it and run `app/reprise`. The `.desktop` file can
be copied to `~/.local/share/applications`, and the application directory can
be placed under `~/.local/lib/reprise` with a launcher symlink in
`~/.local/bin/reprise`.
