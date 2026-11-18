# WCC test tools

Shared symbol: `WCC_TEST_TOOLS`. Runtime tools and their gameplay hooks are compiled only while this symbol is enabled. Tools are created at runtime; do not attach them to saved scenes or prefabs.

## Unity menus

- `Tools > Test Tools > Enable WCC_TEST_TOOLS`: enable tools for Standalone and the Editor.
- `Tools > Test Tools > Disable WCC_TEST_TOOLS`: remove the symbol before committing or making a normal build.
- `Tools > Test Tools > Check Before Commit`: inspect current platform/profile settings.
- `Tools > Test Tools > Build Windows Test Player`: create a Windows Development build with test tools, Profiler connection and frame timing. Other settings are restored after the build. Wait for compilation after changing symbols.

Normal builds are blocked while test symbols are enabled. The dedicated build menu supplies the symbol to that build, so it can also be used with the saved project symbol disabled.

## Stationary camera measurement

1. Run the test player, join a room, and remain in the lobby.
2. Stand still at the desired position and choose the starting view/pitch.
3. Press **F8**. Local input is blocked while the camera rotates at 30 degrees per second.
4. Default sequence: one initial lap, one warmup lap, ten measured laps (144 seconds total).
5. Press **F8** again to cancel and save partial results. Focus loss, leaving the lobby, camera changes, player movement or changed rendering settings also abort the measurement.
6. Results are saved under `Application.persistentDataPath/TestResults/CameraPerformance/lobby-camera-...`: `frames.csv` and `summary.json`. The actual path appears in the status and Player.log. The starting view and input state are restored afterward.

The initial lap is not a guaranteed cold process. Restart the player for a fresh-process comparison. Quality, resolution, VSync and frame caps are recorded, not silently changed. Missing metrics are `NA`; asynchronous FrameTimingManager values are not exact per-angle timings. Editor and build measurements must be compared separately. The tool does not infer lower-spec hardware performance.

## Before every commit

Disable the symbol in Unity, wait for compilation, save settings, and re-stage corrected settings if needed. Then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TestTools/Check-BeforeCommit.ps1
```

The script checks both working-tree and staged `.asset` settings/profiles. Exit code 1 means the symbol remains and the commit must wait. It does not reject `#if WCC_TEST_TOOLS` source code. This is an explicit check, not an installed Git hook.
