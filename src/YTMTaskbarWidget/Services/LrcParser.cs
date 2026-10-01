using System.Text.RegularExpressions;
namespace YTMTaskbarWidget.Services;
public sealed record WordStamp(TimeSpan Timestamp, string Text);
public sealed record LrcLine(TimeSpan Timestamp, string Text, List<WordStamp> Words)
{
    public LrcLine(TimeSpan Timestamp, string Text) : this(Timestamp, Text, new List<WordStamp>()) { }
}
public static partial class LrcParser
{
    [GeneratedRegex(@"^\[(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<x>\d{1,3}))?\]")]
    private static partial Regex TimestampRegex();
    [GeneratedRegex(@"<(?<m>\d+):(?<s>\d{1,2})(?:[.:](?<x>\d{1,3}))?>")]
    private static partial Regex WordTagRegex();
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
            var words = ParseWords(text);
            var clean = words.Count > 0
                ? string.Join("", words.Select(w => w.Text)).Trim()
                : text;
            if (clean.Length == 0) continue;
            foreach (var ts in timestamps)
                out_.Add(new LrcLine(ts, clean, words));
        }
        out_.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return out_;
    }

    private static TimeSpan? ToTimestamp(string m, string s, string x)
    {
        if (!int.TryParse(m, out var min)) return null;
        if (!double.TryParse(s, out var sec)) return null;
        var ms = 0;
        if (x.Length == 1 && int.TryParse(x, out var d)) ms = d * 100;
        else if (x.Length == 2 && int.TryParse(x, out var cs)) ms = cs * 10;
        else if (x.Length == 3 && int.TryParse(x, out var ms3)) ms = ms3;
        return new TimeSpan(0, 0, min, (int)sec, ms);
    }

    public static List<WordStamp> ParseWords(string text)
    {
        var matches = WordTagRegex().Matches(text);
        if (matches.Count == 0) return new List<WordStamp>();
        var words = new List<WordStamp>();
        for (var i = 0; i < matches.Count; i++)
        {
            var ts = ToTimestamp(matches[i].Groups["m"].Value, matches[i].Groups["s"].Value, matches[i].Groups["x"].Value);
            if (ts is null) continue;
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var word = text.Substring(start, end - start);
            if (word.Length == 0) continue;
            words.Add(new WordStamp(ts.Value, word));
        }
        return words;
    }

    public static int CurrentLineIndex(List<LrcLine> lines, TimeSpan position)
    {
        var idx = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Timestamp <= position) idx = i;
            else break;
        }
        return idx;
    }

    public static int SungWordCount(LrcLine line, TimeSpan position)
    {
        var n = 0;
        foreach (var w in line.Words)
        {
            if (w.Timestamp <= position) n++;
            else break;
        }
        return n;
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
