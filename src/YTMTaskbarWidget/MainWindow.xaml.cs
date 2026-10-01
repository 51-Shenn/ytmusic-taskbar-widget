using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
        // so center the 44px pill within the taskbar strip below it.
        const double ww = 340, wh = 44;
        var area = SystemParameters.WorkArea;
        var taskbarHeight = SystemParameters.PrimaryScreenHeight - area.Bottom;
        Width = ww;
        Height = wh;
        LoadOffset();
        Left = ClampLeft(area.Left + (area.Width - ww) / 2 + _savedOffsetX, area, ww);
        Top = area.Bottom + Math.Max(0, (taskbarHeight - wh) / 2);
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
        // Buttons keep their clicks — only start a drag from the pill background.
        // NOTE: no mouse capture here — capturing on press breaks WPF's
        // double-click tracking. Capture starts once real movement is seen.
        if (IsOverButton(e.OriginalSource))
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
        Top = area.Bottom + Math.Max(0, (taskbarHeight - Height) / 2);
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

    private static void OpenBrowser()
    {
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
        TitleText.Text = string.IsNullOrWhiteSpace(np.Artist) ? np.Title : $"{np.Title} - {np.Artist}";
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

    private const string MusicNoteFallback = "\u266A";

    private void UpdateLyricLine()
    {
        var np = _lastNp;
        if (np is null || Visibility != Visibility.Visible)
            return;
        if (_lines.Count == 0)
        {
            // Playing with no lyrics available: show a music note instead of words.
            LyricText.Text = MusicNoteFallback;
            return;
        }
        var pos = np.EffectivePosition;
        var idx = LrcParser.CurrentLineIndex(_lines, pos);
        if (idx < 0)
        {
            LyricText.Text = MusicNoteFallback;
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

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void Log(string msg)
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
