using System.Diagnostics;
using System.Runtime.InteropServices;
namespace YTMTaskbarWidget.Services;
public static class BrowserFocus
{
    public static bool FocusYtmTab(string? trackTitle, string? artist, string? aumid)
    {
        var windows = BrowserWindows(aumid);
        if (windows.Count == 0)
            return false;
        // 1) The track's tab is already the active tab of some window.
        foreach (var hwnd in windows)
        {
            var title = GetTitle(hwnd);
            if (TabNameMatches(trackTitle, artist, title))
                return Focus(hwnd);
        }
        // 2) Shell window registry (works where the shell exposes browser tabs).
        try { if (FocusShellTab()) return true; } catch { }
        // 3) Background tab: use the browser's own tab-search popup —
        //    Ctrl+Shift+A, type the track title, Enter. Requires foreground
        //    ownership so keystrokes can never land in another application.
        if (!string.IsNullOrWhiteSpace(trackTitle))
        {
            try { if (FocusViaTabSearch(windows[0], trackTitle)) return true; } catch { }
        }
        // 4) Last resort: bring the browser forward as-is.
        return Focus(windows[0]);
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

    private static bool FocusViaTabSearch(IntPtr hwnd, string trackTitle)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, 9 /*SW_RESTORE*/);
        var root = GetAncestor(hwnd, 2 /*GA_ROOT*/);
        if (root != IntPtr.Zero)
            hwnd = root;
        if (!SetForegroundWindow(hwnd))
        {
            // Foreground lock: synthesizing a VK_MENU press grants permission.
            keybd_event(0x12, 0, 0, 0);
            SetForegroundWindow(hwnd);
            keybd_event(0x12, 0, 2 /*KEYEVENTF_KEYUP*/, 0);
        }
        if (!WaitForeground(hwnd, 800))
            return false;
        Thread.Sleep(120);

        // Ctrl+Shift+A → tab search popup (Chrome / Edge / Brave).
        SendVk(0x11, true);   // Ctrl
        SendVk(0x10, true);   // Shift
        SendVk(0x41, true);   // A
        SendVk(0x41, false);
        SendVk(0x10, false);
        SendVk(0x11, false);
        Thread.Sleep(220);

        // Type the track title into the auto-focused search box.
        var typed = 0;
        foreach (var ch in trackTitle)
        {
            if (typed >= 60) break;
            if (char.IsControl(ch)) continue;
            SendUnicode(ch);
            typed++;
        }
        if (typed == 0)
            return false;
        Thread.Sleep(180);

        // Enter → activate the top match.
        SendVk(0x0D, true);
        SendVk(0x0D, false);
        Thread.Sleep(120);
        return true;
    }

    private static bool WaitForeground(IntPtr hwnd, int timeoutMs)
    {
        var waited = 0;
        while (waited < timeoutMs)
        {
            if (GetForegroundWindow() == hwnd)
                return true;
            Thread.Sleep(25);
            waited += 25;
        }
        return GetForegroundWindow() == hwnd;
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
        keybd_event(0x12, 0, 0, 0);
        var ok = SetForegroundWindow(hwnd);
        keybd_event(0x12, 0, 2 /*KEYEVENTF_KEYUP*/, 0);
        return ok;
    }

    // ---- synthetic input -------------------------------------------------

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private static void SendVk(byte vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = down ? 0 : KEYEVENTF_KEYUP
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendUnicode(char ch)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
    private static extern IntPtr GetForegroundWindow();

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
