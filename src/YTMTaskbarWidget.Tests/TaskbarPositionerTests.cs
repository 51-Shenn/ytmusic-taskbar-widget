using Xunit;
using YTMTaskbarWidget.Services;

namespace YTMTaskbarWidget.Tests;

public sealed class TaskbarPositionerTests
{
    [Fact]
    public void Centers_On_Bottom_Taskbar()
    {
        var pos = TaskbarPositioner.CalcBottomCenter(screenWidth: 1920, screenHeight: 1080, taskbarHeight: 48, windowWidth: 360, windowHeight: 48, offsetX: 0);

        Assert.Equal(780, pos.Left);
        Assert.Equal(1032, pos.Top);
    }

    [Fact]
    public void Keeps_Widget_On_Screen_When_Taskbar_Is_Shorter_Than_Widget()
    {
        var pos = TaskbarPositioner.CalcBottomCenter(screenWidth: 1920, screenHeight: 1080, taskbarHeight: 40, windowWidth: 340, windowHeight: 44, offsetX: 0);

        Assert.True(pos.Top + 44 <= 1080);
    }

    [Fact]
    public void Window_Height_Includes_Visible_Pill_And_Shadow_Breathing_Room()
    {
        var height = TaskbarPositioner.RequiredWindowHeight(contentHeight: 44, verticalMargin: 0, shadowBreathingRoom: 6);

        Assert.Equal(56, height);
    }
}
