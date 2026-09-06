// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import SwiftUI

/// SwiftUI entry point for the app.
///
/// Declares only the Settings scene: Reprise has no main window, and its menu
/// bar item is built in AppKit by ``RepriseAppDelegate`` rather than through
/// `MenuBarExtra`, which cannot host the custom panel behaviour the app needs.
@main
struct RepriseApp: App {
    @NSApplicationDelegateAdaptor(RepriseAppDelegate.self)
    private var appDelegate

    /// Registers preference defaults before any view can read them.
    ///
    /// Runs here rather than in the delegate because SwiftUI resolves
    /// `@AppStorage` properties as scenes are built, which can happen before
    /// `applicationDidFinishLaunching`. Registering later would let the first
    /// read see a hardcoded fallback instead of the intended default.
    init() {
        ReprisePreferences.registerDefaults()
    }

    /// The Settings window, opened from the panel or with Command-comma.
    var body: some Scene {
        Settings {
            RepriseSettingsView()
        }
    }
}
