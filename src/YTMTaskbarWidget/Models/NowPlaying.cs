using Windows.Media.Control;
namespace YTMTaskbarWidget.Models;
public sealed record NowPlaying(
    string Title,
    string Artist,
    byte[]? ThumbnailBytes,
    GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
    TimeSpan Position,
    DateTimeOffset LastUpdated)
{
    public TimeSpan EffectivePosition
    {
        get
        {
            if (Status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return Position;
            var drift = DateTimeOffset.Now - LastUpdated;
            if (drift < TimeSpan.Zero)
                return Position;
            // Chrome reports timeline position only every ~20-25s; between
            // updates audio keeps advancing, so interpolate at 1x wall clock.
            // Buffering status (non-Playing) freezes us automatically.
            return Position + drift;
        }
    }
}
