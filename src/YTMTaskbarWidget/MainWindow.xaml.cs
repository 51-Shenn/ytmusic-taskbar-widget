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
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        await RefreshAsync();
    }

    private void PlaceBottomCenter()
    {
        Left = (SystemParameters.PrimaryScreenWidth - 380) / 2;
        Top = SystemParameters.PrimaryScreenHeight - 48;
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
        PlayBtn.Content = np.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "||" : "▶";

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
            }
            catch
            {
            }
        }

        LyricText.Text = LrcParser.CurrentLine(_lines, np.Position) ?? string.Empty;
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
