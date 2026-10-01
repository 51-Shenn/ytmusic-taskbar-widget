using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
        Left = (SystemParameters.PrimaryScreenWidth - 400) / 2;
        Top = SystemParameters.PrimaryScreenHeight - 52;
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
        var np = await App.Smtc.GetNowPlayingAsync();
        if (np is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

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

        var cur = LrcParser.CurrentLine(_lines, np.Position);
        LyricText.Text = cur ?? string.Empty;
        Log($"tick title='{np.Title}' artist='{np.Artist}' status={np.Status} pos={np.Position} lines={_lines.Count} cur='{cur}'");
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
