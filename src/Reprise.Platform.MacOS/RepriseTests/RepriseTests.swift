// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import CoreGraphics
import Foundation
import ServiceManagement
import Testing
@testable import Reprise

/// Covers the logic Reprise's behaviour rests on, away from AppKit.
///
/// The app is almost entirely UI, so what is testable is what was deliberately
/// factored out of it: menu bar measurement, panel placement, player
/// selection, preference encoding, and lyrics parsing. Everything here runs
/// without a display, a media player, or the network.
///
/// Preference tests each build a throwaway `UserDefaults` suite and remove it
/// afterwards, so they neither read nor disturb the developer's own settings.
@MainActor
struct RepriseTests {
    /// The menu bar shows the track title alone, never the album.
    ///
    /// The album is carried in the snapshot and would be easy to append; the
    /// menu bar has too little room to spend on it.
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

    /// A title that fits is left alone.
    ///
    /// Checks all three consequences together: no scrolling, no offset however
    /// long it has been showing, and a viewport sized to the text rather than
    /// padded out to the maximum.
    @Test
    func shortMenuBarTitleDoesNotScroll() {
        let title = "Antifreeze"
        let titleWidth = MenuBarMarquee.textWidth(title)

        #expect(!MenuBarMarquee.requiresScrolling(titleWidth: titleWidth))
        #expect(MenuBarMarquee.offset(elapsed: 5, titleWidth: titleWidth) == 0)
        #expect(MenuBarMarquee.viewportWidth(for: titleWidth) == titleWidth)
    }

    /// An over-long title scrolls at a constant rate after its pause.
    ///
    /// The two offsets are sampled 0.1s apart just past the initial pause, and
    /// must differ by exactly the distance the default speed covers in that
    /// time - which is what pins the scroll to a constant rate rather than an
    /// eased one.
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

    /// The panel aligns with the status item and hangs below it.
    ///
    /// The fractional anchor is deliberate: status items rarely land on whole
    /// points, and the x origin must survive pixel alignment. At 2x, 300.5 is
    /// already on a pixel boundary and should pass through unchanged.
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

    /// A panel anchored at the screen edge is pulled back into view.
    ///
    /// Happens whenever the status item sits at the far left of the menu bar,
    /// where aligning with it would put the panel partly off screen.
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

    /// Another app taking focus closes the panel; Reprise itself does not.
    ///
    /// The negative case is the one that matters: opening Settings activates
    /// Reprise, and dismissing on that would close the panel the user just
    /// opened Settings from.
    @Test
    func playerPanelDismissesWhenAnotherApplicationActivates() {
        #expect(
            PlayerPanelActivationPolicy.shouldDismiss(
                activatedProcessIdentifier: 200,
                repriseProcessIdentifier: 100
            )
        )
        #expect(
            !PlayerPanelActivationPolicy.shouldDismiss(
                activatedProcessIdentifier: 100,
                repriseProcessIdentifier: 100
            )
        )
    }

    /// Every `SMAppService` status maps onto a login-item state.
    ///
    /// `requiresApproval` reads as on, since the user has already made the
    /// choice and only a system prompt remains.
    @Test
    func launchAtLoginReflectsServiceManagementStatus() {
        #expect(
            LaunchAtLoginState(status: .notRegistered) == .disabled
        )
        #expect(LaunchAtLoginState(status: .enabled) == .enabled)
        #expect(
            LaunchAtLoginState(status: .requiresApproval)
                == .requiresApproval
        )
        #expect(LaunchAtLoginState(status: .notFound) == .unavailable)
        #expect(!LaunchAtLoginState.disabled.isOn)
        #expect(LaunchAtLoginState.enabled.isOn)
        #expect(LaunchAtLoginState.requiresApproval.isOn)
    }

    /// Menu bar settings round-trip through `UserDefaults`.
    ///
    /// Every value is deliberately set away from its default, so a preference
    /// that silently failed to read back would show as its default rather than
    /// coincidentally matching.
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

    /// Each title format arranges title and artist as named.
    ///
    /// The blank-artist case guards the separator: a naive join would produce
    /// a trailing `Song - ` in the menu bar.
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

    /// A fresh install shows the title alone.
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

    /// A fresh install shows the album cover.
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

    /// Hiding the artwork drops its trailing gap as well.
    ///
    /// Keeping the gap would leave the title floating away from the menu bar
    /// items beside it.
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

    /// With no title, the item is exactly the artwork wide.
    ///
    /// The mirror of the previous case: the gap belongs between two things and
    /// must vanish when either is absent.
    @Test
    func hiddenMenuBarTextLeavesOnlyTheArtworkWidth() {
        #expect(
            MenuBarMarquee.totalWidth(
                for: 0,
                artworkStyle: .albumArtwork
            ) == MenuBarMarquee.artworkSize
        )
    }

    /// Reserved width holds steady across lyric lines of different lengths.
    ///
    /// Without it the item would resize on every line, shifting every status
    /// item to its left several times a minute.
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

    /// The configured lyrics width caps and reserves as expected.
    ///
    /// Covers all three cases: reserved, overflowing, and comfortably fitting.
    /// Only the last should size to the text.
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

    /// The upward push animates lyric lines only, within one track.
    ///
    /// The other two cases are what the animation must not claim: a track
    /// change is not one lyric giving way to the next, and neither is a plain
    /// title change.
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

    /// Hiding both the artwork and the title restores the artwork.
    ///
    /// The important guard in the whole file: that combination leaves an
    /// invisible menu bar item, and a user who reached it would have no way to
    /// click back into Reprise and undo it.
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

    /// A fresh install uses the Liquid panel theme.
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

    /// Reprise does not pause anything until asked to.
    ///
    /// Off by default deliberately: silently pausing a player the user started
    /// elsewhere would be surprising behaviour to inherit on install.
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

    /// The remembered player starts empty and round-trips once set.
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

    /// The default order follows the declaration order of the players.
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

    /// A saved order is honoured, with omitted players appended.
    ///
    /// Serialising two players and reading back three confirms the round trip
    /// always yields a complete order.
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

    /// A preference written before the setting was a list still decodes.
    ///
    /// Early builds stored a single player name. Reading one has to widen into
    /// a full order rather than leaving the other players unranked.
    @Test
    func legacySinglePlayerPriorityBecomesACompleteOrder() {
        #expect(
            ReprisePreferences.playerDisplayOrder(
                from: MediaPlayerKind.appleMusic.rawValue
            ) == [.appleMusic, .spotify, .youtubeMusic]
        )
    }

    /// The fade occupies a fixed width, however wide the viewport.
    ///
    /// A viewport narrower than the fade itself is the edge case: the location
    /// must floor at 0 rather than going negative, which would fade the entire
    /// line out.
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

    /// The menu bar item draws in a colour that contrasts with the menu bar.
    ///
    /// Regression test. The title, the level meter, and the fallback glyph
    /// were all drawn in a fixed white, which is invisible on a light menu bar
    /// - and the item is drawn into layer contents, which nothing tints on the
    /// app's behalf. Resolving `labelColor` per appearance is the fix, so the
    /// two appearances have to land on opposite sides of mid grey.
    ///
    /// - Throws: Rethrows a requirement failure to the test runner.
    @Test
    func menuBarForegroundColorFollowsTheMenuBarAppearance() throws {
        let lightAppearance = try #require(NSAppearance(named: .aqua))
        let darkAppearance = try #require(NSAppearance(named: .darkAqua))

        let onLightMenuBar = try #require(
            MenuBarMarquee.foregroundColor(for: lightAppearance)
                .usingColorSpace(.sRGB)
        )
        let onDarkMenuBar = try #require(
            MenuBarMarquee.foregroundColor(for: darkAppearance)
                .usingColorSpace(.sRGB)
        )

        #expect(onLightMenuBar.brightnessComponent < 0.5)
        #expect(onDarkMenuBar.brightnessComponent > 0.5)
    }

    /// A playing player outranks a paused one of higher priority.
    ///
    /// Priority is a tie-break, not an override: what is audible wins.
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

    /// With both playing, the user's priority decides.
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

    /// The remembered player wins among paused players.
    ///
    /// YouTube Music is last in the display order here, so only the memory can
    /// account for it being chosen.
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

    /// Starting a player makes it the remembered one.
    ///
    /// Spotify is playing throughout and is already remembered, so the switch
    /// to YouTube Music can only come from the transition into playing being
    /// what counts.
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

    /// Starting Music pauses the Spotify that was already playing.
    ///
    /// The core case of the automatic pause setting.
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

    /// The same rule holds with the players reversed.
    ///
    /// Guards against the pause target being decided by player order rather
    /// than by which one just started.
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

    /// Two players appearing at once pause neither.
    ///
    /// This is the shape of Reprise's first poll after launch: everything
    /// already running looks newly playing. Pausing on that would stop music
    /// the user started before Reprise was open.
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

    /// A player that has already stopped is not paused again.
    ///
    /// Spotify goes from playing to paused on its own here, so there is
    /// nothing left to pause and the command would be wasted.
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

    /// With the setting off, nothing is paused.
    ///
    /// Identical inputs to the passing case, so only the flag can account for
    /// the difference.
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

    /// A track with no album still yields a clean menu bar title.
    ///
    /// Singles routinely report an empty album, and the title must not pick up
    /// a separator or a blank from it.
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

    /// A denied automation permission explains how to grant it.
    ///
    /// OSA error -1743 is the one failure a user can actually fix, and the
    /// system's own message does not say where to go.
    @Test
    func automationDenialProvidesRecoveryInstructions() {
        let message = MediaAutomationService.userFacingMessage(
            for: AutomationError.appleScript(number: -1743, message: "Not authorized")
        )

        #expect(message.contains("자동화"))
        #expect(message.contains("시스템 설정"))
    }

    /// Progress and remaining time stay sane past the end of a track.
    ///
    /// Players briefly report a position beyond the duration while advancing;
    /// progress must cap at 1 and remaining must not go negative.
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

    /// Clamping handles negatives, overruns, infinity, and no duration.
    ///
    /// The infinity case matters most: a non-finite value reaching SwiftUI
    /// layout breaks the whole panel rather than one control.
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

    /// Estimated position advances while playing and holds while paused.
    ///
    /// Four cases: normal advance, paused hold, clamping at the end of a
    /// track, and a playback rate multiplying the elapsed time. This is what
    /// lets the progress bar move smoothly between twice-a-second polls.
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

    /// A seek is confirmed within tolerance but not by an unrelated position.
    ///
    /// The tolerance exists because a player that accepted a seek has already
    /// advanced by the time its position is read back; without it no seek
    /// would ever confirm.
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

    /// Volume stays within 0 to 100.
    @Test
    func playerVolumeIsClampedToTheSupportedRange() {
        #expect(PlayerVolume.clamped(-1) == 0)
        #expect(PlayerVolume.clamped(42) == 42)
        #expect(PlayerVolume.clamped(101) == 100)
    }

    /// Mute goes to zero; unmute returns to the remembered level.
    ///
    /// With nothing remembered it falls back to a default, so unmuting always
    /// produces sound rather than appearing to do nothing.
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

    /// Both time styles render on each side of the progress bar.
    ///
    /// The remaining style carries a minus sign, which is what distinguishes a
    /// countdown from a total at a glance.
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

    /// A fresh install shows elapsed on the left and remaining on the right.
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

    /// Lyrics are off by default, with width reserved once enabled.
    ///
    /// Checked both as raw defaults and through `MarqueePreferences`, since
    /// the two read the same keys by different paths.
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

    /// VIBE search results are parsed and the right track chosen.
    ///
    /// The two candidates share a title, so only the artist and album can
    /// separate them - and the artist is written as `백예린(Yerin Baek)` against
    /// a query of `Yerin Baek`, which is exactly the punctuation and script
    /// mismatch the normalisation exists to absorb.
    ///
    /// - Throws: Rethrows a parse failure to the test runner.
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

    /// A track's lyrics identity ignores its duration.
    ///
    /// Players report a duration of 0 for a moment when a track loads. If that
    /// were part of the identity, the lyrics cache would miss and every track
    /// would be fetched twice.
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

    /// VIBE lyrics use the original language and its explicit end times.
    ///
    /// The translation block is deliberately placed first, so picking the
    /// `default` language has to be a real choice rather than taking whichever
    /// came first.
    ///
    /// - Throws: Rethrows a parse failure to the test runner.
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

    /// LRC parsing handles offsets, repeated timestamps, and metadata.
    ///
    /// All three appear in real files. The offset shifts every timestamp by
    /// 0.2s, the repeated timestamps expand one refrain line into two entries,
    /// and the `[ar:]` tag must be skipped rather than parsed as a lyric. The
    /// last line closes at the track duration, so it does not hang in the menu
    /// bar after the song ends.
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

    /// A gap between lyric lines shows no line, but keeps the scroll position.
    ///
    /// The difference between the two lookups: at 6 seconds the first line has
    /// ended and nothing is current, yet it stays focused so the lyric sheet
    /// does not jump during an instrumental break.
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

    /// The shipped bundle points Sparkle at the signed appcast.
    ///
    /// These live in Info.plist rather than in code, so nothing else would
    /// catch a bad merge or a build setting overwriting them - and a wrong
    /// feed URL or public key is an update-channel problem, not a cosmetic
    /// one.
    @Test
    func sparkleUsesTheSignedRepriseAppcast() {
        let info = Bundle.main.infoDictionary

        #expect(
            info?["SUFeedURL"] as? String
                == "https://raw.githubusercontent.com/aodjo/Reprise/main/appcast.xml"
        )
        #expect(
            info?["SUPublicEDKey"] as? String
                == "JnauH8qHts9UuOjiOibjqdvtjSeryPHVXRFj5R8pUGc="
        )
        #expect(info?["SUEnableAutomaticChecks"] as? Bool == true)
        #expect(info?["SUAutomaticallyUpdate"] as? Bool == false)
        #expect(info?["SUEnableInstallerLauncherService"] as? Bool == true)
    }

    /// Builds a snapshot carrying only the fields these tests examine.
    ///
    /// Timing and volume are left at their defaults, since the selection and
    /// title rules do not consider them.
    ///
    /// - Parameters:
    ///   - player: Player the snapshot describes.
    ///   - state: Transport state.
    ///   - title: Track title.
    ///   - album: Album name.
    /// - Returns: A running snapshot with a loaded track.
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
