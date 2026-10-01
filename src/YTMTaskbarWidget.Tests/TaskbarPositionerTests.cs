using Xunit; using YTMTaskbarWidget.Services;
namespace YTMTaskbarWidget.Tests;
public sealed class TaskbarPositionerTests { [Fact] public void Centers_On_Bottom_Taskbar() { var pos = TaskbarPositioner.CalcBottomCenter(screenWidth: 1920, screenHeight: 1080, taskbarHeight: 48, windowWidth: 360, windowHeight: 48, offsetX: 0); Assert.Equal(780, pos.Left); Assert.Equal(1032, pos.Top); } }
