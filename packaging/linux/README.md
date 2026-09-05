# Linux packaging

GitHub Actions publishes self-contained archives for `linux-x64` and
`linux-arm64`. Each archive contains the Avalonia application and the desktop
entry in this directory.

The application talks to MPRIS players over the D-Bus session bus that the
desktop session already provides, so it has no other runtime dependency. Its
labels are Korean, so a font with Hangul coverage should be present:

```bash
sudo apt install fonts-noto-cjk
```

To try a CI archive, extract it and run `app/reprise`. The `.desktop` file can
be copied to `~/.local/share/applications`, and the application directory can
be placed under `~/.local/lib/reprise` with a launcher symlink in
`~/.local/bin/reprise`.
