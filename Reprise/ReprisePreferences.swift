//
//  ReprisePreferences.swift
//  Reprise
//

import CoreGraphics
import Foundation

enum ReprisePreferenceKey {
    static let automaticallyScrollTitles = "automaticallyScrollTitles"
    static let marqueeSpeed = "marqueeSpeed"
    static let menuBarArtworkStyle = "menuBarArtworkStyle"
    static let playerPanelTheme = "playerPanelTheme"
    static let resetsMenuTitleWhenPanelOpens = "resetsMenuTitleWhenPanelOpens"
}

enum MenuBarArtworkStyle: String, CaseIterable, Identifiable {
    case albumArtwork
    case compactDisc
    case levelIndicator
    case hidden

    var id: String {
        rawValue
    }

    var displayName: String {
        switch self {
        case .albumArtwork: "앨범"
        case .compactDisc: "CD"
        case .levelIndicator: "인디케이터"
        case .hidden: "없음"
        }
    }
}

enum PlayerPanelTheme: String, CaseIterable, Identifiable {
    case white
    case black
    case liquid
    case system

    var id: String {
        rawValue
    }

    var displayName: String {
        switch self {
        case .white: "화이트"
        case .black: "블랙"
        case .liquid: "Liquid"
        case .system: "시스템 설정"
        }
    }
}

enum MarqueeSpeed: Double, CaseIterable, Identifiable {
    case slow = 20
    case normal = 30
    case fast = 45

    var id: Double {
        rawValue
    }

    var displayName: String {
        switch self {
        case .slow: "느리게"
        case .normal: "보통"
        case .fast: "빠르게"
        }
    }
}

struct MarqueePreferences: Equatable {
    let automaticallyScrollsTitles: Bool
    let pointsPerSecond: CGFloat
    let resetsMenuTitleWhenPanelOpens: Bool
    let menuBarArtworkStyle: MenuBarArtworkStyle

    static func current(
        defaults: UserDefaults = .standard
    ) -> MarqueePreferences {
        return MarqueePreferences(
            automaticallyScrollsTitles: defaults.bool(
                forKey: ReprisePreferenceKey.automaticallyScrollTitles
            ),
            pointsPerSecond: CGFloat(
                defaults.double(
                    forKey: ReprisePreferenceKey.marqueeSpeed
                )
            ),
            resetsMenuTitleWhenPanelOpens: defaults.bool(
                forKey: ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens
            ),
            menuBarArtworkStyle: MenuBarArtworkStyle(
                rawValue: defaults.string(
                    forKey: ReprisePreferenceKey.menuBarArtworkStyle
                ) ?? ""
            ) ?? .albumArtwork
        )
    }
}

enum ReprisePreferences {
    static func registerDefaults(
        in defaults: UserDefaults = .standard
    ) {
        defaults.register(
            defaults: [
                ReprisePreferenceKey.automaticallyScrollTitles: true,
                ReprisePreferenceKey.marqueeSpeed: MarqueeSpeed.normal.rawValue,
                ReprisePreferenceKey.menuBarArtworkStyle:
                    MenuBarArtworkStyle.albumArtwork.rawValue,
                ReprisePreferenceKey.playerPanelTheme:
                    PlayerPanelTheme.liquid.rawValue,
                ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens: true,
            ]
        )
    }
}
