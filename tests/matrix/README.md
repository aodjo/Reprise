# Test matrix

CI target definitions will live here rather than being encoded into test
source paths. Each target records these dimensions:

- operating system and supported release
- CPU architecture
- runtime identifier
- native execution or emulation
- desktop session where relevant (`X11` or `Wayland`)
- package format and installation mode

The active hosted-runner targets are defined in `targets.json`. GitHub Actions
validates that file, rejects duplicate target names, and expands its entries
into the build-and-test matrix.

Architecture-specific integration tests must execute on the matching CPU.
Cross-compilation alone verifies that an artifact can be produced; it does not
count as a passing native integration test.

The initial target families are:

```text
macos-<version>-x64
macos-<version>-arm64
windows-<version>-x86
windows-<version>-x64
windows-<version>-arm64
ubuntu-<version>-x64-<x11|wayland>
ubuntu-<version>-arm64-<x11|wayland>
```

Exact supported OS releases will be selected when CI workflows and packaging
targets are implemented.
