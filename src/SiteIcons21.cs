// Hallow Keys 2.1 - site icons (off unless you turn them on in Settings).
// Each login's icon is fetched straight from that login's own site - never from a third-party favicon service:
//   - only for https addresses with a real public host name: never for http, IP addresses, localhost, single-label names, .local / .lan /
//     .internal / .home.arpa and other private names, and never when the name resolves to a private, loopback or link-local address
//   - https://<host>/ is read (at most 256 KB) for <link rel="icon"> / "shortcut icon" / "apple-touch-icon", then https://<host>/favicon.ico
//   - no cookies, no stored credentials, no Referer, no cache on disk, a generic User-Agent, 6 s timeouts, at most 3 redirects and only
//     to https on the same registrable domain (www.example.com -> example.com is fine, example.com -> cdn.other.net is not)
//   - images: PNG, ICO, GIF or JPEG only (checked by their first bytes, whatever the server says), at most 100 KB and 512x512 pixels,
//     redrawn here as a 32x32 PNG. SVG and anything else is ignored.
// Icons are kept in icons.hkcache next to the vault, encrypted (AES-256-GCM) with a key derived from the vault's data key; site names
// inside it are HMACs, so neither the sites nor the pictures can be read without unlocking the vault. Icons are refetched after 30 days.
// Not synced: each PC fetches its own. Turning the setting off deletes the cache.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowKeys
{
    public static class SiteIcons
    {
        public static int MaxPageBytes = 256 * 1024, MaxIconBytes = 100 * 1024, MaxPixels = 512, TimeoutMs = 6000, MaxRedirects = 3, Size = 32;
        public static TimeSpan RefreshAfter = TimeSpan.FromDays(30);
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
        static readonly string[] PrivateSuffixes = { ".local", ".localhost", ".lan", ".home", ".internal", ".intranet", ".corp", ".home.arpa", ".localdomain", ".onion", ".invalid", ".test", ".example", ".arpa" };

        // developer tests only (with HALLOW_KEYS_TESTDIR): HALLOW_KEYS_TEST_ICONS = "127.0.0.1:port|sha1-thumbprint" sends *.test names to a
        // local HTTPS stand-in whose certificate has that thumbprint - nothing else changes (same rules, same limits)
        static string testTarget, testThumb; static bool testRead;
        static bool TestMode(string host)
        {
            if (!testRead)
            {
                testRead = true; var e = KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_ICONS") : null;
                if (!string.IsNullOrEmpty(e) && e.Contains("|")) { testTarget = e.Split('|')[0]; testThumb = e.Split('|')[1].Replace(":", "").ToUpperInvariant(); }
            }
            return testTarget != null && host != null && host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
        }

        // why a host name is never fetched (null = it may be)
        public static string Refuse(string host)
        {
            if (string.IsNullOrEmpty(host)) return "no site";
            host = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (host.Length > 253 || host.Length == 0) return "not a site name";
            IPAddress ip; if (IPAddress.TryParse(host.Trim('[', ']'), out ip)) return "an IP address";
            if (!host.Contains('.')) return "a local name";
            if (host == "localhost") return "a local name";
            if (TestMode(host)) return null;
            foreach (var s in PrivateSuffixes) if (host.EndsWith(s, StringComparison.Ordinal)) return "a private or local name";
            if (!Regex.IsMatch(host, @"^[a-z0-9]([a-z0-9-]{0,62})(\.[a-z0-9]([a-z0-9-]{0,62}))+$")) return "not a site name";
            return null;
        }
        // a public internet address (not loopback, private, link-local, carrier-grade NAT, multicast, unspecified...)
        public static bool PublicAddress(IPAddress a)
        {
            if (a == null) return false;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a)) return false;
            var b = a.GetAddressBytes();
            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                if (b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224) return false;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
                if (b[0] == 169 && b[1] == 254) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)) return false;
                if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;
                if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false;
                if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
                return true;
            }
            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast || a.Equals(IPAddress.IPv6Any) || a.Equals(IPAddress.IPv6None)) return false;
                if ((b[0] & 0xFE) == 0xFC) return false;                  // unique local fc00::/7
                if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;   // documentation
                if (b[0] == 0x00) return false;                           // ::/8 (unspecified, IPv4-compatible...)
                return true;
            }
            return false;
        }
        // the site a login's icon comes from: its first https address with a host that may be fetched (http addresses never)
        public static string HostFor(Item it)
        {
            if (it == null || it.Type != "login") return null;
            foreach (var u in it.Urls)
            {
                var p = Domain.Parse(u); if (p == null || p.Scheme != "https" || !p.IsDefaultPort && !TestMode(p.Host)) continue;
                var h = Domain.Ascii(p.Host); if (h != null && Refuse(h) == null) return h;
            }
            return null;
        }
        static bool SameSite(string a, string b) { return a != null && b != null && string.Equals(Domain.Registrable(a), Domain.Registrable(b), StringComparison.OrdinalIgnoreCase); }
        // icon links of a page: https, same registrable domain, in a sensible order (rel=icon first, apple-touch-icon last), at most 4
        public static List<Uri> IconLinks(string html, Uri page)
        {
            var found = new List<Tuple<int, Uri>>();
            if (string.IsNullOrEmpty(html) || page == null) return new List<Uri>();
            foreach (Match m in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = m.Value;
                var rel = Attr(tag, "rel"); var href = Attr(tag, "href");
                if (rel == null || href == null) continue;
                rel = rel.ToLowerInvariant(); var rels = rel.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (!rels.Contains("icon") && !rels.Contains("apple-touch-icon") && !rels.Contains("apple-touch-icon-precomposed")) continue;
                href = WebUtility.HtmlDecode(href).Trim();
                if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || href.Length > 2000) continue;
                Uri u; if (!Uri.TryCreate(page, href, out u) || u.Scheme != "https" || !string.IsNullOrEmpty(u.UserInfo)) continue;
                if (Refuse(u.Host) != null || !SameSite(u.Host, page.Host)) continue;
                var type = (Attr(tag, "type") ?? "").ToLowerInvariant();
                if (type.Contains("svg") || u.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                int rank = rels.Contains("icon") ? (type.Contains("png") ? 0 : 1) : 2;
                var sizes = (Attr(tag, "sizes") ?? "").ToLowerInvariant();
                if (sizes.Contains("32x32")) rank -= 1;
                if (!found.Any(f => f.Item2 == u)) found.Add(Tuple.Create(rank, u));
            }
            return found.OrderBy(f => f.Item1).Select(f => f.Item2).Take(4).ToList();
        }
        static string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, @"\s" + name + @"\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
        }

        // ---- fetching (call on a background STA thread)
        public class Got { public byte[] Body; public string Type = ""; public Uri Final; }
        // one GET with our own redirect handling; null and a reason when refused or failed
        public static Got Get(Uri url, string accept, int maxBytes, string origin, out string why)
        {
            why = null;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                if (url.Scheme != "https") { why = "not https"; return null; }
                var r = Refuse(url.Host); if (r != null) { why = r; return null; }
                if (testTarget != null && !TestMode(url.Host)) { why = "a real site (tests only ask the local stand-in)"; return null; }
                if (!SameSite(url.Host, origin)) { why = "another site"; return null; }
                if (!TestMode(url.Host) && !url.IsDefaultPort) { why = "an unusual port"; return null; }
                if (!TestMode(url.Host))
                {
                    IPAddress[] addrs;
                    try { addrs = Dns.GetHostAddresses(url.Host); } catch { why = "the name doesn't resolve"; return null; }
                    if (addrs.Length == 0 || addrs.Any(a => !PublicAddress(a))) { why = "the name points to a private or local address"; return null; }
                }
                HttpWebResponse resp = null;
                try
                {
                    var target = url;
                    if (TestMode(url.Host)) target = new Uri("https://" + testTarget + url.PathAndQuery);
                    var req = (HttpWebRequest)WebRequest.Create(target);
                    if (TestMode(url.Host)) { req.Host = url.Host; req.ServerCertificateValidationCallback = (s, cert, ch, err) => cert != null && string.Equals(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert).Thumbprint, testThumb, StringComparison.OrdinalIgnoreCase); }
                    req.Method = "GET"; req.AllowAutoRedirect = false; req.CookieContainer = null; req.UseDefaultCredentials = false; req.Credentials = null; req.PreAuthenticate = false;
                    req.UserAgent = UserAgent; req.Accept = accept; req.Timeout = TimeoutMs; req.ReadWriteTimeout = TimeoutMs; req.KeepAlive = false;
                    req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore); req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.MaximumResponseHeadersLength = 32;   // KB
                    try { resp = (HttpWebResponse)req.GetResponse(); }
                    catch (WebException we) { resp = we.Response as HttpWebResponse; if (resp == null) { why = we.Status == WebExceptionStatus.Timeout ? "timed out" : "couldn't connect"; return null; } }
                    int code = (int)resp.StatusCode;
                    if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                    {
                        var loc = resp.Headers["Location"]; Uri next;
                        if (string.IsNullOrEmpty(loc) || !Uri.TryCreate(url, loc, out next)) { why = "a broken redirect"; return null; }
                        if (next.Scheme != "https") { why = "a redirect to http"; return null; }
                        if (!SameSite(next.Host, origin)) { why = "a redirect to another site"; return null; }
                        url = next; continue;
                    }
                    if (code != 200) { why = "HTTP " + code; return null; }
                    if (resp.ContentLength > maxBytes) { why = "too big"; return null; }
                    var ms = new MemoryStream(); var buf = new byte[8192]; int n;
                    using (var s = resp.GetResponseStream())
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > maxBytes) { why = "too big"; return null; } }
                    return new Got { Body = ms.ToArray(), Type = (resp.ContentType ?? "").ToLowerInvariant(), Final = url };
                }
                catch (Exception e) { why = e is WebException && ((WebException)e).Status == WebExceptionStatus.Timeout ? "timed out" : "failed"; return null; }
                finally { if (resp != null) try { resp.Close(); } catch { } }
            }
            why = "too many redirects"; return null;
        }
        public static string Sniff(byte[] b)
        {
            if (b == null || b.Length < 8) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
            if (b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return "ico";
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "gif";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";
            return null;
        }
        // an image file -> a 32x32 PNG (null if it isn't a small PNG/ICO/GIF/JPEG)
        public static byte[] ToPng(byte[] data, out string why)
        {
            why = null;
            if (data == null || data.Length > MaxIconBytes) { why = "too big"; return null; }
            if (Sniff(data) == null) { why = "not a PNG, ICO, GIF or JPEG"; return null; }
            try
            {
                var dec = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                BitmapSource best = null;
                foreach (var f in dec.Frames.Take(16))
                {
                    if (f.PixelWidth <= 0 || f.PixelHeight <= 0 || f.PixelWidth > MaxPixels || f.PixelHeight > MaxPixels) continue;
                    if (best == null || Score(f) < Score(best)) best = f;
                }
                if (best == null) { why = "too large or empty"; return null; }
                BitmapSource src = new FormatConvertedBitmap(best, PixelFormats.Pbgra32, null, 0);
                double sx = (double)Size / src.PixelWidth, sy = (double)Size / src.PixelHeight; double sc = Math.Min(sx, sy);
                var scaled = new TransformedBitmap(src, new ScaleTransform(sc, sc));
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) { RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality); dc.DrawImage(scaled, new System.Windows.Rect((Size - scaled.PixelWidth) / 2.0, (Size - scaled.PixelHeight) / 2.0, scaled.PixelWidth, scaled.PixelHeight)); }
                var rt = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32); rt.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rt));
                using (var o = new MemoryStream()) { enc.Save(o); return o.ToArray(); }
            }
            catch { why = "couldn't be read"; return null; }
        }
        static int Score(BitmapSource f) { int w = Math.Max(f.PixelWidth, f.PixelHeight); return w >= Size ? w - Size : (Size - w) * 4 + 1000; }

        // the whole thing for one site: its page's icon links, then /favicon.ico. null (and why) if there's none that passes
        public static byte[] Fetch(string host, out string why)
        {
            why = null; var r = Refuse(host); if (r != null) { why = r; return null; }
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; } catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            var t0 = Environment.TickCount; var root = new Uri("https://" + host + "/");
            var cands = new List<Uri>();
            string w; var page = Get(root, "text/html,application/xhtml+xml", MaxPageBytes, host, out w);
            if (page != null && (page.Type.Contains("html") || page.Type.Length == 0))
            {
                var html = Encoding.UTF8.GetString(page.Body);
                int head = html.IndexOf("</head", StringComparison.OrdinalIgnoreCase); if (head > 0) html = html.Substring(0, head);
                cands.AddRange(IconLinks(html, page.Final));
            }
            var fav = new Uri("https://" + host + "/favicon.ico"); if (!cands.Contains(fav)) cands.Add(fav);
            foreach (var c in cands)
            {
                if (Environment.TickCount - t0 > 20000) { why = "took too long"; return null; }
                var img = Get(c, "image/png,image/x-icon,image/vnd.microsoft.icon,image/gif,image/jpeg,image/*;q=0.8", MaxIconBytes, host, out w);
                if (img == null) { why = w; continue; }
                if (!(img.Type.StartsWith("image/") || img.Type.StartsWith("application/octet-stream") || img.Type.Length == 0) || img.Type.Contains("svg")) { why = "not an image"; continue; }
                var png = ToPng(img.Body, out w); if (png != null) return png;
                why = w;
            }
            return null;
        }

        // the fallback: a coloured letter tile, the colour always the same for a site
        public static readonly string[] TileColors = { "#C8414B", "#D9822B", "#C9A227", "#3FA34D", "#2E9E8F", "#2F80C8", "#5B5FC7", "#8E4EC6", "#C2408F", "#7A6A58" };
        public static string TileColor(string key)
        {
            using (var sha = SHA256.Create()) { var h = sha.ComputeHash(Encoding.UTF8.GetBytes((key ?? "").ToLowerInvariant())); return TileColors[h[0] % TileColors.Length]; }
        }
        public static string TileLetter(Item it)
        {
            var h = it.Host; var s = !string.IsNullOrEmpty(h) ? Domain.Registrable(h) ?? h : it.Title;
            if (s != null && s.StartsWith("www.")) s = s.Substring(4);
            var ch = (s ?? "").FirstOrDefault(char.IsLetterOrDigit);
            return ch == '\0' ? "?" : char.ToUpperInvariant(ch).ToString();
        }
    }

    // ---- the encrypted icon cache: "HKICONS1" | nonce(12) | ciphertext | tag(16); inside: entries of (HMAC of the host, fetched time, PNG)
    public class IconCache
    {
        public string Path;
        byte[] encKey, nameKey;
        public class Entry { public long Time; public byte[] Png; }
        readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>();
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKICONS1");
        public int Count { get { lock (map) return map.Count; } }
        public static IconCache Open(string path, byte[] dataKey)
        {
            var c = new IconCache { Path = path };
            using (var h = new HMACSHA256(dataKey)) { c.encKey = h.ComputeHash(Encoding.ASCII.GetBytes("HallowKeys icon cache v1")); c.nameKey = h.ComputeHash(Encoding.ASCII.GetBytes("HallowKeys icon names v1")); }
            try
            {
                if (File.Exists(path))
                {
                    var f = File.ReadAllBytes(path);
                    if (f.Length > Magic.Length + 28 && f.Take(8).SequenceEqual(Magic))
                    {
                        var sealedBox = f.Skip(8).ToArray(); var plain = Gcm.Open(c.encKey, sealedBox, Magic);
                        using (var br = new BinaryReader(new MemoryStream(plain)))
                        {
                            int n = br.ReadInt32(); if (n < 0 || n > 100000) throw new InvalidDataException();
                            for (int i = 0; i < n; i++) { var k = br.ReadString(); var t = br.ReadInt64(); int len = br.ReadInt32(); if (len < 0 || len > SiteIcons.MaxIconBytes * 2) throw new InvalidDataException(); var png = br.ReadBytes(len); c.map[k] = new Entry { Time = t, Png = len == 0 ? null : png }; }
                        }
                        Gcm.Wipe(plain);
                    }
                }
            }
            catch { lock (c.map) c.map.Clear(); }   // unreadable (another vault's, damaged): start again
            return c;
        }
        public string Name(string host) { if (closed) return ""; using (var h = new HMACSHA256(nameKey)) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes((host ?? "").ToLowerInvariant()))).Replace("-", "").ToLowerInvariant(); }
        // the cached icon (null if none or "this site has none"); fresh = fetched within 30 days
        public byte[] Get(string host, out bool known, out bool fresh)
        {
            Entry e; lock (map) map.TryGetValue(Name(host), out e);
            known = e != null; fresh = e != null && DateTime.UtcNow.Ticks - e.Time < SiteIcons.RefreshAfter.Ticks;
            return e == null ? null : e.Png;
        }
        public void Put(string host, byte[] png, DateTime utc) { lock (map) map[Name(host)] = new Entry { Time = utc.Ticks, Png = png }; }
        // keeps only these sites (logins deleted or changed since)
        public void Keep(IEnumerable<string> hosts) { var names = new HashSet<string>(hosts.Select(Name)); lock (map) foreach (var k in map.Keys.ToList()) if (!names.Contains(k)) map.Remove(k); }
        readonly object gate = new object(); bool closed;
        public void Save()
        {
            lock (gate) { if (closed) return; SaveLocked(); }
        }
        void SaveLocked()
        {
            byte[] plain;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                lock (map) { bw.Write(map.Count); foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal)) { bw.Write(kv.Key); bw.Write(kv.Value.Time); var p = kv.Value.Png ?? new byte[0]; bw.Write(p.Length); bw.Write(p); } }
                bw.Flush(); plain = ms.ToArray();
            }
            try { var box = Gcm.Seal(encKey, plain, Magic); Vault.WriteAtomic(Path, Magic.Concat(box).ToArray(), false); }
            finally { Gcm.Wipe(plain); }
        }
        public void Close() { lock (gate) { closed = true; Gcm.Wipe(encKey); Gcm.Wipe(nameKey); lock (map) map.Clear(); } }
        public static void Delete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
