//
//  MediaPlayer.swift
//  Reprise
//

import Foundation

enum MediaPlayerKind: String, CaseIterable, Identifiable, Sendable {
    case spotify
    case appleMusic

    nonisolated var id: String { rawValue }

    nonisolated var displayName: String {
        switch self {
        case .spotify: "Spotify"
        case .appleMusic: "Apple Music"
        }
    }

    nonisolated var bundleIdentifier: String {
        switch self {
        case .spotify: "com.spotify.client"
        case .appleMusic: "com.apple.Music"
        }
    }

    nonisolated var symbolName: String {
        switch self {
        case .spotify: "waveform.circle.fill"
        case .appleMusic: "music.note"
        }
    }
}

enum PlaybackState: String, Sendable {
    case playing
    case paused
    case stopped
    case unavailable

    var isPlaying: Bool { self == .playing }
}

struct Track: Equatable, Sendable {
    let title: String
    let album: String
    let artist: String
    let duration: TimeInterval
    let position: TimeInterval
    let artworkData: Data?

    nonisolated init(
        title: String,
        album: String,
        artist: String,
        duration: TimeInterval = 0,
        position: TimeInterval = 0,
        artworkData: Data? = nil
    ) {
        self.title = title
        self.album = album
        self.artist = artist
        self.duration = duration
        self.position = position
        self.artworkData = artworkData
    }

    nonisolated var progress: Double {
        guard duration > 0 else { return 0 }
        return min(max(position / duration, 0), 1)
    }

    nonisolated var remaining: TimeInterval {
        max(duration - position, 0)
    }
}

struct PlayerSnapshot: Equatable, Sendable {
    let player: MediaPlayerKind
    let isRunning: Bool
    let state: PlaybackState
    let track: Track?
    let volume: Int?
    let errorMessage: String?

    nonisolated init(
        player: MediaPlayerKind,
        isRunning: Bool,
        state: PlaybackState,
        track: Track?,
        volume: Int? = nil,
        errorMessage: String?
    ) {
        self.player = player
        self.isRunning = isRunning
        self.state = state
        self.track = track
        self.volume = volume
        self.errorMessage = errorMessage
    }

    nonisolated static func notRunning(_ player: MediaPlayerKind) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: false,
            state: .unavailable,
            track: nil,
            errorMessage: nil
        )
    }

    var menuBarTitle: String? {
        track?.title
    }
}

enum PlayerVolume {
    nonisolated static let range = 0...100
    nonisolated static let defaultAudibleLevel = 50

    nonisolated static func clamped(_ value: Int) -> Int {
        min(max(value, range.lowerBound), range.upperBound)
    }

    nonisolated static func muteToggleTarget(
        current: Int,
        lastAudible: Int?
    ) -> Int {
        let current = clamped(current)
        guard current == 0 else {
            return 0
        }

        let restored = lastAudible.map(clamped) ?? defaultAudibleLevel
        return restored > 0 ? restored : defaultAudibleLevel
    }
}

enum PlaybackCommand: Sendable {
    case previous
    case playPause
    case stop
    case next
}
