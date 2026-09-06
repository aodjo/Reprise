// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import SwiftUI

/// The Settings window, split into six tabs.
///
/// Every pane writes straight to `UserDefaults` through `@AppStorage`, so
/// there is no apply step and no settings model: a change reaches the panel
/// and the menu bar as soon as it is made.
struct RepriseSettingsView: View {
    @Environment(\.controlActiveState)
    private var controlActiveState

    /// Accent colour that dims when the window is not active.
    ///
    /// The tab strip is tinted manually, since a tinted `TabView` does not
    /// follow the window's active state on its own and would keep a
    /// full-strength accent in a background window.
    private var controlTint: Color {
        controlActiveState == .inactive
            ? Color(nsColor: .tertiaryLabelColor)
            : .accentColor
    }

    /// The tab strip and its panes.
    ///
    /// Fixed size because a settings window with movable panes would reflow
    /// its previews, which are laid out at the real dimensions of the menu bar
    /// item and the player panel.
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

/// Playback, lyrics, player priority, and launch at login.
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

    /// The user's player order, decoded from the stored string.
    private var orderedPlayers: [MediaPlayerKind] {
        ReprisePreferences.playerDisplayOrder(
            from: playerDisplayOrder
        )
    }

    /// The General pane.
    ///
    /// The login item state is re-read on appearance and whenever the app
    /// becomes active, because approval happens in System Settings with no
    /// callback: coming back to Reprise is the only cue that it may have
    /// changed.
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

    /// Small icon for a player in the priority list.
    ///
    /// Bundled logos are rendered as templates so they take the list's
    /// foreground colour; Music uses an SF Symbol, since Apple's mark is not
    /// redistributable.
    ///
    /// - Parameter player: Player to represent.
    /// - Returns: The icon view.
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

    /// Reorders the priority list as a row is dragged.
    ///
    /// Written by hand rather than using `List`'s move support, because the
    /// rows live in a `Form` section alongside a toggle - a `List` there would
    /// bring its own chrome and break the grouped layout.
    ///
    /// The drag translates a row's position into an index by dividing by the
    /// row height, and reorders as soon as that index changes, so the list
    /// rearranges under the pointer rather than only on release. The offset
    /// applied afterwards is the remainder: it keeps the dragged row glued to
    /// the pointer even though the rows beneath it have already moved.
    ///
    /// - Parameters:
    ///   - player: Player being dragged.
    ///   - translation: Vertical movement since the drag began.
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

    /// Settles the dragged row into place.
    ///
    /// The order has already been written during the drag, so this only
    /// animates the offset back to zero.
    private func endDrag() {
        withAnimation(
            .spring(response: 0.22, dampingFraction: 0.86)
        ) {
            dragOffset = 0
            draggedPlayer = nil
            dragStartIndex = nil
        }
    }

    /// Moves a player to a new position and stores the order.
    ///
    /// The offset adjustment is required by `move(fromOffsets:toOffset:)`,
    /// which inserts before the given offset: moving downwards needs one more
    /// to land after the target rather than before it.
    ///
    /// - Parameters:
    ///   - player: Player to move.
    ///   - targetIndex: Index to move it to.
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

/// Connected browser sessions and extension download links.
private struct YouTubeMusicSettingsView: View {
    @State private var sessions: [YouTubeMusicSession] = []

    /// The YouTube Music pane.
    ///
    /// Polls the bridge twice a second while visible. Polling rather than
    /// observing because the bridge is an actor with no change notification,
    /// and this list is only on screen while the user is looking at it.
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
                        string: "https://github.com/aodjo/Reprise/releases/latest/download/Reprise-YouTube-Music-Firefox.zip"
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

    /// One row describing a browser tab.
    ///
    /// The badge distinguishes three states the user would otherwise conflate:
    /// the tab Reprise is controlling, one the extension selected but that is
    /// not in use, and one that has stopped responding.
    ///
    /// Collapsed into a single accessibility element, since read out
    /// separately the icon, badge, and version are noise around the one fact
    /// that matters.
    ///
    /// - Parameters:
    ///   - session: Session to describe.
    ///   - displayName: Pre-computed name for the row.
    /// - Returns: The row view.
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

    /// Names a session, numbering tabs when a browser has several.
    ///
    /// The tab number is positional rather than the browser's own tab id,
    /// which is an arbitrary large integer that would mean nothing to a user.
    /// It is omitted entirely when a browser has only one tab.
    ///
    /// - Parameter session: Session to name.
    /// - Returns: The display name.
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

    /// Secondary line describing what a session is playing.
    ///
    /// A session with no title is distinguished by freshness: a fresh one has
    /// genuinely nothing loaded, while a stale one may simply not have
    /// reported yet.
    ///
    /// - Parameter session: Session to describe.
    /// - Returns: The detail text.
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

    /// SF Symbol standing in for a browser family.
    ///
    /// - Parameter browser: Browser to represent.
    /// - Returns: The symbol name.
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

    /// SF Symbol for a playback state.
    ///
    /// - Parameter state: State to represent.
    /// - Returns: The symbol name.
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

/// Geometry the priority drag converts movement into indices with.
private enum PlayerPriorityDragMetrics {
    /// Height of one row plus its spacing, in points.
    ///
    /// Measured from the rendered form rather than derived, since a grouped
    /// `Form` does not expose its row metrics. It must track the row layout:
    /// if the rows change height, dragging picks the wrong index.
    static let rowStep: CGFloat = 39
}

/// Panel theme picker with a live preview.
private struct ThemeSettingsView: View {
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue

    /// The currently selected theme.
    private var theme: PlayerPanelTheme {
        PlayerPanelTheme(rawValue: playerPanelTheme) ?? .liquid
    }

    /// The Theme pane.
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

/// Non-functional replica of the player panel, for previewing a theme.
///
/// Rebuilt rather than reusing ``PlayerPopoverView``, which would need a live
/// store and would send real commands from a preview. Kept at the panel's
/// actual dimensions so the preview is a true likeness.
private struct ThemePanelPreview: View {
    @Environment(\.colorScheme) private var systemColorScheme
    @State private var previewPosition = 69.0

    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue

    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue

    /// Theme being previewed.
    let theme: PlayerPanelTheme

    /// Colour scheme the preview renders in.
    ///
    /// Unlike the real panel this always resolves to a concrete scheme, since
    /// the preview has to look right inside a Settings window that may be in
    /// the opposite appearance.
    private var previewColorScheme: ColorScheme {
        switch theme {
        case .white: .light
        case .black: .dark
        case .liquid, .system: systemColorScheme
        }
    }

    /// Title colour for the previewed theme.
    private var titleColor: Color {
        switch theme {
        case .white: .black
        case .black: .white
        case .liquid, .system:
            systemColorScheme == .dark ? .white : .black
        }
    }

    /// What the time on the left shows, mirrored from the Panel pane.
    private var leadingTimeStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    /// What the time on the right shows, mirrored from the Panel pane.
    private var trailingTimeStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
    }

    /// The preview panel.
    ///
    /// The border and shadow are reproduced too, since the Liquid theme's
    /// translucency reads quite differently without them.
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

    /// Background matching the previewed theme.
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

/// Time display options for the player panel.
private struct PanelSettingsView: View {
    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue

    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue

    /// Selected leading time style.
    private var leadingStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    /// Selected trailing time style.
    private var trailingStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
    }

    /// The Panel pane.
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

/// Progress bar and time labels, previewing the time styles.
///
/// The slider is draggable so the effect of each style can be seen at
/// different positions - a countdown reads very differently near the end of a
/// track than at its start.
private struct PanelTimePreview: View {
    @State private var previewPosition = 69.0

    /// Leading style to preview.
    let leadingStyle: PanelLeadingTimeStyle

    /// Trailing style to preview.
    let trailingStyle: PanelTrailingTimeStyle

    /// The preview.
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

/// AppKit slider used in the settings previews.
///
/// SwiftUI's `Slider` inside a `Form` picks up the form's control sizing and
/// label treatment, which makes it look nothing like the compact slider in the
/// player panel. An `NSSlider` gives the preview the same appearance as the
/// real thing.
private struct SettingsPreviewSlider: NSViewRepresentable {
    /// Slider position.
    @Binding var value: Double

    /// Range the slider spans.
    let range: ClosedRange<Double>

    /// Creates the coordinator that receives the slider's action.
    ///
    /// - Returns: A coordinator bound to the value.
    func makeCoordinator() -> Coordinator {
        Coordinator(value: $value)
    }

    /// Builds the slider.
    ///
    /// - Parameter context: Representable context, carrying the coordinator.
    /// - Returns: The configured slider.
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

    /// Pushes the current value and range into the slider.
    ///
    /// The value is only written when it differs beyond a small epsilon, since
    /// assigning during a drag would fight the user's pointer.
    ///
    /// - Parameters:
    ///   - slider: Slider to update.
    ///   - context: Representable context, carrying the coordinator.
    func updateNSView(_ slider: NSSlider, context: Context) {
        context.coordinator.value = $value
        slider.minValue = range.lowerBound
        slider.maxValue = range.upperBound

        if abs(slider.doubleValue - value) > 0.001 {
            slider.doubleValue = value
        }
    }

    /// Bridges the slider's target-action back to the SwiftUI binding.
    final class Coordinator: NSObject {
        /// Binding to write the slider's value into.
        ///
        /// Replaced on every update, because SwiftUI hands out a fresh binding
        /// each time the view is rebuilt and a stale one would write nowhere.
        var value: Binding<Double>

        /// Creates the coordinator.
        ///
        /// - Parameter value: Binding to write into.
        init(value: Binding<Double>) {
            self.value = value
        }

        /// Forwards a slider change to the binding.
        ///
        /// - Parameter sender: The slider that changed.
        @objc
        func valueChanged(_ sender: NSSlider) {
            value.wrappedValue = sender.doubleValue
        }
    }
}

/// Menu bar appearance options, with a live preview.
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

    /// Selected artwork style.
    private var artworkStyle: MenuBarArtworkStyle {
        MenuBarArtworkStyle(rawValue: menuBarArtworkStyle) ?? .albumArtwork
    }

    /// Selected title format.
    private var titleFormat: MenuBarTitleFormat {
        MenuBarTitleFormat(rawValue: menuBarTitleFormat) ?? .titleOnly
    }

    /// Marquee speed as a layout value.
    private var carouselSpeed: CGFloat {
        CGFloat(marqueeSpeed)
    }

    /// The Menu Bar pane.
    ///
    /// The three handlers at the bottom enforce one invariant: the artwork and
    /// the title cannot both be hidden. That combination leaves an invisible
    /// menu bar item, which the user could not click to get back into Reprise
    /// or reach Settings to undo it. Whichever option was not just changed is
    /// restored instead. The check on appear covers a preferences file that
    /// already holds that pair.
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

/// Replica of the menu bar item, for previewing its appearance.
///
/// Drawn over a blue gradient standing in for a desktop wallpaper, since the
/// real item composites its white template against whatever is behind the menu
/// bar and would be invisible on the settings background.
///
/// Uses the real ``PanelTitleMarqueeView`` and ``MenuBarMarquee`` measurements,
/// so the preview scrolls and sizes exactly as the item will.
private struct MenuBarArtworkPreview: View {
    /// Artwork style to preview.
    let style: MenuBarArtworkStyle

    /// Title format to preview.
    let titleFormat: MenuBarTitleFormat

    /// Whether lyrics mode is on.
    let showsLyrics: Bool

    /// Width budget for lyrics.
    let lyricsWidth: CGFloat

    /// Whether the lyrics width is held when text is shorter.
    let reservesLyricsWidth: Bool

    /// Whether long titles scroll.
    let automaticallyScrolls: Bool

    /// Scroll speed.
    let pointsPerSecond: CGFloat

    /// Sample text for the preview.
    ///
    /// A short line in lyrics mode and a deliberately long one otherwise, so
    /// the scrolling and truncation behaviour is visible without waiting for a
    /// real track that happens to be long.
    private var previewTitle: String {
        if showsLyrics, titleFormat != .hidden {
            return "다시 만나요"
        }
        return titleFormat.text(
            title: "여기에 재생 중인 음악 제목이 표시됩니다",
            artist: "미리보기 아티스트"
        )
    }

    /// Text width budget for the preview.
    private var maximumTextWidth: CGFloat {
        showsLyrics
            ? lyricsWidth
            : MenuBarMarquee.maximumTextWidth
    }

    /// The preview item.
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

/// Preview of the menu bar item's leading visual.
private struct MenuBarLeadingArtworkPreview: View {
    /// Style to preview.
    let style: MenuBarArtworkStyle

    /// The chosen visual.
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

/// Stand-in album cover for the settings previews.
///
/// An angular gradient rather than a real image, so the previews ship no
/// third-party artwork and read as generic at any size.
private struct PreviewAlbumArtwork: View {
    /// Size of the centred note glyph.
    ///
    /// Set by the caller because the same artwork appears at 18 points in the
    /// menu bar preview and 112 in the panel preview.
    var symbolSize: CGFloat = 8

    /// The gradient with its note.
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

/// Spinning disc preview for the compact-disc style.
///
/// Uses a SwiftUI animation rather than the Core Animation rotation the real
/// item runs; they are visually equivalent, and this needs no layer plumbing.
/// It matches the real rotation duration so the speed is faithful.
private struct RotatingDiscPreview: View {
    @State private var rotation = 0.0

    /// The rotating disc.
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

/// Animated level meter preview.
///
/// Driven by a sine of the timeline's clock rather than keyframes, which is
/// less code for the same effect. Each bar is offset in phase so they never
/// move in unison.
private struct PlayingIndicatorPreview: View {
    /// The animated bars.
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

/// Version, update settings, and system details.
///
/// Serves as the About window a menu bar app has nowhere else to put, which is
/// why the version and display details are here: they are what a user quotes
/// in a bug report.
private struct SystemInfoSettingsView: View {
    /// Marketing version and build number.
    private var version: String {
        let shortVersion = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "-"
        let build = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleVersion"
        ) as? String ?? "-"

        return "\(shortVersion) (\(build))"
    }

    /// The macOS version string.
    private var operatingSystem: String {
        ProcessInfo.processInfo.operatingSystemVersionString
    }

    /// The System Info pane.
    ///
    /// Screens are enumerated by offset rather than by name, since two
    /// identical displays report the same localized name and would collide as
    /// identifiers.
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

    /// A display's resolution in pixels.
    ///
    /// Multiplies by the backing scale so a Retina display reports its real
    /// pixel count rather than its point size.
    ///
    /// - Parameter screen: Screen to describe.
    /// - Returns: A `width × height` string.
    private func resolution(of screen: NSScreen) -> String {
        let width = Int(screen.frame.width * screen.backingScaleFactor)
        let height = Int(screen.frame.height * screen.backingScaleFactor)
        return "\(width) × \(height)"
    }
}

/// Sparkle's update preferences.
private struct UpdateSettingsView: View {
    @ObservedObject private var updateController: UpdateController
    @State private var automaticallyChecksForUpdates: Bool
    @State private var automaticallyDownloadsUpdates: Bool

    /// Seeds the toggles from Sparkle's current settings.
    ///
    /// Sparkle stores these itself rather than in Reprise's preferences, so
    /// they cannot be `@AppStorage` and are mirrored into local state,
    /// initialised here and written back on change.
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

    /// The update controls.
    ///
    /// Automatic downloading is disabled without automatic checking, since
    /// there would be nothing to download from. The check button follows
    /// Sparkle's own readiness so it cannot be pressed mid-check.
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
