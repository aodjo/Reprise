//
//  MediaAutomationService.swift
//  Reprise
//

import AppKit
import Foundation

actor MediaAutomationService {
    private struct ArtworkCacheEntry {
        let trackKey: String
        let data: Data?
    }

    private struct SnapshotCacheEntry {
        let snapshot: PlayerSnapshot
        let observedAt: Date
    }

    private static let transientFailureGraceInterval: TimeInterval = 2

    private var artworkCache: [MediaPlayerKind: ArtworkCacheEntry] = [:]
    private var snapshotCache: [MediaPlayerKind: SnapshotCacheEntry] = [:]
    private var snapshotScriptCache: [MediaPlayerKind: NSAppleScript] = [:]
    private let youtubeMusicBridge: YouTubeMusicBridge

    init(
        youtubeMusicBridge: YouTubeMusicBridge = .shared
    ) {
        self.youtubeMusicBridge = youtubeMusicBridge
    }

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

        // Spotify and Music can briefly report the old position immediately
        // after accepting a seek. Do not expose that stale value to the UI.
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

    private func isRunning(_ player: MediaPlayerKind) -> Bool {
        guard let bundleIdentifier = player.automationBundleIdentifier else {
            return false
        }
        return !NSRunningApplication.runningApplications(
            withBundleIdentifier: bundleIdentifier
        ).isEmpty
    }

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
                // Spotify does not expose a native stop command. Rewinding and
                // pausing gives the user the same observable stop behavior.
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

    private func execute(_ source: String) throws -> NSAppleEventDescriptor {
        guard let script = NSAppleScript(source: source) else {
            throw AutomationError.invalidScript
        }

        return try execute(script)
    }

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

enum AutomationError: LocalizedError {
    case playerNotRunning(MediaPlayerKind)
    case invalidScript
    case appleScript(number: Int, message: String)
    case seekNotConfirmed(
        requested: TimeInterval,
        observed: TimeInterval?
    )
}
