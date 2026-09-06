# Linux packaging

GitHub Actions publishes self-contained archives for `linux-x64` and
`linux-arm64`. Each archive contains the Avalonia application and the desktop
entry in this directory.

The application talks to MPRIS players over the D-Bus session bus that the
desktop session already provides, and bundles the Pretendard typeface, so it
has no other runtime dependency.

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
