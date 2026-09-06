// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Foundation

/// Where a set of lyrics came from.
///
/// Surfaced in the UI so the user can see which service supplied the words,
/// and used to decide precedence when both have a match.
enum LyricsSource: String, Equatable, Sendable {
    /// Naver VIBE, which supplies its own per-line timings.
    case vibe = "VIBE"

    /// LRCLIB, which supplies LRC-format lyrics.
    case lrclib = "LRCLIB"
}

/// One timed line of lyrics.
struct LyricLine: Equatable, Sendable {
    /// When the line begins, in seconds from the start of the track.
    let startTime: TimeInterval

    /// When the line stops being current, if known.
    ///
    /// `nil` means the line stays current until the next one begins, which is
    /// how LRC behaves; VIBE supplies explicit ends, which is what lets a gap
    /// between lines show no lyric at all rather than holding the last one.
    let endTime: TimeInterval?

    /// The line's text.
    let text: String

    /// Creates a lyric line.
    ///
    /// - Parameters:
    ///   - startTime: Start offset in seconds.
    ///   - endTime: End offset in seconds. Defaults to `nil`, meaning the line
    ///     runs until the next one.
    ///   - text: The line's text.
    nonisolated init(
        startTime: TimeInterval,
        endTime: TimeInterval? = nil,
        text: String
    ) {
        self.startTime = startTime
        self.endTime = endTime
        self.text = text
    }
}

/// A full set of time-synced lyrics for one track.
///
/// Lines are held sorted by start time, which the lookups below rely on to
/// binary search rather than scan - they run on every UI tick.
struct SyncedLyrics: Equatable, Sendable {
    /// Which service supplied these lyrics.
    let source: LyricsSource

    /// The lines, in ascending order of start time.
    let lines: [LyricLine]

    /// Finds the last line that has begun by a given moment.
    ///
    /// Ignores end times, so it always returns a line once playback is past
    /// the first one. That is what the scrolling lyric view wants: it needs a
    /// line to centre on even during an instrumental gap, where the "current"
    /// line has technically ended.
    ///
    /// - Parameter position: Playback position in seconds.
    /// - Returns: Index of the most recently started line, or `nil` before the
    ///   first line, when there are none, or when the position is not finite.
    nonisolated func focusedLineIndex(
        at position: TimeInterval
    ) -> Int? {
        guard position.isFinite, !lines.isEmpty else { return nil }

        var lowerBound = 0
        var upperBound = lines.count
        while lowerBound < upperBound {
            let middle = (lowerBound + upperBound) / 2
            if lines[middle].startTime <= position {
                lowerBound = middle + 1
            } else {
                upperBound = middle
            }
        }

        let index = lowerBound - 1
        return index >= 0 ? index : nil
    }

    /// Finds the line that is genuinely current at a given moment.
    ///
    /// Unlike ``focusedLineIndex(at:)``, a line whose end time has passed
    /// counts as over. The menu bar uses this so an instrumental break shows
    /// nothing rather than leaving a stale line sitting there.
    ///
    /// - Parameter position: Playback position in seconds.
    /// - Returns: Index of the current line, or `nil` when no line is active.
    nonisolated func lineIndex(at position: TimeInterval) -> Int? {
        guard let index = focusedLineIndex(at: position) else {
            return nil
        }
        if let endTime = lines[index].endTime,
           position >= endTime {
            return nil
        }
        return index
    }

    /// The line that is current at a given moment.
    ///
    /// - Parameter position: Playback position in seconds.
    /// - Returns: The active line, or `nil` when none is.
    nonisolated func line(at position: TimeInterval) -> LyricLine? {
        lineIndex(at: position).map { lines[$0] }
    }
}

/// The identity of a track, for looking lyrics up and caching the result.
///
/// Equality and hashing deliberately ignore duration and compare only
/// normalised text: the same recording reports slightly different lengths
/// across players and services, so including duration would miss cache hits
/// and re-fetch lyrics on every track change. Duration is still carried,
/// because it is a useful tie-breaker when ranking candidates.
struct LyricsTrackQuery: Hashable, Sendable {
    /// Track title, as the player reported it.
    let title: String

    /// Album name, as the player reported it.
    let album: String

    /// Artist credit, as the player reported it.
    let artist: String

    /// Track length in seconds, used only for ranking candidates.
    let duration: TimeInterval

    /// Builds a query from a track.
    ///
    /// - Parameter track: Track to look lyrics up for.
    nonisolated init(track: Track) {
        title = track.title
        album = track.album
        artist = track.artist
        duration = track.duration
    }

    /// Hashes the normalised text fields only.
    ///
    /// Must stay consistent with `==`, which is why duration is excluded here
    /// as well.
    ///
    /// - Parameter hasher: Hasher to feed.
    nonisolated func hash(into hasher: inout Hasher) {
        hasher.combine(Self.normalized(title))
        hasher.combine(Self.normalized(album))
        hasher.combine(Self.normalized(artist))
    }

    /// Compares two queries on normalised text.
    ///
    /// - Parameters:
    ///   - lhs: First query.
    ///   - rhs: Second query.
    /// - Returns: `true` when they describe the same recording.
    nonisolated static func == (
        lhs: LyricsTrackQuery,
        rhs: LyricsTrackQuery
    ) -> Bool {
        normalized(lhs.title) == normalized(rhs.title)
            && normalized(lhs.album) == normalized(rhs.album)
            && normalized(lhs.artist) == normalized(rhs.artist)
    }

    /// Reduces a title or artist to a form two services can be compared on.
    ///
    /// Case, accents, and full-width forms are folded away, then everything
    /// that is not alphanumeric is dropped. That collapses the punctuation
    /// differences that otherwise defeat matching - `Don't Stop (Remastered)`
    /// against `Dont Stop Remastered`, or a Korean title spaced differently by
    /// two catalogues.
    ///
    /// Folding is pinned to `en_US_POSIX` so the result never depends on the
    /// user's locale, which would make a cached lookup behave differently on
    /// two machines.
    ///
    /// - Parameter value: Raw text.
    /// - Returns: A lowercase alphanumeric-only string.
    ///
    /// ## Example
    /// ```swift
    /// LyricsTrackQuery.normalized("Don't Stop (Remastered)")
    /// // "dontstopremastered"
    /// ```
    nonisolated static func normalized(_ value: String) -> String {
        value
            .folding(
                options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive],
                locale: Locale(identifier: "en_US_POSIX")
            )
            .unicodeScalars
            .filter { CharacterSet.alphanumerics.contains($0) }
            .map(String.init)
            .joined()
            .lowercased()
    }
}

/// Progress of a lyrics lookup, as the UI sees it.
///
/// ``unavailable`` is a settled answer, not an error: it means the services
/// were asked and had nothing, so the UI should stop waiting rather than
/// retry.
enum LyricsLoadState: Equatable, Sendable {
    /// No lookup has been attempted for the current track.
    case idle

    /// A lookup is in flight.
    case loading

    /// Lyrics were found.
    case available(SyncedLyrics)

    /// No service had synced lyrics for this track.
    case unavailable

    /// The lyrics, when there are any.
    var lyrics: SyncedLyrics? {
        guard case let .available(lyrics) = self else { return nil }
        return lyrics
    }
}

/// Fetches time-synced lyrics from VIBE and LRCLIB.
///
/// An actor so lookups stay off the main thread; it holds no cached state of
/// its own, since the caller caches by ``LyricsTrackQuery``.
actor LyricsService {
    /// VIBE's web API root.
    private static let vibeBaseURL =
        URL(string: "https://apis.naver.com/vibeWeb/musicapiweb")!

    /// LRCLIB's API root.
    private static let lrclibBaseURL =
        URL(string: "https://lrclib.net/api")!

    private let session: URLSession

    /// Creates the service.
    ///
    /// The default session is ephemeral, so lyric requests leave nothing on
    /// disk, and its timeouts are short because a lookup that outlives the
    /// track it was for is worse than no lookup.
    ///
    /// - Parameter session: Session to use. Defaults to `nil`, which builds
    ///   the configured ephemeral session; tests pass a stub.
    init(session: URLSession? = nil) {
        if let session {
            self.session = session
        } else {
            let configuration = URLSessionConfiguration.ephemeral
            configuration.timeoutIntervalForRequest = 6
            configuration.timeoutIntervalForResource = 10
            configuration.requestCachePolicy = .returnCacheDataElseLoad
            self.session = URLSession(configuration: configuration)
        }
    }

    /// Looks up synced lyrics for a track.
    ///
    /// VIBE is tried first because it supplies explicit per-line end times,
    /// which LRC cannot express; LRCLIB is the broader fallback. Cancellation
    /// is checked between the two so a track change does not pay for a second
    /// lookup nobody is waiting on.
    ///
    /// - Parameter query: The track to find lyrics for.
    /// - Returns: The lyrics, or `nil` when neither service has them. Never
    ///   throws: a lookup failure is indistinguishable from no match as far as
    ///   the UI is concerned.
    func fetchSyncedLyrics(
        for query: LyricsTrackQuery
    ) async -> SyncedLyrics? {
        if let vibeLyrics = await fetchVibeLyrics(for: query) {
            return vibeLyrics
        }
        guard !Task.isCancelled else { return nil }
        return await fetchLRCLIBLyrics(for: query)
    }

    /// Searches VIBE and fetches lyrics for the best matching track.
    ///
    /// Two round trips: VIBE's search does not return lyrics, so a track has
    /// to be chosen first and its lyrics fetched by id.
    ///
    /// - Parameter query: The track to find lyrics for.
    /// - Returns: The lyrics, or `nil` on no match or any failure.
    private func fetchVibeLyrics(
        for query: LyricsTrackQuery
    ) async -> SyncedLyrics? {
        do {
            let searchData = try await data(
                path: "v3/search/track",
                baseURL: Self.vibeBaseURL,
                queryItems: [
                    URLQueryItem(
                        name: "query",
                        value: "\(query.title) \(query.artist)"
                    ),
                    URLQueryItem(name: "display", value: "10"),
                    URLQueryItem(name: "sort", value: "RELEVANCE"),
                ]
            )
            let candidates = try Self.parseVibeSearch(searchData)
            guard let candidate = Self.bestVibeCandidate(
                for: query,
                candidates: candidates
            ) else {
                return nil
            }

            let lyricData = try await data(
                path: "vibe/v4/lyric/\(candidate.trackID)",
                baseURL: Self.vibeBaseURL
            )
            let lyrics = try Self.parseVibeLyrics(lyricData)
            return lyrics
        } catch {
            return nil
        }
    }

    /// Fetches lyrics from LRCLIB, exactly then approximately.
    ///
    /// The `get` endpoint needs all four fields to agree and returns 404 when
    /// they do not, which is common because players disagree on album naming
    /// and round durations differently. A miss there is expected rather than
    /// exceptional, so it falls through to the looser `search` endpoint and
    /// ranks the results locally.
    ///
    /// - Parameter query: The track to find lyrics for.
    /// - Returns: The lyrics, or `nil` when neither endpoint yields a usable
    ///   match.
    private func fetchLRCLIBLyrics(
        for query: LyricsTrackQuery
    ) async -> SyncedLyrics? {
        do {
            let directData = try await data(
                path: "get",
                baseURL: Self.lrclibBaseURL,
                queryItems: Self.lrclibQueryItems(for: query)
            )
            let result = try JSONDecoder().decode(
                LRCLIBResult.self,
                from: directData
            )
            if let lyrics = Self.syncedLyrics(
                from: result,
                duration: query.duration
            ) {
                return lyrics
            }
        } catch {
            // A missing exact match and a temporary endpoint failure both
            // continue through the more permissive search endpoint.
        }

        guard !Task.isCancelled else { return nil }

        do {
            let searchData = try await data(
                path: "search",
                baseURL: Self.lrclibBaseURL,
                queryItems: [
                    URLQueryItem(name: "track_name", value: query.title),
                    URLQueryItem(name: "artist_name", value: query.artist),
                ]
            )
            let results = try JSONDecoder().decode(
                [LRCLIBResult].self,
                from: searchData
            )
            guard let result = Self.bestLRCLIBResult(
                for: query,
                results: results
            ) else {
                return nil
            }
            return Self.syncedLyrics(
                from: result,
                duration: query.duration
            )
        } catch {
            return nil
        }
    }

    /// Performs one API request and returns its body.
    ///
    /// Sends an identifying User-Agent because LRCLIB asks clients to, and
    /// treats any non-2xx status as a failure so an HTML error page never
    /// reaches a parser.
    ///
    /// - Parameters:
    ///   - path: Path relative to `baseURL`.
    ///   - baseURL: Service root.
    ///   - queryItems: Query parameters. Defaults to none.
    /// - Returns: The response body.
    /// - Throws: `URLError.badURL` if the components will not resolve,
    ///   `URLError.badServerResponse` on a non-2xx status, or any transport
    ///   error from `URLSession`.
    private func data(
        path: String,
        baseURL: URL,
        queryItems: [URLQueryItem] = []
    ) async throws -> Data {
        var components = URLComponents(
            url: baseURL.appending(path: path),
            resolvingAgainstBaseURL: false
        )!
        if !queryItems.isEmpty {
            components.queryItems = queryItems
        }
        guard let url = components.url else {
            throw URLError(.badURL)
        }

        var request = URLRequest(url: url)
        request.timeoutInterval = 6
        request.setValue(
            "Reprise/1.0 (https://junx.dev)",
            forHTTPHeaderField: "User-Agent"
        )
        let (data, response) = try await session.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse,
              200..<300 ~= httpResponse.statusCode else {
            throw URLError(.badServerResponse)
        }
        return data
    }

    /// Builds the query for LRCLIB's exact-match endpoint.
    ///
    /// Album and duration are included only when known, since sending a blank
    /// album or a zero duration would narrow the match to nothing rather than
    /// being ignored.
    ///
    /// - Parameter query: The track to look up.
    /// - Returns: Query items for the `get` endpoint.
    private static func lrclibQueryItems(
        for query: LyricsTrackQuery
    ) -> [URLQueryItem] {
        var items = [
            URLQueryItem(name: "track_name", value: query.title),
            URLQueryItem(name: "artist_name", value: query.artist),
        ]
        if !query.album.isEmpty {
            items.append(URLQueryItem(name: "album_name", value: query.album))
        }
        if query.duration > 0 {
            items.append(
                URLQueryItem(
                    name: "duration",
                    value: String(Int(query.duration.rounded()))
                )
            )
        }
        return items
    }
}

extension LyricsService {
    /// One track from a VIBE search result.
    struct VibeCandidate: Equatable, Sendable {
        /// VIBE's track id, needed to fetch the lyrics.
        let trackID: String

        /// Track title as VIBE has it.
        let title: String

        /// Album title as VIBE has it.
        let album: String

        /// Every credited artist; VIBE lists collaborators separately.
        let artists: [String]

        /// Track length in seconds, parsed from VIBE's `mm:ss` field.
        let duration: TimeInterval

        /// Whether VIBE holds timed lyrics for this track.
        let hasSyncedLyrics: Bool

        /// Whether VIBE flags the track as adult-only.
        let isAdult: Bool
    }

    /// Parses a VIBE search response into candidates.
    ///
    /// - Parameter data: XML body from the search endpoint.
    /// - Returns: Every track with an id and a title. Entries missing either
    ///   are skipped rather than failing the parse, since one malformed row
    ///   should not lose the rest of the results.
    /// - Throws: A parse error when the body is not usable XML.
    nonisolated static func parseVibeSearch(
        _ data: Data
    ) throws -> [VibeCandidate] {
        let root = try XMLTreeParser.parse(data)
        return root.descendants(named: "tracks").compactMap { node in
            guard let trackID = node.directText(named: "trackId"),
                  let title = node.directText(named: "trackTitle") else {
                return nil
            }
            let artists = node
                .directChild(named: "artists")?
                .descendants(named: "artistName")
                .compactMap(\.trimmedText) ?? []
            return VibeCandidate(
                trackID: trackID,
                title: title,
                album: node
                    .directChild(named: "album")?
                    .directText(named: "albumTitle") ?? "",
                artists: artists,
                duration: parseClock(
                    node.directText(named: "playTime") ?? ""
                ),
                hasSyncedLyrics:
                    node.directText(named: "hasSyncLyric") == "true",
                isAdult: node.directText(named: "isAdult") == "true"
            )
        }
    }

    /// Picks the VIBE candidate most likely to be the same recording.
    ///
    /// Candidates without timed lyrics are useless here and adult-flagged ones
    /// are excluded outright, so both are filtered before scoring.
    ///
    /// A title match is mandatory - exact or one containing the other, which
    /// tolerates suffixes like `(Live)`. Beyond that, at least one of artist,
    /// album, or duration must corroborate it, so a common title alone cannot
    /// carry a wrong track through. Scoring then prefers exact titles, and
    /// weights artist above album above duration, since a shared artist is far
    /// stronger evidence than a length two catalogues happen to agree on.
    ///
    /// - Parameters:
    ///   - query: The track being looked up.
    ///   - candidates: Search results to rank.
    /// - Returns: The highest scoring candidate, or `nil` when none qualifies.
    nonisolated static func bestVibeCandidate(
        for query: LyricsTrackQuery,
        candidates: [VibeCandidate]
    ) -> VibeCandidate? {
        candidates
            .filter { $0.hasSyncedLyrics && !$0.isAdult }
            .compactMap { candidate -> (VibeCandidate, Int)? in
                let title = LyricsTrackQuery.normalized(candidate.title)
                let queryTitle = LyricsTrackQuery.normalized(query.title)
                guard !title.isEmpty,
                      !queryTitle.isEmpty,
                      title == queryTitle
                        || title.contains(queryTitle)
                        || queryTitle.contains(title) else {
                    return nil
                }

                let queryArtist = LyricsTrackQuery.normalized(query.artist)
                let artistMatches = queryArtist.isEmpty
                    || candidate.artists.contains { artist in
                        let normalized =
                            LyricsTrackQuery.normalized(artist)
                        return normalized == queryArtist
                            || normalized.contains(queryArtist)
                            || queryArtist.contains(normalized)
                    }
                let albumMatches =
                    !query.album.isEmpty
                    && LyricsTrackQuery.normalized(candidate.album)
                        == LyricsTrackQuery.normalized(query.album)
                let durationDifference =
                    query.duration > 0 && candidate.duration > 0
                        ? abs(query.duration - candidate.duration)
                        : .infinity
                let durationMatches = durationDifference <= 6

                guard artistMatches || albumMatches || durationMatches else {
                    return nil
                }

                var score = title == queryTitle ? 100 : 70
                score += artistMatches ? 50 : 0
                score += albumMatches ? 20 : 0
                score += durationMatches ? 25
                    : durationDifference <= 12 ? 8 : 0
                return (candidate, score)
            }
            .max { $0.1 < $1.1 }?
            .0
    }

    /// Parses VIBE's lyric response into timed lines.
    ///
    /// VIBE returns starts, ends, and texts as three parallel arrays rather
    /// than as line elements, so they are zipped back together here. The
    /// counts are required to agree before anything is built: a mismatch would
    /// silently pair each line with the wrong timestamp.
    ///
    /// A track may carry translations, so the `default` language content is
    /// preferred and the first is used only as a fallback.
    ///
    /// - Parameter data: XML body from the lyric endpoint.
    /// - Returns: The lyrics, or `nil` when the track has none, the arrays
    ///   disagree, or every line is blank.
    /// - Throws: A parse error when the body is not usable XML.
    nonisolated static func parseVibeLyrics(
        _ data: Data
    ) throws -> SyncedLyrics? {
        let root = try XMLTreeParser.parse(data)
        guard root.firstDescendantText(named: "hasSyncLyric") == "true",
              let syncNode = root.firstDescendant(named: "syncLyric") else {
            return nil
        }

        let starts = syncNode
            .directChild(named: "startTimeIndex")?
            .children(named: "startTimeIndex")
            .compactMap { $0.trimmedText.flatMap(TimeInterval.init) } ?? []
        let ends = syncNode
            .directChild(named: "endTimeIndex")?
            .children(named: "endTimeIndex")
            .compactMap { $0.trimmedText.flatMap(TimeInterval.init) } ?? []
        let contentNodes = syncNode
            .directChild(named: "contents")?
            .children(named: "contents") ?? []
        let content = contentNodes.first {
            $0.directText(named: "languageType") == "default"
        } ?? contentNodes.first
        let texts = content?
            .directChild(named: "text")?
            .children(named: "text")
            .compactMap(\.trimmedText) ?? []

        guard starts.count == texts.count, !starts.isEmpty else {
            return nil
        }

        let lines: [LyricLine] = zip(starts.indices, texts).compactMap {
            pair in
            let (index, text) = pair
            guard !text.isEmpty else { return nil }
            let endTime = ends.indices.contains(index)
                ? ends[index]
                : starts.indices.contains(index + 1)
                    ? starts[index + 1]
                    : nil
            return LyricLine(
                startTime: starts[index],
                endTime: endTime,
                text: text
            )
        }
        guard !lines.isEmpty else { return nil }
        return SyncedLyrics(source: .vibe, lines: lines)
    }

    /// Parses LRC-format lyrics into timed lines.
    ///
    /// Handles the parts of LRC that appear in real files: an `[offset:]` tag
    /// shifting every subsequent timestamp, and repeated timestamps on one
    /// line for a refrain, which are expanded into a separate entry each.
    /// Metadata tags and untimed lines are skipped.
    ///
    /// Lines are sorted after parsing, because repeated timestamps and offsets
    /// mean file order is not time order. Each line's end is the next line's
    /// start, so no gap appears mid-song; the final line ends at the track
    /// duration when one is known, which stops the last lyric hanging in the
    /// menu bar after the song is over.
    ///
    /// - Parameters:
    ///   - value: LRC file contents.
    ///   - duration: Track length in seconds, used only to close the last
    ///     line. Defaults to 0, leaving it open-ended.
    /// - Returns: Timed lines, sorted by start time. Empty when nothing timed
    ///   was found.
    ///
    /// ## Example
    /// ```swift
    /// LyricsService.parseLRC("[00:12.50]Hello", duration: 180)
    /// // [LyricLine(startTime: 12.5, endTime: 180, text: "Hello")]
    /// ```
    nonisolated static func parseLRC(
        _ value: String,
        duration: TimeInterval = 0
    ) -> [LyricLine] {
        let timestampPattern =
            #"\[(\d{1,3}):(\d{2}(?:\.\d{1,3})?)\]"#
        let expression = try! NSRegularExpression(pattern: timestampPattern)
        var timestampedText: [(TimeInterval, String)] = []
        var offset: TimeInterval = 0

        for rawLine in value.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            if line.lowercased().hasPrefix("[offset:"),
               let closingBracket = line.firstIndex(of: "]"),
               let milliseconds = Double(
                   line[line.index(line.startIndex, offsetBy: 8)..<closingBracket]
               ) {
                offset = milliseconds / 1_000
                continue
            }

            let range = NSRange(line.startIndex..., in: line)
            let matches = expression.matches(in: line, range: range)
            guard !matches.isEmpty else { continue }

            let text = expression
                .stringByReplacingMatches(
                    in: line,
                    range: range,
                    withTemplate: ""
                )
                .trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else { continue }

            for match in matches {
                guard let minuteRange = Range(match.range(at: 1), in: line),
                      let secondRange = Range(match.range(at: 2), in: line),
                      let minutes = Double(line[minuteRange]),
                      let seconds = Double(line[secondRange]) else {
                    continue
                }
                timestampedText.append(
                    (max(minutes * 60 + seconds + offset, 0), text)
                )
            }
        }

        timestampedText.sort {
            if $0.0 == $1.0 { return $0.1 < $1.1 }
            return $0.0 < $1.0
        }
        return timestampedText.indices.map { index in
            LyricLine(
                startTime: timestampedText[index].0,
                endTime: timestampedText.indices.contains(index + 1)
                    ? timestampedText[index + 1].0
                    : duration > timestampedText[index].0
                        ? duration
                        : nil,
                text: timestampedText[index].1
            )
        }
    }

    /// Parses VIBE's `mm:ss` duration field into seconds.
    ///
    /// - Parameter value: Clock string such as `3:52`.
    /// - Returns: The duration in seconds, or 0 when it is not two components.
    private nonisolated static func parseClock(
        _ value: String
    ) -> TimeInterval {
        let components = value.split(separator: ":").compactMap(Double.init)
        guard components.count == 2 else { return 0 }
        return components[0] * 60 + components[1]
    }
}

private extension LyricsService {
    /// One track record from LRCLIB.
    struct LRCLIBResult: Decodable, Sendable {
        /// Track title.
        let trackName: String

        /// Artist credit.
        let artistName: String

        /// Album name, which LRCLIB may omit.
        let albumName: String?

        /// Track length in seconds, as LRCLIB has it.
        let duration: TimeInterval

        /// LRC-format lyrics, absent for records that are unsynced only.
        let syncedLyrics: String?
    }

    /// Picks the LRCLIB result most likely to be the same recording.
    ///
    /// Records without synced lyrics are dropped first, since plain text
    /// cannot be shown against playback. The rest must match on both title and
    /// artist - LRCLIB's search is loose enough to return unrelated tracks -
    /// after which duration decides, being the only remaining signal that
    /// separates a single from an album version.
    ///
    /// - Parameters:
    ///   - query: The track being looked up.
    ///   - results: Search results to rank.
    /// - Returns: The closest match by duration, or `nil` when none qualifies.
    nonisolated static func bestLRCLIBResult(
        for query: LyricsTrackQuery,
        results: [LRCLIBResult]
    ) -> LRCLIBResult? {
        let candidates = results.filter {
            guard let lyrics = $0.syncedLyrics, !lyrics.isEmpty else {
                return false
            }
            let title = LyricsTrackQuery.normalized($0.trackName)
            let queryTitle = LyricsTrackQuery.normalized(query.title)
            let artist = LyricsTrackQuery.normalized($0.artistName)
            let queryArtist = LyricsTrackQuery.normalized(query.artist)
            let titleMatches = title == queryTitle
                || title.contains(queryTitle)
                || queryTitle.contains(title)
            let artistMatches = queryArtist.isEmpty
                || artist == queryArtist
                || artist.contains(queryArtist)
                || queryArtist.contains(artist)
            return titleMatches && artistMatches
        }
        return candidates.min {
            abs($0.duration - query.duration)
                < abs($1.duration - query.duration)
        }
    }

    /// Converts an LRCLIB record into lyrics.
    ///
    /// Prefers the player's duration over LRCLIB's for closing the last line,
    /// since that is what playback will actually be measured against.
    ///
    /// - Parameters:
    ///   - result: The chosen LRCLIB record.
    ///   - duration: Track length from the player, or 0 when unknown.
    /// - Returns: The lyrics, or `nil` when the record has no usable LRC.
    nonisolated static func syncedLyrics(
        from result: LRCLIBResult,
        duration: TimeInterval
    ) -> SyncedLyrics? {
        guard let value = result.syncedLyrics, !value.isEmpty else {
            return nil
        }
        let lines = parseLRC(
            value,
            duration: duration > 0 ? duration : result.duration
        )
        guard !lines.isEmpty else { return nil }
        return SyncedLyrics(source: .lrclib, lines: lines)
    }
}

/// One element of a parsed XML document.
///
/// A minimal tree, built because VIBE returns XML and `XMLParser` is
/// event-driven: the search and lyric responses both need to be walked by
/// name and re-associated across siblings, which streaming callbacks make
/// awkward. `XMLDocument` would do it, but is unavailable in a sandboxed app.
private nonisolated final class XMLTreeNode {
    /// Element name.
    let name: String

    /// Accumulated character data, which arrives in fragments.
    var text = ""

    /// Child elements, in document order.
    var children: [XMLTreeNode] = []

    /// Creates an empty node.
    ///
    /// - Parameter name: Element name.
    init(name: String) {
        self.name = name
    }

    /// The element's text with whitespace trimmed.
    ///
    /// - Returns: The trimmed text, or `nil` when it is blank, so callers can
    ///   treat empty and absent alike.
    var trimmedText: String? {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    /// Direct children with a given name.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: Matching children, in document order.
    func children(named name: String) -> [XMLTreeNode] {
        children.filter { $0.name == name }
    }

    /// The first direct child with a given name.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: The child, or `nil`.
    func directChild(named name: String) -> XMLTreeNode? {
        children.first { $0.name == name }
    }

    /// Text of the first direct child with a given name.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: The trimmed text, or `nil` when the child is absent or blank.
    func directText(named name: String) -> String? {
        directChild(named: name)?.trimmedText
    }

    /// Every descendant with a given name, at any depth.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: Matching descendants, in document order.
    func descendants(named name: String) -> [XMLTreeNode] {
        children.flatMap { child in
            (child.name == name ? [child] : [])
                + child.descendants(named: name)
        }
    }

    /// The first descendant with a given name, depth-first.
    ///
    /// Separate from ``descendants(named:)`` because it stops at the first
    /// hit rather than walking the whole subtree.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: The descendant, or `nil`.
    func firstDescendant(named name: String) -> XMLTreeNode? {
        for child in children {
            if child.name == name {
                return child
            }
            if let match = child.firstDescendant(named: name) {
                return match
            }
        }
        return nil
    }

    /// Text of the first descendant with a given name.
    ///
    /// - Parameter name: Element name to match.
    /// - Returns: The trimmed text, or `nil`.
    func firstDescendantText(named name: String) -> String? {
        firstDescendant(named: name)?.trimmedText
    }
}

/// Builds an ``XMLTreeNode`` tree from XML data.
private nonisolated final class XMLTreeParser: NSObject, XMLParserDelegate {
    private let document = XMLTreeNode(name: "document")
    private var stack: [XMLTreeNode] = []
    private var parsingError: Error?

    /// Parses XML into a tree.
    ///
    /// The delegate is held for the duration of the synchronous parse, so the
    /// local reference is enough to keep it alive - `XMLParser.delegate` is
    /// unowned.
    ///
    /// - Parameter data: XML document.
    /// - Returns: A synthetic root whose children are the document's
    ///   top-level elements.
    /// - Throws: The delegate's error, the parser's error, or
    ///   `URLError.cannotParseResponse` if neither was recorded.
    static func parse(_ data: Data) throws -> XMLTreeNode {
        let delegate = XMLTreeParser()
        let parser = XMLParser(data: data)
        parser.delegate = delegate
        guard parser.parse() else {
            throw delegate.parsingError
                ?? parser.parserError
                ?? URLError(.cannotParseResponse)
        }
        return delegate.document
    }

    /// Opens an element and descends into it.
    ///
    /// - Parameters:
    ///   - parser: Reporting parser.
    ///   - elementName: Name of the element being opened.
    ///   - namespaceURI: Namespace; unused.
    ///   - qName: Qualified name; unused.
    ///   - attributeDict: Attributes; unused, as no VIBE field is an
    ///     attribute.
    func parser(
        _ parser: XMLParser,
        didStartElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?,
        attributes attributeDict: [String: String] = [:]
    ) {
        let node = XMLTreeNode(name: elementName)
        (stack.last ?? document).children.append(node)
        stack.append(node)
    }

    /// Appends character data to the open element.
    ///
    /// Appends rather than assigns, because the parser delivers text in
    /// fragments split around entities and buffer boundaries.
    ///
    /// - Parameters:
    ///   - parser: Reporting parser.
    ///   - string: Text fragment.
    func parser(
        _ parser: XMLParser,
        foundCharacters string: String
    ) {
        stack.last?.text += string
    }

    /// Closes the current element.
    ///
    /// - Parameters:
    ///   - parser: Reporting parser.
    ///   - elementName: Name of the element being closed; unused, since the
    ///     stack already identifies it.
    ///   - namespaceURI: Namespace; unused.
    ///   - qName: Qualified name; unused.
    func parser(
        _ parser: XMLParser,
        didEndElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?
    ) {
        _ = stack.popLast()
    }

    /// Records a parse failure for ``parse(_:)`` to throw.
    ///
    /// - Parameters:
    ///   - parser: Reporting parser.
    ///   - parseError: The failure.
    func parser(
        _ parser: XMLParser,
        parseErrorOccurred parseError: Error
    ) {
        parsingError = parseError
    }
}
