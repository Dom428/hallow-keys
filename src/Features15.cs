// Hallow Keys v1.5 - "Autofill everywhere", the parts without a window:
//  - card and address helpers for the browser (expiry month/year, the card's last 4 digits, first/last name, address lines)
//  - desktop app autofill: a shortcut (off, and no combination, until you pick one) types username, Tab, password into the
//    program in front - only when that program's exe name is stored under "Apps" on a login. It refuses when Keys is locked,
//    the program isn't on a login, it runs as administrator and Keys doesn't, it's full screen, or a game is running (Hallow
//    Overlay / Hub status). Typing uses SendInput with KEYEVENTF_UNICODE (never the clipboard) and stops if the window in
//    front changes. The typing and the "what's in front" parts are interfaces, so the tests use stand-ins and never press keys.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    // ---- cards and identities for the browser
    public static class FormData
    {
        public static string Digits(string s) { return new string((s ?? "").Where(char.IsDigit).ToArray()); }
        public static string Last4(string number) { var d = Digits(number); return d.Length >= 4 ? d.Substring(d.Length - 4) : d; }
        public static string Brand(string number)
        {
            var d = Digits(number); if (d.Length < 2) return "Card";
            if (d[0] == '4') return "Visa";
            int two = int.Parse(d.Substring(0, 2)); int four = d.Length >= 4 ? int.Parse(d.Substring(0, 4)) : 0;
            if ((two >= 51 && two <= 55) || (four >= 2221 && four <= 2720)) return "Mastercard";
            if (two == 34 || two == 37) return "Amex";
            if (d.StartsWith("6011") || d.StartsWith("65")) return "Discover";
            return "Card";
        }
        // "07/27", "7/2027", "07-27", "0727", "2027-07" -> month "07", year "2027"; false if it can't be read
        public static bool ParseExpiry(string s, out string month, out string year)
        {
            month = year = null; s = (s ?? "").Trim(); if (s.Length == 0) return false;
            var parts = s.Split(new[] { '/', '-', '.', ' ', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(Digits).Where(p => p.Length > 0).ToList();
            if (parts.Count == 1) { var d = parts[0]; if (d.Length == 4) parts = new List<string> { d.Substring(0, 2), d.Substring(2) }; else if (d.Length == 6) parts = new List<string> { d.Substring(0, 2), d.Substring(2) }; else return false; }
            if (parts.Count != 2) return false;
            string m = parts[0], y = parts[1];
            if (m.Length == 4 && y.Length <= 2) { var t = m; m = y; y = t; }   // 2027-07
            int mi, yi; if (!int.TryParse(m, out mi) || !int.TryParse(y, out yi) || mi < 1 || mi > 12) return false;
            if (y.Length <= 2) yi += 2000; else if (y.Length != 4) return false;
            month = mi.ToString("00"); year = yi.ToString(); return true;
        }
        // "Ada Lovelace King" -> given "Ada Lovelace", family "King"
        public static void SplitName(string full, out string given, out string family)
        {
            var w = (full ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length <= 1) { given = w.Length == 1 ? w[0] : ""; family = ""; return; }
            given = string.Join(" ", w.Take(w.Length - 1)); family = w[w.Length - 1];
        }
        // the first line of the address, and the rest (comma or new line)
        public static void SplitAddress(string address, out string line1, out string line2)
        {
            var lines = (address ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count <= 1) { line1 = lines.Count == 1 ? lines[0] : ""; line2 = ""; return; }
            line1 = lines[0]; line2 = string.Join(", ", lines.Skip(1));
        }
        public static bool IsHttps(string url) { var u = Domain.Parse(url); return u != null && u.Scheme == "https"; }
    }

    // ---- desktop app autofill
    public class ForegroundInfo
    {
        public IntPtr Hwnd; public int Pid; public string Exe = "";   // exe file name, lower case ("steam.exe")
        public bool Elevated, Fullscreen, Ok;
        public override string ToString() { return Exe + (Elevated ? " (admin)" : "") + (Fullscreen ? " (full screen)" : ""); }
    }
    public interface IForeground { ForegroundInfo Get(); bool Activate(IntPtr hwnd); }
    public interface IKeyTyper { bool Type(IntPtr expected, string text); bool Tab(IntPtr expected); }
    public interface IModifierState { bool AnyDown(); }
    public class GameState { public bool Running; public string Name; }

    public enum FillRefusal { None, NotForeground, Self, Fullscreen, Game, Locked, Elevated, NoMatch, ModifiersHeld, FocusChanged, TypingBlocked, Empty }
    public class FillDecision
    {
        public FillRefusal Refusal; public List<Item> Matches = new List<Item>(); public ForegroundInfo Target;
        public string Message;
    }

    public static class DesktopFill
    {
        public static readonly string[] Suggested = { "steam.exe", "battle.net.exe", "riotclientservices.exe", "epicgameslauncher.exe", "eadesktop.exe", "ubisoftconnect.exe" };
        public static readonly Dictionary<string, string> SuggestedNames = new Dictionary<string, string> {
            { "steam.exe", "Steam" }, { "battle.net.exe", "Battle.net" }, { "riotclientservices.exe", "Riot Client" }, { "epicgameslauncher.exe", "Epic Games" }, { "eadesktop.exe", "EA app" }, { "ubisoftconnect.exe", "Ubisoft Connect" } };

        // "Steam", "C:\...\Steam.exe", " steam.EXE " -> "steam.exe"; null if it isn't a usable program name
        public static string NormalizeExe(string s)
        {
            s = (s ?? "").Trim().Trim('"'); if (s.Length == 0) return null;
            try { s = Path.GetFileName(s); } catch { return null; }
            s = s.ToLowerInvariant(); if (s.Length == 0 || s.Length > 120 || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (!s.EndsWith(".exe")) s += ".exe";
            return s == ".exe" ? null : s;
        }
        public static List<string> ParseApps(string text)
        {
            var o = new List<string>();
            foreach (var p in (text ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { var n = NormalizeExe(p); if (n != null && !o.Contains(n)) o.Add(n); }
            return o;
        }
        public static string Describe(string exe) { string n; return SuggestedNames.TryGetValue(exe ?? "", out n) ? n + " (" + exe + ")" : exe; }

        // which logins may be typed into the program in front - or why not. data == null means Keys is locked.
        // The order matters: a full-screen program or a running game is refused before anything (Keys never pops up over a game).
        public static FillDecision Decide(VaultData data, ForegroundInfo fg, bool selfElevated, GameState game, int selfPid)
        {
            var d = new FillDecision { Target = fg };
            if (fg == null || !fg.Ok || fg.Hwnd == IntPtr.Zero || string.IsNullOrEmpty(fg.Exe)) return Refuse(d, FillRefusal.NotForeground, "Hallow Keys couldn't tell which program is in front.");
            if (fg.Pid == selfPid) return Refuse(d, FillRefusal.Self, "Click into the program you want to sign in to first, then press the shortcut.");
            if (fg.Fullscreen) return Refuse(d, FillRefusal.Fullscreen, "The program in front is full screen - Hallow Keys never types into games.");
            if (game != null && game.Running && !Suggested.Contains(fg.Exe)) return Refuse(d, FillRefusal.Game, "A game is running" + (string.IsNullOrEmpty(game.Name) ? "" : " (" + game.Name + ")") + " - Hallow Keys only types into game launchers while you play.");
            if (data == null) return Refuse(d, FillRefusal.Locked, "Hallow Keys is locked. Unlock it, then press the shortcut again.");
            if (fg.Elevated && !selfElevated) return Refuse(d, FillRefusal.Elevated, Describe(fg.Exe) + " runs as administrator and Hallow Keys doesn't, so Windows won't let it type there.");
            d.Matches = data.Items.Where(i => i.Type == "login" && i.Apps.Contains(fg.Exe) && (i.Password.Length > 0 || i.Username.Length > 0))
                .OrderByDescending(i => i.Favorite).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (d.Matches.Count == 0) return Refuse(d, FillRefusal.NoMatch, "No login has " + Describe(fg.Exe) + " under Apps. Add it to a login in Hallow Keys (Edit > Apps).");
            d.Refusal = FillRefusal.None; return d;
        }
        static FillDecision Refuse(FillDecision d, FillRefusal r, string msg) { d.Refusal = r; d.Message = msg; d.Matches = new List<Item>(); return d; }

        // types username, Tab, password into the target - after the shortcut's keys are let go, and only while the same window is in front
        public static FillRefusal TypeLogin(Item it, ForegroundInfo target, bool selfElevated, IForeground fgApi, IKeyTyper typer, IModifierState mods, int waitMs = 1500)
        {
            if (it.Username.Length == 0 && it.Password.Length == 0) return FillRefusal.Empty;
            var t0 = Environment.TickCount;
            while (mods.AnyDown()) { if (Environment.TickCount - t0 > waitMs) return FillRefusal.ModifiersHeld; Thread.Sleep(20); }
            var now = fgApi.Get();
            if (now == null || !now.Ok || now.Hwnd != target.Hwnd || now.Pid != target.Pid || now.Exe != target.Exe || now.Fullscreen || (now.Elevated && !selfElevated) || !it.Apps.Contains(now.Exe)) return FillRefusal.FocusChanged;
            if (it.Username.Length > 0)
            {
                if (!typer.Type(target.Hwnd, it.Username)) return FillRefusal.TypingBlocked;
                if (it.Password.Length == 0) return FillRefusal.None;
                if (!typer.Tab(target.Hwnd)) return FillRefusal.TypingBlocked;
            }
            return typer.Type(target.Hwnd, it.Password) ? FillRefusal.None : FillRefusal.TypingBlocked;
        }
        public static string Say(FillRefusal r)
        {
            switch (r)
            {
                case FillRefusal.ModifiersHeld: return "Let go of the shortcut keys - nothing was typed.";
                case FillRefusal.FocusChanged: return "The window in front changed, so nothing was typed.";
                case FillRefusal.TypingBlocked: return "Windows didn't let Hallow Keys type there (it stopped as soon as it noticed).";
                case FillRefusal.Empty: return "That login has no username or password.";
                default: return "";
            }
        }

        // ---- is a game running? Hallow Overlay's live stats (fresh) or Hallow Hub's "playing" list. Missing files = no.
        public static GameState ReadGameState(string appData, DateTime now)
        {
            var g = new GameState();
            try
            {
                var js = new JavaScriptSerializer();
                var ov = Path.Combine(appData, "HallowOverlay", "status.json");
                if (File.Exists(ov))
                {
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ov)); object live;
                    if (d != null && d.TryGetValue("live", out live) && live is Dictionary<string, object>)
                    {
                        var l = (Dictionary<string, object>)live; object name, upd; DateTime u;
                        l.TryGetValue("game", out name); l.TryGetValue("updated", out upd);
                        if (name is string && ((string)name).Length > 0 && upd is string && DateTime.TryParse((string)upd, out u) && Math.Abs((now - u).TotalSeconds) < 30) { g.Running = true; g.Name = (string)name; }
                    }
                }
                var hub = Path.Combine(appData, "HallowHub", "status.json");
                if (!g.Running && File.Exists(hub))
                {
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(hub)); object pl, upd, run; DateTime u;
                    if (d != null && d.TryGetValue("playing", out pl) && pl is System.Collections.ArrayList && ((System.Collections.ArrayList)pl).Count > 0
                        && d.TryGetValue("updated", out upd) && upd is string && DateTime.TryParse((string)upd, out u) && (now - u).TotalMinutes < 10
                        && (!d.TryGetValue("running", out run) || !(run is bool) || (bool)run))
                    {
                        g.Running = true; var first = ((System.Collections.ArrayList)pl)[0];
                        g.Name = first is string ? (string)first : first is Dictionary<string, object> && ((Dictionary<string, object>)first).ContainsKey("name") ? Convert.ToString(((Dictionary<string, object>)first)["name"]) : null;
                    }
                }
            }
            catch { }
            return g;
        }
    }

    // ---- the real Windows parts
    public class WinForeground : IForeground
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr t, int cls, out int info, int len, out int ret);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 8; const int TokenElevation = 20, GWL_STYLE = -16, WS_CAPTION = 0xC00000;

        public ForegroundInfo Get()
        {
            var f = new ForegroundInfo(); var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == GetShellWindow() || h == GetDesktopWindow()) return f;
            int pid; GetWindowThreadProcessId(h, out pid); if (pid == 0) return f;
            f.Hwnd = h; f.Pid = pid;
            var p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (p == IntPtr.Zero) { f.Elevated = true; f.Ok = false; return f; }   // can't even look at it: treat as off-limits
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                if (QueryFullProcessImageName(p, 0, sb, ref n)) f.Exe = Path.GetFileName(sb.ToString()).ToLowerInvariant();
                f.Elevated = IsElevated(p, true);
            }
            finally { CloseHandle(p); }
            f.Fullscreen = IsFullscreen(h);
            f.Ok = f.Exe.Length > 0; return f;
        }
        public static bool IsElevated(IntPtr process, bool unknownMeans)
        {
            IntPtr t; if (!OpenProcessToken(process, TOKEN_QUERY, out t)) return unknownMeans;
            try { int e, r; return GetTokenInformation(t, TokenElevation, out e, 4, out r) ? e != 0 : unknownMeans; } finally { CloseHandle(t); }
        }
        public static bool SelfElevated() { using (var p = Process.GetCurrentProcess()) return IsElevated(p.Handle, false); }
        // covers its whole monitor and has no title bar = full screen (borderless or exclusive), the way games run
        static bool IsFullscreen(IntPtr h)
        {
            RECT r; if (!GetWindowRect(h, out r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return false;
            bool covers = r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
            return covers && (GetWindowLong(h, GWL_STYLE) & WS_CAPTION) != WS_CAPTION;
        }
        public bool Activate(IntPtr hwnd) { return SetForegroundWindow(hwnd); }
    }

    public class WinModifiers : IModifierState
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        static readonly int[] Keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C };   // Shift, Ctrl, Alt, left / right Win
        public bool AnyDown() { return Keys.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0); }
    }

    // SendInput with KEYEVENTF_UNICODE: each character goes to the window in front as itself (no keyboard layout, no clipboard).
    // It checks the window in front before every character and stops if it changed.
    public class SendInputTyper : IKeyTyper
    {
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }   // pad: the union is as big as MOUSEINPUT
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4; public const ushort VK_TAB = 9;
        public static int InputSize { get { return Marshal.SizeOf(typeof(INPUT)); } }
        // the key-down / key-up pair for one UTF-16 unit, or for Tab
        public static INPUT[] ForChar(char c) { return new[] { Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) }; }
        public static INPUT[] ForTab() { return new[] { Key(VK_TAB, 0, 0), Key(VK_TAB, 0, KEYEVENTF_KEYUP) }; }
        static INPUT Key(ushort vk, ushort scan, uint flags) { return new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }; }
        public bool Type(IntPtr expected, string text)
        {
            foreach (var c in text) { if (!Send(expected, ForChar(c))) return false; }
            return true;
        }
        public bool Tab(IntPtr expected) { return Send(expected, ForTab()); }
        static bool Send(IntPtr expected, INPUT[] i)
        {
            if (GetForegroundWindow() != expected) return false;
            var n = SendInput((uint)i.Length, i, InputSize); Array.Clear(i, 0, i.Length);
            Thread.Sleep(4);
            return n == i.Length;
        }
    }

    // the programs with a window right now (for the Apps picker): exe names only
    public static class RunningApps
    {
        public static List<string> List()
        {
            var o = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && p.Id != Process.GetCurrentProcess().Id) o.Add(p.ProcessName.ToLowerInvariant() + ".exe"); } catch { }
                finally { p.Dispose(); }
            }
            return o.ToList();
        }
    }
}
