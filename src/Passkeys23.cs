// Hallow Keys 2.3 - "Passkeys": the app's side of WebAuthn (no window here; the item type is in Types16.cs, the UI in Features23UI.cs).
//  - The extension wraps navigator.credentials.create / get on https pages (top frame or same-origin frames) and shows its own prompt;
//    after a click there it asks the app (through the native host) to make or use a passkey. The app checks everything again:
//    https only, the rpId must be the page's host or a parent domain that is still a registrable domain (never a public suffix like
//    "co.uk" or "com", never an IP address), the origin comes from the page's address (never from the page's own words).
//  - Keys are ES256 (ECDSA P-256 with SHA-256, made and used with Windows' CNG). The private key lives only inside the encrypted vault
//    (the passkey item's privateKey field) and signs here, in the app - it is never sent to the browser, shown, copied or exported as text.
//  - Attestation "none"; authenticator data flags UP + UV + BE + BS (synced passkeys: backup eligible and backed up, like 1Password's and
//    Google's), sign count always 0 (the synced-passkey convention), Hallow Keys' own AAGUID below.
//  - User verification = Keys unlocked + a click in the extension's prompt (+ Windows Hello first, when Windows Hello unlock is on).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HallowKeys
{
    public static class Passkeys
    {
        // Hallow Keys' AAGUID (random, fixed for every copy): b7e3f1a4-5c2d-4e8f-9a61-0d3c8e2f47b5
        public static readonly byte[] Aaguid = { 0xb7, 0xe3, 0xf1, 0xa4, 0x5c, 0x2d, 0x4e, 0x8f, 0x9a, 0x61, 0x0d, 0x3c, 0x8e, 0x2f, 0x47, 0xb5 };
        public const string AaguidText = "b7e3f1a4-5c2d-4e8f-9a61-0d3c8e2f47b5";
        public const int Es256 = -7;
        public const byte UP = 0x01, UV = 0x04, BE = 0x08, BS = 0x10, AT = 0x40;

        // ---- base64url (no padding)
        public static string B64u(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        public static byte[] FromB64u(string s)
        {
            if (s == null) return null;
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            if (s.Length % 4 == 1) return null;
            s += new string('=', (4 - s.Length % 4) % 4);
            try { return Convert.FromBase64String(s); } catch { return null; }
        }

        // ---- the page's origin and the rpId rules (WebAuthn: "is a registrable domain suffix of or is equal to" the effective domain)
        // "https://Login.Example.com:8443/x" -> "https://login.example.com:8443"; null unless https with a host name
        public static string OriginOf(string url)
        {
            var u = Domain.Parse(url); if (u == null || u.Scheme != "https") return null;
            var h = Domain.Ascii(u.Host); if (h == null) return null;
            return "https://" + h + (u.IsDefaultPort ? "" : ":" + u.Port);
        }
        // null when the rpId may be used on this page, else why not
        public static string RpIdProblem(string rpId, string pageUrl)
        {
            var u = Domain.Parse(pageUrl);
            if (u == null || u.Scheme != "https") return "Passkeys only work on secure (https) pages";
            var host = Domain.Ascii(u.Host);
            System.Net.IPAddress ip;
            if (host == null || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out ip)) return "Passkeys need a site name, not an IP address";
            if (string.IsNullOrEmpty(rpId)) return "No site name (rpId)";
            if (rpId.Length > 253 || rpId.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '.' || c > 127))) return "That site name (rpId) isn't a domain";
            if (rpId.StartsWith(".") || rpId.EndsWith(".")) return "That site name (rpId) isn't a domain";
            var rp = Domain.Ascii(rpId);
            if (rp == null || rp.StartsWith(".") || rp.EndsWith(".") || rp.Contains("..")) return "That site name (rpId) isn't a domain";
            if (rp != host && !host.EndsWith("." + rp)) return "The site name (rpId) " + rp + " isn't this page's domain";
            var reg = Domain.Registrable(host);   // the rpId may be the registrable domain or longer - never a public suffix above it
            if (reg == null || rp.Length < reg.Length || !(rp == reg || rp.EndsWith("." + reg))) return "The site name (rpId) " + rp + " is a public suffix, not a site";
            return null;
        }
        public static string NormalRpId(string rpId) { return Domain.Ascii(rpId ?? "") ?? ""; }

        public static string ClientData(string type, byte[] challenge, string origin) { return ClientData(type, challenge, origin, null); }
        // v2.13: a cross-origin frame (Hallow Browser forwards one only when the frame has the publickey-credentials-* permission policy)
        public static string ClientData(string type, byte[] challenge, string origin, string topOrigin)
        {
            return "{\"type\":\"" + type + "\",\"challenge\":\"" + B64u(challenge) + "\",\"origin\":\"" + origin + "\",\"crossOrigin\":" + (topOrigin != null ? "true,\"topOrigin\":\"" + topOrigin + "\"" : "false") + "}";
        }

        // ---- keys (CNG). The private key is kept as the 32-byte scalar d; the public key as the 65-byte uncompressed point 04|x|y
        public static void NewKey(out byte[] d, out byte[] point)
        {
            var p = new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport, KeyUsage = CngKeyUsages.Signing };
            using (var k = CngKey.Create(CngAlgorithm.ECDsaP256, null, p))
            {
                var blob = k.Export(CngKeyBlobFormat.EccPrivateBlob);   // BCRYPT_ECCKEY_BLOB: magic, cbKey, X, Y, d
                try
                {
                    int n = BitConverter.ToInt32(blob, 4); if (n != 32) throw new CryptographicException("unexpected key size");
                    point = new byte[65]; point[0] = 4; Buffer.BlockCopy(blob, 8, point, 1, 64);
                    d = new byte[32]; Buffer.BlockCopy(blob, 8 + 64, d, 0, 32);
                }
                finally { Array.Clear(blob, 0, blob.Length); }
            }
        }
        static readonly byte[] PrivMagic = { 0x45, 0x43, 0x53, 0x32 }, PubMagic = { 0x45, 0x43, 0x53, 0x31 };   // ECS2 / ECS1, cbKey 32
        // ECDSA-SHA256 over data; returns the DER signature WebAuthn uses
        public static byte[] Sign(byte[] d, byte[] point, byte[] data)
        {
            if (d == null || d.Length != 32 || point == null || point.Length != 65 || point[0] != 4) throw new CryptographicException("bad passkey key");
            var blob = new byte[8 + 96]; Buffer.BlockCopy(PrivMagic, 0, blob, 0, 4); blob[4] = 32;
            Buffer.BlockCopy(point, 1, blob, 8, 64); Buffer.BlockCopy(d, 0, blob, 72, 32);
            try
            {
                using (var k = CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob))
                using (var ec = new ECDsaCng(k)) { ec.HashAlgorithm = CngAlgorithm.Sha256; return Der(ec.SignData(data)); }
            }
            finally { Array.Clear(blob, 0, blob.Length); }
        }
        public static bool Verify(byte[] point, byte[] data, byte[] derSig)
        {
            var raw = FromDer(derSig); if (raw == null) return false;
            var blob = new byte[8 + 64]; Buffer.BlockCopy(PubMagic, 0, blob, 0, 4); blob[4] = 32; Buffer.BlockCopy(point, 1, blob, 8, 64);
            using (var k = CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob))
            using (var ec = new ECDsaCng(k)) { ec.HashAlgorithm = CngAlgorithm.Sha256; return ec.VerifyData(data, raw); }
        }
        // r|s (32 + 32) -> DER SEQUENCE { INTEGER r, INTEGER s }
        public static byte[] Der(byte[] rs)
        {
            Func<int, byte[]> intOf = off =>
            {
                int i = off, end = off + rs.Length / 2; while (i < end - 1 && rs[i] == 0) i++;
                var v = rs.Skip(i).Take(end - i).ToList(); if ((v[0] & 0x80) != 0) v.Insert(0, 0);
                return new byte[] { 2, (byte)v.Count }.Concat(v).ToArray();
            };
            var r = intOf(0); var s = intOf(rs.Length / 2);
            return new byte[] { 0x30, (byte)(r.Length + s.Length) }.Concat(r).Concat(s).ToArray();
        }
        public static byte[] FromDer(byte[] der)
        {
            try
            {
                if (der == null || der.Length < 8 || der[0] != 0x30 || der[1] != der.Length - 2) return null;
                int p = 2; var o = new byte[64];
                for (int k = 0; k < 2; k++)
                {
                    if (der[p] != 2) return null; int n = der[p + 1]; p += 2;
                    var v = der.Skip(p).Take(n).SkipWhile(b => b == 0).ToArray(); if (v.Length > 32) return null;
                    Buffer.BlockCopy(v, 0, o, k * 32 + 32 - v.Length, v.Length); p += n;
                }
                return p == der.Length ? o : null;
            }
            catch { return null; }
        }
        // SubjectPublicKeyInfo (DER) for P-256 - what getPublicKey() returns
        static readonly byte[] SpkiPrefix = { 0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01, 0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00 };
        public static byte[] Spki(byte[] point) { return SpkiPrefix.Concat(point).ToArray(); }

        // ---- CBOR (just what WebAuthn needs: small maps, ints, byte and text strings), canonical CTAP2 order is written by hand
        static void Head(MemoryStream m, int major, long v)
        {
            if (v < 24) m.WriteByte((byte)(major << 5 | (int)v));
            else if (v < 256) { m.WriteByte((byte)(major << 5 | 24)); m.WriteByte((byte)v); }
            else if (v < 65536) { m.WriteByte((byte)(major << 5 | 25)); m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
            else { m.WriteByte((byte)(major << 5 | 26)); for (int i = 3; i >= 0; i--) m.WriteByte((byte)(v >> (8 * i))); }
        }
        static void Int(MemoryStream m, long v) { if (v >= 0) Head(m, 0, v); else Head(m, 1, -1 - v); }
        static void Bytes(MemoryStream m, byte[] b) { Head(m, 2, b.Length); m.Write(b, 0, b.Length); }
        static void Text(MemoryStream m, string s) { var b = Encoding.UTF8.GetBytes(s); Head(m, 3, b.Length); m.Write(b, 0, b.Length); }
        // COSE_Key EC2 / ES256: {1: 2, 3: -7, -1: 1, -2: x, -3: y}
        public static byte[] CoseKey(byte[] point)
        {
            var m = new MemoryStream(); Head(m, 5, 5);
            Int(m, 1); Int(m, 2); Int(m, 3); Int(m, Es256); Int(m, -1); Int(m, 1);
            Int(m, -2); Bytes(m, point.Skip(1).Take(32).ToArray()); Int(m, -3); Bytes(m, point.Skip(33).Take(32).ToArray());
            return m.ToArray();
        }
        public static byte[] AuthData(string rpId, byte flags, byte[] credId, byte[] point)
        {
            var m = new MemoryStream();
            using (var h = SHA256.Create()) { var rh = h.ComputeHash(Encoding.ASCII.GetBytes(rpId)); m.Write(rh, 0, 32); }
            m.WriteByte(flags); m.Write(new byte[4], 0, 4);   // sign count 0
            if (credId != null)
            {
                m.Write(Aaguid, 0, 16); m.WriteByte((byte)(credId.Length >> 8)); m.WriteByte((byte)credId.Length); m.Write(credId, 0, credId.Length);
                var ck = CoseKey(point); m.Write(ck, 0, ck.Length);
            }
            return m.ToArray();
        }
        // {"fmt": "none", "attStmt": {}, "authData": ...}
        public static byte[] AttestationNone(byte[] authData)
        {
            var m = new MemoryStream(); Head(m, 5, 3);
            Text(m, "fmt"); Text(m, "none"); Text(m, "attStmt"); Head(m, 5, 0); Text(m, "authData"); Bytes(m, authData);
            return m.ToArray();
        }

        // ---- the passkey items
        public static string Val(Item it, string k) { return ItemTypes.Val(it, k); }
        public static bool IsKeyField(Item it, CustomField f) { return it != null && f != null && it.Type == "passkey" && f.Key == "privateKey"; }
        public static IEnumerable<Item> For(IEnumerable<Item> items, string rpId) { return items.Where(i => i.Type == "passkey" && Val(i, "rpId") == rpId); }
        // the passkeys that belong next to a login (same site as one of its addresses)
        public static List<Item> OfLogin(IEnumerable<Item> items, Item login)
        {
            if (login == null || login.Type != "login") return new List<Item>();
            var regs = new HashSet<string>(login.Urls.Select(Domain.HostOf).Where(h => h != null).Select(Domain.Registrable).Where(r => r != null && r.Contains('.')));
            return items.Where(i => i.Type == "passkey" && regs.Contains(Domain.Registrable(Val(i, "rpId")) ?? "")).OrderBy(i => Val(i, "userName"), StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static Item NewItem(string rpId, string rpName, byte[] userHandle, string userName, string displayName, out byte[] credId, out byte[] point)
        {
            byte[] d; NewKey(out d, out point); credId = Rng.Bytes(16);
            var it = new Item { Type = "passkey", Title = string.IsNullOrWhiteSpace(rpName) ? rpId : rpName.Trim(), Urls = { "https://" + rpId } };
            ItemTypes.Ensure(it);
            ItemTypes.Set(it, "rpId", rpId); ItemTypes.Set(it, "userName", userName ?? ""); ItemTypes.Set(it, "displayName", displayName ?? "");
            ItemTypes.Set(it, "userHandle", B64u(userHandle)); ItemTypes.Set(it, "credentialId", B64u(credId)); ItemTypes.Set(it, "publicKey", B64u(point));
            ItemTypes.Set(it, "privateKey", B64u(d)); ItemTypes.Set(it, "algorithm", "ES256 (P-256)");
            Array.Clear(d, 0, d.Length);
            return it;
        }

        // ---- the native host's passkey requests (runs on the window's thread; NativeServer.Answer sends "passkey-*" here)
        static Dictionary<string, object> Err(string name, string msg) { return new Dictionary<string, object> { { "ok", false }, { "error", name }, { "msg", msg } }; }
        static string S(Dictionary<string, object> m, string k) { object o; return m.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }
        static List<string> L(Dictionary<string, object> m, string k)
        {
            object o; if (!m.TryGetValue(k, out o) || o == null) return new List<string>();
            var a = o as System.Collections.IEnumerable; if (a == null || o is string) return new List<string>();
            return a.Cast<object>().Where(x => x != null).Select(x => x.ToString()).Take(200).ToList();
        }
        public static Dictionary<string, object> Answer(string type, Dictionary<string, object> m, Vault v, List<Item> shown, Func<Item, bool> visible, Action<string> used, Func<string> newVaultId, Action<string> saved, string via = null)
        {
            var url = S(m, "url"); var rpId = NormalRpId(S(m, "rpId"));
            var origin = OriginOf(url);
            if (origin == null) return Err("SecurityError", "Passkeys only work on secure (https) pages");
            // v2.13: the tab's address when the request comes from a frame of another origin (https too; same origin = not cross-origin)
            string topOrigin = null; var topUrl = S(m, "topUrl");
            if (topUrl.Length > 0) { topOrigin = OriginOf(topUrl); if (topOrigin == null) return Err("SecurityError", "Passkeys only work on secure (https) pages"); if (topOrigin == origin) topOrigin = null; }
            var why = RpIdProblem(rpId, url);
            if (why != null && RelatedOrigins.RpIdShape(rpId) && RelatedOrigins.Allowed(rpId, origin)) why = null;   // v2.12: the page is listed in the rpId's /.well-known/webauthn (checked on the pipe's thread)
            if (why != null) return Err("SecurityError", why);
            if (v == null) return new Dictionary<string, object> { { "ok", false }, { "running", true }, { "locked", true }, { "msg", "Hallow Keys is locked" } };
            var allow = new HashSet<string>(L(m, "allow"));
            var mine = For(shown, rpId).ToList();
            switch (type)
            {
                case "passkey-list":
                {
                    var list = mine.Where(i => allow.Count == 0 || allow.Contains(Val(i, "credentialId"))).OrderByDescending(i => Vaults.LastUsed(v.Data, i.Id) ?? DateTime.MinValue).Take(30)
                        .Select(i => (object)new Dictionary<string, object> { { "id", i.Id }, { "title", i.Title }, { "userName", Val(i, "userName") }, { "displayName", Val(i, "displayName") } }).ToList();
                    return new Dictionary<string, object> { { "ok", true }, { "rpId", rpId }, { "items", list } };
                }
                case "passkey-get":
                {
                    var challenge = FromB64u(S(m, "challenge"));
                    if (challenge == null || challenge.Length < 16 || challenge.Length > 1024) return Err("NotAllowedError", "The site's challenge isn't valid");
                    var it = mine.FirstOrDefault(i => i.Id == S(m, "id"));
                    if (it == null) return Err("NotAllowedError", "That passkey isn't for this site");
                    if (allow.Count > 0 && !allow.Contains(Val(it, "credentialId"))) return Err("NotAllowedError", "The site didn't ask for that passkey");
                    byte[] d = FromB64u(Val(it, "privateKey")), point = FromB64u(Val(it, "publicKey")), credId = FromB64u(Val(it, "credentialId"));
                    if (d == null || point == null || credId == null) return Err("NotAllowedError", "That passkey is damaged");
                    try
                    {
                        var cd = Encoding.UTF8.GetBytes(ClientData("webauthn.get", challenge, origin, topOrigin));
                        var ad = AuthData(rpId, (byte)(UP | UV | BE | BS), null, null);
                        byte[] hash; using (var h = SHA256.Create()) hash = h.ComputeHash(cd);
                        var sig = Sign(d, point, ad.Concat(hash).ToArray());
                        used(it.Id);
                        return new Dictionary<string, object> { { "ok", true }, { "id", B64u(credId) }, { "clientDataJSON", B64u(cd) }, { "authenticatorData", B64u(ad) }, { "signature", B64u(sig) },
                            { "userHandle", Val(it, "userHandle") }, { "authenticatorAttachment", "platform" } };
                    }
                    finally { Array.Clear(d, 0, d.Length); }
                }
                case "passkey-create":
                {
                    var challenge = FromB64u(S(m, "challenge")); var uh = FromB64u(S(m, "userId"));
                    if (challenge == null || challenge.Length < 16 || challenge.Length > 1024) return Err("NotAllowedError", "The site's challenge isn't valid");
                    if (uh == null || uh.Length < 1 || uh.Length > 64) return Err("TypeError", "The site's user id isn't valid");
                    var algs = L(m, "algs");
                    if (algs.Count > 0 && !algs.Contains(Es256.ToString())) return Err("NotSupportedError", "Hallow Keys makes ES256 passkeys only");
                    var exclude = new HashSet<string>(L(m, "exclude"));
                    // excluded credentials: only the vaults this PC shows count (Travel Mode hides the rest - they aren't told apart)
                    if (mine.Any(i => exclude.Contains(Val(i, "credentialId")))) return Err("InvalidStateError", "You already have a passkey for this account in Hallow Keys");
                    var userName = S(m, "userName"); var display = S(m, "displayName"); var rpName = S(m, "rpName");
                    if (userName.Length > 256 || display.Length > 256 || rpName.Length > 256) return Err("TypeError", "Names too long");
                    byte[] credId, point; var it = NewItem(rpId, rpName, uh, userName, display, out credId, out point);
                    // the same account (user handle) saved again on this site replaces the old passkey (it moves to the trash)
                    var old = mine.Where(i => Val(i, "userHandle") == B64u(uh)).ToList();
                    Vaults.Put(it, newVaultId()); v.Data.Items.Add(it);
                    foreach (var o in old) Organize.MoveToTrash(v.Data, o, DateTime.Now);
                    var cd = Encoding.UTF8.GetBytes(ClientData("webauthn.create", challenge, origin, topOrigin));
                    var ad = AuthData(rpId, (byte)(UP | UV | BE | BS | AT), credId, point);
                    used(it.Id);
                    saved("Saved a passkey for " + rpId + (via != null ? " from " + via : "") + (old.Count > 0 ? " (the old one for that account went to the trash)" : "") + ".");
                    return new Dictionary<string, object> { { "ok", true }, { "id", B64u(credId) }, { "clientDataJSON", B64u(cd) }, { "attestationObject", B64u(AttestationNone(ad)) },
                        { "authenticatorData", B64u(ad) }, { "publicKey", B64u(Spki(point)) }, { "publicKeyAlgorithm", Es256 }, { "transports", new[] { "hybrid", "internal" } }, { "authenticatorAttachment", "platform" } };
                }
            }
            return Err("NotSupportedError", "Unknown passkey request");
        }
    }
}
