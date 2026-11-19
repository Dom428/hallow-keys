// Hallow Keys - which saved logins belong to a web page. Strict on purpose:
//  - the page host must equal the saved host, or both must share the same registrable domain (accounts.example.com ~ www.example.com)
//  - never a https login on a http page; explicit ports must match
//  - hosts are compared in their punycode (ASCII) form, so look-alike Unicode letters never match a real name
// The registrable domain uses a built-in list of common multi-part public suffixes (co.uk, com.au, github.io...).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HallowKeys
{
    public static class Domain
    {
        static readonly HashSet<string> MultiSuffix = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "co.uk", "org.uk", "ac.uk", "gov.uk", "ltd.uk", "plc.uk", "me.uk", "net.uk", "sch.uk", "nhs.uk",
            "com.au", "net.au", "org.au", "edu.au", "gov.au", "asn.au", "id.au", "co.nz", "org.nz", "net.nz", "govt.nz", "ac.nz",
            "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "co.kr", "or.kr", "com.cn", "net.cn", "org.cn", "gov.cn", "com.hk", "com.tw", "com.sg", "com.my",
            "co.in", "net.in", "org.in", "gov.in", "ac.in", "co.za", "org.za", "gov.za", "com.br", "net.br", "org.br", "gov.br", "com.mx", "org.mx", "gob.mx",
            "com.ar", "com.co", "com.pe", "com.ve", "com.tr", "org.tr", "gov.tr", "com.pl", "net.pl", "org.pl", "co.il", "org.il", "ac.il", "com.ua", "co.th",
            "com.ph", "com.vn", "com.eg", "com.sa", "com.pk", "co.id", "or.id", "com.ng", "co.ke", "com.gr", "co.at", "or.at", "com.es", "com.pt",
            "github.io", "gitlab.io", "herokuapp.com", "azurewebsites.net", "cloudfront.net", "appspot.com", "blogspot.com", "netlify.app", "vercel.app",
            "pages.dev", "workers.dev", "firebaseapp.com", "web.app", "s3.amazonaws.com", "duckdns.org", "ddns.net", "no-ip.org" };

        // "https://Accounts.Example.com:443/login" -> "accounts.example.com" (ASCII/punycode, lower case); null if it isn't a web address
        public static string HostOf(string url)
        {
            var u = Parse(url); return u == null ? null : Ascii(u.Host);
        }
        public static Uri Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim(); if (!url.Contains("://")) url = "https://" + url;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            if (u.Scheme != "http" && u.Scheme != "https") return null;
            return u;
        }
        public static string Ascii(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            try { return new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant(); } catch { return null; }
        }
        // "mail.google.co.uk" -> "google.co.uk"; IP addresses and single labels stay as they are
        public static string Registrable(string host)
        {
            host = Ascii(host); if (host == null) return null;
            System.Net.IPAddress ip; if (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out ip)) return host;
            var parts = host.Split('.'); if (parts.Length <= 2) return host;
            var last2 = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            var last3 = parts.Length >= 3 ? parts[parts.Length - 3] + "." + last2 : null;
            if (parts.Length >= 3 && MultiSuffix.Contains(last2)) return last3;
            if (parts.Length >= 4 && MultiSuffix.Contains(last3)) return parts[parts.Length - 4] + "." + last3;
            return last2;
        }
        // may a login saved for savedUrl be offered on pageUrl?
        public static bool Matches(string savedUrl, string pageUrl)
        {
            var s = Parse(savedUrl); var p = Parse(pageUrl);
            if (s == null || p == null) return false;
            if (p.Scheme != "https" && s.Scheme == "https") return false;     // never send a https password to a http page
            var sh = Ascii(s.Host); var ph = Ascii(p.Host);
            if (sh == null || ph == null) return false;
            bool sPort = HasExplicitPort(savedUrl), pPort = HasExplicitPort(pageUrl);
            if ((sPort || pPort) && s.Port != p.Port) return false;
            if (sh == ph) return true;
            var sr = Registrable(sh); var pr = Registrable(ph);
            System.Net.IPAddress ip; if (System.Net.IPAddress.TryParse(sh.Trim('[', ']'), out ip)) return false;   // IPs: exact only
            return sr != null && sr == pr && sr.Contains('.');
        }
        static bool HasExplicitPort(string url)
        {
            var s = url.Trim(); var i = s.IndexOf("://"); if (i >= 0) s = s.Substring(i + 3);
            var end = s.IndexOfAny(new[] { '/', '?', '#' }); if (end >= 0) s = s.Substring(0, end);
            var at = s.LastIndexOf('@'); if (at >= 0) s = s.Substring(at + 1);
            if (s.StartsWith("[")) return s.Contains("]:");
            return s.Contains(':');
        }
    }
}
