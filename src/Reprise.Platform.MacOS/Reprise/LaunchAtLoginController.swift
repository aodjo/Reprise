// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import Combine
import ServiceManagement

/// Login item registration state, reduced to what the settings toggle needs.
///
/// Mirrors `SMAppService.Status` but collapses the cases the UI treats alike,
/// and stays stable if the system enum gains members.
enum LaunchAtLoginState: Equatable {
    /// Not registered; the toggle is off.
    case disabled

    /// Registered and active.
    case enabled

    /// Registered, but the user must approve it in System Settings before it
    /// takes effect.
    case requiresApproval

    /// The service could not be found, so registration cannot be attempted.
    ///
    /// Normally means the app is running from a location macOS will not
    /// register a login item from, such as a build directory.
    case unavailable

    /// Maps a system status onto the state the UI works with.
    ///
    /// `@unknown default` folds into ``unavailable`` rather than ``disabled``
    /// so a future status never presents as a working "off" switch that
    /// silently fails to turn on.
    ///
    /// - Parameter status: Status reported by `SMAppService`.
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

    /// Whether the toggle should read as on.
    ///
    /// Includes ``requiresApproval``, because from the user's point of view
    /// they have already switched it on; what remains is a system prompt, not
    /// a further decision in Reprise.
    var isOn: Bool {
        self == .enabled || self == .requiresApproval
    }
}

/// Drives the "launch at login" setting through `SMAppService`.
///
/// Registration can fail or need approval, so the controller publishes both
/// the resulting state and any error rather than returning a plain success
/// flag, letting the settings pane explain what happened.
@MainActor
final class LaunchAtLoginController: ObservableObject {
    /// Current registration state, refreshed after every change.
    @Published private(set) var state: LaunchAtLoginState

    /// Why the last change failed, or `nil` when it succeeded.
    @Published private(set) var errorMessage: String?

    private let service: SMAppService

    /// Creates a controller over a login item service.
    ///
    /// - Parameter service: Service to register against. Defaults to the main
    ///   app's, and is injectable so tests can drive the state machine
    ///   without touching the user's real login items.
    init(service: SMAppService = .mainApp) {
        self.service = service
        state = LaunchAtLoginState(status: service.status)
    }

    /// Re-reads the registration state from the system.
    ///
    /// Needed because approval happens in System Settings, outside the app,
    /// with no callback: the state is only observed on the next look.
    func refresh() {
        state = LaunchAtLoginState(status: service.status)
    }

    /// Turns launch at login on or off.
    ///
    /// Turning it on branches on the current state: an unregistered service is
    /// registered, one awaiting approval sends the user to System Settings
    /// since Reprise cannot approve on their behalf, and an already-enabled
    /// one is left alone. Turning it off unregisters only when something is
    /// actually registered.
    ///
    /// Failures are published to ``errorMessage`` rather than thrown, since
    /// the caller is a SwiftUI toggle with nowhere to catch. The state is
    /// re-read either way, so a failed change snaps the toggle back to the
    /// truth instead of leaving it showing what was asked for.
    ///
    /// - Parameter enabled: Whether the login item should be registered.
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

    /// Opens the Login Items pane of System Settings.
    ///
    /// Offered alongside the toggle so a user stuck on ``requiresApproval``
    /// has a way to finish the job without hunting through System Settings.
    func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
