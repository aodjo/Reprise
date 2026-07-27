//
//  RepriseApp.swift
//  Reprise
//

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
