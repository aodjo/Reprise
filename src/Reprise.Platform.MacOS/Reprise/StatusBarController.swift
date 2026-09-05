// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Carbon
import CoreText
import Darwin
import SwiftUI

private let settingsHotKeySignature: OSType = 0x5250_5253
private let settingsHotKeyIdentifier: UInt32 = 1

enum MenuBarTitleTransitionStyle: Equatable {
    case immediate
    case lyricsUpward

    static func resolved(
        previousTitle: String?,
        currentTitle: String,
        previousTrackKey: String?,
        currentTrackKey: String?,
        isDisplayingLyrics: Bool
    ) -> Self {
        guard isDisplayingLyrics,
              let previousTitle,
              previousTitle != currentTitle,
              let currentTrackKey,
              previousTrackKey == currentTrackKey else {
            return .immediate
        }
        return .lyricsUpward
    }
}

enum PlayerPanelActivationPolicy {
    static func shouldDismiss(
        activatedProcessIdentifier: pid_t,
        repriseProcessIdentifier: pid_t
    ) -> Bool {
        activatedProcessIdentifier != repriseProcessIdentifier
    }
}

private let settingsHotKeyEventHandler: EventHandlerUPP = {
    _, event, userData in
    guard let event, let userData else {
        return OSStatus(eventNotHandledErr)
    }

    var hotKeyID = EventHotKeyID()
    let status = GetEventParameter(
        event,
        EventParamName(kEventParamDirectObject),
        EventParamType(typeEventHotKeyID),
        nil,
        MemoryLayout<EventHotKeyID>.size,
        nil,
        &hotKeyID
    )
    guard status == noErr,
          hotKeyID.signature == settingsHotKeySignature,
          hotKeyID.id == settingsHotKeyIdentifier else {
        return OSStatus(eventNotHandledErr)
    }

    let appDelegate = Unmanaged<RepriseAppDelegate>
        .fromOpaque(userData)
        .takeUnretainedValue()
    Task { @MainActor in
        appDelegate.openSettingsFromHotKey()
    }
    return noErr
}

@MainActor
final class RepriseAppDelegate: NSObject, NSApplicationDelegate {
    private let store = NowPlayingStore()
    private var statusItem: NSStatusItem?
    private var renderer: MenuBarStatusRenderer?
    private var playerPanel: PlayerPanel?
    private var localEventMonitor: Any?
    private var globalEventMonitor: Any?
    private var workspaceActivationObserver: NSObjectProtocol?
    private var settingsHotKey: EventHotKeyRef?
    private var settingsHotKeyHandler: EventHandlerRef?
    private var instanceLock: RepriseInstanceLock?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Unit-test hosts also load the app target. Avoid creating a status
        // item or sending Apple Events to the user's media apps during tests.
        guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else {
            return
        }

        guard let instanceLock = RepriseInstanceLock() else {
            NSApp.terminate(nil)
            return
        }
        self.instanceLock = instanceLock
        UpdateController.shared.start()

        let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        guard let button = statusItem.button else { return }

        button.imagePosition = .imageOnly
        button.imageScaling = .scaleNone
        button.target = self
        button.action = #selector(togglePopover)
        button.sendAction(on: [.leftMouseUp])

        let contentController = NSHostingController(
            rootView: PlayerPopoverView(store: store)
        )
        let playerPanel = PlayerPanel(contentViewController: contentController)
        configurePanelAppearance(
            playerPanel,
            contentView: contentController.view
        )

        self.statusItem = statusItem
        self.playerPanel = playerPanel
        renderer = MenuBarStatusRenderer(
            statusItem: statusItem,
            store: store
        ) { [weak self] in
            self?.togglePopover()
        }
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(playerPanelContentSizeDidChange),
            name: .playerPanelContentSizeDidChange,
            object: nil
        )
        installSettingsHotKeyHandler()
        store.start()
    }

    func applicationWillTerminate(_ notification: Notification) {
        renderer?.invalidate()
        removeEventMonitors()
        NotificationCenter.default.removeObserver(self)
        unregisterSettingsHotKey()
        removeSettingsHotKeyHandler()
    }

    @objc
    private func togglePopover() {
        guard let button = statusItem?.button,
              let playerPanel else {
            return
        }

        if playerPanel.isVisible {
            closePlayerPanel()
        } else {
            showPlayerPanel(playerPanel, below: button)
        }
    }

    private func configurePanelAppearance(
        _ panel: PlayerPanel,
        contentView: NSView
    ) {
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.level = .popUpMenu
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .none
        panel.collectionBehavior = [
            .canJoinAllSpaces,
            .fullScreenAuxiliary,
            .transient,
            .ignoresCycle,
        ]

        contentView.wantsLayer = true
        contentView.layer?.backgroundColor = NSColor.clear.cgColor
        contentView.layer?.cornerRadius = PlayerPanelLayout.cornerRadius
        contentView.layer?.cornerCurve = .continuous
        contentView.layer?.masksToBounds = true
        contentView.layer?.borderWidth = 0.5
        contentView.layer?.borderColor = NSColor.black
            .withAlphaComponent(0.42)
            .cgColor
    }

    private func showPlayerPanel(
        _ panel: PlayerPanel,
        below button: NSStatusBarButton
    ) {
        layoutPlayerPanel(panel, below: button)
        panel.orderFrontRegardless()
        renderer?.setPanelVisible(true)
        NotificationCenter.default.post(
            name: .playerPanelDidShow,
            object: panel
        )
        installEventMonitors()
        registerSettingsHotKey()
    }

    private func layoutPlayerPanel(
        _ panel: PlayerPanel,
        below button: NSStatusBarButton
    ) {
        panel.contentView?.layoutSubtreeIfNeeded()
        let fittingSize = panel.contentView?.fittingSize
            ?? PlayerPanelLayout.defaultSize
        panel.setContentSize(
            NSSize(
                width: PlayerPanelLayout.defaultSize.width,
                height: max(
                    fittingSize.height,
                    PlayerPanelLayout.defaultSize.height
                )
            )
        )

        guard let buttonWindow = button.window else { return }
        let buttonFrameInWindow = button.convert(button.bounds, to: nil)
        let buttonFrameOnScreen = buttonWindow.convertToScreen(buttonFrameInWindow)
        let screen = buttonWindow.screen ?? NSScreen.main
        let visibleFrame = screen?.visibleFrame
            ?? buttonFrameOnScreen
        let origin = PlayerPanelLayout.origin(
            anchorFrame: buttonFrameOnScreen,
            panelSize: panel.frame.size,
            visibleFrame: visibleFrame,
            backingScale: screen?.backingScaleFactor ?? 1
        )

        panel.setFrameOrigin(origin)
    }

    @objc
    private func playerPanelContentSizeDidChange() {
        Task { @MainActor [weak self] in
            await Task.yield()
            self?.resizePlayerPanelIfVisible()
            try? await Task.sleep(for: .milliseconds(280))
            guard !Task.isCancelled else { return }
            self?.resizePlayerPanelIfVisible()
        }
    }

    private func resizePlayerPanelIfVisible() {
        guard let playerPanel,
              playerPanel.isVisible,
              let button = statusItem?.button else {
            return
        }
        layoutPlayerPanel(playerPanel, below: button)
    }

    private func closePlayerPanel() {
        playerPanel?.orderOut(nil)
        NotificationCenter.default.post(
            name: .playerPanelDidHide,
            object: playerPanel
        )
        renderer?.setPanelVisible(false)
        removeEventMonitors()
        unregisterSettingsHotKey()
    }

    private func installEventMonitors() {
        removeEventMonitors()

        localEventMonitor = NSEvent.addLocalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown]
        ) { [weak self] event in
            guard let self,
                  let panel = self.playerPanel,
                  panel.isVisible else {
                return event
            }

            // Keep the player visible while interacting with another Reprise
            // window, such as Settings. Clicks in other apps are handled by the
            // global monitor below.
            if event.window == nil {
                self.closePlayerPanel()
            }
            return event
        }

        globalEventMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown]
        ) { [weak self] _ in
            let screenLocation = NSEvent.mouseLocation
            Task { @MainActor in
                guard let self,
                      !self.isPointInsideRepriseWindow(screenLocation) else {
                    return
                }
                self.closePlayerPanel()
            }
        }

        workspaceActivationObserver = NSWorkspace.shared.notificationCenter
            .addObserver(
                forName: NSWorkspace.didActivateApplicationNotification,
                object: nil,
                queue: .main
            ) { [weak self] notification in
                guard let activatedApplication = notification.userInfo?[
                    NSWorkspace.applicationUserInfoKey
                ] as? NSRunningApplication,
                      PlayerPanelActivationPolicy.shouldDismiss(
                          activatedProcessIdentifier:
                              activatedApplication.processIdentifier,
                          repriseProcessIdentifier:
                              ProcessInfo.processInfo.processIdentifier
                      ) else {
                    return
                }

                Task { @MainActor [weak self] in
                    self?.closePlayerPanel()
                }
            }
    }

    private func isPointInsideRepriseWindow(_ point: NSPoint) -> Bool {
        let statusBarWindow = statusItem?.button?.window

        return NSApp.windows.contains { window in
            window !== statusBarWindow
                && window.isVisible
                && window.frame.contains(point)
        }
    }

    private func removeEventMonitors() {
        if let localEventMonitor {
            NSEvent.removeMonitor(localEventMonitor)
            self.localEventMonitor = nil
        }
        if let globalEventMonitor {
            NSEvent.removeMonitor(globalEventMonitor)
            self.globalEventMonitor = nil
        }
        if let workspaceActivationObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(
                workspaceActivationObserver
            )
            self.workspaceActivationObserver = nil
        }
    }

    private func installSettingsHotKeyHandler() {
        guard settingsHotKeyHandler == nil else { return }

        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: OSType(kEventHotKeyPressed)
        )
        let userData = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(
            GetApplicationEventTarget(),
            settingsHotKeyEventHandler,
            1,
            &eventType,
            userData,
            &settingsHotKeyHandler
        )
    }

    private func removeSettingsHotKeyHandler() {
        guard let settingsHotKeyHandler else { return }
        RemoveEventHandler(settingsHotKeyHandler)
        self.settingsHotKeyHandler = nil
    }

    private func registerSettingsHotKey() {
        guard settingsHotKey == nil else { return }

        var hotKey: EventHotKeyRef?
        let hotKeyID = EventHotKeyID(
            signature: settingsHotKeySignature,
            id: settingsHotKeyIdentifier
        )
        let status = RegisterEventHotKey(
            UInt32(kVK_ANSI_Comma),
            UInt32(cmdKey),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &hotKey
        )
        guard status == noErr else { return }
        settingsHotKey = hotKey
    }

    private func unregisterSettingsHotKey() {
        guard let settingsHotKey else { return }
        UnregisterEventHotKey(settingsHotKey)
        self.settingsHotKey = nil
    }

    fileprivate func openSettingsFromHotKey() {
        NSApp.activate(ignoringOtherApps: true)
        NotificationCenter.default.post(
            name: .openRepriseSettings,
            object: nil
        )
    }
}

private final class RepriseInstanceLock {
    private let fileDescriptor: Int32

    init?() {
        let lockURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("dev.junx.Reprise.instance.lock")
        let fileDescriptor = Darwin.open(
            lockURL.path,
            O_CREAT | O_RDWR,
            S_IRUSR | S_IWUSR
        )
        guard fileDescriptor >= 0 else { return nil }
        guard flock(fileDescriptor, LOCK_EX | LOCK_NB) == 0 else {
            Darwin.close(fileDescriptor)
            return nil
        }
        self.fileDescriptor = fileDescriptor
    }

    deinit {
        flock(fileDescriptor, LOCK_UN)
        Darwin.close(fileDescriptor)
    }
}

@MainActor
private final class PlayerPanel: NSPanel {
    init(contentViewController: NSViewController) {
        super.init(
            contentRect: NSRect(origin: .zero, size: PlayerPanelLayout.defaultSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        self.contentViewController = contentViewController
        isFloatingPanel = true
        becomesKeyOnlyIfNeeded = true
    }

    override var canBecomeKey: Bool {
        true
    }

    override var canBecomeMain: Bool {
        false
    }
}

enum PlayerPanelLayout {
    static let defaultSize = NSSize(width: 360, height: 140)
    static let cornerRadius: CGFloat = 16
    static let anchorSpacing: CGFloat = 5
    static let screenMargin: CGFloat = 6

    static func origin(
        anchorFrame: NSRect,
        panelSize: NSSize,
        visibleFrame: NSRect,
        backingScale: CGFloat
    ) -> NSPoint {
        let minimumX = visibleFrame.minX + screenMargin
        let maximumX = visibleFrame.maxX - panelSize.width - screenMargin
        let unclampedX = min(
            max(anchorFrame.minX, minimumX),
            max(minimumX, maximumX)
        )
        let x = floor(unclampedX * backingScale) / backingScale
        let y = anchorFrame.minY - panelSize.height - anchorSpacing
        return NSPoint(x: x, y: y.rounded())
    }
}

@MainActor
private final class MenuBarStatusRenderer: NSObject {
    private let statusItem: NSStatusItem
    private let store: NowPlayingStore
    private let marqueeView = MenuBarMarqueeView()
    private var lastContentKey = ""
    private var lastRenderedTitle: String?
    private var lastRenderedTrackKey: String?
    private var isPanelVisible = false

    init(
        statusItem: NSStatusItem,
        store: NowPlayingStore,
        onClick: @escaping () -> Void
    ) {
        self.statusItem = statusItem
        self.store = store
        super.init()

        guard let button = statusItem.button else { return }
        button.image = nil
        button.title = ""
        marqueeView.onClick = onClick
        marqueeView.onAppearanceChange = { [weak self] in
            self?.updateContent()
        }

        marqueeView.translatesAutoresizingMaskIntoConstraints = false
        button.addSubview(marqueeView)
        NSLayoutConstraint.activate([
            marqueeView.leadingAnchor.constraint(equalTo: button.leadingAnchor),
            marqueeView.trailingAnchor.constraint(equalTo: button.trailingAnchor),
            marqueeView.topAnchor.constraint(equalTo: button.topAnchor),
            marqueeView.bottomAnchor.constraint(equalTo: button.bottomAnchor),
        ])

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(refreshRequested(_:)),
            name: .displayedLyricDidChange,
            object: store
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(refreshRequested(_:)),
            name: UserDefaults.didChangeNotification,
            object: nil
        )

        store.onMenuBarContentChange = { [weak self] in
            self?.updateContent()
        }
        updateContent()
    }

    func invalidate() {
        NotificationCenter.default.removeObserver(self)
        store.onMenuBarContentChange = nil
        marqueeView.stopAnimation()
    }

    func setPanelVisible(_ isVisible: Bool) {
        isPanelVisible = isVisible
        let preferences = MarqueePreferences.current()
        marqueeView.setPaused(
            isVisible && preferences.resetsMenuTitleWhenPanelOpens
        )
    }

    @objc
    private func updateContent() {
        guard let button = statusItem.button else { return }

        store.ensureLyricsForActiveTrack()
        let snapshot = store.menuBarSnapshot
        let preferences = MarqueePreferences.current()
        let title = if preferences.menuBarTitleFormat == .hidden {
            ""
        } else if preferences.menuBarShowsLyrics,
                  let lyric = store.displayedLyricText {
            lyric
        } else {
            snapshot?.track.map {
                preferences.menuBarTitleFormat.text(
                    title: $0.title,
                    artist: $0.artist
                )
            } ?? store.menuBarTitle
        }
        let trackKey = Self.trackKey(for: snapshot)
        let transitionStyle = MenuBarTitleTransitionStyle.resolved(
            previousTitle: lastRenderedTitle,
            currentTitle: title,
            previousTrackKey: lastRenderedTrackKey,
            currentTrackKey: trackKey,
            isDisplayingLyrics:
                preferences.menuBarShowsLyrics
                && store.displayedLyricText != nil
                && preferences.menuBarTitleFormat != .hidden
        )
        let contentKey = Self.contentKey(
            title: title,
            snapshot: snapshot,
            preferences: preferences,
            appearanceName: button.effectiveAppearance.name.rawValue
        )
        guard contentKey != lastContentKey else { return }
        lastContentKey = contentKey

        let contentWidth = marqueeView.update(
            title: title,
            artworkData: snapshot?.track?.artworkData,
            symbolName: snapshot?.player.symbolName ?? "music.note",
            artworkStyle: preferences.menuBarArtworkStyle,
            isPlaying: snapshot?.state.isPlaying == true,
            reservesTextWidth:
                preferences.menuBarShowsLyrics
                && preferences.menuBarReservesLyricsWidth
                && preferences.menuBarTitleFormat != .hidden
                && snapshot?.track != nil,
            transitionStyle: transitionStyle,
            preferences: preferences
        )
        lastRenderedTitle = title
        lastRenderedTrackKey = trackKey
        marqueeView.setPaused(
            isPanelVisible
                && preferences.resetsMenuTitleWhenPanelOpens
        )
        button.setAccessibilityElement(true)
        button.setAccessibilityLabel(store.menuBarAccessibilityLabel)
        button.setAccessibilityTitle(store.menuBarAccessibilityLabel)
        button.toolTip = title.isEmpty
            ? store.menuBarAccessibilityLabel
            : title
        statusItem.length = contentWidth + MenuBarMarquee.horizontalPadding
    }

    @objc
    private func refreshRequested(_ notification: Notification) {
        updateContent()
    }

    private static func contentKey(
        title: String,
        snapshot: PlayerSnapshot?,
        preferences: MarqueePreferences,
        appearanceName: String
    ) -> String {
        guard let snapshot, let track = snapshot.track else {
            return [
                "none",
                appearanceName,
                title,
                String(preferences.automaticallyScrollsTitles),
                String(describing: preferences.pointsPerSecond),
                String(preferences.resetsMenuTitleWhenPanelOpens),
                preferences.menuBarArtworkStyle.rawValue,
                String(describing: preferences.menuBarLyricsWidth),
                String(preferences.menuBarReservesLyricsWidth),
                String(preferences.menuBarShowsLyrics),
                preferences.menuBarTitleFormat.rawValue,
            ].joined(separator: "|")
        }
        return [
            snapshot.player.rawValue,
            snapshot.state.rawValue,
            appearanceName,
            // Synced lyrics change independently of the track metadata.
            // Include the rendered title so every lyric line invalidates
            // the menu bar content.
            title,
            track.title,
            track.album,
            track.artist,
            String(track.artworkData?.count ?? 0),
            String(preferences.automaticallyScrollsTitles),
            String(describing: preferences.pointsPerSecond),
            String(preferences.resetsMenuTitleWhenPanelOpens),
            preferences.menuBarArtworkStyle.rawValue,
            String(describing: preferences.menuBarLyricsWidth),
            String(preferences.menuBarReservesLyricsWidth),
            String(preferences.menuBarShowsLyrics),
            preferences.menuBarTitleFormat.rawValue,
        ].joined(separator: "|")
    }

    private static func trackKey(
        for snapshot: PlayerSnapshot?
    ) -> String? {
        guard let snapshot, let track = snapshot.track else {
            return nil
        }
        return [
            snapshot.player.rawValue,
            track.title,
            track.album,
            track.artist,
            String(Int(track.duration.rounded())),
        ].joined(separator: "\u{0}")
    }
}

@MainActor
private final class MenuBarMarqueeView: NSView {
    var onClick: (() -> Void)?

    /// Called when the menu bar's appearance changes.
    ///
    /// Everything this view draws is baked into layer contents with a resolved
    /// colour, so a light-to-dark switch has to redraw rather than restyle.
    /// The renderer owns the values needed to rebuild, so it is asked to run
    /// the update again rather than this view caching its own arguments.
    var onAppearanceChange: (() -> Void)?

    private let artworkLayer = CALayer()
    private let artworkMaskLayer = CAShapeLayer()
    private let indicatorLayer = CALayer()
    private let indicatorBars = (0..<4).map { _ in CALayer() }
    private let textViewportLayer = CALayer()
    private let fadeMaskLayer = CAGradientLayer()
    private let scrollingLayer = CALayer()
    private let firstTitleLayer = CALayer()
    private let secondTitleLayer = CALayer()
    private var pendingAnimation: (
        distance: CGFloat,
        scale: CGFloat,
        pointsPerSecond: CGFloat
    )?
    private var marqueeStartedAt: CFTimeInterval?
    private var isPaused = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)

        wantsLayer = true
        layer?.masksToBounds = false

        artworkLayer.cornerRadius = 4
        artworkLayer.masksToBounds = true
        artworkLayer.contentsGravity = .resizeAspectFill
        layer?.addSublayer(artworkLayer)

        indicatorLayer.masksToBounds = false
        layer?.addSublayer(indicatorLayer)
        for bar in indicatorBars {
            bar.cornerRadius = 1
            indicatorLayer.addSublayer(bar)
        }

        textViewportLayer.masksToBounds = true
        layer?.addSublayer(textViewportLayer)
        textViewportLayer.addSublayer(scrollingLayer)

        firstTitleLayer.contentsGravity = .resize
        secondTitleLayer.contentsGravity = .resize
        scrollingLayer.addSublayer(firstTitleLayer)
        scrollingLayer.addSublayer(secondTitleLayer)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        self
    }

    override func mouseDown(with event: NSEvent) {
        onClick?()
    }

    /// Redraws when the menu bar switches between light and dark.
    ///
    /// The menu bar does not only follow the system appearance: in Light Mode
    /// it turns dark over a dark desktop picture, and back again. Each of those
    /// arrives here, and none of them changes the content the renderer would
    /// otherwise notice, so without this the item keeps whatever colour it was
    /// last baked with.
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        onAppearanceChange?()
    }

    /// Text colour that reads against the menu bar as it is right now.
    ///
    /// Resolved from this view's own effective appearance rather than the
    /// app's, because a status item takes the menu bar's appearance - which is
    /// what the item is actually drawn on, and which differs from the app's
    /// whenever the menu bar is darkened over a desktop picture.
    private var menuBarForegroundColor: NSColor {
        MenuBarMarquee.foregroundColor(for: effectiveAppearance)
    }

    /// Redraws the item and reports how wide it needs to be.
    ///
    /// The width has to be returned rather than derived from layout, because
    /// the status item's length is set by the caller and Auto Layout inside a
    /// status button does not drive it.
    ///
    /// Text width is capped differently for lyrics than for titles, since a
    /// lyric line is generally longer and the user sets its budget explicitly.
    ///
    /// - Parameters:
    ///   - title: Text to display.
    ///   - artworkData: Cover art, or `nil`.
    ///   - symbolName: Fallback SF Symbol when there is no cover.
    ///   - artworkStyle: Which leading visual to draw.
    ///   - isPlaying: Whether to animate the disc or meter.
    ///   - reservesTextWidth: Whether to hold the full text width even when
    ///     the text is shorter, so the item does not resize on every lyric.
    ///   - transitionStyle: How to animate the title change.
    ///   - preferences: Current appearance settings.
    /// - Returns: The width the status item should take.
    func update(
        title: String,
        artworkData: Data?,
        symbolName: String,
        artworkStyle: MenuBarArtworkStyle,
        isPlaying: Bool,
        reservesTextWidth: Bool,
        transitionStyle: MenuBarTitleTransitionStyle,
        preferences: MarqueePreferences
    ) -> CGFloat {
        let titleWidth = MenuBarMarquee.textWidth(title)
        let maximumTextWidth = preferences.menuBarShowsLyrics
            ? preferences.menuBarLyricsWidth
            : MenuBarMarquee.maximumTextWidth
        let viewportWidth = MenuBarMarquee.viewportWidth(
            for: titleWidth,
            reservesMaximumTextWidth: reservesTextWidth,
            maximumWidth: maximumTextWidth
        )
        let contentWidth = MenuBarMarquee.totalWidth(
            for: titleWidth,
            artworkStyle: artworkStyle,
            reservesMaximumTextWidth: reservesTextWidth,
            maximumWidth: maximumTextWidth
        )
        let availableHeight = max(bounds.height, NSStatusBar.system.thickness)
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        let foregroundColor = menuBarForegroundColor
        let titleBitmap = title.isEmpty
            ? nil
            : Self.titleBitmap(
                title,
                scale: scale,
                foregroundColor: foregroundColor
            )
        let titleHeight = titleBitmap?.pointSize.height ?? MenuBarMarquee.font.pointSize
        let titleY = MenuBarMarquee.titleOriginY(
            availableHeight: availableHeight,
            scale: scale
        )

        prepareTitleTransition(transitionStyle)
        firstTitleLayer.contents = titleBitmap?.image
        secondTitleLayer.contents = titleBitmap?.image
        firstTitleLayer.contentsScale = scale
        secondTitleLayer.contentsScale = scale

        artworkLayer.frame = CGRect(
            x: 0,
            y: (availableHeight - MenuBarMarquee.artworkSize) / 2,
            width: MenuBarMarquee.artworkSize,
            height: MenuBarMarquee.artworkSize
        )
        artworkLayer.contents = Self.artworkContents(
            data: artworkData,
            symbolName: symbolName,
            foregroundColor: foregroundColor
        )
        updateLeadingVisual(
            style: artworkStyle,
            availableHeight: availableHeight,
            isPlaying: isPlaying,
            foregroundColor: foregroundColor
        )

        let textOrigin = MenuBarMarquee.leadingVisualWidth(
            for: artworkStyle,
            titleWidth: titleWidth
        )
        textViewportLayer.frame = CGRect(
            x: textOrigin,
            y: 0,
            width: viewportWidth,
            height: availableHeight
        )
        MarqueeFade.update(
            contentLayer: textViewportLayer,
            maskLayer: fadeMaskLayer,
            size: CGSize(
                width: viewportWidth,
                height: availableHeight
            ),
            showsFade: MenuBarMarquee.requiresScrolling(
                titleWidth: titleWidth,
                maximumWidth: maximumTextWidth
            )
        )
        scrollingLayer.frame = CGRect(
            x: 0,
            y: 0,
            width: max(titleWidth * 2 + MenuBarMarquee.titleGap, viewportWidth),
            height: availableHeight
        )
        firstTitleLayer.frame = CGRect(
            x: 0,
            y: titleY,
            width: titleWidth,
            height: titleHeight
        )
        secondTitleLayer.frame = CGRect(
            x: titleWidth + MenuBarMarquee.titleGap,
            y: titleY,
            width: titleWidth,
            height: titleHeight
        )

        stopAnimation()
        if preferences.automaticallyScrollsTitles,
           MenuBarMarquee.requiresScrolling(
               titleWidth: titleWidth,
               maximumWidth: maximumTextWidth
           ) {
            pendingAnimation = (
                titleWidth + MenuBarMarquee.titleGap,
                scale,
                preferences.pointsPerSecond
            )
            if !isPaused {
                startAnimation(
                    distance: titleWidth + MenuBarMarquee.titleGap,
                    scale: scale,
                    pointsPerSecond: preferences.pointsPerSecond
                )
            }
        } else {
            pendingAnimation = nil
            secondTitleLayer.isHidden = true
        }

        return contentWidth
    }

    private func prepareTitleTransition(
        _ style: MenuBarTitleTransitionStyle
    ) {
        textViewportLayer.removeAnimation(
            forKey: "lyricsLineTransition"
        )
        guard style == .lyricsUpward else { return }

        let transition = CATransition()
        transition.type = .push
        transition.subtype = .fromBottom
        transition.duration = MenuBarMarquee.lyricsTransitionDuration
        transition.timingFunction = CAMediaTimingFunction(
            name: .easeInEaseOut
        )
        textViewportLayer.add(
            transition,
            forKey: "lyricsLineTransition"
        )
    }

    private func updateLeadingVisual(
        style: MenuBarArtworkStyle,
        availableHeight: CGFloat,
        isPlaying: Bool,
        foregroundColor: NSColor
    ) {
        artworkLayer.isHidden = style == .levelIndicator || style == .hidden
        indicatorLayer.isHidden = style != .levelIndicator

        switch style {
        case .albumArtwork:
            artworkLayer.mask = nil
            artworkLayer.cornerRadius = 4
            stopDiscAnimation()
            stopIndicatorAnimation()
        case .compactDisc:
            artworkLayer.cornerRadius = MenuBarMarquee.artworkSize / 2
            updateDiscMask()
            updateDiscAnimation(isPlaying: isPlaying)
            stopIndicatorAnimation()
        case .levelIndicator:
            artworkLayer.mask = nil
            stopDiscAnimation()
            layoutIndicator(
                availableHeight: availableHeight,
                foregroundColor: foregroundColor
            )
            updateIndicatorAnimation(isPlaying: isPlaying)
        case .hidden:
            artworkLayer.mask = nil
            stopDiscAnimation()
            stopIndicatorAnimation()
        }
    }

    private func updateDiscMask() {
        let bounds = CGRect(
            origin: .zero,
            size: CGSize(
                width: MenuBarMarquee.artworkSize,
                height: MenuBarMarquee.artworkSize
            )
        )
        let path = CGMutablePath()
        path.addEllipse(in: bounds)
        path.addEllipse(
            in: bounds.insetBy(
                dx: MenuBarMarquee.discHoleInset,
                dy: MenuBarMarquee.discHoleInset
            )
        )
        artworkMaskLayer.frame = bounds
        artworkMaskLayer.path = path
        artworkMaskLayer.fillRule = .evenOdd
        artworkMaskLayer.fillColor = NSColor.black.cgColor
        artworkLayer.mask = artworkMaskLayer
    }

    private func updateDiscAnimation(isPlaying: Bool) {
        guard isPlaying else {
            stopDiscAnimation()
            return
        }
        guard artworkLayer.animation(forKey: "discRotation") == nil else {
            return
        }

        let animation = CABasicAnimation(keyPath: "transform.rotation.z")
        animation.fromValue = 0
        animation.toValue = CGFloat.pi * 2
        animation.duration = MenuBarMarquee.discRotationDuration
        animation.repeatCount = .infinity
        animation.timingFunction = CAMediaTimingFunction(name: .linear)
        animation.isRemovedOnCompletion = false
        artworkLayer.add(animation, forKey: "discRotation")
    }

    private func stopDiscAnimation() {
        artworkLayer.removeAnimation(forKey: "discRotation")
        artworkLayer.setAffineTransform(.identity)
    }

    /// Positions the level meter's bars.
    ///
    /// Heights are deliberately uneven so the meter reads as a level display
    /// even when paused and still.
    ///
    /// - Parameters:
    ///   - availableHeight: Height of the menu bar item.
    ///   - foregroundColor: Colour resolved for the current menu bar.
    private func layoutIndicator(
        availableHeight: CGFloat,
        foregroundColor: NSColor
    ) {
        indicatorLayer.frame = CGRect(
            x: 0,
            y: (availableHeight - MenuBarMarquee.artworkSize) / 2,
            width: MenuBarMarquee.artworkSize,
            height: MenuBarMarquee.artworkSize
        )

        for (index, bar) in indicatorBars.enumerated() {
            let height = MenuBarMarquee.indicatorBarHeights[index]
            bar.backgroundColor = foregroundColor.cgColor
            bar.frame = CGRect(
                x: MenuBarMarquee.indicatorHorizontalInset
                    + CGFloat(index)
                    * (
                        MenuBarMarquee.indicatorBarWidth
                            + MenuBarMarquee.indicatorBarSpacing
                    ),
                y: (MenuBarMarquee.artworkSize - height) / 2,
                width: MenuBarMarquee.indicatorBarWidth,
                height: height
            )
        }
    }

    private func updateIndicatorAnimation(isPlaying: Bool) {
        guard isPlaying else {
            stopIndicatorAnimation()
            return
        }

        for (index, bar) in indicatorBars.enumerated() {
            guard bar.animation(forKey: "level") == nil else { continue }
            let animation = CAKeyframeAnimation(keyPath: "transform.scale.y")
            animation.values = [0.55, 1, 0.7, 0.4, 0.85, 0.55]
            animation.keyTimes = [0, 0.2, 0.4, 0.6, 0.82, 1]
            animation.duration = 0.68 + Double(index) * 0.07
            animation.beginTime = CACurrentMediaTime() + Double(index) * 0.06
            animation.repeatCount = .infinity
            animation.isRemovedOnCompletion = false
            bar.add(animation, forKey: "level")
        }
    }

    private func stopIndicatorAnimation() {
        for bar in indicatorBars {
            bar.removeAnimation(forKey: "level")
        }
    }

    func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.removeAnimation(forKey: "returnToStart")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = true
        marqueeStartedAt = nil
    }

    func setPaused(_ paused: Bool) {
        guard isPaused != paused else { return }
        isPaused = paused

        if paused {
            returnToStart()
            return
        }

        stopAnimation()
        guard let pendingAnimation else { return }
        startAnimation(
            distance: pendingAnimation.distance,
            scale: pendingAnimation.scale,
            pointsPerSecond: pendingAnimation.pointsPerSecond
        )
    }

    private func returnToStart() {
        let wasMoving = isMarqueeInMotion
        let currentX = CGFloat((
            scrollingLayer.presentation()?.value(
                forKeyPath: "transform.translation.x"
            ) as? NSNumber
        )?.doubleValue ?? 0)

        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.removeAnimation(forKey: "returnToStart")
        scrollingLayer.setAffineTransform(.identity)
        marqueeStartedAt = nil

        guard wasMoving, abs(currentX) > 0.5 else { return }

        let opacity = CAKeyframeAnimation(keyPath: "opacity")
        opacity.values = [1, 0, 0, 1]
        opacity.keyTimes = [0, 0.35, 0.45, 1]

        let translation = CAKeyframeAnimation(
            keyPath: "transform.translation.x"
        )
        translation.values = [
            currentX,
            currentX - MenuBarMarquee.returnGlideDistance,
            0,
            0,
        ]
        translation.keyTimes = [0, 0.4, 0.4001, 1]
        translation.calculationMode = .linear

        let transition = CAAnimationGroup()
        transition.animations = [opacity, translation]
        transition.duration = MenuBarMarquee.returnTransitionDuration
        transition.timingFunction = CAMediaTimingFunction(
            name: .easeOut
        )
        scrollingLayer.add(transition, forKey: "returnToStart")
    }

    private var isMarqueeInMotion: Bool {
        guard let marqueeStartedAt,
              let pendingAnimation else {
            return false
        }

        let distance = pendingAnimation.distance
        let travelDuration = TimeInterval(
            distance / pendingAnimation.pointsPerSecond
        )
        let cycleDuration = MenuBarMarquee.initialPause + travelDuration
        let elapsed = max(CACurrentMediaTime() - marqueeStartedAt, 0)
        let cycleTime = elapsed.truncatingRemainder(
            dividingBy: cycleDuration
        )
        return cycleTime > MenuBarMarquee.initialPause
    }

    private func startAnimation(
        distance: CGFloat,
        scale: CGFloat,
        pointsPerSecond: CGFloat
    ) {
        secondTitleLayer.isHidden = false
        scrollingLayer.add(
            PixelAlignedMarquee.animation(
                distance: distance,
                scale: scale,
                initialPause: MenuBarMarquee.initialPause,
                pointsPerSecond: pointsPerSecond
            ),
            forKey: "marquee"
        )
        marqueeStartedAt = CACurrentMediaTime()
    }

    private static func titleBitmap(
        _ title: String,
        scale: CGFloat,
        foregroundColor: NSColor
    ) -> (image: CGImage, pointSize: CGSize)? {
        let attributedTitle = NSAttributedString(
            string: title,
            attributes: [
                .font: MenuBarMarquee.font,
                .foregroundColor: foregroundColor,
                .kern: MenuBarMarquee.characterSpacing,
            ]
        )
        let line = CTLineCreateWithAttributedString(attributedTitle)
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var leading: CGFloat = 0
        let measuredWidth = CGFloat(
            CTLineGetTypographicBounds(
                line,
                &ascent,
                &descent,
                &leading
            )
        )
        let pointSize = CGSize(
            width: max(ceil(measuredWidth), 1),
            height: max(ceil(ascent + descent + leading), 1)
        )
        let pixelWidth = max(Int(ceil(pointSize.width * scale)), 1)
        let pixelHeight = max(Int(ceil(pointSize.height * scale)), 1)

        guard let context = CGContext(
            data: nil,
            width: pixelWidth,
            height: pixelHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return nil
        }

        context.scaleBy(x: scale, y: scale)
        context.textPosition = CGPoint(x: 0, y: descent)
        CTLineDraw(line, context)

        guard let image = context.makeImage() else { return nil }
        return (image, pointSize)
    }

    private static func artworkContents(
        data: Data?,
        symbolName: String,
        foregroundColor: NSColor
    ) -> CGImage? {
        if let data,
           let image = NSImage(data: data),
           let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            return cgImage
        }

        guard let symbol = NSImage(
            systemSymbolName: symbolName,
            accessibilityDescription: nil
        ),
              let glyph = symbol.cgImage(
                  forProposedRect: nil,
                  context: nil,
                  hints: nil
              ) else {
            return nil
        }

        guard let context = CGContext(
            data: nil,
            width: glyph.width,
            height: glyph.height,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return glyph
        }

        let bounds = CGRect(
            x: 0,
            y: 0,
            width: glyph.width,
            height: glyph.height
        )
        context.draw(glyph, in: bounds)
        context.setBlendMode(.sourceIn)
        context.setFillColor(foregroundColor.cgColor)
        context.fill(bounds)

        return context.makeImage() ?? glyph
    }
}

enum MenuBarMarquee {
    static let maximumTextWidth = CGFloat(
        MenuBarLyricsWidth.defaultValue
    )
    static let artworkSize: CGFloat = 18
    static let artworkTitleSpacing: CGFloat = 5
    static let discHoleInset: CGFloat = 7
    static let discRotationDuration: TimeInterval = 4
    static let indicatorBarWidth: CGFloat = 2
    static let indicatorBarSpacing: CGFloat = 2
    static let indicatorHorizontalInset: CGFloat = 2
    static let indicatorBarHeights: [CGFloat] = [8, 13, 10, 15]
    static let horizontalPadding: CGFloat = 8
    static let titleGap: CGFloat = 28
    static let initialPause: TimeInterval = 1.4
    static let pointsPerSecond: CGFloat = 30
    static let titleVerticalAdjustment: CGFloat = 0.5
    static let returnGlideDistance: CGFloat = 4
    static let returnTransitionDuration: TimeInterval = 0.24
    static let lyricsTransitionDuration: TimeInterval = 0.28
    static let font = NSFont.menuBarFont(ofSize: 0)
    static let characterSpacing: CGFloat = 0

    /// Resolves the colour the menu bar item must draw itself in.
    ///
    /// The item is drawn into `CALayer` contents, which are composited
    /// verbatim - unlike an `NSImage` marked as a template on the status
    /// button, nothing tints them afterwards. So the colour has to be resolved
    /// up front and baked into the bitmaps, and everything baked with it has
    /// to be rebuilt when the appearance changes.
    ///
    /// `labelColor` is used rather than a fixed black or white so the result
    /// tracks whatever the system considers legible on the menu bar, including
    /// the Light Mode case where a dark desktop picture darkens the bar.
    ///
    /// - Parameter appearance: Appearance to resolve against, normally the
    ///   status item view's effective appearance.
    /// - Returns: A concrete colour, safe to bake into a bitmap.
    static func foregroundColor(for appearance: NSAppearance) -> NSColor {
        var resolved = NSColor.labelColor

        appearance.performAsCurrentDrawingAppearance {
            resolved = NSColor.labelColor.usingColorSpace(.sRGB)
                ?? NSColor.labelColor
        }

        return resolved
    }

    /// Measures how wide text will draw in the menu bar font.
    ///
    /// - Parameter text: Text to measure.
    /// - Returns: Width in points, rounded up.
    static func textWidth(_ text: String) -> CGFloat {
        let attributedText = NSAttributedString(
            string: text,
            attributes: [
                .font: font,
                .kern: characterSpacing,
            ]
        )
        let line = CTLineCreateWithAttributedString(attributedText)
        return ceil(CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil)))
    }

    static func requiresScrolling(
        titleWidth: CGFloat,
        maximumWidth: CGFloat = maximumTextWidth
    ) -> Bool {
        titleWidth > maximumWidth
    }

    static func viewportWidth(
        for titleWidth: CGFloat,
        reservesMaximumTextWidth: Bool = false,
        maximumWidth: CGFloat = maximumTextWidth
    ) -> CGFloat {
        reservesMaximumTextWidth
            ? maximumWidth
            : min(titleWidth, maximumWidth)
    }

    static func leadingVisualWidth(
        for artworkStyle: MenuBarArtworkStyle,
        titleWidth: CGFloat
    ) -> CGFloat {
        guard artworkStyle != .hidden else { return 0 }
        return artworkSize + (titleWidth > 0 ? artworkTitleSpacing : 0)
    }

    static func totalWidth(
        for titleWidth: CGFloat,
        artworkStyle: MenuBarArtworkStyle = .albumArtwork,
        reservesMaximumTextWidth: Bool = false,
        maximumWidth: CGFloat = maximumTextWidth
    ) -> CGFloat {
        leadingVisualWidth(
            for: artworkStyle,
            titleWidth: titleWidth
        ) + viewportWidth(
            for: titleWidth,
            reservesMaximumTextWidth: reservesMaximumTextWidth,
            maximumWidth: maximumWidth
        )
    }

    static func titleOriginY(
        availableHeight: CGFloat,
        scale: CGFloat
    ) -> CGFloat {
        let descent = abs(font.descender)
        let visibleCenterFromBitmapBottom = descent + font.capHeight / 2
        let origin = availableHeight / 2 - visibleCenterFromBitmapBottom
        let pixelAlignedOrigin = (origin * scale).rounded() / scale
        return pixelAlignedOrigin + titleVerticalAdjustment
    }

    static func offset(elapsed: TimeInterval, titleWidth: CGFloat) -> CGFloat {
        guard requiresScrolling(titleWidth: titleWidth) else { return 0 }

        let travelDistance = titleWidth + titleGap
        let travelDuration = TimeInterval(travelDistance / pointsPerSecond)
        let cycleDuration = initialPause + travelDuration
        let cycleTime = max(elapsed, 0).truncatingRemainder(dividingBy: cycleDuration)

        guard cycleTime > initialPause else { return 0 }
        return -CGFloat(cycleTime - initialPause) * pointsPerSecond
    }
}
