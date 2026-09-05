// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import XCTest

/// Captures a launch screenshot for each UI configuration.
///
/// Separate from ``RepriseUITests`` because these run once per appearance and
/// language combination Xcode is configured with, and exist to produce
/// attachments for review rather than to assert anything.
final class RepriseUITestsLaunchTests: XCTestCase {

    /// Runs the tests once per target application UI configuration.
    ///
    /// What turns a single launch check into one screenshot per appearance and
    /// localisation, which is the point of this class.
    override class var runsForEachTargetApplicationUIConfiguration: Bool {
        true
    }

    /// Stops the run at the first failure.
    ///
    /// - Throws: Rethrows any setup failure to XCTest.
    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    /// Launches the app and attaches a screenshot.
    ///
    /// The attachment is kept regardless of outcome, since its value is the
    /// visual record rather than evidence of a failure.
    ///
    /// - Throws: Rethrows any launch failure to XCTest.
    @MainActor
    func testLaunch() throws {
        let app = XCUIApplication()
        app.launch()

        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = "Launch Screen"
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
