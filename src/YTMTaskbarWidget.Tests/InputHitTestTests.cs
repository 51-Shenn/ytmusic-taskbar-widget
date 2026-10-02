using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace YTMTaskbarWidget.Tests;

public sealed class InputHitTestTests
{
    // Regression: clicking the lyric line crashed the app with
    //   System.InvalidOperationException: 'System.Windows.Documents.Run' is not a Visual or Visual3D.
    // thrown from VisualTreeHelper.GetParent, reached via IsOverButton during
    // DragZone_PreviewMouseLeftButtonDown. The lyric line is built from Run
    // elements, and a Run is a DependencyObject but never a Visual.
    [Fact]
    public void IsOverButton_With_Bare_Run_Source_Returns_False_Without_Throwing()
    {
        var run = new Run("lyric word");

        Assert.False(MainWindow.IsOverButton(run));
    }

    [Fact]
    public void IsOverDragHandle_With_Bare_Run_Source_Returns_False_Without_Throwing()
    {
        var run = new Run("lyric word");

        Assert.False(MainWindow.IsOverDragHandle(run));
    }

    [Fact]
    public void IsOverButton_With_Run_Nested_In_TextBlock_Returns_False_Without_Throwing()
    {
        OnStaThread(() =>
        {
            var run = new Run("word one");
            var textBlock = new TextBlock();
            textBlock.Inlines.Add(run);
            textBlock.Inlines.Add(new Run("word two"));

            Assert.False(MainWindow.IsOverButton(run));
        });
    }

    [Fact]
    public void IsOverButton_Still_Detects_A_Button()
    {
        OnStaThread(() => Assert.True(MainWindow.IsOverButton(new Button())));
    }

    [Fact]
    public void IsOverButton_With_Null_Source_Is_False()
    {
        Assert.False(MainWindow.IsOverButton(null));
    }

    // FrameworkElement construction (Button, TextBlock) requires an STA thread;
    // xUnit runs tests on MTA threads.
    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
