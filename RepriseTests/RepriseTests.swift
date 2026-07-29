//
//  RepriseTests.swift
//  RepriseTests
//

import CoreGraphics
import Foundation
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
    func playerPanelStartsAtTheStatusItemLeadingEdge() {
        let origin = PlayerPanelLayout.origin(
            anchorFrame: CGRect(x: 300.5, y: 900, width: 100, height: 22),
            panelSize: CGSize(width: 360, height: 140),
            visibleFrame: CGRect(x: 0, y: 0, width: 1_440, height: 900),
            backingScale: 2
        )

        #expect(origin == CGPoint(x: 300.5, y: 755))
    }

    @Test
    func playerPanelStaysInsideTheVisibleScreenWidth() {
        let origin = PlayerPanelLayout.origin(
            anchorFrame: CGRect(x: 0, y: 900, width: 20, height: 22),
            panelSize: CGSize(width: 360, height: 140),
            visibleFrame: CGRect(x: 0, y: 0, width: 1_440, height: 900),
            backingScale: 2
        )

        #expect(origin.x == PlayerPanelLayout.screenMargin)
    }

    @Test
    func marqueePreferencesArePersistedAndReadBack() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)
        defaults.set(
            false,
            forKey: ReprisePreferenceKey.automaticallyScrollTitles
        )
        defaults.set(
            MarqueeSpeed.fast.rawValue,
            forKey: ReprisePreferenceKey.marqueeSpeed
        )
        defaults.set(
            false,
            forKey: ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens
        )
        defaults.set(
            PlayerPanelTheme.black.rawValue,
            forKey: ReprisePreferenceKey.playerPanelTheme
        )
        defaults.set(
            MenuBarArtworkStyle.levelIndicator.rawValue,
            forKey: ReprisePreferenceKey.menuBarArtworkStyle
        )
        defaults.set(
            MenuBarTitleFormat.artistTitle.rawValue,
            forKey: ReprisePreferenceKey.menuBarTitleFormat
        )

        let preferences = MarqueePreferences.current(defaults: defaults)

        #expect(!preferences.automaticallyScrollsTitles)
        #expect(preferences.pointsPerSecond == CGFloat(MarqueeSpeed.fast.rawValue))
        #expect(!preferences.resetsMenuTitleWhenPanelOpens)
        #expect(preferences.menuBarArtworkStyle == .levelIndicator)
        #expect(preferences.menuBarTitleFormat == .artistTitle)
        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.playerPanelTheme
            ) == PlayerPanelTheme.black.rawValue
        )
    }

    @Test
    func menuBarTitleFormatOrdersTitleAndArtist() {
        #expect(
            MenuBarTitleFormat.titleOnly.text(
                title: "Song",
                artist: "Artist"
            ) == "Song"
        )
        #expect(
            MenuBarTitleFormat.titleArtist.text(
                title: "Song",
                artist: "Artist"
            ) == "Song - Artist"
        )
        #expect(
            MenuBarTitleFormat.artistTitle.text(
                title: "Song",
                artist: "Artist"
            ) == "Artist - Song"
        )
        #expect(
            MenuBarTitleFormat.artistTitle.text(
                title: "Song",
                artist: ""
            ) == "Song"
        )
        #expect(
            MenuBarTitleFormat.hidden.text(
                title: "Song",
                artist: "Artist"
            ).isEmpty
        )
    }

    @Test
    func titleOnlyIsTheDefaultMenuBarTitleFormat() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.menuBarTitleFormat
            ) == MenuBarTitleFormat.titleOnly.rawValue
        )
    }

    @Test
    func albumArtworkIsTheDefaultMenuBarArtworkStyle() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.menuBarArtworkStyle
            ) == MenuBarArtworkStyle.albumArtwork.rawValue
        )
    }

    @Test
    func hiddenMenuBarArtworkRemovesItsSpacing() {
        let titleWidth: CGFloat = 80

        #expect(
            MenuBarMarquee.totalWidth(
                for: titleWidth,
                artworkStyle: .hidden
            ) == titleWidth
        )
        #expect(
            MenuBarMarquee.totalWidth(
                for: titleWidth,
                artworkStyle: .compactDisc
            ) == titleWidth
                + MenuBarMarquee.artworkSize
                + MenuBarMarquee.artworkTitleSpacing
        )
    }

    @Test
    func hiddenMenuBarTextLeavesOnlyTheArtworkWidth() {
        #expect(
            MenuBarMarquee.totalWidth(
                for: 0,
                artworkStyle: .albumArtwork
            ) == MenuBarMarquee.artworkSize
        )
    }

    @Test
    func atLeastOneMenuBarElementRemainsVisible() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        defaults.set(
            MenuBarArtworkStyle.hidden.rawValue,
            forKey: ReprisePreferenceKey.menuBarArtworkStyle
        )
        defaults.set(
            MenuBarTitleFormat.hidden.rawValue,
            forKey: ReprisePreferenceKey.menuBarTitleFormat
        )

        let preferences = MarqueePreferences.current(defaults: defaults)

        #expect(preferences.menuBarArtworkStyle == .albumArtwork)
        #expect(preferences.menuBarTitleFormat == .hidden)
    }

    @Test
    func liquidIsTheDefaultPlayerPanelTheme() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.playerPanelTheme
            ) == PlayerPanelTheme.liquid.rawValue
        )
    }

    @Test
    func marqueeFadeUsesAFixedWidthAtTheTrailingEdge() {
        #expect(
            abs(
                MarqueeFade.startLocation(
                    viewportWidth: 200
                ) - 0.93
            ) < 0.0001
        )
        #expect(
            MarqueeFade.startLocation(
                viewportWidth: 5
            ) == 0
        )
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

    @Test
    func playbackPositionIsClampedToTrackDuration() {
        #expect(
            PlaybackPosition.clamped(-1, duration: 200) == 0
        )
        #expect(
            PlaybackPosition.clamped(75, duration: 200) == 75
        )
        #expect(
            PlaybackPosition.clamped(250, duration: 200) == 200
        )
        #expect(
            PlaybackPosition.clamped(.infinity, duration: 200) == 0
        )
        #expect(
            PlaybackPosition.clamped(75, duration: 0) == 0
        )
    }

    @Test
    func seekOnlyCompletesAfterPlayerReportsTheTargetPosition() {
        #expect(
            PlaybackPosition.confirmsSeek(
                actual: 121.2,
                target: 120
            )
        )
        #expect(
            !PlaybackPosition.confirmsSeek(
                actual: 45,
                target: 120
            )
        )
    }

    @Test
    func playerVolumeIsClampedToTheSupportedRange() {
        #expect(PlayerVolume.clamped(-1) == 0)
        #expect(PlayerVolume.clamped(42) == 42)
        #expect(PlayerVolume.clamped(101) == 100)
    }

    @Test
    func muteToggleRestoresTheLastAudibleVolume() {
        #expect(
            PlayerVolume.muteToggleTarget(
                current: 73,
                lastAudible: 42
            ) == 0
        )
        #expect(
            PlayerVolume.muteToggleTarget(
                current: 0,
                lastAudible: 73
            ) == 73
        )
        #expect(
            PlayerVolume.muteToggleTarget(
                current: 0,
                lastAudible: nil
            ) == PlayerVolume.defaultAudibleLevel
        )
    }

    @Test
    func panelTimeStylesFormatBothSides() {
        #expect(
            PanelTimeDisplay.leadingText(
                style: .elapsed,
                position: 69
            ) == "1:09"
        )
        #expect(
            PanelTimeDisplay.leadingText(
                style: .zero,
                position: 69
            ) == "00:00"
        )
        #expect(
            PanelTimeDisplay.trailingText(
                style: .remaining,
                duration: 193,
                remaining: 124
            ) == "-2:04"
        )
        #expect(
            PanelTimeDisplay.trailingText(
                style: .duration,
                duration: 193,
                remaining: 124
            ) == "3:13"
        )
    }

    @Test
    func elapsedAndRemainingAreTheDefaultPanelTimeStyles() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.panelLeadingTimeStyle
            ) == PanelLeadingTimeStyle.elapsed.rawValue
        )
        #expect(
            defaults.string(
                forKey: ReprisePreferenceKey.panelTrailingTimeStyle
            ) == PanelTrailingTimeStyle.remaining.rawValue
        )
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
