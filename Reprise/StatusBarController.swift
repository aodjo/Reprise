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
    private let popover = NSPopover()

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
        popover.contentViewController = contentController
        popover.contentSize = NSSize(width: 360, height: 140)
        popover.behavior = .transient
        popover.animates = false

        self.statusItem = statusItem
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
    }

    @objc
    private func togglePopover() {
        guard let button = statusItem?.button else { return }

        if popover.isShown {
            popover.performClose(nil)
        } else {
            popover.show(
                relativeTo: button.bounds,
                of: button,
                preferredEdge: .minY
            )
        }
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
            startAnimation(
                distance: titleWidth + MenuBarMarquee.titleGap,
                scale: scale
            )
        } else {
            secondTitleLayer.isHidden = true
        }

        return contentWidth
    }

    func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = false
    }

    private func startAnimation(
        distance: CGFloat,
        scale: CGFloat
    ) {
        let travelDuration = TimeInterval(distance / MenuBarMarquee.pointsPerSecond)
        let totalDuration = MenuBarMarquee.initialPause + travelDuration
        let pauseRatio = NSNumber(value: MenuBarMarquee.initialPause / totalDuration)
        let stepCount = max(Int(ceil(distance * scale)), 1)
        var values: [CGFloat] = [0, 0]
        var keyTimes: [NSNumber] = [0, pauseRatio]

        values.reserveCapacity(stepCount + 2)
        keyTimes.reserveCapacity(stepCount + 2)

        for step in 1...stepCount {
            let traveled = min(CGFloat(step) / scale, distance)
            let elapsed = MenuBarMarquee.initialPause
                + TimeInterval(traveled / MenuBarMarquee.pointsPerSecond)
            values.append(-traveled)
            keyTimes.append(NSNumber(value: elapsed / totalDuration))
        }

        let animation = CAKeyframeAnimation(keyPath: "transform.translation.x")
        animation.values = values
        animation.keyTimes = keyTimes
        animation.duration = totalDuration
        animation.repeatCount = .infinity
        animation.calculationMode = .discrete
        animation.isRemovedOnCompletion = false
        scrollingLayer.add(animation, forKey: "marquee")
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
