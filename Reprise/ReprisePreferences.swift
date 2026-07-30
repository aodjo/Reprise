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
    static let menuBarTitleFormat = "menuBarTitleFormat"
    static let panelLeadingTimeStyle = "panelLeadingTimeStyle"
    static let panelTrailingTimeStyle = "panelTrailingTimeStyle"
    static let playerDisplayPriority = "playerDisplayPriority"
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

enum MenuBarTitleFormat: String, CaseIterable, Identifiable {
    case titleOnly
    case titleArtist
    case artistTitle
    case hidden

    var id: String {
        rawValue
    }

    var displayName: String {
        switch self {
        case .titleOnly: "제목만"
        case .titleArtist: "제목 - 아티스트"
        case .artistTitle: "아티스트 - 제목"
        case .hidden: "없음"
        }
    }

    func text(title: String, artist: String) -> String {
        let title = title.trimmingCharacters(in: .whitespacesAndNewlines)
        let artist = artist.trimmingCharacters(in: .whitespacesAndNewlines)

        switch self {
        case .titleOnly:
            return title.isEmpty ? artist : title
        case .titleArtist:
            return [title, artist]
                .filter { !$0.isEmpty }
                .joined(separator: " - ")
        case .artistTitle:
            return [artist, title]
                .filter { !$0.isEmpty }
                .joined(separator: " - ")
        case .hidden:
            return ""
        }
    }
}

enum PanelLeadingTimeStyle: String, CaseIterable, Identifiable {
    case elapsed
    case zero

    var id: String {
        rawValue
    }

    var displayName: String {
        switch self {
        case .elapsed: "현재 재생"
        case .zero: "00:00"
        }
    }
}

enum PanelTrailingTimeStyle: String, CaseIterable, Identifiable {
    case remaining
    case duration

    var id: String {
        rawValue
    }

    var displayName: String {
        switch self {
        case .remaining: "남은 시간"
        case .duration: "총 길이"
        }
    }
}

enum PanelTimeDisplay {
    static func leadingText(
        style: PanelLeadingTimeStyle,
        position: TimeInterval
    ) -> String {
        switch style {
        case .elapsed:
            timeString(position)
        case .zero:
            "00:00"
        }
    }

    static func trailingText(
        style: PanelTrailingTimeStyle,
        duration: TimeInterval,
        remaining: TimeInterval
    ) -> String {
        switch style {
        case .remaining:
            "-\(timeString(remaining))"
        case .duration:
            timeString(duration)
        }
    }

    private static func timeString(_ time: TimeInterval) -> String {
        guard time.isFinite, time > 0 else { return "0:00" }
        let totalSeconds = Int(time.rounded(.down))
        return "\(totalSeconds / 60):\(String(format: "%02d", totalSeconds % 60))"
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
    let menuBarTitleFormat: MenuBarTitleFormat

    static func current(
        defaults: UserDefaults = .standard
    ) -> MarqueePreferences {
        let artworkStyle = MenuBarArtworkStyle(
            rawValue: defaults.string(
                forKey: ReprisePreferenceKey.menuBarArtworkStyle
            ) ?? ""
        ) ?? .albumArtwork
        let titleFormat = MenuBarTitleFormat(
            rawValue: defaults.string(
                forKey: ReprisePreferenceKey.menuBarTitleFormat
            ) ?? ""
        ) ?? .titleOnly

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
            menuBarArtworkStyle:
                artworkStyle == .hidden && titleFormat == .hidden
                    ? .albumArtwork
                    : artworkStyle,
            menuBarTitleFormat: titleFormat
        )
    }
}

enum ReprisePreferences {
    static let defaultPlayerDisplayOrder = MediaPlayerKind.allCases
        .map(\.rawValue)
        .joined(separator: ",")

    static func playerDisplayOrder(
        in defaults: UserDefaults = .standard
    ) -> [MediaPlayerKind] {
        playerDisplayOrder(
            from: defaults.string(
                forKey: ReprisePreferenceKey.playerDisplayPriority
            )
        )
    }

    static func playerDisplayOrder(
        from rawValue: String?
    ) -> [MediaPlayerKind] {
        let savedPlayers = (rawValue ?? "")
            .split(separator: ",")
            .compactMap { MediaPlayerKind(rawValue: String($0)) }

        var result: [MediaPlayerKind] = []
        for player in savedPlayers + MediaPlayerKind.allCases
        where !result.contains(player) {
            result.append(player)
        }
        return result
    }

    static func serializedPlayerDisplayOrder(
        _ players: [MediaPlayerKind]
    ) -> String {
        let orderedPlayers = players + MediaPlayerKind.allCases.filter {
            !players.contains($0)
        }
        return orderedPlayers
            .map(\.rawValue)
            .joined(separator: ",")
    }

    static func registerDefaults(
        in defaults: UserDefaults = .standard
    ) {
        defaults.register(
            defaults: [
                ReprisePreferenceKey.automaticallyScrollTitles: true,
                ReprisePreferenceKey.marqueeSpeed: MarqueeSpeed.normal.rawValue,
                ReprisePreferenceKey.menuBarArtworkStyle:
                    MenuBarArtworkStyle.albumArtwork.rawValue,
                ReprisePreferenceKey.menuBarTitleFormat:
                    MenuBarTitleFormat.titleOnly.rawValue,
                ReprisePreferenceKey.panelLeadingTimeStyle:
                    PanelLeadingTimeStyle.elapsed.rawValue,
                ReprisePreferenceKey.panelTrailingTimeStyle:
                    PanelTrailingTimeStyle.remaining.rawValue,
                ReprisePreferenceKey.playerDisplayPriority:
                    defaultPlayerDisplayOrder,
                ReprisePreferenceKey.playerPanelTheme:
                    PlayerPanelTheme.liquid.rawValue,
                ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens: true,
            ]
        )
    }
}
