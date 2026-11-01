# macOS distribution

Build the Universal release application with both `arm64` and `x86_64`:

```sh
xcodebuild build -project src/Reprise.Platform.MacOS/Reprise.xcodeproj \
  -scheme Reprise -configuration Release -destination 'generic/platform=macOS' \
  -derivedDataPath Artifacts/macos-build 'ARCHS=arm64 x86_64' ONLY_ACTIVE_ARCH=NO
```

Package with an installed Developer ID identity, the existing Sparkle signing
key, and an existing `notarytool` Keychain profile:

```sh
python3 packaging/macos/package.py \
  Artifacts/macos-build/Build/Products/Release/Reprise.app Artifacts/macos-release \
  --identity 'Developer ID Application: NAME (TEAM)' \
  --notary-profile PROFILE \
  --sparkle-tools Artifacts/macos-build/SourcePackages/artifacts/sparkle/Sparkle/bin
```

The packager signs nested Sparkle helpers separately, removes debugging rights
from the app, requires accepted notarization for the app and DMG, staples both,
and verifies Gatekeeper acceptance. It emits a Universal DMG, a stapled update
ZIP, and the Sparkle signature for the ZIP. Credentials remain in the Keychain.

Upload the final ZIP before publishing its signed enclosure in `appcast.xml`.
Never edit the ZIP after signing or reuse an existing release filename with
different contents. The stable feed remains on `main`; prerelease feeds must
remain separate.

The signing order follows [Sparkle's sandbox distribution guide](https://sparkle-project.org/documentation/sandboxing/#code-signing).
