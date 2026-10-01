using Windows.Media.Control;
namespace YTMTaskbarWidget.Models;
public sealed record NowPlaying(
    string Title,
    string Artist,
    byte[]? ThumbnailBytes,
    GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
    TimeSpan Position,
    DateTimeOffset LastUpdated,
    double PlaybackRate = 1.0)
{
    public TimeSpan EffectivePosition
    {
        get
        {
            if (Status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return Position;
            if (PlaybackRate <= 0)
                return Position;
            var drift = DateTimeOffset.Now - LastUpdated;
            if (drift < TimeSpan.Zero)
                return Position;
            // Chrome reports timeline position only every ~20-25s; between
            // updates audio keeps advancing, so interpolate at playback rate.
            // (Buffering status and rate==0 freeze us automatically.)
            return Position + TimeSpan.FromTicks((long)(drift.Ticks * PlaybackRate));
        }
    }
}
