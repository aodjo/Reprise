# Reprise for macOS

This directory contains the existing native SwiftUI/AppKit application and all
of its Xcode test targets.

```text
Reprise.xcodeproj/  Xcode project and Swift package resolution
Reprise/            Application source and assets
RepriseTests/       Unit and integration tests
RepriseUITests/     UI tests
```

Open the project from the repository root with:

```bash
open src/Reprise.Platform.MacOS/Reprise.xcodeproj
```

The shared browser extension remains at the repository root and is referenced
from the Xcode project through a relative path.
