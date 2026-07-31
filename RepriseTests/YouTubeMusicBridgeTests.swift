// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation
import Testing
@testable import Reprise

@Suite(.serialized)
@MainActor
struct YouTubeMusicBridgeTests {
    @Test
    func handshakeRequiresTheFixedExtensionOriginAndSubprotocol() {
        let validHeaders = [
            (name: "Origin", value: YouTubeMusicBridgeProtocol.extensionOrigin),
        ]

        #expect(
            YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: [
                    YouTubeMusicBridgeProtocol.subprotocolName,
                ],
                headers: validHeaders
            )
        )
        #expect(
            !YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: ["another-protocol"],
                headers: validHeaders
            )
        )
        #expect(
            YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: [
                    YouTubeMusicBridgeProtocol.subprotocolName,
                ],
                headers: [
                    (
                        name: "Origin",
                        value: "moz-extension://123e4567-e89b-12d3-a456-426614174000"
                    ),
                ]
            )
        )
        #expect(
            !YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: [
                    YouTubeMusicBridgeProtocol.subprotocolName,
                ],
                headers: [
                    (name: "Origin", value: "moz-extension://not-an-uuid"),
                ]
            )
        )
        #expect(
            !YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: [
                    YouTubeMusicBridgeProtocol.subprotocolName,
                ],
                headers: [
                    (name: "Origin", value: "https://music.youtube.com"),
                ]
            )
        )
    }

    @Test
    func snapshotMessagesAreValidatedAndClamped() throws {
        let data = Data(
            #"{"type":"snapshot","protocolVersion":1,"sequence":7,"tabId":42,"state":"playing","title":"  Song  ","album":"Album","artist":"Artist","duration":200,"position":75,"volume":140,"playbackRate":1.5,"capturedAtMs":1000000,"artworkUrl":"https://lh3.googleusercontent.com/art","videoId":"abc123","trackUrl":"https://music.youtube.com/watch?v=abc123"}"#.utf8
        )

        guard case let .snapshot(message) = try YouTubeMusicInboundMessage
            .decode(from: data) else {
            Issue.record("snapshot 메시지로 디코딩되지 않음")
            return
        }
        let snapshot = try message.validated()

        #expect(snapshot.sequence == 7)
        #expect(snapshot.tabID == 42)
        #expect(snapshot.state == .playing)
        #expect(snapshot.title == "Song")
        #expect(snapshot.volume == 100)
        #expect(snapshot.playbackRate == 1.5)
        #expect(snapshot.capturedAt == Date(timeIntervalSince1970: 1_000))
        #expect(snapshot.videoID == "abc123")
        #expect(snapshot.trackURL?.host == "music.youtube.com")
    }

    @Test
    func sessionMessagesExposeEveryBrowserTab() throws {
        let nowMilliseconds = Date().timeIntervalSince1970 * 1_000
        let data = Data(
            #"{"type":"sessions","protocolVersion":1,"sequence":3,"selectedTabId":42,"sessions":[{"tabId":42,"state":"playing","title":"First Song","artist":"First Artist","visible":true,"updatedAtMs":\#(nowMilliseconds)},{"tabId":84,"state":"paused","title":"Second Song","artist":"Second Artist","visible":false,"updatedAtMs":\#(nowMilliseconds)}]}"#.utf8
        )

        guard case let .sessions(message) = try YouTubeMusicInboundMessage
            .decode(from: data) else {
            Issue.record("sessions 메시지로 디코딩되지 않음")
            return
        }
        let payload = try message.validated()

        #expect(payload.sequence == 3)
        #expect(payload.selectedTabID == 42)
        #expect(payload.sessions.count == 2)
        #expect(payload.sessions[0].tabID == 42)
        #expect(payload.sessions[0].state == .playing)
        #expect(payload.sessions[0].title == "First Song")
        #expect(payload.sessions[0].isVisible)
        #expect(payload.sessions[1].tabID == 84)
        #expect(payload.sessions[1].state == .paused)
        #expect(!payload.sessions[1].isVisible)
    }

    @Test
    func helloMessagesRequireAKnownBrowserExtensionID() throws {
        let firefoxHello = Data(
            #"{"type":"hello","protocolVersion":1,"extensionVersion":"1.0.0","extensionId":"reprise-youtube-music@junx.dev","browserName":"LibreWolf"}"#.utf8
        )
        let unknownHello = Data(
            #"{"type":"hello","protocolVersion":1,"extensionVersion":"1.0.0","extensionId":"another-extension@example.com"}"#.utf8
        )

        guard case let .hello(message) = try YouTubeMusicInboundMessage
            .decode(from: firefoxHello) else {
            Issue.record("hello 메시지로 디코딩되지 않음")
            return
        }
        try message.validate()
        #expect(message.browser == .firefox)
        #expect(message.resolvedBrowserName == "LibreWolf")

        do {
            guard case let .hello(message) = try YouTubeMusicInboundMessage
                .decode(from: unknownHello) else {
                Issue.record("hello 메시지로 디코딩되지 않음")
                return
            }
            try message.validate()
            Issue.record("등록되지 않은 확장 ID가 허용됨")
        } catch let error as YouTubeMusicBridgeProtocolError {
            #expect(error == .invalidMessage)
        }
    }

    @Test
    func snapshotRejectsUnsupportedProtocolAndUntrustedTrackURL() {
        let unsupportedVersion = Data(
            #"{"type":"snapshot","protocolVersion":2,"sequence":0,"state":"paused","title":"Song","duration":10,"position":1,"volume":50}"#.utf8
        )
        let untrustedTrackURL = Data(
            #"{"type":"snapshot","protocolVersion":1,"sequence":0,"state":"paused","title":"Song","duration":10,"position":1,"volume":50,"trackUrl":"https://example.com/watch?v=1"}"#.utf8
        )

        do {
            guard case let .snapshot(message) =
                    try YouTubeMusicInboundMessage.decode(
                        from: unsupportedVersion
                    ) else {
                Issue.record("snapshot 메시지로 디코딩되지 않음")
                return
            }
            _ = try message.validated()
            Issue.record("지원하지 않는 프로토콜이 허용됨")
        } catch let error as YouTubeMusicBridgeProtocolError {
            #expect(error == .unsupportedProtocolVersion(2))
        } catch {
            Issue.record("예상하지 못한 오류: \(error)")
        }

        do {
            guard case let .snapshot(message) =
                    try YouTubeMusicInboundMessage.decode(
                        from: untrustedTrackURL
                    ) else {
                Issue.record("snapshot 메시지로 디코딩되지 않음")
                return
            }
            _ = try message.validated()
            Issue.record("신뢰하지 않는 트랙 URL이 허용됨")
        } catch let error as YouTubeMusicBridgeProtocolError {
            #expect(error == .invalidMessage)
        } catch {
            Issue.record("예상하지 못한 오류: \(error)")
        }
    }

    @Test
    func commandMessagesUseTheVersionedProtocol() throws {
        let command = YouTubeMusicCommandMessage.setVolume(
            140,
            id: UUID(uuidString: "00000000-0000-0000-0000-000000000001")!
        )
        let object = try JSONSerialization.jsonObject(
            with: command.encoded()
        ) as? [String: Any]

        #expect(object?["type"] as? String == "command")
        #expect(object?["protocolVersion"] as? Int == 1)
        #expect(object?["command"] as? String == "setVolume")
        #expect(object?["volume"] as? Int == 100)

        let targetedPause = YouTubeMusicCommandMessage.playback(
            .pause,
            tabID: 42,
            id: UUID(
                uuidString: "00000000-0000-0000-0000-000000000002"
            )!
        )
        let targetedObject = try JSONSerialization.jsonObject(
            with: targetedPause.encoded()
        ) as? [String: Any]

        #expect(targetedObject?["command"] as? String == "pause")
        #expect(targetedObject?["tabId"] as? Int == 42)
    }

    @Test
    func youtubeMusicParticipatesInPriorityAndAutomaticPause() {
        let spotify = makeSnapshot(
            player: .spotify,
            state: .playing,
            title: "Spotify"
        )
        let youtube = makeSnapshot(
            player: .youtubeMusic,
            state: .playing,
            title: "YouTube"
        )
        let preferred = NowPlayingStore.preferredSnapshot(
            snapshots: [
                .spotify: spotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: youtube,
            ],
            displayOrder: [.youtubeMusic, .spotify, .appleMusic]
        )

        #expect(preferred?.player == .youtubeMusic)

        let playerToPause = NowPlayingStore.playerToPause(
            automaticPauseEnabled: true,
            previousSnapshots: [
                .spotify: spotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: .notRunning(.youtubeMusic),
            ],
            currentSnapshots: [
                .spotify: spotify,
                .appleMusic: .notRunning(.appleMusic),
                .youtubeMusic: youtube,
            ]
        )

        #expect(playerToPause == .spotify)

        let appleMusic = makeSnapshot(
            player: .appleMusic,
            state: .playing,
            title: "Apple Music"
        )
        let playersToPause = NowPlayingStore.playersToPause(
            automaticPauseEnabled: true,
            previousSnapshots: [
                .spotify: spotify,
                .appleMusic: appleMusic,
                .youtubeMusic: .notRunning(.youtubeMusic),
            ],
            currentSnapshots: [
                .spotify: spotify,
                .appleMusic: appleMusic,
                .youtubeMusic: youtube,
            ]
        )

        #expect(playersToPause == [.spotify, .appleMusic])
    }

    @Test
    func localWebSocketTransfersSnapshotsAndCommands() async throws {
        let bridge = YouTubeMusicBridge()
        await bridge.start()

        for _ in 0..<20 {
            if await bridge.connectionStatus() == .waiting {
                break
            }
            try await Task.sleep(for: .milliseconds(25))
        }

        var request = URLRequest(
            url: URL(
                string: "ws://127.0.0.1:\(YouTubeMusicBridgeProtocol.port)"
            )!
        )
        request.setValue(
            YouTubeMusicBridgeProtocol.extensionOrigin,
            forHTTPHeaderField: "Origin"
        )
        request.setValue(
            YouTubeMusicBridgeProtocol.subprotocolName,
            forHTTPHeaderField: "Sec-WebSocket-Protocol"
        )
        let session = URLSession(configuration: .ephemeral)
        let task = session.webSocketTask(with: request)
        task.resume()

        try await task.send(
            .string(
                #"{"type":"hello","protocolVersion":1,"extensionVersion":"1.0.0","extensionId":"apmolpbmjjndmedbogieopgmapoehdlp","browserName":"Google Chrome"}"#
            )
        )
        try await task.send(
            .string(
                #"{"type":"snapshot","protocolVersion":1,"sequence":100,"tabId":7,"state":"playing","title":"Bridge Song","album":"Bridge Album","artist":"Bridge Artist","duration":180,"position":12,"volume":64,"playbackRate":1.25,"artworkUrl":null,"videoId":"bridge123","trackUrl":"https://music.youtube.com/watch?v=bridge123"}"#
            )
        )
        let sessionTimestamp = Date().timeIntervalSince1970 * 1_000
        try await task.send(
            .string(
                #"{"type":"sessions","protocolVersion":1,"sequence":0,"selectedTabId":7,"sessions":[{"tabId":7,"state":"playing","title":"Bridge Song","artist":"Bridge Artist","visible":true,"updatedAtMs":\#(sessionTimestamp)},{"tabId":9,"state":"paused","title":"Background Song","artist":"Background Artist","visible":false,"updatedAtMs":\#(sessionTimestamp)}]}"#
            )
        )

        var receivedSnapshot: PlayerSnapshot?
        for _ in 0..<40 {
            let snapshot = await bridge.snapshot()
            if snapshot.track?.title == "Bridge Song" {
                receivedSnapshot = snapshot
                break
            }
            try await Task.sleep(for: .milliseconds(25))
        }

        #expect(receivedSnapshot?.player == .youtubeMusic)
        #expect(receivedSnapshot?.state == .playing)
        #expect(receivedSnapshot?.volume == 64)
        #expect(receivedSnapshot?.playbackRate == 1.25)

        let initialSessions = await bridge.sessions()
        let initialSession = try #require(
            initialSessions.first { $0.tabID == 7 }
        )
        #expect(initialSessions.count == 2)
        #expect(initialSession.browser == .chromium)
        #expect(initialSession.browserName == "Google Chrome")
        #expect(
            initialSession.extensionID
                == YouTubeMusicBridgeProtocol.extensionID
        )
        #expect(initialSession.extensionVersion == "1.0.0")
        #expect(initialSession.tabID == 7)
        #expect(initialSession.state == .playing)
        #expect(initialSession.title == "Bridge Song")
        #expect(initialSession.artist == "Bridge Artist")
        #expect(initialSession.isSelected)
        #expect(initialSession.isActive)
        #expect(initialSession.isFresh)
        #expect(!initialSession.isStale)
        #expect(initialSession.lastUpdatedAt != nil)
        #expect(
            initialSessions.first { $0.tabID == 9 }?.title
                == "Background Song"
        )
        #expect(initialSessions.first { $0.tabID == 9 }?.isActive == false)

        let commandTask = Task {
            try await bridge.perform(.next)
        }
        let outbound = try await task.receive()
        let outboundData: Data
        switch outbound {
        case let .data(data):
            outboundData = data
        case let .string(string):
            outboundData = Data(string.utf8)
        @unknown default:
            Issue.record("알 수 없는 WebSocket 메시지")
            outboundData = Data()
        }
        let command = try JSONSerialization.jsonObject(
            with: outboundData
        ) as? [String: Any]
        #expect(command?["command"] as? String == "next")
        let commandID = try #require(command?["id"] as? String)

        var firefoxRequest = request
        firefoxRequest.setValue(
            "moz-extension://123e4567-e89b-12d3-a456-426614174000",
            forHTTPHeaderField: "Origin"
        )
        let secondSession = URLSession(configuration: .ephemeral)
        let secondTask = secondSession.webSocketTask(with: firefoxRequest)
        secondTask.resume()
        try await secondTask.send(
            .string(
                #"{"type":"hello","protocolVersion":1,"extensionVersion":"2.0.0","extensionId":"reprise-youtube-music@junx.dev","browserName":"Firefox"}"#
            )
        )

        try await Task.sleep(for: .milliseconds(100))
        let sessionsWithoutFirefoxTabs = await bridge.sessions()
        #expect(sessionsWithoutFirefoxTabs.count == 2)
        #expect(
            !sessionsWithoutFirefoxTabs.contains { $0.browser == .firefox }
        )

        try await secondTask.send(
            .string(
                #"{"type":"snapshot","protocolVersion":1,"sequence":0,"tabId":8,"state":"playing","title":"Second Browser Song","album":"Second Album","artist":"Second Artist","duration":240,"position":30,"volume":55,"playbackRate":1,"artworkUrl":null,"videoId":"second123","trackUrl":"https://music.youtube.com/watch?v=second123"}"#
            )
        )
        try await secondTask.send(
            .string(
                #"{"type":"sessions","protocolVersion":1,"sequence":0,"selectedTabId":8,"sessions":[{"tabId":8,"state":"playing","title":"Second Browser Song","artist":"Second Artist","visible":true,"updatedAtMs":\#(sessionTimestamp)},{"tabId":10,"state":"paused","title":"Firefox Background","artist":"Another Artist","visible":false,"updatedAtMs":\#(sessionTimestamp)}]}"#
            )
        )
        try await Task.sleep(for: .milliseconds(200))

        // A newly connected browser must not steal an equally-ranked active
        // player just by opening its WebSocket connection.
        #expect(await bridge.connectedExtensionVersion() == "1.0.0")
        #expect(await bridge.snapshot().track?.title == "Bridge Song")

        let simultaneousSessions = await bridge.sessions()
        #expect(simultaneousSessions.count == 4)
        let chromiumSession = try #require(
            simultaneousSessions.first { $0.tabID == 7 }
        )
        let firefoxSession = try #require(
            simultaneousSessions.first { $0.tabID == 8 }
        )
        #expect(chromiumSession.tabID == 7)
        #expect(chromiumSession.isActive)
        #expect(firefoxSession.tabID == 8)
        #expect(firefoxSession.browserName == "Firefox")
        #expect(firefoxSession.title == "Second Browser Song")
        #expect(!firefoxSession.isActive)

        try await task.send(
            .string(
                #"{"type":"ack","protocolVersion":1,"id":"\#(commandID)","success":true,"error":null}"#
            )
        )
        try await commandTask.value

        // Once the first browser pauses, the already-playing second browser
        // becomes active without requiring either extension to reconnect.
        try await task.send(
            .string(
                #"{"type":"snapshot","protocolVersion":1,"sequence":101,"tabId":7,"state":"paused","title":"Bridge Song","album":"Bridge Album","artist":"Bridge Artist","duration":180,"position":14,"volume":64,"playbackRate":1.25,"artworkUrl":null,"videoId":"bridge123","trackUrl":"https://music.youtube.com/watch?v=bridge123"}"#
            )
        )
        let switchedTimestamp = Date().timeIntervalSince1970 * 1_000
        try await task.send(
            .string(
                #"{"type":"sessions","protocolVersion":1,"sequence":1,"selectedTabId":7,"sessions":[{"tabId":7,"state":"paused","title":"Bridge Song","artist":"Bridge Artist","visible":true,"updatedAtMs":\#(switchedTimestamp)},{"tabId":9,"state":"paused","title":"Background Song","artist":"Background Artist","visible":false,"updatedAtMs":\#(switchedTimestamp)}]}"#
            )
        )
        for _ in 0..<40 {
            if await bridge.connectedExtensionVersion() == "2.0.0" {
                break
            }
            try await Task.sleep(for: .milliseconds(25))
        }
        #expect(await bridge.connectedExtensionVersion() == "2.0.0")
        #expect(await bridge.snapshot().track?.title == "Second Browser Song")
        let switchedSessions = await bridge.sessions()
        #expect(
            switchedSessions.first { $0.browser == .chromium }?.state
                == .paused
        )
        #expect(
            switchedSessions.first { $0.browser == .chromium }?.isActive
                == false
        )
        #expect(
            switchedSessions.first { $0.browser == .firefox }?.isActive
                == true
        )

        let completionProbe = VolumeCompletionProbe()
        let volumeCommandTask = Task {
            let actualVolume = try await bridge.setVolume(37)
            await completionProbe.store(actualVolume)
            return actualVolume
        }
        let volumeOutbound = try await secondTask.receive()
        let volumeOutboundData: Data
        switch volumeOutbound {
        case let .data(data):
            volumeOutboundData = data
        case let .string(string):
            volumeOutboundData = Data(string.utf8)
        @unknown default:
            Issue.record("알 수 없는 WebSocket 메시지")
            volumeOutboundData = Data()
        }
        let volumeCommand = try JSONSerialization.jsonObject(
            with: volumeOutboundData
        ) as? [String: Any]
        #expect(volumeCommand?["command"] as? String == "setVolume")
        #expect(volumeCommand?["volume"] as? Int == 37)
        let volumeCommandID = try #require(
            volumeCommand?["id"] as? String
        )
        try await secondTask.send(
            .string(
                #"{"type":"ack","protocolVersion":1,"id":"\#(volumeCommandID)","success":true,"error":null}"#
            )
        )
        try await Task.sleep(for: .milliseconds(100))
        #expect(await completionProbe.value() == nil)

        // A matching snapshot from another browser cannot confirm a command
        // that was sent to the selected browser.
        try await task.send(
            .string(
                #"{"type":"snapshot","protocolVersion":1,"sequence":102,"tabId":7,"state":"paused","title":"Bridge Song","album":"Bridge Album","artist":"Bridge Artist","duration":180,"position":15,"volume":37,"playbackRate":1.25,"artworkUrl":null,"videoId":"bridge123","trackUrl":"https://music.youtube.com/watch?v=bridge123"}"#
            )
        )
        try await Task.sleep(for: .milliseconds(100))
        #expect(await completionProbe.value() == nil)

        try await secondTask.send(
            .string(
                #"{"type":"snapshot","protocolVersion":1,"sequence":1,"tabId":8,"state":"playing","title":"Second Browser Song","album":"Second Album","artist":"Second Artist","duration":240,"position":31,"volume":37,"playbackRate":1,"artworkUrl":null,"videoId":"second123","trackUrl":"https://music.youtube.com/watch?v=second123"}"#
            )
        )
        #expect(try await volumeCommandTask.value == 37)

        secondTask.cancel(with: .normalClosure, reason: nil)
        for _ in 0..<40 {
            if await bridge.connectedExtensionVersion() == "1.0.0" {
                break
            }
            try await Task.sleep(for: .milliseconds(25))
        }
        #expect(await bridge.connectedExtensionVersion() == "1.0.0")
        #expect(await bridge.snapshot().track?.title == "Bridge Song")
        let remainingSessions = await bridge.sessions()
        #expect(remainingSessions.count == 2)
        #expect(remainingSessions.first?.browser == .chromium)
        #expect(remainingSessions.first?.isActive == true)

        task.cancel(with: .normalClosure, reason: nil)
        await bridge.stop()
        session.invalidateAndCancel()
        secondSession.invalidateAndCancel()
    }

    private func makeSnapshot(
        player: MediaPlayerKind,
        state: PlaybackState,
        title: String
    ) -> PlayerSnapshot {
        PlayerSnapshot(
            player: player,
            isRunning: true,
            state: state,
            track: Track(
                title: title,
                album: "Album",
                artist: "Artist"
            ),
            errorMessage: nil
        )
    }
}

private actor VolumeCompletionProbe {
    private var storedValue: Int?

    func store(_ value: Int) {
        storedValue = value
    }

    func value() -> Int? {
        storedValue
    }
}
