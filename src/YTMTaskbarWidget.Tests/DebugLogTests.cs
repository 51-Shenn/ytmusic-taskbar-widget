using Xunit;

namespace YTMTaskbarWidget.Tests;

public sealed class DebugLogTests
{
    [Fact]
    public void LogTo_Truncates_Once_The_File_Exceeds_The_Size_Cap()
    {
        var path = NewTempLog();
        try
        {
            File.WriteAllText(path, new string('x', (int)MainWindow.MaxLogBytes + 1));

            MainWindow.LogTo(path, "after-truncation");

            var info = new FileInfo(path);
            Assert.True(info.Length <= MainWindow.MaxLogBytes, $"log is {info.Length} bytes, cap is {MainWindow.MaxLogBytes}");
            Assert.Contains("after-truncation", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LogTo_Appends_Under_The_Cap_Without_Discarding_History()
    {
        var path = NewTempLog();
        try
        {
            MainWindow.LogTo(path, "first-line");
            MainWindow.LogTo(path, "second-line");

            var text = File.ReadAllText(path);
            Assert.Contains("first-line", text);
            Assert.Contains("second-line", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string NewTempLog() =>
        Path.Combine(Path.GetTempPath(), $"ytm-log-test-{Guid.NewGuid():N}.log");
}
