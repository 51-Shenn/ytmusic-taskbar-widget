namespace YTMTaskbarWidget.Services;
public sealed record WidgetPos(double Left, double Top);
public static class TaskbarPositioner { public static WidgetPos CalcBottomCenter(double screenWidth, double screenHeight, double taskbarHeight, double windowWidth, double windowHeight, double offsetX) => new((screenWidth - windowWidth) / 2 + offsetX, screenHeight - taskbarHeight); }
