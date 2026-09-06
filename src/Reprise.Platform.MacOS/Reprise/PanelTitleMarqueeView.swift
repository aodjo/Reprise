// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import CoreText
import SwiftUI

/// Scrolling track title for the player panel.
///
/// Bridges to AppKit because SwiftUI has no way to express what this needs:
/// a Core Animation keyframe track that steps in whole device pixels, so text
/// stays crisp while it moves. A SwiftUI `offset` animates on a continuous
/// timeline and lands on fractional pixels, which makes glyphs shimmer.
struct PanelTitleMarqueeView: NSViewRepresentable {
    /// Text to display.
    let title: String

    /// Whether an over-long title scrolls on its own, or only on hover.
    let automaticallyScrolls: Bool

    /// Scroll speed in points per second.
    let pointsPerSecond: CGFloat

    /// Text colour, supplied by the panel's theme.
    let foregroundColor: NSColor

    /// Creates the backing AppKit view.
    ///
    /// - Parameter context: Representable context; unused.
    /// - Returns: An empty marquee view, configured by the first update.
    func makeNSView(context: Context) -> PanelTitleMarqueeNSView {
        PanelTitleMarqueeNSView()
    }

    /// Pushes the current values into the AppKit view.
    ///
    /// - Parameters:
    ///   - nsView: View to update.
    ///   - context: Representable context; unused.
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

/// AppKit view that renders and scrolls the panel's track title.
///
/// The title is rasterised once into a bitmap and shown twice, side by side,
/// inside a layer that translates leftwards. When the first copy scrolls out,
/// the second is exactly where the first began, so the loop is seamless
/// without re-rendering anything per frame.
///
/// Everything is redrawn only when something that affects the rendering
/// actually changes - text, size, backing scale, or colour - because
/// rasterising text is far too expensive to repeat on every layout pass.
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

    /// Builds the layer tree and subscribes to panel visibility.
    ///
    /// The panel notifications matter because a menu bar popover is not
    /// deallocated when dismissed: without them the marquee would keep
    /// animating off screen, burning CPU for nothing.
    ///
    /// - Parameter frameRect: Initial frame.
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

    /// Unavailable; this view is never loaded from a nib.
    ///
    /// - Parameter coder: Unarchiver; unused.
    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    /// Unsubscribes from the panel visibility notifications.
    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    /// Re-renders when the view's geometry changes.
    ///
    /// Passes `force: false`, so a layout pass that did not change the size
    /// costs a comparison rather than a re-rasterisation.
    override func layout() {
        super.layout()
        refresh(force: false)
    }

    /// Rebuilds the hover tracking region.
    ///
    /// Uses `inVisibleRect`, which is why the rect passed in is `.zero`:
    /// AppKit recomputes it from the visible bounds, keeping the region
    /// correct as the panel resizes without needing an explicit rect here.
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

    /// Starts a hover-driven scroll.
    ///
    /// Ignored when the title already scrolls on its own, since there is
    /// nothing for hovering to trigger.
    ///
    /// - Parameter event: Mouse event; unused.
    override func mouseEntered(with event: NSEvent) {
        guard !automaticallyScrolls else { return }
        isHovering = true
        refresh(force: true)
    }

    /// Ends a hover-driven scroll and returns the title to its start.
    ///
    /// - Parameter event: Mouse event; unused.
    override func mouseExited(with event: NSEvent) {
        guard isHovering else { return }
        isHovering = false
        refresh(force: true)
    }

    /// Re-renders when the system appearance changes.
    ///
    /// The title is a bitmap baked with a resolved colour, so a light-to-dark
    /// switch would otherwise leave the old colour on screen.
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        refresh(force: true)
    }

    /// Applies new content and appearance from SwiftUI.
    ///
    /// Returns early when nothing changed, because SwiftUI calls
    /// `updateNSView` on every re-render of the surrounding panel - which
    /// happens once a second as the position ticks - and re-rasterising the
    /// title each time would restart the animation and stutter.
    ///
    /// - Parameters:
    ///   - title: Text to display.
    ///   - automaticallyScrolls: Whether to scroll without hovering.
    ///   - pointsPerSecond: Scroll speed.
    ///   - foregroundColor: Text colour.
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

    /// Restarts the marquee when the panel becomes visible.
    ///
    /// Forced, so the scroll begins from the start of the title each time the
    /// panel is opened rather than resuming mid-word.
    @objc
    private func playerPanelDidShow() {
        refresh(force: true)
    }

    /// Stops animating once the panel is hidden.
    ///
    /// Also clears the hover flag, since no exit event arrives for a pointer
    /// that was over the title when the panel closed.
    @objc
    private func playerPanelDidHide() {
        isHovering = false
        stopAnimation()
    }

    /// Rebuilds the rendered title and restarts the animation.
    ///
    /// The single path through which everything is updated. It bails out
    /// early when nothing that affects rendering has changed, since it runs
    /// on every layout pass.
    ///
    /// Scrolling only starts when the title genuinely overflows; a title that
    /// fits is left static and its duplicate hidden, so no second copy shows
    /// through the gap.
    ///
    /// - Parameter force: Re-render even when the inputs look unchanged. Used
    ///   for changes the cached comparison cannot see, such as a new colour or
    ///   an appearance switch.
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

    /// Halts the marquee and returns the title to its starting position.
    private func stopAnimation() {
        scrollingLayer.removeAnimation(forKey: "marquee")
        scrollingLayer.setAffineTransform(.identity)
        secondTitleLayer.isHidden = false
    }

    /// Measures how wide the title will draw.
    ///
    /// Uses Core Text's typographic bounds rather than an `NSAttributedString`
    /// size, so the measurement matches exactly what ``titleBitmap(_:scale:foregroundColor:)``
    /// draws with the same line. Any disagreement would show as a seam in the
    /// loop.
    ///
    /// - Parameter text: Text to measure.
    /// - Returns: Width in points, rounded up.
    private static func textWidth(_ text: String) -> CGFloat {
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

    /// Works out where the title bitmap should sit vertically.
    ///
    /// Centres the visual weight of the glyphs rather than the bitmap: a text
    /// bitmap includes ascender and descender space that most titles do not
    /// fill, so centring the box leaves the letters looking high. Positioning
    /// by cap height and descender instead makes the title look centred
    /// whether or not it happens to contain a descending letter.
    ///
    /// The result is snapped to a device pixel, since a half-pixel origin
    /// blurs the whole line.
    ///
    /// - Parameters:
    ///   - availableHeight: Height of the view in points.
    ///   - scale: Backing scale factor.
    /// - Returns: The bitmap's y origin in points.
    private static func titleOriginY(
        availableHeight: CGFloat,
        scale: CGFloat
    ) -> CGFloat {
        let descent = abs(font.descender)
        let visibleCenterFromBitmapBottom = descent + font.capHeight / 2
        let origin = availableHeight / 2 - visibleCenterFromBitmapBottom
        return (origin * scale).rounded() / scale
    }

    /// Rasterises the title into a bitmap at the display's scale.
    ///
    /// Drawing once into an image and animating that costs far less than
    /// letting Core Animation re-render text on every frame, which is what
    /// keeps a continuously scrolling title cheap.
    ///
    /// The context is scaled and the baseline placed at the descent, so the
    /// glyphs land inside the bitmap with their descenders intact.
    ///
    /// - Parameters:
    ///   - title: Text to draw.
    ///   - scale: Backing scale factor.
    ///   - foregroundColor: Text colour, resolved before drawing.
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
                .font: font,
                .foregroundColor: foregroundColor,
                .kern: characterSpacing,
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

    /// Font the panel title is drawn in.
    private static let font = NSFont.systemFont(
        ofSize: NSFont.systemFontSize,
        weight: .semibold
    )

    /// Extra tracking between characters; none, matching system text.
    private static let characterSpacing: CGFloat = 0

    /// Blank space between the two copies of the title.
    ///
    /// Also the extra distance the animation travels, so the loop reads as a
    /// pause between repetitions rather than the title running into itself.
    private static let titleGap: CGFloat = 24

    /// How long an auto-scrolling title rests before moving.
    ///
    /// Long enough to read the beginning of the title before it starts.
    private static let initialPause: TimeInterval = 1.4

    /// How long a hover-scrolling title rests before moving.
    ///
    /// Much shorter, because hovering is a deliberate request to see the rest
    /// and a long wait would feel unresponsive.
    private static let hoverInitialPause: TimeInterval = 0.25
}

/// Soft fade at the trailing edge of a scrolling title.
///
/// Shared by the panel and the menu bar so both taper text the same way,
/// rather than clipping it at a hard edge.
enum MarqueeFade {
    /// Width of the fade in points.
    static let width: CGFloat = 14

    /// Where the gradient begins, as a fraction of the viewport width.
    ///
    /// Expressed as a fraction because `CAGradientLayer` locations are
    /// normalised. Clamped at 0, since a viewport narrower than the fade
    /// would otherwise produce a negative location and fade the whole line
    /// out.
    ///
    /// - Parameters:
    ///   - viewportWidth: Visible width in points.
    ///   - fadeWidth: Fade width in points. Defaults to ``width``.
    /// - Returns: A location from 0 to 1.
    static func startLocation(
        viewportWidth: CGFloat,
        fadeWidth: CGFloat = width
    ) -> CGFloat {
        guard viewportWidth > 0 else { return 0 }
        return max(0, 1 - fadeWidth / viewportWidth)
    }

    /// Applies or removes the fade mask on a layer.
    ///
    /// The mask is removed entirely when the text fits, so a title that does
    /// not overflow is not needlessly dimmed at its end.
    ///
    /// Layer property changes are wrapped in a transaction with actions
    /// disabled, since Core Animation would otherwise implicitly animate the
    /// frame and colours - producing a visible sweep every time the title
    /// changes.
    ///
    /// - Parameters:
    ///   - contentLayer: Layer to mask.
    ///   - maskLayer: Gradient layer to reuse as the mask.
    ///   - size: Size of the viewport.
    ///   - showsFade: Whether the content overflows and needs fading.
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

/// Builds the marquee animation that moves in whole device pixels.
enum PixelAlignedMarquee {
    /// Creates the scrolling animation.
    ///
    /// A `CABasicAnimation` would interpolate continuously and land the text
    /// on fractional pixels, where the glyph rasteriser resamples it and the
    /// text visibly shimmers. This instead precomputes one keyframe per device
    /// pixel of travel and uses discrete calculation mode, so the title only
    /// ever sits on an exact pixel boundary and stays sharp throughout.
    ///
    /// The cost is a values array proportional to the distance - a few hundred
    /// entries for a typical title, built once per render rather than per
    /// frame.
    ///
    /// The pause is expressed as two identical leading keyframes, which holds
    /// the title still at the start of each repetition.
    ///
    /// - Parameters:
    ///   - distance: Total travel in points, being the title width plus its
    ///     trailing gap.
    ///   - scale: Backing scale factor, which sets the step size.
    ///   - initialPause: Seconds to hold before moving.
    ///   - pointsPerSecond: Scroll speed.
    /// - Returns: An infinitely repeating keyframe animation on
    ///   `transform.translation.x`.
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
    /// Posted when the player panel becomes visible.
    ///
    /// Lets marquee views restart from the beginning of their text. A popover
    /// is reused rather than rebuilt, so there is no view lifecycle callback
    /// that would serve.
    static let playerPanelDidShow = Notification.Name(
        "dev.junx.Reprise.playerPanelDidShow"
    )

    /// Posted when the player panel is dismissed.
    ///
    /// Lets marquee views stop animating while off screen.
    static let playerPanelDidHide = Notification.Name(
        "dev.junx.Reprise.playerPanelDidHide"
    )
}
