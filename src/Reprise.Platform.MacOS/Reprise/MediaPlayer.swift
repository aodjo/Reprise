// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation

/// One of the music sources Reprise can follow and control.
///
/// The raw values are persisted in preferences, so renaming a case would
/// silently reset a user's player priority order. Every case is `nonisolated`
/// because the model is read from the automation actors as well as the UI.
enum MediaPlayerKind: String, CaseIterable, Identifiable, Sendable {
    /// The Spotify desktop app, driven through AppleScript.
    case spotify

    /// The Music app bundled with macOS, driven through AppleScript.
    case appleMusic

    /// YouTube Music in a browser, reached through the Reprise extension.
    case youtubeMusic

    /// Stable identity for SwiftUI lists, backed by the persisted raw value.
    nonisolated var id: String { rawValue }

    /// The player's name as it should appear in the UI.
    nonisolated var displayName: String {
        switch self {
        case .spotify: "Spotify"
        case .appleMusic: "Apple Music"
        case .youtubeMusic: "YouTube Music"
        }
    }

    /// Bundle identifier to target when sending AppleScript commands.
    ///
    /// `nil` for YouTube Music, which has no app of its own: it is reached
    /// over the extension bridge instead. That `nil` is what distinguishes
    /// the two control paths throughout the app.
    nonisolated var automationBundleIdentifier: String? {
        switch self {
        case .spotify: "com.spotify.client"
        case .appleMusic: "com.apple.Music"
        case .youtubeMusic: nil
        }
    }

    /// SF Symbol used wherever the player needs an icon.
    nonisolated var symbolName: String {
        switch self {
        case .spotify: "waveform.circle.fill"
        case .appleMusic: "music.note"
        case .youtubeMusic: "play.rectangle.fill"
        }
    }
}

/// Transport state of a player.
///
/// `unavailable` covers a player that is not running or cannot be reached,
/// which is distinct from `stopped`: a stopped player is present and could
/// resume, while an unavailable one has nothing to command.
enum PlaybackState: String, Sendable {
    /// A track is advancing.
    case playing

    /// A track is loaded and its position is frozen.
    case paused

    /// The player is idle with no active track.
    case stopped

    /// The player is not running, or could not be reached.
    case unavailable

    /// Whether playback is currently advancing.
    nonisolated var isPlaying: Bool { self == .playing }
}

/// The track a player currently has loaded.
///
/// Values are captured at one moment; `position` in particular goes stale
/// immediately, which is why `PlaybackPosition.estimated(...)` exists to
/// advance it between polls rather than polling at frame rate.
struct Track: Equatable, Sendable {
    /// Track title.
    let title: String

    /// Album name.
    let album: String

    /// Artist credit.
    let artist: String

    /// Total track length in seconds, or 0 when the player did not report one.
    let duration: TimeInterval

    /// Elapsed playback time in seconds, as of when this was captured.
    let position: TimeInterval

    /// Encoded cover art, or `nil` when the player supplied none.
    let artworkData: Data?

    /// Creates a track description.
    ///
    /// The timing and artwork parameters default to empty so that callers
    /// which only know the text metadata - the demo content and several
    /// tests - can build a track without inventing placeholder timings.
    ///
    /// - Parameters:
    ///   - title: Track title.
    ///   - album: Album name.
    ///   - artist: Artist credit.
    ///   - duration: Total length in seconds. Defaults to 0, meaning unknown.
    ///   - position: Elapsed time in seconds. Defaults to 0.
    ///   - artworkData: Encoded cover art. Defaults to `nil`.
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

    /// Fraction of the track already played, for the progress bar.
    ///
    /// A track with no reported duration reads as 0 rather than dividing by
    /// zero, and the result is clamped because players briefly report a
    /// position past the end while advancing to the next track.
    ///
    /// - Returns: Progress from 0 to 1 inclusive.
    nonisolated var progress: Double {
        guard duration > 0 else { return 0 }
        return min(max(position / duration, 0), 1)
    }

    /// Time left in the track, never negative.
    nonisolated var remaining: TimeInterval {
        max(duration - position, 0)
    }
}

/// Arithmetic for playback positions that outside data cannot be trusted for.
///
/// Players report positions that are negative, past the end, or not finite at
/// all, and a seek is confirmed by polling back a value that only approximates
/// what was asked for. Keeping those rules here means the UI and the
/// automation layer cannot disagree about them.
enum PlaybackPosition {
    /// How far a polled position may sit from the requested one and still
    /// count as the seek having landed.
    ///
    /// Sized for the round trip through AppleScript: a player that accepted
    /// the seek will already have advanced slightly by the time its position
    /// is read back, so an exact comparison would never confirm.
    nonisolated static let seekConfirmationTolerance: TimeInterval = 1.5

    /// Forces a position into the valid range for a track.
    ///
    /// Rejects non-finite inputs and unknown durations outright, since a NaN
    /// reaching a SwiftUI layout value would break the whole panel rather
    /// than just the progress bar.
    ///
    /// - Parameters:
    ///   - position: Position to constrain, in seconds.
    ///   - duration: Track length in seconds.
    /// - Returns: A position from 0 to `duration`, or 0 when either input is
    ///   not a usable finite number.
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

    /// Decides whether a polled position means a requested seek took effect.
    ///
    /// - Parameters:
    ///   - actual: Position read back from the player.
    ///   - target: Position that was requested.
    /// - Returns: `true` when the two agree within
    ///   ``seekConfirmationTolerance``; `false` if either value is not finite.
    nonisolated static func confirmsSeek(
        actual: TimeInterval,
        target: TimeInterval
    ) -> Bool {
        guard actual.isFinite, target.isFinite else {
            return false
        }

        return abs(actual - target) <= seekConfirmationTolerance
    }

    /// Projects a polled position forward to the current instant.
    ///
    /// Polling a player often enough for a smooth progress bar would be far
    /// more expensive than advancing the last known position by the elapsed
    /// wall-clock time, so the UI ticks on this and the poll only corrects it.
    /// Time is only added while playing; a paused track keeps its position.
    ///
    /// - Parameters:
    ///   - observedPosition: Position reported by the last poll, in seconds.
    ///   - state: Playback state at that poll.
    ///   - observedAt: When that poll happened.
    ///   - date: Instant to project to, normally now.
    ///   - duration: Track length in seconds, used to clamp the result.
    ///   - playbackRate: Speed multiplier. Defaults to 1; negative rates are
    ///     treated as 0 rather than rewinding the estimate.
    /// - Returns: The estimated current position, clamped to the track.
    ///
    /// ## Example
    /// ```swift
    /// // Playing, polled 2s ago at 0:10, so roughly 0:12 now.
    /// PlaybackPosition.estimated(
    ///     observedPosition: 10,
    ///     state: .playing,
    ///     observedAt: now.addingTimeInterval(-2),
    ///     at: now,
    ///     duration: 180
    /// ) // 12
    /// ```
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

/// Everything Reprise knows about one player after a single poll.
///
/// Carries `isRunning` and `errorMessage` alongside the playback data so the
/// UI can tell apart the three ways a player yields no track: it is not
/// running, it is running but idle, or the attempt to reach it failed.
struct PlayerSnapshot: Equatable, Sendable {
    /// Which player this describes.
    let player: MediaPlayerKind

    /// Whether the player's app or bridge is currently available.
    let isRunning: Bool

    /// Transport state at the moment of the poll.
    let state: PlaybackState

    /// The loaded track, or `nil` when there is none.
    let track: Track?

    /// Player volume from 0 to 100, or `nil` when it does not report one.
    let volume: Int?

    /// Playback speed multiplier, normalised to a finite non-negative value.
    let playbackRate: Double

    /// Why the poll failed, or `nil` when it succeeded.
    let errorMessage: String?

    /// Creates a snapshot, normalising the playback rate.
    ///
    /// The rate is sanitised at the boundary rather than at each use, because
    /// it feeds position estimation where a NaN or negative value would
    /// corrupt every later reading rather than just this one.
    ///
    /// - Parameters:
    ///   - player: Which player this describes.
    ///   - isRunning: Whether the player is available.
    ///   - state: Transport state.
    ///   - track: The loaded track, if any.
    ///   - volume: Volume from 0 to 100. Defaults to `nil`.
    ///   - playbackRate: Speed multiplier. Defaults to 1; non-finite values
    ///     fall back to 1 and negative values are raised to 0.
    ///   - errorMessage: Failure description, or `nil`.
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

    /// Builds the snapshot for a player that is not running.
    ///
    /// Used on the common path where a player simply is not open, which is
    /// not a failure and so carries no error message.
    ///
    /// - Parameter player: The player that is unavailable.
    /// - Returns: An unavailable snapshot with no track and no error.
    nonisolated static func notRunning(_ player: MediaPlayerKind) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: false,
            state: .unavailable,
            track: nil,
            errorMessage: nil
        )
    }

    /// Title to show in the menu bar, or `nil` when no track is loaded.
    var menuBarTitle: String? {
        track?.title
    }
}

/// Volume rules shared by the slider, the mute button, and the players.
enum PlayerVolume {
    /// The scale every player's volume is expressed on.
    nonisolated static let range = 0...100

    /// Level to restore to when unmuting with no remembered level.
    nonisolated static let defaultAudibleLevel = 50

    /// Forces a volume into the supported range.
    ///
    /// - Parameter value: Volume to constrain.
    /// - Returns: A value within ``range``.
    nonisolated static func clamped(_ value: Int) -> Int {
        min(max(value, range.lowerBound), range.upperBound)
    }

    /// Works out what a mute toggle should set the volume to.
    ///
    /// Muting is a jump to 0; unmuting restores the level from before the
    /// mute. A remembered level of 0 is ignored in favour of
    /// ``defaultAudibleLevel``, so unmuting always produces audible sound
    /// rather than appearing to do nothing.
    ///
    /// - Parameters:
    ///   - current: Volume the player is at now.
    ///   - lastAudible: Level from before the mute, if one was remembered.
    /// - Returns: 0 when currently audible, otherwise the level to restore.
    ///
    /// ## Example
    /// ```swift
    /// PlayerVolume.muteToggleTarget(current: 70, lastAudible: nil) // 0
    /// PlayerVolume.muteToggleTarget(current: 0, lastAudible: 70)   // 70
    /// PlayerVolume.muteToggleTarget(current: 0, lastAudible: 0)    // 50
    /// ```
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

/// Transport command Reprise can send to a player.
enum PlaybackCommand: Sendable {
    /// Skip back to the previous track.
    case previous

    /// Pause without toggling.
    case pause

    /// Toggle between playing and paused.
    case playPause

    /// Stop playback and release the track.
    case stop

    /// Skip forward to the next track.
    case next
}
