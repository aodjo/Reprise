//
//  ArtworkView.swift
//  Reprise
//

import AppKit
import SwiftUI

struct ArtworkView: View {
    let data: Data?
    let size: CGFloat
    var cornerRadius: CGFloat = 12
    var symbolName: String = "music.note"

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

    private var displayImage: NSImage? {
        guard let data, let image = NSImage(data: data) else { return nil }

        // MenuBarExtra measures an NSImage's intrinsic point size before SwiftUI
        // applies view modifiers. Give it the intended point size up front so a
        // high-resolution cover never expands the macOS status item.
        image.size = NSSize(width: size, height: size)
        return image
    }
}
