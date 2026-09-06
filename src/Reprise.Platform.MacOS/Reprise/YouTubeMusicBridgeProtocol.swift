// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation

/// Wire protocol between Reprise and the YouTube Music browser extension.
///
/// YouTube Music has no scriptable app, so control goes through an extension
/// talking to a local WebSocket server inside Reprise. That server listens on
/// the loopback interface, which any process on the machine can reach, so
/// every constant and check here exists to keep the connection narrow: a
/// fixed subprotocol, an origin allowlist, and hard caps on message size and
/// session count.
///
/// No user data ever leaves the machine over this link.
nonisolated enum YouTubeMusicBridgeProtocol {
    /// Wire format version, which both ends must agree on exactly.
    ///
    /// Rejecting a mismatch outright is deliberate: a partially understood
    /// message is worse than a clear "update the extension".
    static let version = 1

    /// Loopback port the bridge server listens on.
    ///
    /// Fixed rather than negotiated because the extension has no way to
    /// discover a dynamic port.
    static let port: UInt16 = 19_436

    /// WebSocket subprotocol the extension must request.
    ///
    /// The first filter on an incoming connection: a browser page or another
    /// app that stumbles onto the port will not name this.
    static let subprotocolName = "reprise-youtube-music-v1"

    /// Extension id in Chromium browsers.
    static let extensionID = "apmolpbmjjndmedbogieopgmapoehdlp"

    /// Extension id in Firefox.
    static let firefoxExtensionID = "reprise-youtube-music@junx.dev"

    /// Public key pinning the Chromium extension's identity.
    ///
    /// Determines the extension id, so a build signed with a different key
    /// cannot claim the same origin.
    static let extensionPublicKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEArUGImdEvl2yZFlOUsVt/7lfhIuhpIPjLVbp4ihHMu5rHbIxFPGsA3q3W5IcPbcI/G6ujsh7C5LRC+1c9X4ftXBEcGEKryKPZ3WlfsmwuuXxEd3N6x6OzCw0ABEplzwuDh6ZMCFfDY8sG30Au1UoJTK3ZOunRCp9K/UFl+a76ozZRmKl284R8cWHjduDeEO5cMmjO+LTsXwT+df+rY14cWA88+tEvzdhiZpctHIwE7AIXUmr7jrcQlMbz+m4jalHCi2pd/np3BRbSAT5lQN6I4l2LVegdX5cpwQjueBMOROIgerOlIy0avCrmrWty0hF0J5ZOLDa/TvIhp9sAew0d9wIDAQAB"

    /// Exact `Origin` a Chromium extension connection must present.
    static let extensionOrigin = "chrome-extension://\(extensionID)"

    /// Largest inbound frame accepted, in bytes.
    ///
    /// Bounds what a client on the loopback port can make Reprise allocate.
    /// Well above a real snapshot, which is a few hundred bytes.
    static let maximumMessageSize = 64 * 1_024

    /// Most tab sessions accepted in one message.
    ///
    /// A user might have a handful of YouTube Music tabs; 32 leaves room while
    /// keeping the session list bounded.
    static let maximumSessionCount = 32

    /// How long a snapshot stays current before it is treated as stale.
    ///
    /// A tab that stops reporting - suspended, crashed, or closed without
    /// notice - would otherwise sit in the panel indefinitely showing a track
    /// that is no longer playing.
    static let staleSnapshotInterval: TimeInterval = 5

    /// Decides whether a WebSocket handshake should be accepted.
    ///
    /// Two gates. The subprotocol must be ours, which turns away anything that
    /// merely found the port. Then the `Origin` must be the extension: Chromium
    /// origins are compared against the exact pinned value, since the id is
    /// derived from the signing key and cannot be forged.
    ///
    /// Firefox is the harder case - it assigns each install a random UUID
    /// origin, so no fixed value exists to compare against. The origin is
    /// instead required to be a bare `moz-extension://` URL whose host parses
    /// as a UUID, with no user, password, port, path, query, or fragment. That
    /// shape check is what stops a crafted origin from carrying anything past
    /// the scheme, since it cannot confirm the origin is *our* extension.
    ///
    /// - Parameters:
    ///   - subprotocols: Subprotocols the client offered.
    ///   - headers: Handshake headers, matched case-insensitively as HTTP
    ///     requires.
    /// - Returns: `true` when the connection may proceed.
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

        if origin == extensionOrigin {
            return true
        }

        guard let origin,
              let components = URLComponents(string: origin),
              components.scheme?.lowercased() == "moz-extension",
              components.user == nil,
              components.password == nil,
              components.port == nil,
              components.path.isEmpty,
              components.query == nil,
              components.fragment == nil,
              let host = components.host,
              UUID(uuidString: host) != nil else {
            return false
        }

        return true
    }
}

/// State of the bridge, as the settings pane reports it.
nonisolated enum YouTubeMusicBridgeStatus: Equatable, Sendable {
    /// The server is not running.
    case stopped

    /// The server is listening but no extension has connected.
    case waiting

    /// An extension is connected.
    case connected

    /// The server could not start, with the reason attached.
    case failed(String)

    /// Whether an extension is connected right now.
    var isConnected: Bool {
        self == .connected
    }

    /// Korean status line shown in the settings pane.
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

/// Which browser family an extension connection came from.
nonisolated enum YouTubeMusicBrowserKind: String, CaseIterable, Sendable {
    /// Chrome, Edge, Brave, and other Chromium builds.
    case chromium

    /// Firefox and its derivatives.
    case firefox

    /// Fallback name when the extension does not report the browser.
    var displayName: String {
        switch self {
        case .chromium:
            "Chromium"
        case .firefox:
            "Firefox"
        }
    }

    /// Identifies the browser from the extension id it reported.
    ///
    /// - Parameter extensionID: Id from the hello message.
    /// - Returns: The browser, or `nil` for an id that is not one of ours.
    init?(extensionID: String) {
        switch extensionID {
        case YouTubeMusicBridgeProtocol.extensionID:
            self = .chromium
        case YouTubeMusicBridgeProtocol.firefoxExtensionID:
            self = .firefox
        default:
            return nil
        }
    }
}

/// A YouTube Music tab reported by one connected browser extension.
///
/// The user may have several open across more than one browser, so the
/// settings pane lists them and lets one be chosen. Identity is the
/// connection and tab together, since tab ids are only unique per browser.
nonisolated struct YouTubeMusicSession: Identifiable, Equatable, Sendable {
    /// Stable identity across connections, for SwiftUI lists.
    let id: String

    /// Which extension connection reported this tab.
    let connectionID: UUID

    /// Browser family the tab is in.
    let browser: YouTubeMusicBrowserKind

    /// Browser name for display, as reported or inferred.
    let browserName: String

    /// Extension id that reported the tab.
    let extensionID: String

    /// Extension version, shown to help diagnose version mismatches.
    let extensionVersion: String

    /// Browser-assigned tab id, used to address commands.
    let tabID: Int?

    /// Transport state in this tab.
    let state: PlaybackState

    /// Track title in this tab.
    let title: String

    /// Artist credit in this tab.
    let artist: String

    /// Whether this is the tab Reprise is following.
    let isSelected: Bool

    /// Whether the tab is playing or paused with a track loaded.
    let isActive: Bool

    /// Whether the tab is visible in its browser window.
    let isVisible: Bool

    /// When the tab last reported, or `nil` if it never has.
    let lastUpdatedAt: Date?

    /// Whether the last report is recent enough to trust.
    let isFresh: Bool

    /// Whether this tab has gone quiet and should not be trusted.
    var isStale: Bool { !isFresh }
}

/// A message received from the extension.
nonisolated enum YouTubeMusicInboundMessage: Sendable {
    /// Connection handshake carrying the extension's identity.
    case hello(YouTubeMusicHelloMessage)

    /// Full playback state of the selected tab.
    case snapshot(YouTubeMusicSnapshotMessage)

    /// The list of YouTube Music tabs the extension can see.
    case sessions(YouTubeMusicSessionsMessage)

    /// Keep-alive proving the connection is still live.
    case heartbeat(YouTubeMusicHeartbeatMessage)

    /// Result of a command Reprise sent.
    case acknowledgement(YouTubeMusicAcknowledgementMessage)

    /// Decodes one frame into a typed message.
    ///
    /// The size cap is applied before any parsing, so an oversized frame is
    /// rejected without being decoded. The body is then read twice: once for
    /// its `type` field, and again as the concrete message that names implies.
    /// Decoding cannot be driven off a single pass because the payload shape
    /// is only known after the type is read.
    ///
    /// Note this only produces the message; each type still has to be
    /// validated before its contents are trusted.
    ///
    /// - Parameter data: Raw WebSocket frame.
    /// - Returns: The decoded message.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/messageTooLarge``,
    ///   ``YouTubeMusicBridgeProtocolError/unknownMessageType(_:)``, or a
    ///   `DecodingError` when the body does not match its declared type.
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
        case "sessions":
            return .sessions(
                try decoder.decode(YouTubeMusicSessionsMessage.self, from: data)
            )
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

/// Just enough of a message to learn which type it is.
nonisolated private struct MessageHeader: Decodable {
    /// Discriminator naming the concrete message type.
    let type: String
}

/// Wire form of the tab list.
nonisolated struct YouTubeMusicSessionsMessage: Decodable, Sendable {
    /// Message discriminator; must be `sessions`.
    let type: String

    /// Protocol version the extension is speaking.
    let protocolVersion: Int

    /// Monotonic counter for ordering messages.
    let sequence: Int

    /// Tab the extension considers selected, if any.
    let selectedTabID: Int?

    /// One entry per YouTube Music tab.
    let sessions: [YouTubeMusicTabSessionMessage]

    /// Maps the wire's `tabId` spelling onto Swift naming.
    enum CodingKeys: String, CodingKey {
        case type
        case protocolVersion
        case sequence
        case selectedTabID = "selectedTabId"
        case sessions
    }

    /// Checks the message and converts it into a trusted payload.
    ///
    /// Beyond the type, version, and count checks, two invariants are
    /// enforced that later code depends on: tab ids must be unique, since they
    /// key the session map and a duplicate would silently drop a tab, and a
    /// declared selection must name a tab actually present, or Reprise would
    /// follow a session it has no state for.
    ///
    /// - Returns: The validated payload.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` on any
    ///   violated invariant, or
    ///   ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``.
    func validated() throws -> YouTubeMusicSessionsPayload {
        guard type == "sessions",
              sequence >= 0,
              sessions.count
                <= YouTubeMusicBridgeProtocol.maximumSessionCount else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)

        var seenTabIDs = Set<Int>()
        let sessions = try sessions.map { session in
            guard seenTabIDs.insert(session.tabID).inserted else {
                throw YouTubeMusicBridgeProtocolError.invalidMessage
            }
            return try session.validated()
        }
        if let selectedTabID,
           !seenTabIDs.contains(selectedTabID) {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }

        return YouTubeMusicSessionsPayload(
            sequence: sequence,
            selectedTabID: selectedTabID,
            sessions: sessions
        )
    }
}

/// Wire form of one tab in the session list.
nonisolated struct YouTubeMusicTabSessionMessage: Decodable, Sendable {
    /// Browser-assigned tab id.
    let tabID: Int

    /// Playback state as a raw string.
    let state: String

    /// Track title.
    let title: String

    /// Artist credit, which the extension may omit.
    let artist: String?

    /// Whether the tab is visible in its window.
    let visible: Bool

    /// When the tab captured this, in milliseconds since the epoch.
    let updatedAtMilliseconds: Double

    /// Maps the wire's abbreviated names onto Swift naming.
    enum CodingKeys: String, CodingKey {
        case tabID = "tabId"
        case state
        case title
        case artist
        case visible
        case updatedAtMilliseconds = "updatedAtMs"
    }

    /// Checks the tab entry and converts it into a trusted payload.
    ///
    /// Text is trimmed and length-capped, since it is rendered in the menu bar
    /// where an unbounded string would be a display problem. A timestamp more
    /// than five seconds in the future is rejected: clocks drift a little, but
    /// a far-future value would make the entry permanently outrank fresher
    /// ones in the staleness comparison.
    ///
    /// A tab with no title is forced to `stopped` whatever it claimed, because
    /// YouTube Music briefly reports playing with an empty title while a video
    /// loads, and taking that at face value makes the panel flicker.
    ///
    /// - Returns: The validated payload.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage``.
    func validated() throws -> YouTubeMusicTabSessionPayload {
        guard tabID >= 0,
              let state = PlaybackState(rawValue: state),
              state != .unavailable,
              updatedAtMilliseconds.isFinite,
              updatedAtMilliseconds > 0 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }

        let title = sanitized(title, maximumLength: 512)
        let updatedAt = Date(
            timeIntervalSince1970: updatedAtMilliseconds / 1_000
        )
        guard updatedAt.timeIntervalSinceNow <= 5 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        return YouTubeMusicTabSessionPayload(
            tabID: tabID,
            state: title.isEmpty ? .stopped : state,
            title: title,
            artist: sanitized(artist ?? "", maximumLength: 512),
            isVisible: visible,
            updatedAt: updatedAt
        )
    }
}

/// A validated tab list, safe for the bridge to act on.
nonisolated struct YouTubeMusicSessionsPayload: Equatable, Sendable {
    /// Monotonic counter for ordering messages.
    let sequence: Int

    /// Tab the extension considers selected, guaranteed to be in ``sessions``.
    let selectedTabID: Int?

    /// The tabs, with unique ids.
    let sessions: [YouTubeMusicTabSessionPayload]
}

/// A validated tab entry.
nonisolated struct YouTubeMusicTabSessionPayload: Equatable, Sendable {
    /// Browser-assigned tab id.
    let tabID: Int

    /// Playback state, never `unavailable`.
    let state: PlaybackState

    /// Track title, trimmed and length-capped.
    let title: String

    /// Artist credit, trimmed and length-capped.
    let artist: String

    /// Whether the tab is visible in its window.
    let isVisible: Bool

    /// When the tab captured this.
    let updatedAt: Date
}

/// Wire form of the connection handshake.
nonisolated struct YouTubeMusicHelloMessage: Decodable, Sendable {
    /// Message discriminator; must be `hello`.
    let type: String

    /// Protocol version the extension is speaking.
    let protocolVersion: Int

    /// Extension version, for display and diagnostics.
    let extensionVersion: String

    /// Extension id, checked against the known ids.
    let extensionID: String

    /// Browser name, which the extension may omit.
    let browserName: String?

    /// Maps the wire's `extensionId` spelling onto Swift naming.
    enum CodingKeys: String, CodingKey {
        case type
        case protocolVersion
        case extensionVersion
        case extensionID = "extensionId"
        case browserName
    }

    /// Checks the handshake.
    ///
    /// The extension id must be one of the two known values. Combined with the
    /// origin check in
    /// ``YouTubeMusicBridgeProtocol/acceptsHandshake(subprotocols:headers:)``,
    /// this is the second point at which an unrelated client is turned away.
    /// String lengths are capped because both are displayed in settings.
    ///
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` or
    ///   ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``.
    func validate() throws {
        guard type == "hello" else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)
        guard !extensionVersion.isEmpty,
              extensionVersion.count <= 64,
              (browserName?.count ?? 0) <= 64,
              [
                  YouTubeMusicBridgeProtocol.extensionID,
                  YouTubeMusicBridgeProtocol.firefoxExtensionID,
              ].contains(extensionID) else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
    }

    /// Browser family implied by the extension id.
    var browser: YouTubeMusicBrowserKind? {
        YouTubeMusicBrowserKind(extensionID: extensionID)
    }

    /// Browser name to display.
    ///
    /// Prefers what the extension reported, since a user running Brave should
    /// see Brave rather than the generic family name; falls back to the family
    /// and finally to a neutral label.
    var resolvedBrowserName: String {
        let browserName = sanitized(browserName ?? "", maximumLength: 64)
        if !browserName.isEmpty {
            return browserName
        }
        return browser?.displayName ?? "Browser"
    }
}

/// Wire form of the keep-alive message.
nonisolated struct YouTubeMusicHeartbeatMessage: Decodable, Sendable {
    /// Message discriminator; must be `heartbeat`.
    let type: String

    /// Protocol version the extension is speaking.
    let protocolVersion: Int

    /// Checks the heartbeat.
    ///
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` or
    ///   ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``.
    func validate() throws {
        guard type == "heartbeat" else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)
    }
}

/// Wire form of a command result.
nonisolated struct YouTubeMusicAcknowledgementMessage: Decodable, Sendable {
    /// Message discriminator; must be `ack`.
    let type: String

    /// Protocol version the extension is speaking.
    let protocolVersion: Int

    /// Id of the command this answers.
    let id: String

    /// Whether the command succeeded.
    let success: Bool

    /// Why it failed, when it did.
    let error: String?

    /// Checks the acknowledgement.
    ///
    /// The id must be non-empty because it is what matches this reply to the
    /// caller waiting on it; lengths are capped as the error text is shown.
    ///
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` or
    ///   ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``.
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

/// Wire form of the full playback snapshot.
nonisolated struct YouTubeMusicSnapshotMessage: Decodable, Sendable {
    /// Message discriminator; must be `snapshot`.
    let type: String

    /// Protocol version the extension is speaking.
    let protocolVersion: Int

    /// Monotonic counter for ordering messages.
    let sequence: Int

    /// Tab this snapshot describes.
    let tabID: Int?

    /// Playback state as a raw string.
    let state: String

    /// Track title.
    let title: String

    /// Album name, which the extension may omit.
    let album: String?

    /// Artist credit, which the extension may omit.
    let artist: String?

    /// Track length in seconds.
    let duration: TimeInterval

    /// Playback position in seconds.
    let position: TimeInterval

    /// Volume on a 0 to 100 scale.
    let volume: Double

    /// Playback speed multiplier, which the extension may omit.
    let playbackRate: Double?

    /// When the tab captured this, in milliseconds since the epoch.
    let capturedAtMilliseconds: Double?

    /// Cover art URL.
    let artworkURL: String?

    /// YouTube video id, the most reliable track identity.
    let videoID: String?

    /// Link back to the track on YouTube Music.
    let trackURL: String?

    /// Maps the wire's abbreviated names onto Swift naming.
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
        case playbackRate
        case capturedAtMilliseconds = "capturedAtMs"
        case artworkURL = "artworkUrl"
        case videoID = "videoId"
        case trackURL = "trackUrl"
    }

    /// Checks the snapshot and converts it into a trusted payload.
    ///
    /// Every number is required to be finite before use: these values reach
    /// SwiftUI layout, where a NaN does not degrade gracefully. Playback rate
    /// is additionally bounded to a sane range, since it multiplies the
    /// position estimate between polls.
    ///
    /// URLs are the part most worth distrusting, since they come from a web
    /// page. Both must be HTTPS and length-capped, and the track link must
    /// point at `music.youtube.com`, so a snapshot cannot turn Reprise's link
    /// into a jump to an arbitrary site. Failing rather than dropping a bad
    /// URL is deliberate: a well-behaved extension never sends one, so its
    /// presence means the message should not be trusted at all.
    ///
    /// As with a tab entry, an empty title forces `stopped` to avoid the
    /// flicker while a video loads.
    ///
    /// - Returns: The validated payload.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` or
    ///   ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``.
    func validated() throws -> YouTubeMusicSnapshotPayload {
        guard type == "snapshot", sequence >= 0 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }
        try validateProtocolVersion(protocolVersion)

        let state = PlaybackState(rawValue: state)
        let playbackRate = playbackRate ?? 1
        guard let state,
              state != .unavailable,
              duration.isFinite,
              position.isFinite,
              volume.isFinite,
              playbackRate.isFinite,
              playbackRate > 0,
              playbackRate <= 16 else {
            throw YouTubeMusicBridgeProtocolError.invalidMessage
        }

        let capturedAt: Date?
        if let capturedAtMilliseconds {
            guard capturedAtMilliseconds.isFinite,
                  capturedAtMilliseconds > 0 else {
                throw YouTubeMusicBridgeProtocolError.invalidMessage
            }
            capturedAt = Date(
                timeIntervalSince1970: capturedAtMilliseconds / 1_000
            )
        } else {
            capturedAt = nil
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
            playbackRate: playbackRate,
            capturedAt: capturedAt,
            artworkURL: artworkURL,
            videoID: sanitized(videoID ?? "", maximumLength: 128),
            trackURL: trackURL
        )
    }
}

/// A validated playback snapshot, safe for the bridge to act on.
nonisolated struct YouTubeMusicSnapshotPayload: Equatable, Sendable {
    /// Monotonic counter for ordering messages.
    let sequence: Int

    /// Tab this snapshot describes.
    let tabID: Int?

    /// Playback state, never `unavailable`.
    let state: PlaybackState

    /// Track title, trimmed and length-capped.
    let title: String

    /// Album name, trimmed and length-capped.
    let album: String

    /// Artist credit, trimmed and length-capped.
    let artist: String

    /// Track length in seconds, never negative.
    let duration: TimeInterval

    /// Playback position in seconds, never negative.
    let position: TimeInterval

    /// Volume from 0 to 100.
    let volume: Int

    /// Playback speed, finite and within a sane range.
    let playbackRate: Double

    /// When the tab captured this, when it said.
    let capturedAt: Date?

    /// Validated HTTPS cover art URL.
    let artworkURL: URL?

    /// YouTube video id, empty when absent.
    let videoID: String

    /// Validated link on `music.youtube.com`.
    let trackURL: URL?

    /// Identity used to decide whether the track has changed.
    ///
    /// Prefers the video id, which is exact. Text is the fallback for tracks
    /// with no id - a live stream, or a page the extension read partially -
    /// where a change in title is the only signal available.
    var trackKey: String {
        if !videoID.isEmpty {
            return videoID
        }
        return [title, album, artist].joined(separator: "\u{0}")
    }
}

/// A command sent from Reprise to the extension.
nonisolated struct YouTubeMusicCommandMessage: Encodable, Equatable, Sendable {
    /// Message discriminator.
    let type = "command"

    /// Protocol version Reprise speaks.
    let protocolVersion = YouTubeMusicBridgeProtocol.version

    /// Unique id the acknowledgement will quote back.
    let id: String

    /// Command name the extension understands.
    let command: String

    /// Tab to act on, or `nil` for the selected one.
    let tabID: Int?

    /// Target position in seconds, for `seek`.
    let position: TimeInterval?

    /// Target volume from 0 to 100, for `setVolume`.
    let volume: Int?

    /// Maps the wire's `tabId` spelling onto Swift naming.
    enum CodingKeys: String, CodingKey {
        case type
        case protocolVersion
        case id
        case command
        case tabID = "tabId"
        case position
        case volume
    }

    /// Builds a transport command.
    ///
    /// - Parameters:
    ///   - command: Transport control to invoke.
    ///   - tabID: Tab to act on. Defaults to `nil`, meaning the selected tab.
    ///   - id: Correlation id. Defaults to a fresh UUID; injectable so tests
    ///     can assert on the encoded message.
    /// - Returns: The command message.
    static func playback(
        _ command: PlaybackCommand,
        tabID: Int? = nil,
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
            tabID: tabID,
            position: nil,
            volume: nil
        )
    }

    /// Builds a seek command.
    ///
    /// - Parameters:
    ///   - position: Target position in seconds; negatives become 0.
    ///   - id: Correlation id. Defaults to a fresh UUID.
    /// - Returns: The command message.
    static func seek(
        to position: TimeInterval,
        id: UUID = UUID()
    ) -> Self {
        Self(
            id: id.uuidString,
            command: "seek",
            tabID: nil,
            position: max(position, 0),
            volume: nil
        )
    }

    /// Builds a volume command.
    ///
    /// - Parameters:
    ///   - volume: Target level, clamped to 0 through 100.
    ///   - id: Correlation id. Defaults to a fresh UUID.
    /// - Returns: The command message.
    static func setVolume(
        _ volume: Int,
        id: UUID = UUID()
    ) -> Self {
        Self(
            id: id.uuidString,
            command: "setVolume",
            tabID: nil,
            position: nil,
            volume: PlayerVolume.clamped(volume)
        )
    }

    /// Encodes the command for sending.
    ///
    /// Keys are sorted so the output is deterministic, which is what lets
    /// tests compare against a fixed string.
    ///
    /// - Returns: The JSON body.
    /// - Throws: An `EncodingError` if encoding fails.
    func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(self)
    }
}

/// Failures on the extension bridge.
nonisolated enum YouTubeMusicBridgeProtocolError: LocalizedError, Equatable {
    /// The extension speaks a different protocol version.
    case unsupportedProtocolVersion(Int)

    /// The message named a type Reprise does not handle.
    case unknownMessageType(String)

    /// The frame exceeded the size cap.
    case messageTooLarge

    /// The message failed validation.
    case invalidMessage

    /// No extension is connected.
    case extensionNotConnected

    /// A command was sent but never acknowledged.
    case commandNotConfirmed

    /// The extension rejected a command, with its reason if given.
    case commandRejected(String?)

    /// Korean message for the panel's error line.
    ///
    /// The malformed-message cases share one wording: the distinction between
    /// them matters when debugging, not to a user, whose remedy is the same in
    /// every case.
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

/// Requires an exact protocol version match.
///
/// - Parameter version: Version the extension reported.
/// - Throws: ``YouTubeMusicBridgeProtocolError/unsupportedProtocolVersion(_:)``
///   when it differs from ``YouTubeMusicBridgeProtocol/version``.
nonisolated private func validateProtocolVersion(_ version: Int) throws {
    guard version == YouTubeMusicBridgeProtocol.version else {
        throw YouTubeMusicBridgeProtocolError.unsupportedProtocolVersion(
            version
        )
    }
}

/// Trims and truncates text arriving from the extension.
///
/// Every displayed string passes through here, so no web page can push an
/// unbounded value into the menu bar or the settings list.
///
/// - Parameters:
///   - value: Raw text.
///   - maximumLength: Longest string to keep.
/// - Returns: The trimmed, truncated text.
nonisolated private func sanitized(
    _ value: String,
    maximumLength: Int
) -> String {
    let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
    return String(trimmed.prefix(maximumLength))
}

/// Validates a URL supplied by the extension.
///
/// HTTPS is required so artwork and links cannot be downgraded to plaintext,
/// and the optional host requirement pins the track link to YouTube Music so
/// a snapshot cannot redirect the user somewhere else.
///
/// An absent value is fine and yields `nil`; a present but invalid one throws,
/// because a well-behaved extension never sends one and its presence means
/// the whole message is suspect.
///
/// - Parameters:
///   - value: Raw URL string, or `nil`.
///   - maximumLength: Longest string to accept.
///   - requiredHost: Host the URL must have. Defaults to `nil`, meaning any.
/// - Returns: The parsed URL, or `nil` when the input was absent or empty.
/// - Throws: ``YouTubeMusicBridgeProtocolError/invalidMessage`` when a
///   supplied value is too long, unparsable, not HTTPS, or on the wrong host.
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
