// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Foundation
import Network

/// Reprise's side of the YouTube Music browser extension link.
///
/// Runs a loopback WebSocket server, tracks every connected browser and the
/// tabs each reports, and picks one to present as the YouTube Music "player".
///
/// The complexity here is that this is a one-to-many bridge where the other
/// players are one-to-one: a user can have Chrome and Firefox connected at
/// once, each with several YouTube Music tabs. Most of the state below exists
/// to choose a single tab out of that, keep the choice stable while it stays
/// valid, and abandon it the moment it goes quiet.
///
/// An actor because callbacks arrive on the network queue while commands come
/// from the main actor.
actor YouTubeMusicBridge {
    /// The app-wide bridge.
    ///
    /// Singleton because it binds a fixed port; a second instance would fail
    /// to listen and leave the extension connected to the wrong one.
    static let shared = YouTubeMusicBridge()

    private let port: UInt16
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

    /// Creates the bridge without starting it.
    ///
    /// - Parameter port: Port to listen on. Defaults to the protocol's fixed
    ///   port; injectable so tests can bind an ephemeral one.
    init(port: UInt16 = YouTubeMusicBridgeProtocol.port) {
        self.port = port
    }

    /// Starts listening for extension connections.
    ///
    /// Safe to call repeatedly - the poll loop does, on every sweep - because
    /// an existing server short-circuits it. A bind failure is recorded as a
    /// status rather than thrown, since the caller is a poll with nowhere to
    /// report to, and the settings pane surfaces it instead.
    func start() {
        guard server == nil else { return }

        let server = YouTubeMusicWebSocketServer(
            port: port,
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
        } catch {
            status = .failed(error.localizedDescription)
        }
    }

    /// Stops the server and discards all connection state.
    ///
    /// Everything is cleared rather than kept for a restart, because a browser
    /// reconnecting is issued a new connection id and none of the old state
    /// would apply to it.
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

    /// The bridge's current state, for the settings pane.
    ///
    /// - Returns: The status.
    func connectionStatus() -> YouTubeMusicBridgeStatus {
        status
    }

    /// The port actually bound, which differs when 0 was requested.
    ///
    /// - Returns: The port, or `nil` when not listening.
    func listeningPort() -> UInt16? {
        server?.listeningPort
    }

    /// Version of the extension on the active connection.
    ///
    /// - Returns: The version, or `nil` when nothing is connected.
    func connectedExtensionVersion() -> String? {
        extensionVersion
    }

    /// Turns single-session enforcement on or off.
    ///
    /// Switching it on pauses the extra tabs immediately rather than waiting
    /// for the next one to start playing, since a user enabling it while three
    /// tabs play expects that to take effect now.
    ///
    /// - Parameter enabled: Whether only one tab may play at a time.
    func setAutomaticallyPausesOtherSessions(_ enabled: Bool) async {
        guard automaticallyPausesOtherSessions != enabled else { return }

        automaticallyPausesOtherSessions = enabled
        guard enabled,
              let retainedSession = preferredPlayingSession() else {
            return
        }
        await pausePlayingSessions(except: retainedSession)
    }

    /// Every YouTube Music tab across every connected browser.
    ///
    /// Two shapes are handled. Extensions that send a session list report all
    /// their tabs; older ones report only the tab they selected, and a single
    /// session is synthesised from their snapshot so they still appear.
    ///
    /// The sort puts the active session first and is otherwise fully
    /// deterministic - browser, then connection, then tab - so the settings
    /// list does not reshuffle on every refresh.
    ///
    /// - Parameter date: Instant to judge freshness against. Defaults to now.
    /// - Returns: Sessions, active first.
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

    /// Presents the selected tab as an ordinary player snapshot.
    ///
    /// This is what makes YouTube Music look like Spotify or Music to the rest
    /// of the app despite having no app of its own.
    ///
    /// A stale snapshot yields a running-but-stopped player rather than a
    /// track: the extension is connected, so YouTube Music is available, but
    /// its last report is too old to show as current.
    ///
    /// Position is projected forward from the observation, since the extension
    /// reports far less often than the progress bar updates. Artwork is only
    /// attached when the cached image belongs to this exact track, so a stale
    /// cover never appears over a new one.
    ///
    /// - Parameter date: Instant to evaluate. Defaults to now.
    /// - Returns: A snapshot, never an error.
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

    /// Sends a transport command to the selected tab.
    ///
    /// - Parameter command: Transport control to invoke.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/extensionNotConnected``,
    ///   ``YouTubeMusicBridgeProtocolError/commandRejected(_:)``, or
    ///   ``YouTubeMusicBridgeProtocolError/commandNotConfirmed``.
    func perform(_ command: PlaybackCommand) async throws {
        _ = try await send(.playback(command))
    }

    /// Sets the volume and waits for a snapshot confirming it.
    ///
    /// Waiting for the acknowledgement is not enough: it only says the command
    /// was received, and the caller needs the level the page settled on. So it
    /// polls for a snapshot newer than the one before the command whose volume
    /// is within one step of the target, tolerating YouTube Music's own
    /// rounding.
    ///
    /// - Parameter volume: Desired level from 0 to 100.
    /// - Returns: The level the page reports.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/commandNotConfirmed`` if no
    ///   matching snapshot arrives within about two seconds, plus the errors
    ///   sending can raise.
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

    /// Seeks and waits for a snapshot confirming the new position.
    ///
    /// Same reasoning as ``setVolume(_:)``, at a longer interval: a seek makes
    /// the page buffer, so its next snapshot takes noticeably longer to
    /// arrive.
    ///
    /// - Parameter position: Target position in seconds.
    /// - Returns: The position the page reports.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/commandNotConfirmed`` if no
    ///   matching snapshot arrives within about 1.2 seconds, plus the errors
    ///   sending can raise.
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

    /// Records a server status change, clearing state when disconnected.
    ///
    /// Losing the last connection invalidates everything: connection ids are
    /// not reused, so keeping snapshots would leave the panel showing a track
    /// from a browser that has gone.
    ///
    /// - Parameter status: New server status.
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

    /// Handles one message from a connection.
    ///
    /// Everything except `hello` requires the connection to have identified
    /// itself first, so a client that skipped the handshake cannot inject
    /// state. Anything that fails to decode or validate is dropped silently:
    /// the sender is a browser extension with no channel to report to, and a
    /// malformed message is not worth surfacing to the user.
    ///
    /// Snapshots and session lists each carry their own sequence number and
    /// older ones are discarded, since WebSocket ordering does not survive the
    /// hop through the extension's own async plumbing.
    ///
    /// - Parameters:
    ///   - data: Raw frame.
    ///   - connectionID: Connection it arrived on.
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

    /// Sends a command and waits for its acknowledgement.
    ///
    /// The sequence number is captured before sending so callers that need to
    /// confirm an effect can tell a snapshot from after the command apart from
    /// one already in flight.
    ///
    /// Acknowledgements are matched by command id and required to come from
    /// the connection the command was sent to, so a second browser cannot
    /// answer for the first. Six seconds is generous, but a page that is
    /// buffering can genuinely take that long.
    ///
    /// - Parameters:
    ///   - command: Command to send.
    ///   - targetConnectionID: Connection to send to. Defaults to `nil`,
    ///     meaning the active one.
    /// - Returns: The dispatch record, for confirming the effect.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/extensionNotConnected``,
    ///   ``YouTubeMusicBridgeProtocolError/commandRejected(_:)``, or
    ///   ``YouTubeMusicBridgeProtocolError/commandNotConfirmed``.
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

    /// Picks which playing tab should keep playing.
    ///
    /// Ranked by visibility, then selection, then recency: the tab the user is
    /// actually looking at is the best guess at what they meant to hear.
    ///
    /// - Parameter date: Instant to judge freshness against. Defaults to now.
    /// - Returns: The tab to retain, or `nil` when none is playing.
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

    /// Every tab currently playing, across all connections.
    ///
    /// Stale tabs are excluded: one that has stopped reporting may well have
    /// been closed, and pausing it would only waste a round trip.
    ///
    /// - Parameter date: Instant to judge freshness against. Defaults to now.
    /// - Returns: Addressable targets for the playing tabs.
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

    /// Pauses every playing tab except one.
    ///
    /// Sent concurrently, since these go to different tabs and possibly
    /// different browsers; doing them in sequence would leave the last one
    /// audible for as long as the earlier ones took.
    ///
    /// Failures are ignored: a tab that will not pause is usually one that has
    /// just been closed, and there is nothing useful to tell the user.
    ///
    /// - Parameter retainedSession: The tab allowed to keep playing.
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

    /// Chooses which browser connection to present as the player.
    ///
    /// Stale connections are excluded outright, then the rest are ranked by
    /// ``selectionPriority(for:)``.
    ///
    /// The tie-break is what keeps the panel steady: the current connection is
    /// kept whenever it still ties for the best priority, so two browsers both
    /// playing do not trade the panel back and forth as their snapshots
    /// arrive. Only when it drops behind does the most recently heard-from
    /// connection take over.
    ///
    /// - Parameter date: Instant to judge freshness against. Defaults to now.
    /// - Returns: The selected connection, or `nil` when none is fresh.
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

    /// Switches the active connection and resets what belonged to the old one.
    ///
    /// Artwork is cleared because it was cached for the previous connection's
    /// track and would otherwise appear over the new one's.
    ///
    /// - Parameter connectionID: Connection to activate.
    private func activateConnection(_ connectionID: UUID) {
        guard activeConnectionID != connectionID else { return }

        activeConnectionID = connectionID
        extensionVersion = connectionIdentities[connectionID]?.extensionVersion
        clearArtwork()
    }

    /// Cleans up after a browser disconnects.
    ///
    /// Commands still awaiting an acknowledgement from that connection are
    /// failed immediately rather than left to time out, since the reply can
    /// never arrive.
    ///
    /// If the active connection was the one that left, another is selected at
    /// once - so closing one of two browsers falls back to the other rather
    /// than blanking the panel until the next poll.
    ///
    /// - Parameter connectionID: Connection that closed.
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

    /// Downloads cover art for the selected track.
    ///
    /// Skipped when the track and URL are both unchanged, so a snapshot
    /// arriving several times a second does not re-download the same image.
    ///
    /// The response is validated the same way as Spotify artwork - status,
    /// size ceiling, decode check - because the URL came from a web page.
    ///
    /// The conditions are re-checked after the download because it is the one
    /// suspension point here long enough for everything to change: the track
    /// may have moved on, or another browser taken over. Without that, a slow
    /// download would land a stale cover on a track it does not belong to.
    ///
    /// - Parameters:
    ///   - snapshot: Snapshot naming the track and its artwork URL.
    ///   - connectionID: Connection it came from, re-checked afterwards.
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

    /// Discards the cached cover art.
    private func clearArtwork() {
        artworkTrackKey = nil
        artworkURL = nil
        artworkData = nil
    }

    /// Decides which timestamp a snapshot's position was measured at.
    ///
    /// The extension's own capture time is more accurate than arrival, since
    /// it excludes the hop through the browser and the socket. But it comes
    /// from another process's clock, so it is only trusted when it sits within
    /// a sane window before arrival - a skewed or malicious clock would
    /// otherwise throw every position estimate off.
    ///
    /// - Parameters:
    ///   - capturedAt: Time the extension reported, or `nil`.
    ///   - receivedAt: Time Reprise received the message.
    /// - Returns: The timestamp to anchor position estimates on.
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

    /// Ranks a connection by how much it deserves to be the active one.
    ///
    /// Playing with a track beats merely having a track, which beats nothing
    /// at all - so a browser sitting on a paused tab never displaces one
    /// actually playing music.
    ///
    /// - Parameter snapshot: Snapshot to rank.
    /// - Returns: 2, 1, or 0, higher being better.
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

    /// Finds a tab that has just started playing in this session list.
    ///
    /// Comparing against the previous list is what makes this a transition
    /// rather than a state: without it, every session message would re-pause
    /// the other tabs for as long as one kept playing.
    ///
    /// Selection outranks visibility here, the reverse of
    /// ``preferredPlayingSession(at:)``, because a tab starting on its own is
    /// most likely the one the extension already considers selected.
    ///
    /// - Parameters:
    ///   - payload: The new session list.
    ///   - previousPayload: The previous list, or `nil` if this is the first.
    ///   - connectionID: Connection the list came from.
    ///   - date: Instant to judge freshness against. Defaults to now.
    /// - Returns: The tab that just started, or `nil` when none did.
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

    /// Builds a stable identity for a tab across refreshes.
    ///
    /// Combines both parts because tab ids are only unique within a browser,
    /// so two browsers could otherwise collide in the settings list.
    ///
    /// - Parameters:
    ///   - connectionID: Connection the tab belongs to.
    ///   - tabID: Tab id, or `nil` before one is known.
    /// - Returns: The identifier.
    private nonisolated static func sessionIdentifier(
        connectionID: UUID,
        tabID: Int?
    ) -> String {
        "\(connectionID.uuidString):\(tabID.map(String.init) ?? "pending")"
    }
}

/// A snapshot with the two timestamps that matter for it.
///
/// Both are kept because they answer different questions: arrival decides
/// whether the connection is still alive, while observation is what position
/// estimates are measured from.
nonisolated private struct ConnectionSnapshot: Sendable {
    /// The validated snapshot.
    let payload: YouTubeMusicSnapshotPayload

    /// When Reprise received it, used for staleness.
    let receivedAt: Date

    /// When the position was measured, used for estimation.
    let observedAt: Date
}

/// The most recent tab list from one connection.
nonisolated private struct ConnectionSessionList: Sendable {
    /// The validated session list.
    let payload: YouTubeMusicSessionsPayload
}

/// Enough to address one tab and rank it against others.
nonisolated private struct YouTubeMusicSessionTarget: Sendable {
    /// Connection the tab belongs to.
    let connectionID: UUID

    /// Browser-assigned tab id.
    let tabID: Int

    /// Whether the extension considers this its selected tab.
    let isSelected: Bool

    /// Whether the tab is visible in its window.
    let isVisible: Bool

    /// When the tab last reported.
    let updatedAt: Date
}

/// Who is on the other end of a connection.
nonisolated private struct ConnectionIdentity: Sendable {
    /// Extension id from the handshake.
    let extensionID: String

    /// Extension version, for display.
    let extensionVersion: String

    /// Browser name, for display.
    let browserName: String
}

/// What a sent command needs to remember to confirm its effect.
nonisolated private struct CommandDispatch: Sendable {
    /// Connection the command went to.
    let connectionID: UUID

    /// Sequence number before sending, so a later snapshot can be recognised.
    let startingSequence: Int
}

/// Loopback WebSocket server the extension connects to.
///
/// Wraps `Network.framework`, which is callback- and queue-based rather than
/// async. All mutable state is confined to one serial queue, which is what
/// `@unchecked Sendable` is asserting; the actor above never touches these
/// fields directly, only through the callbacks.
nonisolated private final class YouTubeMusicWebSocketServer: @unchecked Sendable {
    private let queue = DispatchQueue(
        label: "dev.junx.Reprise.youtubeMusicBridge"
    )
    private let onMessage: @Sendable (UUID, Data) -> Void
    private let onStatusChange: @Sendable (YouTubeMusicBridgeStatus) -> Void
    private let onConnectionClosed: @Sendable (UUID) -> Void
    private let port: UInt16
    private var listener: NWListener?
    private var connections: [UUID: NWConnection] = [:]
    private var readyConnectionIDs: Set<UUID> = []

    /// Creates the server without starting it.
    ///
    /// - Parameters:
    ///   - port: Port to bind.
    ///   - onMessage: Called with each accepted text frame.
    ///   - onStatusChange: Called when the listener or connection set changes.
    ///   - onConnectionClosed: Called when a connection goes away.
    init(
        port: UInt16,
        onMessage: @escaping @Sendable (UUID, Data) -> Void,
        onStatusChange: @escaping @Sendable (YouTubeMusicBridgeStatus) -> Void,
        onConnectionClosed: @escaping @Sendable (UUID) -> Void
    ) {
        self.port = port
        self.onMessage = onMessage
        self.onStatusChange = onStatusChange
        self.onConnectionClosed = onConnectionClosed
    }

    /// The port actually bound.
    ///
    /// Reads through the queue because the listener is queue-confined. Only
    /// safe from outside that queue, which is the sole caller.
    ///
    /// - Returns: The port, or `nil` when not listening.
    var listeningPort: UInt16? {
        queue.sync {
            listener?.port?.rawValue
        }
    }

    /// Binds the port and begins accepting connections.
    ///
    /// Three settings define the security posture. `acceptLocalOnly` keeps the
    /// listener off every non-loopback interface, so nothing on the network
    /// can reach it. The message size cap is applied at the protocol layer, so
    /// an oversized frame is refused before it is buffered. And the client
    /// request handler runs
    /// ``YouTubeMusicBridgeProtocol/acceptsHandshake(subprotocols:headers:)``,
    /// rejecting the handshake outright rather than accepting and filtering
    /// later.
    ///
    /// `allowLocalEndpointReuse` lets a relaunched Reprise rebind immediately
    /// instead of waiting out the socket's lingering close.
    ///
    /// - Throws: ``YouTubeMusicWebSocketServerError/invalidPort`` for an
    ///   unusable port, or an `NWError` if the listener cannot be created.
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
            rawValue: port
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

    /// Closes every connection and stops listening.
    ///
    /// Bridged to async through a continuation so the caller knows the port is
    /// released before it returns - important if the bridge is restarted, as
    /// the rebind would otherwise race the teardown.
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

    /// Sends a text frame to one connection.
    ///
    /// The connection must be in the ready set, not merely present: a socket
    /// that is still handshaking would accept the write and drop it.
    ///
    /// - Parameters:
    ///   - data: Frame body.
    ///   - connectionID: Connection to send to.
    /// - Throws: ``YouTubeMusicBridgeProtocolError/extensionNotConnected``
    ///   when the connection is gone or not ready, or an `NWError` if the
    ///   write fails.
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

    /// Maps listener state onto a bridge status.
    ///
    /// Ready reports waiting or connected depending on whether any browser has
    /// arrived, so the settings pane distinguishes "listening, nobody there"
    /// from "listening, connected".
    ///
    /// - Parameter state: New listener state.
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

    /// Registers an incoming connection and starts reading once it is ready.
    ///
    /// Reading only begins on `.ready`, since receiving before the handshake
    /// completes would deliver nothing.
    ///
    /// - Parameter connection: The new connection.
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

    /// Reads one frame and queues the next read.
    ///
    /// The recursive tail is how `Network.framework` models a continuous read;
    /// it is not unbounded recursion, since each call returns before the
    /// next is invoked from the completion handler.
    ///
    /// Only text frames are forwarded. The size is checked again here even
    /// though the protocol layer caps it, because that cap governs reassembly
    /// rather than what reaches this handler.
    ///
    /// - Parameters:
    ///   - connection: Connection to read from.
    ///   - connectionID: Its identifier.
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

    /// Forgets a connection and reports it closed, exactly once.
    ///
    /// The removal check is what makes it idempotent: failure, cancellation,
    /// and a close frame can all fire for the same connection, and the bridge
    /// must not be told about it more than once.
    ///
    /// - Parameter connectionID: Connection to remove.
    private func removeConnection(_ connectionID: UUID) {
        guard connections.removeValue(forKey: connectionID) != nil else {
            return
        }
        readyConnectionIDs.remove(connectionID)
        onConnectionClosed(connectionID)
    }
}

/// Failures from the bridge's WebSocket server.
nonisolated private enum YouTubeMusicWebSocketServerError: LocalizedError {
    /// The configured port is not a valid port number.
    case invalidPort

    /// Korean message for the settings pane.
    var errorDescription: String? {
        "YouTube Music 브리지 포트를 열 수 없습니다."
    }
}
