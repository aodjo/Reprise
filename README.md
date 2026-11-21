<div align="center">

# Reprise

**A lightweight music controller for macOS, Windows, and Linux.**

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-lightgrey.svg)](#requirements)
[![Release](https://img.shields.io/github/v/release/aodjo/reprise)](https://github.com/aodjo/reprise/releases)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/aodjo/reprise)](https://github.com/aodjo/reprise/commits)
[![GitHub Issues](https://img.shields.io/github/issues/aodjo/reprise)](https://github.com/aodjo/reprise/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/aodjo/reprise)](https://github.com/aodjo/reprise/pulls)

**English** | [한국어](docs/README-ko.md)

</div>

---

Reprise puts whatever you're listening to one click away. Album art, track title
and artist, a scrubbable progress bar, and playback controls — all in a single
menu bar or system tray panel. When synced lyrics are available, they appear
alongside the player and in the menu bar, taskbar, or desktop panel.

On macOS, Reprise connects to **Apple Music**, **Spotify**, and **YouTube Music**
through local apps and a browser extension. Windows uses system media sessions,
including **Spotify** and **Melon**; Linux uses **MPRIS-compatible players**.
Local playback control needs no account or API keys. Optionally connect a Google
account to share listening history, charts, and a profile on [reprise.mx](https://reprise.mx).

## Features

- **Your players, one control** — automatically follows the active player using each platform's local media integration
- **Now playing at a glance** — album art, track title, and artist, right in the menu bar or tray panel
- **Full playback control** — play, pause, skip, and scrub without leaving the app you're in
- **Progress bar** — see where you are in a track and jump anywhere in it
- **Synced lyrics** — time-synced lyrics in the player and the menu bar, Windows taskbar, or supported Linux panels
- **Make it yours** — configure the title format, album art style, carousel behavior, lyrics width, and panel theme
- **At home on your desktop** — SwiftUI on macOS, with a shared Avalonia player on Windows and Linux
- **Local player control** — playback state and commands travel only between Reprise and your local app or browser extension
- **Optional listening history** — connect through Google and explicitly approve sharing to use your profile and listening charts on reprise.mx

## Requirements

| Platform | Requirements | Player connection |
| --- | --- | --- |
| macOS | macOS 14 Sonoma or later | Music, Spotify, or YouTube Music through the Reprise browser extension |
| Windows | Windows 10 1809 or later; x64 or ARM64 | Apps and browsers that expose a Windows media session |
| Linux | A desktop with a D-Bus session bus; x64 or ARM64 | MPRIS-compatible apps and browsers |

On first launch, macOS will ask for permission to control your music app.
Reprise needs this to read the current track and send playback commands. You can
review it anytime under **System Settings → Privacy & Security → Automation**.

## Installation

### macOS — Homebrew

```bash
brew install --cask aodjo/tap/reprise
```

### macOS — Mac App Store

Coming soon.

### macOS — Direct download

Download the latest `.dmg` from the
[Releases](https://github.com/aodjo/reprise/releases) page.

### macOS — Build from source

```bash
git clone https://github.com/aodjo/reprise.git
cd reprise
open src/Reprise.Platform.MacOS/Reprise.xcodeproj
```

Build and run the `Reprise` scheme in Xcode 16 or later.

### Windows

Download the [Windows installer](https://aodjo.github.io/Reprise/windows/):
choose **x64** for Intel/AMD PCs or **ARM64** for Windows on Arm. Windows 10
1809 or later is required. Installation is per-user and includes .NET.

Windows players and browsers connect through the system media-session API.
Use the tray icon to open the player or settings. Settings → System info →
Check for updates downloads and installs the next build for the same release
stage and CPU architecture. Development installers are unsigned.

See [Windows packaging](packaging/windows/README.md) for source builds.

### Linux

The Linux app uses Avalonia for the desktop and reads local MPRIS media sessions
from the D-Bus session bus. Its player includes album art, a scrolling title, transport controls, a scrubber,
volume, and the White, Dark, Liquid, and System themes.

Add the apt repository once, and later builds arrive with your usual
updates:

```bash
echo "deb [trusted=yes] https://aodjo.github.io/Reprise release main" | sudo tee /etc/apt/sources.list.d/reprise.list
sudo apt update && sudo apt install reprise
```

There is also an AppImage on the [Linux download page](https://aodjo.github.io/Reprise/) for running Reprise without
installing anything, and the source builds directly:

```bash
dotnet run --project src/Reprise.Platform.Linux/Reprise.Platform.Linux.csproj
```

One click on the tray icon opens the panel, which hides again when it loses
focus, like the macOS popover. On panels that support the Ubuntu tray label
extension, the track title or the current lyric line appears beside the icon,
as it does in the macOS menu bar. On GNOME a small shell extension, installed
and switched on by the package, draws that entry itself and scrolls the title
smoothly. The interface is set in Pretendard, bundled
with the application. Linux CI also publishes self-contained `linux-x64` and
`linux-arm64` archives.

## Usage

Click the Reprise icon in your menu bar or system tray to open the player panel.

Reprise gives priority to whichever player is actively playing. If more than one
is playing at the same time, it follows the order set under **Settings → General
→ Display Priority**. When nothing is playing, it stays on the highest-priority
player that still has a track loaded. Drag the entries in Settings to reorder
them.

Open Settings with the gear button in the player panel, or press
<kbd>⌘</kbd><kbd>,</kbd> on macOS while the panel is open. Depending on your platform, you can configure:

- Automatically pausing the current player when another one starts playing
- Player display priority and YouTube Music extension connection status
- Synced lyrics in the menu bar, taskbar, or Linux panel, along with lyrics width and reserved space
- Panel theme — White, Dark, Liquid, or matching your system setting
- Title format, album art / CD / level indicator style, and carousel speed
- How elapsed time, remaining time, and total duration are displayed

On macOS, YouTube Music requires a separate browser extension. See the
[browser extension guide](BrowserExtension/README.md) for local installation
instructions. Windows and Linux can discover browser playback through system
media sessions or MPRIS when the browser exposes them.

To share listening history, open the **Reprise.mx** section in Settings and connect
your Google account. Sharing starts only after you approve the connection in your
browser; you can disconnect from Settings at any time.

## Contributing

Contributions of every kind are welcome — bug reports, feature ideas, and pull
requests alike.

Reprise is released under GPLv3, and is also distributed on the Mac App Store
under separate license terms. To make both forms of distribution possible,
contributors are asked to sign a Contributor License Agreement (CLA). You keep
the copyright to your work; the CLA grants permission to distribute it under
both sets of terms.

Signing is automatic. Open a pull request and the CLA bot will comment with a
signing link. Agreeing once in a comment is enough — you won't be asked again.

See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## License

Reprise is free software, distributed under the
[GNU General Public License v3.0](LICENSE).

Anyone may use, study, modify, and share Reprise. If you distribute a modified
version, it must also be released under GPLv3 with source code included.

The version distributed on the Mac App Store is provided under separate
proprietary license terms, as permitted by the copyright holder.

**The name "Reprise" and the Reprise icon are trademarks of Junsung Lee and are
not covered by the GPL. Forks must use a different name and icon.**

## Notices

Reprise is an independent project and is not affiliated with, endorsed by, or
sponsored by Apple Inc. or Spotify AB.

Apple and Apple Music are trademarks of Apple Inc., registered in the U.S. and
other countries. Spotify is a trademark of Spotify AB. YouTube and YouTube Music
are trademarks of Google LLC.

---

<div align="center">
<sub>Copyright © 2026 <a href="https://junx.dev/">Junsung Lee</a>. All rights reserved.</sub>
</div>
