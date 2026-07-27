//
//  PlayerPopoverView.swift
//  Reprise
//

import AppKit
import SwiftUI

struct PlayerPopoverView: View {
    @Environment(\.colorScheme) private var systemColorScheme
    @Environment(\.openSettings) private var openSettings
    @AppStorage(ReprisePreferenceKey.automaticallyScrollTitles)
    private var automaticallyScrollTitles = true
    @AppStorage(ReprisePreferenceKey.marqueeSpeed)
    private var marqueeSpeed = MarqueeSpeed.normal.rawValue
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue
    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue
    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue
    @State private var volume = 100.0
    @State private var showsVolumeSlider = false
    @State private var volumeUpdateTask: Task<Void, Never>?
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

    private var leadingTimeStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    private var trailingTimeStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
    }

    private var preferredColorScheme: ColorScheme? {
        switch theme {
        case .white: .light
        case .black: .dark
        case .liquid, .system: nil
        }
    }

    private var panelTitleColor: NSColor {
        switch theme {
        case .white:
            .black
        case .black:
            .white
        case .liquid, .system:
            systemColorScheme == .dark ? .white : .black
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

            footer
        }
        .frame(width: 360)
        .background {
            panelBackground
        }
        .preferredColorScheme(preferredColorScheme)
        .task {
            await store.refresh()
            syncVolume()
        }
        .onChange(of: snapshot.volume) {
            syncVolume()
        }
        .onChange(of: snapshot.player) {
            showsVolumeSlider = false
            syncVolume()
        }
        .onDisappear {
            volumeUpdateTask?.cancel()
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
                            pointsPerSecond: CGFloat(marqueeSpeed),
                            foregroundColor: panelTitleColor
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

    private var footer: some View {
        HStack(spacing: 8) {
            Text(appVersionText)
                .font(.caption2)
                .foregroundStyle(.secondary)

            Spacer(minLength: 8)

            volumeButton

            settingsButton
        }
        .padding(.horizontal, 14)
        .frame(height: 30)
        .overlay(alignment: .top) {
            Rectangle()
                .fill(.primary.opacity(0.14))
                .frame(height: 0.5)
        }
        .zIndex(3)
    }

    private var appVersionText: String {
        let version = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "-"
        let build = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleVersion"
        ) as? String ?? "-"

        return "Reprise v\(version) (\(build))"
    }

    private var volumeButton: some View {
        Button {
            withAnimation(.easeOut(duration: 0.14)) {
                showsVolumeSlider.toggle()
            }
        } label: {
            Image(systemName: volumeSymbol)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(.secondary)
                .frame(width: 19, height: 19)
        }
        .buttonStyle(.plain)
        .disabled(snapshot.volume == nil || !snapshot.isRunning)
        .help("\(snapshot.player.displayName) 음량 \(Int(volume.rounded()))%")
        .accessibilityLabel("\(snapshot.player.displayName) 음량")
        .accessibilityValue("\(Int(volume.rounded()))퍼센트")
        .accessibilityIdentifier("volumeButton")
        .overlay(alignment: .topTrailing) {
            if showsVolumeSlider {
                volumeSlider
                    .offset(x: 27, y: -42)
                    .transition(
                        .opacity.combined(
                            with: .scale(
                                scale: 0.94,
                                anchor: .topTrailing
                            )
                        )
                    )
            }
        }
        .zIndex(showsVolumeSlider ? 2 : 0)
    }

    private var volumeSlider: some View {
        HStack(spacing: 8) {
            Image(systemName: volumeSymbol)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
                .frame(width: 13)

            Slider(
                value: Binding(
                    get: { volume },
                    set: { newValue in
                        volume = newValue
                        scheduleVolumeUpdate()
                    }
                ),
                in: 0...100,
                step: 1
            ) { isEditing in
                if !isEditing {
                    scheduleVolumeUpdate(immediately: true)
                }
            }
            .controlSize(.small)
            .accessibilityLabel("\(snapshot.player.displayName) 음량")
            .accessibilityValue("\(Int(volume.rounded()))퍼센트")

            Text("\(Int(volume.rounded()))")
                .font(.caption2.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(width: 23, alignment: .trailing)
        }
        .padding(.horizontal, 10)
        .frame(width: 160, height: 36)
        .background(
            .regularMaterial,
            in: RoundedRectangle(cornerRadius: 10, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .stroke(.primary.opacity(0.14), lineWidth: 0.5)
        }
        .shadow(color: .black.opacity(0.22), radius: 7, y: 3)
    }

    private var volumeSymbol: String {
        switch volume {
        case ...0:
            "speaker.slash.fill"
        case ..<34:
            "speaker.wave.1.fill"
        case ..<67:
            "speaker.wave.2.fill"
        default:
            "speaker.wave.3.fill"
        }
    }

    private func syncVolume() {
        guard volumeUpdateTask == nil,
              let snapshotVolume = snapshot.volume else {
            return
        }
        volume = Double(snapshotVolume)
    }

    private func scheduleVolumeUpdate(immediately: Bool = false) {
        volumeUpdateTask?.cancel()
        let level = PlayerVolume.clamped(Int(volume.rounded()))
        let player = snapshot.player

        volumeUpdateTask = Task {
            if !immediately {
                try? await Task.sleep(for: .milliseconds(80))
            }
            guard !Task.isCancelled else { return }
            await store.setVolume(level, for: player)
            guard !Task.isCancelled else { return }
            volumeUpdateTask = nil
            if let actualVolume = store.snapshot(for: player).volume {
                volume = Double(actualVolume)
            }
        }
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
                Text(
                    PanelTimeDisplay.leadingText(
                        style: leadingTimeStyle,
                        position: track.position
                    )
                )
                Spacer()
                Text(
                    PanelTimeDisplay.trailingText(
                        style: trailingTimeStyle,
                        duration: track.duration,
                        remaining: track.remaining
                    )
                )
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
