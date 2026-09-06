// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Testing
@testable import Reprise

/// Covers demo mode, which produces the app's promotional screenshots.
///
/// Worth testing despite being non-shipping behaviour: the screenshots have to
/// be reproducible, so a change that made demo content drift would be caught
/// only by someone noticing a screenshot looked different.
@MainActor
struct RepriseDemoContentTests {
    /// Either trigger turns demo mode on, and neither turns it on by accident.
    ///
    /// The negative case matters most: demo mode reaching a shipping build
    /// would replace a user's real music with a fabricated track.
    @Test
    func demoModeCanBeEnabledByArgumentOrEnvironment() {
        #expect(
            RepriseDemoMode.isEnabled(
                arguments: ["Reprise", "--demo"],
                environment: [:]
            )
        )
        #expect(
            RepriseDemoMode.isEnabled(
                arguments: ["Reprise"],
                environment: ["REPRISE_DEMO_MODE": "1"]
            )
        )
        #expect(
            !RepriseDemoMode.isEnabled(
                arguments: ["Reprise"],
                environment: [:]
            )
        )
    }

    /// A demo store presents the fixed promotional track.
    ///
    /// The playback rate assertion is the one that keeps screenshots
    /// reproducible: at 0 the position never advances, so the progress bar and
    /// the displayed lyric stay put however long the app has been open. The
    /// lyric assertion follows from that - line four is what is current at the
    /// pinned position.
    @Test
    func demoModeSeedsStablePromotionalContent() async {
        let store = NowPlayingStore(demoMode: true)

        await store.refresh()

        let snapshot = store.activeSnapshot
        #expect(snapshot.player == .youtubeMusic)
        #expect(snapshot.state == .playing)
        #expect(snapshot.track?.title == RepriseDemoContent.title)
        #expect(snapshot.track?.artist == RepriseDemoContent.artist)
        #expect(snapshot.track?.artworkData?.isEmpty == false)
        #expect(snapshot.playbackRate == 0)
        #expect(store.syncedLyrics?.lines.count == 8)
        #expect(
            store.displayedLyricText
                == "One place for every song you love"
        )
    }

    /// Demo controls update the fabricated state without reaching a player.
    ///
    /// Demo mode has to stay self-contained: a screenshot session must never
    /// pause or seek whatever the user actually has playing. Seeking to 118
    /// also moves the displayed lyric, confirming the lyric view is driven by
    /// the same state the controls change.
    @Test
    func demoControlsStayLocalAndUpdateThePromotionalState() async {
        let store = NowPlayingStore(demoMode: true)

        await store.setVolume(42, for: .youtubeMusic)
        await store.seek(to: 118, for: .youtubeMusic)
        await store.perform(.playPause)

        let snapshot = store.activeSnapshot
        #expect(snapshot.volume == 42)
        #expect(snapshot.track?.position == 118)
        #expect(snapshot.state == .paused)
        #expect(
            store.displayedLyricText
                == "Your music, always within reach"
        )
    }
}
