// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Foundation
import Network

actor YouTubeMusicBridge {
    static let shared = YouTubeMusicBridge()

    private var server: YouTubeMusicWebSocketServer?
    private var status = YouTubeMusicBridgeStatus.stopped
    private var activeConnectionID: UUID?
    private var connectionIdentities: [UUID: ConnectionIdentity] = [:]
    private var connectionSnapshots: [UUID: ConnectionSnapshot] = [:]
    private var connectionSessionLists: [UUID: ConnectionSessionList] = [:]
    private var lastSequences: [UUID: Int] = [:]
    private var lastSessionSequences: [UUID: Int] = [:]
    private var extensionVersion: String?
    private var automaticallyPausesOtherSessions = false
    private var pendingCommandConnections: [String: UUID] = [:]
    private var commandResults: [String: Result<Void, Error>] = [:]
    private var artworkTrackKey: String?
    private var artworkURL: URL?
    private var artworkData: Data?

    func start() {
        guard server == nil else { return }

        let server = YouTubeMusicWebSocketServer(
            onMessage: { [weak self] connectionID, data in
                Task {
                    await self?.receive(
                        data,
                        from: connectionID
                    )
                }
            },
            onStatusChange: { [weak self] status in
                Task {
                    await self?.setStatus(status)
                }
            },
            onConnectionClosed: { [weak self] connectionID in
                Task {
                    await self?.connectionClosed(connectionID)
                }
            }
        )
        self.server = server

        do {
            try server.start()
            status = .waiting
        } catch {
            status = .failed(error.localizedDescription)
        }
    }

    func stop() async {
        await server?.stop()
        server = nil
        status = .stopped
        activeConnectionID = nil
        connectionIdentities.removeAll()
        connectionSnapshots.removeAll()
        connectionSessionLists.removeAll()
        lastSequences.removeAll()
        lastSessionSequences.removeAll()
        extensionVersion = nil
        pendingCommandConnections.removeAll()
        commandResults.removeAll()
        clearArtwork()
    }

    func connectionStatus() -> YouTubeMusicBridgeStatus {
        status
    }

    func connectedExtensionVersion() -> String? {
        extensionVersion
    }

    func setAutomaticallyPausesOtherSessions(_ enabled: Bool) async {
        guard automaticallyPausesOtherSessions != enabled else { return }

        automaticallyPausesOtherSessions = enabled
        guard enabled,
              let retainedSession = preferredPlayingSession() else {
            return
        }
        await pausePlayingSessions(except: retainedSession)
    }

    func sessions(at date: Date = Date()) -> [YouTubeMusicSession] {
        _ = selectActiveConnection(at: date)

        return connectionIdentities.flatMap { connectionID, identity in
            guard let browser = YouTubeMusicBrowserKind(
                extensionID: identity.extensionID
            ) else {
                return [YouTubeMusicSession]()
            }

            if let sessionList = connectionSessionLists[connectionID] {
                return sessionList.payload.sessions.map { tab in
                    let isFresh = date.timeIntervalSince(tab.updatedAt)
                        <= YouTubeMusicBridgeProtocol.staleSnapshotInterval
                    let isSelected = sessionList.payload.selectedTabID
                        == tab.tabID
                    return YouTubeMusicSession(
                        id: Self.sessionIdentifier(
                            connectionID: connectionID,
                            tabID: tab.tabID
                        ),
                        connectionID: connectionID,
                        browser: browser,
                        browserName: identity.browserName,
                        extensionID: identity.extensionID,
                        extensionVersion: identity.extensionVersion,
                        tabID: tab.tabID,
                        state: tab.state,
                        title: tab.title,
                        artist: tab.artist,
                        isSelected: isSelected,
                        isActive: isFresh
                            && activeConnectionID == connectionID
                            && isSelected,
                        isVisible: tab.isVisible,
                        lastUpdatedAt: tab.updatedAt,
                        isFresh: isFresh
                    )
                }
            }

            // Compatibility with extensions that only report the selected
            // snapshot and do not yet send a full tab session list.
            guard let connectionSnapshot = connectionSnapshots[connectionID],
                  let tabID = connectionSnapshot.payload.tabID else {
                return [YouTubeMusicSession]()
            }
            let payload = connectionSnapshot.payload
            let isStale = date.timeIntervalSince(connectionSnapshot.receivedAt)
                > YouTubeMusicBridgeProtocol.staleSnapshotInterval

            return [YouTubeMusicSession(
                id: Self.sessionIdentifier(
                    connectionID: connectionID,
                    tabID: tabID
                ),
                connectionID: connectionID,
                browser: browser,
                browserName: identity.browserName,
                extensionID: identity.extensionID,
                extensionVersion: identity.extensionVersion,
                tabID: tabID,
                state: payload.state,
                title: payload.title,
                artist: payload.artist,
                isSelected: true,
                isActive: !isStale && activeConnectionID == connectionID,
                isVisible: false,
                lastUpdatedAt: connectionSnapshot.receivedAt,
                isFresh: !isStale
            )]
        }
        .sorted { lhs, rhs in
            if lhs.isActive != rhs.isActive {
                return lhs.isActive
            }
            if lhs.browser.rawValue != rhs.browser.rawValue {
                return lhs.browser.rawValue < rhs.browser.rawValue
            }
            if lhs.connectionID != rhs.connectionID {
                return lhs.connectionID.uuidString
                    < rhs.connectionID.uuidString
            }
            if lhs.tabID != rhs.tabID {
                return (lhs.tabID ?? .max) < (rhs.tabID ?? .max)
            }
            return lhs.id < rhs.id
        }
    }

    func snapshot(at date: Date = Date()) -> PlayerSnapshot {
        guard status.isConnected else {
            return .notRunning(.youtubeMusic)
        }

        _ = selectActiveConnection(at: date)
        guard let activeConnectionID,
              let snapshot = connectionSnapshots[activeConnectionID],
              date.timeIntervalSince(snapshot.receivedAt)
                <= YouTubeMusicBridgeProtocol.staleSnapshotInterval else {
            return PlayerSnapshot(
                player: .youtubeMusic,
                isRunning: true,
                state: .stopped,
                track: nil,
                errorMessage: nil
            )
        }

        let latestSnapshot = snapshot.payload
        let track: Track?
        if latestSnapshot.title.isEmpty {
            track = nil
        } else {
            let estimatedPosition = PlaybackPosition.estimated(
                observedPosition: latestSnapshot.position,
                state: latestSnapshot.state,
                observedAt: snapshot.observedAt,
                at: date,
                duration: latestSnapshot.duration,
                playbackRate: latestSnapshot.playbackRate
            )
            track = Track(
                title: latestSnapshot.title,
                album: latestSnapshot.album,
                artist: latestSnapshot.artist,
                duration: latestSnapshot.duration,
                position: estimatedPosition,
                artworkData: artworkTrackKey == latestSnapshot.trackKey
                    ? artworkData
                    : nil
            )
        }

        return PlayerSnapshot(
            player: .youtubeMusic,
            isRunning: true,
            state: latestSnapshot.state,
            track: track,
            volume: latestSnapshot.volume,
            playbackRate: latestSnapshot.playbackRate,
            errorMessage: nil
        )
    }

    func perform(_ command: PlaybackCommand) async throws {
        _ = try await send(.playback(command))
    }

    func setVolume(_ volume: Int) async throws -> Int {
        let volume = PlayerVolume.clamped(volume)
        let dispatch = try await send(.setVolume(volume))

        for _ in 0..<40 {
            if let snapshot = connectionSnapshots[dispatch.connectionID]?.payload,
               snapshot.sequence > dispatch.startingSequence,
               abs(snapshot.volume - volume) <= 1 {
                return snapshot.volume
            }
            try await Task.sleep(for: .milliseconds(50))
        }

        throw YouTubeMusicBridgeProtocolError.commandNotConfirmed
    }

    func setPosition(_ position: TimeInterval) async throws -> TimeInterval {
        let position = position.isFinite ? max(position, 0) : 0
        let dispatch = try await send(.seek(to: position))

        for _ in 0..<12 {
            try await Task.sleep(for: .milliseconds(100))
            guard let snapshot = connectionSnapshots[dispatch.connectionID]?.payload,
                  snapshot.sequence > dispatch.startingSequence else {
                continue
            }
            let actualPosition = snapshot.position
            if PlaybackPosition.confirmsSeek(
                actual: actualPosition,
                target: position
            ) {
                return actualPosition
            }
        }

        throw YouTubeMusicBridgeProtocolError.commandNotConfirmed
    }

    private func setStatus(_ status: YouTubeMusicBridgeStatus) {
        self.status = status
        if !status.isConnected {
            activeConnectionID = nil
            connectionIdentities.removeAll()
            connectionSnapshots.removeAll()
            connectionSessionLists.removeAll()
            lastSequences.removeAll()
            lastSessionSequences.removeAll()
            extensionVersion = nil
            clearArtwork()
        }
    }

    private func receive(
        _ data: Data,
        from connectionID: UUID
    ) async {
        let message: YouTubeMusicInboundMessage
        do {
            message = try YouTubeMusicInboundMessage.decode(from: data)
        } catch {
            return
        }

        switch message {
        case let .hello(message):
            do {
                try message.validate()
                connectionIdentities[connectionID] = ConnectionIdentity(
                    extensionID: message.extensionID,
                    extensionVersion: message.extensionVersion,
                    browserName: message.resolvedBrowserName
                )
            } catch {
                return
            }

        case let .heartbeat(message):
            guard connectionIdentities[connectionID] != nil else {
                return
            }
            do {
                try message.validate()
            } catch {
                return
            }

        case let .acknowledgement(message):
            guard connectionIdentities[connectionID] != nil else {
                return
            }
            do {
                try message.validate()
            } catch {
                return
            }
            guard pendingCommandConnections[message.id] == connectionID else {
                return
            }
            commandResults[message.id] = message.success
                ? .success(())
                : .failure(
                    YouTubeMusicBridgeProtocolError.commandRejected(
                        message.error
                    )
                )

        case let .snapshot(message):
            guard connectionIdentities[connectionID] != nil else {
                return
            }
            let payload: YouTubeMusicSnapshotPayload
            do {
                payload = try message.validated()
            } catch {
                return
            }
            guard payload.sequence >= (lastSequences[connectionID] ?? -1) else {
                return
            }

            let receivedAt = Date()
            lastSequences[connectionID] = payload.sequence
            connectionSnapshots[connectionID] = ConnectionSnapshot(
                payload: payload,
                receivedAt: receivedAt,
                observedAt: Self.observationDate(
                    capturedAt: payload.capturedAt,
                    receivedAt: receivedAt
                )
            )
            let previousConnectionID = activeConnectionID
            let selectedConnectionID = selectActiveConnection(at: receivedAt)
            if let selectedConnectionID,
               selectedConnectionID == connectionID
                || selectedConnectionID != previousConnectionID,
               let selectedSnapshot = connectionSnapshots[selectedConnectionID] {
                await updateArtwork(
                    for: selectedSnapshot.payload,
                    from: selectedConnectionID
                )
            }

        case let .sessions(message):
            guard connectionIdentities[connectionID] != nil else {
                return
            }
            let payload: YouTubeMusicSessionsPayload
            do {
                payload = try message.validated()
            } catch {
                return
            }
            guard payload.sequence
                    >= (lastSessionSequences[connectionID] ?? -1) else {
                return
            }

            let previousPayload = connectionSessionLists[connectionID]?.payload
            lastSessionSequences[connectionID] = payload.sequence
            connectionSessionLists[connectionID] = ConnectionSessionList(
                payload: payload
            )

            guard automaticallyPausesOtherSessions,
                  let newlyPlayingSession = Self.newlyPlayingSession(
                      in: payload,
                      previousPayload: previousPayload,
                      connectionID: connectionID
                  ) else {
                return
            }
            await pausePlayingSessions(except: newlyPlayingSession)
        }
    }

    private func send(
        _ command: YouTubeMusicCommandMessage,
        to targetConnectionID: UUID? = nil
    ) async throws -> CommandDispatch {
        guard status.isConnected,
              let server else {
            throw YouTubeMusicBridgeProtocolError.extensionNotConnected
        }
        let connectionID: UUID
        if let targetConnectionID {
            guard connectionIdentities[targetConnectionID] != nil else {
                throw YouTubeMusicBridgeProtocolError.extensionNotConnected
            }
            connectionID = targetConnectionID
        } else {
            guard let selectedConnectionID = selectActiveConnection() else {
                throw YouTubeMusicBridgeProtocolError.extensionNotConnected
            }
            connectionID = selectedConnectionID
        }
        let dispatch = CommandDispatch(
            connectionID: connectionID,
            startingSequence: lastSequences[connectionID] ?? -1
        )

        pendingCommandConnections[command.id] = connectionID
        defer {
            pendingCommandConnections[command.id] = nil
            commandResults[command.id] = nil
        }
        try await server.send(
            command.encoded(),
            to: connectionID
        )

        for _ in 0..<120 {
            if let result = commandResults[command.id] {
                try result.get()
                return dispatch
            }
            try await Task.sleep(for: .milliseconds(50))
        }

        throw YouTubeMusicBridgeProtocolError.commandNotConfirmed
    }

    private func preferredPlayingSession(
        at date: Date = Date()
    ) -> YouTubeMusicSessionTarget? {
        let sessions = playingSessionTargets(at: date)
        guard !sessions.isEmpty else { return nil }

        return sessions.max { lhs, rhs in
            if lhs.isVisible != rhs.isVisible {
                return !lhs.isVisible
            }
            if lhs.isSelected != rhs.isSelected {
                return !lhs.isSelected
            }
            return lhs.updatedAt < rhs.updatedAt
        }
    }

    private func playingSessionTargets(
        at date: Date = Date()
    ) -> [YouTubeMusicSessionTarget] {
        connectionSessionLists.flatMap { connectionID, sessionList in
            sessionList.payload.sessions.compactMap { session in
                guard session.state == .playing,
                      date.timeIntervalSince(session.updatedAt)
                        <= YouTubeMusicBridgeProtocol.staleSnapshotInterval else {
                    return nil
                }
                return YouTubeMusicSessionTarget(
                    connectionID: connectionID,
                    tabID: session.tabID,
                    isSelected: sessionList.payload.selectedTabID
                        == session.tabID,
                    isVisible: session.isVisible,
                    updatedAt: session.updatedAt
                )
            }
        }
    }

    private func pausePlayingSessions(
        except retainedSession: YouTubeMusicSessionTarget
    ) async {
        let targets = playingSessionTargets().filter {
            $0.connectionID != retainedSession.connectionID
                || $0.tabID != retainedSession.tabID
        }
        guard !targets.isEmpty else { return }

        await withTaskGroup(of: Void.self) { group in
            for target in targets {
                group.addTask { [weak self] in
                    guard let self else { return }
                    _ = try? await self.send(
                        .playback(.pause, tabID: target.tabID),
                        to: target.connectionID
                    )
                }
            }
        }
    }

    @discardableResult
    private func selectActiveConnection(at date: Date = Date()) -> UUID? {
        let candidates = connectionSnapshots.filter {
            date.timeIntervalSince($0.value.receivedAt)
                <= YouTubeMusicBridgeProtocol.staleSnapshotInterval
        }
        guard !candidates.isEmpty else {
            if activeConnectionID != nil {
                activeConnectionID = nil
                extensionVersion = nil
                clearArtwork()
            }
            return nil
        }

        let bestPriority = candidates.values
            .map { Self.selectionPriority(for: $0.payload) }
            .max() ?? 0
        let selectedConnectionID: UUID
        if let activeConnectionID,
           let activeSnapshot = candidates[activeConnectionID],
           Self.selectionPriority(for: activeSnapshot.payload) == bestPriority {
            selectedConnectionID = activeConnectionID
        } else {
            selectedConnectionID = candidates
                .filter {
                    Self.selectionPriority(for: $0.value.payload)
                        == bestPriority
                }
                .max {
                    $0.value.receivedAt < $1.value.receivedAt
                }!
                .key
        }

        activateConnection(selectedConnectionID)
        return selectedConnectionID
    }

    private func activateConnection(_ connectionID: UUID) {
        guard activeConnectionID != connectionID else { return }

        activeConnectionID = connectionID
        extensionVersion = connectionIdentities[connectionID]?.extensionVersion
        clearArtwork()
    }

    private func connectionClosed(_ connectionID: UUID) async {
        connectionIdentities[connectionID] = nil
        connectionSnapshots[connectionID] = nil
        connectionSessionLists[connectionID] = nil
        lastSequences[connectionID] = nil
        lastSessionSequences[connectionID] = nil
        for (commandID, targetConnectionID) in pendingCommandConnections
        where targetConnectionID == connectionID {
            commandResults[commandID] = .failure(
                YouTubeMusicBridgeProtocolError.extensionNotConnected
            )
        }
        guard activeConnectionID == connectionID else { return }

        activeConnectionID = nil
        extensionVersion = nil
        clearArtwork()
        if let fallbackConnectionID = selectActiveConnection(),
           let fallbackSnapshot = connectionSnapshots[fallbackConnectionID] {
            await updateArtwork(
                for: fallbackSnapshot.payload,
                from: fallbackConnectionID
            )
        }
    }

    private func updateArtwork(
        for snapshot: YouTubeMusicSnapshotPayload,
        from connectionID: UUID
    ) async {
        guard !snapshot.title.isEmpty else {
            clearArtwork()
            return
        }

        let trackKey = snapshot.trackKey
        guard artworkTrackKey != trackKey
                || artworkURL != snapshot.artworkURL else {
            return
        }

        artworkTrackKey = trackKey
        artworkURL = snapshot.artworkURL
        artworkData = nil

        guard let url = snapshot.artworkURL else { return }
        var request = URLRequest(url: url)
        request.timeoutInterval = 5

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let response = response as? HTTPURLResponse,
                  (200..<300).contains(response.statusCode),
                  data.count <= 8_000_000,
                  NSImage(data: data) != nil,
                  activeConnectionID == connectionID,
                  connectionSnapshots[connectionID]?.payload.trackKey
                    == trackKey,
                  connectionSnapshots[connectionID]?.payload.artworkURL
                    == url else {
                return
            }
            artworkData = data
        } catch {
            return
        }
    }

    private func clearArtwork() {
        artworkTrackKey = nil
        artworkURL = nil
        artworkData = nil
    }

    private nonisolated static func observationDate(
        capturedAt: Date?,
        receivedAt: Date
    ) -> Date {
        guard let capturedAt,
              receivedAt.timeIntervalSince(capturedAt) >= 0,
              receivedAt.timeIntervalSince(capturedAt) <= 5 else {
            return receivedAt
        }
        return capturedAt
    }

    private nonisolated static func selectionPriority(
        for snapshot: YouTubeMusicSnapshotPayload
    ) -> Int {
        if snapshot.state == .playing, !snapshot.title.isEmpty {
            return 2
        }
        if !snapshot.title.isEmpty {
            return 1
        }
        return 0
    }

    private nonisolated static func newlyPlayingSession(
        in payload: YouTubeMusicSessionsPayload,
        previousPayload: YouTubeMusicSessionsPayload?,
        connectionID: UUID,
        at date: Date = Date()
    ) -> YouTubeMusicSessionTarget? {
        let previousStates = Dictionary(
            uniqueKeysWithValues: previousPayload?.sessions.map {
                ($0.tabID, $0.state)
            } ?? []
        )
        let candidates: [YouTubeMusicSessionTarget] =
            payload.sessions.compactMap { session in
                guard session.state == .playing,
                      previousStates[session.tabID] != .playing,
                      date.timeIntervalSince(session.updatedAt)
                        <= YouTubeMusicBridgeProtocol
                            .staleSnapshotInterval else {
                    return nil
                }
                return YouTubeMusicSessionTarget(
                    connectionID: connectionID,
                    tabID: session.tabID,
                    isSelected: payload.selectedTabID == session.tabID,
                    isVisible: session.isVisible,
                    updatedAt: session.updatedAt
                )
            }

        return candidates.max { lhs, rhs in
            if lhs.isSelected != rhs.isSelected {
                return !lhs.isSelected
            }
            if lhs.isVisible != rhs.isVisible {
                return !lhs.isVisible
            }
            return lhs.updatedAt < rhs.updatedAt
        }
    }

    private nonisolated static func sessionIdentifier(
        connectionID: UUID,
        tabID: Int?
    ) -> String {
        "\(connectionID.uuidString):\(tabID.map(String.init) ?? "pending")"
    }
}

nonisolated private struct ConnectionSnapshot: Sendable {
    let payload: YouTubeMusicSnapshotPayload
    let receivedAt: Date
    let observedAt: Date
}

nonisolated private struct ConnectionSessionList: Sendable {
    let payload: YouTubeMusicSessionsPayload
}

nonisolated private struct YouTubeMusicSessionTarget: Sendable {
    let connectionID: UUID
    let tabID: Int
    let isSelected: Bool
    let isVisible: Bool
    let updatedAt: Date
}

nonisolated private struct ConnectionIdentity: Sendable {
    let extensionID: String
    let extensionVersion: String
    let browserName: String
}

nonisolated private struct CommandDispatch: Sendable {
    let connectionID: UUID
    let startingSequence: Int
}

nonisolated private final class YouTubeMusicWebSocketServer: @unchecked Sendable {
    private let queue = DispatchQueue(
        label: "dev.junx.Reprise.youtubeMusicBridge"
    )
    private let onMessage: @Sendable (UUID, Data) -> Void
    private let onStatusChange: @Sendable (YouTubeMusicBridgeStatus) -> Void
    private let onConnectionClosed: @Sendable (UUID) -> Void
    private var listener: NWListener?
    private var connections: [UUID: NWConnection] = [:]
    private var readyConnectionIDs: Set<UUID> = []

    init(
        onMessage: @escaping @Sendable (UUID, Data) -> Void,
        onStatusChange: @escaping @Sendable (YouTubeMusicBridgeStatus) -> Void,
        onConnectionClosed: @escaping @Sendable (UUID) -> Void
    ) {
        self.onMessage = onMessage
        self.onStatusChange = onStatusChange
        self.onConnectionClosed = onConnectionClosed
    }

    func start() throws {
        let parameters = NWParameters(tls: nil)
        parameters.allowLocalEndpointReuse = true
        parameters.acceptLocalOnly = true

        let websocketOptions = NWProtocolWebSocket.Options()
        websocketOptions.autoReplyPing = true
        websocketOptions.maximumMessageSize =
            YouTubeMusicBridgeProtocol.maximumMessageSize
        websocketOptions.setSubprotocols([
            YouTubeMusicBridgeProtocol.subprotocolName,
        ])
        websocketOptions.setClientRequestHandler(queue) {
            subprotocols,
            headers in
            let accepted = YouTubeMusicBridgeProtocol.acceptsHandshake(
                subprotocols: subprotocols,
                headers: headers
            )
            return NWProtocolWebSocket.Response(
                status: accepted ? .accept : .reject,
                subprotocol: accepted
                    ? YouTubeMusicBridgeProtocol.subprotocolName
                    : nil
            )
        }
        parameters.defaultProtocolStack.applicationProtocols.insert(
            websocketOptions,
            at: 0
        )

        guard let port = NWEndpoint.Port(
            rawValue: YouTubeMusicBridgeProtocol.port
        ) else {
            throw YouTubeMusicWebSocketServerError.invalidPort
        }
        let listener = try NWListener(using: parameters, on: port)
        self.listener = listener

        listener.stateUpdateHandler = { [weak self] state in
            self?.handleListenerState(state)
        }
        listener.newConnectionHandler = { [weak self] connection in
            self?.accept(connection)
        }
        listener.start(queue: queue)
    }

    func stop() async {
        await withCheckedContinuation { continuation in
            queue.async { [weak self] in
                guard let self else {
                    continuation.resume()
                    return
                }
                listener?.cancel()
                listener = nil
                connections.values.forEach { $0.cancel() }
                connections.removeAll()
                readyConnectionIDs.removeAll()
                onStatusChange(.stopped)
                continuation.resume()
            }
        }
    }

    func send(
        _ data: Data,
        to connectionID: UUID
    ) async throws {
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<Void, Error>) in
            queue.async { [weak self] in
                guard let self,
                      readyConnectionIDs.contains(connectionID),
                      let connection = connections[connectionID] else {
                    continuation.resume(
                        throwing:
                            YouTubeMusicBridgeProtocolError.extensionNotConnected
                    )
                    return
                }

                let metadata = NWProtocolWebSocket.Metadata(opcode: .text)
                let context = NWConnection.ContentContext(
                    identifier: "reprise-command",
                    metadata: [metadata]
                )
                connection.send(
                    content: data,
                    contentContext: context,
                    isComplete: true,
                    completion: .contentProcessed { error in
                        if let error {
                            continuation.resume(throwing: error)
                        } else {
                            continuation.resume(returning: ())
                        }
                    }
                )
            }
        }
    }

    private func handleListenerState(_ state: NWListener.State) {
        switch state {
        case .ready:
            onStatusChange(
                readyConnectionIDs.isEmpty ? .waiting : .connected
            )
        case let .failed(error):
            onStatusChange(.failed(error.localizedDescription))
        case .cancelled:
            onStatusChange(.stopped)
        case .setup, .waiting:
            break
        @unknown default:
            break
        }
    }

    private func accept(_ connection: NWConnection) {
        let connectionID = UUID()
        connections[connectionID] = connection
        connection.stateUpdateHandler = { [weak self, weak connection] state in
            guard let self, let connection else { return }
            switch state {
            case .ready:
                readyConnectionIDs.insert(connectionID)
                onStatusChange(.connected)
                receiveNextMessage(
                    from: connection,
                    connectionID: connectionID
                )
            case .failed:
                removeConnection(connectionID)
                if readyConnectionIDs.isEmpty {
                    onStatusChange(.waiting)
                }
            case .cancelled:
                removeConnection(connectionID)
                if readyConnectionIDs.isEmpty {
                    onStatusChange(.waiting)
                }
            case .setup, .waiting, .preparing:
                break
            @unknown default:
                break
            }
        }
        connection.start(queue: queue)
    }

    private func receiveNextMessage(
        from connection: NWConnection,
        connectionID: UUID
    ) {
        connection.receiveMessage { [weak self, weak connection] data, context, _, error in
            guard let self, let connection else { return }

            if error != nil {
                removeConnection(connectionID)
                connection.cancel()
                if readyConnectionIDs.isEmpty {
                    onStatusChange(.waiting)
                }
                return
            }

            let metadata = context?.protocolMetadata(
                definition: NWProtocolWebSocket.definition
            ) as? NWProtocolWebSocket.Metadata

            switch metadata?.opcode {
            case .text:
                if let data,
                   data.count <= YouTubeMusicBridgeProtocol.maximumMessageSize {
                    onMessage(
                        connectionID,
                        data
                    )
                }
            case .close:
                removeConnection(connectionID)
                connection.cancel()
                if readyConnectionIDs.isEmpty {
                    onStatusChange(.waiting)
                }
                return
            default:
                break
            }

            receiveNextMessage(
                from: connection,
                connectionID: connectionID
            )
        }
    }

    private func removeConnection(_ connectionID: UUID) {
        guard connections.removeValue(forKey: connectionID) != nil else {
            return
        }
        readyConnectionIDs.remove(connectionID)
        onConnectionClosed(connectionID)
    }
}

nonisolated private enum YouTubeMusicWebSocketServerError: LocalizedError {
    case invalidPort

    var errorDescription: String? {
        "YouTube Music 브리지 포트를 열 수 없습니다."
    }
}
