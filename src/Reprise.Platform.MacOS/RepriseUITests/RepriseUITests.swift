// Reprise — a menu bar music controller for macOS
// Copyright 2026 Junsung Lee. All rights reserved.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License.

import XCTest

/// End-to-end checks driving the built app.
///
/// Coverage here is deliberately thin: Reprise lives in the menu bar, which
/// `XCUIApplication` cannot reach into the way it can a normal window, so the
/// behavioural testing sits in the unit target and this one guards launch.
final class RepriseUITests: XCTestCase {

    /// Stops the run at the first failure.
    ///
    /// Later assertions in a UI test almost always fail as a consequence of
    /// the first, so continuing past it just buries the real cause.
    ///
    /// - Throws: Rethrows any setup failure to XCTest.
    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    /// Reserved for teardown; nothing needs releasing yet.
    ///
    /// - Throws: Rethrows any teardown failure to XCTest.
    override func tearDownWithError() throws {
    }

    /// Checks the app launches without crashing.
    ///
    /// There is nothing to assert beyond reaching the end: `launch()` fails
    /// the test itself if the app does not come up, which makes this a smoke
    /// test for the launch path rather than for any behaviour.
    ///
    /// - Throws: Rethrows any launch failure to XCTest.
    @MainActor
    func testExample() throws {
        let app = XCUIApplication()
        app.launch()
    }

    /// Measures how long a cold launch takes.
    ///
    /// - Throws: Rethrows any launch failure to XCTest.
    @MainActor
    func testLaunchPerformance() throws {
        measure(metrics: [XCTApplicationLaunchMetric()]) {
            XCUIApplication().launch()
        }
    }
}
