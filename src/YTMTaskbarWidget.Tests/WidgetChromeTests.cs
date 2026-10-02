using Xunit;

namespace YTMTaskbarWidget.Tests;

public sealed class WidgetChromeTests
{
    [Fact]
    public void Hover_Chrome_Is_Subtle_Glass_Background_Without_Outline_Or_Shadow()
    {
        var xaml = File.ReadAllText(FindMainWindowXaml());

        Assert.Contains("Margin=\"12,0\"", xaml);
        Assert.Contains("<LinearGradientBrush StartPoint=\"0,0\" EndPoint=\"0,1\">", xaml);
        Assert.Contains("<GradientStop Color=\"#33FFFFFF\" Offset=\"0.0\"/>", xaml);
        Assert.Contains("<GradientStop Color=\"#40262626\" Offset=\"0.35\"/>", xaml);
        Assert.DoesNotContain("<SolidColorBrush Color=\"#FFFFFFFF\" Opacity=\"0.4\"/>", xaml);
        Assert.DoesNotContain("DropShadowEffect", xaml);
        Assert.DoesNotContain("<Setter Property=\"BorderBrush\">", xaml);
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
}
