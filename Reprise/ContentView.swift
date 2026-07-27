//
//  ContentView.swift
//  Reprise
//

import SwiftUI

/// Xcode previews and UI tests use the same content as the menu bar popover.
struct ContentView: View {
    @State private var store = NowPlayingStore()

    var body: some View {
        PlayerPopoverView(store: store)
            .task {
                store.start()
            }
    }
}

#Preview {
    ContentView()
}
