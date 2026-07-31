<div align="center">

# Reprise

**A lightweight menu bar music controller for macOS.**

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-macOS-lightgrey.svg)](#requirements)
[![Release](https://img.shields.io/github/v/release/aodjo/reprise)](https://github.com/aodjo/reprise/releases)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/aodjo/reprise)](https://github.com/aodjo/reprise/commits)
[![GitHub Issues](https://img.shields.io/github/issues/aodjo/reprise)](https://github.com/aodjo/reprise/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/aodjo/reprise)](https://github.com/aodjo/reprise/pulls)

**English** | [한국어](docs/README-ko.md)

</div>

---

Reprise puts whatever you're listening to one click away. Album art, track title
and artist, a scrubbable progress bar, and playback controls — all in a single
menu bar panel. When synced lyrics are available, they appear alongside the
player.

Supports **Apple Music**, **Spotify**, and **YouTube Music**, the last of which
connects through a browser extension. No Reprise account, no API keys, and no
relay server in between.

## Features

- **Three players, one control** — automatically follows whichever of Apple Music, Spotify, or YouTube Music is currently playing
- **Now playing at a glance** — album art, track title, and artist, right in the menu bar panel
- **Full playback control** — play, pause, skip, and scrub without leaving the app you're in
- **Progress bar** — see where you are in a track and jump anywhere in it
- **Synced lyrics** — time-synced lyrics, in the panel and in the menu bar
- **Built around your menu bar** — configure the title format, album art style, carousel behavior, lyrics width, and panel theme
- **Native and light** — built with SwiftUI, sitting quietly in the menu bar
- **Local player control** — playback state and commands travel only between Reprise and your local app or browser extension

## Requirements

- macOS 14 Sonoma or later
- At least one of the following:
  - The Music app included with macOS
  - The Spotify desktop app
  - A Chromium or Firefox browser with the Reprise YouTube Music extension installed

On first launch, macOS will ask for permission to control your music app.
Reprise needs this to read the current track and send playback commands. You can
review it anytime under **System Settings → Privacy & Security → Automation**.

## Installation

### Homebrew

```bash
brew install --cask aodjo/tap/reprise
```

### Mac App Store

Coming soon.

### Direct download

Download the latest `.dmg` from the
[Releases](https://github.com/aodjo/reprise/releases) page.

### Build from source

```bash
git clone https://github.com/aodjo/reprise.git
cd reprise
open Reprise.xcodeproj
```

Build and run the `Reprise` scheme in Xcode 16 or later.

## Usage

Click the Reprise icon in your menu bar to open the player panel.

Reprise gives priority to whichever player is actively playing. If more than one
is playing at the same time, it follows the order set under **Settings → General
→ Display Priority**. When nothing is playing, it stays on the highest-priority
player that still has a track loaded. Drag the entries in Settings to reorder
them.

Open Settings with the gear button in the player panel, or press
<kbd>⌘</kbd><kbd>,</kbd> while the panel is open. You can configure:

- Automatically pausing the current player when another one starts playing
- Player display priority and YouTube Music extension connection status
- Synced lyrics in the menu bar, along with lyrics width and reserved space
- Panel theme — White, Dark, Liquid, or matching your system setting
- Title format, album art / CD / level indicator style, and carousel speed
- How elapsed time, remaining time, and total duration are displayed

YouTube Music requires a separate browser extension. See the
[browser extension guide](BrowserExtension/README.md) for local installation
instructions.

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
Pull requests containing only documentation or typo fixes are exempt.

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
