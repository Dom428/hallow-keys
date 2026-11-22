// Hallow Keys v1.7 - "Watchtower-level health", the parts without a window:
//  - two public lists, downloaded whole (only when you click, or weekly if you turn that on) and kept in the data folder:
//    2fa.directory's list of sites that offer 2FA codes (TOTP), and Have I Been Pwned's list of breached sites. Nothing about your
//    logins is ever sent: no domain, no username, no hash - the lists are matched against your logins here, by registrable domain.
//  - the report: leaked, breached since the password was last changed, reused, weak, 2FA available but not set up, unsecured (http)
//    addresses, old passwords, expired / expiring items - one security score with its formula, and a "fix these first" list.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class Watchtower
    {
        // HALLOW_KEYS_2FA_API / HALLOW_KEYS_BREACH_API (developer tests only, in a test folder): local stand-ins for the two lists
        static string Env(string n) { return KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable(n) : null; }
        public static string TwoFaUrl { get { return Env("HALLOW_KEYS_2FA_API") ?? "https://api.2fa.directory/v3/totp.json"; } }
        public static string BreachUrl { get { return Env("HALLOW_KEYS_BREACH_API") ?? "https://haveibeenpwned.com/api/v3/breaches"; } }
        public static string TwoFaCache { get { return Path.Combine(KeysSettings.Dir, "2fa-directory.json"); } }
        public static string BreachCache { get { return Path.Combine(KeysSettings.Dir, "hibp-breaches.json"); } }
        public const int MaxBytes = 40 * 1024 * 1024;

        public class TwoFaSite { public string Name = "", Domain = "", Doc = ""; }
        public class Breach { public string Name = "", Title = "", Domain = ""; public DateTime BreachDate, AddedDate; public bool Passwords; }

        // ---- the lists
        static JavaScriptSerializer Js() { return new JavaScriptSerializer { MaxJsonLength = MaxBytes }; }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : ""; }
        // 2fa.directory v3 totp.json: [["Name", {"domain": "...", "additional-domains": [...], "documentation": "...", "tfa": [...]}], ...]
        public static Dictionary<string, TwoFaSite> ParseTwoFa(string json)
        {
            var o = new Dictionary<string, TwoFaSite>(StringComparer.OrdinalIgnoreCase);
            var list = Js().DeserializeObject(json) as object[];
            if (list == null) throw new FormatException("not a list");
            foreach (var e in list.OfType<object[]>())
            {
                if (e.Length < 2) continue; var d = e[1] as Dictionary<string, object>; if (d == null) continue;
                var site = new TwoFaSite { Name = Convert.ToString(e[0], CultureInfo.InvariantCulture), Domain = S(d, "domain"), Doc = S(d, "documentation") };
                if (site.Doc.Length == 0) site.Doc = S(d, "url");
                object tfa; if (d.TryGetValue("tfa", out tfa) && tfa is object[] && !((object[])tfa).Any(x => "totp".Equals(x))) continue;   // only sites with authenticator codes
                var domains = new List<string> { site.Domain }; object add;
                if (d.TryGetValue("additional-domains", out add) && add is object[]) domains.AddRange(((object[])add).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)));
                foreach (var dom in domains) { var r = Domain.Registrable(dom); if (r != null && r.Contains('.') && !o.ContainsKey(r)) o[r] = site; }
            }
            return o;
        }
        // Have I Been Pwned's breach list: [{"Name", "Title", "Domain", "BreachDate": "2015-03-01", "AddedDate": "...Z", "DataClasses": [...],
        // "IsFabricated", "IsSpamList", ...}]. Made-up ("fabricated") breaches, spam lists and ones without a site are left out.
        public static Dictionary<string, List<Breach>> ParseBreaches(string json)
        {
            var o = new Dictionary<string, List<Breach>>(StringComparer.OrdinalIgnoreCase);
            var list = Js().DeserializeObject(json) as object[];
            if (list == null) throw new FormatException("not a list");
            foreach (var d in list.OfType<Dictionary<string, object>>())
            {
                object f; if ((d.TryGetValue("IsFabricated", out f) && f is bool && (bool)f) || (d.TryGetValue("IsSpamList", out f) && f is bool && (bool)f)) continue;
                var b = new Breach { Name = S(d, "Name"), Title = S(d, "Title"), Domain = S(d, "Domain") };
                DateTime bd, ad;
                if (!DateTime.TryParse(S(d, "BreachDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out bd)) continue;
                b.BreachDate = bd.Date;
                b.AddedDate = DateTime.TryParse(S(d, "AddedDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out ad) ? ad : bd;
                object dc; b.Passwords = d.TryGetValue("DataClasses", out dc) && dc is object[] && ((object[])dc).Any(x => "Passwords".Equals(x));
                var r = Domain.Registrable(b.Domain); if (r == null || !r.Contains('.')) continue;
                List<Breach> l; if (!o.TryGetValue(r, out l)) o[r] = l = new List<Breach>(); l.Add(b);
            }
            return o;
        }

        // GET the whole list (no query, no cookies, nothing about you), check it parses, then replace the cached copy
        public static int Download(string url, string path, Func<string, int> parseCount, out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "HallowKeys"; req.Timeout = 30000; req.ReadWriteTimeout = 30000; req.AllowAutoRedirect = true;
                byte[] body;
                using (var resp = req.GetResponse()) using (var s = resp.GetResponseStream()) using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxBytes) throw new IOException("the list is bigger than expected"); }
                    body = ms.ToArray();
                }
                var text = Encoding.UTF8.GetString(body);
                int count = parseCount(text);
                if (count == 0) throw new FormatException("the list was empty");
                Vault.WriteAtomic(path, body, false);
                return count;
            }
            catch (Exception e) { error = e is FormatException || e is ArgumentException || e is InvalidOperationException ? "the download wasn't the list it should be" : e.Message; return -1; }
        }
        public static int UpdateTwoFa(out string error) { return Download(TwoFaUrl, TwoFaCache, t => ParseTwoFa(t).Count, out error); }
        public static int UpdateBreaches(out string error) { return Download(BreachUrl, BreachCache, t => ParseBreaches(t).Count, out error); }
        public static Dictionary<string, TwoFaSite> LoadTwoFa() { try { return File.Exists(TwoFaCache) ? ParseTwoFa(File.ReadAllText(TwoFaCache)) : null; } catch { return null; } }
        public static Dictionary<string, List<Breach>> LoadBreaches() { try { return File.Exists(BreachCache) ? ParseBreaches(File.ReadAllText(BreachCache)) : null; } catch { return null; } }
        public static DateTime? CachedOn(string path) { return File.Exists(path) ? File.GetLastWriteTime(path) : (DateTime?)null; }

        // ---- matching (all here, on this PC)
        static IEnumerable<string> Registrables(Item it) { return it.Urls.Select(u => Domain.Registrable(Domain.HostOf(u))).Where(r => r != null && r.Contains('.')).Distinct(); }
        public static bool HasTotp(Item it) { return Totp.Parse(it.Totp) != null || !string.IsNullOrEmpty(it.TotpOnPhone) || it.Fields.Any(f => f.Kind == "totp" && Totp.Parse(f.Value) != null); }
        public static TwoFaSite TwoFaFor(Item it, Dictionary<string, TwoFaSite> sites)
        {
            if (sites == null || it.Type != "login" || HasTotp(it)) return null;
            foreach (var r in Registrables(it)) { TwoFaSite s; if (sites.TryGetValue(r, out s)) return s; }
            return null;
        }
        // the newest breach of the login's site that happened on or after the day its password was set (so that password may be in it)
        public static Breach BreachFor(Item it, Dictionary<string, List<Breach>> breaches)
        {
            if (breaches == null || it.Type != "login" || string.IsNullOrEmpty(it.Password)) return null;
            var since = it.PasswordChanged.ToUniversalTime().Date;
            return Registrables(it).SelectMany(r => { List<Breach> l; return breaches.TryGetValue(r, out l) ? l : new List<Breach>(); })
                .Where(b => b.BreachDate >= since).OrderByDescending(b => b.BreachDate).FirstOrDefault();
        }
        // http addresses on the public internet (devices on your own network - localhost, private IPs, .local / .lan / .home.arpa /
        // .internal names, single names - often can't do https, so they're left alone)
        public static List<string> HttpUrls(Item it)
        {
            return it.Urls.Where(u => { var p = Domain.Parse(u); return p != null && p.Scheme == "http" && !Local(p.Host); }).ToList();
        }
        public static bool Local(string host)
        {
            host = (host ?? "").Trim('[', ']').ToLowerInvariant(); if (host.Length == 0 || !host.Contains('.') || host == "localhost") return true;
            if (new[] { ".local", ".lan", ".home.arpa", ".internal", ".localhost", ".home", ".test" }.Any(s => host.EndsWith(s))) return true;
            IPAddress ip; if (!IPAddress.TryParse(host, out ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length == 4) return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC;
        }
        public static string ToHttps(string url) { var t = (url ?? "").Trim(); return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + t.Substring(7) : t; }
        // the site's standard "change your password" address (https only)
        public static string ChangePasswordUrl(Item it)
        {
            foreach (var u in it.Urls) { var p = Domain.Parse(u); if (p != null && p.Scheme == "https") return "https://" + p.Authority + "/.well-known/change-password"; }
            return null;
        }

        // ---- the report and the score
        public const int PLeaked = 40, PBreached = 30, PReused = 25, PWeak = 25, PTwoFa = 10, PHttp = 10, POld = 10, PExpiredItem = 5, PExpiredMax = 20;
        public static string Formula(int oldDays)
        {
            return "Each login starts at 100 and loses " + PLeaked + " if its password is leaked, " + PBreached + " if its site was breached since the password was set, " + PReused + " if the password is reused, " + PWeak + " if it's weak, and " +
                PTwoFa + " each for 2FA available but not set up, an http address and a password older than " + oldDays + " days (never below 0). The score is the average of your logins, minus " + PExpiredItem + " for each expired item (at most " + PExpiredMax + ").";
        }
        public class Issue { public string Kind, Text; public int Points; }
        public class Row { public Item Item; public List<Issue> Issues = new List<Issue>(); public int Points { get { return Issues.Sum(i => i.Points); } } public Breach Breach; public TwoFaSite TwoFa; public List<string> Http = new List<string>(); }
        public class Report
        {
            public Health.Report Basic; public List<Item> Breached = new List<Item>(), TwoFa = new List<Item>(), Http = new List<Item>(); public List<ExpiryEntry> Expired = new List<ExpiryEntry>(), Expiring = new List<ExpiryEntry>();
            public List<Row> Rows = new List<Row>(); public int Score, Logins; public bool HaveTwoFa, HaveBreaches;
            public int Count { get { return Basic.Weak.Count + Basic.Reused.Distinct().Count() + Basic.Old.Count + Basic.Leaked.Count + Breached.Count + TwoFa.Count + Http.Count + Expired.Count + Expiring.Count; } }
        }
        public static Report Build(VaultData d, int oldDays, HashSet<string> leakedIds, Dictionary<string, TwoFaSite> twoFa, Dictionary<string, List<Breach>> breaches, int expiryDays, DateTime now)
        {
            var r = new Report { Basic = Health.Check(d, oldDays, leakedIds), HaveTwoFa = twoFa != null, HaveBreaches = breaches != null };
            var exp = Expiry.Soon(d, now, expiryDays); r.Expired = exp.Where(e => e.DaysLeft < 0).ToList(); r.Expiring = exp.Where(e => e.DaysLeft >= 0).ToList();
            var logins = d.Items.Where(i => i.Type == "login").ToList(); r.Logins = logins.Count;
            var rows = new Dictionary<Item, Row>();
            Func<Item, Row> row = it => { Row x; if (!rows.TryGetValue(it, out x)) rows[it] = x = new Row { Item = it }; return x; };
            int total = 0;
            foreach (var it in logins)
            {
                var x = row(it);
                if (r.Basic.Leaked.Contains(it)) x.Issues.Add(new Issue { Kind = "leaked", Points = PLeaked, Text = "Its password is in known data breaches" });
                var b = BreachFor(it, breaches); if (b != null) { x.Breach = b; r.Breached.Add(it); x.Issues.Add(new Issue { Kind = "breached", Points = PBreached, Text = (b.Title.Length > 0 ? b.Title : b.Name) + " was breached in " + b.BreachDate.ToString("MMM yyyy") + (b.Passwords ? " (passwords taken)" : "") + ", after this password was set" }); }
                if (r.Basic.Reused.Contains(it)) x.Issues.Add(new Issue { Kind = "reused", Points = PReused, Text = "The same password is on " + (r.Basic.Reused.Count(o => o.Password == it.Password) - 1) + " other login" + (r.Basic.Reused.Count(o => o.Password == it.Password) == 2 ? "" : "s") });
                if (r.Basic.Weak.Contains(it)) x.Issues.Add(new Issue { Kind = "weak", Points = PWeak, Text = "Weak password" });
                var t = TwoFaFor(it, twoFa); if (t != null) { x.TwoFa = t; r.TwoFa.Add(it); x.Issues.Add(new Issue { Kind = "2fa", Points = PTwoFa, Text = (t.Name.Length > 0 ? t.Name : "This site") + " offers 2FA codes - not set up here" }); }
                var h = HttpUrls(it); if (h.Count > 0) { x.Http = h; r.Http.Add(it); x.Issues.Add(new Issue { Kind = "http", Points = PHttp, Text = "Unsecured address (http): " + h[0] }); }
                if (r.Basic.Old.Contains(it)) x.Issues.Add(new Issue { Kind = "old", Points = POld, Text = "Password set " + it.PasswordChanged.ToString("MMM yyyy") + " (over " + oldDays + " days ago)" });
                total += Math.Max(0, 100 - x.Points);
            }
            foreach (var e in r.Expired) row(e.Item).Issues.Add(new Issue { Kind = "expired", Points = PExpiredItem, Text = e.Label + ": " + Expiry.Describe(e.DaysLeft) });
            foreach (var e in r.Expiring) row(e.Item).Issues.Add(new Issue { Kind = "expiring", Points = 0, Text = e.Label + ": " + Expiry.Describe(e.DaysLeft) });
            double avg = logins.Count == 0 ? 100 : (double)total / logins.Count;
            r.Score = (int)Math.Round(Math.Max(0, Math.Min(100, avg - Math.Min(PExpiredMax, PExpiredItem * r.Expired.Count))), MidpointRounding.AwayFromZero);
            // fix these first: the most points to win first, then the soonest expiry, then by name
            r.Rows = rows.Values.Where(x => x.Issues.Count > 0).OrderByDescending(x => x.Points).ThenBy(x => x.Issues.Any(i => i.Kind == "expiring") ? 0 : 1)
                .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            return r;
        }
        public static string Rating(int score) { return score >= 90 ? "Excellent" : score >= 75 ? "Good" : score >= 50 ? "Needs work" : "At risk"; }
    }
}
