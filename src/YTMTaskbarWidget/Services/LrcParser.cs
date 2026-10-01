using System.Text.RegularExpressions;
namespace YTMTaskbarWidget.Services;
public sealed record LrcLine(TimeSpan Timestamp, string Text);
public static partial class LrcParser
{
    [GeneratedRegex(@"^\[(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<x>\d{1,3}))?\]")]
    private static partial Regex TimestampRegex();
    public static List<LrcLine> Parse(string? syncedLyrics)
    {
        var out_ = new List<LrcLine>();
        if (string.IsNullOrWhiteSpace(syncedLyrics)) return out_;
        foreach (var raw in syncedLyrics.Split('\n'))
        {
            var rest = raw.Trim().TrimEnd('\r');
            var timestamps = new List<TimeSpan>();
            var invalid = false;
            while (true)
            {
                var m = TimestampRegex().Match(rest);
                if (!m.Success) break;
                if (!int.TryParse(m.Groups["m"].Value, out var min)) { invalid = true; break; }
                if (!double.TryParse(m.Groups["s"].Value, out var sec)) { invalid = true; break; }
                var frac = m.Groups["x"].Value;
                var ms = 0;
                if (frac.Length == 1 && int.TryParse(frac, out var d)) ms = d * 100;
                else if (frac.Length == 2 && int.TryParse(frac, out var cs)) ms = cs * 10;
                else if (frac.Length == 3 && int.TryParse(frac, out var ms3)) ms = ms3;
                timestamps.Add(new TimeSpan(0, 0, min, (int)sec, ms));
                rest = rest.Substring(m.Length);
            }
            if (invalid || timestamps.Count == 0) continue;
            var text = rest.Trim();
            if (text.Length == 0) continue;
            foreach (var ts in timestamps)
                out_.Add(new LrcLine(ts, text));
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
