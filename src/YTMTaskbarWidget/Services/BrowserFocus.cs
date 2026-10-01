using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
namespace YTMTaskbarWidget.Services;
public static class BrowserFocus
{
    public static bool FocusYtmTab(string? trackTitle, string? artist, string? aumid)
    {
        // 1) Find the exact tab (including background tabs) whose page title
        //    matches the playing track and invoke it via UI Automation.
        try { if (FocusTabViaUia(trackTitle, artist, aumid)) return true; } catch { }
        // 2) Shell window registry: tab whose URL is music.youtube.com.
        try { if (FocusShellTab()) return true; } catch { }
        // 3) A browser window already showing the track (active tab match).
        try { if (FocusWindowByTitle(trackTitle, artist, aumid)) return true; } catch { }
        // 4) Any visible window of the browser that owns the media session.
        try { return FocusProcessWindow(aumid); } catch { return false; }
    }

    private static bool FocusTabViaUia(string? trackTitle, string? artist, string? aumid)
    {
        foreach (var hwnd in BrowserWindows(aumid))
        {
            var root = AutomationElement.FromHandle(hwnd);
            var tabs = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            foreach (AutomationElement tab in tabs)
            {
                if (!TabNameMatches(trackTitle, artist, tab.Current.Name))
                    continue;
                if (tab.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)
                    && pattern is InvokePattern invoke)
                {
                    invoke.Invoke();
                    Focus(hwnd);
                    return true;
                }
            }
        }
        return false;
    }

    public static bool TabNameMatches(string? trackTitle, string? artist, string? tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName))
            return false;
        if (!string.IsNullOrWhiteSpace(trackTitle) &&
            (tabName.Contains(trackTitle, StringComparison.OrdinalIgnoreCase)
             || trackTitle.Contains(tabName, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!string.IsNullOrWhiteSpace(artist) &&
            tabName.Contains(artist, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool FocusWindowByTitle(string? trackTitle, string? artist, string? aumid)
    {
        foreach (var hwnd in BrowserWindows(aumid))
        {
            var title = GetTitle(hwnd);
            if (TabNameMatches(trackTitle, artist, title))
                return Focus(hwnd);
        }
        return false;
    }

    private static List<IntPtr> BrowserWindows(string? aumid)
    {
        var result = new List<IntPtr>();
        var exe = ExeFromAumid(aumid);
        if (exe is null)
            return result;
        var selfPid = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid == 0 || pid == selfPid) return true;
            try
            {
                var name = Process.GetProcessById((int)pid).ProcessName;
                if (!string.Equals(name, exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true;
            }
            if (!IsWindowVisible(h)) return true;
            if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
            result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;
        var sb = new System.Text.StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool FocusShellTab()
    {
        var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (type is null) return false;
        var shellWindows = Activator.CreateInstance(type);
        if (shellWindows is null) return false;
        try
        {
            dynamic shell = shellWindows;
            var count = (int)shell.Count;
            for (var i = 1; i <= count; i++)
            {
                dynamic? item = null;
                try
                {
                    item = shell.Item(i);
                    var url = (string?)item.LocationURL;
                    if (url is not null && url.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var hwnd = new IntPtr((int)item.HWND);
                        if (hwnd != IntPtr.Zero && Focus(hwnd))
                            return true;
                    }
                }
                catch
                {
                }
                finally
                {
                    if (item is not null)
                    {
                        try { Marshal.FinalReleaseComObject(item); } catch { }
                    }
                }
            }
        }
        finally
        {
            try { Marshal.FinalReleaseComObject(shellWindows); } catch { }
        }
        return false;
    }

    private static bool FocusProcessWindow(string? aumid)
    {
        var windows = BrowserWindows(aumid);
        return windows.Count > 0 && Focus(windows[0]);
    }

    private static string? ExeFromAumid(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        if (aumid.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "chrome";
        if (aumid.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "msedge";
        if (aumid.Contains("brave", StringComparison.OrdinalIgnoreCase)) return "brave";
        return null;
    }

    private static bool Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, 9 /*SW_RESTORE*/);
        var root = GetAncestor(hwnd, 2 /*GA_ROOT*/);
        if (root != IntPtr.Zero)
            hwnd = root;
        if (SetForegroundWindow(hwnd))
            return true;
        // Foreground lock: synthesizing a VK_MENU press grants permission.
        keybd_event(0x12, 0, 0, 0);
        var ok = SetForegroundWindow(hwnd);
        keybd_event(0x12, 0, 2 /*KEYEVENTF_KEYUP*/, 0);
        return ok;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int nCmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, int gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
