//
//  PlayerPopoverView.swift
//  Reprise
//

import SwiftUI

struct PlayerPopoverView: View {
    @Bindable var store: NowPlayingStore

    private var snapshot: PlayerSnapshot {
        store.activeSnapshot
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
        .frame(width: 390)
        .background(.regularMaterial)
        .task {
            await store.refresh()
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
                        Text(track.title)
                            .font(.headline.weight(.semibold))
                            .lineLimit(1)
                            .accessibilityLabel("곡 \(track.title)")

                        if !track.artist.isEmpty {
                            Text(track.artist)
                                .font(.subheadline)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                        }
                    }

                    Spacer(minLength: 4)

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
                .resizable()
                .scaledToFit()
                .frame(width: 21, height: 21)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Spotify 로고")
                .accessibilityIdentifier("playerLogo")

        case .appleMusic:
            Image(systemName: "music.note")
                .font(.system(size: 17, weight: .bold))
                .symbolRenderingMode(.monochrome)
                .foregroundStyle(.white)
                .frame(width: 19, height: 19)
                .accessibilityLabel("Apple Music 로고")
                .accessibilityIdentifier("playerLogo")
        }
    }
}

#Preview {
    PlayerPopoverView(store: NowPlayingStore())
}
