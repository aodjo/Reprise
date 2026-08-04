// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import SwiftUI

@main
struct RepriseApp: App {
    @NSApplicationDelegateAdaptor(RepriseAppDelegate.self)
    private var appDelegate

    init() {
        ReprisePreferences.registerDefaults()
    }

    var body: some Scene {
        Settings {
            RepriseSettingsView()
        }
    }
}
