// Hallow Keys 2.2 - "Every browser": which browsers are installed, where each one looks for the native messaging host, and whether
// Hallow Keys is connected there.
//   - Chrome, Edge, Brave, Chromium and Yandex read their own key under HKCU\Software\...\NativeMessagingHosts; Opera, Opera GX,
//     Vivaldi and Arc read Google Chrome's key (so they're covered by it - nothing extra is written for them); Firefox reads Mozilla's.
//   - Connect browsers writes Chrome's key always (it also serves Opera, Opera GX, Vivaldi, Arc and most other Chromium browsers), and
//     the other keys only for browsers that are installed - nothing is duplicated where a browser doesn't look.
//   - Installs are found by their usual program folders (per-user and per-machine), Windows' "Start menu internet" list and, for Arc
//     (a Store app), its package folder. Nothing is changed in any browser.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace HallowKeys
{
    public class BrowserInfo
    {
        public string Id, Name, RegKey, ReadsFrom, ExtPage, Package, Note = "";
        public string[] Exes = new string[0];      // with %LOCALAPPDATA% / %PROGRAMFILES% / %PROGRAMFILES(X86)%
        public string[] MenuNames = new string[0]; // their names in HKLM/HKCU\SOFTWARE\Clients\StartMenuInternet
        public bool Firefox;
        public string Key { get { return RegKey ?? Browsers.Get(ReadsFrom).RegKey; } }   // the registry key this browser actually reads
    }
    public class BrowserState { public BrowserInfo Info; public bool Installed, Connected, PointsHere; public string Where = ""; }

    public static class Browsers
    {
        public static readonly List<BrowserInfo> All = new List<BrowserInfo>
        {
            new BrowserInfo { Id = "chrome", Name = "Google Chrome", RegKey = @"Software\Google\Chrome", ExtPage = "chrome://extensions", MenuNames = new[] { "Google Chrome" },
                Exes = new[] { @"%PROGRAMFILES%\Google\Chrome\Application\chrome.exe", @"%PROGRAMFILES(X86)%\Google\Chrome\Application\chrome.exe", @"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe" } },
            new BrowserInfo { Id = "edge", Name = "Microsoft Edge", RegKey = @"Software\Microsoft\Edge", ExtPage = "edge://extensions", MenuNames = new[] { "Microsoft Edge" },
                Exes = new[] { @"%PROGRAMFILES(X86)%\Microsoft\Edge\Application\msedge.exe", @"%PROGRAMFILES%\Microsoft\Edge\Application\msedge.exe" } },
            new BrowserInfo { Id = "brave", Name = "Brave", RegKey = @"Software\BraveSoftware\Brave-Browser", ExtPage = "brave://extensions", MenuNames = new[] { "Brave" },
                Exes = new[] { @"%PROGRAMFILES%\BraveSoftware\Brave-Browser\Application\brave.exe", @"%PROGRAMFILES(X86)%\BraveSoftware\Brave-Browser\Application\brave.exe", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application\brave.exe" } },
            new BrowserInfo { Id = "opera", Name = "Opera", ReadsFrom = "chrome", ExtPage = "opera://extensions", MenuNames = new[] { "OperaStable", "Opera Stable" }, Note = "reads Chrome's registration",
                Exes = new[] { @"%LOCALAPPDATA%\Programs\Opera\opera.exe", @"%LOCALAPPDATA%\Programs\Opera\launcher.exe", @"%PROGRAMFILES%\Opera\opera.exe", @"%PROGRAMFILES%\Opera\launcher.exe" } },
            new BrowserInfo { Id = "operagx", Name = "Opera GX", ReadsFrom = "chrome", ExtPage = "opera://extensions", MenuNames = new[] { "Opera GXStable", "Opera GX Stable" }, Note = "reads Chrome's registration",
                Exes = new[] { @"%LOCALAPPDATA%\Programs\Opera GX\opera.exe", @"%LOCALAPPDATA%\Programs\Opera GX\launcher.exe", @"%PROGRAMFILES%\Opera GX\opera.exe" } },
            new BrowserInfo { Id = "vivaldi", Name = "Vivaldi", ReadsFrom = "chrome", ExtPage = "vivaldi://extensions", MenuNames = new[] { "Vivaldi" }, Note = "reads Chrome's registration",
                Exes = new[] { @"%LOCALAPPDATA%\Vivaldi\Application\vivaldi.exe", @"%PROGRAMFILES%\Vivaldi\Application\vivaldi.exe" } },
            new BrowserInfo { Id = "arc", Name = "Arc", ReadsFrom = "chrome", ExtPage = "arc://extensions", Package = "TheBrowserCompany.Arc", Note = "reads Chrome's registration" },
            new BrowserInfo { Id = "chromium", Name = "Chromium", RegKey = @"Software\Chromium", ExtPage = "chrome://extensions", MenuNames = new[] { "Chromium" },
                Exes = new[] { @"%LOCALAPPDATA%\Chromium\Application\chrome.exe", @"%PROGRAMFILES%\Chromium\Application\chrome.exe" } },
            new BrowserInfo { Id = "yandex", Name = "Yandex Browser", RegKey = @"Software\Yandex\YandexBrowser", ExtPage = "browser://extensions", MenuNames = new[] { "Yandex" },
                Exes = new[] { @"%LOCALAPPDATA%\Yandex\YandexBrowser\Application\browser.exe", @"%PROGRAMFILES%\Yandex\YandexBrowser\Application\browser.exe" } },
            new BrowserInfo { Id = "firefox", Name = "Firefox", RegKey = @"Software\Mozilla", ExtPage = "about:debugging", Firefox = true, MenuNames = new[] { "FIREFOX.EXE", "Firefox-308046B0AF4A39CB" },
                Exes = new[] { @"%PROGRAMFILES%\Mozilla Firefox\firefox.exe", @"%PROGRAMFILES(X86)%\Mozilla Firefox\firefox.exe", @"%LOCALAPPDATA%\Mozilla Firefox\firefox.exe" } },
        };
        public static BrowserInfo Get(string id) { return All.First(b => b.Id == id); }
        public static IEnumerable<string> OwnKeys { get { return All.Where(b => b.RegKey != null).Select(b => b.RegKey).Distinct(); } }

        // tests (HALLOW_KEYS_TESTDIR only): HALLOW_KEYS_TEST_BROWSERS = a folder standing in for LOCALAPPDATA / PROGRAMFILES / PROGRAMFILES(X86)
        // (sub-folders local, pf, pf86, packages), and no Start-menu list is read
        static string TestRoot { get { return KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_BROWSERS") : null; } }
        public static string Expand(string p)
        {
            var t = TestRoot;
            Func<string, string, string> sub = (name, real) => t != null ? Path.Combine(t, name) : real ?? "";
            return p.Replace("%LOCALAPPDATA%", sub("local", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))
                    .Replace("%PROGRAMFILES(X86)%", sub("pf86", Environment.GetEnvironmentVariable("ProgramFiles(x86)")))
                    .Replace("%PROGRAMFILES%", sub("pf", Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetEnvironmentVariable("ProgramFiles")));
        }
        static string PackagesDir { get { var t = TestRoot; return t != null ? Path.Combine(t, "packages") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages"); } }
        // Windows' list of browsers (name -> command), per machine and per user
        public static Dictionary<string, string> StartMenu()
        {
            var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TestRoot != null) return o;
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var path in new[] { @"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet" })
                    try
                    {
                        using (var k = hive.OpenSubKey(path)) if (k != null) foreach (var n in k.GetSubKeyNames())
                            using (var c = k.OpenSubKey(n + @"\shell\open\command")) { var v = c == null ? null : c.GetValue("") as string; if (!o.ContainsKey(n)) o[n] = (v ?? "").Trim().Trim('"'); }
                    }
                    catch { }
            return o;
        }
        public static string FindInstall(BrowserInfo b, Dictionary<string, string> menu)
        {
            foreach (var e in b.Exes) { var p = Expand(e); try { if (File.Exists(p)) return p; } catch { } }
            if (b.Package != null) try { var d = Directory.Exists(PackagesDir) ? Directory.GetDirectories(PackagesDir, b.Package + "_*").FirstOrDefault() : null; if (d != null) return "Store app (" + Path.GetFileName(d) + ")"; } catch { }
            foreach (var n in b.MenuNames) { string c; if (menu.TryGetValue(n, out c)) return c.Length > 0 ? c : n; }
            return null;
        }
        // browsers Windows lists that aren't in the table (other Chromium browsers usually read Chrome's registration)
        public static List<string> Others(Dictionary<string, string> menu)
        {
            var known = new HashSet<string>(All.SelectMany(b => b.MenuNames), StringComparer.OrdinalIgnoreCase); known.Add("IEXPLORE.EXE");
            return menu.Keys.Where(k => !known.Contains(k) && !k.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("Opera", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
        }
        public static List<BrowserState> States(string exePath)
        {
            var menu = StartMenu(); var o = new List<BrowserState>();
            foreach (var b in All)
            {
                var s = new BrowserState { Info = b }; var w = FindInstall(b, menu); s.Installed = w != null; s.Where = w ?? "";
                var manifest = b.Firefox ? NativeHost.FirefoxManifestPath : NativeHost.ManifestPath;
                s.Connected = NativeHost.KeyPointsTo(b.Key, manifest);
                s.PointsHere = s.Connected && NativeHost.ManifestExe(manifest) is string && string.Equals(NativeHost.ManifestExe(manifest), exePath, StringComparison.OrdinalIgnoreCase);
                o.Add(s);
            }
            return o;
        }
        // the keys Connect browsers writes: Chrome's always (it serves Opera, Opera GX, Vivaldi, Arc and others), each other browser's own key
        // only when that browser is installed, Firefox's when Firefox is installed - and keys from earlier versions (Edge, Brave) kept up to date
        public static List<string> KeysToWrite()
        {
            var menu = StartMenu(); var keys = new List<string> { Get("chrome").RegKey };
            foreach (var b in All.Where(x => x.RegKey != null && x.Id != "chrome" && !x.Firefox))
                if (FindInstall(b, menu) != null || NativeHost.KeyExists(b.RegKey)) keys.Add(b.RegKey);
            return keys.Distinct().ToList();
        }
        public static bool FirefoxWanted() { return FindInstall(Get("firefox"), StartMenu()) != null || NativeHost.KeyExists(Get("firefox").RegKey); }
    }
}
