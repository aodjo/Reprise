//
//  NowPlayingStore.swift
//  Reprise
//

import Foundation
import Observation

@MainActor
@Observable
final class NowPlayingStore {
    private(set) var spotify = PlayerSnapshot.notRunning(.spotify)
    private(set) var appleMusic = PlayerSnapshot.notRunning(.appleMusic)
    private(set) var youtubeMusic = PlayerSnapshot.notRunning(.youtubeMusic)
    private(set) var isRefreshing = false
    private(set) var commandError: String?
    private(set) var lyricsState = LyricsLoadState.idle
    private(set) var displayedLyricText: String?
    @ObservationIgnored
    var onMenuBarContentChange: (() -> Void)?

    private let automation = MediaAutomationService()
    private let lyricsService: LyricsService
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

    init(lyricsService: LyricsService = LyricsService()) {
        self.lyricsService = lyricsService
    }

    var activeSnapshot: PlayerSnapshot {
        menuBarSnapshot
            ?? snapshot(for: playerDisplayOrder.first ?? .spotify)
    }

    var menuBarSnapshot: PlayerSnapshot? {
        Self.preferredSnapshot(
            snapshots: snapshotsByPlayer,
            displayOrder: playerDisplayOrder,
            rememberedPlayer: rememberedPlayer
        )
    }

    private var snapshotsByPlayer: [MediaPlayerKind: PlayerSnapshot] {
        [
            .spotify: spotify,
            .appleMusic: appleMusic,
            .youtubeMusic: youtubeMusic,
        ]
    }

    private var playerDisplayOrder: [MediaPlayerKind] {
        ReprisePreferences.playerDisplayOrder()
    }

    private var automaticallyPausesOtherPlayer: Bool {
        ReprisePreferences.automaticallyPausesOtherPlayer()
    }

    private var rememberedPlayer: MediaPlayerKind? {
        guard ReprisePreferences.remembersLastPlayedPlayer() else {
            return nil
        }
        return ReprisePreferences.lastPlayedPlayer()
    }

    var menuBarTitle: String {
        menuBarSnapshot?.menuBarTitle ?? "Reprise"
    }

    var menuBarSymbol: String {
        menuBarSnapshot?.player.symbolName ?? "music.note"
    }

    var menuBarAccessibilityLabel: String {
        guard let snapshot = menuBarSnapshot, let track = snapshot.track else {
            return "Reprise, 재생 중인 음악 없음"
        }
        return "\(snapshot.player.displayName), \(track.title)"
    }

    var syncedLyrics: SyncedLyrics? {
        lyricsState.lyrics
    }

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

    func currentLyricLineIndex(
        at date: Date = Date()
    ) -> Int? {
        syncedLyrics?.focusedLineIndex(
            at: estimatedPlaybackPosition(at: date)
        )
    }

    func focusedLyricLineIndex(
        at date: Date = Date()
    ) -> Int? {
        currentLyricLineIndex(at: date)
    }

    func start() {
        guard pollingTask == nil else { return }

        lyricsDisplayTask = Task { [weak self] in
            while !Task.isCancelled {
                self?.updateDisplayedLyric()
                try? await Task.sleep(for: .milliseconds(100))
            }
        }

        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    func refresh() async {
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

    func perform(_ command: PlaybackCommand) async {
        commandError = nil
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

    func setVolume(_ volume: Int, for player: MediaPlayerKind) async {
        commandError = nil
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

    func seek(
        to position: TimeInterval,
        for player: MediaPlayerKind
    ) async {
        commandError = nil
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

    func ensureLyricsForActiveTrack() {
        synchronizeLyricsWithActiveTrack(observedAt: nil)
    }

    func snapshot(for player: MediaPlayerKind) -> PlayerSnapshot {
        switch player {
        case .spotify: spotify
        case .appleMusic: appleMusic
        case .youtubeMusic: youtubeMusic
        }
    }

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

private struct PlaybackAnchor {
    let query: LyricsTrackQuery
    let position: TimeInterval
    let duration: TimeInterval
    let state: PlaybackState
    let playbackRate: Double
    let observedAt: Date
}

private enum LyricsCacheEntry {
    case available(SyncedLyrics)
    case unavailable

    init(lyrics: SyncedLyrics?) {
        if let lyrics {
            self = .available(lyrics)
        } else {
            self = .unavailable
        }
    }

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
    static let displayedLyricDidChange = Notification.Name(
        "dev.junx.Reprise.displayedLyricDidChange"
    )
}
