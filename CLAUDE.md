# Lanting

Lightweight Dynamic Island for Windows 11. C# + WinUI 3 on .NET 10, Windows App SDK 1.8, WinUIEx. Unpackaged, self-contained, win-x64.

## Working with Hanif

- Reply in Indonesian, casual and simple. Hanif is new to git, PRs and CI: explain jargon in one plain sentence and give click-by-click steps.
- Commits must not mention Claude: no `Co-Authored-By`, no `Claude-Session` trailer, no "Generated with Claude Code" footer in PR bodies. Commit as Hanif's own git identity.
- Plan and milestones: see `README.md` (Status table). Do the next milestone only when Hanif asks.

## Layout

- `src/Lanting.Core`: no UI. `IslandController` picks the content to show (priority, then most recent). `IslandStateMachine` handles Hidden, Compact, Expanded and Notification. `IIslandModule` and `ModuleHost` isolate module faults.
  - `Lyrics/`: `LrcParser`, `TrackQuery` (cleans YouTube-style titles), `LrcLibClient` (LRCLIB search + memory cache). `MediaModule` drives lyric lines with one timer per line via `PlaybackClock`; Firefox/Zen report no timeline, so position is estimated from the observed track start.
  - `Media/`: `ArtworkColor` picks the accent color from album art. `MediaModule` turns `IMediaSource` updates into the `media/now-playing` content (payload `NowPlaying`).
  - `Weather/WeatherService.cs`: IP location (ipwho.is) + Open-Meteo, fetched only when the dashboard opens and the data is older than 30 min. `Clock/DayProgress.cs`.
  - `IslandStateMachine` opens the island only after the pointer rests on it for `IslandOptions.ExpandDelay` (250 ms), because the island now sits on the top screen edge where browser tabs are.
  - `Battery/`: `BatteryModule` announces connect/disconnect (Normal, 3 s) and low/critical battery (Alert/Critical notifications), each warning once until the battery is recharged above `RearmPercent`.
- `src/Lanting.App`: WinUI shell.
  - `IslandWindow.cs` builds its UI **in code, not XAML**. `IslandWindow.xaml` threw XamlParseException at startup, so keep new window UI in code.
  - The island is attached to the top screen edge (`TopMargin = 0`; `PillAnimator` shifts the clip up by the corner radius so only the bottom corners are rounded).
  - `Views/`: one `IIslandView` per content type (`MediaView`, `BatteryView`; optional `Notification` element for the 360x64 pill), plus `IdleView` (clock) as the fallback. `IslandWindow` picks the first view whose `CanShow` matches for the compact pill.
  - `Views/Dashboard/`: the expanded island is always `DashboardView` (header with date/clock/settings button, then `MediaView.Card`, `WeatherCard`, `TogglesCard`, `AppsCard`). `ClockTicker` ticks once per minute.
  - `QuickToggles/`: `IQuickToggle` implementations (Windows radios for Wi-Fi/Bluetooth, Core Audio mute for speaker/mic, `SetThreadExecutionState` keep-awake, registry dark mode).
    `Motion` holds shared composition animations; `Visualizer` costs about 8% of one core while music plays (60 fps compositor), 0% when paused.
  - `Battery/PnpBatterySource.cs`: DeviceWatcher on Bluetooth device nodes, reading `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (battery) and `{83DA6326-97A6-4088-9453-A1923F573B29} 15` (connected), grouped by ContainerId. Not yet tested with a real Bluetooth keyboard/mouse.
  - `Media/SmtcMediaSource.cs`: reads Windows SMTC (event based) and sends play/pause/next/previous.
  - `SettingsWindow.cs` (code-built, Mica) writes to `SettingsStore` (Core, `%LOCALAPPDATA%\Lanting\settings.json`); `SettingsApplier` pushes settings live to the state machine, modules and views.
  - `Shell/TrayMenu.cs` uses WinUIEx `TrayIcon`; `Shell/FullscreenWatcher.cs` registers the island as an appbar to receive `ABN_FULLSCREENAPP`; `Shell/AutoStart.cs` uses the HKCU Run key (same value the installer writes).
  - `Shell/` holds the native window chrome, the composition spring animator (`PillAnimator`), `AppsFolder` (app name from an AppUserModelId) and `StartupLog`.
- `tests/Lanting.Core.Tests`: xunit tests for Core, using FakeTimeProvider.

## Commands

```powershell
dotnet test tests/Lanting.Core.Tests
dotnet build src/Lanting.App            # F5 in VS Code builds and launches Debug
```

- The startup log is `%LOCALAPPDATA%\Lanting\lanting.log`. Read it first when the app does not appear.
- CI (`.github/workflows/build.yml`) runs the tests, the build and a publish, then a smoke test that launches the exe (with `LANTING_SMOKE_EXPANDED=1` and `LANTING_SMOKE_SETTINGS=1` it also opens the dashboard and Settings) and fails on `FATAL` in the log, then builds the Inno Setup installer (`installer/Lanting.iss`).
- Verify UI changes on a Release publish too: theme-resource lookups from code failed only in Release (v0.1.0 Settings crash).
- Keep icon glyphs as `\uXXXX` escapes in C# source; editors show private-use characters as blanks.
- Releases: bump `<Version>` in `Directory.Build.props`, then push a `vX.Y.Z` tag; `.github/workflows/release.yml` publishes the installer and a portable zip.
