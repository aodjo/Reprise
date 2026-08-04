// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation

enum MediaPlayerKind: String, CaseIterable, Identifiable, Sendable {
    case spotify
    case appleMusic
    case youtubeMusic

    nonisolated var id: String { rawValue }

    nonisolated var displayName: String {
        switch self {
        case .spotify: "Spotify"
        case .appleMusic: "Apple Music"
        case .youtubeMusic: "YouTube Music"
        }
    }

    nonisolated var automationBundleIdentifier: String? {
        switch self {
        case .spotify: "com.spotify.client"
        case .appleMusic: "com.apple.Music"
        case .youtubeMusic: nil
        }
    }

    nonisolated var symbolName: String {
        switch self {
        case .spotify: "waveform.circle.fill"
        case .appleMusic: "music.note"
        case .youtubeMusic: "play.rectangle.fill"
        }
    }
}

enum PlaybackState: String, Sendable {
    case playing
    case paused
    case stopped
    case unavailable

    nonisolated var isPlaying: Bool { self == .playing }
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

enum PlaybackPosition {
    nonisolated static let seekConfirmationTolerance: TimeInterval = 1.5

    nonisolated static func clamped(
        _ position: TimeInterval,
        duration: TimeInterval
    ) -> TimeInterval {
        guard position.isFinite,
              duration.isFinite,
              duration > 0 else {
            return 0
        }
        return min(max(position, 0), duration)
    }

    nonisolated static func confirmsSeek(
        actual: TimeInterval,
        target: TimeInterval
    ) -> Bool {
        guard actual.isFinite, target.isFinite else {
            return false
        }

        return abs(actual - target) <= seekConfirmationTolerance
    }

    nonisolated static func estimated(
        observedPosition: TimeInterval,
        state: PlaybackState,
        observedAt: Date,
        at date: Date,
        duration: TimeInterval,
        playbackRate: Double = 1
    ) -> TimeInterval {
        let elapsed = state.isPlaying
            ? max(date.timeIntervalSince(observedAt), 0)
                * max(playbackRate, 0)
            : 0
        return clamped(
            observedPosition + elapsed,
            duration: duration
        )
    }
}

struct PlayerSnapshot: Equatable, Sendable {
    let player: MediaPlayerKind
    let isRunning: Bool
    let state: PlaybackState
    let track: Track?
    let volume: Int?
    let playbackRate: Double
    let errorMessage: String?

    nonisolated init(
        player: MediaPlayerKind,
        isRunning: Bool,
        state: PlaybackState,
        track: Track?,
        volume: Int? = nil,
        playbackRate: Double = 1,
        errorMessage: String?
    ) {
        self.player = player
        self.isRunning = isRunning
        self.state = state
        self.track = track
        self.volume = volume
        self.playbackRate = playbackRate.isFinite
            ? max(playbackRate, 0)
            : 1
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
    case pause
    case playPause
    case stop
    case next
}
