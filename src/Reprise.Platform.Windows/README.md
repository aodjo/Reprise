# Reprise for Windows

The Windows host uses Global System Media Transport Controls (GSMTC), available
on Windows 10 1809 and later, to discover players and control playback. It hosts
the shared Avalonia panel, preferences, artwork tray icon, and smooth marquee.
Players must expose a Windows media session; browser players may require media
to start before they appear. Windows does not expose per-player volume through
GSMTC, so that control is disabled rather than changing the system volume.

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
open the player. General settings provide a visibility toggle and width control.
The strip follows taskbar DPI and system theme, and hides during fullscreen,
taskbar autohide, or insufficient free space. The tray artwork setting also
controls the leading cover. Start, Search, and trusted Windows shell flyouts
do not count as fullscreen apps, even when their native surface covers the
monitor. If the shell temporarily stops exposing tray accessibility geometry,
the strip retains its last measured position only while that shell UI has focus
and the taskbar HWND, bounds, monitor, and DPI still match. Explorer restarts,
layout changes, and hidden taskbars invalidate this fallback.
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
