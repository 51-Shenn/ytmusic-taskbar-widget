using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace YTMTaskbarWidget.Services;
public sealed record LrcResult(List<LrcLine> Lines, string? PlainLyrics);
public sealed class LrclibClient
{
    private const string BaseUrl = "https://lrclib.net/api/get";
    private const string SearchUrl = "https://lrclib.net/api/search";
    private readonly HttpClient _http;
    public LrclibClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }
    public async Task<LrcResult?> GetAsync(string title, string artist, TimeSpan? duration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        try
        {
            if (!string.IsNullOrWhiteSpace(artist))
            {
                var exact = await GetExactAsync(title, artist, duration, ct);
                if (exact is not null)
                    return exact;
            }
            return await SearchFirstAsync(title, artist, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<LrcResult?> GetExactAsync(string title, string artist, TimeSpan? duration, CancellationToken ct)
    {
        var url = $"{BaseUrl}?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (duration.HasValue)
            url += $"&duration={Math.Round(duration.Value.TotalSeconds)}";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;
        var dto = await resp.Content.ReadFromJsonAsync<LrclibDto>(cancellationToken: ct);
        if (dto?.SyncedLyrics is null)
            return null;
        var lines = LrcParser.Parse(dto.SyncedLyrics);
        if (lines.Count == 0)
            return null;
        return new LrcResult(lines, dto.PlainLyrics);
    }

    public async Task<LrcResult?> SearchFirstAsync(string title, string? artist, CancellationToken ct = default)
    {
        var url = $"{SearchUrl}?track_name={Uri.EscapeDataString(title)}";
        if (!string.IsNullOrWhiteSpace(artist))
            url += $"&artist_name={Uri.EscapeDataString(artist)}";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;
        var list = await resp.Content.ReadFromJsonAsync<List<LrclibDto>>(cancellationToken: ct);
        if (list is null)
            return null;
        foreach (var dto in list)
        {
            if (string.IsNullOrWhiteSpace(dto.SyncedLyrics))
                continue;
            var lines = LrcParser.Parse(dto.SyncedLyrics);
            if (lines.Count == 0)
                continue;
            return new LrcResult(lines, dto.PlainLyrics);
        }
        return null;
    }
    sealed class LrclibDto { [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; } [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; } }
}
