# Linux packaging

GitHub Actions publishes self-contained archives for `linux-x64` and
`linux-arm64`. Each archive contains the Avalonia application and the desktop
entry in this directory.

The first Linux alpha expects `playerctl` to be installed by the distribution:

```bash
sudo apt install playerctl
```

To try a CI archive, extract it and run `app/reprise`. The `.desktop` file can
be copied to `~/.local/share/applications`, and the application directory can
be placed under `~/.local/lib/reprise` with a launcher symlink in
`~/.local/bin/reprise`.
