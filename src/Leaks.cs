// Hallow Keys v1.2 - "Check for leaked passwords" (Have I Been Pwned's Pwned Passwords range API) and the phone copy for Hallow Remote.
// Leak check: for each password, only the first 5 characters of its SHA-1 hash are sent (k-anonymity), with the Add-Padding header so the
// answer's size doesn't give anything away; the match happens here. Runs only when you click, or weekly if you turn that on.
// Phone copy: an extra file (phone.bin) with your items, encrypted with AES-256-GCM under a key from your master password (PBKDF2-SHA256,
// 600,000 rounds, its own salt) in a form the phone's browser can open with WebCrypto. Hallow Remote serves only this file, and only over HTTPS.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class Pwned
    {
        // HALLOW_KEYS_PWNED (tests only, in a test folder): a local stand-in for the API
        static string Api { get { var t = KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_PWNED") : null; return (t ?? "https://api.pwnedpasswords.com").TrimEnd('/'); } }
        public static string Sha1Hex(string password)
        {
            var b = Encoding.UTF8.GetBytes(password);
            try { using (var h = SHA1.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", ""); } finally { Array.Clear(b, 0, b.Length); }
        }
        // ids of the logins whose password was found in a breach; progress gets "n of m"
        public static HashSet<string> Check(IList<Item> items, Action<string> progress, out string error)
        {
            error = null; var leaked = new HashSet<string>();
            var byHash = items.Where(i => i.Type == "login" && !string.IsNullOrEmpty(i.Password)).GroupBy(i => Sha1Hex(i.Password)).ToList();
            var prefixes = byHash.GroupBy(g => g.Key.Substring(0, 5)).ToList(); int n = 0;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            foreach (var p in prefixes)
            {
                n++; progress("Checking " + n + " of " + prefixes.Count + "...");
                string body;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(Api + "/range/" + p.Key);
                    req.UserAgent = "HallowKeys"; req.Headers["Add-Padding"] = "true"; req.Timeout = 15000;
                    using (var r = req.GetResponse()) using (var rd = new StreamReader(r.GetResponseStream())) body = rd.ReadToEnd();
                }
                catch (Exception e) { error = "Couldn't reach Have I Been Pwned: " + e.Message; return null; }
                var found = new HashSet<string>();
                foreach (var line in body.Split('\n'))
                {
                    var c = line.Trim().Split(':'); int count;
                    if (c.Length == 2 && int.TryParse(c[1], out count) && count > 0) found.Add(p.Key + c[0].ToUpperInvariant());   // count 0 = padding
                }
                foreach (var g in p) if (found.Contains(g.Key)) foreach (var i in g) leaked.Add(i.Id);
            }
            return leaked;
        }
    }

    public static class PhoneCopy
    {
        public static string FilePath { get { return Path.Combine(KeysSettings.Dir, "phone.bin"); } }
        static readonly byte[] Aad = Encoding.ASCII.GetBytes("HallowKeysPhone1");
        // a new phone key from the master password (its own salt); stored inside the encrypted vault
        public static void NewKey(VaultData d, byte[] password)
        {
            var salt = Rng.Bytes(16); d.PhoneSalt = salt; d.PhoneKey = Kdf.Derive(password, salt, Kdf.MinIterations); d.PhoneIterations = Kdf.MinIterations;
        }
        public static void Write(VaultData d)
        {
            if (d.PhoneKey == null || d.PhoneSalt == null) return;
            var items = d.Items.Select(ItemTypes.PhoneItem).ToList();   // v1.6: new types and your own fields in a form Remote 1.10 already shows (see ItemTypes.PhoneItem)
            var json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Serialize(new Dictionary<string, object> { { "made", DateTime.Now.ToString("s") }, { "items", items } });
            var plain = Encoding.UTF8.GetBytes(json);
            try
            {
                var nonce = Rng.Bytes(12); byte[] c, t; Gcm.EncryptRaw(d.PhoneKey, nonce, plain, Aad, out c, out t);
                var blob = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "v", 1 }, { "kdf", "PBKDF2-SHA256" }, { "iter", d.PhoneIterations }, { "salt", Convert.ToBase64String(d.PhoneSalt) },
                    { "iv", Convert.ToBase64String(nonce) }, { "aad", "HallowKeysPhone1" }, { "ct", Convert.ToBase64String(c.Concat(t).ToArray()) }, { "made", DateTime.Now.ToString("s") } });
                Vault.WriteAtomic(FilePath, Encoding.UTF8.GetBytes(blob), false);
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
        public static void Remove() { try { if (File.Exists(FilePath)) { File.WriteAllBytes(FilePath, new byte[16]); File.Delete(FilePath); } } catch { } }
    }
}
