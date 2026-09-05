// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation
import Observation

/// The app's single source of truth for what is playing.
///
/// Polls every player twice a second, decides which one the UI should follow,
/// fetches lyrics for it, and applies the user's commands. The panel, the menu
/// bar item, and the settings pane all read from here.
///
/// Two problems shape most of this type. The first is that a command and a
/// poll race: the poll is already in flight when the user drags the volume
/// slider, and its stale answer would arrive afterwards and undo the change.
/// That is handled by ``stateMutationRevision`` and the per-operation ids,
/// which let a mutation invalidate any poll that overlapped it.
///
/// The second is that polling at the rate a progress bar needs would be
/// prohibitively expensive, so playback position is anchored at each poll and
/// projected forward from the clock in between.
@MainActor
@Observable
final class NowPlayingStore {
    /// Latest Spotify state.
    private(set) var spotify = PlayerSnapshot.notRunning(.spotify)

    /// Latest Music state.
    private(set) var appleMusic = PlayerSnapshot.notRunning(.appleMusic)

    /// Latest YouTube Music state.
    private(set) var youtubeMusic = PlayerSnapshot.notRunning(.youtubeMusic)

    /// Whether a poll is in flight, so overlapping ones can be skipped.
    private(set) var isRefreshing = false

    /// Message from the last failed command, or `nil`.
    private(set) var commandError: String?

    /// Progress of the lyrics lookup for the current track.
    private(set) var lyricsState = LyricsLoadState.idle

    /// The lyric line showing right now, or `nil` when there is none.
    private(set) var displayedLyricText: String?

    /// Called when something the menu bar item draws has changed.
    ///
    /// A callback rather than observation because the status item is AppKit,
    /// which has no way to observe an `@Observable`. Excluded from observation
    /// so assigning it does not itself invalidate SwiftUI views.
    @ObservationIgnored
    var onMenuBarContentChange: (() -> Void)?

    private let automation = MediaAutomationService()
    private let lyricsService: LyricsService
    private let isDemoMode: Bool
    private var pollingTask: Task<Void, Never>?
    private var lyricsTask: Task<Void, Never>?
    private var lyricsDisplayTask: Task<Void, Never>?
    private var hasCompletedInitialRefresh = false
    private var stateMutationRevision = 0
    private var lyricsQuery: LyricsTrackQuery?
    private var lyricsCache: [LyricsTrackQuery: LyricsCacheEntry] = [:]
    private var playbackAnchor: PlaybackAnchor?
    private var volumeOperationIDs: [MediaPlayerKind: UUID] = [:]
    private var seekOperationIDs: [MediaPlayerKind: UUID] = [:]

    /// Creates the store.
    ///
    /// Nothing starts polling until ``start()``, so building a store is cheap
    /// and safe in previews.
    ///
    /// - Parameters:
    ///   - lyricsService: Service to fetch lyrics with. Injectable for tests.
    ///   - demoMode: Whether to present fabricated content. Defaults to
    ///     whatever the launch environment asks for.
    init(
        lyricsService: LyricsService = LyricsService(),
        demoMode: Bool = RepriseDemoMode.isEnabled()
    ) {
        self.lyricsService = lyricsService
        isDemoMode = demoMode

        if demoMode {
            installDemoContent()
        }
    }

    /// The snapshot the panel should display.
    ///
    /// Falls back to the highest-priority player when nothing has a track, so
    /// the panel always has something to render rather than going blank.
    var activeSnapshot: PlayerSnapshot {
        menuBarSnapshot
            ?? snapshot(for: playerDisplayOrder.first ?? .spotify)
    }

    /// The snapshot the menu bar should follow, or `nil` when none qualifies.
    var menuBarSnapshot: PlayerSnapshot? {
        Self.preferredSnapshot(
            snapshots: snapshotsByPlayer,
            displayOrder: playerDisplayOrder,
            rememberedPlayer: rememberedPlayer
        )
    }

    /// The three snapshots keyed by player, for the selection helpers.
    private var snapshotsByPlayer: [MediaPlayerKind: PlayerSnapshot] {
        [
            .spotify: spotify,
            .appleMusic: appleMusic,
            .youtubeMusic: youtubeMusic,
        ]
    }

    /// The user's player priority order.
    ///
    /// Read fresh each time rather than cached, so a change in settings takes
    /// effect on the next poll with no notification to wire up.
    private var playerDisplayOrder: [MediaPlayerKind] {
        ReprisePreferences.playerDisplayOrder()
    }

    /// Whether starting one player should pause the previous one.
    private var automaticallyPausesOtherPlayer: Bool {
        ReprisePreferences.automaticallyPausesOtherPlayer()
    }

    /// The remembered player, when that setting is on.
    ///
    /// Returns `nil` when the setting is off even if a player is stored, so
    /// callers do not have to check the setting themselves.
    private var rememberedPlayer: MediaPlayerKind? {
        guard ReprisePreferences.remembersLastPlayedPlayer() else {
            return nil
        }
        return ReprisePreferences.lastPlayedPlayer()
    }

    /// Title for the menu bar, falling back to the app name.
    var menuBarTitle: String {
        menuBarSnapshot?.menuBarTitle ?? "Reprise"
    }

    /// SF Symbol for the menu bar, falling back to a generic note.
    var menuBarSymbol: String {
        menuBarSnapshot?.player.symbolName ?? "music.note"
    }

    /// VoiceOver description of the menu bar item.
    ///
    /// Names the player as well as the track, since the icon conveys which one
    /// is playing to a sighted user and would otherwise be lost.
    var menuBarAccessibilityLabel: String {
        guard let snapshot = menuBarSnapshot, let track = snapshot.track else {
            return "Reprise, 재생 중인 음악 없음"
        }
        return "\(snapshot.player.displayName), \(track.title)"
    }

    /// Lyrics for the current track, when they have been found.
    var syncedLyrics: SyncedLyrics? {
        lyricsState.lyrics
    }

    /// Playback position projected to a given instant.
    ///
    /// The anchor is only trusted when it belongs to the track now playing;
    /// otherwise the snapshot's own position is used, so a track change cannot
    /// briefly show the previous track's progress advancing.
    ///
    /// - Parameter date: Instant to project to. Defaults to now.
    /// - Returns: The estimated position in seconds, clamped to the track.
    func estimatedPlaybackPosition(
        at date: Date = Date()
    ) -> TimeInterval {
        guard let snapshot = menuBarSnapshot,
              let track = snapshot.track else {
            return 0
        }
        let query = LyricsTrackQuery(track: track)
        guard let playbackAnchor,
              playbackAnchor.query == query else {
            return PlaybackPosition.clamped(
                track.position,
                duration: track.duration
            )
        }

        return PlaybackPosition.estimated(
            observedPosition: playbackAnchor.position,
            state: playbackAnchor.state,
            observedAt: playbackAnchor.observedAt,
            at: date,
            duration: playbackAnchor.duration,
            playbackRate: playbackAnchor.playbackRate
        )
    }

    /// The lyric line to highlight at a given instant.
    ///
    /// - Parameter date: Instant to evaluate. Defaults to now.
    /// - Returns: The focused line, or `nil` when there are no lyrics or
    ///   playback has not reached the first line.
    func currentLyricLine(
        at date: Date = Date()
    ) -> LyricLine? {
        guard let syncedLyrics,
              let index = syncedLyrics.focusedLineIndex(
                  at: estimatedPlaybackPosition(at: date)
              ) else {
            return nil
        }
        return syncedLyrics.lines[index]
    }

    /// Index of the lyric line to highlight at a given instant.
    ///
    /// - Parameter date: Instant to evaluate. Defaults to now.
    /// - Returns: The index, or `nil` when no line is focused.
    func currentLyricLineIndex(
        at date: Date = Date()
    ) -> Int? {
        syncedLyrics?.focusedLineIndex(
            at: estimatedPlaybackPosition(at: date)
        )
    }

    /// Index of the lyric line the scrolling view should centre on.
    ///
    /// Currently identical to ``currentLyricLineIndex(at:)``; kept as its own
    /// entry point because the scroll view's idea of focus is a presentation
    /// concern that may diverge from which line is strictly current.
    ///
    /// - Parameter date: Instant to evaluate. Defaults to now.
    /// - Returns: The index, or `nil`.
    func focusedLyricLineIndex(
        at date: Date = Date()
    ) -> Int? {
        currentLyricLineIndex(at: date)
    }

    /// Begins polling players and advancing the displayed lyric.
    ///
    /// Two loops, deliberately at different rates. Lyrics advance every 100ms
    /// because a late line is immediately noticeable against the music, and it
    /// costs only arithmetic against the anchor. Players are polled every
    /// 500ms because each poll means AppleScript round trips.
    ///
    /// Demo mode runs the lyric loop but never polls, so it neither reads nor
    /// touches the user's real players.
    ///
    /// Guarded so calling twice is harmless, which matters because the panel
    /// can appear more than once over the app's life.
    func start() {
        guard pollingTask == nil, lyricsDisplayTask == nil else { return }

        lyricsDisplayTask = Task { [weak self] in
            while !Task.isCancelled {
                self?.updateDisplayedLyric()
                try? await Task.sleep(for: .milliseconds(100))
            }
        }

        guard !isDemoMode else {
            onMenuBarContentChange?()
            return
        }

        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    /// Polls every player once and republishes the results.
    ///
    /// The revision check is what keeps a command from being undone: it is
    /// captured before the poll and compared after, so if the user changed
    /// something while the poll was in flight, its now-stale results are
    /// discarded entirely.
    ///
    /// Results that survive that check still pass through
    /// ``mergingPendingMutations(_:for:)``, which preserves the values of
    /// operations that are still settling.
    ///
    /// The first poll is exempt from automatic pausing: at launch every
    /// running player looks newly playing, and acting on that would pause
    /// music the user started before Reprise did.
    func refresh() async {
        guard !isDemoMode else {
            updateDisplayedLyric()
            return
        }
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }

        let refreshRevision = stateMutationRevision
        let snapshots = await automation.snapshots(
            automaticallyPausesOtherYouTubeMusicSessions:
                automaticallyPausesOtherPlayer
        )
        guard refreshRevision == stateMutationRevision else {
            return
        }

        let previousSnapshots = snapshotsByPlayer
        let currentSpotify = mergingPendingMutations(
            snapshots[.spotify] ?? .notRunning(.spotify),
            for: .spotify
        )
        let currentAppleMusic = mergingPendingMutations(
            snapshots[.appleMusic] ?? .notRunning(.appleMusic),
            for: .appleMusic
        )
        let currentYouTubeMusic = mergingPendingMutations(
            snapshots[.youtubeMusic] ?? .notRunning(.youtubeMusic),
            for: .youtubeMusic
        )

        spotify = currentSpotify
        appleMusic = currentAppleMusic
        youtubeMusic = currentYouTubeMusic
        rememberLastPlayedPlayerIfNeeded(
            previousSnapshots: previousSnapshots,
            currentSnapshots: snapshotsByPlayer
        )
        synchronizeLyricsWithActiveTrack(observedAt: Date())
        if MediaPlayerKind.allCases.contains(where: { player in
            Self.menuBarContentChanged(
                from: previousSnapshots[player]
                    ?? .notRunning(player),
                to: snapshotsByPlayer[player]
                    ?? .notRunning(player)
            )
        }) {
            onMenuBarContentChange?()
        }

        guard hasCompletedInitialRefresh else {
            hasCompletedInitialRefresh = true
            return
        }

        let playersToPause = Self.playersToPause(
            automaticPauseEnabled: automaticallyPausesOtherPlayer,
            previousSnapshots: previousSnapshots,
            currentSnapshots: snapshotsByPlayer
        )

        for player in playersToPause {
            await pauseAutomatically(player)
        }

        if !playersToPause.isEmpty {
            synchronizeLyricsWithActiveTrack(observedAt: Date())
            onMenuBarContentChange?()
        }
    }

    /// Sends a transport command to whichever player is on display.
    ///
    /// The brief sleep before refreshing lets the player settle: read
    /// immediately, Spotify and Music often still report the previous track,
    /// which would flash the old title in the panel.
    ///
    /// - Parameter command: Transport control to invoke.
    func perform(_ command: PlaybackCommand) async {
        commandError = nil
        if isDemoMode {
            performDemoCommand(command)
            return
        }
        stateMutationRevision &+= 1
        let targetPlayer = activeSnapshot.player

        do {
            try await automation.perform(command, on: targetPlayer)
            try? await Task.sleep(for: .milliseconds(250))
            await refresh()
        } catch {
            commandError = MediaAutomationService.userFacingMessage(for: error)
        }
    }

    /// Sets a player's volume.
    ///
    /// The new value is applied locally first so the slider tracks the
    /// pointer, then reconciled with what the player actually took. On failure
    /// the previous value is restored rather than left showing a change that
    /// never happened.
    ///
    /// The operation id guards against overlapping drags: a slider produces
    /// many calls in quick succession, and without it an earlier, slower one
    /// could land after a later one and snap the volume backwards. Only the
    /// most recent operation is allowed to write.
    ///
    /// - Parameters:
    ///   - volume: Desired level from 0 to 100.
    ///   - player: Player to adjust.
    func setVolume(_ volume: Int, for player: MediaPlayerKind) async {
        commandError = nil
        if isDemoMode {
            guard player == RepriseDemoContent.player else { return }
            updateSnapshot(
                snapshot(for: player).withVolume(
                    PlayerVolume.clamped(volume)
                ),
                for: player
            )
            onMenuBarContentChange?()
            return
        }
        stateMutationRevision &+= 1
        let previousSnapshot = snapshot(for: player)
        let volume = PlayerVolume.clamped(volume)
        let operationID = UUID()
        volumeOperationIDs[player] = operationID
        defer {
            if volumeOperationIDs[player] == operationID {
                volumeOperationIDs[player] = nil
            }
        }
        updateSnapshot(previousSnapshot.withVolume(volume), for: player)

        do {
            let actualVolume = try await automation.setVolume(
                volume,
                on: player
            )
            guard volumeOperationIDs[player] == operationID else { return }
            stateMutationRevision &+= 1
            let currentSnapshot = snapshot(for: player)
            updateSnapshot(
                currentSnapshot.withVolume(actualVolume),
                for: player
            )
        } catch {
            guard !Task.isCancelled,
                  !(error is CancellationError) else { return }
            guard volumeOperationIDs[player] == operationID else { return }
            stateMutationRevision &+= 1
            if let previousVolume = previousSnapshot.volume {
                let currentSnapshot = snapshot(for: player)
                updateSnapshot(
                    currentSnapshot.withVolume(previousVolume),
                    for: player
                )
            }
            commandError = MediaAutomationService.userFacingMessage(for: error)
        }
    }

    /// Seeks a player to a position.
    ///
    /// Applied locally first so the progress bar follows the drag, then
    /// reconciled against the position the player confirms.
    ///
    /// Beyond the operation id, every write is gated on the track still being
    /// the one that was seeked. A seek can take a second to confirm, which is
    /// long enough for the track to change - and writing a position from the
    /// previous track into the new one would put the progress bar and the
    /// lyrics somewhere arbitrary.
    ///
    /// - Parameters:
    ///   - position: Target position in seconds.
    ///   - player: Player to seek.
    func seek(
        to position: TimeInterval,
        for player: MediaPlayerKind
    ) async {
        commandError = nil
        if isDemoMode {
            guard player == RepriseDemoContent.player,
                  let track = snapshot(for: player).track else {
                return
            }
            updateSnapshot(
                snapshot(for: player).withPosition(
                    PlaybackPosition.clamped(
                        position,
                        duration: track.duration
                    )
                ),
                for: player
            )
            installDemoPlaybackAnchor()
            updateDisplayedLyric()
            onMenuBarContentChange?()
            return
        }
        stateMutationRevision &+= 1
        let previousSnapshot = snapshot(for: player)
        guard let track = previousSnapshot.track,
              track.duration > 0 else {
            return
        }
        let trackQuery = LyricsTrackQuery(track: track)
        let operationID = UUID()
        seekOperationIDs[player] = operationID
        defer {
            if seekOperationIDs[player] == operationID {
                seekOperationIDs[player] = nil
            }
        }

        let position = PlaybackPosition.clamped(
            position,
            duration: track.duration
        )
        updateSnapshot(
            previousSnapshot.withPosition(position),
            for: player
        )
        synchronizeLyricsWithActiveTrack(observedAt: Date())

        do {
            let actualPosition = try await automation.setPosition(
                position,
                on: player
            )
            guard seekOperationIDs[player] == operationID,
                  let currentTrack = snapshot(for: player).track,
                  LyricsTrackQuery(track: currentTrack) == trackQuery else {
                return
            }
            // Invalidate any refresh that began while the player was still
            // settling on the requested position.
            stateMutationRevision &+= 1
            let currentSnapshot = snapshot(for: player)
            updateSnapshot(
                currentSnapshot.withPosition(
                    PlaybackPosition.clamped(
                        actualPosition,
                        duration: currentTrack.duration
                    )
                ),
                for: player
            )
            synchronizeLyricsWithActiveTrack(observedAt: Date())
        } catch {
            guard !Task.isCancelled,
                  !(error is CancellationError) else { return }
            guard seekOperationIDs[player] == operationID,
                  let currentTrack = snapshot(for: player).track,
                  LyricsTrackQuery(track: currentTrack) == trackQuery else {
                return
            }
            stateMutationRevision &+= 1
            let currentSnapshot = snapshot(for: player)
            updateSnapshot(
                currentSnapshot.withPosition(track.position),
                for: player
            )
            synchronizeLyricsWithActiveTrack(observedAt: Date())
            commandError = MediaAutomationService.userFacingMessage(for: error)
        }
    }

    /// Starts a lyrics lookup for the current track if one is not under way.
    ///
    /// Called when the panel opens, since lyrics are only fetched when the
    /// track changes and a user opening the panel mid-track would otherwise
    /// see nothing until the next one.
    ///
    /// Passes no observation date, which leaves the playback anchor alone: the
    /// anchor is still valid, and resetting it here would jump the progress
    /// bar back to the last polled position.
    func ensureLyricsForActiveTrack() {
        synchronizeLyricsWithActiveTrack(observedAt: nil)
    }

    /// The stored snapshot for one player.
    ///
    /// - Parameter player: Player to read.
    /// - Returns: Its most recent snapshot.
    func snapshot(for player: MediaPlayerKind) -> PlayerSnapshot {
        switch player {
        case .spotify: spotify
        case .appleMusic: appleMusic
        case .youtubeMusic: youtubeMusic
        }
    }

    /// Chooses which player the UI should follow.
    ///
    /// Four passes, in order. A remembered player that is playing wins
    /// outright, which is what makes that setting mean anything. Otherwise the
    /// highest-priority player that is playing takes it. Failing that the same
    /// two passes repeat against players that merely have a track loaded, so
    /// the panel shows a paused track rather than nothing.
    ///
    /// Players absent from the display order are appended, so a stored order
    /// written before a player existed still yields a complete ranking.
    ///
    /// Static and parameterised so the selection rules can be tested without a
    /// store, real players, or preferences.
    ///
    /// - Parameters:
    ///   - snapshots: Current state of every player.
    ///   - displayOrder: The user's priority order.
    ///   - rememberedPlayer: Player to favour, or `nil`. Defaults to `nil`.
    /// - Returns: The snapshot to follow, or `nil` when no player has a track.
    static func preferredSnapshot(
        snapshots: [MediaPlayerKind: PlayerSnapshot],
        displayOrder: [MediaPlayerKind],
        rememberedPlayer: MediaPlayerKind? = nil
    ) -> PlayerSnapshot? {
        let orderedPlayers = displayOrder + MediaPlayerKind.allCases.filter {
            !displayOrder.contains($0)
        }

        if let rememberedPlayer,
           let remembered = snapshots[rememberedPlayer],
           remembered.state == .playing,
           remembered.track != nil {
            return remembered
        }

        if let playing = orderedPlayers
            .compactMap({ snapshots[$0] })
            .first(where: { $0.state == .playing && $0.track != nil }) {
            return playing
        }

        if let rememberedPlayer,
           let remembered = snapshots[rememberedPlayer],
           remembered.track != nil {
            return remembered
        }

        return orderedPlayers
            .compactMap({ snapshots[$0] })
            .first(where: { $0.track != nil })
    }

    /// Two-player convenience over
    /// ``preferredSnapshot(snapshots:displayOrder:rememberedPlayer:)``.
    ///
    /// Predates YouTube Music support and is kept for the tests written
    /// against it; it fills YouTube Music in as not running.
    ///
    /// - Parameters:
    ///   - spotify: Spotify state.
    ///   - appleMusic: Music state.
    ///   - displayOrder: The user's priority order.
    /// - Returns: The snapshot to follow, or `nil`.
    static func preferredSnapshot(
        spotify: PlayerSnapshot,
        appleMusic: PlayerSnapshot,
        displayOrder: [MediaPlayerKind]
    ) -> PlayerSnapshot? {
        preferredSnapshot(
            snapshots: [
                .spotify: spotify,
                .appleMusic: appleMusic,
                .youtubeMusic: .notRunning(.youtubeMusic),
            ],
            displayOrder: displayOrder
        )
    }

    /// Decides which player to record as last played.
    ///
    /// A player that has just started outranks one that was already going,
    /// since starting something is the clearest statement of what the user
    /// wants to hear.
    ///
    /// Returns `nil` when the remembered player is still among those playing,
    /// so a second player starting alongside it does not quietly steal the
    /// memory from the one the user chose.
    ///
    /// - Parameters:
    ///   - previousSnapshots: State before this poll.
    ///   - currentSnapshots: State after this poll.
    ///   - displayOrder: The user's priority order.
    ///   - rememberedPlayer: Currently remembered player, if any.
    /// - Returns: The player to remember, or `nil` to leave it unchanged.
    static func playerToRemember(
        previousSnapshots: [MediaPlayerKind: PlayerSnapshot],
        currentSnapshots: [MediaPlayerKind: PlayerSnapshot],
        displayOrder: [MediaPlayerKind],
        rememberedPlayer: MediaPlayerKind?
    ) -> MediaPlayerKind? {
        let orderedPlayers = displayOrder + MediaPlayerKind.allCases.filter {
            !displayOrder.contains($0)
        }
        let playingPlayers = orderedPlayers.filter {
            currentSnapshots[$0]?.state == .playing
                && currentSnapshots[$0]?.track != nil
        }

        if let newlyPlaying = playingPlayers.first(where: {
            previousSnapshots[$0]?.state != .playing
        }) {
            return newlyPlaying
        }

        if let rememberedPlayer,
           playingPlayers.contains(rememberedPlayer) {
            return nil
        }
        return playingPlayers.first
    }

    /// Decides which players should be paused because another just started.
    ///
    /// Only fires on a transition into playing, and only pauses players that
    /// were already playing before and still are. Both halves matter: without
    /// the transition check every poll would re-pause, and without requiring
    /// the other player to have been playing beforehand, two players starting
    /// in the same poll would each try to pause the other.
    ///
    /// - Parameters:
    ///   - automaticPauseEnabled: Whether the setting is on.
    ///   - previousSnapshots: State before this poll.
    ///   - currentSnapshots: State after this poll.
    /// - Returns: Players to pause, without duplicates. Empty when the setting
    ///   is off or nothing started.
    static func playersToPause(
        automaticPauseEnabled: Bool,
        previousSnapshots: [MediaPlayerKind: PlayerSnapshot],
        currentSnapshots: [MediaPlayerKind: PlayerSnapshot]
    ) -> [MediaPlayerKind] {
        guard automaticPauseEnabled else {
            return []
        }

        var result: [MediaPlayerKind] = []
        for newlyPlaying in MediaPlayerKind.allCases {
            guard previousSnapshots[newlyPlaying]?.state != .playing,
                  currentSnapshots[newlyPlaying]?.state == .playing else {
                continue
            }

            for player in MediaPlayerKind.allCases
            where player != newlyPlaying
                && previousSnapshots[player]?.state == .playing
                && currentSnapshots[player]?.state == .playing
                && !result.contains(player) {
                result.append(player)
            }
        }

        return result
    }

    /// Whether a change affects anything the menu bar draws.
    ///
    /// Deliberately ignores position and volume, which change on every poll
    /// and are not shown in the menu bar. Redrawing on those would rebuild the
    /// status item constantly and restart its marquee mid-scroll.
    ///
    /// Artwork is compared only on presence, since the bytes change with every
    /// track but the layout only changes when a cover appears or disappears.
    ///
    /// - Parameters:
    ///   - previous: State before this poll.
    ///   - current: State after this poll.
    /// - Returns: `true` when the menu bar item needs rebuilding.
    private static func menuBarContentChanged(
        from previous: PlayerSnapshot,
        to current: PlayerSnapshot
    ) -> Bool {
        previous.player != current.player
            || previous.isRunning != current.isRunning
            || previous.state != current.state
            || previous.track?.title != current.track?.title
            || previous.track?.album != current.track?.album
            || previous.track?.artist != current.track?.artist
            || (previous.track?.artworkData == nil)
                != (current.track?.artworkData == nil)
    }

    /// Single-player convenience over
    /// ``playersToPause(automaticPauseEnabled:previousSnapshots:currentSnapshots:)``.
    ///
    /// - Parameters:
    ///   - automaticPauseEnabled: Whether the setting is on.
    ///   - previousSnapshots: State before this poll.
    ///   - currentSnapshots: State after this poll.
    /// - Returns: The first player to pause, or `nil`.
    static func playerToPause(
        automaticPauseEnabled: Bool,
        previousSnapshots: [MediaPlayerKind: PlayerSnapshot],
        currentSnapshots: [MediaPlayerKind: PlayerSnapshot]
    ) -> MediaPlayerKind? {
        playersToPause(
            automaticPauseEnabled: automaticPauseEnabled,
            previousSnapshots: previousSnapshots,
            currentSnapshots: currentSnapshots
        ).first
    }

    /// Two-player convenience over
    /// ``playerToPause(automaticPauseEnabled:previousSnapshots:currentSnapshots:)``.
    ///
    /// Predates YouTube Music support and is kept for the tests written
    /// against it.
    ///
    /// - Parameters:
    ///   - automaticPauseEnabled: Whether the setting is on.
    ///   - previousSpotify: Spotify state before this poll.
    ///   - previousAppleMusic: Music state before this poll.
    ///   - currentSpotify: Spotify state after this poll.
    ///   - currentAppleMusic: Music state after this poll.
    /// - Returns: The player to pause, or `nil`.
    static func playerToPause(
        automaticPauseEnabled: Bool,
        previousSpotify: PlayerSnapshot,
        previousAppleMusic: PlayerSnapshot,
        currentSpotify: PlayerSnapshot,
        currentAppleMusic: PlayerSnapshot
    ) -> MediaPlayerKind? {
        let previousSnapshots: [MediaPlayerKind: PlayerSnapshot] = [
            .spotify: previousSpotify,
            .appleMusic: previousAppleMusic,
            .youtubeMusic: .notRunning(.youtubeMusic),
        ]
        let currentSnapshots: [MediaPlayerKind: PlayerSnapshot] = [
            .spotify: currentSpotify,
            .appleMusic: currentAppleMusic,
            .youtubeMusic: .notRunning(.youtubeMusic),
        ]
        return playerToPause(
            automaticPauseEnabled: automaticPauseEnabled,
            previousSnapshots: previousSnapshots,
            currentSnapshots: currentSnapshots
        )
    }

    /// Pauses a player on the app's initiative, not the user's.
    ///
    /// The local state is updated immediately rather than waiting for the next
    /// poll, so the panel does not show two players playing at once for the
    /// half-second in between.
    ///
    /// - Parameter player: Player to pause.
    private func pauseAutomatically(
        _ player: MediaPlayerKind
    ) async {
        stateMutationRevision &+= 1

        do {
            try await automation.perform(.pause, on: player)
            updateSnapshot(
                snapshot(for: player).withPlaybackState(.paused),
                for: player
            )
        } catch {
            commandError = MediaAutomationService.userFacingMessage(
                for: error
            )
        }
    }

    /// Records the last played player when that setting is on.
    ///
    /// Writes only on an actual change, since this runs on every poll and
    /// `UserDefaults` would otherwise be written twice a second.
    ///
    /// - Parameters:
    ///   - previousSnapshots: State before this poll.
    ///   - currentSnapshots: State after this poll.
    private func rememberLastPlayedPlayerIfNeeded(
        previousSnapshots: [MediaPlayerKind: PlayerSnapshot],
        currentSnapshots: [MediaPlayerKind: PlayerSnapshot]
    ) {
        guard ReprisePreferences.remembersLastPlayedPlayer(),
              let player = Self.playerToRemember(
                  previousSnapshots: previousSnapshots,
                  currentSnapshots: currentSnapshots,
                  displayOrder: playerDisplayOrder,
                  rememberedPlayer: ReprisePreferences.lastPlayedPlayer()
              ),
              player != ReprisePreferences.lastPlayedPlayer() else {
            return
        }
        ReprisePreferences.setLastPlayedPlayer(player)
    }

    /// Stores a snapshot into the property for its player.
    ///
    /// - Parameters:
    ///   - snapshot: New state.
    ///   - player: Player it belongs to.
    private func updateSnapshot(
        _ snapshot: PlayerSnapshot,
        for player: MediaPlayerKind
    ) {
        switch player {
        case .spotify:
            spotify = snapshot
        case .appleMusic:
            appleMusic = snapshot
        case .youtubeMusic:
            youtubeMusic = snapshot
        }
    }

    /// Seeds the store with the fabricated demo track.
    ///
    /// Marks the initial refresh complete so the automatic-pause logic stays
    /// dormant, and installs the lyrics directly rather than fetching them, so
    /// demo mode makes no network requests.
    private func installDemoContent() {
        youtubeMusic = RepriseDemoContent.snapshot()
        lyricsState = .available(RepriseDemoContent.lyrics)
        lyricsQuery = youtubeMusic.track.map(LyricsTrackQuery.init)
        hasCompletedInitialRefresh = true
        installDemoPlaybackAnchor()
        updateDisplayedLyric()
    }

    /// Re-anchors demo playback at the current fabricated position.
    ///
    /// The demo snapshot carries a playback rate of 0, so the anchor holds the
    /// position exactly where it was put - which is what keeps a screenshot
    /// reproducible however long the app has been open.
    private func installDemoPlaybackAnchor() {
        guard let track = youtubeMusic.track else {
            playbackAnchor = nil
            return
        }
        playbackAnchor = PlaybackAnchor(
            query: LyricsTrackQuery(track: track),
            position: track.position,
            duration: track.duration,
            state: youtubeMusic.state,
            playbackRate: youtubeMusic.playbackRate,
            observedAt: Date()
        )
    }

    /// Applies a transport command to the fabricated demo state.
    ///
    /// Previous and next jump to fixed positions rather than changing track,
    /// since demo mode has only one. The positions are chosen to land on
    /// different lyric lines, so a screenshot of the controls shows the lyric
    /// view responding.
    ///
    /// - Parameter command: Transport control to simulate.
    private func performDemoCommand(_ command: PlaybackCommand) {
        let current = youtubeMusic
        let state: PlaybackState
        var position = current.track?.position ?? RepriseDemoContent.position

        switch command {
        case .pause:
            state = .paused
        case .playPause:
            state = current.state.isPlaying ? .paused : .playing
        case .stop:
            state = .stopped
        case .previous:
            state = .playing
            position = 32
        case .next:
            state = .playing
            position = 116
        }

        youtubeMusic = RepriseDemoContent.snapshot(
            state: state,
            position: position,
            volume: current.volume ?? RepriseDemoContent.volume
        )
        installDemoPlaybackAnchor()
        updateDisplayedLyric()
        onMenuBarContentChange?()
    }

    /// Keeps in-flight command values from being overwritten by a poll.
    ///
    /// A poll that began before a command still returns the pre-command state.
    /// Where an operation is outstanding, the local value is carried into the
    /// incoming snapshot so the slider or progress bar does not visibly snap
    /// back and then forward again.
    ///
    /// Position is only preserved when the track has not changed: holding a
    /// stale position across a track change would be worse than accepting the
    /// poll's value.
    ///
    /// - Parameters:
    ///   - incomingSnapshot: Snapshot from the poll.
    ///   - player: Player it describes.
    /// - Returns: The snapshot with pending values preserved.
    private func mergingPendingMutations(
        _ incomingSnapshot: PlayerSnapshot,
        for player: MediaPlayerKind
    ) -> PlayerSnapshot {
        let currentSnapshot = snapshot(for: player)
        var mergedSnapshot = incomingSnapshot

        if volumeOperationIDs[player] != nil,
           let pendingVolume = currentSnapshot.volume {
            mergedSnapshot = mergedSnapshot.withVolume(pendingVolume)
        }

        if seekOperationIDs[player] != nil,
           let currentTrack = currentSnapshot.track,
           let incomingTrack = incomingSnapshot.track,
           LyricsTrackQuery(track: currentTrack)
                == LyricsTrackQuery(track: incomingTrack) {
            mergedSnapshot = mergedSnapshot.withPosition(
                currentTrack.position
            )
        }

        return mergedSnapshot
    }

    /// Re-anchors playback and fetches lyrics when the track changes.
    ///
    /// Runs on every poll, so it is written to do nothing in the common case
    /// where the track has not changed - the lyrics lookup is gated on the
    /// query differing.
    ///
    /// Results are cached by track, which matters because the same track comes
    /// back on repeat or when switching between players, and re-fetching would
    /// mean a needless round trip and a visible blank while it loads.
    ///
    /// - Parameter observedAt: When the position was observed, which re-anchors
    ///   the estimate. Pass `nil` to leave an existing anchor for the same
    ///   track alone, which is what callers not reporting a fresh position -
    ///   the panel opening - want.
    private func synchronizeLyricsWithActiveTrack(
        observedAt: Date?
    ) {
        guard let snapshot = menuBarSnapshot,
              let track = snapshot.track else {
            lyricsTask?.cancel()
            lyricsTask = nil
            lyricsQuery = nil
            playbackAnchor = nil
            lyricsState = .idle
            updateDisplayedLyric()
            return
        }

        let query = LyricsTrackQuery(track: track)
        let anchorDate = observedAt
            ?? (playbackAnchor?.query == query ? nil : Date())
        if let anchorDate {
            playbackAnchor = PlaybackAnchor(
                query: query,
                position: track.position,
                duration: track.duration,
                state: snapshot.state,
                playbackRate: snapshot.playbackRate,
                observedAt: anchorDate
            )
        }

        guard lyricsQuery != query else { return }
        lyricsTask?.cancel()
        lyricsQuery = query
        lyricsState = .loading
        updateDisplayedLyric()

        if let cached = lyricsCache[query] {
            lyricsState = cached.state
            updateDisplayedLyric()
            return
        }

        let service = lyricsService
        lyricsTask = Task { [weak self] in
            let lyrics = await service.fetchSyncedLyrics(for: query)
            guard !Task.isCancelled, let self else { return }
            let entry = LyricsCacheEntry(lyrics: lyrics)
            lyricsCache[query] = entry
            guard lyricsQuery == query else { return }
            lyricsState = entry.state
            updateDisplayedLyric()
            lyricsTask = nil
        }
    }

    /// Recomputes the current lyric line and notifies when it changes.
    ///
    /// Called ten times a second, so it returns early unless the line actually
    /// changed - roughly once every few seconds. Without that check the menu
    /// bar item would be rebuilt on every tick.
    ///
    /// - Parameter date: Instant to evaluate. Defaults to now.
    private func updateDisplayedLyric(
        at date: Date = Date()
    ) {
        let text = currentLyricLine(at: date)?.text
        guard displayedLyricText != text else { return }
        displayedLyricText = text
        NotificationCenter.default.post(
            name: .displayedLyricDidChange,
            object: self
        )
        onMenuBarContentChange?()
    }
}

/// A playback position with enough context to project it forward.
///
/// The query is what ties it to a specific track, so a stale anchor is
/// recognised rather than applied to whatever is playing now.
private struct PlaybackAnchor {
    /// Track this anchor belongs to.
    let query: LyricsTrackQuery

    /// Position observed at ``observedAt``, in seconds.
    let position: TimeInterval

    /// Track length in seconds, for clamping the estimate.
    let duration: TimeInterval

    /// Playback state at the observation, which decides whether time advances.
    let state: PlaybackState

    /// Speed multiplier to project at.
    let playbackRate: Double

    /// When the position was observed.
    let observedAt: Date
}

/// A settled lyrics lookup, cached by track.
///
/// Distinct from ``LyricsLoadState`` because only the two final outcomes are
/// worth caching: `idle` and `loading` describe a lookup in progress, and
/// storing either would let a later read conclude the answer was already
/// known. Caching `unavailable` is the point - it stops a track with no lyrics
/// being looked up again every time it comes round.
private enum LyricsCacheEntry {
    /// Lyrics were found.
    case available(SyncedLyrics)

    /// Neither service had lyrics for this track.
    case unavailable

    /// Wraps a lookup result.
    ///
    /// - Parameter lyrics: The lyrics found, or `nil`.
    init(lyrics: SyncedLyrics?) {
        if let lyrics {
            self = .available(lyrics)
        } else {
            self = .unavailable
        }
    }

    /// The published state this entry corresponds to.
    var state: LyricsLoadState {
        switch self {
        case let .available(lyrics):
            .available(lyrics)
        case .unavailable:
            .unavailable
        }
    }
}

private extension PlayerSnapshot {
    /// A copy with a different playback state.
    ///
    /// - Parameter state: New transport state.
    /// - Returns: The updated snapshot.
    func withPlaybackState(
        _ state: PlaybackState
    ) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: isRunning,
            state: state,
            track: track,
            volume: volume,
            playbackRate: playbackRate,
            errorMessage: errorMessage
        )
    }

    /// A copy with a different volume.
    ///
    /// - Parameter volume: New level.
    /// - Returns: The updated snapshot.
    func withVolume(_ volume: Int) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: isRunning,
            state: state,
            track: track,
            volume: volume,
            playbackRate: playbackRate,
            errorMessage: errorMessage
        )
    }

    /// A copy with a different playback position.
    ///
    /// Rebuilds the track because ``Track`` is immutable. A snapshot with no
    /// track passes through unchanged, since there is nothing to position.
    ///
    /// - Parameter position: New position in seconds.
    /// - Returns: The updated snapshot.
    func withPosition(_ position: TimeInterval) -> PlayerSnapshot {
        let updatedTrack = track.map {
            Track(
                title: $0.title,
                album: $0.album,
                artist: $0.artist,
                duration: $0.duration,
                position: position,
                artworkData: $0.artworkData
            )
        }

        return PlayerSnapshot(
            player: player,
            isRunning: isRunning,
            state: state,
            track: updatedTrack,
            volume: volume,
            playbackRate: playbackRate,
            errorMessage: errorMessage
        )
    }
}

extension Notification.Name {
    /// Posted when the lyric line on display changes.
    ///
    /// Lets the AppKit menu bar item update its lyric text, which cannot
    /// observe the store's `@Observable` state.
    static let displayedLyricDidChange = Notification.Name(
        "dev.junx.Reprise.displayedLyricDidChange"
    )
}
