using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace YTMTaskbarWidget.Services;
public sealed record LrcResult(List<LrcLine> Lines, string? PlainLyrics);
public sealed class LrclibClient
{
    private const string BaseUrl = "https://lrclib.net/api/get";
    private readonly HttpClient _http;
    public LrclibClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }
    public async Task<LrcResult?> GetAsync(string title, string artist, TimeSpan? duration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(artist);
        try
        {
            var url = $"{BaseUrl}?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (duration.HasValue) url += $"&duration={Math.Round(duration.Value.TotalSeconds)}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var dto = await resp.Content.ReadFromJsonAsync<LrclibDto>(cancellationToken: ct);
            if (dto?.SyncedLyrics is null) return null;
            var lines = LrcParser.Parse(dto.SyncedLyrics);
            if (lines.Count == 0) return null;
            return new LrcResult(lines, dto.PlainLyrics);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
    sealed class LrclibDto { [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; } [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; } }
}
