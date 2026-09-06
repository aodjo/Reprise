// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import AppKit
import SwiftUI

/// Square album cover, with a branded placeholder when no artwork exists.
///
/// Used at every size Reprise shows a cover, from the menu bar item to the
/// panel, so it always renders something: a track with no artwork still
/// occupies its slot instead of collapsing the surrounding layout.
struct ArtworkView: View {
    /// Encoded cover art, or `nil` to draw the placeholder.
    let data: Data?

    /// Edge length in points; the view is always square.
    let size: CGFloat

    /// Corner rounding, defaulting to the panel's card radius.
    var cornerRadius: CGFloat = 12

    /// SF Symbol drawn on the placeholder. Defaults to a generic music note.
    var symbolName: String = "music.note"

    /// Draws the cover, or an accent gradient carrying ``symbolName``.
    ///
    /// The whole view is collapsed into a single accessibility element: the
    /// artwork conveys nothing a screen reader user cannot get from the track
    /// title beside it, so exposing the gradient and symbol separately would
    /// add noise without information.
    var body: some View {
        Group {
            if let image = displayImage {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFill()
            } else {
                ZStack {
                    LinearGradient(
                        colors: [
                            Color.accentColor.opacity(0.68),
                            Color.accentColor.opacity(0.24),
                        ],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    )

                    Image(systemName: symbolName)
                        .font(.system(size: max(size * 0.32, 10), weight: .medium))
                        .foregroundStyle(.white.opacity(0.9))
                }
            }
        }
        .frame(width: size, height: size)
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
        .accessibilityRepresentation {
            Image(systemName: "photo")
                .accessibilityLabel("앨범 커버")
                .accessibilityValue(data == nil ? "기본 이미지" : "음악 앨범 이미지")
        }
    }

    /// Decodes ``data`` into an image sized for this view.
    ///
    /// The explicit `size` assignment is load-bearing: `MenuBarExtra` measures
    /// an `NSImage`'s intrinsic point size before SwiftUI applies view
    /// modifiers, so a high-resolution cover would widen the macOS status item
    /// itself. Stamping the intended point size up front keeps the menu bar
    /// item at a fixed width whatever the source resolution.
    ///
    /// - Returns: The decoded image, or `nil` when there is no data or it is
    ///   not a format AppKit can read.
    private var displayImage: NSImage? {
        guard let data, let image = NSImage(data: data) else { return nil }

        image.size = NSSize(width: size, height: size)
        return image
    }
}
