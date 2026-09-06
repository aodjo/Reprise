// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Foundation

/// Detects the demo mode used for screenshots and store assets.
enum RepriseDemoMode {
    /// Whether Reprise should show fabricated content instead of real players.
    ///
    /// Accepts either a launch argument or an environment variable so the mode
    /// can be turned on from an Xcode scheme, a UI test, and a shell command
    /// alike.
    ///
    /// - Parameters:
    ///   - arguments: Process arguments. Defaults to the real ones; injectable
    ///     for tests.
    ///   - environment: Process environment. Defaults to the real one;
    ///     injectable for tests.
    /// - Returns: `true` when demo mode is requested.
    nonisolated static func isEnabled(
        arguments: [String] = ProcessInfo.processInfo.arguments,
        environment: [String: String] = ProcessInfo.processInfo.environment
    ) -> Bool {
        arguments.contains("--demo")
            || environment["REPRISE_DEMO_MODE"] == "1"
    }
}

/// The fabricated track Reprise shows in demo mode.
///
/// Exists so marketing screenshots do not depend on whatever happens to be
/// playing, and can be reproduced exactly. Everything is invented: the track,
/// the artist, and the cover art, which is drawn at runtime rather than
/// shipped as an asset so no third-party artwork is ever distributed.
@MainActor
enum RepriseDemoContent {
    /// Player the demo track is attributed to.
    static let player = MediaPlayerKind.youtubeMusic

    /// Demo track title.
    static let title = "Midnight Signal"

    /// Demo album name.
    static let album = "Demo Sessions"

    /// Demo artist name.
    static let artist = "Reprise Studio"

    /// Demo track length in seconds.
    static let duration: TimeInterval = 214

    /// Playback position the demo frame sits at, in seconds.
    ///
    /// Chosen to put the progress bar visibly past its start without
    /// approaching the end, so a screenshot reads as mid-playback.
    static let position: TimeInterval = 78

    /// Demo volume level.
    static let volume = 68

    /// Demo lyrics, spaced to line up with ``position``.
    ///
    /// Line timings are spread across ``duration`` so whichever moment a
    /// screenshot captures has a lyric showing.
    static let lyrics = SyncedLyrics(
        source: .lrclib,
        lines: [
            LyricLine(
                startTime: 0,
                text: "The city lights are keeping time"
            ),
            LyricLine(
                startTime: 29,
                text: "Every beat is right where it belongs"
            ),
            LyricLine(
                startTime: 58,
                text: "One place for every song you love"
            ),
            LyricLine(
                startTime: 87,
                text: "Stay in rhythm with Reprise"
            ),
            LyricLine(
                startTime: 116,
                text: "Your music, always within reach"
            ),
            LyricLine(
                startTime: 145,
                text: "Across every player, seamlessly"
            ),
            LyricLine(
                startTime: 174,
                text: "Let the moment keep on playing"
            ),
            LyricLine(startTime: 203, text: "Reprise"),
        ]
    )

    /// Builds a snapshot of the demo track.
    ///
    /// The playback rate is pinned to 0 so position estimation never advances
    /// the frame: the panel still renders as active playback - play icon,
    /// spinning disc - while the progress bar and times stay exactly where a
    /// screenshot needs them, however long the app is left open.
    ///
    /// - Parameters:
    ///   - state: Transport state to present. Defaults to `.playing`.
    ///   - requestedPosition: Position in seconds. Defaults to ``position``.
    ///   - requestedVolume: Volume level. Defaults to ``volume``.
    /// - Returns: A snapshot that looks like a real playing player.
    static func snapshot(
        state: PlaybackState = .playing,
        position requestedPosition: TimeInterval? = nil,
        volume requestedVolume: Int? = nil
    ) -> PlayerSnapshot {
        let position = requestedPosition ?? Self.position
        let volume = requestedVolume ?? Self.volume
        return PlayerSnapshot(
            player: player,
            isRunning: true,
            state: state,
            track: Track(
                title: title,
                album: album,
                artist: artist,
                duration: duration,
                position: position,
                artworkData: artworkData
            ),
            volume: volume,
            playbackRate: 0,
            errorMessage: nil
        )
    }

    /// Cover art for the demo track, rendered once on first use.
    private static let artworkData = makeArtworkData()

    /// Draws the demo cover art as a PNG.
    ///
    /// Generated rather than bundled so the app ships no artwork it does not
    /// own, and so the image scales to any size a screenshot needs. The
    /// composition is a diagonal gradient, two soft circles for depth, and a
    /// symmetric bar equaliser standing in for a waveform.
    ///
    /// - Returns: PNG data, or `nil` if the drawing context or gradient could
    ///   not be created, in which case ``ArtworkView`` falls back to its
    ///   placeholder.
    private static func makeArtworkData() -> Data? {
        let size = 512
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        guard let context = CGContext(
            data: nil,
            width: size,
            height: size,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: colorSpace,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return nil
        }

        let gradientColors = [
            NSColor(
                calibratedRed: 0.12,
                green: 0.06,
                blue: 0.32,
                alpha: 1
            ).cgColor,
            NSColor(
                calibratedRed: 0.37,
                green: 0.13,
                blue: 0.67,
                alpha: 1
            ).cgColor,
            NSColor(
                calibratedRed: 0.00,
                green: 0.66,
                blue: 0.78,
                alpha: 1
            ).cgColor,
        ] as CFArray
        guard let gradient = CGGradient(
            colorsSpace: colorSpace,
            colors: gradientColors,
            locations: [0, 0.56, 1]
        ) else {
            return nil
        }

        context.drawLinearGradient(
            gradient,
            start: CGPoint(x: 20, y: 20),
            end: CGPoint(x: 492, y: 492),
            options: []
        )

        context.setFillColor(
            NSColor.white.withAlphaComponent(0.09).cgColor
        )
        context.fillEllipse(
            in: CGRect(x: -80, y: 278, width: 310, height: 310)
        )
        context.fillEllipse(
            in: CGRect(x: 310, y: -90, width: 260, height: 260)
        )

        let heights: [CGFloat] = [72, 126, 176, 226, 176, 126, 72]
        let barWidth: CGFloat = 25
        let spacing: CGFloat = 15
        let totalWidth = CGFloat(heights.count) * barWidth
            + CGFloat(heights.count - 1) * spacing
        let startX = (CGFloat(size) - totalWidth) / 2

        context.saveGState()
        context.setShadow(
            offset: CGSize(width: 0, height: -5),
            blur: 18,
            color: NSColor.black.withAlphaComponent(0.28).cgColor
        )
        context.setFillColor(NSColor.white.withAlphaComponent(0.94).cgColor)
        for (index, height) in heights.enumerated() {
            let rect = CGRect(
                x: startX + CGFloat(index) * (barWidth + spacing),
                y: (CGFloat(size) - height) / 2,
                width: barWidth,
                height: height
            )
            context.addPath(
                CGPath(
                    roundedRect: rect,
                    cornerWidth: barWidth / 2,
                    cornerHeight: barWidth / 2,
                    transform: nil
                )
            )
            context.fillPath()
        }
        context.restoreGState()

        guard let image = context.makeImage() else { return nil }
        return NSBitmapImageRep(cgImage: image).representation(
            using: .png,
            properties: [:]
        )
    }
}
