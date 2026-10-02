using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _lyricTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private List<LrcLine> _lines = new();
    private YTMTaskbarWidget.Models.NowPlaying? _lastNp;
    private string _key = string.Empty;
    private bool _refreshing;
    private int _nullStreak;
    private int _tickCount;
    private bool _dragging;
    private bool _dragArmed;
    private Point _dragGrabOffset;
    private Point _downPos;
    private DateTime _lastClickTime = DateTime.MinValue;
    private Point _lastClickPos;
    private double _savedOffsetX;
    private System.Windows.Controls.Border[] _waveBars = Array.Empty<System.Windows.Controls.Border>();
    private int _waveTick;
    private Native.WinEventDelegate? _winEventProc;
    private double _titleNatural;
    private double _titleLoopMax;
    private string _displayTitle = string.Empty;
    private bool _marqueeActive;
    private bool _marqueePending;
    private double _marqueeOffset;
    private int _marqueeHoldMs;
    private static string OffsetFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YTMTaskbarWidget",
            "taskbar-offset.txt");

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PrevBtn.Click += async (_, _) => await App.Smtc.PreviousAsync();
        PlayBtn.Click += async (_, _) => await App.Smtc.TogglePlayPauseAsync();
        NextBtn.Click += async (_, _) => await App.Smtc.NextAsync();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => PlaceBottomCenter();
        Deactivated += (_, _) => PinTopmost();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await App.Smtc.InitAsync();
        PlaceBottomCenter();
        MakeClickThrough();
        HookShellEvents();
        _waveBars = new[] { WaveBar0, WaveBar1, WaveBar2, WaveBar3 };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _lyricTimer.Tick += (_, _) =>
        {
            UpdateWave();
            UpdateLyricLine();
            UpdateMarquee();
        };
        _lyricTimer.Start();
        await RefreshAsync();
    }

    private void HookShellEvents()
    {
        // The taskbar re-asserts its topmost Z-order whenever an app opens,
        // closes or restores, briefly burying the pill behind it. Re-pin the
        // instant the foreground window changes instead of waiting a tick.
        _winEventProc = (_, eventType, _, _, _, _, _) =>
        {
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
                PinTopmost();
        };
        try
        {
            Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND,
                Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0, 0,
                Native.WINEVENT_OUTOFCONTEXT);
        }
        catch
        {
        }
    }

    private void UpdateWave()
    {
        if (_waveBars.Length == 0)
            return;
        var np = _lastNp;
        var playing = np is not null
            && np.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            && Visibility == Visibility.Visible;
        _waveTick++;
        for (var i = 0; i < _waveBars.Length; i++)
        {
            // Bars are center-anchored in XAML, so height changes move both
            // ends around the horizontal midline instead of only the top.
            _waveBars[i].Height = playing
                ? 2 + 9 * Math.Abs(Math.Sin(_waveTick * 0.45 + i * 1.1))
                : 2;
        }
    }

    private void PlaceBottomCenter()
    {
        // Sit INSIDE the taskbar: WorkArea.Bottom is the taskbar's top edge,
        // so center the pill within the taskbar strip below it.
        const double ww = 352;
        var wh = TaskbarPositioner.RequiredWindowHeight(contentHeight: 44, verticalMargin: 0, shadowBreathingRoom: 6);
        var area = SystemParameters.WorkArea;
        var taskbarHeight = SystemParameters.PrimaryScreenHeight - area.Bottom;
        Width = ww;
        Height = wh;
        LoadOffset();
        var pos = TaskbarPositioner.CalcBottomCenter(area.Width, SystemParameters.PrimaryScreenHeight, taskbarHeight, ww, wh, _savedOffsetX);
        Left = ClampLeft(area.Left + pos.Left, area, ww);
        Top = pos.Top;
    }

    private static double ClampLeft(double left, Rect area, double ww)
    {
        var min = area.Left + 4;
        var max = area.Left + area.Width - ww - 4;
        if (max < min)
            return (min + Math.Max(max, area.Left)) / 2;
        return Math.Min(Math.Max(left, min), max);
    }

    private void DragZone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Buttons keep their clicks; only the left drag handle starts a drag.
        // NOTE: no mouse capture here — capturing on press breaks WPF's
        // double-click tracking. Capture starts once real movement is seen.
        if (IsOverButton(e.OriginalSource))
            return;
        if (!IsOverDragHandle(e.OriginalSource))
            return;
        _dragging = true;
        _dragArmed = false;
        _downPos = e.GetPosition(this);
        var cursor = PointToScreen(e.GetPosition(this));
        _dragGrabOffset = new Point(cursor.X - Left, cursor.Y - Top);
    }

    private void DragZone_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        if ((e.GetPosition(this) - _downPos).Length <= 4)
            return;
        if (!_dragArmed)
        {
            _dragArmed = true;
            DragZone.CaptureMouse();
        }
        var area = SystemParameters.WorkArea;
        var cursor = PointToScreen(e.GetPosition(this));
        var taskbarHeight = SystemParameters.PrimaryScreenHeight - area.Bottom;
        // Lock vertically inside the taskbar strip; let the user slide horizontally.
        Left = ClampLeft(cursor.X - _dragGrabOffset.X, area, Width);
        Top = TaskbarPositioner.CalcBottomCenter(area.Width, SystemParameters.PrimaryScreenHeight, taskbarHeight, Width, Height, 0).Top;
        e.Handled = true;
    }

    private void DragZone_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Release over a control button with no background-drag in progress:
        // hands off entirely so Button.Click fires normally.
        if (!_dragging && IsOverButton(e.OriginalSource))
            return;
        var moved = _dragging && _dragArmed;
        if (_dragging)
        {
            _dragging = false;
            _dragArmed = false;
            try { DragZone.ReleaseMouseCapture(); } catch { }
            // Remember the offset from center so the spot survives restarts.
            var area = SystemParameters.WorkArea;
            _savedOffsetX = Left - (area.Left + (area.Width - Width) / 2);
            SaveOffset();
        }
        // Double left-click on the background (not a drag, not a button): open YTM.
        // Tracked manually: mouse capture + handled tunneling make e.ClickCount unreliable.
        if (!moved && !IsOverButton(e.OriginalSource))
        {
            var now = DateTime.UtcNow;
            var pos = e.GetPosition(this);
            var interval = (now - _lastClickTime).TotalMilliseconds;
            var dist = (pos - _lastClickPos).Length;
            const double doubleClickMs = 500;
            if ((interval <= doubleClickMs && dist <= 4) || e.ClickCount >= 2)
            {
                _lastClickTime = DateTime.MinValue;
                OpenBrowser();
            }
            else
            {
                _lastClickTime = now;
                _lastClickPos = pos;
            }
        }
        e.Handled = true;
    }

    private void DragZone_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Application.Current.Shutdown();
            e.Handled = true;
        }
    }

    private void OpenBrowser()
    {
        // Never block the UI thread: WinRT/COM/foreground waits all run in
        // the background; if anything hangs there, the widget stays alive.
        _ = Task.Run(() =>
        {
            // Prefer the already-open YouTube Music tab playing this track
            // over spawning a new tab; only open a fresh one if we can't.
            try
            {
                var track = App.Smtc.CurrentTrack();
                if (BrowserFocus.FocusYtmTab(track?.Title, track?.Artist, App.Smtc.CurrentSessionAumid()))
                    return;
            }
            catch
            {
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://music.youtube.com")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        });
    }

    private static bool IsOverButton(object? source)
    {
        // Icon Paths live in the button template, where the logical-tree walk
        // can miss them — climb the visual tree as well.
        for (var d = source as DependencyObject; d is not null;)
        {
            if (d is System.Windows.Controls.Button)
                return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d)
                ?? LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private static bool IsOverDragHandle(object? source)
    {
        for (var d = source as DependencyObject; d is not null;)
        {
            if (d is FrameworkElement { Name: "DragHandle" })
                return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d)
                ?? LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void LoadOffset()
    {
        try
        {
            if (File.Exists(OffsetFile) && double.TryParse(File.ReadAllText(OffsetFile).Trim(), out var x))
                _savedOffsetX = Math.Max(-2000, Math.Min(2000, x));
        }
        catch
        {
        }
    }

    private void SaveOffset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OffsetFile)!);
            File.WriteAllText(OffsetFile, _savedOffsetX.ToString("F0"));
        }
        catch
        {
        }
    }

    private void MakeClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        var ex = Native.GetWindowLong(hwnd, GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        PinTopmost();
    }

    private void PinTopmost()
    {
        // The taskbar is topmost too — re-assert our Z position whenever we
        // lose activation and periodically, or we slide behind it and look "closed".
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
            Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch
        {
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            // Never let a transient SMTC/network failure kill the timer or hide the widget.
            Log($"refresh FAILED err={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        // Every 500ms: the taskbar can slide over us at any moment; the
        // foreground hook handles the common case, this catches the rest.
        PinTopmost();
        var np = await App.Smtc.GetNowPlayingAsync();
        if (np is null)
        {
            // Track transitions briefly report null — only hide after ~2s of silence.
            _nullStreak++;
            if (_nullStreak >= 4)
                Visibility = Visibility.Collapsed;
            return;
        }
        _nullStreak = 0;
        _lastNp = np;

        Visibility = Visibility.Visible;
        var playing = np.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayGlyph.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;

        if (np.ThumbnailBytes is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(np.ThumbnailBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                Thumb.Source = bmp;
            }
            catch
            {
                Thumb.Source = null;
            }
        }
        else
        {
            Thumb.Source = null;
        }

        var key = $"{np.Title}\0{np.Artist}";
        if (key != _key)
        {
            _key = key;
            _lines = new List<LrcLine>();
            LyricText.Text = string.Empty;
            // Single copy of the title; SetupMarquee may double it for the loop.
            _displayTitle = string.IsNullOrWhiteSpace(np.Artist) ? np.Title : $"{np.Title} - {np.Artist}";
            TitleText.Text = _displayTitle;
            SetupMarquee();
            try
            {
                var res = await App.Lyrics.GetAsync(np.Title, np.Artist, null);
                if (res is not null)
                    _lines = res.Lines;
                Log($"fetch title='{np.Title}' artist='{np.Artist}' lines={_lines.Count}");
            }
            catch (Exception ex)
            {
                Log($"fetch FAILED title='{np.Title}' artist='{np.Artist}' err={ex.GetType().Name}: {ex.Message}");
            }
        }

        var cur = LrcParser.CurrentLine(_lines, np.EffectivePosition);
        UpdateLyricLine();
        _tickCount++;
        if (_tickCount % 10 == 0)
        {
            Log($"tick title='{np.Title}' artist='{np.Artist}' status={np.Status} pos={np.Position} eff={np.EffectivePosition} lines={_lines.Count} cur='{cur}'");
        }
    }

    private const string NoLyricsDash = "-";
    private const string LyricsPendingNote = "\u266B";

    private void UpdateLyricLine()
    {
        var np = _lastNp;
        if (np is null || Visibility != Visibility.Visible)
            return;
        if (_lines.Count == 0)
        {
            // No lyrics exist for this track: dash.
            LyricText.Text = NoLyricsDash;
            return;
        }
        var pos = np.EffectivePosition;
        var idx = LrcParser.CurrentLineIndex(_lines, pos);
        if (idx < 0)
        {
            // Lyrics exist but none yet (intro): music note.
            LyricText.Text = LyricsPendingNote;
            return;
        }
        var line = _lines[idx];
        if (line.Words.Count == 0)
        {
            LyricText.Text = line.Text;
            return;
        }
        // Karaoke: sung words red, upcoming words gray.
        var sung = LrcParser.SungWordCount(line, pos);
        LyricText.Inlines.Clear();
        for (var i = 0; i < line.Words.Count; i++)
        {
            var run = new System.Windows.Documents.Run(line.Words[i].Text)
            {
                Foreground = i < sung ? SungBrush : UpcomingBrush
            };
            LyricText.Inlines.Add(run);
        }
    }

    private static readonly System.Windows.Media.SolidColorBrush SungBrush = CreateFrozenBrush(0xFF, 0x33, 0x55);

    private static readonly System.Windows.Media.SolidColorBrush UpcomingBrush = CreateFrozenBrush(0x9A, 0x9A, 0x9A);

    internal static double MarqueeLoopDistance(double titleWidth, double duplicatedWidth)
    {
        if (titleWidth <= 0 || duplicatedWidth <= titleWidth)
            return 0;

        return duplicatedWidth - titleWidth;
    }

    internal static double MarqueeRenderWidth(double titleWidth, double duplicatedWidth)
    {
        if (MarqueeLoopDistance(titleWidth, duplicatedWidth) <= 0)
            return double.NaN;

        return duplicatedWidth;
    }

    internal const int MarqueeSeamPauseMs = 4000;

    internal static string MarqueeLoopText(string title) => $"{title}          {title}";

    private void SetupMarquee()
    {
        _marqueeActive = false;
        _marqueePending = false;
        _marqueeOffset = 0;
        _marqueeHoldMs = 0;
        Canvas.SetLeft(TitleText, 0);
        TitleText.Width = double.NaN;
        TitleText.TextTrimming = TextTrimming.None;
        TitleText.Text = _displayTitle; // normalize to a single copy
        TitleText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _titleNatural = TitleText.DesiredSize.Width;
        var avail = TitleClip.ActualWidth;
        if (avail <= 0)
        {
            // Not laid out yet — retry on the next timer tick.
            _marqueePending = true;
            Log($"marquee pending natural={_titleNatural:F1} (clip not laid out)");
            return;
        }
        if (_titleNatural > avail + 0.5)
        {
            // Seamless loop: render the title twice with a small gap. Scrolling
            // exactly one copy + gap puts the second copy's first character at
            // the original start position, where the frame is identical to
            // offset 0 — the 2s pause and snap-back are invisible.
            TitleText.Text = MarqueeLoopText(_displayTitle);
            TitleText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var duplicatedWidth = TitleText.DesiredSize.Width;
            _titleLoopMax = MarqueeLoopDistance(_titleNatural, duplicatedWidth);
            if (_titleLoopMax <= 0)
            {
                TitleText.Width = double.NaN;
                TitleText.Text = _displayTitle;
                TitleText.TextTrimming = TextTrimming.CharacterEllipsis;
                return;
            }
            TitleText.Width = MarqueeRenderWidth(_titleNatural, duplicatedWidth);
            _marqueeActive = true;
            Log($"marquee active natural={_titleNatural:F1} avail={avail:F1} loop={_titleLoopMax:F1} title='{_displayTitle}'");
            try
            {
                var tl = TitleClip.PointToScreen(new System.Windows.Point(0, 0));
                var sc = TitleClip.PointToScreen(new System.Windows.Point(avail, 0));
                var th = Thumb.PointToScreen(new System.Windows.Point(0, 0));
                var te = TitleText.PointToScreen(new System.Windows.Point(TitleText.ActualWidth, 0));
                Log($"geom clip=({tl.X:F0},{tl.Y:F0})->({sc.X:F0}) thumb=({th.X:F0}) txtRight={te.X:F0} txtActW={TitleText.ActualWidth:F1} dpiScale={(sc.X - tl.X) / avail:F2}");
            }
            catch (System.Exception ex) { Log($"geom err {ex.Message}"); }
            return;
        }
        _titleLoopMax = 0;
        // Fits: static with ellipsis as a safety net.
        Log($"marquee fits natural={_titleNatural:F1} avail={avail:F1} title='{_displayTitle}'");
        TitleText.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private int _marqueeProbe;

    private void UpdateMarquee()
    {
        if (_marqueePending)
        {
            SetupMarquee();
            if (_marqueePending)
                return;
        }
        if (!_marqueeActive)
            return;
        if (_marqueeProbe++ >= 0)
            Log($"mqprobe x={Canvas.GetLeft(TitleText):F1} len={TitleText.Text.Length} trim={TitleText.TextTrimming} loop={_titleLoopMax:F1} hold={_marqueeHoldMs} off={_marqueeOffset:F1} vis={Visibility}");
        if (_marqueeHoldMs > 0)
        {
            // Pause at the seam, then snap to offset 0 — frame-identical,
            // so the next loop starts seamlessly.
            _marqueeHoldMs -= 150;
            if (_marqueeHoldMs <= 0)
            {
                _marqueeOffset = 0;
                Canvas.SetLeft(TitleText, 0);
            }
            return;
        }
        var max = _titleLoopMax;
        if (max <= 0)
            return;
        _marqueeOffset += 6; // 40 px/s at the 150ms tick — slow and steady
        if (_marqueeOffset >= max)
        {
            // Second copy's first character is exactly where copy 1 started.
            _marqueeOffset = max;
            Canvas.SetLeft(TitleText, -max);
            _marqueeHoldMs = MarqueeSeamPauseMs;
            return;
        }
        Canvas.SetLeft(TitleText, -_marqueeOffset);
    }

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    internal static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "YTMWidget-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch
        {
        }
    }

    private static class Native
    {
        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    }
}
