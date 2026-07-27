//
//  RepriseSettingsView.swift
//  Reprise
//

import AppKit
import SwiftUI

struct RepriseSettingsView: View {
    var body: some View {
        TabView {
            GeneralSettingsView()
                .tabItem {
                    Label("일반", systemImage: "gearshape")
                }

            MenuBarSettingsView()
                .tabItem {
                    Label("메뉴바", systemImage: "menubar.rectangle")
                }

            SystemInfoSettingsView()
                .tabItem {
                    Label("시스템 정보", systemImage: "info.square")
                }
        }
        .frame(width: 500, height: 410)
    }
}

private struct GeneralSettingsView: View {
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue

    var body: some View {
        Form {
            Section("테마") {
                Picker("플레이어 패널", selection: $playerPanelTheme) {
                    ForEach(PlayerPanelTheme.allCases) { theme in
                        Text(theme.displayName)
                            .tag(theme.rawValue)
                    }
                }
                .pickerStyle(.segmented)
            }
        }
        .formStyle(.grouped)
    }
}

private struct MenuBarSettingsView: View {
    @AppStorage(ReprisePreferenceKey.automaticallyScrollTitles)
    private var automaticallyScrollTitles = true

    @AppStorage(ReprisePreferenceKey.marqueeSpeed)
    private var marqueeSpeed = MarqueeSpeed.normal.rawValue

    @AppStorage(ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens)
    private var resetsMenuTitleWhenPanelOpens = true

    @AppStorage(ReprisePreferenceKey.menuBarArtworkStyle)
    private var menuBarArtworkStyle = MenuBarArtworkStyle.albumArtwork.rawValue

    private var artworkStyle: MenuBarArtworkStyle {
        MenuBarArtworkStyle(rawValue: menuBarArtworkStyle) ?? .albumArtwork
    }

    var body: some View {
        Form {
            Section("미리보기") {
                MenuBarArtworkPreview(style: artworkStyle)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
            }

            Section("현재 재생 중인 곡") {
                Picker("제목 왼쪽 표시", selection: $menuBarArtworkStyle) {
                    ForEach(MenuBarArtworkStyle.allCases) { style in
                        Text(style.displayName)
                            .tag(style.rawValue)
                    }
                }
                .pickerStyle(.segmented)
            }

            Section("캐러셀") {
                Toggle(
                    "긴 곡 제목 캐러셀 움직이기",
                    isOn: $automaticallyScrollTitles
                )

                Picker("캐러셀 속도", selection: $marqueeSpeed) {
                    ForEach(MarqueeSpeed.allCases) { speed in
                        Text(speed.displayName)
                            .tag(speed.rawValue)
                    }
                }
                .pickerStyle(.segmented)
                .disabled(!automaticallyScrollTitles)

                Toggle(
                    "패널을 열면 제목을 처음으로 되돌리기",
                    isOn: $resetsMenuTitleWhenPanelOpens
                )
                .disabled(!automaticallyScrollTitles)
            }
        }
        .formStyle(.grouped)
    }
}

private struct MenuBarArtworkPreview: View {
    let style: MenuBarArtworkStyle

    var body: some View {
        HStack(spacing: 5) {
            if style != .hidden {
                MenuBarLeadingArtworkPreview(style: style)
                    .frame(width: 18, height: 18)
            }

            Text("지금 재생 중인 곡")
                .font(.system(size: 13, weight: .medium))
                .lineLimit(1)
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 10)
        .frame(height: 30)
        .background {
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [
                            Color(red: 0.08, green: 0.42, blue: 0.65),
                            Color(red: 0.03, green: 0.30, blue: 0.52),
                        ],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
        .animation(.easeInOut(duration: 0.18), value: style)
    }
}

private struct MenuBarLeadingArtworkPreview: View {
    let style: MenuBarArtworkStyle

    var body: some View {
        switch style {
        case .albumArtwork:
            PreviewAlbumArtwork()
                .clipShape(RoundedRectangle(cornerRadius: 4))
        case .compactDisc:
            RotatingDiscPreview()
        case .levelIndicator:
            PlayingIndicatorPreview()
        case .hidden:
            EmptyView()
        }
    }
}

private struct PreviewAlbumArtwork: View {
    var body: some View {
        ZStack {
            AngularGradient(
                colors: [
                    Color(red: 0.94, green: 0.27, blue: 0.35),
                    Color(red: 0.98, green: 0.68, blue: 0.22),
                    Color(red: 0.22, green: 0.72, blue: 0.78),
                    Color(red: 0.42, green: 0.25, blue: 0.72),
                    Color(red: 0.94, green: 0.27, blue: 0.35),
                ],
                center: .center
            )

            Image(systemName: "music.note")
                .font(.system(size: 8, weight: .bold))
                .foregroundStyle(.white)
                .shadow(radius: 1)
        }
    }
}

private struct RotatingDiscPreview: View {
    @State private var rotation = 0.0

    var body: some View {
        PreviewAlbumArtwork()
            .clipShape(Circle())
            .overlay {
                Circle()
                    .fill(.black.opacity(0.72))
                    .frame(width: 5, height: 5)
                    .overlay {
                        Circle()
                            .fill(.white.opacity(0.9))
                            .frame(width: 1.5, height: 1.5)
                    }
            }
            .rotationEffect(.degrees(rotation))
            .onAppear {
                withAnimation(
                    .linear(duration: MenuBarMarquee.discRotationDuration)
                        .repeatForever(autoreverses: false)
                ) {
                    rotation = 360
                }
            }
    }
}

private struct PlayingIndicatorPreview: View {
    var body: some View {
        TimelineView(.animation) { timeline in
            let time = timeline.date.timeIntervalSinceReferenceDate

            HStack(alignment: .center, spacing: 2) {
                ForEach(0..<4, id: \.self) { index in
                    let phase = time * 7 + Double(index) * 1.35
                    let height = 6 + (sin(phase) + 1) * 4

                    Capsule()
                        .fill(.white)
                        .frame(width: 2, height: height)
                }
            }
            .frame(width: 18, height: 18)
        }
    }
}

private struct SystemInfoSettingsView: View {
    private var version: String {
        let shortVersion = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "-"
        let build = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleVersion"
        ) as? String ?? "-"

        return "\(shortVersion) (\(build))"
    }

    private var operatingSystem: String {
        ProcessInfo.processInfo.operatingSystemVersionString
    }

    var body: some View {
        Form {
            Section("앱") {
                LabeledContent("버전", value: version)
            }

            Section("시스템") {
                LabeledContent("운영체제", value: operatingSystem)
            }

            Section("연결된 디스플레이") {
                ForEach(
                    Array(NSScreen.screens.enumerated()),
                    id: \.offset
                ) { _, screen in
                    LabeledContent(screen.localizedName) {
                        Text(resolution(of: screen))
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func resolution(of screen: NSScreen) -> String {
        let width = Int(screen.frame.width * screen.backingScaleFactor)
        let height = Int(screen.frame.height * screen.backingScaleFactor)
        return "\(width) × \(height)"
    }
}

#Preview {
    RepriseSettingsView()
}
