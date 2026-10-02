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

    public static List<string> SplitPlainWords(string text)
    {
        var parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
            words.Add(i + 1 < parts.Length ? parts[i] + " " : parts[i]);
        return words;
    }

    public static int EstimateSungCount(int wordCount, TimeSpan lineStart, TimeSpan lineEnd, TimeSpan position)
    {
        if (wordCount <= 0)
            return 0;
        if (position < lineStart)
            return 0;
        if (position >= lineEnd)
            return wordCount;
        var totalMs = (lineEnd - lineStart).TotalMilliseconds;
        if (totalMs <= 0)
            return wordCount;
        var elapsedMs = (position - lineStart).TotalMilliseconds;
        var fraction = Math.Max(0, Math.Min(1, elapsedMs / totalMs));
        // +1 so the first word lights the moment its line starts,
        // matching timestamped behavior at pos == lineStart.
        return Math.Max(1, Math.Min(wordCount, (int)(fraction * wordCount) + 1));
    }

    public static int EstimateSungCount(IReadOnlyList<string> words, TimeSpan lineStart, TimeSpan lineEnd, TimeSpan position)
    {
        var wordCount = words?.Count ?? 0;
        if (wordCount <= 0)
            return 0;
        if (position < lineStart)
            return 0;
        if (position >= lineEnd)
            return wordCount;
        var totalMs = (lineEnd - lineStart).TotalMilliseconds;
        if (totalMs <= 0)
            return wordCount;
        var elapsedMs = (position - lineStart).TotalMilliseconds;
        double totalWeight = 0;
        var weights = new double[wordCount];
        for (var i = 0; i < wordCount; i++)
        {
            var w = Math.Max(1, (words![i] ?? string.Empty).Trim().Length);
            weights[i] = w;
            totalWeight += w;
        }
        if (totalWeight <= 0)
            return EstimateSungCount(wordCount, lineStart, lineEnd, position);
        // First word lights at line start; each following word flips when
        // its weighted share of the line duration has elapsed.
        var acc = 0.0;
        for (var i = 0; i < wordCount; i++)
        {
            acc += weights[i] / totalWeight * totalMs;
            if (elapsedMs < acc)
                return Math.Max(1, i + 1);
        }
        return wordCount;
    }

    public static double WordProgress(LrcLine line, TimeSpan lineEnd, TimeSpan position)
    {
        if (line.Words.Count == 0)
            return 0;
        var sung = SungWordCount(line, position);
        if (sung <= 0)
            return 0;
        var active = Math.Min(sung, line.Words.Count) - 1;
        var start = line.Words[active].Timestamp;
        var end = active + 1 < line.Words.Count ? line.Words[active + 1].Timestamp : lineEnd;
        var totalMs = (end - start).TotalMilliseconds;
        if (totalMs <= 0)
            return 1;
        var elapsedMs = (position - start).TotalMilliseconds;
        return Math.Max(0, Math.Min(1, elapsedMs / totalMs));
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
