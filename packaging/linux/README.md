# Linux packaging

GitHub Actions publishes a Debian package and a self-contained archive for
`linux-x64` and `linux-arm64`. Both carry the whole application, including
its .NET runtime and the Pretendard typeface.

## Debian package

The easiest way in. It puts the application in `/opt/reprise`, a launcher on
the path, a desktop entry, and the GNOME extension where the shell looks for
it.

```bash
sudo apt install ./reprise_2.0.0~alpha.1_amd64.deb
gnome-extensions enable reprise@junx.dev
```

Removing it takes the extension with it:

```bash
sudo apt remove reprise
```

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
