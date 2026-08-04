// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import Foundation

enum RepriseDemoMode {
    nonisolated static func isEnabled(
        arguments: [String] = ProcessInfo.processInfo.arguments,
        environment: [String: String] = ProcessInfo.processInfo.environment
    ) -> Bool {
        arguments.contains("--demo")
            || environment["REPRISE_DEMO_MODE"] == "1"
    }
}

@MainActor
enum RepriseDemoContent {
    static let player = MediaPlayerKind.youtubeMusic
    static let title = "Midnight Signal"
    static let album = "Demo Sessions"
    static let artist = "Reprise Studio"
    static let duration: TimeInterval = 214
    static let position: TimeInterval = 78
    static let volume = 68

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
            // Keep the promotional frame stable while the play icon and
            // rotating-disc treatment still render as active playback.
            playbackRate: 0,
            errorMessage: nil
        )
    }

    private static let artworkData = makeArtworkData()

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
