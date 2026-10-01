using Windows.Media.Control;
namespace YTMTaskbarWidget.Models;
public sealed record NowPlaying(string Title, string Artist, byte[]? ThumbnailBytes, GlobalSystemMediaTransportControlsSessionPlaybackStatus Status, TimeSpan Position);
