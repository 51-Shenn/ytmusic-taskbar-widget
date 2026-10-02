namespace YTMTaskbarWidget.Services;
public sealed record WidgetPos(double Left, double Top);
public static class TaskbarPositioner
{
    public static double RequiredWindowHeight(double contentHeight, double verticalMargin, double shadowBreathingRoom) =>
        contentHeight + verticalMargin * 2 + shadowBreathingRoom * 2;

    public static WidgetPos CalcBottomCenter(double screenWidth, double screenHeight, double taskbarHeight, double windowWidth, double windowHeight, double offsetX)
    {
        var left = (screenWidth - windowWidth) / 2 + offsetX;
        var top = screenHeight - taskbarHeight + Math.Max(0, (taskbarHeight - windowHeight) / 2);
        return new(left, Math.Min(top, screenHeight - windowHeight));
    }
}
