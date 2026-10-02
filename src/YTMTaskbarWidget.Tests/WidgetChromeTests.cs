using Xunit;

namespace YTMTaskbarWidget.Tests;

public sealed class WidgetChromeTests
{
    [Fact]
    public void Hover_Chrome_Is_White_Glass_Background_Without_Dark_Stops_Outline_Or_Shadow()
    {
        var xaml = File.ReadAllText(FindMainWindowXaml());

        Assert.Contains("Margin=\"12,0\"", xaml);
        Assert.Contains("<LinearGradientBrush StartPoint=\"0,0\" EndPoint=\"0,1\">", xaml);
        Assert.Contains("<GradientStop Color=\"#22FFFFFF\" Offset=\"0.0\"/>", xaml);
        Assert.Contains("<GradientStop Color=\"#0FFFFFFF\" Offset=\"1.0\"/>", xaml);
        Assert.Contains("x:Key=\"DragHandleChrome\"", xaml);
        Assert.Contains("Binding=\"{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Border}}\"", xaml);
        Assert.Contains("<Setter Property=\"Background\" Value=\"#33FFFFFF\"/>", xaml);
        Assert.Contains("<Setter Property=\"Background\" Value=\"#77FFFFFF\"/>", xaml);
        Assert.Contains("x:Name=\"DragHandle\"", xaml);
        Assert.Contains("Width=\"10\" Height=\"20\"", xaml);
        Assert.Contains("Width=\"3\" Height=\"16\"", xaml);
        Assert.Contains("CornerRadius=\"1.5\"", xaml);
        Assert.Contains("Margin=\"-3,0,4,0\"", xaml);
        Assert.DoesNotContain("#40262626", xaml);
        Assert.DoesNotContain("#30101010", xaml);
        Assert.DoesNotContain("<GradientStop Color=\"#33FFFFFF\" Offset=\"0.0\"/>", xaml);
        Assert.DoesNotContain("<SolidColorBrush Color=\"#FFFFFFFF\" Opacity=\"0.4\"/>", xaml);
        Assert.DoesNotContain("DropShadowEffect", xaml);
        Assert.DoesNotContain("<Setter Property=\"BorderBrush\">", xaml);

        var code = File.ReadAllText(FindMainWindowCode());
        Assert.Contains("if (!IsOverDragHandle(e.OriginalSource))", code);
        Assert.Contains("internal static bool IsOverDragHandle", code);
    }

    private static string FindMainWindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "YTMTaskbarWidget", "MainWindow.xaml");
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find MainWindow.xaml");
    }

    private static string FindMainWindowCode()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "YTMTaskbarWidget", "MainWindow.xaml.cs");
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find MainWindow.xaml.cs");
    }
}
