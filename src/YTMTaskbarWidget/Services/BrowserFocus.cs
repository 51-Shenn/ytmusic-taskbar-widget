using System.Diagnostics;
using System.Runtime.InteropServices;
namespace YTMTaskbarWidget.Services;
public static class BrowserFocus
{
    // Session-only cache of the window that most recently hosted the playing
    // YTM tab. Browser tab identities are not exposed to any OS API (the tab
    // strip is UIA-invisible and Chrome's tab IDs need --remote-debugging),
    // so the window handle is the closest available "tab id". Handles die
    // with their window, so the cache is never persisted and is discarded
    // as soon as the handle disappears from the current window list.
    private static IntPtr _lastYtmHwnd;

    // Serializes callers: concurrent tab-search sweeps would interleave
    // synthetic keystrokes and break both.
    private static readonly object Sync = new();

    public static bool FocusYtmTab(string? trackTitle, string? artist, string? aumid)
    {
        lock (Sync)
        {
            var windows = BrowserWindows(aumid);
            if (_lastYtmHwnd != IntPtr.Zero && !windows.Contains(_lastYtmHwnd))
            {
                _lastYtmHwnd = IntPtr.Zero; // cached window was closed
                MainWindow.Log("focus cache dropped (window closed)");
            }

            // 1) The track's tab is already the active tab of some window —
            //    cached window first, since it hosted the track last time.
            if (_lastYtmHwnd != IntPtr.Zero &&
                TabNameMatches(trackTitle, artist, GetTitle(_lastYtmHwnd)))
            {
                MainWindow.Log($"focus cache hit hwnd=0x{_lastYtmHwnd.ToInt64():X}");
                if (Focus(_lastYtmHwnd))
                    return true;
            }
            foreach (var hwnd in windows)
            {
                if (!TabNameMatches(trackTitle, artist, GetTitle(hwnd)))
                    continue;
                MainWindow.Log($"focus active-tab match hwnd=0x{hwnd.ToInt64():X}");
                _lastYtmHwnd = hwnd; // identity confirmed even if focusing fails
                if (Focus(hwnd))
                    return true;
            }
            if (windows.Count == 0)
                return false;

            // 2) Shell window registry (works where the shell exposes browser tabs).
            try
            {
                if (FocusShellTab())
                {
                    MainWindow.Log("focus shell registry match");
                    return true;
                }
            }
            catch { }

            // 3) Background tab: tab-search popup per window — Ctrl+Shift+A,
            //    type a query, Enter, verify. The query ladder starts with the
            //    track title (verified against the track) and falls back to
            //    player-name keywords, because some players never put the song
            //    into the tab title (Spotify web keeps a generic one), which is
            //    exactly the "music running in the background" case. Cached
            //    window first, then every other window.
            foreach (var hwnd in CachedFirst(windows))
            {
                foreach (var (query, verify, titleVerified) in BuildQueryVariants(trackTitle, artist))
                {
                    try
                    {
                        if (!FocusViaTabSearch(hwnd, query, verify))
                            continue;
                        if (titleVerified)
                        {
                            MainWindow.Log($"focus tab-search ok hwnd=0x{hwnd.ToInt64():X} (cache set) query='{query}'");
                            _lastYtmHwnd = hwnd;
                        }
                        else
                        {
                            MainWindow.Log($"focus keyword jump hwnd=0x{hwnd.ToInt64():X} query='{query}'");
                            _lastGoodKeyword = query;
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Log($"focus tab-search threw hwnd=0x{hwnd.ToInt64():X} {ex.GetType().Name}");
                    }
                }
            }

            // 4) Last resort: bring the first browser window forward as-is.
            MainWindow.Log($"focus last-resort hwnd=0x{windows[0].ToInt64():X}");
            return Focus(windows[0]);
        }
    }

    private static IEnumerable<IntPtr> CachedFirst(List<IntPtr> windows)
    {
        if (_lastYtmHwnd != IntPtr.Zero)
            yield return _lastYtmHwnd;
        foreach (var hwnd in windows)
        {
            if (hwnd != _lastYtmHwnd)
                yield return hwnd;
        }
    }

    // Session memory of the keyword query that last found the playing tab,
    // tried before the hardcoded player keywords on the next double-click.
    private static string? _lastGoodKeyword;

    private static List<(string Query, Func<string, bool> Verify, bool TitleVerified)> BuildQueryVariants(
        string? trackTitle, string? artist)
    {
        var list = new List<(string Query, Func<string, bool> Verify, bool TitleVerified)>();
        void Add(string? query, Func<string, bool> verify, bool titleVerified)
        {
            if (string.IsNullOrWhiteSpace(query) || list.Any(v => v.Query == query))
                return;
            list.Add((query, verify, titleVerified));
        }

        if (!string.IsNullOrWhiteSpace(trackTitle))
        {
            Func<string, bool> matches = t => TabNameMatches(trackTitle, artist, t);
            Add(BuildSearchQuery(trackTitle, artist!), matches, true);
            Add(trackTitle, matches, true);
            Add(artist, matches, true);
        }
        // Player keywords: tab-search matches tab titles, not playback state,
        // so identify the player instead when the song never appears in titles.
        // "spotify" needs "web player" too — an unrelated tab can contain
        // "spotify" in a login-code subject line.
        if (!string.IsNullOrWhiteSpace(_lastGoodKeyword))
            Add(_lastGoodKeyword, KeywordVerify(_lastGoodKeyword!), false);
        Add("spotify", KeywordVerify("spotify"), false);
        Add("youtube music", KeywordVerify("youtube music"), false);
        return list;
    }

    private static Func<string, bool> KeywordVerify(string keyword)
    {
        if (keyword.Equals("spotify", StringComparison.OrdinalIgnoreCase))
            return t => t.Contains("spotify", StringComparison.OrdinalIgnoreCase)
                     && t.Contains("web player", StringComparison.OrdinalIgnoreCase);
        return t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildSearchQuery(string trackTitle, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(artist) &&
            !trackTitle.Contains(artist, StringComparison.OrdinalIgnoreCase))
            return trackTitle + " " + artist;
        return trackTitle;
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

    private static bool FocusViaTabSearch(IntPtr hwnd, string query, Func<string, bool> verify)
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
        {
            MainWindow.Log($"ts hwnd=0x{hwnd.ToInt64():X} failed to come foreground");
            return false;
        }
        Thread.Sleep(120);

        // Already on a tab that satisfies the verifier (e.g. the playing
        // tab is active but its title differs from the track): nothing to search.
        if (verify(GetTitle(hwnd)))
        {
            MainWindow.Log($"ts hwnd=0x{hwnd.ToInt64():X} pre-match query='{query}' title='{GetTitle(hwnd)}'");
            return true;
        }

        // Ctrl+Shift+A → tab search popup (Chrome / Edge / Brave).
        SendVk(0x11, true);   // Ctrl
        SendVk(0x10, true);   // Shift
        SendVk(0x41, true);   // A
        SendVk(0x41, false);
        SendVk(0x10, false);
        SendVk(0x11, false);
        Thread.Sleep(220);

        // Type the query into the auto-focused search box.
        var typed = 0;
        foreach (var ch in query)
        {
            if (typed >= 60) break;
            if (char.IsControl(ch)) continue;
            SendUnicode(ch);
            typed++;
        }
        if (typed == 0)
        {
            MainWindow.Log($"ts hwnd=0x{hwnd.ToInt64():X} empty query '{query}'");
            return false;
        }
        Thread.Sleep(180);

        // Enter → activate the top match, then verify the window actually
        // switched to the expected tab before claiming success.
        SendVk(0x0D, true);
        SendVk(0x0D, false);
        for (var waited = 0; waited < 700; waited += 25)
        {
            Thread.Sleep(25);
            if (verify(GetTitle(hwnd)))
            {
                MainWindow.Log($"ts hwnd=0x{hwnd.ToInt64():X} ok query='{query}' title='{GetTitle(hwnd)}'");
                return true;
            }
        }

        // No match in this window: dismiss the popup and let the caller
        // try the next window.
        MainWindow.Log($"ts hwnd=0x{hwnd.ToInt64():X} no match query='{query}' afterTitle='{GetTitle(hwnd)}'");
        SendVk(0x1B, true);
        SendVk(0x1B, false);
        Thread.Sleep(120);
        return false;
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
