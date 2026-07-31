//
//  YouTubeMusicBridgeProtocol.swift
//  Reprise
//

import Foundation

nonisolated enum YouTubeMusicBridgeProtocol {
    static let version = 1
    static let port: UInt16 = 19_436
    static let subprotocolName = "reprise-youtube-music-v1"
    static let extensionID = "apmolpbmjjndmedbogieopgmapoehdlp"
    static let extensionPublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEArUGImdEvl2yZFlOUsVt/7lfhIuhpIPjLVbp4ihHMu5rHbIxFPGsA3q3W5IcPbcI/G6ujsh7C5LRC+1c9X4ftXBEcGEKryKPZ3WlfsmwuuXxEd3N6x6OzCw0ABEplzwuDh6ZMCFfDY8sG30Au1UoJTK3ZOunRCp9K/UFl+a76ozZRmKl284R8cWHjduDeEO5cMmjO+LTsXwT+df+rY14cWA88+tEvzdhiZpctHIwE7AIXUmr7jrcQlMbz+m4jalHCi2pd/np3BRbSAT5lQN6I4l2LVegdX5cpwQjueBMOROIgerOlIy0avCrmrWty0hF0J5ZOLDa/TvIhp9sAew0d9wIDAQAB"
    static let extensionOrigin = "chrome-extension://\(extensionID)"
    static let maximumMessageSize = 64 * 1_024
    static let staleSnapshotInterval: TimeInterval = 5

    static func acceptsHandshake(
        subprotocols: [String],
        headers: [(name: String, value: String)]
    ) -> Bool {
        guard subprotocols.contains(subprotocolName) else {
            return false
        }

        let origin = headers.first {
            $0.name.caseInsensitiveCompare("Origin") == .orderedSame
        }?.value.trimmingCharacters(in: CharacterSet(charactersIn: "/"))

        return origin == extensionOrigin
    }
}

nonisolated enum YouTubeMusicBridgeStatus: Equatable, Sendable {
    case stopped
    case waiting
    case connected
    case failed(String)

    var isConnected: Bool {
        self == .connected
    }

    var displayText: String {
        switch self {
        case .stopped:
            "중지됨"
        case .waiting:
            "확장 프로그램 연결 대기 중"
        case .connected:
            "연결됨"
        case .failed:
            "연결 오류"
        }
    }
}

nonisolated enum YouTubeMusicInboundMessage: Sendable {
    case hello(YouTubeMusicHelloMessage)
    case snapshot(YouTubeMusicSnapshotMessage)
    case heartbeat(YouTubeMusicHeartbeatMessage)
    case acknowledgement(YouTubeMusicAcknowledgementMessage)

    static func decode(from data: Data) throws -> Self {
        guard data.count <= YouTubeMusicBridgeProtocol.maximumMessageSize else {
            throw YouTubeMusicBridgeProtocolError.messageTooLarge
        }

        let decoder = JSONDecoder()
        let header = try decoder.decode(MessageHeader.self, from: data)

        switch header.type {
        case "hello":
            return .hello(try decoder.decode(YouTubeMusicHelloMessage.self, from: data))
        case "snapshot":
            return .snapshot(try decoder.decode(YouTubeMusicSnapshotMessage.self, from: data))
        case "heartbeat":
            return .heartbeat(try decoder.decode(YouTubeMusicHeartbeatMessage.self, from: data))
        case "ack":
            return .acknowledgement(
                try decoder.decode(YouTubeMusicAcknowledgementMessage.self, from: data)
            )
        default:
            throw YouTubeMusicBridgeProtocolError.unknownMessageType(header.type)
        }
    }
}

nonisolated private struct MessageHeader: Decodable {
    let type: String
}

nonisolated struct YouTubeMusicHelloMessage: Decodable, Sendable {
    let type: String
    let protocolVersion: Int
    let extensionVersion: String

    func validate() throws {
        guard type == "hello" else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)
        guard !extensionVersion.isEmpty, extensionVersion.count <= 64 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
    }
}

nonisolated struct YouTubeMusicHeartbeatMessage: Decodable, Sendable {
    let type: String
    let protocolVersion: Int

    func validate() throws {
        guard type == "heartbeat" else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)
    }
}

nonisolated struct YouTubeMusicAcknowledgementMessage: Decodable, Sendable {
    let type: String
    let protocolVersion: Int
    let id: String
    let success: Bool
    let error: String?

    func validate() throws {
        guard type == "ack",
              !id.isEmpty,
              id.count <= 64,
              (error?.count ?? 0) <= 512 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)
    }
}

nonisolated struct YouTubeMusicSnapshotMessage: Decodable, Sendable {
    let type: String
    let protocolVersion: Int
    let sequence: Int
    let tabID: Int?
    let state: String
    let title: String
    let album: String?
    let artist: String?
    let duration: TimeInterval
    let position: TimeInterval
    let volume: Double
    let artworkURL: String?
    let videoID: String?
    let trackURL: String?

    enum CodingKeys: String, CodingKey {
        case type
        case protocolVersion
        case sequence
        case tabID = "tabId"
        case state
        case title
        case album
        case artist
        case duration
        case position
        case volume
        case artworkURL = "artworkUrl"
        case videoID = "videoId"
        case trackURL = "trackUrl"
    }

    func validated() throws -> YouTubeMusicSnapshotPayload {
        guard type == "snapshot", sequence >= 0 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)

        let state = PlaybackState(rawValue: state)
        guard let state,
              state != .unavailable,
              duration.isFinite,
              position.isFinite,
              volume.isFinite else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }

        let title = sanitized(title, maximumLength: 512)
        let album = sanitized(album ?? "", maximumLength: 512)
        let artist = sanitized(artist ?? "", maximumLength: 512)
        let artworkURL = try validatedHTTPSURL(
            artworkURL,
            maximumLength: 2_048
        )
        let trackURL = try validatedHTTPSURL(
            trackURL,
            maximumLength: 2_048,
            requiredHost: "music.youtube.com"
        )

        return YouTubeMusicSnapshotPayload(
            sequence: sequence,
            tabID: tabID,
            state: title.isEmpty ? .stopped : state,
            title: title,
            album: album,
            artist: artist,
            duration: max(duration, 0),
            position: max(position, 0),
            volume: PlayerVolume.clamped(Int(volume.rounded())),
            artworkURL: artworkURL,
            videoID: sanitized(videoID ?? "", maximumLength: 128),
            trackURL: trackURL
        )
    }
}

nonisolated struct YouTubeMusicSnapshotPayload: Equatable, Sendable {
    let sequence: Int
    let tabID: Int?
    let state: PlaybackState
    let title: String
    let album: String
    let artist: String
    let duration: TimeInterval
    let position: TimeInterval
    let volume: Int
    let artworkURL: URL?
    let videoID: String
    let trackURL: URL?

    var trackKey: String {
        if !videoID.isEmpty {
            return videoID
        }
        return [title, album, artist].joined(separator: "\u{0}")
    }
}

nonisolated struct YouTubeMusicCommandMessage: Encodable, Equatable, Sendable {
    let type = "command"
    let protocolVersion = YouTubeMusicBridgeProtocol.version
    let id: String
    let command: String
    let position: TimeInterval?
    let volume: Int?

    static func playback(
        _ command: PlaybackCommand,
        id: UUID = UUID()
    ) -> Self {
        let commandName = switch command {
        case .previous: "previous"
        case .pause: "pause"
        case .playPause: "playPause"
        case .stop: "stop"
        case .next: "next"
        }
        return Self(
            id: id.uuidString,
            command: commandName,
            position: nil,
            volume: nil
        )
    }

    static func seek(
        to position: TimeInterval,
        id: UUID = UUID()
    ) -> Self {
        Self(
            id: id.uuidString,
            command: "seek",
            position: max(position, 0),
            volume: nil
        )
    }

    static func setVolume(
        _ volume: Int,
        id: UUID = UUID()
    ) -> Self {
        Self(
            id: id.uuidString,
            command: "setVolume",
            position: nil,
            volume: PlayerVolume.clamped(volume)
        )
    }

    func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(self)
    }
}

nonisolated enum YouTubeMusicBridgeProtocolError: LocalizedError, Equatable {
    case unsupportedProtocolVersion(Int)
    case unknownMessageType(String)
    case messageTooLarge
    case invalidMessage
    case extensionNotConnected
    case commandNotConfirmed
    case commandRejected(String?)

    var errorDescription: String? {
        switch self {
        case .unsupportedProtocolVersion:
            "확장 프로그램의 프로토콜 버전이 Reprise와 맞지 않습니다."
        case .unknownMessageType, .invalidMessage, .messageTooLarge:
            "확장 프로그램이 올바르지 않은 메시지를 보냈습니다."
        case .extensionNotConnected:
            "YouTube Music 확장 프로그램이 연결되어 있지 않습니다."
        case .commandNotConfirmed:
            "YouTube Music이 변경 사항을 확인하지 못했습니다."
        case let .commandRejected(message):
            if let message, !message.isEmpty {
                "YouTube Music 명령을 실행하지 못했습니다: \(message)"
            } else {
                "YouTube Music 명령을 실행하지 못했습니다."
            }
        }
    }
}

nonisolated private func validateProtocolVersion(_ version: Int) throws {
    guard version == YouTubeMusicBridgeProtocol.version else {
        throw YouTubeMusicBridgeProtocolError.unsupportedProtocolVersion(
            version
        )
    }
}

nonisolated private func sanitized(
    _ value: String,
    maximumLength: Int
) -> String {
    let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
    return String(trimmed.prefix(maximumLength))
}

nonisolated private func validatedHTTPSURL(
    _ value: String?,
    maximumLength: Int,
    requiredHost: String? = nil
) throws -> URL? {
    guard let value, !value.isEmpty else {
        return nil
    }
    guard value.count <= maximumLength,
          let url = URL(string: value),
          url.scheme?.lowercased() == "https" else {
        throw YouTubeMusicBridgeProtocolError.invalidMessage
    }
    if let requiredHost,
       url.host?.lowercased() != requiredHost {
        throw YouTubeMusicBridgeProtocolError.invalidMessage
    }
    return url
}
