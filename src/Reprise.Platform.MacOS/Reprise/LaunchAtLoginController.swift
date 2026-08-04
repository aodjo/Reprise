// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Combine
import ServiceManagement

enum LaunchAtLoginState: Equatable {
    case disabled
    case enabled
    case requiresApproval
    case unavailable

    init(status: SMAppService.Status) {
        switch status {
        case .notRegistered:
            self = .disabled
        case .enabled:
            self = .enabled
        case .requiresApproval:
            self = .requiresApproval
        case .notFound:
            self = .unavailable
        @unknown default:
            self = .unavailable
        }
    }

    var isOn: Bool {
        self == .enabled || self == .requiresApproval
    }
}

@MainActor
final class LaunchAtLoginController: ObservableObject {
    @Published private(set) var state: LaunchAtLoginState
    @Published private(set) var errorMessage: String?

    private let service: SMAppService

    init(service: SMAppService = .mainApp) {
        self.service = service
        state = LaunchAtLoginState(status: service.status)
    }

    func refresh() {
        state = LaunchAtLoginState(status: service.status)
    }

    func setEnabled(_ enabled: Bool) {
        errorMessage = nil

        do {
            if enabled {
                switch state {
                case .disabled, .unavailable:
                    try service.register()
                case .requiresApproval:
                    SMAppService.openSystemSettingsLoginItems()
                case .enabled:
                    break
                }
            } else if state.isOn {
                try service.unregister()
            }
        } catch {
            errorMessage = error.localizedDescription
        }

        refresh()
    }

    func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
