//
//  StatusBarController.swift
//  Reprise
//

import AppKit
import CoreText
import SwiftUI

@MainActor
final class RepriseAppDelegate: NSObject, NSApplicationDelegate {
    private let store = NowPlayingStore()
    private var statusItem: NSStatusItem?
    private var renderer: MenuBarStatusRenderer?
    private var playerPanel: PlayerPanel?
    private var localEventMonitor: Any?
    private var globalEventMonitor: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Unit-test hosts also load the app target. Avoid creating a status
        // item or sending Apple Events to the user's media apps during tests.
        guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else {
            return
        }

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
        store.start()
    }

    func applicationWillTerminate(_ notification: Notification) {
        renderer?.invalidate()
        removeEventMonitors()
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
        contentView.layer?.cornerRadius = PlayerPanelLayout.cornerRadius
        contentView.layer?.cornerCurve = .continuous
        contentView.layer?.masksToBounds = true
        contentView.layer?.borderWidth = 1
        contentView.layer?.borderColor = NSColor.separatorColor.cgColor
    }

    private func showPlayerPanel(
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
        panel.orderFrontRegardless()
        renderer?.setPanelVisible(true)
        NotificationCenter.default.post(
            name: .playerPanelDidShow,
            object: panel
        )
        installEventMonitors()
    }

    private func closePlayerPanel() {
        playerPanel?.orderOut(nil)
        renderer?.setPanelVisible(false)
        removeEventMonitors()
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

            if event.window !== panel,
               event.window !== self.statusItem?.button?.window {
                self.closePlayerPanel()
            }
            return event
        }

        globalEventMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.leftMouseDown, .rightMouseDown]
        ) { [weak self] _ in
            Task { @MainActor in
                self?.closePlayerPanel()
            }
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
    private var updateTimer: Timer?
    private var lastContentKey = ""

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

        marqueeView.translatesAutoresizingMaskIntoConstraints = false
        button.addSubview(marqueeView)
        NSLayoutConstraint.activate([
            marqueeView.leadingAnchor.constraint(equalTo: button.leadingAnchor),
            marqueeView.trailingAnchor.constraint(equalTo: button.trailingAnchor),
            marqueeView.topAnchor.constraint(equalTo: button.topAnchor),
            marqueeView.bottomAnchor.constraint(equalTo: button.bottomAnchor),
        ])

        let timer = Timer(
            timeInterval: 0.25,
            target: self,
            selector: #selector(updateContent),
            userInfo: nil,
            repeats: true
        )
        RunLoop.main.add(timer, forMode: .common)
        updateTimer = timer
        updateContent()
    }

    func invalidate() {
        updateTimer?.invalidate()
        updateTimer = nil
        marqueeView.stopAnimation()
    }

    func setPanelVisible(_ isVisible: Bool) {
        marqueeView.setPaused(isVisible)
    }

    @objc
    private func updateContent() {
        guard let button = statusItem.button else { return }

        let title = store.menuBarTitle
        let snapshot = store.menuBarSnapshot
        let contentKey = Self.contentKey(title: title, snapshot: snapshot)
        guard contentKey != lastContentKey else { return }
        lastContentKey = contentKey

        let contentWidth = marqueeView.update(
            title: title,
            artworkData: snapshot?.track?.artworkData,
            symbolName: snapshot?.player.symbolName ?? "music.note"
        )
        button.setAccessibilityElement(true)
        button.setAccessibilityLabel(store.menuBarAccessibilityLabel)
        button.setAccessibilityTitle(store.menuBarAccessibilityLabel)
        button.toolTip = title
        statusItem.length = contentWidth + MenuBarMarquee.horizontalPadding
    }

    private static func contentKey(
        title: String,
        snapshot: PlayerSnapshot?
    ) -> String {
        guard let snapshot, let track = snapshot.track else {
            return "none|\(title)"
        }
        return [
            snapshot.player.rawValue,
            track.title,
            track.album,
            track.artist,
            String(track.artworkData?.count ?? 0),
        ].joined(separator: "|")
    }
}

@MainActor
private final class MenuBarMarqueeView: NSView {
    var onClick: (() -> Void)?

    private let artworkLayer = CALayer()
    private let textViewportLayer = CALayer()
    private let scrollingLayer = CALayer()
    private let firstTitleLayer = CALayer()
    private let secondTitleLayer = CALayer()
    private var pendingAnimation: (distance: CGFloat, scale: CGFloat)?
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

    func update(
        title: String,
        artworkData: Data?,
        symbolName: String
    ) -> CGFloat {
        let titleWidth = MenuBarMarquee.textWidth(title)
        let viewportWidth = MenuBarMarquee.viewportWidth(for: titleWidth)
        let contentWidth = MenuBarMarquee.totalWidth(for: titleWidth)
        let availableHeight = max(bounds.height, NSStatusBar.system.thickness)
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        let titleBitmap = Self.titleBitmap(title, scale: scale)
        let titleHeight = titleBitmap?.pointSize.height ?? MenuBarMarquee.font.pointSize
        let titleY = MenuBarMarquee.titleOriginY(
            availableHeight: availableHeight,
            scale: scale
        )

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
            symbolName: symbolName
        )

        let textOrigin = MenuBarMarquee.artworkSize + MenuBarMarquee.artworkTitleSpacing
        textViewportLayer.frame = CGRect(
            x: textOrigin,
            y: 0,
            width: viewportWidth,
            height: availableHeight
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
        if MenuBarMarquee.requiresScrolling(titleWidth: titleWidth) {
            pendingAnimation = (
                titleWidth + MenuBarMarquee.titleGap,
                scale
            )
            if !isPaused {
                startAnimation(
                    distance: titleWidth + MenuBarMarquee.titleGap,
                    scale: scale
                )
            }
        } else {
            pendingAnimation = nil
            secondTitleLayer.isHidden = true
        }

        return contentWidth
    }

    func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.removeAnimation(forKey: "returnToStart")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = false
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
            scale: pendingAnimation.scale
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
            distance / MenuBarMarquee.pointsPerSecond
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
        scale: CGFloat
    ) {
        scrollingLayer.add(
            PixelAlignedMarquee.animation(
                distance: distance,
                scale: scale,
                initialPause: MenuBarMarquee.initialPause,
                pointsPerSecond: MenuBarMarquee.pointsPerSecond
            ),
            forKey: "marquee"
        )
        marqueeStartedAt = CACurrentMediaTime()
    }

    private static func titleBitmap(
        _ title: String,
        scale: CGFloat
    ) -> (image: CGImage, pointSize: CGSize)? {
        let attributedTitle = NSAttributedString(
            string: title,
            attributes: [
                .font: MenuBarMarquee.font,
                .foregroundColor: NSColor.white,
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
        symbolName: String
    ) -> CGImage? {
        if let data,
           let image = NSImage(data: data),
           let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            return cgImage
        }

        guard let symbol = NSImage(
            systemSymbolName: symbolName,
            accessibilityDescription: nil
        ) else {
            return nil
        }
        return symbol.cgImage(forProposedRect: nil, context: nil, hints: nil)
    }
}

enum MenuBarMarquee {
    static let maximumTextWidth: CGFloat = 170
    static let artworkSize: CGFloat = 18
    static let artworkTitleSpacing: CGFloat = 5
    static let horizontalPadding: CGFloat = 8
    static let titleGap: CGFloat = 28
    static let initialPause: TimeInterval = 1.4
    static let pointsPerSecond: CGFloat = 30
    static let titleVerticalAdjustment: CGFloat = 0.5
    static let returnGlideDistance: CGFloat = 4
    static let returnTransitionDuration: TimeInterval = 0.24
    static let font = NSFont.menuBarFont(ofSize: 0)

    static func textWidth(_ text: String) -> CGFloat {
        let attributedText = NSAttributedString(
            string: text,
            attributes: [.font: font]
        )
        let line = CTLineCreateWithAttributedString(attributedText)
        return ceil(CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil)))
    }

    static func requiresScrolling(titleWidth: CGFloat) -> Bool {
        titleWidth > maximumTextWidth
    }

    static func viewportWidth(for titleWidth: CGFloat) -> CGFloat {
        min(titleWidth, maximumTextWidth)
    }

    static func totalWidth(for titleWidth: CGFloat) -> CGFloat {
        artworkSize + artworkTitleSpacing + viewportWidth(for: titleWidth)
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
