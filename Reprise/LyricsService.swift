//
//  LyricsService.swift
//  Reprise
//

import Foundation

enum LyricsSource: String, Equatable, Sendable {
    case vibe = "VIBE"
    case lrclib = "LRCLIB"
}

struct LyricLine: Equatable, Sendable {
    let startTime: TimeInterval
    let endTime: TimeInterval?
    let text: String

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

struct SyncedLyrics: Equatable, Sendable {
    let source: LyricsSource
    let lines: [LyricLine]

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

    nonisolated func line(at position: TimeInterval) -> LyricLine? {
        lineIndex(at: position).map { lines[$0] }
    }
}

struct LyricsTrackQuery: Hashable, Sendable {
    let title: String
    let album: String
    let artist: String
    let duration: TimeInterval

    nonisolated init(track: Track) {
        title = track.title
        album = track.album
        artist = track.artist
        duration = track.duration
    }

    nonisolated func hash(into hasher: inout Hasher) {
        hasher.combine(Self.normalized(title))
        hasher.combine(Self.normalized(album))
        hasher.combine(Self.normalized(artist))
    }

    nonisolated static func == (
        lhs: LyricsTrackQuery,
        rhs: LyricsTrackQuery
    ) -> Bool {
        normalized(lhs.title) == normalized(rhs.title)
            && normalized(lhs.album) == normalized(rhs.album)
            && normalized(lhs.artist) == normalized(rhs.artist)
    }

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

enum LyricsLoadState: Equatable, Sendable {
    case idle
    case loading
    case available(SyncedLyrics)
    case unavailable

    var lyrics: SyncedLyrics? {
        guard case let .available(lyrics) = self else { return nil }
        return lyrics
    }
}

actor LyricsService {
    private static let vibeBaseURL =
        URL(string: "https://apis.naver.com/vibeWeb/musicapiweb")!
    private static let lrclibBaseURL =
        URL(string: "https://lrclib.net/api")!

    private let session: URLSession

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

    func fetchSyncedLyrics(
        for query: LyricsTrackQuery
    ) async -> SyncedLyrics? {
        if let vibeLyrics = await fetchVibeLyrics(for: query) {
            return vibeLyrics
        }
        guard !Task.isCancelled else { return nil }
        return await fetchLRCLIBLyrics(for: query)
    }

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
    struct VibeCandidate: Equatable, Sendable {
        let trackID: String
        let title: String
        let album: String
        let artists: [String]
        let duration: TimeInterval
        let hasSyncedLyrics: Bool
        let isAdult: Bool
    }

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

    private nonisolated static func parseClock(
        _ value: String
    ) -> TimeInterval {
        let components = value.split(separator: ":").compactMap(Double.init)
        guard components.count == 2 else { return 0 }
        return components[0] * 60 + components[1]
    }
}

private extension LyricsService {
    struct LRCLIBResult: Decodable, Sendable {
        let trackName: String
        let artistName: String
        let albumName: String?
        let duration: TimeInterval
        let syncedLyrics: String?
    }

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

private nonisolated final class XMLTreeNode {
    let name: String
    var text = ""
    var children: [XMLTreeNode] = []

    init(name: String) {
        self.name = name
    }

    var trimmedText: String? {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    func children(named name: String) -> [XMLTreeNode] {
        children.filter { $0.name == name }
    }

    func directChild(named name: String) -> XMLTreeNode? {
        children.first { $0.name == name }
    }

    func directText(named name: String) -> String? {
        directChild(named: name)?.trimmedText
    }

    func descendants(named name: String) -> [XMLTreeNode] {
        children.flatMap { child in
            (child.name == name ? [child] : [])
                + child.descendants(named: name)
        }
    }

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

    func firstDescendantText(named name: String) -> String? {
        firstDescendant(named: name)?.trimmedText
    }
}

private nonisolated final class XMLTreeParser: NSObject, XMLParserDelegate {
    private let document = XMLTreeNode(name: "document")
    private var stack: [XMLTreeNode] = []
    private var parsingError: Error?

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

    func parser(
        _ parser: XMLParser,
        foundCharacters string: String
    ) {
        stack.last?.text += string
    }

    func parser(
        _ parser: XMLParser,
        didEndElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?
    ) {
        _ = stack.popLast()
    }

    func parser(
        _ parser: XMLParser,
        parseErrorOccurred parseError: Error
    ) {
        parsingError = parseError
    }
}
