//
//  RepriseApp.swift
//  Reprise
//

import SwiftUI

@main
struct RepriseApp: App {
    @NSApplicationDelegateAdaptor(RepriseAppDelegate.self)
    private var appDelegate

    var body: some Scene {
        Settings {
            EmptyView()
        }
    }
}
