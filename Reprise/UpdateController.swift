// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Combine
import Sparkle

@MainActor
final class UpdateController: ObservableObject {
    static let shared = UpdateController()

    let updaterController: SPUStandardUpdaterController

    @Published private(set) var canCheckForUpdates = false

    private var canCheckObservation: AnyCancellable?
    private var hasStarted = false

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

    var automaticallyChecksForUpdates: Bool {
        updaterController.updater.automaticallyChecksForUpdates
    }

    var automaticallyDownloadsUpdates: Bool {
        updaterController.updater.automaticallyDownloadsUpdates
    }

    func start() {
        guard !hasStarted else { return }
        hasStarted = true
        updaterController.startUpdater()
    }

    func checkForUpdates() {
        start()
        updaterController.checkForUpdates(nil)
    }

    func setAutomaticallyChecksForUpdates(_ enabled: Bool) {
        updaterController.updater.automaticallyChecksForUpdates = enabled
    }

    func setAutomaticallyDownloadsUpdates(_ enabled: Bool) {
        updaterController.updater.automaticallyDownloadsUpdates = enabled
    }
}
