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
    private List<LrcLine> _lines = new();
    private string _key = string.Empty;
    private bool _refreshing;
    private int _nullStreak;
    private int _tickCount;
    private bool _dragging;
    private Point _dragGrabOffset;
    private double _savedOffsetX;
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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await App.Smtc.InitAsync();
        PlaceBottomCenter();
        MakeClickThrough();
        EnableAcrylicBlur();
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        await RefreshAsync();
    }

    private void PlaceBottomCenter()
    {
        // Sit INSIDE the taskbar: WorkArea.Bottom is the taskbar's top edge,
        // so center the 40px pill within the taskbar strip below it.
        const double ww = 340, wh = 40;
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
        if (IsOverButton(e.OriginalSource))
            return;
        _dragging = true;
        var cursor = PointToScreen(e.GetPosition(this));
        _dragGrabOffset = new Point(cursor.X - Left, cursor.Y - Top);
        DragZone.CaptureMouse();
        e.Handled = true;
    }

    private void DragZone_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
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
        if (!_dragging)
            return;
        _dragging = false;
        DragZone.ReleaseMouseCapture();
        // Remember the offset from center so the spot survives restarts.
        var area = SystemParameters.WorkArea;
        _savedOffsetX = Left - (area.Left + (area.Width - Width) / 2);
        SaveOffset();
        e.Handled = true;
    }

    private static bool IsOverButton(object? source)
    {
        for (var d = source as DependencyObject; d is not null; d = LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Button)
                return true;
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
    }

    private void EnableAcrylicBlur()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var accent = new Native.AccentPolicy
            {
                AccentState = Native.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = 0x99282828u
            };
            var data = new Native.WindowCompositionAttributeData
            {
                Attribute = Native.WCA_ACCENT_POLICY,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent)),
                SizeOfData = Marshal.SizeOf(accent)
            };
            try
            {
                Marshal.StructureToPtr(accent, data.Data, false);
                Native.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(data.Data);
            }
        }
        catch
        {
            // Gradient fallback in XAML already looks glassy; ignore.
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

        Visibility = Visibility.Visible;
        TitleText.Text = string.IsNullOrWhiteSpace(np.Artist) ? np.Title : $"{np.Title} - {np.Artist}";
        PlayBtn.Content = np.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "\uE103" : "\uE102";

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
        LyricText.Text = cur ?? string.Empty;
        _tickCount++;
        if (_tickCount % 10 == 0)
            Log($"tick title='{np.Title}' artist='{np.Artist}' status={np.Status} pos={np.Position} eff={np.EffectivePosition} lines={_lines.Count} cur='{cur}'");
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
        internal const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        internal const int WCA_ACCENT_POLICY = 19;

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    }
}
