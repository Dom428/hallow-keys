// Hallow Keys 2.12 - "Passkeys on Windows": the parts without a window (the page is in Features212UI.cs).
//  - Related origins (WebAuthn Level 3, "related origin requests"): a site may use one passkey on several of its own domains by
//    listing them at https://<rpId>/.well-known/webauthn ({"origins": [...]}). Microsoft does this: its passkeys belong to
//    login.microsoft.com, and login.live.com / account.live.com / login.microsoftonline.com ask for them. Chrome and Edge allow it
//    since version 128, so Keys' own rpId check (2.3: the page's host or a parent domain) refused Microsoft's sign-in. Now, when the
//    rpId isn't the page's domain, Keys fetches that file itself (https only, no redirects, 64 KB at most, 5 s, public addresses only,
//    no cookies) and allows the request only if the page's exact origin is listed, under the spec's five-label limit. The answer is
//    remembered for 10 minutes (a "no" for 1 minute). The fetch runs on the pipe's thread before the request reaches the window, and
//    the window only reads the remembered answer - the extension's message itself can never say "related".
//  - What Windows offers (read only): is the third-party passkey provider API (webauthn.dll's WebAuthNPluginAddAuthenticator) there,
//    and does this copy of Keys have a package identity (Windows only accepts providers from packaged, signed apps).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class RelatedOrigins
    {
        public const int MaxLabels = 5, MaxBytes = 64 * 1024, TimeoutMs = 5000;
        static readonly ConcurrentDictionary<string, DateTime> known = new ConcurrentDictionary<string, DateTime>();   // "rpId|origin" -> until (allowed)
        static readonly ConcurrentDictionary<string, DateTime> refused = new ConcurrentDictionary<string, DateTime>();
        public static Action<string> Log = s => { };
        public static int Fetches;   // for the tests

        // the rpId may be used through related origins at all? (a real site name, not an IP or a public suffix)
        public static bool RpIdShape(string rpId)
        {
            var rp = Domain.Ascii(rpId ?? ""); if (string.IsNullOrEmpty(rp) || rp.StartsWith(".") || rp.Contains("..")) return false;
            IPAddress ip; if (IPAddress.TryParse(rp.Trim('[', ']'), out ip)) return false;
            var reg = Domain.Registrable(rp); if (reg == null || !reg.Contains('.') || Domain.IsMultiSuffix(reg)) return false;   // never "com", "co.uk"
            return rp == reg || rp.EndsWith("." + reg);
        }
        // the spec's "label" of an origin: its registrable domain without the public suffix ("login.live.com" -> "live")
        public static string Label(string host)
        {
            var reg = Domain.Registrable(host); if (reg == null || !reg.Contains('.')) return null;
            IPAddress ip; if (IPAddress.TryParse(reg.Trim('[', ']'), out ip)) return null;
            return reg.Substring(0, reg.IndexOf('.'));
        }
        // WebAuthn L3 5.11.1: walk "origins", count distinct labels (at most 5), true once the caller's origin is met
        public static bool Listed(string json, string callerOrigin, out string why)
        {
            why = null;
            object[] origins;
            try
            {
                var o = new JavaScriptSerializer { MaxJsonLength = MaxBytes * 2 }.DeserializeObject(json) as Dictionary<string, object>;
                if (o == null || !o.ContainsKey("origins")) { why = "no \"origins\" list"; return false; }
                origins = o["origins"] as object[]; if (origins == null) { why = "\"origins\" isn't a list"; return false; }
            }
            catch { why = "not JSON"; return false; }
            var seen = new HashSet<string>();
            foreach (var x in origins)
            {
                var s = x as string; if (s == null) continue;
                var origin = Passkeys.OriginOf(s); if (origin == null) continue;   // https with a host name only
                var label = Label(Domain.HostOf(origin)); if (string.IsNullOrEmpty(label)) continue;
                if (seen.Count >= MaxLabels && !seen.Contains(label)) continue;
                if (origin == callerOrigin) return true;
                if (seen.Count < MaxLabels) seen.Add(label);
            }
            why = "this page's origin isn't in the site's list"; return false;
        }
        public static bool Allowed(string rpId, string origin)
        {
            DateTime until; return origin != null && known.TryGetValue(rpId + "|" + origin, out until) && until > DateTime.UtcNow;
        }
        public static void Forget() { known.Clear(); refused.Clear(); }

        // on the pipe thread, before the request reaches the window: a passkey request whose rpId isn't the page's domain -> look it up
        public static void Prepare(string json)
        {
            try
            {
                var m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); object t, u, r;
                if (!m.TryGetValue("type", out t) || !(t is string) || !((string)t).StartsWith("passkey-")) return;
                m.TryGetValue("url", out u); m.TryGetValue("rpId", out r);
                var url = u as string; var rpId = Passkeys.NormalRpId(r as string);
                if (url == null || Passkeys.RpIdProblem(rpId, url) == null) return;   // the normal rule already allows it
                var origin = Passkeys.OriginOf(url); if (origin == null || !RpIdShape(rpId)) return;
                string why; Check(rpId, origin, out why);
            }
            catch { }
        }
        public static bool Check(string rpId, string origin, out string why)
        {
            why = null; var key = rpId + "|" + origin; DateTime until;
            if (Allowed(rpId, origin)) return true;
            if (refused.TryGetValue(key, out until) && until > DateTime.UtcNow) { why = "refused a moment ago"; return false; }
            var body = Fetch(rpId, out why);
            var ok = body != null && Listed(body, origin, out why);
            if (ok) known[key] = DateTime.UtcNow.AddMinutes(10); else refused[key] = DateTime.UtcNow.AddMinutes(1);
            Log("passkey: related origins " + (ok ? "allowed" : "refused (" + why + ")"));   // never the site names
            return ok;
        }

        // developer tests only (with HALLOW_KEYS_TESTDIR): HALLOW_KEYS_TEST_WELLKNOWN = "127.0.0.1:port|sha1-thumbprint" sends *.test
        // names to a local HTTPS stand-in with that certificate - and then nothing else is ever fetched
        static string testTarget, testThumb; static bool testRead;
        static bool TestOn()
        {
            if (!testRead)
            {
                testRead = true; var e = KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_WELLKNOWN") : null;
                if (!string.IsNullOrEmpty(e) && e.Contains("|")) { testTarget = e.Split('|')[0]; testThumb = e.Split('|')[1].Replace(":", "").ToUpperInvariant(); }
            }
            return testTarget != null;
        }
        static string Fetch(string rpId, out string why)
        {
            why = null; Fetches++;
            var test = TestOn();
            if (test && !rpId.EndsWith(".test", StringComparison.Ordinal)) { why = "a real site (tests only ask the local stand-in)"; return null; }
            if (!test)
            {
                IPAddress[] addrs;
                try { addrs = Dns.GetHostAddresses(rpId); } catch { why = "the name doesn't resolve"; return null; }
                if (addrs.Length == 0 || addrs.Any(a => !SiteIcons.PublicAddress(a))) { why = "the name points to a private or local address"; return null; }
            }
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; } catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }   // TLS 1.2 / 1.3
            HttpWebResponse resp = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(test ? "https://" + testTarget + "/.well-known/webauthn" : "https://" + rpId + "/.well-known/webauthn");
                if (test) { req.Host = rpId; req.ServerCertificateValidationCallback = (s, cert, ch, err) => cert != null && string.Equals(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert).Thumbprint, testThumb, StringComparison.OrdinalIgnoreCase); }
                req.Method = "GET"; req.AllowAutoRedirect = false; req.CookieContainer = null; req.UseDefaultCredentials = false; req.Credentials = null; req.PreAuthenticate = false;
                req.UserAgent = SiteIcons.UserAgent; req.Accept = "application/json"; req.Timeout = TimeoutMs; req.ReadWriteTimeout = TimeoutMs; req.KeepAlive = false;
                req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore); req.MaximumResponseHeadersLength = 32;
                try { resp = (HttpWebResponse)req.GetResponse(); }
                catch (WebException we) { resp = we.Response as HttpWebResponse; if (resp == null) { why = we.Status == WebExceptionStatus.Timeout ? "timed out" : "couldn't connect"; return null; } }
                int code = (int)resp.StatusCode;
                if (code >= 300 && code < 400) { why = "a redirect (not allowed)"; return null; }
                if (code != 200) { why = "HTTP " + code; return null; }
                if (!(resp.ContentType ?? "").ToLowerInvariant().StartsWith("application/json")) { why = "not application/json"; return null; }
                if (resp.ContentLength > MaxBytes) { why = "too big"; return null; }
                var ms = new MemoryStream(); var buf = new byte[8192]; int n;
                using (var s = resp.GetResponseStream())
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxBytes) { why = "too big"; return null; } }
                return new UTF8Encoding(false, true).GetString(ms.ToArray());
            }
            catch (Exception e) { why = e is WebException && ((WebException)e).Status == WebExceptionStatus.Timeout ? "timed out" : "failed"; return null; }
            finally { if (resp != null) try { resp.Close(); } catch { } }
        }
    }

    // ---- what Windows offers for third-party passkey managers (read only - nothing is registered)
    public static class WindowsPasskeys
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr mod);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint ApiVersion();
        public const int NoPackage = 15700;   // APPMODEL_ERROR_NO_PACKAGE

        public class Info { public bool PluginApi, Experimental; public uint ApiVersion; public bool Packaged; public string Os; }
        public static Info Detect()
        {
            var i = new Info { Os = Environment.OSVersion.Version.ToString() };
            var h = LoadLibraryEx("webauthn.dll", IntPtr.Zero, 0x800);   // LOAD_LIBRARY_SEARCH_SYSTEM32
            if (h != IntPtr.Zero)
            {
                try
                {
                    i.PluginApi = GetProcAddress(h, "WebAuthNPluginAddAuthenticator") != IntPtr.Zero;
                    i.Experimental = GetProcAddress(h, "EXPERIMENTAL_WebAuthNPluginAddAuthenticator") != IntPtr.Zero;
                    var v = GetProcAddress(h, "WebAuthNGetApiVersionNumber");
                    if (v != IntPtr.Zero) i.ApiVersion = ((ApiVersion)Marshal.GetDelegateForFunctionPointer(v, typeof(ApiVersion)))();
                }
                finally { FreeLibrary(h); }
            }
            try { int len = 0; i.Packaged = GetCurrentPackageFullName(ref len, null) != NoPackage; } catch { i.Packaged = false; }
            return i;
        }
        public static string Summary(Info i)
        {
            if (i == null) return "";
            return i.PluginApi ? "Windows on this PC has the passkey manager plugin API (WebAuthn API version " + i.ApiVersion + ")." : "Windows on this PC doesn't have the passkey manager plugin API yet (WebAuthn API version " + i.ApiVersion + ").";
        }
    }
}
