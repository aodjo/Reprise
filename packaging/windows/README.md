# Windows packaging

Build on macOS with .NET 10 and NSIS (`brew install makensis`), or on Windows
with .NET 10 and NSIS installed. The self-contained application needs Windows
10 1809 or later. Intel/AMD x64 is the default; ARM64 and x86 are also supported.

```sh
python3 packaging/windows/build.py --architecture x64
python3 packaging/windows/build.py --architecture arm64 --build-number 20261029010000
```

Outputs live in `Artifacts/windows-{architecture}/{buildNumber}`: a per-user
NSIS installer, portable ZIP, application payload, and update.json. No VM shared
folder is used. Supply the same UTC build number for each architecture in one
release. VersionPrefix/VersionSuffix determine the release stage. The installer
uses independent per-stage, per-architecture registry keys and destinations.

```sh
python3 packaging/windows/publish.py Artifacts/windows-x64/BUILD --dry-run
python3 packaging/windows/publish.py Artifacts/windows-x64/BUILD Artifacts/windows-arm64/BUILD
```

Publication validates the application PE architecture, metadata, immutable URL,
size, and digest before updating the apt-repo artifact branch. It preserves
Linux packages and publishes Windows feeds under
`https://aodjo.github.io/Reprise/windows/{alpha|beta|release}/{x64|arm64|x86}/update.json`.
GitHub Pages serves the installers and download index at `/Reprise/windows/`.
Publication sparsely checks out feed metadata, workflow files, and the requested
installer filenames, so previous installer binaries are not downloaded again.
Only those changed paths are staged; existing Windows and Linux archives remain
in the artifact branch. Reusing a published filename still checks its contents.

The Pages staging workflow serves the three newest Windows installers per
stage/architecture and always preserves the active manifest's download. Older
installers remain unchanged in the `apt-repo` artifact branch and local build
directories; only the temporary Pages staging copy is pruned. Linux content is
copied unchanged. This keeps repeated alpha builds below Pages' site-size limit.
The application never crosses release stages or architectures, and a feed
cannot be rolled back by the publishing command. Keep development installers
on alpha. Signing is not configured; these installers are unsigned.

Run `dotnet test Reprise.slnx` for managed tests. Native broker tests explicitly
skip on non-Windows hosts. Windows CI tests each configured architecture,
publishes a self-contained payload, checks its executable, and builds an
installer. A real interactive Windows session is needed to verify playback,
tray placement, and the installer handoff end to end.
