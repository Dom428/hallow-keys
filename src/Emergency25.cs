// Hallow Keys 2.5 - "Emergency access", the parts without a window (the Sharing page's Emergency access section and the fuller Emergency
// Kit are in Features25UI.cs / Features20UI.cs):
//  - A trusted-contact package (.hkemergency): an encrypted copy of the vaults you pick that only your contact's Hallow Keys can open -
//    a random content key encrypts the items (AES-256-GCM, the header as associated data) and is sealed to the contact's member card
//    (ECDH P-256 + HKDF + AES-GCM, see Sharing24.cs). Optionally it also needs your recovery code: then a key from the code
//    (PBKDF2-SHA256, 600,000 rounds, its own salt) is mixed into the seal, so the package is useless without both the contact's Keys and
//    the code (keep the code somewhere they can get to only when it's needed - a sealed envelope, a solicitor, a safe).
//  - It's a snapshot: it holds the items as they were when you made it. Make a new one now and then.
//  - Not offered: NordPass's / Bitwarden's "release after N days unless you say no" - that needs a server that waits and asks you, and
//    Hallow Keys has none. The app says so.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class EmergencyPackage
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKEMRG1\0");
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024 };
        public class Info { public string Id = "", From = "", FromFingerprint = "", To = "", ToId = "", Vaults = ""; public DateTime Made; public bool NeedsCode; public int Items; }
        static byte[] CodeKey(string code, byte[] salt) { var b = Vault.RecoveryBytes(code); try { return Kdf.Derive(b, salt, Kdf.MinIterations); } finally { Array.Clear(b, 0, b.Length); } }
        static string SealInfo(string id, string toId) { return "emergency|" + id + "|" + toId; }

        // items of the chosen vaults (live items only - not the trash), without revisions; the vault records so the contact sees the names
        public static byte[] Make(VaultData owner, IEnumerable<string> vaultIds, MemberCard contact, string recoveryCode, string vaultPathForCheck)
        {
            if (owner.MemberPub == null) throw new VaultException("Your member identity isn't set up yet (open the Sharing page once).");
            var ids = new HashSet<string>(vaultIds.Select(x => string.IsNullOrEmpty(x) ? Vaults.PersonalId : x));
            if (ids.Count == 0) throw new VaultException("Pick at least one vault.");
            byte[] codeSalt = null, codeKey = null;
            if (recoveryCode != null)
            {
                // the code must really be this vault's recovery code (otherwise the contact could never open it)
                try { using (Vault.OpenWithRecovery(vaultPathForCheck, recoveryCode)) { } } catch { throw new VaultException("That isn't your vault's recovery code - the package wasn't made."); }
                codeSalt = Rng.Bytes(16); codeKey = CodeKey(recoveryCode, codeSalt);
            }
            var items = owner.Items.Where(i => ids.Contains(Vaults.IdOf(owner, i))).Select(i => { var c = ItemTypes.Clone(i, false); c.Sync = null; return c; }).ToList();
            var vaults = Vaults.Live(owner).Where(v => ids.Contains(v.Id)).ToList();
            var id = Guid.NewGuid().ToString("N"); var key = Rng.Bytes(32);
            try
            {
                var wrap = Ecies.Seal(contact.Pub, key, SealInfo(id, contact.Id), codeKey);
                var me = Members.Mine(owner);
                var header = Encoding.UTF8.GetBytes(js.Serialize(new Dictionary<string, object> { { "v", 1 }, { "id", id }, { "from", me.Name }, { "fromFp", me.Fingerprint }, { "to", contact.Name }, { "toId", contact.Id },
                    { "made", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }, { "needsCode", codeKey != null }, { "codeSalt", codeSalt == null ? "" : Passkeys.B64u(codeSalt) }, { "codeIter", Kdf.MinIterations },
                    { "vaults", string.Join(", ", vaults.Select(v => v.Name)) }, { "items", items.Count }, { "wrap", Passkeys.B64u(wrap) } }));
                byte[] body; using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(VaultData)).WriteObject(ms, new VaultData { Items = items, Vaults = vaults, Folders = owner.Folders.ToList() }); body = ms.ToArray(); }
                try
                {
                    var nonce = Rng.Bytes(12); byte[] c, t; Gcm.EncryptRaw(key, nonce, body, Magic.Concat(header).ToArray(), out c, out t);
                    var o = new MemoryStream(); o.Write(Magic, 0, 8); o.Write(BitConverter.GetBytes(header.Length), 0, 4); o.Write(header, 0, header.Length); o.Write(nonce, 0, 12); o.Write(c, 0, c.Length); o.Write(t, 0, 16);
                    return o.ToArray();
                }
                finally { Array.Clear(body, 0, body.Length); }
            }
            finally { Array.Clear(key, 0, key.Length); if (codeKey != null) Array.Clear(codeKey, 0, codeKey.Length); }
        }
        public static Info Peek(byte[] b)
        {
            if (b == null || b.Length < 40 || !b.Take(8).SequenceEqual(Magic)) throw new VaultException("That isn't a Hallow Keys emergency package.");
            int hl = BitConverter.ToInt32(b, 8); if (hl <= 0 || hl > b.Length - 40) throw new VaultException("The emergency package is damaged.");
            Dictionary<string, object> h; try { h = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(b, 12, hl)); } catch { throw new VaultException("The emergency package is damaged."); }
            return new Info { Id = h["id"] as string, From = h["from"] as string, FromFingerprint = h["fromFp"] as string, To = h["to"] as string, ToId = h["toId"] as string, Vaults = h["vaults"] as string,
                Made = DateTime.Parse(h["made"] as string, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), NeedsCode = (bool)h["needsCode"], Items = Convert.ToInt32(h["items"]) };
        }
        // opens a package with the contact's own member key (and the owner's recovery code if it needs one)
        public static VaultData Open(byte[] b, VaultData contact, string recoveryCode, out Info info)
        {
            info = Peek(b); int hl = BitConverter.ToInt32(b, 8);
            var h = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(b, 12, hl));
            if (contact.MemberPub == null || Ecies.MemberId(contact.MemberPub) != info.ToId) throw new VaultException("This package is for " + info.To + "'s Hallow Keys, not yours.");
            byte[] codeKey = null;
            if (info.NeedsCode)
            {
                if (string.IsNullOrWhiteSpace(recoveryCode)) throw new VaultException("This package also needs " + info.From + "'s recovery code.");
                int iter = Convert.ToInt32(h["codeIter"]); if (iter < 100000 || iter > 10000000) throw new VaultException("The emergency package is damaged.");
                var cb = Vault.RecoveryBytes(recoveryCode); try { codeKey = Kdf.Derive(cb, Passkeys.FromB64u(h["codeSalt"] as string), iter); } finally { Array.Clear(cb, 0, cb.Length); }
            }
            byte[] key;
            try { key = Ecies.Open(contact.MemberKey, contact.MemberPub, Passkeys.FromB64u(h["wrap"] as string), SealInfo(info.Id, info.ToId), codeKey); }
            catch { throw new VaultException(info.NeedsCode ? "Wrong recovery code, or the package was changed." : "The package was changed or damaged - it doesn't open."); }
            finally { if (codeKey != null) Array.Clear(codeKey, 0, codeKey.Length); }
            byte[] body;
            try { body = Gcm.DecryptRaw(key, b.Skip(12 + hl).Take(12).ToArray(), b.Skip(24 + hl).Take(b.Length - 24 - hl - 16).ToArray(), b.Skip(b.Length - 16).ToArray(), Magic.Concat(b.Skip(12).Take(hl)).ToArray()); }
            catch { throw new VaultException("The package was changed or damaged - it doesn't open."); }
            finally { Array.Clear(key, 0, key.Length); }
            VaultData d; using (var ms = new MemoryStream(body)) d = (VaultData)new DataContractJsonSerializer(typeof(VaultData)).ReadObject(ms);
            Array.Clear(body, 0, body.Length);
            return d;
        }
        // puts a package's items into the contact's vault as a new vault "From <name>" (new ids, so nothing collides or syncs back)
        public static VaultInfo Import(VaultData into, VaultData package, Info info, string dev)
        {
            string why; var v = Vaults.Create(into, "From " + Members.CleanName(info.From), "user-shield", "#E0A030", dev, out why);
            if (v == null) v = Vaults.Create(into, "From " + Members.CleanName(info.From) + " " + info.Made.ToLocalTime().ToString("yyyy-MM-dd"), "user-shield", "#E0A030", dev, out why);
            if (v == null) throw new VaultException(why ?? "Couldn't make a vault for the package.");
            foreach (var it in package.Items) { var c = ItemTypes.Clone(it, true); c.Sync = null; Vaults.Put(c, v.Id); c.Tags.Add("emergency"); into.Items.Add(c); }
            return v;
        }
    }
}
