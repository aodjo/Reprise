// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Foundation

/// Reads and controls the local media players.
///
/// Spotify and Music are driven with AppleScript; YouTube Music has no app to
/// script, so it is delegated to the browser extension bridge. Presenting both
/// behind one type keeps that split out of the store and the UI.
///
/// An actor because it owns caches that a once-a-second poll and the user's
/// button presses both reach, and because AppleScript execution is
/// synchronous and blocking - serialising it here keeps it off the main
/// thread without a lock at every call site.
actor MediaAutomationService {
    /// Artwork held for one track, so a poll does not re-fetch a cover that
    /// has not changed.
    private struct ArtworkCacheEntry {
        /// Identity of the track the artwork belongs to.
        let trackKey: String

        /// The cover data, or `nil` if the track genuinely has none. Caching
        /// the absence matters as much as the data: it stops every poll
        /// retrying a download for a track that will never have a cover.
        let data: Data?
    }

    /// The last good snapshot for a player, with when it was taken.
    private struct SnapshotCacheEntry {
        /// The snapshot itself.
        let snapshot: PlayerSnapshot

        /// When it was captured, for judging whether it is still usable.
        let observedAt: Date
    }

    /// How long a stale snapshot may stand in for a failed read.
    ///
    /// AppleScript intermittently fails for a poll or two while a player
    /// changes tracks or the system is busy. Bridging that gap keeps the panel
    /// from flashing an error over a player that is working normally; beyond
    /// it, the failure is real enough to show.
    private static let transientFailureGraceInterval: TimeInterval = 2

    private var artworkCache: [MediaPlayerKind: ArtworkCacheEntry] = [:]
    private var snapshotCache: [MediaPlayerKind: SnapshotCacheEntry] = [:]
    private var snapshotScriptCache: [MediaPlayerKind: NSAppleScript] = [:]
    private let youtubeMusicBridge: YouTubeMusicBridge

    /// Creates the service.
    ///
    /// - Parameter youtubeMusicBridge: Bridge to the browser extension.
    ///   Defaults to the shared instance; injectable for tests.
    init(
        youtubeMusicBridge: YouTubeMusicBridge = .shared
    ) {
        self.youtubeMusicBridge = youtubeMusicBridge
    }

    /// Polls every supported player once.
    ///
    /// Players are read sequentially rather than concurrently because each
    /// AppleScript call blocks, and running them in parallel would multiply
    /// the automation load without shortening the slowest one.
    ///
    /// - Parameter automaticallyPausesOtherYouTubeMusicSessions: Whether the
    ///   extension should pause its other tabs when one starts playing. Passed
    ///   through on every poll so a settings change takes effect without
    ///   needing its own notification path.
    /// - Returns: One snapshot per player. Players that are not running are
    ///   present as unavailable rather than absent.
    func snapshots(
        automaticallyPausesOtherYouTubeMusicSessions: Bool
    ) async -> [MediaPlayerKind: PlayerSnapshot] {
        await youtubeMusicBridge.start()
        await youtubeMusicBridge.setAutomaticallyPausesOtherSessions(
            automaticallyPausesOtherYouTubeMusicSessions
        )
        var result: [MediaPlayerKind: PlayerSnapshot] = [:]

        for player in MediaPlayerKind.allCases {
            result[player] = await snapshot(for: player)
        }

        return result
    }

    /// Sends a transport command to one player.
    ///
    /// - Parameters:
    ///   - command: Transport control to invoke.
    ///   - player: Player to command.
    /// - Throws: ``AutomationError/playerNotRunning(_:)`` when the app is not
    ///   open, ``AutomationError/appleScript(number:message:)`` when the
    ///   script fails - automation permission being the usual cause - or a
    ///   `YouTubeMusicBridgeProtocolError` from the extension.
    func perform(
        _ command: PlaybackCommand,
        on player: MediaPlayerKind
    ) async throws {
        if player == .youtubeMusic {
            try await youtubeMusicBridge.perform(command)
            return
        }

        guard isRunning(player) else {
            throw AutomationError.playerNotRunning(player)
        }

        guard let bundleIdentifier = player.automationBundleIdentifier else {
            throw AutomationError.playerNotRunning(player)
        }
        let source = """
        tell application id "\(bundleIdentifier)"
            \(appleScriptCommand(command, for: player))
        end tell
        """
        _ = try execute(source)
    }

    /// Sets a player's volume and reports back what it actually took.
    ///
    /// The value is read back rather than assumed, because players round and
    /// clamp differently; returning the real level keeps the slider from
    /// drifting away from the player it controls.
    ///
    /// - Parameters:
    ///   - volume: Desired level from 0 to 100; clamped before use.
    ///   - player: Player to adjust.
    /// - Returns: The level the player settled on, falling back to the
    ///   requested value if it reported something unparsable.
    /// - Throws: ``AutomationError/playerNotRunning(_:)`` or
    ///   ``AutomationError/appleScript(number:message:)``.
    func setVolume(
        _ volume: Int,
        on player: MediaPlayerKind
    ) async throws -> Int {
        if player == .youtubeMusic {
            return try await youtubeMusicBridge.setVolume(volume)
        }

        guard isRunning(player) else {
            throw AutomationError.playerNotRunning(player)
        }

        let volume = PlayerVolume.clamped(volume)
        guard let bundleIdentifier = player.automationBundleIdentifier else {
            throw AutomationError.playerNotRunning(player)
        }
        let source = """
        tell application id "\(bundleIdentifier)"
            set sound volume to \(volume)
            return sound volume as text
        end tell
        """
        let descriptor = try execute(source)
        return Int(descriptor.stringValue ?? "")
            .map(PlayerVolume.clamped)
            ?? volume
    }

    /// Seeks a player and waits for it to confirm the new position.
    ///
    /// Spotify and Music both accept a seek and then keep reporting the old
    /// position for a short while. Returning immediately would hand the UI a
    /// stale value and make the progress bar snap backwards, so the position
    /// is polled until it agrees with the target within
    /// ``PlaybackPosition/seekConfirmationTolerance``. Twelve attempts at
    /// 100ms covers roughly a second, well past what a healthy player needs.
    ///
    /// - Parameters:
    ///   - position: Target position in seconds; non-finite values become 0.
    ///   - player: Player to seek.
    /// - Returns: The confirmed position, as the player reports it.
    /// - Throws: ``AutomationError/seekNotConfirmed(requested:observed:)`` if
    ///   the player never agrees, plus the errors the other commands can
    ///   raise.
    func setPosition(
        _ position: TimeInterval,
        on player: MediaPlayerKind
    ) async throws -> TimeInterval {
        if player == .youtubeMusic {
            return try await youtubeMusicBridge.setPosition(position)
        }

        guard isRunning(player) else {
            throw AutomationError.playerNotRunning(player)
        }

        let position = position.isFinite ? max(position, 0) : 0
        guard let bundleIdentifier = player.automationBundleIdentifier else {
            throw AutomationError.playerNotRunning(player)
        }
        let setPositionSource = """
        tell application id "\(bundleIdentifier)"
            set player position to \(position)
        end tell
        """
        _ = try execute(setPositionSource)

        let readPositionSource = """
        tell application id "\(bundleIdentifier)"
            return player position as text
        end tell
        """
        var lastObservedPosition: TimeInterval?

        for attempt in 0..<12 {
            if attempt > 0 {
                try await Task.sleep(for: .milliseconds(100))
            }

            let descriptor = try execute(readPositionSource)
            guard let actualPosition = Double(
                descriptor.stringValue ?? ""
            ) else {
                continue
            }
            lastObservedPosition = actualPosition

            if PlaybackPosition.confirmsSeek(
                actual: actualPosition,
                target: position
            ) {
                return actualPosition
            }
        }

        throw AutomationError.seekNotConfirmed(
            requested: position,
            observed: lastObservedPosition
        )
    }

    /// Reads one player's current state.
    ///
    /// A player that is not running clears its caches on the way out, so
    /// relaunching it cannot serve artwork or a snapshot from its previous
    /// session.
    ///
    /// Errors never propagate: a poll failure within the grace interval
    /// replays the last good snapshot, and anything longer becomes an
    /// unavailable snapshot carrying a readable message. The panel therefore
    /// always has something to render, and one failing player never blocks
    /// the others in the same sweep.
    ///
    /// - Parameter player: Player to read.
    /// - Returns: The player's state, never a thrown error.
    private func snapshot(for player: MediaPlayerKind) async -> PlayerSnapshot {
        if player == .youtubeMusic {
            return await youtubeMusicBridge.snapshot()
        }

        guard isRunning(player) else {
            artworkCache[player] = nil
            snapshotCache[player] = nil
            return .notRunning(player)
        }

        do {
            let descriptor = try executeSnapshotScript(for: player)
            let values = (1...8).map { descriptor.atIndex($0)?.stringValue ?? "" }
            let state = PlaybackState(rawValue: values[0]) ?? .stopped
            let volume = Int(values[7]).map(PlayerVolume.clamped)
            let track: Track?

            if values[1].isEmpty {
                track = nil
            } else {
                let rawDuration = Double(values[4]) ?? 0
                let duration = player == .spotify ? rawDuration / 1_000 : rawDuration
                let position = Double(values[5]) ?? 0
                let trackKey = [values[1], values[2], values[3]].joined(separator: "\u{0}")
                let artwork = await artwork(
                    for: player,
                    trackKey: trackKey,
                    remoteURL: values[6]
                )

                track = Track(
                    title: values[1],
                    album: values[2],
                    artist: values[3],
                    duration: duration,
                    position: position,
                    artworkData: artwork
                )
            }

            let snapshot = PlayerSnapshot(
                player: player,
                isRunning: true,
                state: state,
                track: track,
                volume: volume,
                errorMessage: nil
            )
            snapshotCache[player] = SnapshotCacheEntry(
                snapshot: snapshot,
                observedAt: Date()
            )
            return snapshot
        } catch {
            if let cached = snapshotCache[player],
               Date().timeIntervalSince(cached.observedAt)
                <= Self.transientFailureGraceInterval {
                return cached.snapshot
            }
            return PlayerSnapshot(
                player: player,
                isRunning: true,
                state: .unavailable,
                track: nil,
                errorMessage: Self.userFacingMessage(for: error)
            )
        }
    }

    /// Whether a player's app is currently open.
    ///
    /// Checked before every script so Reprise never sends AppleScript to a
    /// closed app, which macOS would answer by launching it - turning a
    /// routine poll into an unwanted app launch.
    ///
    /// - Parameter player: Player to check.
    /// - Returns: `true` when the app is running. Always `false` for players
    ///   with no bundle identifier, such as YouTube Music.
    private func isRunning(_ player: MediaPlayerKind) -> Bool {
        guard let bundleIdentifier = player.automationBundleIdentifier else {
            return false
        }
        return !NSRunningApplication.runningApplications(
            withBundleIdentifier: bundleIdentifier
        ).isEmpty
    }

    /// Builds the AppleScript that reads a player's whole state at once.
    ///
    /// One script returning a fixed eight-element list, rather than a call per
    /// field: it is a single automation round trip, and every value describes
    /// the same instant. A stopped player returns the same shape with empty
    /// track fields, so the caller's index-based parsing never has to branch.
    ///
    /// Spotify's artwork URL read is wrapped in `try` because it fails on
    /// local files and podcasts, where the rest of the track is still valid.
    ///
    /// - Parameter player: Player to build the script for.
    /// - Returns: AppleScript source, or an empty string for players that are
    ///   not scripted.
    private func snapshotScript(for player: MediaPlayerKind) -> String {
        let trackStatements = switch player {
        case .spotify:
            """
            set currentSong to current track
            set songName to name of currentSong as text
            set albumName to album of currentSong as text
            set artistName to artist of currentSong as text
            set durationValue to duration of currentSong as text
            set artworkLocation to ""
            try
                set artworkLocation to artwork url of currentSong as text
            end try
            """
        case .appleMusic:
            """
            set songName to name of current track as text
            set albumName to album of current track as text
            set artistName to artist of current track as text
            set durationValue to duration of current track as text
            set artworkLocation to ""
            """
        case .youtubeMusic:
            ""
        }

        guard let bundleIdentifier = player.automationBundleIdentifier else {
            return ""
        }
        return """
        tell application id "\(bundleIdentifier)"
            set currentState to player state
            set volumeValue to sound volume as text
            if currentState is playing then
                set stateName to "playing"
            else if currentState is paused then
                set stateName to "paused"
            else
                set stateName to "stopped"
            end if

            if stateName is "stopped" then
                return {stateName, "", "", "", "", "", "", volumeValue}
            end if

            \(trackStatements)
            set positionValue to player position as text
            return {stateName, songName, albumName, artistName, durationValue, positionValue, artworkLocation, volumeValue}
        end tell
        """
    }

    /// Runs the snapshot script, compiling it only once per player.
    ///
    /// Compiling AppleScript is the expensive part, and the source never
    /// varies for a given player, so the compiled script is cached across the
    /// once-a-second poll.
    ///
    /// - Parameter player: Player to read.
    /// - Returns: The descriptor holding the eight-element result list.
    /// - Throws: ``AutomationError/invalidScript`` if the source will not
    ///   compile, or ``AutomationError/appleScript(number:message:)``.
    private func executeSnapshotScript(
        for player: MediaPlayerKind
    ) throws -> NSAppleEventDescriptor {
        let script: NSAppleScript

        if let cached = snapshotScriptCache[player] {
            script = cached
        } else {
            guard let compiled = NSAppleScript(source: snapshotScript(for: player)) else {
                throw AutomationError.invalidScript
            }
            snapshotScriptCache[player] = compiled
            script = compiled
        }

        return try execute(script)
    }

    /// Fetches cover art for a track, reusing the cached copy when possible.
    ///
    /// The cache is keyed on the track's own identity rather than the player,
    /// so the expensive part - a network download for Spotify, a second
    /// AppleScript round trip for Music - happens once per track instead of
    /// once per poll.
    ///
    /// - Parameters:
    ///   - player: Player the track belongs to.
    ///   - trackKey: Identity of the track, built from its text metadata.
    ///   - remoteURL: Artwork URL, used by Spotify only.
    /// - Returns: Encoded artwork, or `nil` when there is none to be had.
    private func artwork(
        for player: MediaPlayerKind,
        trackKey: String,
        remoteURL: String
    ) async -> Data? {
        if let cached = artworkCache[player], cached.trackKey == trackKey {
            return cached.data
        }

        let data: Data?

        switch player {
        case .spotify:
            data = await downloadArtwork(from: remoteURL)
        case .appleMusic:
            data = appleMusicArtwork()
        case .youtubeMusic:
            data = nil
        }

        artworkCache[player] = ArtworkCacheEntry(trackKey: trackKey, data: data)
        return data
    }

    /// Downloads cover art from a URL.
    ///
    /// The response is validated before it is trusted: status code, an 8MB
    /// ceiling, and a decode check. Artwork is the one thing Reprise fetches
    /// from a URL a third-party app supplies, so it is treated as untrusted
    /// input - the size limit bounds memory, and the decode check keeps
    /// something that is not an image from reaching the UI layer.
    ///
    /// The five-second timeout matters because this sits inside the poll: a
    /// slow host would otherwise stall every player's refresh.
    ///
    /// - Parameter urlString: Absolute URL of the artwork.
    /// - Returns: The image data, or `nil` on any failure. Never throws, since
    ///   a missing cover is not worth failing a snapshot over.
    private func downloadArtwork(from urlString: String) async -> Data? {
        guard let url = URL(string: urlString), !urlString.isEmpty else { return nil }

        var request = URLRequest(url: url)
        request.timeoutInterval = 5

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard
                let response = response as? HTTPURLResponse,
                (200..<300).contains(response.statusCode),
                data.count <= 8_000_000,
                NSImage(data: data) != nil
            else {
                return nil
            }
            return data
        } catch {
            return nil
        }
    }

    /// Reads the current track's cover art out of the Music app.
    ///
    /// Music holds artwork as embedded data rather than a URL, so it comes
    /// back over AppleScript instead of over the network. The count is checked
    /// first because asking for `artwork 1` of a track with none raises a
    /// script error rather than returning empty.
    ///
    /// - Returns: The raw image data, or `nil` when the track has no artwork
    ///   or the script fails.
    private func appleMusicArtwork() -> Data? {
        let source = """
        tell application id "com.apple.Music"
            if (count of artworks of current track) is greater than 0 then
                return raw data of artwork 1 of current track
            end if
        end tell
        """

        guard let descriptor = try? execute(source) else { return nil }
        let data = descriptor.data
        return data.isEmpty ? nil : data
    }

    /// Maps a transport command onto the player's AppleScript vocabulary.
    ///
    /// Mostly a one-to-one translation, except that Spotify exposes no stop
    /// command: pausing and rewinding to zero produces the same observable
    /// result the user expects from stop.
    ///
    /// - Parameters:
    ///   - command: Command to translate.
    ///   - player: Player whose vocabulary to use.
    /// - Returns: AppleScript statements, or an empty string for players not
    ///   driven by script.
    private func appleScriptCommand(
        _ command: PlaybackCommand,
        for player: MediaPlayerKind
    ) -> String {
        switch command {
        case .previous:
            return "previous track"
        case .pause:
            return "pause"
        case .playPause:
            return "playpause"
        case .stop:
            switch player {
            case .spotify:
                return """
                pause
                set player position to 0
                """
            case .appleMusic:
                return "stop"
            case .youtubeMusic:
                return ""
            }
        case .next:
            return "next track"
        }
    }

    /// Compiles and runs AppleScript source.
    ///
    /// - Parameter source: Script source.
    /// - Returns: The script's result descriptor.
    /// - Throws: ``AutomationError/invalidScript`` if it will not compile, or
    ///   ``AutomationError/appleScript(number:message:)`` if it fails to run.
    private func execute(_ source: String) throws -> NSAppleEventDescriptor {
        guard let script = NSAppleScript(source: source) else {
            throw AutomationError.invalidScript
        }

        return try execute(script)
    }

    /// Runs an already compiled script.
    ///
    /// `NSAppleScript` signals failure through an out-parameter dictionary
    /// rather than by returning nil, so the presence of that dictionary - not
    /// the result - is what decides whether the call succeeded.
    ///
    /// - Parameter script: Compiled script to run.
    /// - Returns: The script's result descriptor.
    /// - Throws: ``AutomationError/appleScript(number:message:)`` carrying the
    ///   OSA error number, which the caller needs to recognise a permission
    ///   denial.
    private func execute(_ script: NSAppleScript) throws -> NSAppleEventDescriptor {
        var details: NSDictionary?
        let result = script.executeAndReturnError(&details)

        if let details {
            let number = details[NSAppleScript.errorNumber] as? Int ?? 0
            let message = details[NSAppleScript.errorMessage] as? String ?? "알 수 없는 오류"
            throw AutomationError.appleScript(number: number, message: message)
        }

        return result
    }

    /// Turns any automation failure into a message worth showing a user.
    ///
    /// The case that matters is OSA error -1743, macOS refusing automation
    /// access. It is the one failure the user can actually fix, and the raw
    /// message does not say how, so it is matched specifically and answered
    /// with the path through System Settings.
    ///
    /// - Parameter error: Error from any of the automation paths.
    /// - Returns: A Korean message for the panel's error line.
    nonisolated static func userFacingMessage(for error: Error) -> String {
        if let bridgeError = error as? YouTubeMusicBridgeProtocolError {
            return bridgeError.localizedDescription
        }

        guard let automationError = error as? AutomationError else {
            return "플레이어 정보를 가져오지 못했습니다."
        }

        switch automationError {
        case .appleScript(let number, _) where number == -1743:
            return "자동화 권한이 필요합니다. 시스템 설정 › 개인정보 보호 및 보안 › 자동화에서 Reprise를 허용해 주세요."
        case .playerNotRunning(let player):
            return "\(player.displayName)이(가) 실행 중이 아닙니다."
        case .appleScript(_, let message):
            return "플레이어와 통신하지 못했습니다: \(message)"
        case .invalidScript:
            return "플레이어 제어 스크립트를 준비하지 못했습니다."
        case .seekNotConfirmed:
            return "플레이어가 변경한 재생 위치를 확인하지 못했습니다."
        }
    }
}

/// Failures from the AppleScript control path.
enum AutomationError: LocalizedError {
    /// The player's app is not open.
    case playerNotRunning(MediaPlayerKind)

    /// The script source could not be compiled.
    case invalidScript

    /// The script ran and failed.
    ///
    /// The number is the OSA error code; -1743 means automation permission was
    /// denied, which is the only value any caller inspects.
    case appleScript(number: Int, message: String)

    /// A seek was sent but the player never reported the new position.
    ///
    /// Carries the observed position, if one was ever read, to distinguish a
    /// player that ignored the seek from one that never answered at all.
    case seekNotConfirmed(
        requested: TimeInterval,
        observed: TimeInterval?
    )
}
