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
