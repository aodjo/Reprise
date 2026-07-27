//
//  PanelTitleMarqueeView.swift
//  Reprise
//

import AppKit
import CoreText
import SwiftUI

struct PanelTitleMarqueeView: NSViewRepresentable {
    let title: String
    let automaticallyScrolls: Bool
    let pointsPerSecond: CGFloat
    let foregroundColor: NSColor

    func makeNSView(context: Context) -> PanelTitleMarqueeNSView {
        PanelTitleMarqueeNSView()
    }

    func updateNSView(
        _ nsView: PanelTitleMarqueeNSView,
        context: Context
    ) {
        nsView.update(
            title: title,
            automaticallyScrolls: automaticallyScrolls,
            pointsPerSecond: pointsPerSecond,
            foregroundColor: foregroundColor
        )
    }
}

@MainActor
final class PanelTitleMarqueeNSView: NSView {
    private var title = ""
    private var automaticallyScrolls = true
    private var pointsPerSecond = CGFloat(MarqueeSpeed.normal.rawValue)
    private var foregroundColor = NSColor.labelColor

    private let scrollingLayer = CALayer()
    private let firstTitleLayer = CALayer()
    private let secondTitleLayer = CALayer()
    private let fadeMaskLayer = CAGradientLayer()
    private var renderedSize = CGSize.zero
    private var renderedScale: CGFloat = 0
    private var renderedTitle = ""
    private var isHovering = false
    private var titleTrackingArea: NSTrackingArea?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)

        wantsLayer = true
        layer?.masksToBounds = true

        firstTitleLayer.contentsGravity = .resize
        secondTitleLayer.contentsGravity = .resize
        scrollingLayer.addSublayer(firstTitleLayer)
        scrollingLayer.addSublayer(secondTitleLayer)
        layer?.addSublayer(scrollingLayer)

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(playerPanelDidShow),
            name: .playerPanelDidShow,
            object: nil
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(playerPanelDidHide),
            name: .playerPanelDidHide,
            object: nil
        )
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    override func layout() {
        super.layout()
        refresh(force: false)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()

        if let titleTrackingArea {
            removeTrackingArea(titleTrackingArea)
        }

        let trackingArea = NSTrackingArea(
            rect: .zero,
            options: [
                .mouseEnteredAndExited,
                .activeAlways,
                .inVisibleRect,
            ],
            owner: self
        )
        addTrackingArea(trackingArea)
        titleTrackingArea = trackingArea
    }

    override func mouseEntered(with event: NSEvent) {
        guard !automaticallyScrolls else { return }
        isHovering = true
        refresh(force: true)
    }

    override func mouseExited(with event: NSEvent) {
        guard isHovering else { return }
        isHovering = false
        refresh(force: true)
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        refresh(force: true)
    }

    func update(
        title: String,
        automaticallyScrolls: Bool,
        pointsPerSecond: CGFloat,
        foregroundColor: NSColor
    ) {
        guard title != self.title
                || automaticallyScrolls != self.automaticallyScrolls
                || pointsPerSecond != self.pointsPerSecond
                || foregroundColor != self.foregroundColor else {
            return
        }

        self.title = title
        self.automaticallyScrolls = automaticallyScrolls
        self.pointsPerSecond = pointsPerSecond
        self.foregroundColor = foregroundColor
        refresh(force: true)
    }

    @objc
    private func playerPanelDidShow() {
        refresh(force: true)
    }

    @objc
    private func playerPanelDidHide() {
        isHovering = false
        stopAnimation()
    }

    private func refresh(force: Bool) {
        guard bounds.width > 0, bounds.height > 0, !title.isEmpty else {
            stopAnimation()
            layer?.mask = nil
            firstTitleLayer.contents = nil
            secondTitleLayer.contents = nil
            return
        }

        let scale = window?.backingScaleFactor
            ?? NSScreen.main?.backingScaleFactor
            ?? 2
        let size = bounds.size
        guard force
                || title != renderedTitle
                || size != renderedSize
                || scale != renderedScale else {
            return
        }

        renderedTitle = title
        renderedSize = size
        renderedScale = scale

        let titleWidth = Self.textWidth(title)
        MarqueeFade.update(
            contentLayer: layer,
            maskLayer: fadeMaskLayer,
            size: bounds.size,
            showsFade: titleWidth > bounds.width
        )
        let bitmap = Self.titleBitmap(
            title,
            scale: scale,
            foregroundColor: foregroundColor
        )
        let titleHeight = bitmap?.pointSize.height ?? Self.font.pointSize
        let titleY = Self.titleOriginY(
            availableHeight: bounds.height,
            scale: scale
        )

        firstTitleLayer.contents = bitmap?.image
        secondTitleLayer.contents = bitmap?.image
        firstTitleLayer.contentsScale = scale
        secondTitleLayer.contentsScale = scale

        scrollingLayer.frame = CGRect(
            x: 0,
            y: 0,
            width: max(
                titleWidth * 2 + Self.titleGap,
                bounds.width
            ),
            height: bounds.height
        )
        firstTitleLayer.frame = CGRect(
            x: 0,
            y: titleY,
            width: titleWidth,
            height: titleHeight
        )
        secondTitleLayer.frame = CGRect(
            x: titleWidth + Self.titleGap,
            y: titleY,
            width: titleWidth,
            height: titleHeight
        )

        stopAnimation()
        guard titleWidth > bounds.width,
              automaticallyScrolls || isHovering else {
            secondTitleLayer.isHidden = true
            return
        }

        let distance = titleWidth + Self.titleGap
        let initialPause = automaticallyScrolls
            ? Self.initialPause
            : Self.hoverInitialPause
        scrollingLayer.add(
            PixelAlignedMarquee.animation(
                distance: distance,
                scale: scale,
                initialPause: initialPause,
                pointsPerSecond: pointsPerSecond
            ),
            forKey: "marquee"
        )
    }

    private func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = false
    }

    private static func textWidth(_ text: String) -> CGFloat {
        let attributedText = NSAttributedString(
            string: text,
            attributes: [.font: font]
        )
        let line = CTLineCreateWithAttributedString(attributedText)
        return ceil(CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil)))
    }

    private static func titleOriginY(
        availableHeight: CGFloat,
        scale: CGFloat
    ) -> CGFloat {
        let descent = abs(font.descender)
        let visibleCenterFromBitmapBottom = descent + font.capHeight / 2
        let origin = availableHeight / 2 - visibleCenterFromBitmapBottom
        return (origin * scale).rounded() / scale
    }

    private static func titleBitmap(
        _ title: String,
        scale: CGFloat,
        foregroundColor: NSColor
    ) -> (image: CGImage, pointSize: CGSize)? {
        let attributedTitle = NSAttributedString(
            string: title,
            attributes: [
                .font: font,
                .foregroundColor: foregroundColor,
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

    private static let font = NSFont.systemFont(
        ofSize: NSFont.systemFontSize,
        weight: .semibold
    )
    private static let titleGap: CGFloat = 24
    private static let initialPause: TimeInterval = 1.4
    private static let hoverInitialPause: TimeInterval = 0.25
}

enum MarqueeFade {
    static let width: CGFloat = 14

    static func startLocation(
        viewportWidth: CGFloat,
        fadeWidth: CGFloat = width
    ) -> CGFloat {
        guard viewportWidth > 0 else { return 0 }
        return max(0, 1 - fadeWidth / viewportWidth)
    }

    static func update(
        contentLayer: CALayer?,
        maskLayer: CAGradientLayer,
        size: CGSize,
        showsFade: Bool
    ) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        defer { CATransaction.commit() }

        guard showsFade, size.width > 0, size.height > 0 else {
            contentLayer?.mask = nil
            return
        }

        maskLayer.frame = CGRect(origin: .zero, size: size)
        maskLayer.startPoint = CGPoint(x: 0, y: 0.5)
        maskLayer.endPoint = CGPoint(x: 1, y: 0.5)
        maskLayer.colors = [
            NSColor.black.cgColor,
            NSColor.black.cgColor,
            NSColor.clear.cgColor,
        ]
        maskLayer.locations = [
            0,
            NSNumber(
                value: startLocation(
                    viewportWidth: size.width
                )
            ),
            1,
        ]
        contentLayer?.mask = maskLayer
    }
}

enum PixelAlignedMarquee {
    static func animation(
        distance: CGFloat,
        scale: CGFloat,
        initialPause: TimeInterval,
        pointsPerSecond: CGFloat
    ) -> CAKeyframeAnimation {
        let travelDuration = TimeInterval(distance / pointsPerSecond)
        let totalDuration = initialPause + travelDuration
        let pauseRatio = NSNumber(value: initialPause / totalDuration)
        let stepCount = max(Int(ceil(distance * scale)), 1)
        var values: [CGFloat] = [0, 0]
        var keyTimes: [NSNumber] = [0, pauseRatio]

        values.reserveCapacity(stepCount + 2)
        keyTimes.reserveCapacity(stepCount + 2)

        for step in 1...stepCount {
            let traveled = min(CGFloat(step) / scale, distance)
            let elapsed = initialPause
                + TimeInterval(traveled / pointsPerSecond)
            values.append(-traveled)
            keyTimes.append(NSNumber(value: elapsed / totalDuration))
        }

        let animation = CAKeyframeAnimation(
            keyPath: "transform.translation.x"
        )
        animation.values = values
        animation.keyTimes = keyTimes
        animation.duration = totalDuration
        animation.repeatCount = .infinity
        animation.calculationMode = .discrete
        animation.isRemovedOnCompletion = false
        return animation
    }
}

extension Notification.Name {
    static let playerPanelDidShow = Notification.Name(
        "dev.junx.Reprise.playerPanelDidShow"
    )
    static let playerPanelDidHide = Notification.Name(
        "dev.junx.Reprise.playerPanelDidHide"
    )
}
