// Hallow Keys v1.1 - browser autofill: the native messaging host and the app's side of it.
//  - The browser starts "HallowKeys.exe chrome-extension://<id>/" (that's how Chrome, Edge and Brave call a native host; "--native-host"
//    works too). The host answers only the pinned Hallow Keys extension, never reads the vault, and passes each message to the
//    running, unlocked app over its own pipe, with a per-session secret the app writes for this Windows user.
//  - The app answers: status, match (titles and usernames of logins for a page - no passwords), fill (one login, only if it
//    matches the page), totp (v1.3: just the 2FA code of one matching login), save-check / save (after the user confirms in the extension), open.
//  - v1.5: match with kind "card" / "identity" (titles, the card's last 4 digits, names - https pages only), card (one card's details,
//    the security code only when asked for), identity (one address), totp with "sameSiteAs" (the 2FA code right after a password fill,
//    only on the same site). Firefox starts the host as "HallowKeys.exe <manifest path> <add-on ID>" and only the pinned ID is answered.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowKeys
{
    public static class NativeHost
    {
        // the extension's fixed ID (from the "key" in extension\manifest.json)
        public const string ExtensionId = "nakjoajpklglfbaallnjofphmnhbedfo";
        public const string HostName = "com.hallow.keys";
        public static string Origin { get { return "chrome-extension://" + ExtensionId + "/"; } }
        public const string FirefoxId = "hallow-keys@dom428.github.io";   // v1.5: the Firefox add-on's ID (browser_specific_settings.gecko.id)
        public static string PipeName { get { return "HallowKeys.Native." + Environment.UserName + (KeysSettings.TestDir != null ? ".test" : ""); } }
        public static string SecretFile { get { return Path.Combine(KeysSettings.Dir, "session.key"); } }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        // ---- the host process (started by the browser): stdin/stdout, 4-byte length + UTF-8 JSON
        public static int Run(string[] args)
        {
            if (!Allowed(args)) return 1;   // only the pinned extension
            var stdin = Console.OpenStandardInput(); var stdout = Console.OpenStandardOutput();
            while (true)
            {
                var len = new byte[4]; if (ReadAll(stdin, len) < 4) return 0;
                int n = BitConverter.ToInt32(len, 0); if (n <= 0 || n > 1024 * 1024) return 1;
                var buf = new byte[n]; if (ReadAll(stdin, buf) < n) return 0;
                string reply;
                try { reply = Forward(Encoding.UTF8.GetString(buf)); } catch { reply = "{\"ok\":false,\"msg\":\"Hallow Keys didn't answer\"}"; }
                finally { Array.Clear(buf, 0, buf.Length); }
                var o = Encoding.UTF8.GetBytes(reply); stdout.Write(BitConverter.GetBytes(o.Length), 0, 4); stdout.Write(o, 0, o.Length); stdout.Flush(); Array.Clear(o, 0, o.Length);
            }
        }
        // Chrome / Edge / Brave pass "chrome-extension://<id>/"; Firefox passes the manifest path, then the add-on ID
        public static bool Allowed(string[] args)
        {
            var origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase));
            if (origin != null) return string.Equals(origin.TrimEnd('/') + "/", Origin, StringComparison.Ordinal);
            var other = args.Where(a => !a.StartsWith("--") && !a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
            return other.Count == 1 && string.Equals(other[0], FirefoxId, StringComparison.Ordinal);
        }
        static int ReadAll(Stream s, byte[] b) { int got = 0; while (got < b.Length) { int r = s.Read(b, got, b.Length - got); if (r <= 0) break; got += r; } return got; }
        static string Forward(string json)
        {
            string secret = null; try { secret = File.ReadAllText(SecretFile).Trim(); } catch { }
            var msg = js.Deserialize<Dictionary<string, object>>(json); object t; msg.TryGetValue("type", out t);
            if (secret == null || !PipeExists())
            {
                if (t as string == "open") { StartApp(); return "{\"ok\":true,\"running\":false,\"msg\":\"Starting Hallow Keys\"}"; }
                return "{\"ok\":false,\"running\":false,\"msg\":\"Hallow Keys isn't running\"}";
            }
            using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
            {
                c.Connect(2000);
                var line = Encoding.UTF8.GetBytes(secret + " " + json.Replace("\r", " ").Replace("\n", " ") + "\n"); c.Write(line, 0, line.Length); c.Flush(); Array.Clear(line, 0, line.Length);
                var r = new StreamReader(c, new UTF8Encoding(false)); var t2 = r.ReadLineAsync(); if (!t2.Wait(5000)) return "{\"ok\":false,\"msg\":\"Hallow Keys didn't answer\"}";
                return t2.Result ?? "{\"ok\":false}";
            }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        static bool PipeExists() { if (WaitNamedPipe(@"\\.\pipe\" + PipeName, 1)) return true; return System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 2; }
        static void StartApp() { try { System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { } }

        // ---- registering the host with the browsers (per user, HKCU; asked for in the app). HALLOW_KEYS_REGROOT (tests) uses another key.
        public static readonly string[][] Browsers = { new[] { "Chrome", @"Software\Google\Chrome" }, new[] { "Edge", @"Software\Microsoft\Edge" }, new[] { "Brave", @"Software\BraveSoftware\Brave-Browser" } };
        static string RegRoot(string b) { var t = KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_REGROOT") : null; return (t != null ? t + "\\" + b.Substring("Software\\".Length) : b) + @"\NativeMessagingHosts\" + HostName; }
        public static string ManifestPath { get { return Path.Combine(KeysSettings.Dir, "native-host.json"); } }
        public static void Register(string exePath)
        {
            Directory.CreateDirectory(KeysSettings.Dir);
            var m = js.Serialize(new Dictionary<string, object> { { "name", HostName }, { "description", "Hallow Keys" }, { "path", exePath }, { "type", "stdio" }, { "allowed_origins", new[] { Origin } } });
            File.WriteAllText(ManifestPath, m, new UTF8Encoding(false));
            foreach (var b in Browsers) using (var k = Registry.CurrentUser.CreateSubKey(RegRoot(b[1]))) k.SetValue("", ManifestPath);
            // v1.5 Firefox: its own manifest with allowed_extensions (add-on IDs instead of origins)
            var fm = js.Serialize(new Dictionary<string, object> { { "name", HostName }, { "description", "Hallow Keys" }, { "path", exePath }, { "type", "stdio" }, { "allowed_extensions", new[] { FirefoxId } } });
            File.WriteAllText(FirefoxManifestPath, fm, new UTF8Encoding(false));
            using (var k = Registry.CurrentUser.CreateSubKey(RegRoot(FirefoxRoot))) k.SetValue("", FirefoxManifestPath);
        }
        public static void Unregister()
        {
            foreach (var b in Browsers) try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot(b[1]), false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot(FirefoxRoot), false); } catch { }
            try { File.Delete(ManifestPath); } catch { }
            try { File.Delete(FirefoxManifestPath); } catch { }
        }
        public static bool Registered { get { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot(Browsers[0][1]))) return k != null && File.Exists(ManifestPath); } }
        public static bool FirefoxRegistered { get { using (var k = Registry.CurrentUser.OpenSubKey(RegRoot(FirefoxRoot))) return k != null && File.Exists(FirefoxManifestPath); } }
        public const string FirefoxRoot = @"Software\Mozilla";   // HKCU\Software\Mozilla\NativeMessagingHosts\com.hallow.keys
        public static string FirefoxManifestPath { get { return Path.Combine(KeysSettings.Dir, "native-host-firefox.json"); } }
    }

    // ---- the app's side: a pipe for the host only, checked with the session secret
    public class NativeServer
    {
        readonly Func<Func<string>, string> onUi; readonly Func<Vault> vault; readonly Action showApp; readonly Action<string> saved;
        byte[] secret; volatile bool stopped;
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public NativeServer(Func<Func<string>, string> onUi, Func<Vault> vault, Action showApp, Action<string> saved) { this.onUi = onUi; this.vault = vault; this.showApp = showApp; this.saved = saved; }

        public void Start()
        {
            secret = Rng.Bytes(32);
            try { Directory.CreateDirectory(KeysSettings.Dir); File.WriteAllText(NativeHost.SecretFile, BitConverter.ToString(secret).Replace("-", "")); } catch { }
            new Thread(Loop) { IsBackground = true, Name = "native host pipe" }.Start();
        }
        public void Stop() { stopped = true; try { File.Delete(NativeHost.SecretFile); } catch { } }
        void Loop()
        {
            int fails = 0;
            while (!stopped)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    var sec = new PipeSecurity();
                    sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    pipe = new NamedPipeServerStream(NativeHost.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, sec);
                    pipe.WaitForConnection(); fails = 0; var p = pipe; pipe = null;
                    new Thread(() => Handle(p)) { IsBackground = true }.Start();
                }
                catch { if (pipe != null) try { pipe.Dispose(); } catch { } Thread.Sleep(Math.Min(30000, 500 * ++fails)); }
            }
        }
        void Handle(NamedPipeServerStream p)
        {
            try
            {
                var r = new StreamReader(p, new UTF8Encoding(false)); var t = r.ReadLineAsync(); string reply;
                if (!t.Wait(3000) || t.Result == null) reply = "{\"ok\":false}";
                else
                {
                    var line = t.Result; int sp = line.IndexOf(' ');
                    var given = sp > 0 ? line.Substring(0, sp) : "";
                    if (!SameSecret(given)) reply = "{\"ok\":false,\"msg\":\"not allowed\"}";
                    else reply = onUi(() => Answer(line.Substring(sp + 1)));
                }
                var b = Encoding.UTF8.GetBytes(reply.Replace("\r", " ").Replace("\n", " ") + "\n"); p.Write(b, 0, b.Length); p.Flush(); Array.Clear(b, 0, b.Length);
                try { p.WaitForPipeDrain(); } catch { }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        bool SameSecret(string hex)
        {
            var mine = BitConverter.ToString(secret).Replace("-", "");
            if (hex == null || hex.Length != mine.Length) return false;
            int d = 0; for (int i = 0; i < mine.Length; i++) d |= char.ToUpperInvariant(hex[i]) ^ mine[i]; return d == 0;
        }
        static string S(Dictionary<string, object> m, string k) { object o; return m.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }
        static bool B(Dictionary<string, object> m, string k) { object o; return m.TryGetValue(k, out o) && o is bool && (bool)o; }
        string Json(object o) { return js.Serialize(o); }

        // runs on the window's thread
        public string Answer(string json)
        {
            Dictionary<string, object> m; try { m = js.Deserialize<Dictionary<string, object>>(json); } catch { return "{\"ok\":false}"; }
            var type = S(m, "type"); var v = vault();
            if (type == "open") { showApp(); return Json(new Dictionary<string, object> { { "ok", true }, { "running", true } }); }
            if (type == "status") return Json(new Dictionary<string, object> { { "ok", true }, { "running", true }, { "locked", v == null } });
            if (v == null) return Json(new Dictionary<string, object> { { "ok", false }, { "running", true }, { "locked", true }, { "msg", "Hallow Keys is locked" } });
            var url = S(m, "url");
            if (Domain.Parse(url) == null) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "Not a web page" } });
            var host = Domain.HostOf(url);
            Func<Item, bool> matches = i => i.Type == "login" && i.Urls.Any(u => Domain.Matches(u, url));
            var kind = S(m, "kind");
            // v1.5: cards and addresses aren't tied to a site, so they're offered on any https page - never on http
            if ((type == "match" && (kind == "card" || kind == "identity")) || type == "card" || type == "identity")
            {
                if (!FormData.IsHttps(url)) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "Cards and addresses are only filled on secure (https) pages" } });
                if (type == "match")
                {
                    var found = v.Data.Items.Where(i => i.Type == kind).OrderByDescending(i => i.Favorite).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).Take(30)
                        .Select(i => (object)(kind == "card"
                            ? new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "sub", FormData.Brand(i.CardNumber) + " •••• " + FormData.Last4(i.CardNumber) }, { "cvc", i.Cvc.Length > 0 } }
                            : new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "sub", string.Join(" - ", new[] { i.FullName, i.Email }.Where(x => x.Length > 0)) } })).ToList();
                    return Json(new Dictionary<string, object> { { "ok", true }, { "items", found } });
                }
                var it = v.Data.Items.FirstOrDefault(i => i.Id == S(m, "id") && i.Type == type);
                if (it == null) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "Not found" } });
                if (type == "card")
                {
                    string mon, yr; FormData.ParseExpiry(it.Expiry, out mon, out yr);
                    var o = new Dictionary<string, object> { { "ok", true }, { "holder", it.CardHolder }, { "number", FormData.Digits(it.CardNumber) }, { "expMonth", mon ?? "" }, { "expYear", yr ?? "" }, { "hasCvc", it.Cvc.Length > 0 } };
                    if (B(m, "cvc")) o["cvc"] = it.Cvc;   // the security code only when the extension asks for it (a second click, or its setting)
                    return Json(o);
                }
                string given, family, a1, a2; FormData.SplitName(it.FullName, out given, out family); FormData.SplitAddress(it.Address, out a1, out a2);
                return Json(new Dictionary<string, object> { { "ok", true }, { "name", it.FullName }, { "given", given }, { "family", family }, { "email", it.Email }, { "tel", it.Phone },
                    { "address", it.Address.Replace("\r", "") }, { "address1", a1 }, { "address2", a2 }, { "city", it.City }, { "postcode", it.PostCode }, { "country", it.Country } });
            }
            switch (type)
            {
                case "match":
                    var list = v.Data.Items.Where(matches).OrderByDescending(i => i.Urls.Any(u => Domain.HostOf(u) == host)).ThenBy(i => i.Title)
                        .Select(i => (object)new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "username", i.Username }, { "totp", Totp.Parse(i.Totp) != null } }).Take(30).ToList();
                    return Json(new Dictionary<string, object> { { "ok", true }, { "items", list } });
                case "fill":
                {
                    var it = v.Data.Items.FirstOrDefault(i => i.Id == S(m, "id"));
                    if (it == null || !matches(it)) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "That login isn't for this site" } });
                    var spec = Totp.Parse(it.Totp);
                    return Json(new Dictionary<string, object> { { "ok", true }, { "username", it.Username }, { "password", it.Password }, { "totp", spec == null ? null : Totp.Code(spec, Totp.Now()) } });
                }
                case "totp":   // v1.3: only the current 2FA code of one login, only if it matches the page
                {
                    var it = v.Data.Items.FirstOrDefault(i => i.Id == S(m, "id"));
                    if (it == null || !matches(it)) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "That login isn't for this site" } });
                    var spec = Totp.Parse(it.Totp);
                    if (spec == null) return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "That login has no 2FA code" } });
                    var prev = S(m, "sameSiteAs");   // v1.5: the code right after a password fill - only on the same site as that page
                    if (prev.Length > 0 && (Domain.Parse(prev) == null || Domain.Registrable(Domain.HostOf(prev)) != Domain.Registrable(host) || (Domain.Parse(prev).Scheme == "https" && Domain.Parse(url).Scheme != "https")))
                        return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "Not the same site" } });
                    var now = Totp.Now();
                    return Json(new Dictionary<string, object> { { "ok", true }, { "totp", Totp.Code(spec, now) }, { "left", spec.Period - now % spec.Period } });
                }
                case "save-check":
                case "save":
                {
                    var user = S(m, "username"); var pw = S(m, "password");
                    if (pw.Length == 0 || pw.Length > 1000 || user.Length > 500) return Json(new Dictionary<string, object> { { "ok", false } });
                    var same = v.Data.Items.FirstOrDefault(i => matches(i) && string.Equals(i.Username, user, StringComparison.OrdinalIgnoreCase));
                    var state = same == null ? "new" : same.Password == pw ? "same" : "update";
                    if (type == "save-check") return Json(new Dictionary<string, object> { { "ok", true }, { "state", state } });
                    if (state == "same") return Json(new Dictionary<string, object> { { "ok", true }, { "state", state } });
                    if (same != null) { Organize.RecordOldPassword(same, same.Password, DateTime.Now); same.Password = pw; same.PasswordChanged = same.Updated = DateTime.Now; }
                    else
                    {
                        var u = Domain.Parse(url);
                        v.Data.Items.Add(new Item { Type = "login", Title = host, Username = user, Password = pw, Urls = { u.Scheme + "://" + u.Authority } });
                    }
                    saved(state == "new" ? "Saved a new login for " + host + "." : "Updated the password for " + host + ".");
                    return Json(new Dictionary<string, object> { { "ok", true }, { "state", state } });
                }
            }
            return Json(new Dictionary<string, object> { { "ok", false }, { "msg", "Unknown request" } });
        }
    }
}
