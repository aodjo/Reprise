// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Combine
import Sparkle

/// Wraps Sparkle so the rest of the app can offer updates without importing it.
///
/// Sparkle exposes its readiness through KVO, which SwiftUI cannot observe
/// directly; this bridges that into a published property. The updater is also
/// deliberately not started in `init`, so simply constructing the controller
/// - as previews and tests do - never reaches the network.
@MainActor
final class UpdateController: ObservableObject {
    /// The app-wide updater.
    ///
    /// A singleton because Sparkle expects one updater per process; a second
    /// would race the first over the same feed and user defaults.
    static let shared = UpdateController()

    /// The underlying Sparkle controller, exposed for its standard UI.
    let updaterController: SPUStandardUpdaterController

    /// Whether an update check can be started right now.
    ///
    /// False while a check is already running or the updater has not started,
    /// and used to disable the menu item rather than let a press do nothing.
    @Published private(set) var canCheckForUpdates = false

    private var canCheckObservation: AnyCancellable?
    private var hasStarted = false

    /// Builds the Sparkle controller and bridges its KVO state.
    ///
    /// `startingUpdater: false` defers all network activity to ``start()``,
    /// keeping app launch free of an update check the user did not ask for.
    /// The observation is delivered on the main run loop because it drives
    /// published state that SwiftUI reads.
    private init() {
        updaterController = SPUStandardUpdaterController(
            startingUpdater: false,
            updaterDelegate: nil,
            userDriverDelegate: nil
        )
        let updater = updaterController.updater
        canCheckForUpdates = updater.canCheckForUpdates
        canCheckObservation = updater
            .publisher(for: \.canCheckForUpdates)
            .receive(on: RunLoop.main)
            .sink { [weak self] canCheckForUpdates in
                self?.canCheckForUpdates = canCheckForUpdates
            }
    }

    /// Whether Sparkle checks for updates on its own schedule.
    var automaticallyChecksForUpdates: Bool {
        updaterController.updater.automaticallyChecksForUpdates
    }

    /// Whether Sparkle downloads a found update without asking first.
    var automaticallyDownloadsUpdates: Bool {
        updaterController.updater.automaticallyDownloadsUpdates
    }

    /// Starts the updater, at most once per process.
    ///
    /// Guarded because Sparkle raises an exception on a second start, and the
    /// call sites - app launch and ``checkForUpdates()`` - can both run first
    /// depending on how quickly the user opens the menu.
    func start() {
        guard !hasStarted else { return }
        hasStarted = true
        updaterController.startUpdater()
    }

    /// Runs an update check with Sparkle's own progress UI.
    ///
    /// Starts the updater first, so checking works even when the user reaches
    /// for it before the scheduled start has happened.
    func checkForUpdates() {
        start()
        updaterController.checkForUpdates(nil)
    }

    /// Turns Sparkle's scheduled update checks on or off.
    ///
    /// - Parameter enabled: Whether to check automatically.
    func setAutomaticallyChecksForUpdates(_ enabled: Bool) {
        updaterController.updater.automaticallyChecksForUpdates = enabled
    }

    /// Turns automatic downloading of found updates on or off.
    ///
    /// - Parameter enabled: Whether to download without prompting.
    func setAutomaticallyDownloadsUpdates(_ enabled: Bool) {
        updaterController.updater.automaticallyDownloadsUpdates = enabled
    }
}
