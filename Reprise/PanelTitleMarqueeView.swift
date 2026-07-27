//
//  PanelTitleMarqueeView.swift
//  Reprise
//

import AppKit
import CoreText
import SwiftUI

struct PanelTitleMarqueeView: NSViewRepresentable {
    let title: String

    func makeNSView(context: Context) -> PanelTitleMarqueeNSView {
        PanelTitleMarqueeNSView()
    }

    func updateNSView(
        _ nsView: PanelTitleMarqueeNSView,
        context: Context
    ) {
        nsView.title = title
    }
}

@MainActor
final class PanelTitleMarqueeNSView: NSView {
    var title = "" {
        didSet {
            guard title != oldValue else { return }
            refresh(force: true)
        }
    }

    private let scrollingLayer = CALayer()
    private let firstTitleLayer = CALayer()
    private let secondTitleLayer = CALayer()
    private var renderedSize = CGSize.zero
    private var renderedScale: CGFloat = 0
    private var renderedTitle = ""

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

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        refresh(force: true)
    }

    @objc
    private func playerPanelDidShow() {
        refresh(force: true)
    }

    private func refresh(force: Bool) {
        guard bounds.width > 0, bounds.height > 0, !title.isEmpty else {
            stopAnimation()
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
        let bitmap = Self.titleBitmap(title, scale: scale)
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
        guard titleWidth > bounds.width else {
            secondTitleLayer.isHidden = true
            return
        }

        let distance = titleWidth + Self.titleGap
        scrollingLayer.add(
            PixelAlignedMarquee.animation(
                distance: distance,
                scale: scale,
                initialPause: Self.initialPause,
                pointsPerSecond: Self.pointsPerSecond
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
        scale: CGFloat
    ) -> (image: CGImage, pointSize: CGSize)? {
        let attributedTitle = NSAttributedString(
            string: title,
            attributes: [
                .font: font,
                .foregroundColor: NSColor.labelColor,
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
    private static let pointsPerSecond: CGFloat = 30
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
}
