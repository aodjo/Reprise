# Cross-platform tests

Tests are separated by responsibility before they are separated by operating
system or CPU architecture.

```text
unit/          Pure shared behavior; runs in every CI target
contracts/     Rules every platform adapter must satisfy
integration/   Native media, tray, startup, and operating-system integration
packaging/     Installer launch, upgrade, uninstall, and signature smoke tests
fixtures/      Language-neutral protocol and media-session samples
matrix/        Supported OS, CPU, runtime, and display-session combinations
```

The `common` integration directory holds tests shared by every architecture of
one operating system. Architecture directories contain only behavior that
cannot be covered by the common suite.

Architecture names are normalized as follows:

- macOS: `x64`, `arm64`
- Windows: `x86`, `x64`, `arm64`
- Linux/Ubuntu: `x64`, `arm64`

`amd64` is another name for `x64`, not an additional test target. Linux
distribution versions, `glibc` compatibility, and X11/Wayland sessions are
represented by CI matrix entries so the same test sources can run in each
environment.
