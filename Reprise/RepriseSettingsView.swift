//
//  RepriseSettingsView.swift
//  Reprise
//

import AppKit
import SwiftUI

struct RepriseSettingsView: View {
    var body: some View {
        TabView {
            ThemeSettingsView()
                .tabItem {
                    Label("테마", systemImage: "paintpalette")
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

private struct ThemeSettingsView: View {
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue

    private var theme: PlayerPanelTheme {
        PlayerPanelTheme(rawValue: playerPanelTheme) ?? .liquid
    }

    var body: some View {
        Form {
            Section("미리보기") {
                ThemePanelPreview(theme: theme)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
            }

            Section("플레이어 패널") {
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

private struct ThemePanelPreview: View {
    @Environment(\.colorScheme) private var systemColorScheme

    let theme: PlayerPanelTheme

    private var previewColorScheme: ColorScheme {
        switch theme {
        case .white: .light
        case .black: .dark
        case .liquid, .system: systemColorScheme
        }
    }

    private var titleColor: Color {
        switch theme {
        case .white: .black
        case .black: .white
        case .liquid, .system:
            systemColorScheme == .dark ? .white : .black
        }
    }

    var body: some View {
        HStack(spacing: 14) {
            PreviewAlbumArtwork(symbolSize: 28)
                .frame(width: 112, height: 112)
                .clipShape(RoundedRectangle(cornerRadius: 11))

            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .top, spacing: 8) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("음악 제목 미리보기")
                            .font(.headline)
                            .foregroundStyle(titleColor)
                            .lineLimit(1)

                        Text("아티스트")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }

                    Spacer(minLength: 0)

                    Image(systemName: "music.note")
                        .font(.system(size: 17, weight: .bold))
                        .foregroundStyle(.primary)
                }

                Spacer(minLength: 5)

                HStack(spacing: 28) {
                    Image(systemName: "backward.fill")
                    Image(systemName: "pause.fill")
                        .font(.system(size: 24, weight: .semibold))
                    Image(systemName: "forward.fill")
                }
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(.primary.opacity(0.72))
                .frame(maxWidth: .infinity)

                Spacer(minLength: 5)

                VStack(spacing: 3) {
                    ProgressView(value: 0.38)
                        .progressViewStyle(.linear)
                        .tint(.accentColor)

                    HStack {
                        Text("1:09")
                        Spacer()
                        Text("-2:04")
                    }
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.secondary)
                }
            }
            .frame(height: 112)
        }
        .padding(14)
        .frame(width: 360, height: 140)
        .background {
            themeBackground
        }
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(Color(nsColor: .separatorColor), lineWidth: 1)
        }
        .shadow(color: .black.opacity(0.16), radius: 6, y: 2)
        .environment(\.colorScheme, previewColorScheme)
        .animation(.easeInOut(duration: 0.2), value: theme)
    }

    @ViewBuilder
    private var themeBackground: some View {
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

    @AppStorage(ReprisePreferenceKey.menuBarTitleFormat)
    private var menuBarTitleFormat = MenuBarTitleFormat.titleOnly.rawValue

    private var artworkStyle: MenuBarArtworkStyle {
        MenuBarArtworkStyle(rawValue: menuBarArtworkStyle) ?? .albumArtwork
    }

    private var titleFormat: MenuBarTitleFormat {
        MenuBarTitleFormat(rawValue: menuBarTitleFormat) ?? .titleOnly
    }

    private var carouselSpeed: CGFloat {
        CGFloat(marqueeSpeed)
    }

    var body: some View {
        Form {
            Section("미리보기") {
                MenuBarArtworkPreview(
                    style: artworkStyle,
                    titleFormat: titleFormat,
                    automaticallyScrolls: automaticallyScrollTitles,
                    pointsPerSecond: carouselSpeed
                )
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
            }

            Section("현재 재생 중인 곡") {
                Picker("텍스트 내용", selection: $menuBarTitleFormat) {
                    ForEach(MenuBarTitleFormat.allCases) { format in
                        Text(format.displayName)
                            .tag(format.rawValue)
                    }
                }
                .pickerStyle(.segmented)

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
    let titleFormat: MenuBarTitleFormat
    let automaticallyScrolls: Bool
    let pointsPerSecond: CGFloat

    private var previewTitle: String {
        titleFormat.text(
            title: "여기에 재생 중인 음악 제목이 표시됩니다",
            artist: "미리보기 아티스트"
        )
    }

    var body: some View {
        HStack(spacing: 5) {
            if style != .hidden {
                MenuBarLeadingArtworkPreview(style: style)
                    .frame(width: 18, height: 18)
            }

            PanelTitleMarqueeView(
                title: previewTitle,
                automaticallyScrolls: automaticallyScrolls,
                pointsPerSecond: pointsPerSecond,
                foregroundColor: .white
            )
            .frame(
                width: MenuBarMarquee.maximumTextWidth,
                height: 30
            )
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
    var symbolSize: CGFloat = 8

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
                .font(.system(size: symbolSize, weight: .bold))
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
