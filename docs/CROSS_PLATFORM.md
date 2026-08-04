# Cross-platform migration

Reprise will use one repository and one mainline for macOS, Windows, and
Linux. Shared behavior and UI belong in `src`, while operating-system APIs
are isolated behind platform adapters.

## Source layout

```text
src/
├── Reprise.Core/              Shared models, player selection, lyrics, and protocols
├── Reprise.Desktop/           Shared Avalonia desktop UI
├── Reprise.Platform.MacOS/    Native macOS Xcode app and platform integration
├── Reprise.Platform.Windows/  Windows GSMTC and notification-area integration
└── Reprise.Platform.Linux/    Linux MPRIS/D-Bus and tray integration
```

The existing native macOS application, its Xcode project, and its test targets
live together under `src/Reprise.Platform.MacOS`. They remain buildable during
the migration and serve as the macOS implementation until the shared desktop
application reaches feature parity.

## Test layout

```text
tests/
├── unit/
│   └── Reprise.Core.Tests/
├── contracts/
│   └── Reprise.Platform.Contracts.Tests/
├── integration/
│   ├── macos/{common,x64,arm64}/
│   ├── windows/{common,x86,x64,arm64}/
│   └── linux/{common,x64,arm64}/
├── packaging/
│   ├── macos/{x64,arm64,universal}/
│   ├── windows/{x86,x64,arm64}/
│   └── linux/{x64,arm64}/
├── fixtures/
└── matrix/
```

Unit and platform-contract tests are source-shared and run for every supported
target. Only tests that exercise native APIs or generated installers belong in
an operating-system or architecture directory. `x64` and `amd64` refer to the
same target architecture and are represented by `x64` throughout the
repository.

Operating-system versions and Linux display backends are CI matrix dimensions,
not duplicated test source directories. This allows the same Linux integration
suite to run on supported Ubuntu releases under X11 and Wayland, and the same
platform suite to run on multiple macOS and Windows versions.

## Packaging layout

Platform-specific installers, signing configuration, and release metadata
belong under `packaging/macos`, `packaging/windows`, and `packaging/linux`.
Build workflows belong under `.github/workflows` and should validate all
supported operating systems before changes reach the main branch.

## Browser extension

Browser-independent extension sources will move into
`BrowserExtension/Shared`. Chromium and Mozilla directories remain packaging
targets for browser-specific manifests and generated artifacts.

## Branch and version policy

- `main` remains the single integration branch.
- `migration/cross-platform` contains the initial migration scaffold.
- New work uses short-lived `feature/*` and `fix/*` branches.
- Creating the scaffold does not change the application version.
- The first public release of the unified cross-platform application will be
  `2.0.0`; development before that release can use prerelease identifiers such
  as `2.0.0-alpha.1`.
