using System.Text.RegularExpressions;
namespace YTMTaskbarWidget.Services;
public sealed record LrcLine(TimeSpan Timestamp, string Text);
public static partial class LrcParser
{
    [GeneratedRegex(@"^\[(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<x>\d{1,3}))?\](?<t>.*)$")]
    private static partial Regex LineRegex();
    public static List<LrcLine> Parse(string? syncedLyrics)
    {
        var out_ = new List<LrcLine>();
        if (string.IsNullOrWhiteSpace(syncedLyrics)) return out_;
        foreach (var raw in syncedLyrics.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            var m = LineRegex().Match(line);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups["m"].Value, out var min)) continue;
            if (!double.TryParse(m.Groups["s"].Value, out var sec)) continue;
            var frac = m.Groups["x"].Value;
            var ms = 0;
            if (frac.Length == 2 && int.TryParse(frac, out var cs)) ms = cs * 10;
            else if (frac.Length == 3 && int.TryParse(frac, out var ms3)) ms = ms3;
            var text = m.Groups["t"].Value.Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith("ti:", StringComparison.OrdinalIgnoreCase)) continue;
            if (text.StartsWith("ar:", StringComparison.OrdinalIgnoreCase)) continue;
            out_.Add(new LrcLine(new TimeSpan(0, 0, min, (int)sec, ms), text));
        }
        out_.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return out_;
    }
    public static string? CurrentLine(List<LrcLine> lines, TimeSpan position)
    {
        string? cur = null;
        foreach (var l in lines)
        {
            if (l.Timestamp <= position) cur = l.Text;
            else break;
        }
        return cur;
    }
}
