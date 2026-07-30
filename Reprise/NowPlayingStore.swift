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
    private(set) var isRefreshing = false
    private(set) var commandError: String?

    private let automation = MediaAutomationService()
    private var pollingTask: Task<Void, Never>?
    private var stateMutationRevision = 0

    var activeSnapshot: PlayerSnapshot {
        menuBarSnapshot
            ?? snapshot(for: playerDisplayOrder.first ?? .spotify)
    }

    var menuBarSnapshot: PlayerSnapshot? {
        Self.preferredSnapshot(
            spotify: spotify,
            appleMusic: appleMusic,
            displayOrder: playerDisplayOrder
        )
    }

    private var playerDisplayOrder: [MediaPlayerKind] {
        ReprisePreferences.playerDisplayOrder()
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

    func start() {
        guard pollingTask == nil else { return }

        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(for: .seconds(2))
            }
        }
    }

    func refresh() async {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }

        let refreshRevision = stateMutationRevision
        let snapshots = await automation.snapshots()
        guard refreshRevision == stateMutationRevision else {
            return
        }

        spotify = snapshots[.spotify] ?? .notRunning(.spotify)
        appleMusic = snapshots[.appleMusic] ?? .notRunning(.appleMusic)
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
        updateSnapshot(previousSnapshot.withVolume(volume), for: player)

        do {
            let actualVolume = try await automation.setVolume(
                volume,
                on: player
            )
            updateSnapshot(
                previousSnapshot.withVolume(actualVolume),
                for: player
            )
        } catch {
            updateSnapshot(previousSnapshot, for: player)
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

        let position = PlaybackPosition.clamped(
            position,
            duration: track.duration
        )
        updateSnapshot(
            previousSnapshot.withPosition(position),
            for: player
        )

        do {
            let actualPosition = try await automation.setPosition(
                position,
                on: player
            )
            // Invalidate any refresh that began while the player was still
            // settling on the requested position.
            stateMutationRevision &+= 1
            let currentSnapshot = snapshot(for: player)
            updateSnapshot(
                currentSnapshot.withPosition(
                    PlaybackPosition.clamped(
                        actualPosition,
                        duration: track.duration
                    )
                ),
                for: player
            )
        } catch {
            stateMutationRevision &+= 1
            updateSnapshot(previousSnapshot, for: player)
            commandError = MediaAutomationService.userFacingMessage(for: error)
        }
    }

    func snapshot(for player: MediaPlayerKind) -> PlayerSnapshot {
        switch player {
        case .spotify: spotify
        case .appleMusic: appleMusic
        }
    }

    static func preferredSnapshot(
        spotify: PlayerSnapshot,
        appleMusic: PlayerSnapshot,
        displayOrder: [MediaPlayerKind]
    ) -> PlayerSnapshot? {
        let snapshots: [MediaPlayerKind: PlayerSnapshot] = [
            .spotify: spotify,
            .appleMusic: appleMusic,
        ]
        let orderedPlayers = displayOrder + MediaPlayerKind.allCases.filter {
            !displayOrder.contains($0)
        }

        if let playing = orderedPlayers
            .compactMap({ snapshots[$0] })
            .first(where: { $0.state == .playing && $0.track != nil }) {
            return playing
        }

        return orderedPlayers
            .compactMap({ snapshots[$0] })
            .first(where: { $0.track != nil })
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
        }
    }
}

private extension PlayerSnapshot {
    func withVolume(_ volume: Int) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: isRunning,
            state: state,
            track: track,
            volume: volume,
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
            errorMessage: errorMessage
        )
    }
}
