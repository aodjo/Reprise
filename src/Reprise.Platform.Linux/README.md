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
next to the album cover; KDE Plasma and other hosts show the cover alone. Long
label carries the line whole: a tray label cannot animate, so imitating the
panel's scrolling title would mean publishing a moving slice of the text
and cutting words in half, and showing the line in full is the honest
alternative. Reserving space for lyrics pads the label with spaces rather
than claiming a width it does not use. The entry publishes no menu, so a
click opens the panel straight away as it does on macOS; quitting is in
the panel footer. Its font is the panel's own, which no application can
set.

On GNOME the optional extension in `packaging/linux/gnome-extension` replaces
that entry with one the shell draws from Reprise's own D-Bus service,
`dev.junx.Reprise`. Because the extension owns the actor, the title scrolls
pixel by pixel like the macOS menu bar rather than stepping a character at a
time, and the service hands it the whole line, the cover as a PNG, and the
user's scrolling settings. Install it with
`packaging/linux/install-gnome-extension.sh`.

The interface is set in Pretendard, bundled with the application, so the panel
looks the same on every distribution and covers Hangul without depending on
which fonts are installed.

The gear button in the panel footer opens a settings window with the same
six tabs as the macOS app - General, YouTube Music, Theme, Menu bar, Panel,
and System info - including the live previews. Settings live in
`$XDG_CONFIG_HOME/reprise/preferences.json` (normally
`~/.config/reprise/preferences.json`) and cover auto-pausing other players,
tray lyrics and their width, display priority and the remembered player,
launch at login (an XDG autostart entry), the panel theme, the tray text and
icon, the marquee, and the two time labels. The YouTube Music tab lists
browser tabs seen over MPRIS, since Linux browsers need no extension.

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
