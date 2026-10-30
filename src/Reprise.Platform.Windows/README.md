# Reprise for Windows

The Windows host uses Global System Media Transport Controls (GSMTC), available
on Windows 10 1809 and later, to discover players and control playback. It hosts
the shared Avalonia panel, preferences, artwork tray icon, and smooth marquee.
Players must expose a Windows media session; browser players may require media
to start before they appear. Dedicated Spotify and Melon audio streams support
per-application Core Audio volume, including application-specific output devices.
The system master volume and other applications are never changed. The control
is unavailable before a local audio stream exists, for browser tabs sharing a
process, and for Spotify Connect playback on another device before account connection. GSMTC exposes no
remote-device volume API; an accessibility slider can advertise writes without
actually applying them, so it is not treated as a supported remote control.

Install the per-user `Setup.exe`; no administrator account or separate .NET
installation is required. The application lives under LocalAppData/Programs,
preferences under AppData/Reprise/{channel}, and startup uses the current user's
Run key. A second launch opens the existing instance. The tray icon opens the
panel; its footer opens settings or exits the application.

Synced lyrics and the album cover appear in a transparent strip immediately left
of the primary taskbar's notification area. The native window spans the taskbar
height so Windows' minimum window height cannot shift its contents downward.
Lyric advances push upward over 280 ms with macOS's ease-in/ease-out timing;
first appearance and track changes are immediate. The cover stays still while
lyrics move. Long lines scroll smoothly with fades at both ends;
without synced lyrics the strip shows the track title and artist. Click it to
open the player. General settings provide visibility and width controls. Taskbar settings include title format, cover style, and left/right placement. Left placement uses the measured gap between Widgets and centered application buttons and falls back to the right when there is no safe gap. Unreserved lyric widths ease over the same 280 ms as the upward line push.
The strip follows taskbar DPI and system theme, and hides during fullscreen,
taskbar autohide, or insufficient free space. The tray artwork setting also
controls the leading cover. Start, Search, and trusted Windows shell flyouts
do not count as fullscreen apps, even when their native surface covers the
monitor. If the shell temporarily stops exposing tray accessibility geometry,
the strip retains its last measured position only while that shell UI has focus
and the taskbar HWND, bounds, monitor, and DPI still match. Explorer restarts,
layout changes, and hidden taskbars invalidate this fallback.
Narrow legacy task-list bounds never reduce the previously measured occupied
button area, so wide lyric settings cannot overlap XAML app icons during a menu.
Tray-only or partial menu updates do not replace complete desktop measurements;
hidden legacy task-list HWNDs are ignored because their bounds can omit XAML icons.
The lyric popup is owned by the taskbar so shell menus cannot paint over it;
this changes only Reprise's native owner and never reparents or resizes Explorer.
If the native strip is destroyed, it is recreated with the current lyric and
artwork while retaining its nonactivating behavior.
The first letters stay opaque while stationary or
at the beginning of a new line; the leading fade grows only as text leaves the
viewport. It reads shell/accessibility
geometry and never resizes, reparents, or injects code into Explorer. Vertical
taskbars are not supported. The self-contained payload includes Microsoft's
desktop runtime for reading Windows 11 XAML taskbar accessibility bounds.

General → Taskbar lyrics → Reserve lyric space is off by default on Windows.
When off, the strip measures the actual glyph width and hugs the notification
area; the width slider is only an upper limit for long lines. When on, short
lines retain that maximum width. This preference is separate from Linux's
reserved-label setting, and very short lines can use windows below the native
default minimum width without extending over tray icons.

The click-opened player's lyric sheet keeps the most recently started line
highlighted until the next line begins, matching macOS and the taskbar lyric.
Short gaps in a provider's explicit line end times do not dim the entire sheet;
before the first timed line, no row is highlighted.

The Liquid theme uses a custom live refractive background, with slight center
magnification, Snell-law bevel displacement, and fixed upper-left highlights.
Moving the pointer does not change the surface lighting or rim shading.
The entire background, including the edge and corner areas, is heavily frosted
by 128 Gaussian-distributed live DWM samples with a 14-DIP standard deviation.
The samples avoid a repeating grid, which can leave sharp bands in fine
backgrounds. A faint refractive bevel preserves optical depth without leaving
clear strips around the frosted center.
The foreground remains transparent: a separate Acrylic host
backdrop would cover the thumbnail lens with a different background surface.
Liquid uses white text over a 78% dark tint in both desktop appearance modes,
preserving soft transmitted colors while keeping text readable. Sampling stops
when hidden.
The native backdrop follows the player in physical pixels and clips to its
rounded outline. DWM thumbnail relationships sample only visible, uncloaked,
non-minimized windows below the player, in desktop stacking order. Their pixels
stay in the window manager; the app does not record the desktop, exclude itself
from screen recording, or modify another application's windows. Hidden panels
disconnect those relationships. Ordinary playback changes update through DWM;
only source geometry is polled while visible. This is a custom Windows optical
approximation, not Apple's material renderer. It does not use the unsupported
backdrop-brush transform or the windowed Magnification API, whose filtering is
unavailable with some virtual display drivers. Black and White remain opaque.

Settings → System info → Check for updates discovers the next installer for the
installed release stage and process architecture. Install verifies size, SHA-256,
and installer version, exits Reprise, replaces its files, and reopens the app.
Portable copies do not install updates. Alpha, beta, and release feeds and
installation registrations are separate. Development installers are unsigned.

See [packaging](../../packaging/windows/README.md) for building and publishing.

Spotify Connect volume uses the official Spotify Web API after General → Spotify
Connect → Connect. It requires Spotify Premium and a device that advertises volume
support. The displayed Windows Spotify track must match the authorized account's
playback; writes name that exact device and track, and fail if either changes.
Cloud reads run separately from native playback polling, with caching and Spotify
rate-limit backoff. No playback device is transferred by Reprise. Disconnect
removes this app's local authorization. Tokens use Windows DPAPI CurrentUser
protection in the release-stage preference directory.

Maintainers register a public Spotify application with the redirect URI
`http://127.0.0.1/reprise/spotify/callback` (loopback dynamic ports are supported),
then build with `REPRISE_SPOTIFY_CLIENT_ID=<public-client-id>` or
`-p:SpotifyClientId=<public-client-id>`. This public identifier can be bundled;
never ship or request a Client Secret. PKCE opens Spotify's official consent page
and listens only on a random IPv4 loopback port for three minutes. Required scopes
are `user-read-playback-state` and `user-modify-playback-state`. Unconfigured builds
show the connection requirement rather than offering a nonworking authorization
button. Test accounts must be eligible under the registered application's Spotify
development-mode access rules until production access is approved.
