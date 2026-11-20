import AppKit
import Foundation
import Observation
import Security
import SwiftUI

/// An opt-in collector. Google identity and interval accounting are owned by the server.
@MainActor @Observable
final class RecordShopConnection {
    /// One connection shared by the real player store and settings window.
    static let shared = RecordShopConnection()
    private(set) var status = "연결되지 않음"
    private(set) var pairingCode: String?
    private(set) var isConnecting = false
    private(set) var isConnected = false
    private let origin: URL
    private let session: URLSession
    private var credentials: Credentials?
    private var pairingTask: Task<Void, Never>?
    private var collectionTask: Task<Void, Never>?
    private var observation: [String: Any]?
    private var artwork: Data?
    private var observedAt: TimeInterval = 0
    private var lease: Lease?
    private var pending: Data?
    private var pendingArtwork: Data?
    private var uploaded: Set<String> = []

    /// Restores a Keychain credential; no listening information is sent before pairing.
    private init() {
        let configured = ProcessInfo.processInfo.environment["REPRISE_RECORD_SHOP_URL"] ?? "https://reprise.mx"
        let candidate = URL(string: configured)
        let isLoopback = ["localhost", "127.0.0.1", "::1"].contains(candidate?.host ?? "")
        origin = candidate?.scheme == "https" || (candidate?.scheme == "http" && isLoopback)
            ? candidate! : URL(string: "https://reprise.mx")!
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15
        session = URLSession(configuration: config)
        if let data = Self.readSecret(service: origin.absoluteString), let value = try? JSONDecoder().decode(Credentials.self, from: data) {
            credentials = value
            isConnected = true
            status = "연결됨 · 다음 재생을 기다리는 중"
        }
    }

    /// Records only the real polling path; demo snapshots never call this method.
    func observe(_ snapshot: PlayerSnapshot?) {
        defer { observedAt = ProcessInfo.processInfo.systemUptime; startCollection() }
        guard let snapshot, let track = snapshot.track, !track.title.isEmpty, !track.artist.isEmpty else {
            observation = nil; artwork = nil; return
        }
        let provider: String
        switch snapshot.player {
        case .spotify: provider = "spotify"
        case .appleMusic: provider = "apple-music"
        case .youtubeMusic: provider = "youtube-music"
        }
        observation = ["title": track.title, "artist": track.artist, "album": track.album,
            "playing": snapshot.state.isPlaying, "provider": provider,
            "evidence": provider == "spotify" ? "remote_state" : "app_state"]
        if track.duration.isFinite && track.duration > 0 { observation?["duration_seconds"] = track.duration }
        if track.position.isFinite && track.position >= 0 { observation?["position_seconds"] = track.position }
        artwork = track.artworkData
    }

    /// Opens a browser for Google sign-in and explicit public-chart consent.
    func connect() {
        guard !isConnecting, !isConnected else { return }
        isConnecting = true
        pairingTask = Task { [weak self] in
            guard let self else { return }
            defer { isConnecting = false; pairingCode = nil; pairingTask = nil }
            do {
                let grant: Grant = try await request("/api/device/start", body: ["name": "Reprise · \(Host.current().localizedName ?? "Mac")"])
                guard let url = URL(string: grant.verificationUri), url.host == origin.host,
                    url.port == origin.port, url.scheme == origin.scheme, url.path == "/connect" else { throw ConnectionError.invalidResponse }
                pairingCode = grant.userCode
                status = "브라우저에서 Google 로그인 후 코드 \(grant.userCode)를 확인하세요."
                NSWorkspace.shared.open(url)
                let expires = ProcessInfo.processInfo.systemUptime + 600
                while ProcessInfo.processInfo.systemUptime < expires {
                    try await Task.sleep(for: .seconds(max(5, min(30, grant.interval))))
                    do {
                        let token: Token = try await request("/api/device/token", body: ["device_code": grant.deviceCode])
                        let saved = Credentials(token: token.accessToken, deviceId: token.deviceId)
                        try Self.writeSecret(try JSONEncoder().encode(saved), service: origin.absoluteString)
                        credentials = saved; isConnected = true; lease = nil; pending = nil
                        status = "연결됨 · 재생 기록 전송에 동의했습니다."
                        startCollection()
                        return
                    } catch ConnectionError.http(202) { continue }
                    catch ConnectionError.http(429) { continue }
                }
                status = "연결 코드가 만료되었습니다. 다시 연결해 주세요."
            } catch is CancellationError { status = "연결이 취소되었습니다." }
            catch { status = "연결하지 못했습니다. 서버와 Google 로그인 설정을 확인하세요." }
        }
    }

    /// Cancels only the current pairing attempt.
    func cancel() { pairingTask?.cancel() }

    /// Stops local reporting immediately, then revokes this installation on the server.
    func disconnect() async {
        pairingTask?.cancel()
        let previous = credentials
        credentials = nil; isConnected = false; lease = nil; pending = nil
        collectionTask?.cancel(); collectionTask = nil
        Self.deleteSecret(service: origin.absoluteString)
        status = "연결 해제됨"
        guard let previous else { return }
        do {
            var request = URLRequest(url: URL(string: "/api/devices/\(previous.deviceId)", relativeTo: origin)!)
            request.httpMethod = "DELETE"
            request.setValue("Bearer \(previous.token)", forHTTPHeaderField: "Authorization")
            let (_, response) = try await session.data(for: request)
            if let response = response as? HTTPURLResponse, ![204,401].contains(response.statusCode) { throw ConnectionError.http(response.statusCode) }
        } catch { status = "로컬 연결 해제됨 · 웹 프로필에서 서버 연결도 해제해 주세요." }
    }

    /// Runs one ten-second reporting loop, independently of the popover's visibility.
    private func startCollection() {
        guard isConnected, collectionTask == nil else { return }
        collectionTask = Task { [weak self] in
            while !Task.isCancelled {
                do {
                    try await Task.sleep(for: .seconds(10))
                    try await self?.send()
                } catch is CancellationError { break }
                catch { self?.status = "연결 재시도 중 · 확인되지 않은 구간은 집계하지 않습니다." }
            }
        }
    }

    /// Retries identical pending requests; it never uploads a client-computed listening duration.
    private func send() async throws {
        guard let credentials else { return }
        if pending == nil {
            var body: [String: Any] = ["observation": ProcessInfo.processInfo.systemUptime - observedAt < 5 ? observation as Any? ?? NSNull() : NSNull()]
            if let lease { body["epoch"] = lease.epoch; body["nonce"] = lease.nonce; body["sequence"] = lease.nextSequence }
            pending = try JSONSerialization.data(withJSONObject: body)
            pendingArtwork = artwork
        }
        var request = URLRequest(url: URL(string: "/api/collector", relativeTo: origin)!)
        request.httpMethod = "POST"; request.httpBody = pending
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(credentials.token)", forHTTPHeaderField: "Authorization")
        let (data, rawResponse) = try await session.data(for: request)
        guard self.credentials?.token == credentials.token else { return }
        guard let response = rawResponse as? HTTPURLResponse else { throw ConnectionError.invalidResponse }
        if response.statusCode == 401 {
            self.credentials = nil; isConnected = false; lease = nil; pending = nil
            Self.deleteSecret(service: origin.absoluteString)
            status = "서버에서 연결이 해제되었습니다."
            return
        }
        if [409,429].contains(response.statusCode) { lease = nil; pending = nil; return }
        guard [200,202].contains(response.statusCode) else { throw ConnectionError.http(response.statusCode) }
        let result = try Self.decoder.decode(Lease.self, from: data)
        pending = nil; lease = result.role == "owner" ? result : nil
        status = result.role == "observer" ? "연결됨 · 다른 기기에서 집계 중"
            : (result.creditedSeconds ?? 0) > 0 ? "연결됨 · 서버에서 청취 시간 집계 중" : "연결됨 · 재생 상태 확인 중"
        guard (result.creditedSeconds ?? 0) > 0, let trackId = result.trackId, !uploaded.contains(trackId),
            let artwork = pendingArtwork, artwork.count <= 2 * 1024 * 1024 else { return }
        var upload = URLRequest(url: URL(string: "/api/artwork/\(trackId)", relativeTo: origin)!)
        upload.httpMethod = "PUT"; upload.httpBody = artwork
        upload.setValue("Bearer \(credentials.token)", forHTTPHeaderField: "Authorization")
        let (_, uploadedResponse) = try await session.data(for: upload)
        if (uploadedResponse as? HTTPURLResponse)?.statusCode == 200 { uploaded.insert(trackId) }
    }

    /// Sends a bounded JSON request and refuses pending status as a completed authorization.
    private func request<T: Decodable>(_ path: String, body: [String: Any]) async throws -> T {
        var request = URLRequest(url: URL(string: path, relativeTo: origin)!)
        request.httpMethod = "POST"; request.httpBody = try JSONSerialization.data(withJSONObject: body)
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let (data, response) = try await session.data(for: request)
        guard let response = response as? HTTPURLResponse, response.statusCode == 200 else {
            throw ConnectionError.http((response as? HTTPURLResponse)?.statusCode ?? 503)
        }
        return try Self.decoder.decode(T.self, from: data)
    }

    /// Reads only this service's generic-password entry from the current user's Keychain.
    private static func readSecret(service: String) -> Data? {
        let query: [String: Any] = [kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"dev.junx.Reprise.record-shop",kSecAttrAccount as String:service,kSecReturnData as String:true]
        var result: CFTypeRef?
        return SecItemCopyMatching(query as CFDictionary,&result) == errSecSuccess ? result as? Data : nil
    }

    /// Stores bearer credentials in Keychain, never in UserDefaults or logs.
    private static func writeSecret(_ data: Data, service: String) throws {
        deleteSecret(service: service)
        let query: [String: Any] = [kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"dev.junx.Reprise.record-shop",kSecAttrAccount as String:service,kSecValueData as String:data,kSecAttrAccessible as String:kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        guard SecItemAdd(query as CFDictionary,nil) == errSecSuccess else { throw ConnectionError.keychain }
    }

    /// Removes the installation credential when the user disconnects or the server revokes it.
    private static func deleteSecret(service: String) {
        SecItemDelete([kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:"dev.junx.Reprise.record-shop",kSecAttrAccount as String:service] as CFDictionary)
    }

    /// Decodes the Worker's snake-case protocol names.
    private static var decoder: JSONDecoder { let value = JSONDecoder(); value.keyDecodingStrategy = .convertFromSnakeCase; return value }
    /// Opaque installation credential stored only in Keychain.
    private struct Credentials: Codable { let token: String; let deviceId: String }
    /// Expiring browser authorization challenge.
    private struct Grant: Decodable { let deviceCode: String; let userCode: String; let verificationUri: String; let interval: Int }
    /// One-time token exchange result.
    private struct Token: Decodable { let accessToken: String; let deviceId: String }
    /// Server-owned sequence and nonce, excluding any client-side duration calculation.
    private struct Lease: Decodable { let role: String; let epoch: String?; let nonce: String?; let nextSequence: Int?; let creditedSeconds: Double?; let trackId: String? }
    /// Expected transport and local credential-store failures.
    private enum ConnectionError: Error { case http(Int), invalidResponse, keychain }
}

/// Explicit opt-in controls embedded in the existing General settings form.
struct RecordShopSettingsSection: View {
    private var connection: RecordShopConnection { .shared }
    /// Shares metadata only after browser sign-in and consent, and offers immediate disconnection.
    var body: some View {
        Section {
            HStack {
                Text("청취 기록 공유")
                Spacer()
                if connection.isConnected {
                    Button("연결 해제") { Task { await connection.disconnect() } }
                } else if connection.isConnecting {
                    Button("연결 취소") { connection.cancel() }
                } else {
                    Button("Google 계정 연결") { connection.connect() }
                }
            }
            Text(connection.status).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
        } header: { Text("Reprise.mx") }
        footer: { Text("연결 후 곡명·아티스트·앨범·재생 상태를 전송하며, 프로필과 청취 시간이 공개 차트에 표시됩니다.") }
    }
}
