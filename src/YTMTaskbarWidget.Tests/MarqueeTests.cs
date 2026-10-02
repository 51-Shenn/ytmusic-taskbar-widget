using YTMTaskbarWidget;
using Xunit;

namespace YTMTaskbarWidget.Tests;

public sealed class MarqueeTests
{
    [Fact]
    public void LoopDistance_Is_FirstCopy_Plus_Gap()
    {
        var distance = MainWindow.MarqueeLoopDistance(titleWidth: 120, duplicatedWidth: 255);

        Assert.Equal(135, distance);
    }

    [Fact]
    public void LoopDistance_Is_Zero_When_Measurements_Are_Invalid()
    {
        Assert.Equal(0, MainWindow.MarqueeLoopDistance(titleWidth: 0, duplicatedWidth: 255));
        Assert.Equal(0, MainWindow.MarqueeLoopDistance(titleWidth: 120, duplicatedWidth: 120));
    }

    [Fact]
    public void RenderWidth_Uses_Duplicated_Text_Width_For_Active_Marquee()
    {
        var width = MainWindow.MarqueeRenderWidth(titleWidth: 120, duplicatedWidth: 255);

        Assert.Equal(255, width);
    }

    [Fact]
    public void RenderWidth_Is_Auto_When_Measurements_Are_Invalid()
    {
        Assert.True(double.IsNaN(MainWindow.MarqueeRenderWidth(titleWidth: 0, duplicatedWidth: 255)));
        Assert.True(double.IsNaN(MainWindow.MarqueeRenderWidth(titleWidth: 120, duplicatedWidth: 120)));
    }

    [Fact]
    public void LoopText_Uses_Larger_Readable_Gap_Between_Copies()
    {
        Assert.Equal("Song Title          Song Title", MainWindow.MarqueeLoopText("Song Title"));
    }

    [Fact]
    public void SeamPause_Is_Four_Seconds()
    {
        Assert.Equal(4000, MainWindow.MarqueeSeamPauseMs);
    }

    [Theory]
    [InlineData(100, 150, 5, 3, 0)]
    [InlineData(0, 150, 5, 3, 0)]
    [InlineData(200, 0, 5, 3, 0)]
    [InlineData(200, 150, 1, 1, 0)]
    [InlineData(200, 150, 4, 0, 0)]
    [InlineData(200, 150, 4, 1, 0)]
    [InlineData(200, 150, 4, 4, 50)]
    [InlineData(200, 150, 4, 2, 50.0 / 3)]
    public void LyricScrollOffset_Follows_Sung_Fraction(
        double natural, double avail, int words, int sung, double expected)
    {
        Assert.Equal(expected, MainWindow.LyricScrollOffset(natural, avail, words, sung), 5);
    }

    [Theory]
    // Paused must kill the infinite loop and snap the title back to offset 0.
    [InlineData(false, 190, 0, 190, 0, 0)]
    [InlineData(false, 190, 2000, 190, 0, 0)]
    // Playing: one 6px step per 150ms tick.
    [InlineData(true, 10, 0, 190, 16, 0)]
    // Reaching the seam parks at the end and starts the 4s hold.
    [InlineData(true, 186, 0, 190, 190, 4000)]
    // The hold counts down in place, then rewinds to 0.
    [InlineData(true, 190, 4000, 190, 190, 3850)]
    [InlineData(true, 190, 150, 190, 0, 0)]
    // Inactive marquee (loopMax 0) never moves.
    [InlineData(true, 10, 0, 0, 10, 0)]
    public void NextMarquee_Steps_The_Scroll_State(
        bool isPlaying, double offset, int holdMs, double loopMax,
        double expectedOffset, int expectedHold)
    {
        var (nextOffset, nextHold) = MainWindow.NextMarquee(offset, holdMs, loopMax, isPlaying);

        Assert.Equal(expectedOffset, nextOffset);
        Assert.Equal(expectedHold, nextHold);
    }
}
