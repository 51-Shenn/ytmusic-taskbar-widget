using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;
using YTMTaskbarWidget.Models;
namespace YTMTaskbarWidget.Services;
public sealed class SmtcService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _session;
    public async Task InitAsync() { _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_mgr is null) return null;
        var current = _mgr.GetCurrentSession();
        if (IsBrowserYtm(current)) return current;
        foreach (var s in _mgr.GetSessions()) if (IsBrowserYtm(s)) return s;
        return current;
    }
    static bool IsBrowserYtm(GlobalSystemMediaTransportControlsSession? s)
    {
        if (s is null) return false;
        var id = s.SourceAppUserModelId ?? string.Empty;
        return id.Contains("chrome", StringComparison.OrdinalIgnoreCase) || id.Contains("msedge", StringComparison.OrdinalIgnoreCase) || id.Contains("brave", StringComparison.OrdinalIgnoreCase);
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
            try {
            using var stream = await tref.OpenReadAsync();
            using var ms = new MemoryStream();
            using var net = stream.AsStreamForRead();
            await net.CopyToAsync(ms);
            thumb = ms.ToArray();
            } catch { thumb = null; }
        }
        return new NowPlaying(props.Title, props.Artist ?? string.Empty, thumb, info.PlaybackStatus, timeline.Position, timeline.LastUpdatedTime);
    }
    public Task<bool> TogglePlayPauseAsync() => DoAsync(async s => await s.TryTogglePlayPauseAsync());
    public Task<bool> NextAsync() => DoAsync(async s => await s.TrySkipNextAsync());
    public Task<bool> PreviousAsync() => DoAsync(async s => await s.TrySkipPreviousAsync());
    async Task<bool> DoAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> fn)
    {
        var s = _session ?? PickSession();
        if (s is null) return false;
        try { return await fn(s); } catch { return false; }
    }
}
