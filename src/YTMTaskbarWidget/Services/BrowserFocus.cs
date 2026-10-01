using System.Diagnostics;
using System.Runtime.InteropServices;
namespace YTMTaskbarWidget.Services;
public static class BrowserFocus
{
    public static bool FocusYtmTab(string? aumid)
    {
        // 1) Find the exact browser tab whose URL is music.youtube.com via the
        //    shell's window registry (all Chromium browsers expose their tabs
        //    there) and bring its top-level window forward.
        try { if (FocusShellTab()) return true; } catch { }
        // 2) Fall back to any visible window of the browser that owns the
        //    SMTC session (its active tab may already be the YTM one).
        try { return FocusProcessWindow(aumid); } catch { return false; }
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
        var exe = ExeFromAumid(aumid);
        if (exe is null) return false;
        var found = IntPtr.Zero;
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
            found = h;
            return false;
        }, IntPtr.Zero);
        return found != IntPtr.Zero && Focus(found);
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

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
