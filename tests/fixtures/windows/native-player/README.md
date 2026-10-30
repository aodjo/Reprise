# Interactive Windows media verification

This diagnostic creates an actual Windows MediaPlayer session playing generated
silence. It is test-only code and is never packaged with Reprise. Run it as the
logged-in desktop user; session 0 cannot represent interactive media players.
The fixture owns a Windows STA message pump, matching the real application's
WinRT calling context. An ordinary async console provider can fail with
RPC_E_WRONG_THREAD and is not an equivalent playback test.

```powershell
dotnet publish tests/fixtures/windows/native-player/NativePlayer.csproj -c Release -r win-arm64 --self-contained true -o Artifacts/native-player
Artifacts/native-player/Reprise.Platform.Windows.Tests.exe --verify-and-exit
Get-Content Artifacts/native-player/result.json
```

Use `win-x64` for Intel/AMD Windows. Omit `--verify-and-exit` to keep the player
available while inspecting Reprise's panel, artwork, and controls. Create a file
named `stop` beside the fixture executable to exit; remove it before running
again. The result verifies native discovery, stable session identity, artwork,
pause/play status, position change, and next/previous acceptance. It returns a
nonzero exit status when any assertion fails. Do not interpret the fixture as
verification against Spotify, Apple Music, or every browser implementation.

Run with `--taskbar-lyrics` to exercise the real taskbar publisher with explicitly
test-only Korean lyric lines. It checks actual native HWND/client bounds against
the taskbar height, cover visibility and decoding, intermediate push offsets,
rapid line changes, track changes, and hiding on disable or player removal.
When a real player publishes artwork, it uses the actual GSMTC thumbnail through
the production artwork loader and PNG renderer. Otherwise it uses the fixture
icon. `taskbar-result.json` records the results. `taskbar-rest.png`,
`taskbar-transition.png`, and `taskbar-finished.png` render its own transparent
surface; these are not desktop screenshots. The diagnostic remains open until
the `stop` file is created; add `--verify-and-exit` to exit after verification.

`--verify-settings` constructs the real Windows settings window, opens all six
tabs in three rounds, changes the taskbar lyric preference in an isolated file,
and closes/reopens the same window. It writes `settings-result.json` and one
own-surface PNG render per tab, then exits nonzero on failure. Windows CI runs
this regression check for each architecture. It never changes login startup or
the installed application's preferences.

`--verify-fade` compares the real initial sentence render with a fully opaque
leading edge, including disabled scrolling. It writes `fade-result.json` and
own-surface PNGs; Windows CI runs it for both architectures.

`--verify-lyrics-highlight` opens the production player with isolated timed lyric
fixtures, then measures current-row and adjacent-row glyph alpha in its actual
renderer. It checks an active line, a gap after the provider's explicit end time,
and hiding/reopening after the position changes while paused. The current line
must remain opaque and its neighbor dimmed, matching the macOS sheet. It writes
`lyrics-result.json` and own-surface PNGs, and exits nonzero on lost emphasis.
It never controls a real media player or changes the user's saved preferences.

`--verify-material` puts an owned stripe-pattern window behind the real player,
captures only the diagnostic-owned native lens HWND, changes the source colors
without rebuilding its DWM connections, and tests theme, hide, and reopen
transitions. `material-result.json`, `material-native.png`, and
`material-live.png` record the native output. This diagnostic never captures a
monitor or changes any window's screen-recording affinity. A real interactive
Windows desktop is required; it is a manual renderer check, not a headless test.
It checks white title/lyric foregrounds and measures residual stripe contrast
in both the center and the side bevel, rejecting a sharp border around the frost.
It requires a transparent foreground with no additional host-backdrop brush,
composes the owned live lens and actual foreground, and measures color/chroma
change in the resulting image. This rejects checking the lens in isolation
while an unrelated Acrylic layer covers it. The composed PNGs are diagnostic
renders of those two owned layers, not monitor screenshots. `--verify-ui` runs the material,
sentence fade, lyric emphasis, and settings checks together.


To verify the production PowerShell/NSIS handoff without a network connection,
first exit Reprise and run the fixture with `--verify-update update.json Setup.exe`.
Use real build outputs of the same stage and architecture and place the installer
in its own disposable directory: the helper removes that directory after success.
The fixture checks the installer's size, SHA-256, and product version, starts the
production helper, and exits. Verify the installed HKCU registration and that
Reprise reopens. This test does not verify the app's network download transport.
