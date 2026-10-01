using YTMTaskbarWidget.Services;
using Xunit;
namespace YTMTaskbarWidget.Tests;
public sealed class BrowserFocusTests
{
    [Fact]
    public void Matches_Title_CaseInsensitive()
    {
        Assert.True(BrowserFocus.TabNameMatches("colors", "Gareth.T", "COLORS - YouTube Music"));
    }
    [Fact]
    public void Matches_When_TabName_Is_Substring_Of_Title()
    {
        Assert.True(BrowserFocus.TabNameMatches("colors - Gareth.T", "Gareth.T", "colors"));
    }
    [Fact]
    public void Falls_Back_To_Artist()
    {
        Assert.True(BrowserFocus.TabNameMatches("some long track title", "Gareth.T", "whatever - Gareth.T - YouTube Music"));
    }
    [Fact]
    public void No_Match_Returns_False()
    {
        Assert.False(BrowserFocus.TabNameMatches("colors", "Gareth.T", "Unrelated Page - YouTube"));
    }
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("colors", "Gareth.T", "")]
    [InlineData("colors", "Gareth.T", "   ")]
    public void Empty_Inputs_Return_False(string? title, string? artist, string? tabName)
    {
        Assert.False(BrowserFocus.TabNameMatches(title, artist, tabName));
    }
    [Fact]
    public void Null_Track_With_Matching_Artist_Still_Matches()
    {
        Assert.True(BrowserFocus.TabNameMatches(null, "Gareth.T", "Gareth.T - YouTube Music"));
    }
}
