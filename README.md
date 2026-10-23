# Spectre

A lightweight desktop app that tracks how much time you spend in chosen apps, runs quietly in the system tray, no manual start/stop needed.

## Features

- Tracks any process by name; logs start/stop times automatically
- Lives in the system tray — closing the window just hides it
- Single instance — launching Spectre again brings up the running window
- Per-app session history, editable name/process, deletable
- Configurable poll and UI refresh intervals
- Fast startup, published as a single native AOT executable — no .NET runtime install required

## Tech

- .NET 10, Avalonia UI 12 (MVVM via CommunityToolkit.Mvvm)
- SQLite (Dapper.AOT, WAL mode) for storage, JSON for config
- Split into `Spectre.Core` (data/tracking logic) and `Spectre.UI` (Avalonia app)

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run --project Spectre.UI
```

## Publishing

Native AOT needs your platform's native toolchain (e.g. `clang` on Linux).

```bash
dotnet publish Spectre.UI -c Release -r linux-x64 -o ./publish/linux-x64
```

Use `win-x64` or `osx-arm64` for other platforms.

## License

GNU GPL v3
