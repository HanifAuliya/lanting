# Lanting

Lightweight Dynamic Island for Windows 11. C# + WinUI 3 on .NET 10, Windows App SDK 1.8, WinUIEx. Unpackaged, self-contained, win-x64.

## Working with Hanif

- Reply in Indonesian, casual and simple. Hanif is new to git, PRs and CI: explain jargon in one plain sentence and give click-by-click steps.
- Commits must not mention Claude: no `Co-Authored-By`, no `Claude-Session` trailer, no "Generated with Claude Code" footer in PR bodies. Commit as Hanif's own git identity.
- Plan and milestones: see `README.md` (Status table). Do the next milestone only when Hanif asks.

## Layout

- `src/Lanting.Core`: no UI. `IslandController` picks the content to show (priority, then most recent). `IslandStateMachine` handles Hidden, Compact, Expanded and Notification. `IIslandModule` and `ModuleHost` isolate module faults.
- `src/Lanting.App`: WinUI shell.
  - `IslandWindow.cs` builds its UI **in code, not XAML**. `IslandWindow.xaml` threw XamlParseException at startup, so keep new window UI in code.
  - `Shell/` holds the native window chrome, the composition spring animator (`PillAnimator`) and `StartupLog`.
- `tests/Lanting.Core.Tests`: xunit tests for Core, using FakeTimeProvider.

## Commands

```powershell
dotnet test tests/Lanting.Core.Tests
dotnet build src/Lanting.App            # F5 in VS Code builds and launches Debug
```

- The startup log is `%LOCALAPPDATA%\Lanting\lanting.log`. Read it first when the app does not appear.
- CI (`.github/workflows/build.yml`) runs the tests, the build and a publish, then a smoke test that launches the exe and fails on `FATAL` in the log.
