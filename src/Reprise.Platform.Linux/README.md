# Reprise for Linux

The Linux host is an Avalonia desktop application that talks to MPRIS players
over the D-Bus session bus. It renders the same player panel as the macOS app:
album art, a scrolling title, previous / play-pause / next, a scrubber with
elapsed and remaining time, a volume slider, and a footer with settings and
quit. The panel opens from the tray icon, hides when it loses focus or on
Escape, and can be dragged anywhere.

The tray entry is Reprise's own StatusNotifierItem rather than Avalonia's, so
it can carry text beside the icon the way the macOS menu bar does. Panels that
honour the Ubuntu label extension - Ubuntu's GNOME, Budgie, MATE, and Xfce
with the indicator plugin - show the track title (or the current lyric line)
next to the album cover; KDE Plasma and other hosts show the cover and menu
only. Long text scrolls by stepping one character every quarter second.

Settings live in `$XDG_CONFIG_HOME/reprise/preferences.json` (normally
`~/.config/reprise/preferences.json`) and cover the panel theme (White, Dark,
Liquid, System), the two time labels, title scrolling, and the tray label:
its format (title, title - artist, artist - title, none), whether it shows
lyrics, and whether the icon is the album cover. They are edited from the
gear button in the panel footer.

Synced lyrics come from VIBE and LRCLIB, the same services the macOS app
uses, and are looked up once per track.

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
