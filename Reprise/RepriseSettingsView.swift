// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import SwiftUI

struct RepriseSettingsView: View {
    @Environment(\.controlActiveState)
    private var controlActiveState

    private var controlTint: Color {
        controlActiveState == .inactive
            ? Color(nsColor: .tertiaryLabelColor)
            : .accentColor
    }

    var body: some View {
        TabView {
            GeneralSettingsView()
                .tabItem {
                    Label("일반", systemImage: "gearshape")
                }

            YouTubeMusicSettingsView()
                .tabItem {
                    Label {
                        Text("YouTube Music")
                    } icon: {
                        Image(systemName: "play.circle")
                            .font(.system(size: 18, weight: .regular))
                    }
                }

            ThemeSettingsView()
                .tabItem {
                    Label("테마", systemImage: "paintpalette")
                }

            MenuBarSettingsView()
                .tabItem {
                    Label("메뉴바", systemImage: "menubar.rectangle")
                }

            PanelSettingsView()
                .tabItem {
                    Label("패널", systemImage: "play.rectangle")
                }

            SystemInfoSettingsView()
                .tabItem {
                    Label("시스템 정보", systemImage: "info.square")
                }
        }
        .tint(controlTint)
        .frame(width: 500, height: 480)
    }
}

private struct GeneralSettingsView: View {
    @StateObject private var launchAtLoginController =
        LaunchAtLoginController()
    @AppStorage(ReprisePreferenceKey.automaticallyPausesOtherPlayer)
    private var automaticallyPausesOtherPlayer = false
    @AppStorage(ReprisePreferenceKey.menuBarShowsLyrics)
    private var menuBarShowsLyrics = false
    @AppStorage(ReprisePreferenceKey.menuBarLyricsWidth)
    private var menuBarLyricsWidth = MenuBarLyricsWidth.defaultValue
    @AppStorage(ReprisePreferenceKey.menuBarReservesLyricsWidth)
    private var menuBarReservesLyricsWidth = true
    @AppStorage(ReprisePreferenceKey.playerDisplayPriority)
    private var playerDisplayOrder =
        ReprisePreferences.defaultPlayerDisplayOrder
    @AppStorage(ReprisePreferenceKey.remembersLastPlayedPlayer)
    private var remembersLastPlayedPlayer = false
    @State private var draggedPlayer: MediaPlayerKind?
    @State private var dragStartIndex: Int?
    @State private var dragOffset = CGFloat.zero

    private var orderedPlayers: [MediaPlayerKind] {
        ReprisePreferences.playerDisplayOrder(
            from: playerDisplayOrder
        )
    }

    var body: some View {
        Form {
            Section {
                Toggle(
                    "다른 플레이어 자동 정지",
                    isOn: $automaticallyPausesOtherPlayer
                )
            } header: {
                Text("재생")
            } footer: {
                Text(
                    "한 플레이어가 재생을 시작하면 기존에 재생 중이던 다른 플레이어를 일시 정지합니다."
                )
            }

            Section {
                Toggle(
                    "가사 표시",
                    isOn: $menuBarShowsLyrics
                )

                LabeledContent("가사 영역 너비") {
                    HStack(spacing: 8) {
                        Slider(
                            value: $menuBarLyricsWidth,
                            in: MenuBarLyricsWidth.range,
                            step: MenuBarLyricsWidth.step
                        )
                        .frame(width: 160)

                        Text("\(Int(menuBarLyricsWidth.rounded()))pt")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                            .frame(width: 44, alignment: .trailing)
                    }
                }
                .disabled(!menuBarShowsLyrics)

                Toggle(
                    "가사 공간 확보",
                    isOn: $menuBarReservesLyricsWidth
                )
                .disabled(!menuBarShowsLyrics)
            } header: {
                Text("제어 목록")
            } footer: {
                Text(
                    "영역 너비는 가사가 차지할 최대 폭입니다. 공간 확보를 켜면 선택한 너비로 고정해 주변 항목이 움직이지 않게 합니다."
                )
            }

            Section {
                Toggle(
                    "마지막에 재생한 플레이어 기억",
                    isOn: $remembersLastPlayedPlayer
                )

                ForEach(Array(orderedPlayers.enumerated()), id: \.element) {
                    index,
                    player in
                    HStack(spacing: 10) {
                        Text("\(index + 1)")
                            .foregroundStyle(.secondary)
                            .monospacedDigit()
                            .frame(width: 16)

                        playerIcon(for: player)
                            .frame(width: 18, height: 18)

                        Text(player.displayName)

                        Spacer()

                        Image(systemName: "line.3.horizontal")
                            .foregroundStyle(.tertiary)
                            .accessibilityHidden(true)
                            .onContinuousHover { phase in
                                switch phase {
                                case .active:
                                    NSCursor.resizeUpDown.set()
                                case .ended:
                                    NSCursor.arrow.set()
                                }
                            }
                    }
                    .contentShape(Rectangle())
                    .offset(
                        y: draggedPlayer == player
                            ? dragOffset
                            : 0
                    )
                    .zIndex(draggedPlayer == player ? 1 : 0)
                    .gesture(
                        DragGesture(
                            minimumDistance: 2,
                            coordinateSpace: .global
                        )
                        .onChanged { value in
                            updateDrag(
                                for: player,
                                translation: value.translation.height
                            )
                        }
                        .onEnded { _ in
                            endDrag()
                        }
                    )
                }
            } header: {
                Text("표시 우선순위")
            } footer: {
                Text(
                    "기억을 켜면 마지막으로 재생을 시작한 플레이어를 우선 표시합니다. 그 외에는 아래 순서를 사용하며, 항목을 드래그하여 변경할 수 있습니다."
                )
            }

            Section {
                Toggle(
                    "로그인 시 Reprise 자동 실행",
                    isOn: Binding(
                        get: { launchAtLoginController.state.isOn },
                        set: { launchAtLoginController.setEnabled($0) }
                    )
                )

                if launchAtLoginController.state == .requiresApproval {
                    Button("로그인 항목 설정 열기") {
                        launchAtLoginController.openSystemSettings()
                    }
                }

                if let errorMessage = launchAtLoginController.errorMessage {
                    Text(errorMessage)
                        .font(.caption)
                        .foregroundStyle(.red)
                }
            } header: {
                Text("자동 실행")
            } footer: {
                if launchAtLoginController.state == .requiresApproval {
                    Text(
                        "자동 실행을 사용하려면 시스템 설정의 로그인 항목에서 Reprise를 허용해 주세요."
                    )
                } else {
                    Text("Mac에 로그인하면 Reprise를 자동으로 실행합니다.")
                }
            }
        }
        .formStyle(.grouped)
        .onAppear {
            launchAtLoginController.refresh()
        }
        .onReceive(
            NotificationCenter.default.publisher(
                for: NSApplication.didBecomeActiveNotification
            )
        ) { _ in
            launchAtLoginController.refresh()
        }
    }

    @ViewBuilder
    private func playerIcon(
        for player: MediaPlayerKind
    ) -> some View {
        switch player {
        case .spotify:
            Image("SpotifyLogo")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
        case .appleMusic:
            Image(systemName: "music.note")
                .font(.system(size: 15, weight: .semibold))
        case .youtubeMusic:
            Image("YouTubeMusicLogo")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
        }
    }

    private func updateDrag(
        for player: MediaPlayerKind,
        translation: CGFloat
    ) {
        if draggedPlayer != player {
            draggedPlayer = player
            dragStartIndex = orderedPlayers.firstIndex(of: player)
        }

        guard let startIndex = dragStartIndex else {
            return
        }

        let maximumPosition =
            CGFloat(orderedPlayers.count - 1)
            * PlayerPriorityDragMetrics.rowStep
        let draggedPosition = min(
            max(
                CGFloat(startIndex)
                    * PlayerPriorityDragMetrics.rowStep
                    + translation,
                0
            ),
            maximumPosition
        )
        let targetIndex = Int(
            (
                draggedPosition
                    / PlayerPriorityDragMetrics.rowStep
            ).rounded()
        )

        if orderedPlayers.firstIndex(of: player) != targetIndex {
            withAnimation(
                .interactiveSpring(
                    response: 0.22,
                    dampingFraction: 0.86
                )
            ) {
                movePlayer(player, to: targetIndex)
            }
        }

        dragOffset =
            draggedPosition
            - CGFloat(targetIndex)
            * PlayerPriorityDragMetrics.rowStep
    }

    private func endDrag() {
        withAnimation(
            .spring(response: 0.22, dampingFraction: 0.86)
        ) {
            dragOffset = 0
            draggedPlayer = nil
            dragStartIndex = nil
        }
    }

    private func movePlayer(
        _ player: MediaPlayerKind,
        to targetIndex: Int
    ) {
        guard let sourceIndex = orderedPlayers.firstIndex(of: player),
              sourceIndex != targetIndex else {
            return
        }

        var reorderedPlayers = orderedPlayers
        reorderedPlayers.move(
            fromOffsets: IndexSet(integer: sourceIndex),
            toOffset: sourceIndex < targetIndex
                ? targetIndex + 1
                : targetIndex
        )
        playerDisplayOrder =
            ReprisePreferences.serializedPlayerDisplayOrder(
                reorderedPlayers
            )
    }
}

private struct YouTubeMusicSettingsView: View {
    @State private var sessions: [YouTubeMusicSession] = []

    var body: some View {
        Form {
            Section {
                if sessions.isEmpty {
                    HStack(spacing: 10) {
                        Image(systemName: "network.slash")
                            .foregroundStyle(.secondary)
                            .frame(width: 20, height: 20)

                        VStack(alignment: .leading, spacing: 2) {
                            Text("연결된 세션 없음")

                            Text(
                                "YouTube Music을 연 브라우저에서 확장 프로그램을 연결해 주세요."
                            )
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        }

                        Spacer(minLength: 0)
                    }
                    .padding(.vertical, 2)
                } else {
                    ForEach(sessions) { session in
                        sessionRow(
                            session,
                            displayName: sessionDisplayName(for: session)
                        )
                    }
                }
            } header: {
                Text("YouTube Music 세션")
            } footer: {
                Text(
                    "브라우저에서 열린 각 YouTube Music 탭을 표시합니다. 확장은 재생 상태와 현재 보이는 탭을 기준으로 제어할 세션을 자동 선택합니다."
                )
            }

            Section {
                Link(
                    "Chromium 확장 다운로드",
                    destination: URL(
                        string: "https://github.com/aodjo/Reprise/releases/latest/download/Reprise-YouTube-Music-Chromium.zip"
                    )!
                )

                Link(
                    "Firefox 확장 다운로드",
                    destination: URL(
                        string: "https://github.com/aodjo/Reprise/releases/latest/download/Reprise-YouTube-Music-Firefox.xpi"
                    )!
                )
            } header: {
                Text("브라우저 확장")
            } footer: {
                Text(
                    "Chromium에서는 압축을 푼 폴더를 불러오고, Firefox에서는 서명된 XPI를 설치하세요. YouTube Music 탭을 새로고침하면 자동으로 연결됩니다."
                )
            }
        }
        .formStyle(.grouped)
        .task {
            await YouTubeMusicBridge.shared.start()
            while !Task.isCancelled {
                sessions = await YouTubeMusicBridge.shared.sessions()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    @ViewBuilder
    private func sessionRow(
        _ session: YouTubeMusicSession,
        displayName: String
    ) -> some View {
        HStack(spacing: 10) {
            Image(systemName: browserSymbol(for: session.browser))
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(
                    session.isActive ? Color.accentColor : Color.secondary
                )
                .frame(width: 20, height: 20)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(displayName)
                        .fontWeight(.medium)

                    if session.isActive {
                        Text("사용 중")
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(Color.accentColor)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(
                                Color.accentColor.opacity(0.12),
                                in: Capsule()
                            )
                    } else if session.isStale {
                        Text("응답 대기")
                            .font(.caption2.weight(.medium))
                            .foregroundStyle(.secondary)
                    } else if session.isSelected {
                        Text("선택됨")
                            .font(.caption2.weight(.medium))
                            .foregroundStyle(.secondary)
                    }
                }

                Text(sessionDetailText(for: session))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer(minLength: 8)

            VStack(alignment: .trailing, spacing: 3) {
                Image(systemName: stateSymbol(for: session.state))
                    .foregroundStyle(
                        session.state == .playing
                            ? Color.green
                            : Color.secondary
                    )

                Text("v\(session.extensionVersion)")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.tertiary)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(
            "\(displayName) 세션, \(sessionDetailText(for: session))"
        )
    }

    private func sessionDisplayName(
        for session: YouTubeMusicSession
    ) -> String {
        let browserTabs = sessions
            .filter { $0.connectionID == session.connectionID }
            .sorted { ($0.tabID ?? .max) < ($1.tabID ?? .max) }
        guard browserTabs.count > 1,
              let index = browserTabs.firstIndex(where: {
                  $0.id == session.id
              }) else {
            return session.browserName
        }
        return "\(session.browserName) · 탭 \(index + 1)"
    }

    private func sessionDetailText(
        for session: YouTubeMusicSession
    ) -> String {
        guard !session.title.isEmpty else {
            return session.isFresh
                ? "재생 정보 없음"
                : "재생 정보 대기 중"
        }

        let track = session.artist.isEmpty
            ? session.title
            : "\(session.title) · \(session.artist)"
        switch session.state {
        case .playing:
            return track
        case .paused:
            return "일시 정지 · \(track)"
        case .stopped:
            return "정지 · \(track)"
        case .unavailable:
            return "사용할 수 없음 · \(track)"
        }
    }

    private func browserSymbol(
        for browser: YouTubeMusicBrowserKind
    ) -> String {
        switch browser {
        case .chromium:
            "globe"
        case .firefox:
            "flame.fill"
        }
    }

    private func stateSymbol(for state: PlaybackState) -> String {
        switch state {
        case .playing:
            "play.circle.fill"
        case .paused:
            "pause.circle.fill"
        case .stopped:
            "stop.circle"
        case .unavailable:
            "exclamationmark.triangle"
        }
    }
}

private enum PlayerPriorityDragMetrics {
    static let rowStep: CGFloat = 39
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
    @State private var previewPosition = 69.0

    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue

    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue

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

    private var leadingTimeStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    private var trailingTimeStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
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

                VStack(spacing: 1) {
                    SettingsPreviewSlider(
                        value: $previewPosition,
                        range: 0...193
                    )
                        .frame(maxWidth: .infinity)
                        .frame(height: 14)

                    HStack {
                        Text(
                            PanelTimeDisplay.leadingText(
                                style: leadingTimeStyle,
                                position: previewPosition
                            )
                        )
                        Spacer()
                        Text(
                            PanelTimeDisplay.trailingText(
                                style: trailingTimeStyle,
                                duration: 193,
                                remaining: max(193 - previewPosition, 0)
                            )
                        )
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
                .stroke(
                    Color.black.opacity(0.42),
                    lineWidth: 0.5
                )
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
            PlayerPanelPalette.darkBackground
        case .liquid:
            LiquidPanelBackground(cornerRadius: 16)
        case .system:
            Color(nsColor: .windowBackgroundColor)
        }
    }
}

private struct PanelSettingsView: View {
    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue

    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue

    private var leadingStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    private var trailingStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
    }

    var body: some View {
        Form {
            Section("미리보기") {
                PanelTimePreview(
                    leadingStyle: leadingStyle,
                    trailingStyle: trailingStyle
                )
                .frame(maxWidth: .infinity)
                .padding(.vertical, 10)
            }

            Section("시간 표시") {
                Picker("왼쪽", selection: $panelLeadingTimeStyle) {
                    ForEach(PanelLeadingTimeStyle.allCases) { style in
                        Text(style.displayName)
                            .tag(style.rawValue)
                    }
                }
                .pickerStyle(.segmented)

                Picker("오른쪽", selection: $panelTrailingTimeStyle) {
                    ForEach(PanelTrailingTimeStyle.allCases) { style in
                        Text(style.displayName)
                            .tag(style.rawValue)
                    }
                }
                .pickerStyle(.segmented)
            }
        }
        .formStyle(.grouped)
    }
}

private struct PanelTimePreview: View {
    @State private var previewPosition = 69.0

    let leadingStyle: PanelLeadingTimeStyle
    let trailingStyle: PanelTrailingTimeStyle

    var body: some View {
        VStack(spacing: 1) {
            SettingsPreviewSlider(
                value: $previewPosition,
                range: 0...193
            )
                .frame(maxWidth: .infinity)
                .frame(height: 14)

            HStack {
                Text(
                    PanelTimeDisplay.leadingText(
                        style: leadingStyle,
                        position: previewPosition
                    )
                )
                Spacer()
                Text(
                    PanelTimeDisplay.trailingText(
                        style: trailingStyle,
                        duration: 193,
                        remaining: max(193 - previewPosition, 0)
                    )
                )
            }
            .font(.caption2.monospacedDigit())
            .foregroundStyle(.secondary)
        }
        .frame(width: 276)
        .padding(12)
        .background(
            Color(nsColor: .controlBackgroundColor),
            in: RoundedRectangle(cornerRadius: 10)
        )
        .animation(.easeInOut(duration: 0.18), value: leadingStyle)
        .animation(.easeInOut(duration: 0.18), value: trailingStyle)
    }
}

private struct SettingsPreviewSlider: NSViewRepresentable {
    @Binding var value: Double
    let range: ClosedRange<Double>

    func makeCoordinator() -> Coordinator {
        Coordinator(value: $value)
    }

    func makeNSView(context: Context) -> NSSlider {
        let slider = NSSlider(
            value: value,
            minValue: range.lowerBound,
            maxValue: range.upperBound,
            target: context.coordinator,
            action: #selector(Coordinator.valueChanged(_:))
        )
        slider.isContinuous = true
        slider.controlSize = .small
        slider.trackFillColor = .controlAccentColor
        slider.setAccessibilityLabel("재생 위치 미리보기")
        return slider
    }

    func updateNSView(_ slider: NSSlider, context: Context) {
        context.coordinator.value = $value
        slider.minValue = range.lowerBound
        slider.maxValue = range.upperBound

        if abs(slider.doubleValue - value) > 0.001 {
            slider.doubleValue = value
        }
    }

    final class Coordinator: NSObject {
        var value: Binding<Double>

        init(value: Binding<Double>) {
            self.value = value
        }

        @objc
        func valueChanged(_ sender: NSSlider) {
            value.wrappedValue = sender.doubleValue
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

    @AppStorage(ReprisePreferenceKey.menuBarShowsLyrics)
    private var menuBarShowsLyrics = false

    @AppStorage(ReprisePreferenceKey.menuBarLyricsWidth)
    private var menuBarLyricsWidth = MenuBarLyricsWidth.defaultValue

    @AppStorage(ReprisePreferenceKey.menuBarReservesLyricsWidth)
    private var menuBarReservesLyricsWidth = true

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
                    showsLyrics: menuBarShowsLyrics,
                    lyricsWidth:
                        MenuBarLyricsWidth.clamped(menuBarLyricsWidth),
                    reservesLyricsWidth: menuBarReservesLyricsWidth,
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
            .disabled(titleFormat == .hidden)
        }
        .formStyle(.grouped)
        .onAppear {
            if artworkStyle == .hidden, titleFormat == .hidden {
                menuBarArtworkStyle = MenuBarArtworkStyle.albumArtwork.rawValue
            }
        }
        .onChange(of: menuBarTitleFormat) { newValue in
            guard newValue == MenuBarTitleFormat.hidden.rawValue,
                  artworkStyle == .hidden else {
                return
            }
            menuBarArtworkStyle = MenuBarArtworkStyle.albumArtwork.rawValue
        }
        .onChange(of: menuBarArtworkStyle) { newValue in
            guard newValue == MenuBarArtworkStyle.hidden.rawValue,
                  titleFormat == .hidden else {
                return
            }
            menuBarTitleFormat = MenuBarTitleFormat.titleOnly.rawValue
        }
    }
}

private struct MenuBarArtworkPreview: View {
    let style: MenuBarArtworkStyle
    let titleFormat: MenuBarTitleFormat
    let showsLyrics: Bool
    let lyricsWidth: CGFloat
    let reservesLyricsWidth: Bool
    let automaticallyScrolls: Bool
    let pointsPerSecond: CGFloat

    private var previewTitle: String {
        if showsLyrics, titleFormat != .hidden {
            return "다시 만나요"
        }
        return titleFormat.text(
            title: "여기에 재생 중인 음악 제목이 표시됩니다",
            artist: "미리보기 아티스트"
        )
    }

    private var maximumTextWidth: CGFloat {
        showsLyrics
            ? lyricsWidth
            : MenuBarMarquee.maximumTextWidth
    }

    var body: some View {
        HStack(spacing: 5) {
            if style != .hidden {
                MenuBarLeadingArtworkPreview(style: style)
                    .frame(width: 18, height: 18)
            }

            if titleFormat != .hidden {
                PanelTitleMarqueeView(
                    title: previewTitle,
                    automaticallyScrolls: automaticallyScrolls,
                    pointsPerSecond: pointsPerSecond,
                    foregroundColor: .white
                )
                .frame(
                    width: MenuBarMarquee.viewportWidth(
                        for: MenuBarMarquee.textWidth(previewTitle),
                        reservesMaximumTextWidth:
                            showsLyrics && reservesLyricsWidth,
                        maximumWidth: maximumTextWidth
                    ),
                    height: 30
                )
            }
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
        .animation(.easeInOut(duration: 0.18), value: titleFormat)
        .animation(
            .easeInOut(duration: 0.18),
            value: reservesLyricsWidth
        )
        .animation(.easeInOut(duration: 0.18), value: lyricsWidth)
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
                    rotation = -360
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
        VStack(spacing: 0) {
            Form {
                Section("앱") {
                    LabeledContent("버전", value: version)
                }

                Section {
                    UpdateSettingsView()
                } header: {
                    Text("업데이트")
                } footer: {
                    Text(
                        "자동 확인은 하루에 한 번 실행됩니다. 자동 다운로드를 끄면 새 버전이 있을 때 설치 알림만 표시합니다."
                    )
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

            HStack(spacing: 0) {
                Text("Made by ")
                Link(
                    "aodjo",
                    destination: URL(string: "https://junx.dev")!
                )
                Text(" with ❤️")
            }
            .font(.footnote)
            .foregroundStyle(.secondary)
            .padding(.bottom, 18)
        }
    }

    private func resolution(of screen: NSScreen) -> String {
        let width = Int(screen.frame.width * screen.backingScaleFactor)
        let height = Int(screen.frame.height * screen.backingScaleFactor)
        return "\(width) × \(height)"
    }
}

private struct UpdateSettingsView: View {
    @ObservedObject private var updateController: UpdateController
    @State private var automaticallyChecksForUpdates: Bool
    @State private var automaticallyDownloadsUpdates: Bool

    init() {
        let updateController = UpdateController.shared
        self.updateController = updateController
        _automaticallyChecksForUpdates = State(
            initialValue: updateController.automaticallyChecksForUpdates
        )
        _automaticallyDownloadsUpdates = State(
            initialValue: updateController.automaticallyDownloadsUpdates
        )
    }

    var body: some View {
        Toggle(
            "자동으로 업데이트 확인",
            isOn: $automaticallyChecksForUpdates
        )
        .onChange(of: automaticallyChecksForUpdates) { _, enabled in
            updateController.setAutomaticallyChecksForUpdates(enabled)
        }

        Toggle(
            "업데이트 자동 다운로드",
            isOn: $automaticallyDownloadsUpdates
        )
        .disabled(!automaticallyChecksForUpdates)
        .onChange(of: automaticallyDownloadsUpdates) { _, enabled in
            updateController.setAutomaticallyDownloadsUpdates(enabled)
        }

        Button("업데이트 확인…") {
            updateController.checkForUpdates()
        }
        .disabled(!updateController.canCheckForUpdates)
        .task {
            updateController.start()
        }
    }
}

#Preview {
    RepriseSettingsView()
}
