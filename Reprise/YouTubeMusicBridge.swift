//
//  YouTubeMusicBridge.swift
//  Reprise
//

import AppKit
import Foundation
import Network

actor YouTubeMusicBridge {
    static let shared = YouTubeMusicBridge()

    private var server: YouTubeMusicWebSocketServer?
    private var status = YouTubeMusicBridgeStatus.stopped
    private var latestSnapshot: YouTubeMusicSnapshotPayload?
    private var latestSnapshotDate: Date?
    private var lastSequence = -1
    private var activeConnectionID: UUID?
    private var connectionVersions: [UUID: String] = [:]
    private var extensionVersion: String?
    private var pendingCommandIDs: Set<String> = []
    private var commandResults: [String: Result<Void, Error>] = [:]
    private var artworkTrackKey: String?
    private var artworkURL: URL?
    private var artworkData: Data?

    func start() {
        guard server == nil else { return }

        let server = YouTubeMusicWebSocketServer(
            onMessage: { [weak self] connectionID, isSelected, data in
                Task {
                    await self?.receive(
                        data,
                        from: connectionID,
                        isSelected: isSelected
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
        latestSnapshot = nil
        latestSnapshotDate = nil
        lastSequence = -1
        activeConnectionID = nil
        connectionVersions.removeAll()
        extensionVersion = nil
        pendingCommandIDs.removeAll()
        commandResults.removeAll()
        clearArtwork()
    }

    func connectionStatus() -> YouTubeMusicBridgeStatus {
        status
    }

    func connectedExtensionVersion() -> String? {
        extensionVersion
    }

    func snapshot(at date: Date = Date()) -> PlayerSnapshot {
        guard status.isConnected else {
            return .notRunning(.youtubeMusic)
        }

        guard let latestSnapshot,
              let latestSnapshotDate,
              date.timeIntervalSince(latestSnapshotDate)
                <= YouTubeMusicBridgeProtocol.staleSnapshotInterval else {
            return PlayerSnapshot(
                player: .youtubeMusic,
                isRunning: true,
                state: .stopped,
                track: nil,
                errorMessage: nil
            )
        }

        let track: Track?
        if latestSnapshot.title.isEmpty {
            track = nil
        } else {
            track = Track(
                title: latestSnapshot.title,
                album: latestSnapshot.album,
                artist: latestSnapshot.artist,
                duration: latestSnapshot.duration,
                position: PlaybackPosition.clamped(
                    latestSnapshot.position,
                    duration: latestSnapshot.duration
                ),
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
            errorMessage: nil
        )
    }

    func perform(_ command: PlaybackCommand) async throws {
        try await send(.playback(command))
    }

    func setVolume(_ volume: Int) async throws -> Int {
        let volume = PlayerVolume.clamped(volume)
        try await send(.setVolume(volume))
        return volume
    }

    func setPosition(_ position: TimeInterval) async throws -> TimeInterval {
        let position = position.isFinite ? max(position, 0) : 0
        try await send(.seek(to: position))

        for _ in 0..<12 {
            try await Task.sleep(for: .milliseconds(100))
            guard let actualPosition = latestSnapshot?.position else {
                continue
            }
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
            extensionVersion = nil
            lastSequence = -1
        }
    }

    private func receive(
        _ data: Data,
        from connectionID: UUID,
        isSelected: Bool
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
                connectionVersions[connectionID] = message.extensionVersion
                if isSelected {
                    activateConnection(
                        connectionID,
                        extensionVersion: message.extensionVersion
                    )
                    extensionVersion = message.extensionVersion
                    lastSequence = -1
                }
            } catch {
                return
            }

        case let .heartbeat(message):
            guard isSelected, connectionVersions[connectionID] != nil else {
                return
            }
            do {
                try message.validate()
            } catch {
                return
            }
            activateConnection(
                connectionID,
                extensionVersion: connectionVersions[connectionID]
            )

        case let .acknowledgement(message):
            guard isSelected, connectionVersions[connectionID] != nil else {
                return
            }
            do {
                try message.validate()
            } catch {
                return
            }
            activateConnection(
                connectionID,
                extensionVersion: connectionVersions[connectionID]
            )
            guard pendingCommandIDs.contains(message.id) else {
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
            guard isSelected,
                  let connectionVersion = connectionVersions[connectionID]
            else {
                return
            }
            let payload: YouTubeMusicSnapshotPayload
            do {
                payload = try message.validated()
            } catch {
                return
            }
            activateConnection(
                connectionID,
                extensionVersion: connectionVersion
            )
            guard payload.sequence >= lastSequence else {
                return
            }

            lastSequence = payload.sequence
            latestSnapshot = payload
            latestSnapshotDate = Date()
            await updateArtwork(for: payload)
        }
    }

    private func send(_ command: YouTubeMusicCommandMessage) async throws {
        guard status.isConnected, let server else {
            throw YouTubeMusicBridgeProtocolError.extensionNotConnected
        }

        pendingCommandIDs.insert(command.id)
        defer {
            pendingCommandIDs.remove(command.id)
            commandResults[command.id] = nil
        }
        try await server.send(command.encoded())

        for _ in 0..<20 {
            if let result = commandResults[command.id] {
                return try result.get()
            }
            try await Task.sleep(for: .milliseconds(50))
        }

        throw YouTubeMusicBridgeProtocolError.commandNotConfirmed
    }

    private func activateConnection(
        _ connectionID: UUID,
        extensionVersion: String?
    ) {
        guard activeConnectionID != connectionID else { return }

        activeConnectionID = connectionID
        self.extensionVersion = extensionVersion
        latestSnapshot = nil
        latestSnapshotDate = nil
        lastSequence = -1
        clearArtwork()
    }

    private func connectionClosed(_ connectionID: UUID) {
        connectionVersions[connectionID] = nil
        guard activeConnectionID == connectionID else { return }

        activeConnectionID = nil
        extensionVersion = nil
        latestSnapshot = nil
        latestSnapshotDate = nil
        lastSequence = -1
        clearArtwork()
    }

    private func updateArtwork(
        for snapshot: YouTubeMusicSnapshotPayload
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
                  latestSnapshot?.trackKey == trackKey,
                  latestSnapshot?.artworkURL == url else {
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
}

nonisolated private final class YouTubeMusicWebSocketServer: @unchecked Sendable {
    private let queue = DispatchQueue(
        label: "dev.junx.Reprise.youtubeMusicBridge"
    )
    private let onMessage: @Sendable (UUID, Bool, Data) -> Void
    private let onStatusChange: @Sendable (YouTubeMusicBridgeStatus) -> Void
    private let onConnectionClosed: @Sendable (UUID) -> Void
    private var listener: NWListener?
    private var connections: [UUID: NWConnection] = [:]
    private var readyConnectionIDs: [UUID] = []

    init(
        onMessage: @escaping @Sendable (UUID, Bool, Data) -> Void,
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

    func send(_ data: Data) async throws {
        try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<Void, Error>) in
            queue.async { [weak self] in
                guard let self,
                      let connectionID = readyConnectionIDs.last,
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
                if !readyConnectionIDs.contains(connectionID) {
                    readyConnectionIDs.append(connectionID)
                }
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
                        readyConnectionIDs.last == connectionID,
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
        readyConnectionIDs.removeAll { $0 == connectionID }
        onConnectionClosed(connectionID)
    }
}

nonisolated private enum YouTubeMusicWebSocketServerError: LocalizedError {
    case invalidPort

    var errorDescription: String? {
        "YouTube Music 브리지 포트를 열 수 없습니다."
    }
}
