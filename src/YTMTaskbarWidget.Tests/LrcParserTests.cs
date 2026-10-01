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
}
