//
//  RepriseApp.swift
//  Reprise
//

import SwiftUI

@main
struct RepriseApp: App {
    @State private var store: NowPlayingStore

    init() {
        let store = NowPlayingStore()
        _store = State(initialValue: store)

        // Unit-test hosts load the app's entry point too. Avoid sending Apple
        // Events to the user's media apps as a side effect of running tests.
        if ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil {
            store.start()
        }
    }

    var body: some Scene {
        MenuBarExtra {
            PlayerPopoverView(store: store)
        } label: {
            HStack(spacing: 4) {
                ArtworkView(
                    data: store.menuBarSnapshot?.track?.artworkData,
                    size: 18,
                    cornerRadius: 4,
                    symbolName: store.menuBarSymbol
                )
                Text(store.menuBarTitle)
                    .lineLimit(1)
            }
            .accessibilityLabel(store.menuBarAccessibilityLabel)
        }
        .menuBarExtraStyle(.window)
    }
}
