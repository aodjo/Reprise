// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import SwiftUI

/// Colours the panel uses that no system semantic colour provides.
enum PlayerPanelPalette {
    /// Background for the explicit dark theme.
    ///
    /// A fixed grey rather than a semantic colour, because the dark theme is a
    /// deliberate choice and must not follow the system appearance.
    static let darkBackground = Color(white: 0.12)
}

/// Translucent background for the Liquid theme.
///
/// Uses macOS 26's glass effect where available and falls back to a material
/// elsewhere. Both are compile-time and runtime guarded, so the same source
/// builds against an older SDK.
struct LiquidPanelBackground: View {
    /// Corner rounding, matched to the container being filled.
    let cornerRadius: CGFloat

    /// The glass effect, or a material on older systems.
    var body: some View {
#if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            Color.clear
                .glassEffect(
                    .clear,
                    in: RoundedRectangle(
                        cornerRadius: cornerRadius,
                        style: .continuous
                    )
                )
        } else {
            materialBackground
        }
#else
        materialBackground
#endif
    }

    /// Blurred material fallback for systems without the glass effect.
    private var materialBackground: some View {
        RoundedRectangle(
            cornerRadius: cornerRadius,
            style: .continuous
        )
        .fill(.ultraThinMaterial)
    }
}

/// The player panel shown beneath the menu bar item.
///
/// Artwork, track details, transport, progress, and - when available -
/// scrolling lyrics, over a footer carrying volume, settings, and quit.
///
/// Two interactions drive most of the state here. The volume slider and the
/// progress slider both produce a stream of values as the user drags, while
/// the store polls underneath at its own rate. Each therefore holds a local
/// value, debounces what it sends, and ignores incoming updates until the
/// interaction settles - otherwise a poll landing mid-drag would pull the
/// control out from under the pointer.
struct PlayerPopoverView: View {
    @Environment(\.colorScheme) private var systemColorScheme
    @Environment(\.openSettings) private var openSettings
    @AppStorage(ReprisePreferenceKey.automaticallyScrollTitles)
    private var automaticallyScrollTitles = true
    @AppStorage(ReprisePreferenceKey.marqueeSpeed)
    private var marqueeSpeed = MarqueeSpeed.normal.rawValue
    @AppStorage(ReprisePreferenceKey.playerPanelTheme)
    private var playerPanelTheme = PlayerPanelTheme.liquid.rawValue
    @AppStorage(ReprisePreferenceKey.playerDisplayPriority)
    private var playerDisplayOrder =
        ReprisePreferences.defaultPlayerDisplayOrder
    @AppStorage(ReprisePreferenceKey.remembersLastPlayedPlayer)
    private var remembersLastPlayedPlayer = false
    @AppStorage(ReprisePreferenceKey.lastPlayedPlayer)
    private var lastPlayedPlayer = ""
    @AppStorage(ReprisePreferenceKey.panelLeadingTimeStyle)
    private var panelLeadingTimeStyle = PanelLeadingTimeStyle.elapsed.rawValue
    @AppStorage(ReprisePreferenceKey.panelTrailingTimeStyle)
    private var panelTrailingTimeStyle = PanelTrailingTimeStyle.remaining.rawValue
    @State private var volume = 100.0
    @State private var showsVolumeSlider = false
    @State private var volumeUpdateTask: Task<Void, Never>?
    @State private var volumeRequestID: UUID?
    @State private var isVolumeEditing = false
    @State private var lastAudibleVolumes: [String: Int] = [:]
    @State private var seekPosition = 0.0
    @State private var isSeeking = false
    @State private var pendingSeekPosition: TimeInterval?
    @State private var seekRequestID: UUID?

    /// Playback state the panel presents and controls.
    @Bindable var store: NowPlayingStore

    /// Creates the panel over a store.
    ///
    /// - Parameter store: Store to present and command.
    init(store: NowPlayingStore) {
        self.store = store
    }

    /// The player currently on display.
    ///
    /// The three discarded reads are load-bearing: the store picks the active
    /// player from preferences that SwiftUI cannot see it read, so touching
    /// them here registers the dependency. Without it, changing the player
    /// priority in Settings would not refresh the open panel.
    private var snapshot: PlayerSnapshot {
        _ = playerDisplayOrder
        _ = remembersLastPlayedPlayer
        _ = lastPlayedPlayer
        return store.activeSnapshot
    }

    /// Identity of the current track, for noticing a change.
    ///
    /// Excludes duration, so a player revising its reported length mid-track
    /// does not read as a new song and cancel an in-flight seek.
    private var trackIdentity: String? {
        snapshot.track.map {
            [$0.title, $0.album, $0.artist].joined(separator: "\u{0}")
        }
    }

    /// The user's chosen panel theme.
    private var theme: PlayerPanelTheme {
        PlayerPanelTheme(rawValue: playerPanelTheme) ?? .liquid
    }

    /// What the time on the left of the progress bar shows.
    private var leadingTimeStyle: PanelLeadingTimeStyle {
        PanelLeadingTimeStyle(rawValue: panelLeadingTimeStyle) ?? .elapsed
    }

    /// What the time on the right of the progress bar shows.
    private var trailingTimeStyle: PanelTrailingTimeStyle {
        PanelTrailingTimeStyle(rawValue: panelTrailingTimeStyle) ?? .remaining
    }

    /// Colour scheme to force, or `nil` to follow the system.
    ///
    /// The Liquid theme returns `nil` so its translucency picks up whatever is
    /// behind the panel rather than being pinned to one appearance.
    private var preferredColorScheme: ColorScheme? {
        switch theme {
        case .white: .light
        case .black: .dark
        case .liquid, .system: nil
        }
    }

    /// Title colour for the AppKit marquee view.
    ///
    /// Resolved to a concrete `NSColor` rather than left semantic, because the
    /// marquee bakes the title into a bitmap and needs a real colour at draw
    /// time.
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

    /// The panel's contents.
    ///
    /// The lyrics section is conditional and animated, which changes the
    /// panel's height - hence the size notification, since an `NSPanel` does
    /// not resize itself to its SwiftUI content.
    ///
    /// Switching player resets every interaction, because a drag in progress
    /// refers to a player that is no longer on screen.
    ///
    /// The cursor is forced to an arrow on hover: the panel is hosted in a
    /// non-activating window, where AppKit does not always reset the cursor
    /// and it can arrive as an I-beam from whatever was underneath.
    var body: some View {
        VStack(spacing: 0) {
            if let track = snapshot.track {
                playerCard(track)
                    .padding(14)

                if store.syncedLyrics != nil {
                    InlineLyricsView(store: store)
                        .transition(
                            .move(edge: .top).combined(with: .opacity)
                        )
                }
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
        .onContinuousHover { phase in
            guard case .active = phase else { return }
            NSCursor.arrow.set()
        }
        .task {
            await store.refresh()
            syncVolume()
        }
        .onChange(of: snapshot.volume) {
            syncVolume()
        }
        .onChange(of: snapshot.player) {
            cancelVolumeUpdate()
            isVolumeEditing = false
            showsVolumeSlider = false
            isSeeking = false
            pendingSeekPosition = nil
            seekRequestID = nil
            syncVolume(force: true)
        }
        .onChange(of: trackIdentity) {
            isSeeking = false
            pendingSeekPosition = nil
            seekRequestID = nil
        }
        .onChange(of: store.lyricsState) {
            notifyPanelContentSizeChanged()
        }
        .onDisappear {
            cancelVolumeUpdate()
            isVolumeEditing = false
        }
        .onReceive(
            NotificationCenter.default.publisher(
                for: .playerPanelDidHide
            )
        ) { _ in
            showsVolumeSlider = false
        }
        .onReceive(
            NotificationCenter.default.publisher(
                for: .openRepriseSettings
            )
        ) { _ in
            presentSettings()
        }
    }

    /// The panel's background for the selected theme.
    @ViewBuilder
    private var panelBackground: some View {
        switch theme {
        case .white:
            Color.white
        case .black:
            PlayerPanelPalette.darkBackground
        case .liquid:
            LiquidPanelBackground(
                cornerRadius: PlayerPanelLayout.cornerRadius
            )
        case .system:
            Color(nsColor: .windowBackgroundColor)
        }
    }

    /// Artwork, metadata, transport, and progress for a loaded track.
    ///
    /// The right column is pinned to the artwork's height so the transport
    /// buttons stay put whether or not the track has an artist line.
    ///
    /// The progress row is wrapped in a `TimelineView` ticking four times a
    /// second: the position is projected from the store's anchor, so it
    /// advances smoothly without the store polling anywhere near that often.
    ///
    /// - Parameter track: Track to display.
    /// - Returns: The card view.
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

                TimelineView(.periodic(from: .now, by: 0.25)) { context in
                    progress(track, at: context.date)
                }
            }
            .frame(height: 112)
        }
    }

    /// Placeholder shown when no track is loaded.
    ///
    /// Distinguishes three cases so the message is actionable: the player
    /// could not be reached, it is open but idle, or it is not running at all.
    ///
    /// The minimum height keeps the panel from collapsing to the footer, which
    /// would make it flicker in size as playback starts and stops.
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

    /// Previous, play/pause, and next.
    ///
    /// Disabled together when the player is unreachable, since a press would
    /// only produce an error banner.
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

    /// Gear button opening Settings.
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

    /// Button quitting Reprise.
    ///
    /// The panel is the only place quit is offered, since a menu bar app has
    /// no window to close and no Dock icon to quit from.
    private var exitButton: some View {
        Button {
            NSApp.terminate(nil)
        } label: {
            Image(systemName: "rectangle.portrait.and.arrow.right")
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(.secondary)
                .frame(width: 19, height: 19)
        }
        .buttonStyle(.plain)
        .help("Reprise 종료")
        .accessibilityLabel("Reprise 종료")
        .accessibilityIdentifier("exitButton")
    }

    /// Version text with the volume, settings, and quit buttons.
    ///
    /// Raised above the rest of the panel so the volume slider, which is a
    /// child window anchored here, is not overlapped by the content above it.
    private var footer: some View {
        HStack(spacing: 8) {
            Text(appVersionText)
                .font(.caption2)
                .foregroundStyle(.secondary)

            Spacer(minLength: 8)

            volumeButton

            settingsButton

            exitButton
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

    /// Version and build, read from the bundle.
    ///
    /// Shown because a menu bar app has no About window; it is what a user
    /// reporting a problem can quote.
    private var appVersionText: String {
        let version = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleShortVersionString"
        ) as? String ?? "-"
        let build = Bundle.main.object(
            forInfoDictionaryKey: "CFBundleVersion"
        ) as? String ?? "-"

        return "Reprise v\(version) (\(build))"
    }

    /// Speaker button revealing the volume slider.
    ///
    /// The slider lives in a separate child window rather than a popover,
    /// because the panel clips its contents and a popover from a
    /// non-activating panel does not behave reliably. It is attached through
    /// an invisible background view, which gives the child window something to
    /// anchor to.
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
        .background {
            VolumeSliderPanelPresenter(
                isPresented: $showsVolumeSlider,
                volume: $volume,
                symbol: volumeSymbol,
                playerName: snapshot.player.displayName,
                colorScheme: preferredColorScheme,
                onVolumeChanged: {
                    scheduleVolumeUpdate()
                },
                onEditingChanged: { isEditing in
                    isVolumeEditing = isEditing
                    if !isEditing {
                        scheduleVolumeUpdate(immediately: true)
                    }
                },
                onToggleMute: {
                    toggleMute()
                }
            )
        }
        .zIndex(showsVolumeSlider ? 2 : 0)
    }

    /// Speaker symbol matching the current level.
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

    /// Adopts the player's volume into the slider.
    ///
    /// Refuses while the user is dragging or a change is in flight, since
    /// accepting the poll's older value then would drag the slider backwards
    /// under the pointer.
    ///
    /// - Parameter force: Adopt regardless of an interaction in progress. Used
    ///   on a player switch, where the previous player's level does not apply.
    ///   Defaults to `false`.
    private func syncVolume(force: Bool = false) {
        guard force || (
            !isVolumeEditing && volumeRequestID == nil
        ),
              let snapshotVolume = snapshot.volume else {
            return
        }
        volume = Double(snapshotVolume)
        rememberAudibleVolume(snapshotVolume, for: snapshot.player)
    }

    /// Sends the slider's value to the player, debounced.
    ///
    /// A drag emits values continuously and each one costs an AppleScript
    /// round trip, so all but the last 80ms are coalesced. The request id lets
    /// a superseded update abandon itself, so an earlier slower call cannot
    /// land after a later one.
    ///
    /// - Parameter immediately: Skip the debounce, for the end of a drag or a
    ///   mute toggle where the value is final. Defaults to `false`.
    private func scheduleVolumeUpdate(immediately: Bool = false) {
        volumeUpdateTask?.cancel()
        let requestID = UUID()
        volumeRequestID = requestID
        let level = PlayerVolume.clamped(Int(volume.rounded()))
        let player = snapshot.player
        rememberAudibleVolume(level, for: player)

        volumeUpdateTask = Task {
            if !immediately {
                try? await Task.sleep(for: .milliseconds(80))
            }
            guard !Task.isCancelled,
                  volumeRequestID == requestID else { return }
            await store.setVolume(level, for: player)
            guard !Task.isCancelled,
                  volumeRequestID == requestID else { return }
            volumeUpdateTask = nil
            volumeRequestID = nil
            if snapshot.player == player,
               let actualVolume = store.snapshot(for: player).volume {
                volume = Double(actualVolume)
                rememberAudibleVolume(actualVolume, for: player)
            }
        }
    }

    /// Abandons any pending volume change.
    private func cancelVolumeUpdate() {
        volumeUpdateTask?.cancel()
        volumeUpdateTask = nil
        volumeRequestID = nil
    }

    /// Mutes, or restores the level from before the mute.
    ///
    /// The current level is remembered first, so muting from the button always
    /// has something to come back to.
    private func toggleMute() {
        let player = snapshot.player
        let currentVolume = PlayerVolume.clamped(Int(volume.rounded()))
        rememberAudibleVolume(currentVolume, for: player)

        volume = Double(
            PlayerVolume.muteToggleTarget(
                current: currentVolume,
                lastAudible: lastAudibleVolumes[player.rawValue]
            )
        )
        scheduleVolumeUpdate(immediately: true)
    }

    /// Records a non-zero level, per player, for unmuting.
    ///
    /// Kept per player because each has its own volume; unmuting Spotify to
    /// Music's last level would be wrong.
    ///
    /// - Parameters:
    ///   - volume: Level to remember; zero is ignored.
    ///   - player: Player it belongs to.
    private func rememberAudibleVolume(
        _ volume: Int,
        for player: MediaPlayerKind
    ) {
        let volume = PlayerVolume.clamped(volume)
        guard volume > 0 else { return }
        lastAudibleVolumes[player.rawValue] = volume
    }

    /// Opens Settings and brings it forward.
    ///
    /// Activation and the follow-up focus pass are both needed: the panel is
    /// non-activating, so Reprise is not frontmost, and `openSettings` does
    /// not reliably raise an already-open Settings window.
    private func presentSettings() {
        NSApp.activate(ignoringOtherApps: true)
        openSettings()

        Task { @MainActor in
            await SettingsWindowFocus.bringToFront()
        }
    }

    /// Asks the panel window to re-measure its content.
    ///
    /// An `NSPanel` hosting SwiftUI does not resize itself, so the height
    /// change from lyrics appearing has to be announced.
    private func notifyPanelContentSizeChanged() {
        NotificationCenter.default.post(
            name: .playerPanelContentSizeDidChange,
            object: nil
        )
    }

    /// Builds one transport button.
    ///
    /// - Parameters:
    ///   - command: Command to send.
    ///   - symbol: SF Symbol to draw.
    ///   - label: Tooltip and accessibility label.
    ///   - prominent: Whether to draw larger, for play/pause. Defaults to
    ///     `false`.
    /// - Returns: The button.
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

    /// Progress slider with the elapsed and remaining times.
    ///
    /// The displayed position comes from one of three sources in priority
    /// order: the drag in progress, a seek that has been sent but not yet
    /// confirmed, or the store's estimate. The middle case is what stops the
    /// bar snapping back to where the track was while the player catches up.
    ///
    /// The slider's upper bound is floored at 1 so a track with no reported
    /// duration still yields a valid range rather than an empty one.
    ///
    /// - Parameters:
    ///   - track: Track being played.
    ///   - date: Instant to evaluate the position at.
    /// - Returns: The progress view.
    private func progress(
        _ track: Track,
        at date: Date
    ) -> some View {
        let duration = track.duration.isFinite && track.duration > 0
            ? track.duration
            : 0
        let sliderUpperBound = max(duration, 1)
        let displayedPosition: TimeInterval
        if isSeeking {
            displayedPosition = PlaybackPosition.clamped(
                seekPosition,
                duration: duration
            )
        } else if let pendingSeekPosition {
            displayedPosition = PlaybackPosition.clamped(
                pendingSeekPosition,
                duration: duration
            )
        } else {
            displayedPosition = PlaybackPosition.clamped(
                store.estimatedPlaybackPosition(at: date),
                duration: duration
            )
        }
        let displayedRemaining = max(
            duration - displayedPosition,
            0
        )

        return VStack(spacing: 1) {
            Slider(
                value: Binding(
                    get: {
                        displayedPosition
                    },
                    set: { newValue in
                        seekPosition = PlaybackPosition.clamped(
                            newValue,
                            duration: duration
                        )
                        pendingSeekPosition = nil
                        isSeeking = true
                    }
                ),
                in: 0...sliderUpperBound
            ) { isEditing in
                if isEditing {
                    if !isSeeking {
                        seekPosition = displayedPosition
                    }
                    pendingSeekPosition = nil
                    seekRequestID = nil
                    isSeeking = true
                } else {
                    finishSeeking(track)
                }
            }
            .controlSize(.small)
            .tint(.accentColor)
            .accessibilityLabel("재생 진행")
            .accessibilityValue(
                "\(Int((displayedPosition / sliderUpperBound) * 100))퍼센트"
            )
            .help("재생 위치 이동")
            .disabled(duration <= 0 || !snapshot.isRunning)

            HStack {
                Text(
                    PanelTimeDisplay.leadingText(
                        style: leadingTimeStyle,
                        position: displayedPosition
                    )
                )
                Spacer()
                Text(
                    PanelTimeDisplay.trailingText(
                        style: trailingTimeStyle,
                        duration: duration,
                        remaining: displayedRemaining
                    )
                )
            }
            .font(.caption2.monospacedDigit())
            .foregroundStyle(.secondary)
        }
    }

    /// Sends the seek when the user releases the slider.
    ///
    /// The pending position is held until the store confirms, so the bar stays
    /// where it was dropped rather than jumping back for the second or so the
    /// player takes to settle. The request id and player check make sure a
    /// slow seek cannot clear a newer one's pending state.
    ///
    /// - Parameter track: Track being seeked within.
    private func finishSeeking(_ track: Track) {
        guard isSeeking else { return }

        let targetPosition = PlaybackPosition.clamped(
            seekPosition,
            duration: track.duration
        )
        let player = snapshot.player
        let requestID = UUID()
        pendingSeekPosition = targetPosition
        seekRequestID = requestID
        isSeeking = false

        Task {
            await store.seek(to: targetPosition, for: player)
            guard snapshot.player == player,
                  seekRequestID == requestID else {
                return
            }
            pendingSeekPosition = nil
            seekRequestID = nil
        }
    }

    /// Inline banner carrying a command or player error.
    ///
    /// - Parameter message: Text to display.
    /// - Returns: The banner view.
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

    /// Stable identifier for a transport button, for UI tests.
    ///
    /// - Parameter command: Command the button sends.
    /// - Returns: The identifier.
    private func accessibilityIdentifier(for command: PlaybackCommand) -> String {
        switch command {
        case .previous: "previousButton"
        case .pause: "pauseButton"
        case .playPause: "playPauseButton"
        case .stop: "stopButton"
        case .next: "nextButton"
        }
    }

}

/// Raises the Settings window once SwiftUI has created it.
@MainActor
private enum SettingsWindowFocus {
    /// Waits for the Settings window and brings it to the front.
    ///
    /// `openSettings()` returns before the window exists, so there is nothing
    /// to raise at the moment it is called. This polls briefly rather than
    /// guessing a delay - half a second is long enough for a cold open and
    /// exits immediately once found.
    ///
    /// De-miniaturising first covers the case where the user minimised
    /// Settings earlier, where ordering front alone would do nothing.
    static func bringToFront() async {
        for _ in 0..<10 {
            if let window = NSApp.windows.first(where: isSettingsWindow) {
                if window.isMiniaturized {
                    window.deminiaturize(nil)
                }
                NSApp.activate(ignoringOtherApps: true)
                window.makeKeyAndOrderFront(nil)
                return
            }

            try? await Task.sleep(for: .milliseconds(50))
        }
    }

    /// Whether a window looks like the Settings window.
    ///
    /// Identified by shape rather than by title, which is localised. Panels
    /// are excluded so the player panel and the volume slider are not mistaken
    /// for it.
    ///
    /// - Parameter window: Window to test.
    /// - Returns: `true` when it is probably Settings.
    private static func isSettingsWindow(_ window: NSWindow) -> Bool {
        !(window is NSPanel)
            && window.styleMask.contains(.titled)
            && window.canBecomeKey
    }
}

/// Borderless press style for the transport buttons.
///
/// Replaces the system button chrome, which would draw a bordered control
/// where the panel wants bare glyphs, with a dim-and-shrink press response.
private struct CompactControlButtonStyle: ButtonStyle {
    /// Applies the press treatment.
    ///
    /// - Parameter configuration: Button state from SwiftUI.
    /// - Returns: The styled label.
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(Color.primary.opacity(configuration.isPressed ? 0.55 : 0.72))
            .scaleEffect(configuration.isPressed ? 0.94 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}

/// The current player's logo, shown beside the track title.
private struct PlayerLogoView: View {
    /// Player to represent.
    let player: MediaPlayerKind

    /// Spotify's mark, sized and marked as a template.
    ///
    /// Loaded once and resized up front because `Image(nsImage:)` renders at
    /// the image's own size before SwiftUI's frame applies. Template mode is
    /// what lets it take the panel's foreground colour rather than shipping
    /// the brand colour, which would clash with both themes.
    ///
    /// An empty image of the right size is the fallback, so a missing asset
    /// leaves a gap rather than shifting the layout.
    private static let spotifyLogo: NSImage = {
        guard let source = NSImage(named: "SpotifyLogo"),
              let image = source.copy() as? NSImage else {
            return NSImage(size: NSSize(width: 21, height: 21))
        }

        image.size = NSSize(width: 21, height: 21)
        image.isTemplate = true
        return image
    }()

    /// YouTube Music's mark, prepared the same way as ``spotifyLogo``.
    private static let youtubeMusicLogo: NSImage = {
        guard let source = NSImage(named: "YouTubeMusicLogo"),
              let image = source.copy() as? NSImage else {
            return NSImage(size: NSSize(width: 21, height: 21))
        }

        image.size = NSSize(width: 21, height: 21)
        image.isTemplate = true
        return image
    }()

    /// The logo for the current player.
    ///
    /// Music uses an SF Symbol rather than a bundled asset, since Apple's mark
    /// is not redistributable and the note glyph reads the same way.
    @ViewBuilder
    var body: some View {
        switch player {
        case .spotify:
            Image(nsImage: Self.spotifyLogo)
                .renderingMode(.template)
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

        case .youtubeMusic:
            Image(nsImage: Self.youtubeMusicLogo)
                .renderingMode(.template)
                .foregroundStyle(.primary)
                .frame(width: 21, height: 21)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("YouTube Music 로고")
                .accessibilityIdentifier("playerLogo")
        }
    }
}

/// Scrolling lyrics beneath the player card.
///
/// Every line is laid out at once in a `ZStack` and offset by its distance
/// from the focused line, so advancing a line is a change of offset that
/// SwiftUI animates - rather than a scroll position to drive imperatively.
/// A gradient mask fades the top and bottom edges so lines enter and leave
/// without a hard cut.
private struct InlineLyricsView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Store supplying the lyrics and playback position.
    @Bindable var store: NowPlayingStore

    /// Height of one lyric line.
    private let rowHeight: CGFloat = 29

    /// Height of the lyrics area, fitting roughly four lines.
    private let lyricsViewportHeight: CGFloat = 136

    /// Nudge keeping the previous line clear of the top fade.
    private let previousLineTopInset: CGFloat = 2

    /// How many lines ahead take a staggered delay.
    ///
    /// Beyond this the delay stops growing, or lines far down the list would
    /// still be settling long after the current one had moved on.
    private let maximumCascadeStep = 3

    /// Delay added per line of the cascade.
    private let cascadeDelay = 0.045

    /// The lyrics viewport.
    ///
    /// Ticks four times a second, which is enough for a lyric line - unlike
    /// the progress bar, a line changes every few seconds.
    var body: some View {
        VStack(spacing: 0) {
            TimelineView(.periodic(from: .now, by: 0.25)) { context in
                lyricsBody(at: context.date)
            }
            .frame(height: lyricsViewportHeight)
            .mask {
                LinearGradient(
                    stops: [
                        .init(color: .clear, location: 0),
                        .init(color: .black, location: 0.18),
                        .init(color: .black, location: 0.82),
                        .init(color: .clear, location: 1),
                    ],
                    startPoint: .top,
                    endPoint: .bottom
                )
            }
            .clipped()
        }
        .frame(height: lyricsViewportHeight)
        .overlay(alignment: .top) {
            Rectangle()
                .fill(.primary.opacity(0.14))
                .frame(height: 0.5)
        }
    }

    /// Lays every lyric line out relative to the focused one.
    ///
    /// Two indices are used, and the distinction matters. The focused line is
    /// what the list is positioned around and always exists once playback has
    /// begun; the current line may be `nil` during an instrumental gap. So
    /// scrolling stays put while the highlight fades out, rather than the
    /// lyrics jumping whenever no line is active.
    ///
    /// Every line is rendered rather than only the visible few: a lyric sheet
    /// is small enough that windowing would cost more than it saved, and lines
    /// need to already exist to animate in.
    ///
    /// - Parameter date: Instant to evaluate.
    /// - Returns: The stacked lines.
    private func lyricsBody(at date: Date) -> some View {
        let lyrics = store.syncedLyrics
        let currentIndex = store.currentLyricLineIndex(at: date)
        let focusedIndex =
            store.focusedLyricLineIndex(at: date) ?? 0

        return GeometryReader { _ in
            if let lyrics {
                ZStack(alignment: .top) {
                    ForEach(lyrics.lines.indices, id: \.self) { index in
                        let relativeIndex = index - focusedIndex
                        let isCurrent = currentIndex == index

                        lyricRow(
                            lyrics.lines[index].text,
                            isCurrent: isCurrent
                        )
                        .frame(height: rowHeight)
                        .offset(
                            y: previousLineTopInset
                                + CGFloat(relativeIndex + 1) * rowHeight
                        )
                        .animation(
                            movementAnimation(
                                relativeIndex: relativeIndex
                            ),
                            value: focusedIndex
                        )
                        .zIndex(isCurrent ? 1 : 0)
                    }
                }
            }
        }
    }

    /// One lyric line.
    ///
    /// Lines are truncated rather than wrapped, since a wrapped line would
    /// break the fixed row height the offsets depend on.
    ///
    /// - Parameters:
    ///   - text: Line text.
    ///   - isCurrent: Whether this is the active line, drawn at full opacity.
    /// - Returns: The row view.
    private func lyricRow(
        _ text: String?,
        isCurrent: Bool
    ) -> some View {
        Text(text ?? "")
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(.primary)
            .opacity(isCurrent ? 1 : 0.38)
            .lineLimit(1)
            .truncationMode(.tail)
            .frame(maxWidth: .infinity)
            .padding(.horizontal, 18)
            .contentTransition(.opacity)
            .animation(
                reduceMotion
                    ? .easeOut(duration: 0.15)
                    : .easeInOut(duration: 0.22),
                value: isCurrent
            )
    }

    /// Animation for a line moving to its new position.
    ///
    /// Lines below the focus start fractionally later than those above, which
    /// makes the sheet settle as a group rather than snapping as a block. The
    /// spring's slight bounce gives it the weight of a physical scroll.
    ///
    /// Reduce Motion replaces all of it with a plain ease, since the cascade
    /// and bounce are exactly the kind of movement that setting asks to avoid.
    ///
    /// - Parameter relativeIndex: Distance from the focused line; negative
    ///   above, positive below.
    /// - Returns: The animation to apply.
    private func movementAnimation(
        relativeIndex: Int
    ) -> Animation {
        if reduceMotion {
            return .easeOut(duration: 0.18)
        }

        let cascadeStep = min(
            max(relativeIndex, 0),
            maximumCascadeStep
        )

        return .spring(duration: 0.56, bounce: 0.24)
            .delay(Double(cascadeStep) * cascadeDelay)
    }
}

/// Hosts the volume slider in a child window anchored to the footer.
///
/// A popover would be clipped by the panel and does not behave reliably from a
/// non-activating window, so the slider is its own borderless panel attached
/// as a child - which keeps it above the player panel and moving with it.
///
/// The representable itself renders nothing: its view exists only to locate
/// the anchor and the parent window.
private struct VolumeSliderPanelPresenter: NSViewRepresentable {
    /// Whether the slider is showing.
    @Binding var isPresented: Bool

    /// Volume level, shared with the panel.
    @Binding var volume: Double

    /// Speaker symbol matching the level.
    let symbol: String

    /// Player name, for accessibility.
    let playerName: String

    /// Colour scheme to force, or `nil` to follow the system.
    let colorScheme: ColorScheme?

    /// Called as the slider moves.
    let onVolumeChanged: () -> Void

    /// Called when dragging starts and stops.
    let onEditingChanged: (Bool) -> Void

    /// Called when the speaker icon is clicked.
    let onToggleMute: () -> Void

    /// Creates the coordinator owning the child window.
    ///
    /// - Returns: A new coordinator.
    func makeCoordinator() -> Coordinator {
        Coordinator()
    }

    /// Creates the invisible anchor view.
    ///
    /// - Parameter context: Representable context; unused.
    /// - Returns: An empty view, used only for positioning.
    func makeNSView(context: Context) -> NSView {
        NSView()
    }

    /// Pushes current values into the child window.
    ///
    /// - Parameters:
    ///   - nsView: The anchor view.
    ///   - context: Representable context, carrying the coordinator.
    func updateNSView(_ nsView: NSView, context: Context) {
        let content = VolumeSliderPanelContent(
            volume: $volume,
            symbol: symbol,
            playerName: playerName,
            colorScheme: colorScheme,
            onVolumeChanged: onVolumeChanged,
            onEditingChanged: onEditingChanged,
            onToggleMute: onToggleMute
        )

        context.coordinator.update(
            anchorView: nsView,
            isPresented: $isPresented,
            content: content
        )
    }

    /// Tears the child window down with the SwiftUI view.
    ///
    /// Required because the panel is a window rather than a subview, so it
    /// would otherwise outlive the view that created it.
    ///
    /// - Parameters:
    ///   - nsView: The anchor view; unused.
    ///   - coordinator: Coordinator to tear down.
    static func dismantleNSView(
        _ nsView: NSView,
        coordinator: Coordinator
    ) {
        coordinator.tearDown()
    }

    /// Owns the slider's child window and its dismissal.
    @MainActor
    final class Coordinator {
        private let panelSize = NSSize(width: 160, height: 36)
        private let panelSpacing: CGFloat = 6
        private let parentTrailingInset: CGFloat = 14
        private var panel: VolumeSliderPanel?
        private var hostingController:
            NSHostingController<VolumeSliderPanelContent>?
        private weak var parentWindow: NSWindow?
        private weak var anchorView: NSView?
        private var presentation: Binding<Bool>?
        private var localEventMonitor: Any?
        private var globalEventMonitor: Any?

        /// Shows, hides, or refreshes the slider window.
        ///
        /// The hosted root view is replaced on every call so the slider tracks
        /// the volume even when the window is already up.
        ///
        /// The panel is created once and reused, since building a window per
        /// presentation would flicker.
        ///
        /// - Parameters:
        ///   - anchorView: View to position relative to.
        ///   - isPresented: Binding driving visibility, also written on
        ///     dismissal so SwiftUI stays in step.
        ///   - content: Slider content to host.
        func update(
            anchorView: NSView,
            isPresented: Binding<Bool>,
            content: VolumeSliderPanelContent
        ) {
            self.anchorView = anchorView
            presentation = isPresented

            if let hostingController {
                hostingController.rootView = content
            }

            guard isPresented.wrappedValue else {
                dismiss()
                return
            }

            guard let parentWindow = anchorView.window else {
                return
            }

            let panel = panel ?? makePanel(content: content)
            attach(panel, to: parentWindow)
            position(panel, relativeTo: parentWindow)
            panel.orderFrontRegardless()
            installEventMonitors()
        }

        /// Closes and releases the slider window.
        func tearDown() {
            dismiss()
            panel?.contentViewController = nil
            panel?.close()
            panel = nil
            hostingController = nil
        }

        /// Builds the slider's borderless window.
        ///
        /// Placed one level above the player panel so it is never obscured by
        /// its own parent. `hidesOnDeactivate` is off because Reprise is
        /// frequently not the active app while the panel is open.
        ///
        /// - Parameter content: Slider content to host.
        /// - Returns: The configured panel.
        private func makePanel(
            content: VolumeSliderPanelContent
        ) -> VolumeSliderPanel {
            let hostingController = NSHostingController(rootView: content)
            let panel = VolumeSliderPanel(size: panelSize)

            panel.contentViewController = hostingController
            panel.backgroundColor = .clear
            panel.isOpaque = false
            panel.hasShadow = true
            panel.level = NSWindow.Level(
                rawValue: NSWindow.Level.popUpMenu.rawValue + 1
            )
            panel.isReleasedWhenClosed = false
            panel.animationBehavior = .none
            panel.hidesOnDeactivate = false
            panel.collectionBehavior = [
                .canJoinAllSpaces,
                .fullScreenAuxiliary,
                .transient,
                .ignoresCycle,
            ]

            let contentView = hostingController.view
            contentView.wantsLayer = true
            contentView.layer?.backgroundColor = NSColor.clear.cgColor
            contentView.layer?.cornerRadius = 10
            contentView.layer?.cornerCurve = .continuous
            contentView.layer?.masksToBounds = true
            contentView.layer?.borderWidth = 0.5
            contentView.layer?.borderColor = NSColor.black
                .withAlphaComponent(0.42)
                .cgColor

            self.panel = panel
            self.hostingController = hostingController
            return panel
        }

        /// Makes the slider a child of the player panel.
        ///
        /// Child status is what keeps the two moving together and correctly
        /// ordered. Re-parenting is skipped when nothing changed, since
        /// re-adding a child window makes it flicker.
        ///
        /// - Parameters:
        ///   - panel: Slider window.
        ///   - parentWindow: Player panel window.
        private func attach(
            _ panel: NSPanel,
            to parentWindow: NSWindow
        ) {
            guard self.parentWindow !== parentWindow else {
                return
            }

            if let currentParent = self.parentWindow {
                currentParent.removeChildWindow(panel)
            }
            parentWindow.addChildWindow(panel, ordered: .above)
            self.parentWindow = parentWindow
        }

        /// Places the slider under the panel's trailing edge.
        ///
        /// Flips above the panel when there is not enough room below, which
        /// happens with a panel near the bottom of the screen. Both axes are
        /// clamped to the visible frame and the origin snapped to a device
        /// pixel, since a fractional window origin blurs its contents.
        ///
        /// - Parameters:
        ///   - panel: Slider window.
        ///   - parentWindow: Player panel window.
        private func position(
            _ panel: NSPanel,
            relativeTo parentWindow: NSWindow
        ) {
            let visibleFrame = (
                parentWindow.screen ?? NSScreen.main
            )?.visibleFrame ?? parentWindow.frame
            let unclampedX = parentWindow.frame.maxX
                - parentTrailingInset
                - panelSize.width
            let minimumX = visibleFrame.minX + panelSpacing
            let maximumX = visibleFrame.maxX
                - panelSize.width
                - panelSpacing
            let x = min(max(unclampedX, minimumX), maximumX)

            let preferredY = parentWindow.frame.minY
                - panelSize.height
                - panelSpacing
            let y: CGFloat
            if preferredY >= visibleFrame.minY + panelSpacing {
                y = preferredY
            } else {
                y = min(
                    parentWindow.frame.maxY + panelSpacing,
                    visibleFrame.maxY - panelSize.height - panelSpacing
                )
            }

            let scale = parentWindow.screen?.backingScaleFactor ?? 1
            panel.setFrameOrigin(
                NSPoint(
                    x: (x * scale).rounded() / scale,
                    y: (y * scale).rounded() / scale
                )
            )
        }

        /// Hides the slider and detaches it from its parent.
        private func dismiss() {
            removeEventMonitors()

            if let panel, let parentWindow {
                parentWindow.removeChildWindow(panel)
            }
            panel?.orderOut(nil)
            parentWindow = nil
        }

        /// Arms click-outside dismissal for the slider.
        ///
        /// The local monitor lets clicks through to the slider itself and to
        /// the speaker button, which would otherwise dismiss and immediately
        /// re-present. The global monitor catches clicks in other apps.
        private func installEventMonitors() {
            guard localEventMonitor == nil,
                  globalEventMonitor == nil else {
                return
            }

            localEventMonitor = NSEvent.addLocalMonitorForEvents(
                matching: [.leftMouseDown, .rightMouseDown]
            ) { [weak self] event in
                guard let self,
                      let panel,
                      panel.isVisible else {
                    return event
                }

                if event.window === panel
                    || isEventInsideAnchor(event) {
                    return event
                }

                requestDismissal()
                return event
            }

            globalEventMonitor = NSEvent.addGlobalMonitorForEvents(
                matching: [.leftMouseDown, .rightMouseDown]
            ) { [weak self] _ in
                Task { @MainActor in
                    self?.requestDismissal()
                }
            }
        }

        /// Whether a click landed on the speaker button.
        ///
        /// - Parameter event: Mouse event to test.
        /// - Returns: `true` when the click was inside the anchor view.
        private func isEventInsideAnchor(_ event: NSEvent) -> Bool {
            guard let anchorView,
                  event.window === anchorView.window else {
                return false
            }

            let point = anchorView.convert(
                event.locationInWindow,
                from: nil
            )
            return anchorView.bounds.contains(point)
        }

        /// Dismisses the slider and tells SwiftUI it closed.
        ///
        /// Writing the binding matters: without it SwiftUI would still believe
        /// the slider was showing, and the next button press would toggle it
        /// closed rather than open.
        private func requestDismissal() {
            guard presentation?.wrappedValue == true else {
                return
            }

            presentation?.wrappedValue = false
            dismiss()
        }

        /// Disarms the dismissal monitors.
        private func removeEventMonitors() {
            if let localEventMonitor {
                NSEvent.removeMonitor(localEventMonitor)
                self.localEventMonitor = nil
            }
            if let globalEventMonitor {
                NSEvent.removeMonitor(globalEventMonitor)
                self.globalEventMonitor = nil
            }
        }
    }
}

/// The mute button, slider, and readout inside the volume window.
private struct VolumeSliderPanelContent: View {
    /// Volume level, shared with the panel.
    @Binding var volume: Double

    /// Speaker symbol matching the level.
    let symbol: String

    /// Player name, for accessibility.
    let playerName: String

    /// Colour scheme to force, or `nil` to follow the system.
    let colorScheme: ColorScheme?

    /// Called as the slider moves.
    let onVolumeChanged: () -> Void

    /// Called when dragging starts and stops.
    let onEditingChanged: (Bool) -> Void

    /// Called when the speaker icon is clicked.
    let onToggleMute: () -> Void

    /// The slider row.
    ///
    /// The numeric readout has a fixed width so the slider does not resize as
    /// the number goes from one digit to three.
    var body: some View {
        HStack(spacing: 8) {
            Button(action: onToggleMute) {
                Image(systemName: symbol)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 18, height: 22)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(volume > 0 ? "음소거" : "음소거 해제")
            .accessibilityLabel(volume > 0 ? "음소거" : "음소거 해제")

            Slider(
                value: Binding(
                    get: { volume },
                    set: { newValue in
                        volume = newValue
                        onVolumeChanged()
                    }
                ),
                in: 0...100
            ) { isEditing in
                onEditingChanged(isEditing)
            }
            .controlSize(.small)
            .accessibilityLabel("\(playerName) 음량")
            .accessibilityValue("\(Int(volume.rounded()))퍼센트")

            Text("\(Int(volume.rounded()))")
                .font(.caption2.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(width: 23, alignment: .trailing)
        }
        .padding(.horizontal, 10)
        .frame(width: 160, height: 36)
        .background {
            LiquidPanelBackground(cornerRadius: 10)
        }
        .preferredColorScheme(colorScheme)
        .onContinuousHover { phase in
            guard case .active = phase else { return }
            NSCursor.arrow.set()
        }
    }
}

/// Borderless window holding the volume slider.
///
/// Non-activating like the player panel, so adjusting the volume never pulls
/// Reprise in front of whatever the user is working in.
@MainActor
private final class VolumeSliderPanel: NSPanel {
    /// Creates the window at a fixed size.
    ///
    /// - Parameter size: Size of the slider content.
    init(size: NSSize) {
        super.init(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        isFloatingPanel = true
        becomesKeyOnlyIfNeeded = true
    }

    /// Allows the window to take key status.
    ///
    /// Required for the slider to receive drag events, which a borderless
    /// window would otherwise refuse.
    override var canBecomeKey: Bool {
        true
    }

    /// Keeps the window from becoming main.
    override var canBecomeMain: Bool {
        false
    }
}

#Preview("홍보용 데모") {
    PlayerPopoverView(store: NowPlayingStore(demoMode: true))
}

extension Notification.Name {
    /// Posted to open the Settings window.
    ///
    /// Used by the Command-comma hot key, which is handled in the app delegate
    /// but has to reach the SwiftUI view holding the `openSettings` action.
    static let openRepriseSettings = Notification.Name(
        "dev.junx.Reprise.openSettings"
    )

    /// Posted when the panel's SwiftUI content changes height.
    ///
    /// Lets the hosting `NSPanel` re-measure, which it does not do on its own.
    static let playerPanelContentSizeDidChange = Notification.Name(
        "dev.junx.Reprise.playerPanelContentSizeDidChange"
    )
}
