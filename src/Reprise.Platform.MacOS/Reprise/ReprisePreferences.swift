// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import CoreGraphics
import Foundation

/// `UserDefaults` keys for every Reprise setting.
///
/// Centralised so the settings pane, the status bar controller, and
/// `@AppStorage` declarations cannot drift apart on a spelling. Renaming any
/// of these discards the user's existing value, since nothing migrates old
/// keys.
enum ReprisePreferenceKey {
    /// Pause the previous player when another starts playing.
    static let automaticallyPausesOtherPlayer =
        "automaticallyPausesOtherPlayer"

    /// Scroll menu bar titles that are too long to fit.
    static let automaticallyScrollTitles = "automaticallyScrollTitles"

    /// Marquee scroll speed, stored as points per second.
    static let marqueeSpeed = "marqueeSpeed"

    /// Which artwork treatment the menu bar item uses.
    static let menuBarArtworkStyle = "menuBarArtworkStyle"

    /// Width in points reserved for lyrics in the menu bar.
    static let menuBarLyricsWidth = "menuBarLyricsWidth"

    /// Hold the lyrics width even when there is no lyric to show.
    static let menuBarReservesLyricsWidth = "menuBarReservesLyricsWidth"

    /// Show synced lyrics in the menu bar.
    static let menuBarShowsLyrics = "menuBarShowsLyrics"

    /// How the track title and artist are combined in the menu bar.
    static let menuBarTitleFormat = "menuBarTitleFormat"

    /// What the time on the left of the panel's progress bar shows.
    static let panelLeadingTimeStyle = "panelLeadingTimeStyle"

    /// What the time on the right of the panel's progress bar shows.
    static let panelTrailingTimeStyle = "panelTrailingTimeStyle"

    /// The player that was last seen playing, for restoring on launch.
    static let lastPlayedPlayer = "lastPlayedPlayer"

    /// Player display priority, stored as comma-separated raw values.
    static let playerDisplayPriority = "playerDisplayPriority"

    /// Colour theme for the player panel.
    static let playerPanelTheme = "playerPanelTheme"

    /// Restore the last playing player when Reprise launches.
    static let remembersLastPlayedPlayer = "remembersLastPlayedPlayer"

    /// Rewind the marquee to the start when the panel is opened.
    static let resetsMenuTitleWhenPanelOpens = "resetsMenuTitleWhenPanelOpens"
}

/// How the menu bar item represents the current track visually.
enum MenuBarArtworkStyle: String, CaseIterable, Identifiable {
    /// The album cover itself.
    case albumArtwork

    /// A spinning disc that turns while playing.
    case compactDisc

    /// An animated level meter.
    case levelIndicator

    /// No visual at all, leaving only the title.
    case hidden

    /// Stable identity for SwiftUI pickers.
    var id: String {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .albumArtwork: "앨범"
        case .compactDisc: "CD"
        case .levelIndicator: "인디케이터"
        case .hidden: "없음"
        }
    }
}

/// How the track title and artist are laid out in the menu bar.
enum MenuBarTitleFormat: String, CaseIterable, Identifiable {
    /// Title alone.
    case titleOnly

    /// Title, then artist.
    case titleArtist

    /// Artist, then title.
    case artistTitle

    /// No text, leaving only the artwork.
    case hidden

    /// Stable identity for SwiftUI pickers.
    var id: String {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .titleOnly: "제목만"
        case .titleArtist: "제목 - 아티스트"
        case .artistTitle: "아티스트 - 제목"
        case .hidden: "없음"
        }
    }

    /// Renders the menu bar text for a track.
    ///
    /// Blank components are dropped rather than rendered, so a track missing
    /// an artist never produces a dangling separator. ``titleOnly`` falls back
    /// to the artist when the title is blank, since showing nothing at all
    /// would read as Reprise having lost the track.
    ///
    /// - Parameters:
    ///   - title: Track title; surrounding whitespace is trimmed.
    ///   - artist: Artist credit; surrounding whitespace is trimmed.
    /// - Returns: The text to draw, or an empty string for ``hidden``.
    ///
    /// ## Example
    /// ```swift
    /// MenuBarTitleFormat.titleArtist.text(title: "Redshift", artist: "")
    /// // "Redshift"
    /// ```
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

/// What the time to the left of the panel's progress bar shows.
enum PanelLeadingTimeStyle: String, CaseIterable, Identifiable {
    /// Time elapsed in the track.
    case elapsed

    /// A fixed `00:00`, for users who only want the track length.
    case zero

    /// Stable identity for SwiftUI pickers.
    var id: String {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .elapsed: "현재 재생"
        case .zero: "00:00"
        }
    }
}

/// What the time to the right of the panel's progress bar shows.
enum PanelTrailingTimeStyle: String, CaseIterable, Identifiable {
    /// Time left, shown as a negative value.
    case remaining

    /// Total track length.
    case duration

    /// Stable identity for SwiftUI pickers.
    var id: String {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .remaining: "남은 시간"
        case .duration: "총 길이"
        }
    }
}

/// Formats the two times flanking the panel's progress bar.
enum PanelTimeDisplay {
    /// Renders the time to the left of the progress bar.
    ///
    /// - Parameters:
    ///   - style: The user's chosen leading style.
    ///   - position: Elapsed playback time in seconds.
    /// - Returns: The formatted time.
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

    /// Renders the time to the right of the progress bar.
    ///
    /// The remaining style is prefixed with a minus sign, matching how music
    /// players conventionally distinguish a countdown from a total.
    ///
    /// - Parameters:
    ///   - style: The user's chosen trailing style.
    ///   - duration: Total track length in seconds.
    ///   - remaining: Time left in seconds.
    /// - Returns: The formatted time.
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

    /// Formats seconds as `m:ss`.
    ///
    /// Rounds down rather than to nearest so the displayed time never reaches
    /// the track length before playback actually does. Non-finite and
    /// negative inputs collapse to `0:00`, since a player reporting garbage
    /// should not put `nan:aN` in the panel.
    ///
    /// - Parameter time: Duration in seconds.
    /// - Returns: The formatted string, or `0:00` for unusable input.
    private static func timeString(_ time: TimeInterval) -> String {
        guard time.isFinite, time > 0 else { return "0:00" }
        let totalSeconds = Int(time.rounded(.down))
        return "\(totalSeconds / 60):\(String(format: "%02d", totalSeconds % 60))"
    }
}

/// Colour treatment for the player panel.
enum PlayerPanelTheme: String, CaseIterable, Identifiable {
    /// Light panel.
    case white

    /// Dark panel.
    case black

    /// Translucent material that picks up the desktop behind it.
    case liquid

    /// Follow the system appearance.
    case system

    /// Stable identity for SwiftUI pickers.
    var id: String {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .white: "화이트"
        case .black: "다크"
        case .liquid: "Liquid"
        case .system: "시스템 설정"
        }
    }
}

/// Marquee scroll speed, in points per second.
///
/// Raw values are the actual speeds, so the stored preference stays
/// meaningful even if the presets are later renamed or extended.
enum MarqueeSpeed: Double, CaseIterable, Identifiable {
    /// 20 points per second.
    case slow = 20

    /// 30 points per second.
    case normal = 30

    /// 45 points per second.
    case fast = 45

    /// Stable identity for SwiftUI pickers, backed by the speed itself.
    var id: Double {
        rawValue
    }

    /// Label shown in the settings picker.
    var displayName: String {
        switch self {
        case .slow: "느리게"
        case .normal: "보통"
        case .fast: "빠르게"
        }
    }
}

/// Bounds for the lyrics area in the menu bar.
///
/// The menu bar is shared with every other app's status items, so this width
/// is capped: an unbounded lyric line would crowd them out or be truncated by
/// the system.
enum MenuBarLyricsWidth {
    /// Narrowest width that still fits a readable fragment.
    static let minimum = 80.0

    /// Widest width Reprise will claim in the menu bar.
    static let maximum = 300.0

    /// Width used until the user changes it.
    static let defaultValue = 170.0

    /// Slider increment in the settings pane.
    static let step = 10.0

    /// The full allowed range, for binding a slider.
    static let range = minimum...maximum

    /// Forces a stored width into the allowed range.
    ///
    /// Applied on read as well as on write, so a value left over from an
    /// older build with different bounds cannot produce an oversized item.
    ///
    /// - Parameter value: Width in points.
    /// - Returns: A width within ``range``.
    static func clamped(_ value: Double) -> CGFloat {
        CGFloat(min(max(value, minimum), maximum))
    }
}

/// Every menu bar appearance setting, read once as a unit.
///
/// The status bar item rebuilds from a single snapshot rather than reading
/// each key as it draws, so a settings change cannot be applied half-way and
/// leave the item in a combination the user never chose.
struct MarqueePreferences: Equatable {
    /// Whether over-long titles scroll.
    let automaticallyScrollsTitles: Bool

    /// Marquee speed in points per second.
    let pointsPerSecond: CGFloat

    /// Whether opening the panel rewinds the marquee.
    let resetsMenuTitleWhenPanelOpens: Bool

    /// Which artwork treatment to draw.
    let menuBarArtworkStyle: MenuBarArtworkStyle

    /// Width reserved for lyrics, already clamped.
    let menuBarLyricsWidth: CGFloat

    /// Whether to hold that width when no lyric is showing.
    let menuBarReservesLyricsWidth: Bool

    /// Whether lyrics appear in the menu bar at all.
    let menuBarShowsLyrics: Bool

    /// How title and artist are combined.
    let menuBarTitleFormat: MenuBarTitleFormat

    /// Reads the current appearance settings.
    ///
    /// Unrecognised stored values fall back to their defaults rather than
    /// failing, which keeps a preferences file edited by hand or written by
    /// an older build from leaving the menu bar blank.
    ///
    /// Hiding both the artwork and the title would make the menu bar item
    /// invisible and so unclickable, stranding the user with no way back into
    /// Reprise. That combination is resolved by forcing the artwork back on.
    ///
    /// - Parameter defaults: Store to read from. Defaults to `.standard`, and
    ///   is injectable so tests can supply a suite of their own.
    /// - Returns: The settings to render the menu bar item with.
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
        let storedLyricsWidth = (
            defaults.object(
                forKey: ReprisePreferenceKey.menuBarLyricsWidth
            ) as? NSNumber
        )?.doubleValue ?? MenuBarLyricsWidth.defaultValue

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
            menuBarLyricsWidth:
                MenuBarLyricsWidth.clamped(storedLyricsWidth),
            menuBarReservesLyricsWidth: defaults.bool(
                forKey: ReprisePreferenceKey.menuBarReservesLyricsWidth
            ),
            menuBarShowsLyrics: defaults.bool(
                forKey: ReprisePreferenceKey.menuBarShowsLyrics
            ),
            menuBarTitleFormat: titleFormat
        )
    }
}

/// Reads and writes the settings that are not plain `@AppStorage` values.
///
/// Covers the preferences needing encoding or repair on the way in or out -
/// the player order especially - plus the default registration the whole
/// scheme depends on.
enum ReprisePreferences {
    /// Player order used until the user rearranges it.
    ///
    /// Derived from the declaration order of ``MediaPlayerKind`` so a newly
    /// supported player joins the default without a second edit here.
    static let defaultPlayerDisplayOrder = MediaPlayerKind.allCases
        .map(\.rawValue)
        .joined(separator: ",")

    /// Reads the user's player display priority.
    ///
    /// - Parameter defaults: Store to read from. Defaults to `.standard`.
    /// - Returns: Every player, in the user's chosen order.
    static func playerDisplayOrder(
        in defaults: UserDefaults = .standard
    ) -> [MediaPlayerKind] {
        playerDisplayOrder(
            from: defaults.string(
                forKey: ReprisePreferenceKey.playerDisplayPriority
            )
        )
    }

    /// Decodes a stored priority string into a complete player order.
    ///
    /// The result always contains every player exactly once, whatever the
    /// input: unknown names are dropped, duplicates collapse to their first
    /// position, and players missing from the string are appended in
    /// declaration order. That makes a stored value written by an older build
    /// - which knew about fewer players - safe to read, and guarantees the
    /// selector can never be handed an order with a gap in it.
    ///
    /// - Parameter rawValue: Comma-separated raw values, or `nil`.
    /// - Returns: Every player, ordered by preference.
    ///
    /// ## Example
    /// ```swift
    /// // "spotify" alone still yields the full set, Spotify first.
    /// ReprisePreferences.playerDisplayOrder(from: "spotify")
    /// // [.spotify, .appleMusic, .youtubeMusic]
    /// ```
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

    /// Encodes a player order for storage.
    ///
    /// Any player the caller left out is appended, so a partial list from the
    /// settings pane still round-trips through
    /// ``playerDisplayOrder(from:)`` as a complete order.
    ///
    /// - Parameter players: Players in the desired priority order.
    /// - Returns: A comma-separated string covering every player.
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

    /// Whether starting one player should pause the previous one.
    ///
    /// - Parameter defaults: Store to read from. Defaults to `.standard`.
    /// - Returns: The user's setting.
    static func automaticallyPausesOtherPlayer(
        in defaults: UserDefaults = .standard
    ) -> Bool {
        defaults.bool(
            forKey:
                ReprisePreferenceKey.automaticallyPausesOtherPlayer
        )
    }

    /// Whether the last playing player should be restored on launch.
    ///
    /// - Parameter defaults: Store to read from. Defaults to `.standard`.
    /// - Returns: The user's setting.
    static func remembersLastPlayedPlayer(
        in defaults: UserDefaults = .standard
    ) -> Bool {
        defaults.bool(
            forKey: ReprisePreferenceKey.remembersLastPlayedPlayer
        )
    }

    /// Reads the player that was last seen playing.
    ///
    /// - Parameter defaults: Store to read from. Defaults to `.standard`.
    /// - Returns: The remembered player, or `nil` when none was stored or the
    ///   stored name is no longer a supported player.
    static func lastPlayedPlayer(
        in defaults: UserDefaults = .standard
    ) -> MediaPlayerKind? {
        defaults.string(
            forKey: ReprisePreferenceKey.lastPlayedPlayer
        ).flatMap(MediaPlayerKind.init(rawValue:))
    }

    /// Records the player that is playing now.
    ///
    /// - Parameters:
    ///   - player: Player to remember.
    ///   - defaults: Store to write to. Defaults to `.standard`.
    static func setLastPlayedPlayer(
        _ player: MediaPlayerKind,
        in defaults: UserDefaults = .standard
    ) {
        defaults.set(
            player.rawValue,
            forKey: ReprisePreferenceKey.lastPlayedPlayer
        )
    }

    /// Registers the default value for every Reprise setting.
    ///
    /// Must run before anything reads a preference. Registered defaults are
    /// not written to disk, so this stays the single definition of what an
    /// untouched setting means, and resetting one is a matter of removing the
    /// user's value rather than writing the default back.
    ///
    /// - Parameter defaults: Store to register with. Defaults to `.standard`.
    static func registerDefaults(
        in defaults: UserDefaults = .standard
    ) {
        defaults.register(
            defaults: [
                ReprisePreferenceKey.automaticallyPausesOtherPlayer:
                    false,
                ReprisePreferenceKey.automaticallyScrollTitles: true,
                ReprisePreferenceKey.marqueeSpeed: MarqueeSpeed.normal.rawValue,
                ReprisePreferenceKey.menuBarArtworkStyle:
                    MenuBarArtworkStyle.albumArtwork.rawValue,
                ReprisePreferenceKey.menuBarLyricsWidth:
                    MenuBarLyricsWidth.defaultValue,
                ReprisePreferenceKey.menuBarReservesLyricsWidth: true,
                ReprisePreferenceKey.menuBarShowsLyrics: false,
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
                ReprisePreferenceKey.remembersLastPlayedPlayer: false,
                ReprisePreferenceKey.resetsMenuTitleWhenPanelOpens: true,
            ]
        )
    }
}
