// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

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
