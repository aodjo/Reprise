//
//  RepriseTests.swift
//  RepriseTests
//

import CoreGraphics
import Testing
@testable import Reprise

@MainActor
struct RepriseTests {
    @Test
    func menuBarTitleUsesOnlySongTitleBesideArtwork() {
        let snapshot = makeSnapshot(
            player: .spotify,
            state: .playing,
            title: "Midnight City",
            album: "Hurry Up, We're Dreaming"
        )

        #expect(snapshot.menuBarTitle == "Midnight City")
    }

    @Test
    func shortMenuBarTitleDoesNotScroll() {
        let title = "Antifreeze"
        let titleWidth = MenuBarMarquee.textWidth(title)

        #expect(!MenuBarMarquee.requiresScrolling(titleWidth: titleWidth))
        #expect(MenuBarMarquee.offset(elapsed: 5, titleWidth: titleWidth) == 0)
        #expect(MenuBarMarquee.viewportWidth(for: titleWidth) == titleWidth)
    }

    @Test
    func longMenuBarTitleScrollsSmoothlyWithinMaximumWidth() {
        let title = "사랑하긴 했었나요 스쳐가는 인연이었나요 짧지 않은 우리 함께했던 시간들이"
        let titleWidth = MenuBarMarquee.textWidth(title)
        let firstOffset = MenuBarMarquee.offset(
            elapsed: MenuBarMarquee.initialPause + 0.1,
            titleWidth: titleWidth
        )
        let secondOffset = MenuBarMarquee.offset(
            elapsed: MenuBarMarquee.initialPause + 0.2,
            titleWidth: titleWidth
        )

        #expect(MenuBarMarquee.requiresScrolling(titleWidth: titleWidth))
        #expect(MenuBarMarquee.viewportWidth(for: titleWidth) == MenuBarMarquee.maximumTextWidth)
        #expect(abs(firstOffset + 3) < 0.001)
        #expect(abs(secondOffset + 6) < 0.001)
    }

    @Test
    func playingPlayerWinsOverSelectedPausedPlayer() {
        let spotify = makeSnapshot(
            player: .spotify,
            state: .paused,
            title: "Paused song",
            album: "Paused album"
        )
        let appleMusic = makeSnapshot(
            player: .appleMusic,
            state: .playing,
            title: "Playing song",
            album: "Playing album"
        )

        let preferred = NowPlayingStore.preferredSnapshot(
            spotify: spotify,
            appleMusic: appleMusic,
            selectedPlayer: .spotify
        )

        #expect(preferred?.player == .appleMusic)
        #expect(preferred?.track?.title == "Playing song")
    }

    @Test
    func selectedPlayerWinsWhenBothArePlaying() {
        let spotify = makeSnapshot(
            player: .spotify,
            state: .playing,
            title: "Spotify song",
            album: "Spotify album"
        )
        let appleMusic = makeSnapshot(
            player: .appleMusic,
            state: .playing,
            title: "Music song",
            album: "Music album"
        )

        let preferred = NowPlayingStore.preferredSnapshot(
            spotify: spotify,
            appleMusic: appleMusic,
            selectedPlayer: .appleMusic
        )

        #expect(preferred?.player == .appleMusic)
    }

    @Test
    func albumNameDoesNotChangeMenuBarTitle() {
        let snapshot = makeSnapshot(
            player: .appleMusic,
            state: .playing,
            title: "Single",
            album: ""
        )

        #expect(snapshot.menuBarTitle == "Single")
    }

    @Test
    func automationDenialProvidesRecoveryInstructions() {
        let message = MediaAutomationService.userFacingMessage(
            for: AutomationError.appleScript(number: -1743, message: "Not authorized")
        )

        #expect(message.contains("자동화"))
        #expect(message.contains("시스템 설정"))
    }

    @Test
    func playbackProgressAndRemainingTimeAreCalculatedSafely() {
        let track = Track(
            title: "Song",
            album: "Album",
            artist: "Artist",
            duration: 200,
            position: 75
        )

        #expect(track.progress == 0.375)
        #expect(track.remaining == 125)

        let overrun = Track(
            title: "Song",
            album: "Album",
            artist: "Artist",
            duration: 200,
            position: 250
        )

        #expect(overrun.progress == 1)
        #expect(overrun.remaining == 0)
    }

    private func makeSnapshot(
        player: MediaPlayerKind,
        state: PlaybackState,
        title: String,
        album: String
    ) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: true,
            state: state,
            track: Track(title: title, album: album, artist: "Artist"),
            errorMessage: nil
        )
    }
}
