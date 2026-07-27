//
//  PlayerPopoverView.swift
//  Reprise
//

import SwiftUI

struct PlayerPopoverView: View {
    @Environment(\.openSettings) private var openSettings
    @AppStorage(ReprisePreferenceKey.automaticallyScrollTitles)
    private var automaticallyScrollTitles = true
    @AppStorage(ReprisePreferenceKey.marqueeSpeed)
    private var marqueeSpeed = MarqueeSpeed.normal.rawValue
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue
    @Bindable var store: NowPlayingStore
    let onOpenSettings: () -> Void

    init(
        store: NowPlayingStore,
        onOpenSettings: @escaping () -> Void = {}
    ) {
        self.store = store
        self.onOpenSettings = onOpenSettings
    }

    private var snapshot: PlayerSnapshot {
        store.activeSnapshot
    }

    private var theme: PlayerPanelTheme {
        PlayerPanelTheme(rawValue: playerPanelTheme) ?? .liquid
    }

    private var preferredColorScheme: ColorScheme? {
        switch theme {
        case .white: .light
        case .black: .dark
        case .liquid, .system: nil
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            if let track = snapshot.track {
                playerCard(track)
                    .padding(14)
            } else {
                emptyPlayer
                    .padding(16)
            }

            if let message = store.commandError ?? snapshot.errorMessage {
                errorBanner(message)
                    .padding(.horizontal, 14)
                    .padding(.bottom, 14)
            }
        }
        .frame(width: 360)
        .background {
            panelBackground
        }
        .preferredColorScheme(preferredColorScheme)
        .task {
            await store.refresh()
        }
        .onReceive(
            NotificationCenter.default.publisher(
                for: .openRepriseSettings
            )
        ) { _ in
            presentSettings()
        }
    }

    @ViewBuilder
    private var panelBackground: some View {
        switch theme {
        case .white:
            Color.white
        case .black:
            Color.black
        case .liquid:
            Rectangle()
                .fill(.regularMaterial)
        case .system:
            Color(nsColor: .windowBackgroundColor)
        }
    }

    private func playerCard(_ track: Track) -> some View {
        HStack(spacing: 14) {
            ArtworkView(
                data: track.artworkData,
                size: 112,
                cornerRadius: 11,
                symbolName: snapshot.player.symbolName
            )

            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .top, spacing: 8) {
                    VStack(alignment: .leading, spacing: 2) {
                        PanelTitleMarqueeView(
                            title: track.title,
                            automaticallyScrolls: automaticallyScrollTitles,
                            pointsPerSecond: CGFloat(marqueeSpeed)
                        )
                            .frame(height: 17)
                            .accessibilityLabel("곡 \(track.title)")

                        if !track.artist.isEmpty {
                            Text(track.artist)
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)

                    settingsButton

                    PlayerLogoView(player: snapshot.player)
                }

                Spacer(minLength: 5)

                controls

                Spacer(minLength: 5)

                progress(track)
            }
            .frame(height: 112)
        }
    }

    private var emptyPlayer: some View {
        VStack(spacing: 18) {
            if let error = snapshot.errorMessage {
                ContentUnavailableView {
                    Label("플레이어에 접근할 수 없음", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(error)
                }
            } else if snapshot.isRunning {
                ContentUnavailableView {
                    Label("재생 중인 곡 없음", systemImage: "music.note")
                } description: {
                    Text("\(snapshot.player.displayName)에서 음악을 재생해 주세요.")
                }
            } else {
                ContentUnavailableView {
                    Label("\(snapshot.player.displayName)이 꺼져 있음", systemImage: "power")
                } description: {
                    Text("앱을 실행하고 음악을 재생하면 여기에 표시됩니다.")
                }
            }
        }
        .frame(minHeight: 150)
    }

    private var controls: some View {
        HStack(spacing: 28) {
            controlButton(
                command: .previous,
                symbol: "backward.fill",
                label: "이전 곡"
            )

            controlButton(
                command: .playPause,
                symbol: snapshot.state.isPlaying ? "pause.fill" : "play.fill",
                label: snapshot.state.isPlaying ? "일시정지" : "재생",
                prominent: true
            )

            controlButton(
                command: .next,
                symbol: "forward.fill",
                label: "다음 곡"
            )
        }
        .frame(maxWidth: .infinity)
        .disabled(!snapshot.isRunning || snapshot.errorMessage != nil)
    }

    private var settingsButton: some View {
        Button {
            presentSettings()
        } label: {
            Image(systemName: "gearshape")
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(.secondary)
                .frame(width: 19, height: 19)
        }
        .buttonStyle(.plain)
        .help("설정 열기")
        .accessibilityLabel("설정 열기")
        .accessibilityIdentifier("settingsButton")
    }

    private func presentSettings() {
        onOpenSettings()
        openSettings()
    }

    private func controlButton(
        command: PlaybackCommand,
        symbol: String,
        label: String,
        prominent: Bool = false
    ) -> some View {
        Button {
            Task {
                await store.perform(command)
            }
        } label: {
            Image(systemName: symbol)
                .font(.system(size: prominent ? 25 : 19, weight: .semibold))
                .frame(width: 36, height: 34)
        }
        .buttonStyle(CompactControlButtonStyle())
        .help(label)
        .accessibilityLabel(label)
        .accessibilityIdentifier(accessibilityIdentifier(for: command))
    }

    private func progress(_ track: Track) -> some View {
        VStack(spacing: 3) {
            ProgressView(value: track.progress)
                .progressViewStyle(.linear)
                .tint(.accentColor)
                .accessibilityLabel("재생 진행")
                .accessibilityValue("\(Int(track.progress * 100))퍼센트")

            HStack {
                Text(Self.timeString(track.position))
                Spacer()
                Text("-\(Self.timeString(track.remaining))")
            }
            .font(.caption2.monospacedDigit())
            .foregroundStyle(.secondary)
        }
    }

    private func errorBanner(_ message: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
            Text(message)
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
        .padding(10)
        .background(.orange.opacity(0.12), in: RoundedRectangle(cornerRadius: 9))
    }

    private func accessibilityIdentifier(for command: PlaybackCommand) -> String {
        switch command {
        case .previous: "previousButton"
        case .playPause: "playPauseButton"
        case .stop: "stopButton"
        case .next: "nextButton"
        }
    }

    nonisolated private static func timeString(_ time: TimeInterval) -> String {
        guard time.isFinite, time > 0 else { return "0:00" }
        let totalSeconds = Int(time.rounded(.down))
        return "\(totalSeconds / 60):\(String(format: "%02d", totalSeconds % 60))"
    }
}

private struct CompactControlButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(Color.primary.opacity(configuration.isPressed ? 0.55 : 0.72))
            .scaleEffect(configuration.isPressed ? 0.94 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}

private struct PlayerLogoView: View {
    let player: MediaPlayerKind

    @ViewBuilder
    var body: some View {
        switch player {
        case .spotify:
            Image("SpotifyLogo")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .foregroundStyle(.primary)
                .frame(width: 21, height: 21)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Spotify 로고")
                .accessibilityIdentifier("playerLogo")

        case .appleMusic:
            Image(systemName: "music.note")
                .font(.system(size: 17, weight: .bold))
                .symbolRenderingMode(.monochrome)
                .foregroundStyle(.primary)
                .frame(width: 19, height: 19)
                .accessibilityLabel("Apple Music 로고")
                .accessibilityIdentifier("playerLogo")
        }
    }
}

#Preview {
    PlayerPopoverView(store: NowPlayingStore())
}

extension Notification.Name {
    static let openRepriseSettings = Notification.Name(
        "dev.junx.Reprise.openSettings"
    )
}
