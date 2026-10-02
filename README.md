# YouTube Music Taskbar Widget

A tiny always-on-top overlay that lives in your Windows taskbar area and shows what's playing — with live, word-by-word synced lyrics.

Built with WPF on .NET 8. No dependencies, no background service, no telemetry. Just one self-contained `.exe`.

## Features

- **Now playing** — title, artist, and album art, pulled from Windows' System Media Transport Controls (SMTC).
- **Transport controls** — previous, play/pause, next, sent to whatever app is playing.
- **Synced lyrics** — fetched from [LRCLIB](https://lrclib.net) when a track changes. Falls back to a plain `♫` while the song intro runs, or `-` if no lyrics are found.
- **Karaoke word highlighting** — each word lights up as it's sung. Uses real word-level timestamps when the LRC file has them, and estimates them when it doesn't.
- **Horizontally scrolling lyrics** — the current line pans left as words light up, so the active word stays in view.
- **Marquee title** — long `Title - Artist` strings scroll instead of being cut off, pausing briefly at the seam. Pause playback and the scroll stops and rewinds to the start.
- **Sound-wave indicator** — a 4-bar animation that runs while something is playing.
- **Never hides itself** — the widget stays in the taskbar whether or not anything is playing. Your last track stays on screen until you quit it.
- **Stays on top** — re-pins itself above other windows as they gain focus, so it never disappears behind your apps.
- **Starts with Windows** — optional, selected during install.

## Install

Grab the latest release from the [Releases page](../../releases), then either:

| Asset | What it is |
|---|---|
| `YTMTaskbarWidget-Setup-0.1.0.exe` | **Recommended.** Inno Setup installer with a "Start with Windows" checkbox. |
| `YTMTaskbarWidget-0.1.0-win-x64.zip` | Portable single-file `.exe`. Unzip anywhere and run it. |

> [!IMPORTANT]
> The build is **not code-signed**, so SmartScreen will warn you on first run.
> Click **More info → Run anyway**. You can verify the executable is self-contained
> and dependency-free before running it.

## Controls

| Action | Result |
|---|---|
| **Drag the grip** (the dotted handle) | Move the widget left/right along the taskbar. Position is remembered between runs. |
| Click **⏭ / ⏯ / ⏮** | Next, play/pause, previous track. |
| **Double-right-click** | Quit the widget. |
| Left-click anywhere else | Does nothing — it's not a window you activate. |

There is no tray icon, no context menu, and no settings window. The widget is deliberately a single surface you glance at.

## Requirements

- Windows 10 version 2004 (build 19041) or newer
- 64-bit (x64)
- Something playing with visible SMTC metadata — the widget prefers **YouTube Music in Chrome, Edge, or Brave**, but falls back to whatever media session is currently active
- A network connection for lyrics (LRCLIB, 10-second timeout); everything else works offline

## How it fits the taskbar

The widget is a borderless, transparent, always-on-top window with no taskbar button of its own. It sizes itself to `352 × 56` px and centers itself horizontally on your **primary monitor's bottom edge**, riding on top of the taskbar strip.

> [!NOTE]
> Only the bottom taskbar on the primary monitor is supported. A taskbar docked to
> the left, right, or top, or a secondary monitor, is not currently detected.

Your horizontal offset from center is saved to:

```
%LocalAppData%\YTMTaskbarWidget\taskbar-offset.txt
```

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned to `8.0.100` in `global.json`, rolling forward to the latest feature band).

```powershell
git clone https://github.com/51-Shenn/ytmusic-taskbar-widget.git
cd ytmusic-taskbar-widget

dotnet restore YTMTaskbarWidget.sln
dotnet build   YTMTaskbarWidget.sln -c Release --no-restore
dotnet test    YTMTaskbarWidget.sln -c Release --no-build --nologo
```

### Publish a release build

```powershell
pwsh ./build/publish.ps1
```

This is the single source of truth for publish flags. It produces `publish\YTMTaskbarWidget.exe` — a **self-contained, single-file** binary (~75 MB) that needs no .NET runtime installed. Trimming stays off because it breaks WPF XAML.

Keep the "Build from source" section of this README in sync with that script.

### Build the installer

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then compile the script:

```powershell
& "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe" installer\YTMTaskbarWidget.iss
# machine-wide install instead:
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\YTMTaskbarWidget.iss
```

Output lands in `installer\Output\` (gitignored).

## Testing

Tests live in `src/YTMTaskbarWidget.Tests/` and use **xUnit** — 48 tests covering LRC parsing, LRCLIB client behavior (via a stubbed `HttpMessageHandler`, no network), marquee math, taskbar positioning, and widget chrome. Run them with:

```powershell
dotnet test YTMTaskbarWidget.sln --nologo
```

CI runs restore → build → test on every push and pull request to `main` (`.github/workflows/ci.yml`).

## Cutting a release

1. Bump `<Version>` in `src/YTMTaskbarWidget/YTMTaskbarWidget.csproj` — **it must match the tag.**
2. Commit, merge to `main`, then tag and push:

   ```powershell
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. The `Release` workflow (`.github/workflows/release.yml`) publishes the exe, compiles the installer, zips the binary, and attaches both assets to the GitHub release.

## Project layout

```
build/publish.ps1              Release publish flags
installer/YTMTaskbarWidget.iss Inno Setup installer script
src/YTMTaskbarWidget/          The WPF app
src/YTMTaskbarWidget.Tests/    xUnit tests
.github/workflows/             CI + release automation
```

## Known limitations

- Bottom taskbar on the primary monitor only — no left/right/top docks, no secondary monitors.
- Not code-signed, so SmartScreen will warn on first launch.
- No settings UI. The horizontal offset is the only persisted setting.
- No tray icon and no system-wide hotkeys; transport control goes through SMTC rather than synthesized keystrokes.

## License

[MIT](LICENSE) © 2026 Tan Yit Shen
