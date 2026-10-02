using YTMTaskbarWidget.Services;
using Xunit;
namespace YTMTaskbarWidget.Tests;
public sealed class LrcParserTests
{
    [Fact]
    public void Parses_Synced_Lines_And_Finds_Current()
    {
        const string lrc = "[00:10.00]line one\n[00:20.50]line two\n[00:30.00]line three\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Equal(3, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Timestamp);
        Assert.Equal("line one", lines[0].Text);
        Assert.Equal("line one", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(15)));
        Assert.Equal("line two", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(20.5)));
        Assert.Equal("line three", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(99)));
    }
    [Fact]
    public void Ignores_Metadata_And_Empty()
    {
        const string lrc = "[ti:Title]\n[ar:Artist]\n[00:05.00]hi\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Single(lines);
        Assert.Equal("hi", lines[0].Text);
    }
    [Fact]
    public void Parses_Single_Digit_Fraction()
    {
        var lines = LrcParser.Parse("[00:10.5]hello\n");
        Assert.Single(lines);
        Assert.Equal(TimeSpan.FromMilliseconds(10500), lines[0].Timestamp);
        Assert.Equal("hello", lines[0].Text);
    }
    [Fact]
    public void Parses_Repeated_Timestamps()
    {
        var lines = LrcParser.Parse("[00:10.00][00:20.00]same text\n");
        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(20), lines[1].Timestamp);
        Assert.Equal("same text", lines[0].Text);
        Assert.Equal("same text", lines[1].Text);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_Or_Empty_Returns_Empty(string? input)
    {
        Assert.Empty(LrcParser.Parse(input));
    }
    [Fact]
    public void Invalid_Lines_Ignored()
    {
        const string lrc = "no timestamp\n[00:10.00]valid\n[ti:Title]\n[00:xx.00]bad\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Single(lines);
        Assert.Equal("valid", lines[0].Text);
    }
    [Fact]
    public void Unsorted_Input_Sorted()
    {
        const string lrc = "[00:30.00]three\n[00:10.00]one\n[00:20.00]two\n";
        var lines = LrcParser.Parse(lrc);
        Assert.Equal(3, lines.Count);
        Assert.Equal("one", lines[0].Text);
        Assert.Equal("two", lines[1].Text);
        Assert.Equal("three", lines[2].Text);
    }
    [Fact]
    public void CurrentLine_Before_First_Returns_Null()
    {
        var lines = LrcParser.Parse("[00:10.00]one\n[00:20.00]two\n");
        Assert.Null(LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public void CurrentLine_Exact_Boundary_Returns_That_Line()
    {
        var lines = LrcParser.Parse("[00:10.00]one\n[00:20.00]two\n");
        Assert.Equal("one", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(10)));
        Assert.Equal("two", LrcParser.CurrentLine(lines, TimeSpan.FromSeconds(20)));
    }
    [Fact]
    public void Parses_Inline_Word_Tags_And_Strips_Them()
    {
        var lines = LrcParser.Parse("[00:10.00]<00:10.00>hel <00:11.00>lo\n");
        Assert.Single(lines);
        Assert.Equal("hel lo", lines[0].Text);
        Assert.Equal(2, lines[0].Words.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Words[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(11), lines[0].Words[1].Timestamp);
        Assert.Equal(0, LrcParser.SungWordCount(lines[0], TimeSpan.FromSeconds(9)));
        Assert.Equal(1, LrcParser.SungWordCount(lines[0], TimeSpan.FromSeconds(10.5)));
        Assert.Equal(2, LrcParser.SungWordCount(lines[0], TimeSpan.FromSeconds(12)));
        Assert.Equal(-1, LrcParser.CurrentLineIndex(lines, TimeSpan.FromSeconds(9)));
    }
    [Fact]
    public void Plain_Lines_Have_No_Words()
    {
        var lines = LrcParser.Parse("[00:10.00]just text\n");
        Assert.Single(lines);
        Assert.Empty(lines[0].Words);
    }
    [Fact]
    public void SplitPlainWords_Keeps_Spacing_For_Karaoke_Runs()
    {
        var words = LrcParser.SplitPlainWords("hello brave world");
        Assert.Equal(new[] { "hello ", "brave ", "world" }, words);
    }
    [Fact]
    public void SplitPlainWords_Empty_Returns_Empty()
    {
        Assert.Empty(LrcParser.SplitPlainWords("   "));
    }
    [Theory]
    [InlineData(10, 14, 10, 4, 1)]
    [InlineData(10, 14, 11, 4, 2)]
    [InlineData(10, 14, 12, 4, 3)]
    [InlineData(10, 14, 13, 4, 4)]
    [InlineData(10, 14, 14, 4, 4)]
    [InlineData(10, 14, 9, 4, 0)]
    public void EstimateSungCount_Spreads_Words_Evenly(
        int startS, int endS, int posS, int wordCount, int expected)
    {
        var sung = LrcParser.EstimateSungCount(
            wordCount,
            TimeSpan.FromSeconds(startS),
            TimeSpan.FromSeconds(endS),
            TimeSpan.FromSeconds(posS));
        Assert.Equal(expected, sung);
    }
    [Fact]
    public void EstimateSungCount_Zero_Words_Returns_Zero()
    {
        Assert.Equal(0, LrcParser.EstimateSungCount(
            0, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(12)));
    }
    [Fact]
    public void EstimateSungCount_Weighted_Long_Word_Holds_Longer()
    {
        var words = new[] { "supercalifragilistic ", "a" };
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(14);
        Assert.Equal(1, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(11)));
        Assert.Equal(1, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(13)));
        Assert.Equal(2, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(14)));
        Assert.Equal(0, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(9)));
    }
    [Fact]
    public void EstimateSungCount_Weighted_Equal_Words_Matches_Even_Split()
    {
        var words = new[] { "one ", "two ", "three ", "four" };
        var start = TimeSpan.FromSeconds(10);
        var end = TimeSpan.FromSeconds(14);
        Assert.Equal(1, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(10)));
        Assert.Equal(2, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(11)));
        Assert.Equal(4, LrcParser.EstimateSungCount(words, start, end, TimeSpan.FromSeconds(13)));
    }
    [Fact]
    public void WordProgress_Uses_Next_Word_As_Denominator()
    {
        var lines = LrcParser.Parse("[00:10.00]<00:10.00>hel <00:12.00>lo\n");
        var line = lines[0];
        var lineEnd = TimeSpan.FromSeconds(14);
        Assert.Equal(0, LrcParser.WordProgress(line, lineEnd, TimeSpan.FromSeconds(9)), 5);
        Assert.Equal(0.5, LrcParser.WordProgress(line, lineEnd, TimeSpan.FromSeconds(11)), 5);
        Assert.Equal(0.5, LrcParser.WordProgress(line, lineEnd, TimeSpan.FromSeconds(13)), 5);
        Assert.Equal(1, LrcParser.WordProgress(line, lineEnd, TimeSpan.FromSeconds(14)), 5);
    }
    [Fact]
    public void WordProgress_Plain_Line_Returns_Zero()
    {
        var lines = LrcParser.Parse("[00:10.00]just text\n");
        Assert.Equal(0, LrcParser.WordProgress(lines[0], TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(12)));
    }
}
