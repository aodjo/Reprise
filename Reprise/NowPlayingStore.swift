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
    private(set) var selectedPlayer: MediaPlayerKind = .spotify
    private(set) var isRefreshing = false
    private(set) var commandError: String?

    private let automation = MediaAutomationService()
    private var pollingTask: Task<Void, Never>?
    private var hasCompletedInitialRefresh = false

    var activeSnapshot: PlayerSnapshot {
        menuBarSnapshot ?? snapshot(for: selectedPlayer)
    }

    var menuBarSnapshot: PlayerSnapshot? {
        Self.preferredSnapshot(
            spotify: spotify,
            appleMusic: appleMusic,
            selectedPlayer: selectedPlayer
        )
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

        let snapshots = await automation.snapshots()
        spotify = snapshots[.spotify] ?? .notRunning(.spotify)
        appleMusic = snapshots[.appleMusic] ?? .notRunning(.appleMusic)

        if !hasCompletedInitialRefresh {
            if let initiallyPreferred = menuBarSnapshot {
                selectedPlayer = initiallyPreferred.player
            } else if let runningPlayer = MediaPlayerKind.allCases.first(
                where: { snapshot(for: $0).isRunning }
            ) {
                selectedPlayer = runningPlayer
            }
            hasCompletedInitialRefresh = true
        } else if !snapshot(for: selectedPlayer).isRunning,
                  let runningPlayer = MediaPlayerKind.allCases.first(
                    where: { snapshot(for: $0).isRunning }
                  ) {
            selectedPlayer = runningPlayer
        }
    }

    func perform(_ command: PlaybackCommand) async {
        commandError = nil
        let targetPlayer = activeSnapshot.player

        do {
            try await automation.perform(command, on: targetPlayer)
            try? await Task.sleep(for: .milliseconds(250))
            await refresh()
        } catch {
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
        selectedPlayer: MediaPlayerKind
    ) -> PlayerSnapshot? {
        let snapshots: [MediaPlayerKind: PlayerSnapshot] = [
            .spotify: spotify,
            .appleMusic: appleMusic,
        ]

        if let selected = snapshots[selectedPlayer], selected.state == .playing, selected.track != nil {
            return selected
        }

        if let playing = MediaPlayerKind.allCases
            .compactMap({ snapshots[$0] })
            .first(where: { $0.state == .playing && $0.track != nil }) {
            return playing
        }

        if let selected = snapshots[selectedPlayer], selected.track != nil {
            return selected
        }

        return MediaPlayerKind.allCases
            .compactMap({ snapshots[$0] })
            .first(where: { $0.track != nil })
    }
}
