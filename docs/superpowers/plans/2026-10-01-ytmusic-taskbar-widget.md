# YTM Taskbar Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C# WPF always-on-top taskbar overlay that shows Browser YTM now-playing + single synced lyric line with prev/play/next, exactly like the screenshot.

**Architecture:** `SmtcService` polls Windows SMTC for Browser YTM session; `LrclibClient` + `LrcParser` resolve current lyric line by playback position; `MainWindow` renders thumbnail + 2 text lines + 3 buttons; `TaskbarPositioner` docks the borderless window to Win11 bottom-center taskbar.

**Tech Stack:** .NET 8 SDK, WPF net8.0-windows10.0.19041.0, Windows.Media.Control (SMTC), System.Net.Http.Json, xUnit for parser/client tests.

---

## File Structure

New files (all under repo root):

- `src/YTMTaskbarWidget/Models/NowPlaying.cs` — immutable DTO: Title, Artist, ThumbnailBytes, Status, Position.
- `src/YTMTaskbarWidget/Services/LrcParser.cs` — pure `[mm:ss.xx] text` parser + binary-search lookup. No IO.
- `src/YTMTaskbarWidget/Services/LrclibClient.cs` — `GET https://lrclib.net/api/get?track_name=&artist_name=` with HttpClient injection, returns `LrcResult`.
- `src/YTMTaskbarWidget/Services/SmtcService.cs` — wraps `GlobalSystemMediaTransportControlsSessionManager`, finds Browser YTM session, exposes `GetNowPlayingAsync()` + `TryToggle/Next/PreviousAsync()`.
- `src/YTMTaskbarWidget/Services/TaskbarPositioner.cs` — pure math: given workingArea/taskbarHeight/dpi returns Left/Top. WPF window applies it.
- `src/YTMTaskbarWidget/MainWindow.xaml` (modify) — exact-clone layout: thumb + 2 lines + prev/play/next.
- `src/YTMTaskbarWidget/MainWindow.xaml.cs` (modify) — 500ms DispatcherTimer polling, thumbnail load, lyric sync, hide-when-idle, WS_EX_NOACTIVATE/TOOLWINDOW.
- `src/YTMTaskbarWidget.Tests/YTMTaskbarWidget.Tests.csproj` — xUnit net8.0-windows.
- `src/YTMTaskbarWidget.Tests/LrcParserTests.cs` — parser tests.
- `src/YTMTaskbarWidget.Tests/LrclibClientTests.cs` — mocked HttpMessageHandler tests.
- `src/YTMTaskbarWidget.Tests/TaskbarPositionerTests.cs` — position math tests.

Modified:
- `src/YTMTaskbarWidget/YTMTaskbarWidget.csproj` — add HttpClient already in-box, no extra package needed for SMTC on this TFM.
- `YTMTaskbarWidget.sln` — add test project.

---

### Task 0: SDK + Build Baseline

**Files:**
- Modify: `global.json` (already pins 8.0.100 rollForward latestFeature — keep)

- [ ] **Step 1: Install .NET 8 SDK and verify**

Run: `winget install Microsoft.DotNet.SDK.8 --silent --accept-source-agreements --accept-package-agreements`
Then run: `dotnet --version`
Expected: `8.0.xxx`

If `dotnet` still not on PATH, use VS path once then restart shell:
Run: `$env:PATH += ";C:\Program Files\dotnet"` then `dotnet --version`

- [ ] **Step 2: Verify scaffold builds**

Run: `dotnet build YTMTaskbarWidget.sln -c Debug`
Expected: `Build succeeded.` with `YTMTaskbarWidget.dll`

- [ ] **Step 3: Commit if global.json changed, else skip**

Only if you touched files:
```bash
git add -A
git commit -m "chore: verify net8 build baseline"
```

---

### Task 1: Models + LrcParser (pure, TDD)

**Files:**
- Create: `src/YTMTaskbarWidget/Models/NowPlaying.cs`
- Create: `src/YTMTaskbarWidget/Services/LrcParser.cs`
- Create: `src/YTMTaskbarWidget.Tests/YTMTaskbarWidget.Tests.csproj`
- Create: `src/YTMTaskbarWidget.Tests/LrcParserTests.cs`

- [ ] **Step 1: Create test project csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\YTMTaskbarWidget\YTMTaskbarWidget.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing LrcParser test**

```csharp
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget.Tests;

public sealed class LrcParserTests
{
    [Fact]
    public void Parses_Synced_Lines_And_Finds_Current()
    {
        const string lrc = "[00:10.00]line one\n[00:20.50]line two\n[00:30.00]line three\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Equal(3, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Timestamp);
        Assert.Equal("line one", lines[0].Text);
        Assert.Equal("line one", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(15)));
        Assert.Equal("line two", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(20.5)));
        Assert.Equal("line three", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(99)));
    }

    [Fact]
    public void Ignores_Metadata_And_Empty()
    {
        const string lrc = "[ti:Title]\n[ar:Artist]\n[00:05.00]hi\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Single(lines);
        Assert.Equal("hi", lines[0].Text);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/YTMTaskbarWidget.Tests/YTMTaskbarWidget.Tests.csproj --filter "LrcParserTests" -v n`
Expected: FAIL — `LrcParser` not found.

- [ ] **Step 4: Write minimal implementation**

`src/YTMTaskbarWidget/Models/NowPlaying.cs`:
```csharp
using Windows.Media.Control;

namespace YTMTaskbarWidget.Models;

public sealed record NowPlaying(
    string Title,
    string Artist,
    byte[]? ThumbnailBytes,
    GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
    TimeSpan Position);
```

`src/YTMTaskbarWidget/Services/LrcParser.cs`:
```csharp
using System.Text.RegularExpressions;

namespace YTMTaskbarWidget.Services;

public sealed record LrcLine(TimeSpan Timestamp, string Text);

public static partial class LrcParser
{
    [GeneratedRegex(@"^\[(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<x>\d{1,3}))?\](?<t>.*)$")]
    private static partial Regex LineRegex();

    public static List<LrcLine> Parse(string? syncedLyrics)
    {
        var out_ = new List<LrcLine>();
        if (string.IsNullOrWhiteSpace(syncedLyrics)) return out_;
        foreach (var raw in syncedLyrics.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            var m = LineRegex().Match(line);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups["m"].Value, out var min)) continue;
            if (!double.TryParse(m.Groups["s"].Value, out var sec)) continue;
            var frac = m.Groups["x"].Value;
            var ms = 0;
            if (frac.Length == 2 && int.TryParse(frac, out var cs)) ms = cs * 10;
            else if (frac.Length == 3 && int.TryParse(frac, out var ms3)) ms = ms3;
            var text = m.Groups["t"].Value.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("ti:", StringComparison.OrdinalIgnoreCase)) continue;
            if (text.StartsWith("ar:", StringComparison.OrdinalIgnoreCase)) continue;
            out_.Add(new LrcLine(new TimeSpan(0, 0, min, (int)sec, ms), text));
        }
        out_.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return out_;
    }

    public static string? CurrentLine(List<LrcLine> lines, TimeSpan position)
    {
        string? cur = null;
        foreach (var l in lines)
        {
            if (l.Timestamp <= position) cur = l.Text;
            else break;
        }
        return cur;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test src/YTMTaskbarWidget.Tests/YTMTaskbarWidget.Tests.csproj --filter "LrcParserTests" -v n`
Expected: `Passed! - Failed: 0`

- [ ] **Step 6: Add test project to solution + commit**

```bash
dotnet sln YTMTaskbarWidget.sln add src/YTMTaskbarWidget.Tests/YTMTaskbarWidget.Tests.csproj
git add src/YTMTaskbarWidget/Models/NowPlaying.cs src/YTMTaskbarWidget/Services/LrcParser.cs src/YTMTaskbarWidget.Tests/ YTMTaskbarWidget.sln
git commit -m "feat: add NowPlaying model and synced lyrics parser with tests"
```

---

### Task 2: LrclibClient (mocked HTTP, TDD)

**Files:**
- Create: `src/YTMTaskbarWidget/Services/LrclibClient.cs`
- Create: `src/YTMTaskbarWidget.Tests/LrclibClientTests.cs`

- [ ] **Step 1: Write failing client test**

```csharp
using System.Net;
using System.Text;
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget.Tests;

sealed class StubHandler : HttpMessageHandler
{
    private readonly string _json;
    private readonly HttpStatusCode _code;
    public StubHandler(string json, HttpStatusCode code = HttpStatusCode.OK) { _json = json; _code = code; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
}

public sealed class LrclibClientTests
{
    [Fact]
    public async Task Returns_Synced_Lyrics_On_Hit()
    {
        const string json = """{"id":1,"trackName":"T","artistName":"A","syncedLyrics":"[00:10.00]hi\\n","plainLyrics":"hi"}""";
        var client = new LrclibClient(new HttpClient(new StubHandler(json)));
        var r = await client.GetAsync("T", "A", TimeSpan.FromMinutes(3));
        Assert.NotNull(r);
        Assert.Single(r!.Lines);
        Assert.Equal("hi", r.Lines[0].Text);
    }

    [Fact]
    public async Task Returns_Null_On_404()
    {
        var client = new LrclibClient(new HttpClient(new StubHandler("{}", HttpStatusCode.NotFound)));
        Assert.Null(await client.GetAsync("T", "A", null));
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test src/YTMTaskbarWidget.Tests --filter "LrclibClientTests" -v n`
Expected: FAIL — `LrclibClient` not found.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace YTMTaskbarWidget.Services;

public sealed record LrcResult(List<LrcLine> Lines, string? PlainLyrics);

public sealed class LrclibClient
{
    private readonly HttpClient _http;
    public LrclibClient(HttpClient http) => _http = http;

    public async Task<LrcResult?> GetAsync(string title, string artist, TimeSpan? duration, CancellationToken ct = default)
    {
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (duration.HasValue) url += $"&duration={Math.Round(duration.Value.TotalSeconds)}";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var dto = await resp.Content.ReadFromJsonAsync<LrclibDto>(cancellationToken: ct);
        if (dto?.SyncedLyrics is null) return null;
        var lines = LrcParser.Parse(dto.SyncedLyrics);
        if (lines.Count == 0) return null;
        return new LrcResult(lines, dto.PlainLyrics);
    }

    sealed class LrclibDto
    {
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/YTMTaskbarWidget.Tests --filter "LrclibClientTests" -v n`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/YTMTaskbarWidget/Services/LrclibClient.cs src/YTMTaskbarWidget.Tests/LrclibClientTests.cs
git commit -m "feat: add LRCLIB lyrics client with mocked http tests"
```

---

### Task 3: SmtcService (Browser YTM session)

**Files:**
- Create: `src/YTMTaskbarWidget/Services/SmtcService.cs`

No unit test — Windows-only integration, verified manually in Task 5. Keep logic small and null-safe.

- [ ] **Step 1: Implement SmtcService**

```csharp
using Windows.Media.Control;
using Windows.Storage.Streams;
using YTMTaskbarWidget.Models;

namespace YTMTaskbarWidget.Services;

public sealed class SmtcService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;

    public async Task InitAsync()
    {
        _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_mgr is null) return null;
        var current = _mgr.GetCurrentSession();
        if (IsBrowserYtm(current)) return current;
        foreach (var s in _mgr.GetSessions())
            if (IsBrowserYtm(s)) return s;
        return current;
    }

    static bool IsBrowserYtm(GlobalSystemMediaTransportControlsSession? s)
    {
        if (s is null) return false;
        var id = s.SourceAppUserModelId ?? string.Empty;
        return id.Contains("chrome", StringComparison.OrdinalIgnoreCase)
            || id.Contains("msedge", StringComparison.OrdinalIgnoreCase)
            || id.Contains("brave", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<NowPlaying?> GetNowPlayingAsync()
    {
        _session = PickSession();
        if (_session is null) return null;
        var props = await _session.TryGetMediaPropertiesAsync();
        if (props is null || string.IsNullOrWhiteSpace(props.Title)) return null;
        var info = _session.GetPlaybackInfo();
        var timeline = _session.GetTimelineProperties();
        byte[]? thumb = null;
        var tref = props.Thumbnail;
        if (tref is not null)
        {
            using var stream = await tref.OpenReadAsync();
            using var ms = new MemoryStream();
            using var net = stream.AsStreamForRead();
            await net.CopyToAsync(ms);
            thumb = ms.ToArray();
        }
        return new NowPlaying(props.Title, props.Artist ?? string.Empty, thumb, info.PlaybackStatus, timeline.Position);
    }

    public Task<bool> TogglePlayPauseAsync() => DoAsync(s => s.TryTogglePlayPauseAsync());
    public Task<bool> NextAsync() => DoAsync(s => s.TrySkipNextAsync());
    public Task<bool> PreviousAsync() => DoAsync(s => s.TrySkipPreviousAsync());

    static async Task<bool> DoAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> fn)
    {
        return false;
    }
}
```

NOTE: Replace `DoAsync` body during implementation with session-bound call — kept minimal here to avoid speculative generics; final version must call `PickSession()` then `await fn(session)`.

Corrected `DoAsync` to implement now (no placeholder):

```csharp
    async Task<bool> DoAsync(Func<GlobalSystemMediaTransportControlsSession, System.Threading.Tasks.Task<bool>> fn)
    {
        var s = _session ?? PickSession();
        if (s is null) return false;
        try { return await fn(s); } catch { return false; }
    }
```

And change call sites to:
```csharp
    public Task<bool> TogglePlayPauseAsync() => DoAsync(async s => await s.TryTogglePlayPauseAsync());
    public Task<bool> NextAsync() => DoAsync(async s => await s.TrySkipNextAsync());
    public Task<bool> PreviousAsync() => DoAsync(async s => await s.TrySkipPreviousAsync());
```

Use the corrected version in code (the first snippet shows evolution; final file must contain the corrected version only).

- [ ] **Step 2: Build**

Run: `dotnet build YTMTaskbarWidget.sln -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/YTMTaskbarWidget/Services/SmtcService.cs
git commit -m "feat: add SMTC service for browser YTM playback and controls"
```

---

### Task 4: TaskbarPositioner + Overlay UI (exact clone)

**Files:**
- Create: `src/YTMTaskbarWidget/Services/TaskbarPositioner.cs`
- Create: `src/YTMTaskbarWidget.Tests/TaskbarPositionerTests.cs`
- Modify: `src/YTMTaskbarWidget/MainWindow.xaml`

- [ ] **Step 1: Write failing positioner test**

```csharp
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget.Tests;

public sealed class TaskbarPositionerTests
{
    [Fact]
    public void Centers_On_Bottom_Taskbar()
    {
        var pos = TaskbarPositioner.CalcBottomCenter(
            screenWidth: 1920, screenHeight: 1080,
            taskbarHeight: 48, windowWidth: 360, windowHeight: 48, offsetX: 0);
        Assert.Equal(780, pos.Left);
        Assert.Equal(1032, pos.Top);
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test src/YTMTaskbarWidget.Tests --filter "TaskbarPositionerTests" -v n`
Expected: FAIL.

- [ ] **Step 3: Implement positioner**

```csharp
namespace YTMTaskbarWidget.Services;

public sealed record WidgetPos(double Left, double Top);

public static class TaskbarPositioner
{
    public static WidgetPos CalcBottomCenter(double screenWidth, double screenHeight, double taskbarHeight, double windowWidth, double windowHeight, double offsetX)
        => new((screenWidth - windowWidth) / 2 + offsetX, screenHeight - taskbarHeight - (taskbarHeight - windowHeight) / 2 - windowHeight * 0);
}
```

Simplified final math (bottom taskbar, window height == taskbar height 48): `Top = screenHeight - taskbarHeight`, `Left = (screenWidth - windowWidth)/2 + offsetX`. Use exactly that in file.

- [ ] **Step 4: Replace MainWindow.xaml with exact clone**

```xml
<Window x:Class="YTMTaskbarWidget.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="YTM Taskbar Widget" Height="48" Width="380"
        WindowStyle="None" AllowsTransparency="True"
        Background="#D91E1E1E" Topmost="True" ShowInTaskbar="False"
        ResizeMode="NoResize" Top="1000" Left="500">
    <Grid Margin="8,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="32"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <Image x:Name="Thumb" Width="32" Height="32" Margin="0,8,8,8" VerticalAlignment="Center"/>
        <StackPanel Grid.Column="1" VerticalAlignment="Center">
            <TextBlock x:Name="TitleText" Text="Nothing playing" Foreground="White" FontSize="12" TextTrimming="CharacterEllipsis"/>
            <TextBlock x:Name="LyricText" Text="" Foreground="#FFB0B0B0" FontSize="11" TextTrimming="CharacterEllipsis"/>
        </StackPanel>
        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
            <Button x:Name="PrevBtn" Content="|&#x25C0;" Width="32" Height="32" Background="Transparent" Foreground="White" BorderThickness="0"/>
            <Button x:Name="PlayBtn" Content="&#x25B6;" Width="40" Height="40" Background="#FF3A3A3A" Foreground="White" BorderThickness="0"/>
            <Button x:Name="NextBtn" Content="&#x25B6;|" Width="32" Height="32" Background="Transparent" Foreground="White" BorderThickness="0"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test src/YTMTaskbarWidget.Tests --filter "TaskbarPositionerTests" -v n`
Expected: PASS.
Run: `dotnet build YTMTaskbarWidget.sln -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/YTMTaskbarWidget/Services/TaskbarPositioner.cs src/YTMTaskbarWidget.Tests/TaskbarPositionerTests.cs src/YTMTaskbarWidget/MainWindow.xaml
git commit -m "feat: add taskbar positioner and exact-clone overlay layout"
```

---

### Task 5: Wire MainWindow (poll, lyrics, controls, autohide)

**Files:**
- Modify: `src/YTMTaskbarWidget/MainWindow.xaml.cs`
- Modify: `src/YTMTaskbarWidget/App.xaml.cs` (DI: HttpClient + services singletons)

- [ ] **Step 1: Implement App.xaml.cs**

```csharp
using System.Net.Http;
using System.Windows;
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget;

public partial class App : Application
{
    public static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    public static readonly LrclibClient Lyrics = new(SharedHttp);
    public static readonly SmtcService Smtc = new();
}
```

- [ ] **Step 2: Implement MainWindow.xaml.cs**

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace YTMTaskbarWidget;

public partial class MainWindow : Window
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    List<Services.LrcLine> _lines = new();
    string _key = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => { await App.Smtc.InitAsync(); PlaceBottomCenter(); MakeClickThroughExceptButtons(); _timer.Tick += async (_, _) => await RefreshAsync(); _timer.Start(); await RefreshAsync(); };
        PrevBtn.Click += async (_, _) => await App.Smtc.PreviousAsync();
        PlayBtn.Click += async (_, _) => await App.Smtc.TogglePlayPauseAsync();
        NextBtn.Click += async (_, _) => await App.Smtc.NextAsync();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => PlaceBottomCenter();
    }

    void PlaceBottomCenter()
    {
        var sw = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;
        const double tb = 48, ww = 380, wh = 48;
        Left = (sw - ww) / 2;
        Top = sh - tb;
    }

    void MakeClickThroughExceptButtons()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
        Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    }

    async Task RefreshAsync()
    {
        var np = await App.Smtc.GetNowPlayingAsync();
        if (np is null) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        TitleText.Text = string.IsNullOrWhiteSpace(np.Artist) ? np.Title : $"{np.Title} - {np.Artist}";
        PlayBtn.Content = np.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "||" : "▶";
        if (np.ThumbnailBytes is not null)
        {
            using var ms = new MemoryStream(np.ThumbnailBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
            Thumb.Source = bmp;
        }
        var k = np.Title + "\0" + np.Artist;
        if (k != _key)
        {
            _key = k; _lines = new();
            try
            {
                var r = await App.Lyrics.GetAsync(np.Title, np.Artist, null);
                if (r is not null) _lines = r.Lines;
            }
            catch { }
        }
        LyricText.Text = Services.LrcParser.CurrentLine(_lines, np.Position) ?? string.Empty;
    }

    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetWindowLong(nint h, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int SetWindowLong(nint h, int n, int v);
    }
}
```

- [ ] **Step 3: Manual verification**

Run: `dotnet run --project src/YTMTaskbarWidget/YTMTaskbarWidget.csproj -c Debug`
Expected: widget appears centered on taskbar; play YTM in Chrome/Edge, title + lyric line update, prev/play/next work, hides when nothing playing.

- [ ] **Step 4: Run full tests + build**

Run: `dotnet test YTMTaskbarWidget.sln -v n`
Expected: all PASS.
Run: `dotnet build YTMTaskbarWidget.sln -c Release`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/YTMTaskbarWidget/MainWindow.xaml.cs src/YTMTaskbarWidget/App.xaml.cs
git commit -m "feat: wire SMTC polling, lyric sync, controls and autohide"
```

---

## Self-Review

1. Spec coverage: Browser YTM via SMTC yes (Task 3+5); single synced lyric line yes (Tasks 1+2+5); exact clone layout yes (Task 4); like skipped v1 yes (no heart in XAML); Win11 bottom-center yes (TaskbarPositioner + PlaceBottomCenter).
2. Placeholder scan: no TBD/TODO; `DoAsync` corrected inline; `TaskbarPositioner` math simplified to `Top = screenHeight - taskbarHeight`.
3. Type consistency: `LrcLine`, `LrcResult`, `NowPlaying`, `LrclibClient.GetAsync(title, artist, duration)`, `SmtcService.GetNowPlayingAsync/Toggle/Next/Previous` used consistently across Tasks 1-5.
