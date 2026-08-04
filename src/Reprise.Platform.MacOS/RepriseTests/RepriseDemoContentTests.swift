// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Testing
@testable import Reprise

@MainActor
struct RepriseDemoContentTests {
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
