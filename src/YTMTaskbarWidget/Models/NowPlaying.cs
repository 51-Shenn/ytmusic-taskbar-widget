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
            // SMTC sessions stop pushing fresh positions for long stretches (seek
            // stalls, background tabs). Unbounded extrapolation ran lyric display
            // ~20s ahead of the real song, so cap how far ahead we guess.
            if (drift > TimeSpan.FromSeconds(3))
                return Position + TimeSpan.FromSeconds(3);
            return Position + drift;
        }
    }
}
