# RevReel

RevReel — a simple Windows screen recorder with bookmarks and instant replay GIFs. By Rev Creative Developer.

> Status: early development (v0.1.0). Records the whole primary screen to MP4.
> Start and stop with the buttons (Alt+R to record, Alt+S to stop).
> Menu bar: File (open save location, play/show last recording, exit),
> Option (save folder, frame rate, output size, keep on top),
> Help (About, check for updates, keyboard shortcuts, GitHub, report a problem).

## Planned features

- Record the whole screen, one window, or a custom area (MP4).
- Bookmarks: mark a moment while recording, export a clip or GIF later.
- Instant replay GIFs of the last 3, 5 or 10 seconds, on global hotkeys.
- Written status text and visual indicators only. No feature depends on sound.
- Dark and Light themes.

## Requirements

- Windows 10 (1809 or later) or Windows 11, 64-bit.
- Build: .NET 8 SDK, and the Windows 10 SDK 10.0.19041.0
  (installed with Visual Studio 2022's ".NET desktop development" workload).

## Build and run (Visual Studio terminal)

```
dotnet restore RevReel.sln
dotnet build RevReel.sln -c Debug -p:Platform=x64
dotnet run --project src\RevReel\RevReel.csproj -c Debug -p:Platform=x64
```

Recordings are saved to `Videos\RevReel` by default (change it in Option). Settings are stored in `%AppData%\RevReel\settings.json`.

The update check reads the latest GitHub Release. Tag releases like `v0.2.0`.

## Project layout

```
RevReel.sln
src/RevReel/
  Models/      data types (recording state)
  Services/    recording engine wrapper
  Views/       windows and dialogs (later)
  Resources/   colours and shared styles
  Assets/      icons and logo
docs/          notes and design decisions
Installer/     installer script (output is not committed)
```

## Third-party software

- [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) (MIT).

## License

MIT. See `LICENSE`.
