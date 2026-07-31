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
        defaults.set(
            false,
            forKey: ReprisePreferenceKey.menuBarReservesLyricsWidth
        )
        defaults.set(
            240.0,
            forKey: ReprisePreferenceKey.menuBarLyricsWidth
        )

        let preferences = MarqueePreferences.current(defaults: defaults)

        #expect(!preferences.automaticallyScrollsTitles)
        #expect(preferences.pointsPerSecond == CGFloat(MarqueeSpeed.fast.rawValue))
        #expect(!preferences.resetsMenuTitleWhenPanelOpens)
        #expect(preferences.menuBarArtworkStyle == .levelIndicator)
        #expect(preferences.menuBarTitleFormat == .artistTitle)
        #expect(!preferences.menuBarReservesLyricsWidth)
        #expect(preferences.menuBarLyricsWidth == 240)
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
    func reservedLyricsWidthDoesNotChangeWithShortLines() {
        let shortLineWidth = MenuBarMarquee.textWidth("짧은 가사")
        let longerLineWidth = MenuBarMarquee.textWidth(
            "길이가 조금 더 긴 다음 가사"
        )

        #expect(
            MenuBarMarquee.viewportWidth(
                for: shortLineWidth,
                reservesMaximumTextWidth: true
            ) == MenuBarMarquee.maximumTextWidth
        )
        #expect(
            MenuBarMarquee.viewportWidth(
                for: longerLineWidth,
                reservesMaximumTextWidth: true
            ) == MenuBarMarquee.maximumTextWidth
        )
    }

    @Test
    func configuredLyricsWidthControlsTheMenuBarViewport() {
        let configuredWidth: CGFloat = 240

        #expect(
            MenuBarMarquee.viewportWidth(
                for: 40,
                reservesMaximumTextWidth: true,
                maximumWidth: configuredWidth
            ) == configuredWidth
        )
        #expect(
            MenuBarMarquee.viewportWidth(
                for: 400,
                maximumWidth: configuredWidth
            ) == configuredWidth
        )
        #expect(
            MenuBarMarquee.viewportWidth(
                for: 40,
                maximumWidth: configuredWidth
            ) == 40
        )
    }

    @Test
    func lyricLinesUseUpwardTransitionOnlyWithinTheSameTrack() {
        #expect(
            MenuBarTitleTransitionStyle.resolved(
                previousTitle: "첫 번째 가사",
                currentTitle: "두 번째 가사",
                previousTrackKey: "track-a",
                currentTrackKey: "track-a",
                isDisplayingLyrics: true
            ) == .lyricsUpward
        )
        #expect(
            MenuBarTitleTransitionStyle.resolved(
                previousTitle: "이전 곡 가사",
                currentTitle: "새 곡 가사",
                previousTrackKey: "track-a",
                currentTrackKey: "track-b",
                isDisplayingLyrics: true
            ) == .immediate
        )
        #expect(
            MenuBarTitleTransitionStyle.resolved(
                previousTitle: "곡 제목",
                currentTitle: "다음 곡 제목",
                previousTrackKey: "track-a",
                currentTrackKey: "track-a",
                isDisplayingLyrics: false
            ) == .immediate
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
    func automaticallyPausingTheOtherPlayerIsOffByDefault() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            !ReprisePreferences.automaticallyPausesOtherPlayer(
                in: defaults
            )
        )
    }

    @Test
    func rememberingTheLastPlayedPlayerIsOffByDefault() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            !ReprisePreferences.remembersLastPlayedPlayer(
                in: defaults
            )
        )
        #expect(ReprisePreferences.lastPlayedPlayer(in: defaults) == nil)

        ReprisePreferences.setLastPlayedPlayer(
            .youtubeMusic,
            in: defaults
        )
        #expect(
            ReprisePreferences.lastPlayedPlayer(in: defaults)
                == .youtubeMusic
        )
    }

    @Test
    func spotifyIsFirstInTheDefaultPlayerDisplayOrder() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            ReprisePreferences.playerDisplayOrder(
                in: defaults
            ) == [.spotify, .appleMusic, .youtubeMusic]
        )
    }

    @Test
    func savedPlayerDisplayOrderIsUsed() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        defaults.set(
            ReprisePreferences.serializedPlayerDisplayOrder(
                [.appleMusic, .spotify]
            ),
            forKey: ReprisePreferenceKey.playerDisplayPriority
        )

        #expect(
            ReprisePreferences.playerDisplayOrder(
                in: defaults
            ) == [.appleMusic, .spotify, .youtubeMusic]
        )
    }

    @Test
    func legacySinglePlayerPriorityBecomesACompleteOrder() {
        #expect(
            ReprisePreferences.playerDisplayOrder(
                from: MediaPlayerKind.appleMusic.rawValue
            ) == [.appleMusic, .spotify, .youtubeMusic]
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
    func playingPlayerWinsOverPausedDisplayPriority() {
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
            displayOrder: [.spotify, .appleMusic]
        )

        #expect(preferred?.player == .appleMusic)
        #expect(preferred?.track?.title == "Playing song")
    }

    @Test
    func displayPriorityWinsWhenBothArePlaying() {
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
            displayOrder: [.appleMusic, .spotify]
        )

        #expect(preferred?.player == .appleMusic)
    }

    @Test
    func rememberedPlayerWinsWhenNothingIsPlaying() {
        let spotify = makeSnapshot(
            player: .spotify,
            state: .paused,
            title: "Spotify song",
            album: "Spotify album"
        )
        let youtubeMusic = makeSnapshot(
            player: .youtubeMusic,
            state: .paused,
            title: "YouTube song",
            album: "YouTube album"
        )

        let preferred = NowPlayingStore.preferredSnapshot(
            snapshots: [
                .spotify: spotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: youtubeMusic,
            ],
            displayOrder: [.spotify, .appleMusic, .youtubeMusic],
            rememberedPlayer: .youtubeMusic
        )

        #expect(preferred?.player == .youtubeMusic)
    }

    @Test
    func newlyPlayingPlayerBecomesTheRememberedPlayer() {
        let previousSpotify = makeSnapshot(
            player: .spotify,
            state: .playing,
            title: "Spotify song",
            album: "Spotify album"
        )
        let previousYouTube = makeSnapshot(
            player: .youtubeMusic,
            state: .paused,
            title: "YouTube song",
            album: "YouTube album"
        )
        let currentYouTube = makeSnapshot(
            player: .youtubeMusic,
            state: .playing,
            title: "YouTube song",
            album: "YouTube album"
        )

        let player = NowPlayingStore.playerToRemember(
            previousSnapshots: [
                .spotify: previousSpotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: previousYouTube,
            ],
            currentSnapshots: [
                .spotify: previousSpotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: currentYouTube,
            ],
            displayOrder: [.spotify, .appleMusic, .youtubeMusic],
            rememberedPlayer: .spotify
        )

        #expect(player == .youtubeMusic)
    }

    @Test
    func appleMusicStartingPausesPreviouslyPlayingSpotify() {
        let player = NowPlayingStore.playerToPause(
            automaticPauseEnabled: true,
            previousSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            previousAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .paused,
                title: "Music song",
                album: "Music album"
            ),
            currentSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            currentAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            )
        )

        #expect(player == .spotify)
    }

    @Test
    func spotifyStartingPausesPreviouslyPlayingAppleMusic() {
        let player = NowPlayingStore.playerToPause(
            automaticPauseEnabled: true,
            previousSpotify: makeSnapshot(
                player: .spotify,
                state: .stopped,
                title: "Spotify song",
                album: "Spotify album"
            ),
            previousAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            ),
            currentSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            currentAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            )
        )

        #expect(player == .appleMusic)
    }

    @Test
    func simultaneousInitialPlaybackDoesNotPauseEitherPlayer() {
        let player = NowPlayingStore.playerToPause(
            automaticPauseEnabled: true,
            previousSpotify: .notRunning(.spotify),
            previousAppleMusic: .notRunning(.appleMusic),
            currentSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            currentAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            )
        )

        #expect(player == nil)
    }

    @Test
    func stoppedPreviousPlayerDoesNotReceiveAnAutomaticPause() {
        let player = NowPlayingStore.playerToPause(
            automaticPauseEnabled: true,
            previousSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            previousAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .paused,
                title: "Music song",
                album: "Music album"
            ),
            currentSpotify: makeSnapshot(
                player: .spotify,
                state: .paused,
                title: "Spotify song",
                album: "Spotify album"
            ),
            currentAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            )
        )

        #expect(player == nil)
    }

    @Test
    func disabledAutomaticPauseIgnoresAPlayerStarting() {
        let player = NowPlayingStore.playerToPause(
            automaticPauseEnabled: false,
            previousSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            previousAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .paused,
                title: "Music song",
                album: "Music album"
            ),
            currentSpotify: makeSnapshot(
                player: .spotify,
                state: .playing,
                title: "Spotify song",
                album: "Spotify album"
            ),
            currentAppleMusic: makeSnapshot(
                player: .appleMusic,
                state: .playing,
                title: "Music song",
                album: "Music album"
            )
        )

        #expect(player == nil)
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
    func playbackPositionAdvancesSmoothlyOnlyWhilePlaying() {
        let observedAt = Date(timeIntervalSinceReferenceDate: 100)
        let later = Date(timeIntervalSinceReferenceDate: 102.5)

        #expect(
            PlaybackPosition.estimated(
                observedPosition: 10,
                state: .playing,
                observedAt: observedAt,
                at: later,
                duration: 200
            ) == 12.5
        )
        #expect(
            PlaybackPosition.estimated(
                observedPosition: 10,
                state: .paused,
                observedAt: observedAt,
                at: later,
                duration: 200
            ) == 10
        )
        #expect(
            PlaybackPosition.estimated(
                observedPosition: 199,
                state: .playing,
                observedAt: observedAt,
                at: later,
                duration: 200
            ) == 200
        )
        #expect(
            PlaybackPosition.estimated(
                observedPosition: 10,
                state: .playing,
                observedAt: observedAt,
                at: later,
                duration: 200,
                playbackRate: 2
            ) == 15
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

    @Test
    func lyricsDisplayPreferencesUseExpectedDefaults() {
        let suiteName = "RepriseTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        defer {
            defaults.removePersistentDomain(forName: suiteName)
        }

        ReprisePreferences.registerDefaults(in: defaults)

        #expect(
            !defaults.bool(
                forKey: ReprisePreferenceKey.menuBarShowsLyrics
            )
        )
        #expect(
            defaults.bool(
                forKey: ReprisePreferenceKey.menuBarReservesLyricsWidth
            )
        )
        #expect(
            defaults.double(
                forKey: ReprisePreferenceKey.menuBarLyricsWidth
            ) == MenuBarLyricsWidth.defaultValue
        )
        #expect(!MarqueePreferences.current(defaults: defaults).menuBarShowsLyrics)
        #expect(
            MarqueePreferences.current(defaults: defaults)
                .menuBarReservesLyricsWidth
        )
        #expect(
            MarqueePreferences.current(defaults: defaults)
                .menuBarLyricsWidth
                == CGFloat(MenuBarLyricsWidth.defaultValue)
        )
    }

    @Test
    func vibeSearchParsesAndMatchesArtistAliases() throws {
        let xml = """
        <response><result><tracks>
          <tracks>
            <trackId>50639387</trackId>
            <trackTitle>Antifreeze</trackTitle>
            <artists><artists><artistName>백예린(Yerin Baek)</artistName></artists></artists>
            <album><albumTitle>선물</albumTitle></album>
            <hasSyncLyric>true</hasSyncLyric>
            <isAdult>false</isAdult>
            <playTime>04:05</playTime>
          </tracks>
          <tracks>
            <trackId>wrong</trackId>
            <trackTitle>Antifreeze</trackTitle>
            <artists><artists><artistName>Another Artist</artistName></artists></artists>
            <album><albumTitle>Another Album</albumTitle></album>
            <hasSyncLyric>true</hasSyncLyric>
            <isAdult>false</isAdult>
            <playTime>02:00</playTime>
          </tracks>
        </tracks></result></response>
        """
        let candidates = try LyricsService.parseVibeSearch(Data(xml.utf8))
        let query = LyricsTrackQuery(
            track: Track(
                title: "Antifreeze",
                album: "선물",
                artist: "Yerin Baek",
                duration: 245
            )
        )

        #expect(candidates.count == 2)
        #expect(
            LyricsService.bestVibeCandidate(
                for: query,
                candidates: candidates
            )?.trackID == "50639387"
        )
    }

    @Test
    func lyricsTrackIdentityIgnoresTransientDurationChanges() {
        let unavailableDuration = LyricsTrackQuery(
            track: Track(
                title: "Antifreeze",
                album: "선물",
                artist: "Yerin Baek",
                duration: 0
            )
        )
        let resolvedDuration = LyricsTrackQuery(
            track: Track(
                title: "Antifreeze",
                album: "선물",
                artist: "Yerin Baek",
                duration: 245
            )
        )

        #expect(unavailableDuration == resolvedDuration)
        #expect(
            Set([unavailableDuration, resolvedDuration]).count == 1
        )
    }

    @Test
    func vibeSyncedLyricsUseDefaultLanguageAndEndTimes() throws {
        let xml = """
        <response><result><lyric>
          <hasSyncLyric>true</hasSyncLyric>
          <syncLyric>
            <startTimeIndex>
              <startTimeIndex>1.3</startTimeIndex>
              <startTimeIndex>9.0</startTimeIndex>
            </startTimeIndex>
            <endTimeIndex>
              <endTimeIndex>8.0</endTimeIndex>
              <endTimeIndex>15.3</endTimeIndex>
            </endTimeIndex>
            <contents>
              <contents>
                <languageType>translation</languageType>
                <text><text>Translation one</text><text>Translation two</text></text>
              </contents>
              <contents>
                <languageType>default</languageType>
                <text><text>첫 번째 줄</text><text>두 번째 줄</text></text>
              </contents>
            </contents>
          </syncLyric>
        </lyric></result></response>
        """

        let lyrics = try LyricsService.parseVibeLyrics(Data(xml.utf8))

        #expect(lyrics?.source == .vibe)
        #expect(lyrics?.lines.count == 2)
        #expect(lyrics?.lines[0].text == "첫 번째 줄")
        #expect(lyrics?.lines[0].startTime == 1.3)
        #expect(lyrics?.lines[0].endTime == 8.0)
    }

    @Test
    func lrcParserSupportsOffsetsAndMultipleTimestamps() {
        let lrc = """
        [offset:200]
        [00:01.00][00:03.50]같은 가사
        [00:06.25]다음 가사
        [ar:Artist]
        """

        let lines = LyricsService.parseLRC(lrc, duration: 10)

        #expect(lines.count == 3)
        #expect(abs(lines[0].startTime - 1.2) < 0.001)
        #expect(abs(lines[1].startTime - 3.7) < 0.001)
        #expect(lines[0].text == "같은 가사")
        #expect(lines[2].text == "다음 가사")
        #expect(lines[2].endTime == 10)
    }

    @Test
    func syncedLyricsReturnNoLineOutsideVibeTiming() {
        let lyrics = SyncedLyrics(
            source: .vibe,
            lines: [
                LyricLine(startTime: 2, endTime: 5, text: "첫 줄"),
                LyricLine(startTime: 8, endTime: 11, text: "둘째 줄"),
            ]
        )

        #expect(lyrics.line(at: 1) == nil)
        #expect(lyrics.line(at: 3)?.text == "첫 줄")
        #expect(lyrics.line(at: 6) == nil)
        #expect(lyrics.focusedLineIndex(at: 6) == 0)
        #expect(lyrics.line(at: 9)?.text == "둘째 줄")
        #expect(lyrics.line(at: 12) == nil)
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
