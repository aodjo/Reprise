# Reprise for Linux

The Linux host is an Avalonia desktop application that talks to MPRIS players
over the D-Bus session bus. It renders the same player panel as the macOS app:
album art, a scrolling title, previous / play-pause / next, a scrubber with
elapsed and remaining time, a volume slider, and a footer with settings and
quit. The panel opens from the tray icon, hides when it loses focus or on
Escape, and can be dragged anywhere.

Settings live in `$XDG_CONFIG_HOME/reprise/preferences.json` (normally
`~/.config/reprise/preferences.json`) and cover the panel theme (White, Dark,
Liquid, System), the two time labels, and title scrolling. They are edited from
the gear button in the panel footer.

## Requirements

- Ubuntu 24.04 or a compatible modern Linux distribution
- X11, or Wayland with XWayland
- A D-Bus session bus, which every desktop session already provides
- A font with Hangul coverage, such as `fonts-noto-cjk`, since the panel's
  labels are Korean like the macOS app's
- A compositor, for the translucent Liquid theme; without one the panel falls
  back to an opaque ground

```bash
dotnet run --project src/Reprise.Platform.Linux/Reprise.Platform.Linux.csproj
```

MPRIS is reached directly through `Tmds.DBus.Protocol`, so no command line
helper such as `playerctl` has to be installed.

The CI workflow publishes self-contained `linux-x64` and `linux-arm64`
artifacts, so end-user packages do not require a separate .NET installation.
