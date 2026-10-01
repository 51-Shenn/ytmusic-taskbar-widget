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
            return Position + drift;
        }
    }
}
