// Hallow Keys - the password generator, 2FA codes (RFC 6238 TOTP) and Password Health.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HallowKeys
{
    public static class Generator
    {
        public const string Lower = "abcdefghijkmnpqrstuvwxyz", Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", Digits = "23456789", Symbols = "!@#$%^&*-_=+?";
        public const string LowerAll = "abcdefghijklmnopqrstuvwxyz", UpperAll = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", DigitsAll = "0123456789";
        // a random password: every chosen set appears at least once; characters drawn uniformly (rejection sampling)
        public static string Password(int length, bool lower, bool upper, bool digits, bool symbols, bool avoidLookAlikes)
        {
            var sets = new List<string>();
            if (lower) sets.Add(avoidLookAlikes ? Lower : LowerAll); if (upper) sets.Add(avoidLookAlikes ? Upper : UpperAll);
            if (digits) sets.Add(avoidLookAlikes ? Digits : DigitsAll); if (symbols) sets.Add(Symbols);
            if (sets.Count == 0) sets.Add(LowerAll);
            length = Math.Max(Math.Max(4, sets.Count), Math.Min(128, length));
            var all = string.Concat(sets);
            while (true)
            {
                var c = new char[length]; for (int i = 0; i < length; i++) c[i] = all[Rng.Below(all.Length)];
                if (sets.All(s => c.Any(ch => s.IndexOf(ch) >= 0))) return new string(c);   // try again until every set is in (still uniform over valid passwords)
            }
        }
        public static double PasswordBits(int length, bool lower, bool upper, bool digits, bool symbols, bool avoid)
        {
            int n = (lower ? (avoid ? Lower : LowerAll).Length : 0) + (upper ? (avoid ? Upper : UpperAll).Length : 0) + (digits ? (avoid ? Digits : DigitsAll).Length : 0) + (symbols ? Symbols.Length : 0);
            return n == 0 ? 0 : length * Math.Log(n, 2);
        }
        // words from the built-in list, joined; a digit and a capital make sites with rules happy
        public static string Passphrase(int words, string sep, bool capitalize, bool addDigit)
        {
            words = Math.Max(3, Math.Min(12, words));
            var w = Enumerable.Range(0, words).Select(i => { var x = Words.List[Rng.Below(Words.List.Length)]; return capitalize ? char.ToUpperInvariant(x[0]) + x.Substring(1) : x; }).ToList();
            if (addDigit) { int i = Rng.Below(words); w[i] = w[i] + Rng.Below(10); }
            return string.Join(sep ?? "-", w);
        }
        public static double PassphraseBits(int words, bool addDigit) { return words * Math.Log(Words.List.Length, 2) + (addDigit ? Math.Log(10 * words, 2) : 0); }
    }

    public static class Totp
    {
        public class Spec { public byte[] Secret; public string Algorithm = "SHA1"; public int Digits = 6, Period = 30; public string Issuer, Account; }
        // "otpauth://totp/Issuer:me?secret=JBSW...&algorithm=SHA256&digits=8&period=30" or a bare base32 secret
        public static Spec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim(); var s = new Spec();
            if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                Uri u; if (!Uri.TryCreate(text, UriKind.Absolute, out u) || !string.Equals(u.Host, "totp", StringComparison.OrdinalIgnoreCase)) return null;
                var label = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')); var colon = label.IndexOf(':');
                if (colon >= 0) { s.Issuer = label.Substring(0, colon); s.Account = label.Substring(colon + 1).Trim(); } else s.Account = label;
                foreach (var kv in u.Query.TrimStart('?').Split('&').Where(x => x.Contains('=')))
                {
                    var k = Uri.UnescapeDataString(kv.Substring(0, kv.IndexOf('='))).ToLowerInvariant(); var v = Uri.UnescapeDataString(kv.Substring(kv.IndexOf('=') + 1));
                    int n;
                    if (k == "secret") s.Secret = Base32(v);
                    else if (k == "algorithm") s.Algorithm = v.ToUpperInvariant();
                    else if (k == "digits" && int.TryParse(v, out n)) s.Digits = n;
                    else if (k == "period" && int.TryParse(v, out n)) s.Period = n;
                    else if (k == "issuer") s.Issuer = v;
                }
            }
            else s.Secret = Base32(text);
            if (s.Secret == null || s.Secret.Length == 0) return null;
            if (s.Algorithm != "SHA1" && s.Algorithm != "SHA256" && s.Algorithm != "SHA512") return null;
            if (s.Digits != 6 && s.Digits != 8) return null;
            if (s.Period < 10 || s.Period > 300) return null;
            return s;
        }
        public static byte[] Base32(string text)
        {
            const string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var clean = new string((text ?? "").ToUpperInvariant().Where(c => c != ' ' && c != '-' && c != '=').ToArray());
            var o = new List<byte>(); int buf = 0, bits = 0;
            foreach (var ch in clean) { int v = a.IndexOf(ch); if (v < 0) return null; buf = (buf << 5) | v; bits += 5; if (bits >= 8) { o.Add((byte)(buf >> (bits - 8))); bits -= 8; } }
            return o.ToArray();
        }
        // RFC 6238 / RFC 4226 (dynamic truncation)
        public static string Code(Spec s, long unixSeconds)
        {
            long counter = unixSeconds / s.Period;
            var msg = BitConverter.GetBytes(counter); if (BitConverter.IsLittleEndian) Array.Reverse(msg);
            HMAC h = s.Algorithm == "SHA256" ? (HMAC)new HMACSHA256(s.Secret) : s.Algorithm == "SHA512" ? (HMAC)new HMACSHA512(s.Secret) : new HMACSHA1(s.Secret);
            byte[] hash; using (h) hash = h.ComputeHash(msg);
            int off = hash[hash.Length - 1] & 0x0F;
            int bin = ((hash[off] & 0x7F) << 24) | (hash[off + 1] << 16) | (hash[off + 2] << 8) | hash[off + 3];
            int mod = s.Digits == 8 ? 100000000 : 1000000;
            return (bin % mod).ToString(new string('0', s.Digits));
        }
        public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
    }

    public static class Health
    {
        // a rough entropy estimate: the character pool times the length, less for repeats, sequences and common words
        static readonly string[] Common = { "password", "qwerty", "letmein", "welcome", "admin", "iloveyou", "dragon", "monkey", "football", "baseball", "master", "login", "abc123", "111111", "123123", "sunshine", "princess", "shadow", "trustno1" };
        public static double Bits(string p)
        {
            if (string.IsNullOrEmpty(p)) return 0;
            int pool = 0;
            if (p.Any(char.IsLower)) pool += 26; if (p.Any(char.IsUpper)) pool += 26; if (p.Any(char.IsDigit)) pool += 10; if (p.Any(c => !char.IsLetterOrDigit(c) && c < 128)) pool += 33; if (p.Any(c => c >= 128)) pool += 100;
            double per = Math.Log(Math.Max(pool, 2), 2);
            int distinct = p.Distinct().Count();
            double effLen = Math.Min(p.Length, distinct * 2.0);
            int seq = 0; for (int i = 2; i < p.Length; i++) if (p[i] - p[i - 1] == p[i - 1] - p[i - 2] && Math.Abs(p[i] - p[i - 1]) <= 1) seq++;
            effLen = Math.Max(1, effLen - seq);
            double bits = effLen * per;
            var low = p.ToLowerInvariant();
            if (Common.Any(w => low.Contains(w))) bits = Math.Min(bits, 20);
            return bits;
        }
        public static string Rating(double bits) { return bits < 40 ? "Weak" : bits < 60 ? "Fair" : bits < 80 ? "Good" : "Strong"; }

        public class Report { public List<Item> Weak = new List<Item>(), Reused = new List<Item>(), Old = new List<Item>(), Leaked = new List<Item>(); }
        public static Report Check(VaultData d, int oldDays, HashSet<string> leakedIds)
        {
            var r = new Report();
            var logins = d.Items.Where(i => i.Type == "login" && !string.IsNullOrEmpty(i.Password)).ToList();
            foreach (var i in logins) if (Bits(i.Password) < 40) r.Weak.Add(i);
            foreach (var g in logins.GroupBy(i => i.Password, StringComparer.Ordinal).Where(g => g.Count() > 1)) r.Reused.AddRange(g);
            foreach (var i in logins) if ((DateTime.Now - i.PasswordChanged).TotalDays > oldDays) r.Old.Add(i);
            if (leakedIds != null) foreach (var i in logins) if (leakedIds.Contains(i.Id)) r.Leaked.Add(i);
            return r;
        }
    }
}
