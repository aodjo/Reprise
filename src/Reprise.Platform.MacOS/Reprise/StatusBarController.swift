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

/// Four-character code identifying Reprise's hot key registration.
///
/// Carbon hot keys are process-global, so the signature is what keeps this
/// registration distinct from any other in the process.
private let settingsHotKeySignature: OSType = 0x5250_5253

/// Identifier distinguishing the Settings hot key from any future one.
private let settingsHotKeyIdentifier: UInt32 = 1

/// How a menu bar title change should be animated.
enum MenuBarTitleTransitionStyle: Equatable {
    /// Replace the text with no animation.
    case immediate

    /// Push the new line up from below, as a lyric advancing.
    case lyricsUpward

    /// Chooses the transition for a title change.
    ///
    /// The upward push is reserved for one line of lyrics giving way to the
    /// next within the same track. Every condition here rules out a case where
    /// it would mislead: animating a track change as a lyric advance would
    /// suggest continuity that is not there, and animating the first title
    /// would push in from an empty menu bar.
    ///
    /// - Parameters:
    ///   - previousTitle: Title currently shown, or `nil` if none.
    ///   - currentTitle: Title about to be shown.
    ///   - previousTrackKey: Track the previous title belonged to.
    ///   - currentTrackKey: Track the new title belongs to.
    ///   - isDisplayingLyrics: Whether the title is a lyric rather than a
    ///     track name.
    /// - Returns: The transition to use.
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

/// Decides whether another app activating should dismiss the player panel.
enum PlayerPanelActivationPolicy {
    /// Whether the panel should close because a different app came forward.
    ///
    /// Reprise's own windows are exempt, so opening Settings from the panel
    /// does not dismiss the panel that opened it.
    ///
    /// - Parameters:
    ///   - activatedProcessIdentifier: Process that just activated.
    ///   - repriseProcessIdentifier: Reprise's own process.
    /// - Returns: `true` when the panel should close.
    static func shouldDismiss(
        activatedProcessIdentifier: pid_t,
        repriseProcessIdentifier: pid_t
    ) -> Bool {
        activatedProcessIdentifier != repriseProcessIdentifier
    }
}

/// Carbon callback for the Command-comma hot key.
///
/// A C function pointer, so it cannot capture context: the delegate is passed
/// through Carbon's `userData` and recovered here. Taken unretained because
/// the delegate outlives the handler it installed.
///
/// The identity check matters because the handler is installed on the
/// application event target and sees every hot key press in the process, not
/// only Reprise's.
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

/// Owns the menu bar item, the player panel, and the app's lifecycle.
///
/// The panel is a borderless `NSPanel` rather than an `NSPopover` because it
/// has to stay open while the user interacts with other Reprise windows and
/// close on its own terms otherwise - behaviour a popover's dismissal model
/// does not allow. That choice is why dismissal is assembled by hand from
/// three separate monitors below.
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

    /// Builds the menu bar item and panel, and starts polling.
    ///
    /// Two guards come first. Under XCTest the app target is loaded by the
    /// test host, and proceeding would put a status item in the tester's menu
    /// bar and send Apple Events to their music apps. The instance lock then
    /// ensures only one Reprise runs: two would fight over the bridge port and
    /// each show their own menu bar item.
    ///
    /// - Parameter notification: Launch notification; unused.
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

    /// Tears down monitors and the Carbon hot key registration.
    ///
    /// Carbon hot keys and event handlers are process-global and not cleaned
    /// up automatically, so they are released explicitly.
    ///
    /// - Parameter notification: Termination notification; unused.
    func applicationWillTerminate(_ notification: Notification) {
        renderer?.invalidate()
        removeEventMonitors()
        NotificationCenter.default.removeObserver(self)
        unregisterSettingsHotKey()
        removeSettingsHotKeyHandler()
    }

    /// Shows or hides the player panel.
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

    /// Gives the panel its floating, rounded, shadowed appearance.
    ///
    /// The panel window is transparent and the rounding lives on the content
    /// layer, so the SwiftUI content is what gets clipped rather than the
    /// window. `canJoinAllSpaces` and `fullScreenAuxiliary` let it appear over
    /// a full-screen app, which a menu bar utility has to do; `.transient`
    /// keeps it out of Exposé and the window cycle.
    ///
    /// Animation is disabled because the panel is positioned manually and any
    /// implicit animation shows as a slide from the wrong place.
    ///
    /// - Parameters:
    ///   - panel: Panel to configure.
    ///   - contentView: Its hosted content view.
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

    /// Positions and shows the panel, then arms its dismissal.
    ///
    /// `orderFrontRegardless` is required because the panel is
    /// non-activating: an ordinary `orderFront` would not show it without
    /// bringing Reprise forward.
    ///
    /// - Parameters:
    ///   - panel: Panel to show.
    ///   - button: Status item button to anchor beneath.
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

    /// Sizes the panel to its content and places it under the status item.
    ///
    /// Width is fixed while height follows the content, so the panel grows for
    /// lyrics without changing shape. Placement is clamped to the screen's
    /// visible frame, which matters for a status item near the right edge
    /// where the panel would otherwise hang off screen.
    ///
    /// The x origin is floored to a device pixel: a half-pixel window origin
    /// makes the whole panel's text render blurred.
    ///
    /// - Parameters:
    ///   - panel: Panel to position.
    ///   - button: Status item button to anchor beneath.
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

    /// Re-sizes the panel after its SwiftUI content changes height.
    ///
    /// Resized twice on purpose. The yield catches the new size once SwiftUI
    /// has laid out, and the delayed second pass catches content that animates
    /// into place - the lyrics section expanding - where the fitting size
    /// keeps growing after the first measurement.
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

    /// Re-runs panel layout, but only while it is on screen.
    private func resizePlayerPanelIfVisible() {
        guard let playerPanel,
              playerPanel.isVisible,
              let button = statusItem?.button else {
            return
        }
        layoutPlayerPanel(playerPanel, below: button)
    }

    /// Hides the panel and disarms its dismissal machinery.
    ///
    /// The hot key is unregistered here so Command-comma only reaches Reprise
    /// while the panel is open, leaving it to other apps otherwise.
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

    /// Arms the three ways the panel gets dismissed.
    ///
    /// Three are needed because no single one covers every case. The local
    /// monitor sees clicks inside Reprise, where a `nil` window means the
    /// click landed on the menu bar or the desktop rather than one of our
    /// windows. The global monitor sees clicks in other apps, and checks the
    /// point against Reprise's windows because a click on Settings arrives
    /// there too. The workspace observer catches an app being activated
    /// without a click at all - through Command-Tab, or a launch.
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

    /// Whether a screen point lands on one of Reprise's own windows.
    ///
    /// The status bar window is excluded so clicking the menu bar item reaches
    /// the toggle rather than being read as a click inside the app, which
    /// would leave the panel open.
    ///
    /// - Parameter point: Point in screen coordinates.
    /// - Returns: `true` when the point is over a Reprise window.
    private func isPointInsideRepriseWindow(_ point: NSPoint) -> Bool {
        let statusBarWindow = statusItem?.button?.window

        return NSApp.windows.contains { window in
            window !== statusBarWindow
                && window.isVisible
                && window.frame.contains(point)
        }
    }

    /// Disarms every dismissal monitor.
    ///
    /// Called both on close and before re-arming, so monitors never stack up
    /// across repeated openings.
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

    /// Installs the Carbon handler that receives hot key presses.
    ///
    /// Installed once for the app's lifetime, separate from registering the
    /// key itself, which comes and goes with the panel. `passUnretained` is
    /// safe because the delegate outlives the handler.
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

    /// Removes the Carbon event handler.
    private func removeSettingsHotKeyHandler() {
        guard let settingsHotKeyHandler else { return }
        RemoveEventHandler(settingsHotKeyHandler)
        self.settingsHotKeyHandler = nil
    }

    /// Claims Command-comma while the panel is open.
    ///
    /// A Carbon hot key is system-wide, which is the only way to catch the
    /// shortcut for a non-activating panel that never becomes key. It is
    /// therefore registered only while the panel is visible, so Reprise does
    /// not hold Command-comma away from whatever app the user is in.
    ///
    /// A failed registration is ignored: another app may already hold the
    /// combination, and the gear button remains.
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

    /// Releases Command-comma back to the system.
    private func unregisterSettingsHotKey() {
        guard let settingsHotKey else { return }
        UnregisterEventHotKey(settingsHotKey)
        self.settingsHotKey = nil
    }

    /// Opens Settings in response to the hot key.
    ///
    /// Activation comes first because the panel is non-activating, so Reprise
    /// is not frontmost and the Settings window would otherwise open behind
    /// whatever the user is looking at.
    fileprivate func openSettingsFromHotKey() {
        NSApp.activate(ignoringOtherApps: true)
        NotificationCenter.default.post(
            name: .openRepriseSettings,
            object: nil
        )
    }
}

/// Holds an exclusive lock so only one Reprise runs at a time.
///
/// A file lock rather than a bundle-identifier check, because it is released
/// by the kernel when the process dies: a crashed Reprise leaves nothing
/// behind that would block the next launch.
private final class RepriseInstanceLock {
    private let fileDescriptor: Int32

    /// Acquires the lock, or fails when another instance holds it.
    ///
    /// `LOCK_NB` makes this fail immediately rather than wait, so a second
    /// launch terminates at once instead of hanging invisibly.
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

    /// Releases the lock and closes the descriptor.
    deinit {
        flock(fileDescriptor, LOCK_UN)
        Darwin.close(fileDescriptor)
    }
}

/// Borderless floating window hosting the player panel.
///
/// `nonactivatingPanel` is what lets the panel appear without pulling Reprise
/// to the front, so opening it does not take focus from the user's work.
@MainActor
private final class PlayerPanel: NSPanel {
    /// Creates the panel around its content controller.
    ///
    /// - Parameter contentViewController: Controller hosting the SwiftUI panel.
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

    /// Allows the panel to take key status.
    ///
    /// Overridden because a borderless window refuses key by default, which
    /// would leave the sliders and buttons inside unable to receive events.
    override var canBecomeKey: Bool {
        true
    }

    /// Keeps the panel from becoming the main window.
    ///
    /// Becoming main would make Reprise present as the active application,
    /// which is exactly what a non-activating panel exists to avoid.
    override var canBecomeMain: Bool {
        false
    }
}

/// Geometry for the player panel.
enum PlayerPanelLayout {
    /// Panel size before content is measured.
    static let defaultSize = NSSize(width: 360, height: 140)

    /// Corner rounding of the panel's content.
    static let cornerRadius: CGFloat = 16

    /// Gap between the status item and the panel's top edge.
    static let anchorSpacing: CGFloat = 5

    /// Minimum gap kept between the panel and the screen edges.
    static let screenMargin: CGFloat = 6

    /// Places the panel under its status item, kept on screen.
    ///
    /// Aligns the panel's left edge with the status item, then clamps it
    /// within the visible frame, which is what keeps a panel anchored near the
    /// right of the menu bar fully visible.
    ///
    /// The clamp is written so the lower bound wins on a screen too narrow to
    /// hold the panel: better to overflow the right edge, away from the menu
    /// bar's controls, than the left.
    ///
    /// The x origin is floored to a device pixel, since a fractional window
    /// origin blurs everything drawn inside it.
    ///
    /// - Parameters:
    ///   - anchorFrame: Status item frame in screen coordinates.
    ///   - panelSize: Size of the panel.
    ///   - visibleFrame: Usable area of the screen.
    ///   - backingScale: Display scale factor.
    /// - Returns: The panel's origin in screen coordinates.
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

/// Keeps the menu bar item's contents in step with the store.
///
/// The status item hosts a custom view rather than using its title and image,
/// because the menu bar has to show a scrolling title, a rotating disc, or an
/// animated level meter - none of which `NSStatusItem` provides.
///
/// Rebuilding that view is expensive, so every update is gated on a content
/// key: the store changes several times a second and almost none of those
/// changes alter what the menu bar draws.
@MainActor
private final class MenuBarStatusRenderer: NSObject {
    private let statusItem: NSStatusItem
    private let store: NowPlayingStore
    private let marqueeView = MenuBarMarqueeView()
    private var lastContentKey = ""
    private var lastRenderedTitle: String?
    private var lastRenderedTrackKey: String?
    private var isPanelVisible = false

    /// Installs the marquee view into the status item and starts observing.
    ///
    /// The button's own title and image are cleared, since the custom view
    /// draws everything; constraints pin it to the button so the status item's
    /// variable length governs the size.
    ///
    /// `UserDefaults.didChangeNotification` is observed so a settings change
    /// is reflected at once without a channel of its own.
    ///
    /// - Parameters:
    ///   - statusItem: Status item to render into.
    ///   - store: Store to read playback state from.
    ///   - onClick: Called when the item is clicked.
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

    /// Stops observing and animating, before termination.
    ///
    /// The store's callback is cleared explicitly because it holds a closure
    /// referencing this renderer.
    func invalidate() {
        NotificationCenter.default.removeObserver(self)
        store.onMenuBarContentChange = nil
        marqueeView.stopAnimation()
    }

    /// Notes whether the panel is open and pauses the marquee if wanted.
    ///
    /// The setting exists because a title scrolling in the menu bar while the
    /// same title sits still in the open panel is distracting.
    ///
    /// - Parameter isVisible: Whether the panel is on screen.
    func setPanelVisible(_ isVisible: Bool) {
        isPanelVisible = isVisible
        let preferences = MarqueePreferences.current()
        marqueeView.setPaused(
            isVisible && preferences.resetsMenuTitleWhenPanelOpens
        )
    }

    /// Rebuilds the menu bar item from the current state.
    ///
    /// The title resolves in priority order: hidden wins outright, then a
    /// lyric if lyrics are enabled and one is showing, then the formatted
    /// track title.
    ///
    /// The content key check is what makes this cheap enough to call as often
    /// as it is - it returns immediately unless something visible changed.
    ///
    /// The status item's length is set from the measured content width, since
    /// a variable-length item does not size itself to a hosted view.
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

    /// Rebuilds the item in response to a notification.
    ///
    /// - Parameter notification: Triggering notification; unused, since both
    ///   observed names lead to the same full rebuild.
    @objc
    private func refreshRequested(_ notification: Notification) {
        updateContent()
    }

    /// Builds a string that changes only when the menu bar's look does.
    ///
    /// Every input that affects rendering is folded in, and nothing else:
    /// position and volume are excluded because they change constantly and
    /// are not drawn. Artwork contributes only its byte count, which is enough
    /// to notice a different cover without hashing the image.
    ///
    /// - Parameters:
    ///   - title: Text about to be drawn.
    ///   - snapshot: Player state, or `nil`.
    ///   - preferences: Current appearance settings.
    ///   - appearanceName: Name of the menu bar's effective appearance. Part
    ///     of the key because the title and glyphs are baked with a colour
    ///     resolved from it, so a light-to-dark switch changes what is drawn
    ///     without changing anything else here.
    /// - Returns: A key to compare against the previous render.
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

    /// Identifies a track, for deciding whether a title change stayed within
    /// one song.
    ///
    /// Distinct from the content key: this ignores appearance settings and the
    /// rendered title, so it changes only when the actual track does. That is
    /// what lets a lyric advance be animated while a track change is not.
    ///
    /// - Parameter snapshot: Player state, or `nil`.
    /// - Returns: The track identity, or `nil` when no track is loaded.
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

/// The custom view drawn inside the menu bar item.
///
/// Draws a leading visual - cover, spinning disc, or level meter - beside a
/// title that scrolls when it overflows. Built from Core Animation layers
/// rather than `draw(_:)` so the animations run on the render server and cost
/// the app nothing per frame, which matters for a view that may animate
/// continuously for hours.
@MainActor
private final class MenuBarMarqueeView: NSView {
    /// Called when the item is clicked.
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

    /// Builds the layer tree.
    ///
    /// The title layers sit inside a clipping viewport layer, which is what
    /// bounds the scrolling text; the view's own layer must not clip, or the
    /// panel's shadow would be cut off.
    ///
    /// - Parameter frameRect: Initial frame.
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

    /// Unavailable; this view is never loaded from a nib.
    ///
    /// - Parameter coder: Unarchiver; unused.
    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    /// Claims every click within the view.
    ///
    /// Returns `self` unconditionally so a click anywhere - including the gaps
    /// between layers - opens the panel, rather than falling through to the
    /// status button and doing nothing.
    ///
    /// - Parameter point: Point being tested; unused.
    /// - Returns: Always this view.
    override func hitTest(_ point: NSPoint) -> NSView? {
        self
    }

    /// Opens the panel on click.
    ///
    /// - Parameter event: Mouse event; unused.
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

    /// Arms the push animation for a lyric line change.
    ///
    /// A `CATransition` on the viewport animates whatever its contents become
    /// next, so it is added before the new bitmap is assigned. Any previous
    /// transition is removed first, or a rapid change would stack them.
    ///
    /// - Parameter style: Transition to apply.
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

    /// Switches between the cover, disc, and meter treatments.
    ///
    /// Every branch stops the animations belonging to the other styles, so a
    /// style change cannot leave a rotation or a meter running on a hidden
    /// layer and burning frames.
    ///
    /// - Parameters:
    ///   - style: Visual to show.
    ///   - availableHeight: Height of the menu bar item.
    ///   - isPlaying: Whether the visual should animate.
    ///   - foregroundColor: Colour resolved for the current menu bar.
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

    /// Cuts the centre hole that makes the cover read as a record.
    ///
    /// Two ellipses with an even-odd fill rule: the outer one rounds the
    /// artwork and the inner one punches the spindle hole out of it.
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

    /// Spins the disc while playing.
    ///
    /// Left alone if already running, so the rotation continues smoothly
    /// across a track change instead of snapping back to zero.
    ///
    /// - Parameter isPlaying: Whether playback is advancing.
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

    /// Stops the disc and returns it upright.
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

    /// Animates the level meter while playing.
    ///
    /// Each bar gets a slightly different duration and start delay, so they
    /// drift out of phase and never resynchronise into an obvious loop. The
    /// meter is decorative - it reflects that audio is playing, not its actual
    /// level, which the players do not expose.
    ///
    /// - Parameter isPlaying: Whether playback is advancing.
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

    /// Freezes the level meter at its resting heights.
    private func stopIndicatorAnimation() {
        for bar in indicatorBars {
            bar.removeAnimation(forKey: "level")
        }
    }

    /// Halts the marquee and returns the title to its start.
    func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.removeAnimation(forKey: "returnToStart")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = true
        marqueeStartedAt = nil
    }

    /// Pauses or resumes scrolling as the panel opens and closes.
    ///
    /// Pausing rewinds through ``returnToStart()`` rather than freezing where
    /// it is, so reopening the panel does not leave the title stopped mid-word.
    ///
    /// - Parameter paused: Whether scrolling should stop.
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

    /// Rewinds a scrolled title with a short glide-and-fade.
    ///
    /// Snapping back would read as a glitch, so the title continues briefly in
    /// its direction of travel while fading out, then reappears at the start.
    /// The jump happens under cover of zero opacity, which is what the tightly
    /// spaced key times at 0.4 achieve.
    ///
    /// Skipped entirely when the title was resting in its initial pause, since
    /// there is nothing to rewind and the animation would be noise.
    ///
    /// The current offset is read from the presentation layer, the only place
    /// the in-flight animated value exists.
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

    /// Whether the title is currently moving rather than resting.
    ///
    /// Derived from elapsed time within the animation's cycle rather than read
    /// from Core Animation, which exposes no such state. Used to decide
    /// whether a rewind animation is worth playing at all.
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

    /// Starts the pixel-aligned scroll.
    ///
    /// The start time is recorded so ``isMarqueeInMotion`` can work out where
    /// in its cycle the animation is.
    ///
    /// - Parameters:
    ///   - distance: Travel distance in points.
    ///   - scale: Backing scale factor.
    ///   - pointsPerSecond: Scroll speed.
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

    /// Rasterises the menu bar title.
    ///
    /// The colour is baked into the bitmap and cannot adapt afterwards, since
    /// layer contents are composited as-is. It therefore has to be resolved
    /// for the menu bar's current appearance by the caller, and the bitmap
    /// rebuilt whenever that appearance changes.
    ///
    /// - Parameters:
    ///   - title: Text to draw.
    ///   - scale: Backing scale factor.
    ///   - foregroundColor: Colour resolved for the current menu bar.
    /// - Returns: The image and its point size, or `nil` if the context could
    ///   not be created.
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

    /// Resolves what to draw in the artwork slot.
    ///
    /// Falls back to the player's SF Symbol, so the slot is never empty and
    /// the item's width does not change when a track happens to have no cover.
    ///
    /// Real cover art is used as-is; the fallback symbol is tinted, because a
    /// symbol rasterises to a single opaque colour that would disappear
    /// against half the menu bar appearances. Tinting is done by drawing the
    /// glyph and filling through it with `sourceIn`, which keeps the fill only
    /// where the glyph is opaque.
    ///
    /// - Parameters:
    ///   - data: Cover art, or `nil`.
    ///   - symbolName: Fallback SF Symbol name.
    ///   - foregroundColor: Colour resolved for the current menu bar.
    /// - Returns: The image, or `nil` if neither could be produced. Falls back
    ///   to the untinted glyph if a drawing context cannot be created, since a
    ///   symbol in the wrong colour still beats an empty slot.
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

/// Metrics and measurement for the menu bar item.
///
/// Separated from the view so the width arithmetic - which decides how much of
/// the user's menu bar Reprise occupies - can be tested without AppKit.
enum MenuBarMarquee {
    /// Text width budget when showing track titles.
    static let maximumTextWidth = CGFloat(
        MenuBarLyricsWidth.defaultValue
    )

    /// Edge length of the leading visual.
    static let artworkSize: CGFloat = 18

    /// Gap between the leading visual and the title.
    static let artworkTitleSpacing: CGFloat = 5

    /// Inset of the disc's spindle hole.
    static let discHoleInset: CGFloat = 7

    /// Seconds for one full rotation of the disc.
    static let discRotationDuration: TimeInterval = 4

    /// Width of one level meter bar.
    static let indicatorBarWidth: CGFloat = 2

    /// Gap between level meter bars.
    static let indicatorBarSpacing: CGFloat = 2

    /// Inset before the first level meter bar.
    static let indicatorHorizontalInset: CGFloat = 2

    /// Resting heights of the meter bars, uneven so it reads as a level.
    static let indicatorBarHeights: [CGFloat] = [8, 13, 10, 15]

    /// Padding added around the item's content.
    static let horizontalPadding: CGFloat = 8

    /// Blank space between the two copies of a scrolling title.
    static let titleGap: CGFloat = 28

    /// How long a title rests before scrolling.
    static let initialPause: TimeInterval = 1.4

    /// Default scroll speed, used where no preference applies.
    static let pointsPerSecond: CGFloat = 30

    /// Nudge applied after pixel alignment.
    ///
    /// The menu bar's own text sits fractionally lower than a purely
    /// arithmetic centring puts it; this matches Reprise's title to the items
    /// beside it.
    static let titleVerticalAdjustment: CGFloat = 0.5

    /// How far a rewinding title glides before jumping back.
    static let returnGlideDistance: CGFloat = 4

    /// Duration of the rewind transition.
    static let returnTransitionDuration: TimeInterval = 0.24

    /// Duration of the lyric line push.
    static let lyricsTransitionDuration: TimeInterval = 0.28

    /// The system menu bar font, at its natural size.
    static let font = NSFont.menuBarFont(ofSize: 0)

    /// Extra tracking between characters; none, matching system text.
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

    /// Whether a title is too wide to show at once.
    ///
    /// - Parameters:
    ///   - titleWidth: Measured title width.
    ///   - maximumWidth: Budget. Defaults to ``maximumTextWidth``.
    /// - Returns: `true` when the title must scroll.
    static func requiresScrolling(
        titleWidth: CGFloat,
        maximumWidth: CGFloat = maximumTextWidth
    ) -> Bool {
        titleWidth > maximumWidth
    }

    /// How wide the text area should be.
    ///
    /// Reserving the full width keeps the item from resizing as each lyric
    /// line arrives, which would jitter every status item to its left.
    ///
    /// - Parameters:
    ///   - titleWidth: Measured title width.
    ///   - reservesMaximumTextWidth: Whether to always claim the full budget.
    ///     Defaults to `false`.
    ///   - maximumWidth: Budget. Defaults to ``maximumTextWidth``.
    /// - Returns: Width of the text viewport in points.
    static func viewportWidth(
        for titleWidth: CGFloat,
        reservesMaximumTextWidth: Bool = false,
        maximumWidth: CGFloat = maximumTextWidth
    ) -> CGFloat {
        reservesMaximumTextWidth
            ? maximumWidth
            : min(titleWidth, maximumWidth)
    }

    /// Width the leading visual occupies, including its trailing gap.
    ///
    /// The gap is dropped when there is no title, so an artwork-only item is
    /// not padded on its right.
    ///
    /// - Parameters:
    ///   - artworkStyle: Which visual is shown.
    ///   - titleWidth: Measured title width.
    /// - Returns: Width in points; 0 when the visual is hidden.
    static func leadingVisualWidth(
        for artworkStyle: MenuBarArtworkStyle,
        titleWidth: CGFloat
    ) -> CGFloat {
        guard artworkStyle != .hidden else { return 0 }
        return artworkSize + (titleWidth > 0 ? artworkTitleSpacing : 0)
    }

    /// Total content width of the menu bar item.
    ///
    /// - Parameters:
    ///   - titleWidth: Measured title width.
    ///   - artworkStyle: Which visual is shown. Defaults to album artwork.
    ///   - reservesMaximumTextWidth: Whether to always claim the full text
    ///     budget. Defaults to `false`.
    ///   - maximumWidth: Text budget. Defaults to ``maximumTextWidth``.
    /// - Returns: Width in points, before the status item's padding.
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

    /// Where the title bitmap should sit vertically.
    ///
    /// Centres the glyphs by cap height and descender rather than centring the
    /// bitmap box, then snaps to a device pixel and applies
    /// ``titleVerticalAdjustment`` so Reprise's title lines up with the system
    /// items beside it.
    ///
    /// - Parameters:
    ///   - availableHeight: Height of the menu bar item.
    ///   - scale: Backing scale factor.
    /// - Returns: The bitmap's y origin in points.
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

    /// Where a scrolling title would be after a given elapsed time.
    ///
    /// The animation itself runs on the render server, so this is not used to
    /// drive it; it expresses the same timing in a form that can be asserted
    /// against, covering the initial pause and the constant-speed travel.
    ///
    /// - Parameters:
    ///   - elapsed: Seconds since the scroll began.
    ///   - titleWidth: Measured title width.
    /// - Returns: The horizontal offset, 0 or negative. Always 0 for a title
    ///   that fits or during the initial pause.
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
