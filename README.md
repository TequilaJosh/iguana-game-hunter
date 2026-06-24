# 🦎 LazerGuanas Game Hunter

A reptilian-themed WPF desktop app for tracking your game backlog as a kanban board —
**Dormant → Hunting → Devoured** — with per-game play-session time tracking.

## Features

- **Kanban board** with drag-and-drop and right-click to move games between columns
- **Play-session timer** — Start at the beginning of a stream, Stop at the end; the app
  banks the elapsed time and totals it per game. Manual entry and per-session notes too.
- **Requester tracking** — note who asked for each game
- **Search** across title, platform, genre, and requester
- **Custom reptilian theme** with an iguana-eye icon and custom title bar
- **Auto-update** — checks GitHub Releases on launch and offers to install newer versions

## Installing

Download the latest `LazerGuanas-Game-Hunter-Setup-x.y.z.exe` from the
[Releases page](https://github.com/TequilaJosh/iguana-game-hunter/releases/latest)
and run it. The app checks for updates automatically on each launch (and via the ⟳
button in the title bar).

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) (Windows).

```sh
dotnet build
dotnet run
```

### Producing an installer locally

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php). Then:

```sh
build-installer.bat
```

This publishes a self-contained single-file build and compiles
`installer/LazerGuanas-Game-Hunter-Setup-<version>.exe`.

### Cutting a release

Just run:

```sh
release.bat            # bump patch: 1.0.2 -> 1.0.3
release.bat minor      # 1.0.3 -> 1.1.0
release.bat major      # 1.1.0 -> 2.0.0
release.bat 1.4.2      # set an explicit version
```

`release.bat` bumps the version in `GameTracker.csproj`, validates the build, commits,
and pushes a `v<version>` tag. GitHub Actions then builds the installer `.exe` and
publishes the GitHub Release — which the auto-updater in the wild picks up automatically.

> The version lives in `GameTracker.csproj` only, so the app, the installer filename, and
> the release tag always match. If the target tag already exists, `release.bat` aborts
> before changing anything.
