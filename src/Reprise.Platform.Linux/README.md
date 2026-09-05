# Reprise for Linux

The Linux host is an Avalonia desktop application that talks to MPRIS players
over the D-Bus session bus. The first MVP displays the active Linux media
session, supports previous, play/pause, and next commands, and remains
available through a system tray icon when its window is closed.

## Requirements

- Ubuntu 24.04 or a compatible modern Linux distribution
- X11, or Wayland with XWayland
- A D-Bus session bus, which every desktop session already provides

```bash
dotnet run --project src/Reprise.Platform.Linux/Reprise.Platform.Linux.csproj
```

MPRIS is reached directly through `Tmds.DBus.Protocol`, so no command line
helper such as `playerctl` has to be installed.

The CI workflow publishes self-contained `linux-x64` and `linux-arm64`
artifacts, so end-user packages do not require a separate .NET installation.
