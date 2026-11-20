// Hallow Keys 2.4 - "Sharing (no server)", the parts without a window (the window's parts are in Features24UI.cs):
//  - Member identity: each vault gets an ECDH P-256 key pair (CNG). The private key lives only inside the encrypted vault; the public
//    half goes out as a "member card" (a short code or a .hkmember file) with a fingerprint both people compare (read it out by phone).
//  - Sealing to a member: ECDH (an ephemeral P-256 key and the member's key, CNG) -> SHA-256 -> HKDF-SHA256 (salt = both public keys,
//    info names the purpose) -> AES-256-GCM. Only the holder of the member's private key can open it.
//  - Shared vaults: one file (shared.hkshared) in a folder the members share (OneDrive, Dropbox, a NAS...). A random content key encrypts
//    the items (AES-256-GCM, the whole header as associated data, so a changed member list or a changed byte fails); the content key is
//    sealed to every member. Removing a member makes a new content key ("epoch") for the members left - the removed member can't read
//    anything written after that, though they may have kept copies of what they could already see (the app says so). A file with an
//    older epoch than one this vault has already seen is refused (a removed member can't roll the folder back to a key they know).
//    Members' copies written at the same time (cloud conflict files) are merged item by item with 2.0's rules (version vectors, conflict
//    copies, tombstones; an edit beats a delete).
//  - Item share files (.hkshare): one item encrypted with its own passphrase (Argon2id + AES-256-GCM) and an expiry date. The expiry is
//    in the authenticated header and Hallow Keys refuses an expired file - but it can't make a copy forget itself: anyone who keeps the
//    file and the passphrase could read it with other software. The app says so.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    [DataContract] public class SharedRef : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = "", Name = "", Folder = "";
        [DataMember] public int Epoch;          // the highest epoch this vault has seen (older files are refused)
        [DataMember(EmitDefaultValue = false)] public byte[] Key;   // that epoch's content key (inside the encrypted vault only): a file with the same epoch must use it, the next epoch must prove it knew it
    }
    public class MemberCard
    {
        public string Name = ""; public byte[] Pub;
        public string Id { get { return Ecies.MemberId(Pub); } }
        public string Fingerprint { get { return Ecies.Fingerprint(Pub); } }
    }

    // ---- ECDH P-256 sealing (CNG)
    public static class Ecies
    {
        static readonly byte[] PubMagic = { 0x45, 0x43, 0x4B, 0x31 }, PrivMagic = { 0x45, 0x43, 0x4B, 0x32 };   // ECK1 / ECK2 (ECDH P-256), cbKey 32
        public static void NewKey(out byte[] d, out byte[] pub)
        {
            var p = new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport, KeyUsage = CngKeyUsages.KeyAgreement };
            using (var k = CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, p))
            {
                var blob = k.Export(CngKeyBlobFormat.EccPrivateBlob);
                try { pub = new byte[65]; pub[0] = 4; Buffer.BlockCopy(blob, 8, pub, 1, 64); d = new byte[32]; Buffer.BlockCopy(blob, 72, d, 0, 32); }
                finally { Array.Clear(blob, 0, blob.Length); }
            }
        }
        static CngKey PrivKey(byte[] d, byte[] pub)
        {
            var blob = new byte[104]; Buffer.BlockCopy(PrivMagic, 0, blob, 0, 4); blob[4] = 32; Buffer.BlockCopy(pub, 1, blob, 8, 64); Buffer.BlockCopy(d, 0, blob, 72, 32);
            try { return CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob); } finally { Array.Clear(blob, 0, blob.Length); }
        }
        static CngKey PubKey(byte[] pub)
        {
            if (pub == null || pub.Length != 65 || pub[0] != 4) throw new CryptographicException("not a P-256 public key");
            var blob = new byte[72]; Buffer.BlockCopy(PubMagic, 0, blob, 0, 4); blob[4] = 32; Buffer.BlockCopy(pub, 1, blob, 8, 64);
            return CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob);   // CNG checks the point is on the curve
        }
        // SHA-256 of the ECDH shared secret (CNG's hash KDF)
        static byte[] Agree(byte[] d, byte[] pub, byte[] other)
        {
            using (var k = PrivKey(d, pub)) using (var e = new ECDiffieHellmanCng(k)) using (var o = PubKey(other))
            { e.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash; e.HashAlgorithm = CngAlgorithm.Sha256; return e.DeriveKeyMaterial(o); }
        }
        // ephemeral pub (65) | nonce (12) | ciphertext | tag (16). extra: another secret mixed into the key (2.5: a recovery code)
        public static byte[] Seal(byte[] recipientPub, byte[] plain, string info, byte[] extra = null)
        {
            byte[] ed, ep; NewKey(out ed, out ep);
            var z = Agree(ed, ep, recipientPub); Array.Clear(ed, 0, ed.Length);
            var key = Hkdf.Derive(extra == null ? z : z.Concat(extra).ToArray(), ep.Concat(recipientPub).ToArray(), Encoding.UTF8.GetBytes("HallowKeys seal v1|" + info), 32);
            Array.Clear(z, 0, z.Length);
            try { var nonce = Rng.Bytes(12); byte[] c, t; Gcm.EncryptRaw(key, nonce, plain, ep, out c, out t); return ep.Concat(nonce).Concat(c).Concat(t).ToArray(); }
            finally { Array.Clear(key, 0, key.Length); }
        }
        public static byte[] Open(byte[] d, byte[] myPub, byte[] sealedBox, string info, byte[] extra = null)
        {
            if (sealedBox == null || sealedBox.Length < 65 + 12 + 16) throw new CryptographicException("too short");
            var ep = sealedBox.Take(65).ToArray(); var nonce = sealedBox.Skip(65).Take(12).ToArray();
            var c = sealedBox.Skip(77).Take(sealedBox.Length - 77 - 16).ToArray(); var t = sealedBox.Skip(sealedBox.Length - 16).ToArray();
            var z = Agree(d, myPub, ep);
            var key = Hkdf.Derive(extra == null ? z : z.Concat(extra).ToArray(), ep.Concat(myPub).ToArray(), Encoding.UTF8.GetBytes("HallowKeys seal v1|" + info), 32);
            Array.Clear(z, 0, z.Length);
            try { return Gcm.DecryptRaw(key, nonce, c, t, ep); } finally { Array.Clear(key, 0, key.Length); }
        }
        public static string MemberId(byte[] pub) { using (var h = SHA256.Create()) return Passkeys.B64u(h.ComputeHash(pub).Take(9).ToArray()); }
        // 20 base32 characters of SHA-256(public key) in groups of 4 - read it out to the other person
        public static string Fingerprint(byte[] pub)
        {
            const string A = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; byte[] h; using (var s = SHA256.Create()) h = s.ComputeHash(pub);
            var sb = new StringBuilder(); int bits = 0, acc = 0, n = 0;
            foreach (var b in h) { acc = acc << 8 | b; bits += 8; while (bits >= 5 && n < 20) { bits -= 5; sb.Append(A[(acc >> bits) & 31]); n++; if (n % 4 == 0 && n < 20) sb.Append('-'); } if (n >= 20) break; }
            return sb.ToString();
        }
    }

    // ---- your member identity and member cards
    public static class Members
    {
        public static void Ensure(VaultData d, string defaultName)
        {
            if (d.MemberKey != null && d.MemberPub != null) return;
            byte[] k, p; Ecies.NewKey(out k, out p); d.MemberKey = k; d.MemberPub = p;
            if (string.IsNullOrEmpty(d.MemberName)) d.MemberName = CleanName(defaultName);
        }
        public static string CleanName(string s) { s = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim(); return s.Length == 0 ? "Someone" : s.Length > 40 ? s.Substring(0, 40) : s; }
        public static MemberCard Mine(VaultData d) { return d.MemberPub == null ? null : new MemberCard { Name = d.MemberName ?? "", Pub = d.MemberPub }; }
        // "HKM1-" + base64url(name length | name | public key) + "-" + 4 check characters
        public static string Code(MemberCard c)
        {
            var name = Encoding.UTF8.GetBytes(CleanName(c.Name)); var body = new[] { (byte)name.Length }.Concat(name).Concat(c.Pub).ToArray();
            return "HKM1-" + Passkeys.B64u(body) + "." + Check(body);
        }
        static string Check(byte[] body) { using (var h = SHA256.Create()) return Passkeys.B64u(h.ComputeHash(body).Take(3).ToArray()); }
        public static MemberCard Parse(string code, out string why)
        {
            why = null; code = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (!code.StartsWith("HKM1-")) { why = "That isn't a Hallow Keys member card (it starts with HKM1-)."; return null; }
            var dot = code.LastIndexOf('.'); if (dot < 6) { why = "The member card is cut short."; return null; }
            var body = Passkeys.FromB64u(code.Substring(5, dot - 5));
            if (body == null || body.Length < 1 + 65 || Check(body) != code.Substring(dot + 1)) { why = "The member card has a typo (its check characters don't match)."; return null; }
            int n = body[0]; if (body.Length != 1 + n + 65) { why = "The member card is damaged."; return null; }
            var pub = body.Skip(1 + n).ToArray();
            try { using (var t = new ECDiffieHellmanCng(CngKey.Import(new byte[] { 0x45, 0x43, 0x4B, 0x31, 32, 0, 0, 0 }.Concat(pub.Skip(1)).ToArray(), CngKeyBlobFormat.EccPublicBlob))) { } }
            catch { why = "The member card's key isn't valid."; return null; }
            if (pub[0] != 4) { why = "The member card's key isn't valid."; return null; }
            return new MemberCard { Name = CleanName(Encoding.UTF8.GetString(body, 1, n)), Pub = pub };
        }
    }

    // ---- shared vaults
    public class SharedMember { public string Id = "", Name = ""; public byte[] Pub, Wrap; }
    public class SharedFile
    {
        public string Id = "", Name = ""; public int Epoch = 1; public List<SharedMember> Members = new List<SharedMember>();
        public List<string> Removed = new List<string>();   // "name|when" of members removed (shown to the others)
        public List<Dictionary<string, object>> Proofs = new List<Dictionary<string, object>>();   // per key change: { e, h = hash of the new key, m = HMAC(previous key, e | h) }
        public VaultData Data = new VaultData();
        public byte[] Key;   // the content key for this epoch (in memory only)
        public const string FileName = "shared.hkshared";
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKSHARE\0");
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024 };
        static string Info(string id, int epoch, string memberId) { return "shared-vault|" + id + "|" + epoch + "|" + memberId; }

        public static SharedFile Create(string name, MemberCard me)
        {
            var f = new SharedFile { Id = Guid.NewGuid().ToString("N"), Name = HallowKeys.Members.CleanName(name), Key = Rng.Bytes(32) };
            f.Members.Add(new SharedMember { Id = me.Id, Name = me.Name, Pub = me.Pub });
            return f;
        }
        public bool Has(string memberId) { return Members.Any(m => m.Id == memberId); }
        public void AddMember(MemberCard c) { if (Has(c.Id)) return; Members.Add(new SharedMember { Id = c.Id, Name = c.Name, Pub = c.Pub }); }
        // a new content key and epoch for the members left
        public void RemoveMember(string memberId, DateTime when)
        {
            var m = Members.FirstOrDefault(x => x.Id == memberId); if (m == null) return;
            Members.Remove(m); Removed.Add(m.Name + "|" + when.ToString("yyyy-MM-dd"));
            var prev = Key; Key = Rng.Bytes(32); Epoch++;
            var hk = KeyHash(Key); Proofs.Add(new Dictionary<string, object> { { "e", Epoch }, { "h", hk }, { "m", Mac(prev, Id, Epoch, hk) } });
            if (prev != null) Array.Clear(prev, 0, prev.Length);
        }
        DataContractJsonSerializer Ser() { return new DataContractJsonSerializer(typeof(VaultData)); }
        public static string KeyHash(byte[] k) { using (var h = SHA256.Create()) return Passkeys.B64u(h.ComputeHash(Encoding.ASCII.GetBytes("HallowKeys shared key id").Concat(k).ToArray()).Take(16).ToArray()); }
        static string Mac(byte[] prevKey, string id, int epoch, string newHash) { using (var h = new HMACSHA256(prevKey)) return Passkeys.B64u(h.ComputeHash(Encoding.UTF8.GetBytes("rekey|" + id + "|" + epoch + "|" + newHash))); }
        // is this file a genuine continuation of what this vault has seen? null = yes, else why not
        public string Continues(SharedRef seen)
        {
            if (seen == null || seen.Key == null) return null;   // first open (joining): you check the members' fingerprints
            if (Epoch < seen.Epoch) return "older than one you've already seen (key " + Epoch + " < " + seen.Epoch + ") - someone may have put back an old copy";
            if (Epoch == seen.Epoch) return KeyHash(Key) == KeyHash(seen.Key) ? null : "not written with this shared vault's key - it didn't come from a member";
            var p = Proofs.FirstOrDefault(x => Convert.ToInt32(x["e"]) == seen.Epoch + 1);
            if (p == null || (p["m"] as string) != Mac(seen.Key, Id, seen.Epoch + 1, p["h"] as string)) return "its new key wasn't made by a member who had the old one";
            if (Epoch == seen.Epoch + 1 && (p["h"] as string) != KeyHash(Key)) return "its key doesn't match its own key change";
            return null;
        }
        public byte[] ToBytes()
        {
            foreach (var m in Members) m.Wrap = Ecies.Seal(m.Pub, Key, Info(Id, Epoch, m.Id));
            var header = Encoding.UTF8.GetBytes(js.Serialize(new Dictionary<string, object> { { "v", 1 }, { "id", Id }, { "name", Name }, { "epoch", Epoch }, { "removed", Removed }, { "proofs", Proofs },
                { "members", Members.Select(m => (object)new Dictionary<string, object> { { "id", m.Id }, { "name", m.Name }, { "pub", Passkeys.B64u(m.Pub) }, { "wrap", Passkeys.B64u(m.Wrap) } }).ToList() } }));
            byte[] body; using (var ms = new MemoryStream()) { Ser().WriteObject(ms, new VaultData { Items = Data.Items, Trash = Data.Trash, Tombstones = Data.Tombstones }); body = ms.ToArray(); }
            try
            {
                var nonce = Rng.Bytes(12); byte[] c, t; var aad = Magic.Concat(header).ToArray(); Gcm.EncryptRaw(Key, nonce, body, aad, out c, out t);
                var o = new MemoryStream(); o.Write(Magic, 0, 8); o.Write(BitConverter.GetBytes(header.Length), 0, 4); o.Write(header, 0, header.Length); o.Write(nonce, 0, 12); o.Write(c, 0, c.Length); o.Write(t, 0, 16);
                return o.ToArray();
            }
            finally { Array.Clear(body, 0, body.Length); }
        }
        // opens a shared file as this member; throws VaultException with a plain message
        public static SharedFile FromBytes(byte[] b, byte[] myKey, byte[] myPub)
        {
            if (b == null || b.Length < 12 + 28 || !b.Take(8).SequenceEqual(Magic)) throw new VaultException("That isn't a Hallow Keys shared vault.");
            int hl = BitConverter.ToInt32(b, 8); if (hl <= 0 || hl > b.Length - 12 - 28) throw new VaultException("The shared vault file is damaged.");
            var header = b.Skip(12).Take(hl).ToArray(); Dictionary<string, object> h;
            try { h = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(header)); } catch { throw new VaultException("The shared vault file is damaged."); }
            var f = new SharedFile { Id = h["id"] as string, Name = h["name"] as string, Epoch = Convert.ToInt32(h["epoch"]) };
            object pr; if (h.TryGetValue("proofs", out pr) && pr is System.Collections.ArrayList) f.Proofs = ((System.Collections.ArrayList)pr).Cast<Dictionary<string, object>>().ToList();
            object rm; if (h.TryGetValue("removed", out rm) && rm is System.Collections.ArrayList) f.Removed = ((System.Collections.ArrayList)rm).Cast<object>().Select(x => x.ToString()).ToList();
            foreach (Dictionary<string, object> m in (System.Collections.ArrayList)h["members"]) f.Members.Add(new SharedMember { Id = m["id"] as string, Name = m["name"] as string, Pub = Passkeys.FromB64u(m["pub"] as string), Wrap = Passkeys.FromB64u(m["wrap"] as string) });
            var meId = Ecies.MemberId(myPub); var mine = f.Members.FirstOrDefault(m => m.Id == meId && m.Pub.SequenceEqual(myPub));
            if (mine == null) throw new VaultException("You aren't a member of this shared vault (or you were removed).");
            try { f.Key = Ecies.Open(myKey, myPub, mine.Wrap, Info(f.Id, f.Epoch, meId)); } catch { throw new VaultException("This shared vault's key for you doesn't open - the file was changed or damaged."); }
            var nonce = b.Skip(12 + hl).Take(12).ToArray(); var rest = b.Length - 12 - hl - 12 - 16;
            byte[] body;
            try { body = Gcm.DecryptRaw(f.Key, nonce, b.Skip(12 + hl + 12).Take(rest).ToArray(), b.Skip(b.Length - 16).ToArray(), Magic.Concat(header).ToArray()); }
            catch { throw new VaultException("The shared vault file was changed or damaged (it doesn't pass its check)."); }
            using (var ms = new MemoryStream(body)) f.Data = (VaultData)f.Ser().ReadObject(ms);
            Array.Clear(body, 0, body.Length);
            return f;
        }
    }

    // reading, merging and writing a shared vault's folder
    public static class SharedVaults
    {
        public static string MainPath(string folder) { return Path.Combine(folder, SharedFile.FileName); }
        // the main file and the cloud services' conflict copies of it ("shared (1).hkshared", "shared-PC.hkshared", "shared.hkshared.conflict...")
        public static List<string> Copies(string folder)
        {
            if (!Directory.Exists(folder)) return new List<string>();
            return Directory.GetFiles(folder).Where(p => { var n = Path.GetFileName(p).ToLowerInvariant(); return n.StartsWith("shared") && n.Contains(".hkshared") && !n.EndsWith(".tmp"); })
                .OrderBy(p => p == MainPath(folder) ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        // opens the folder: every copy this member can open (newest epoch wins the member list), items merged; refuses an epoch older than seen
        public static SharedFile Open(string folder, VaultData mine, SharedRef seen, out List<string> merged, out string note)
        {
            merged = new List<string>(); note = null; SharedFile best = null; string older = null; var all = new List<SharedFile>(); var problems = new List<string>();
            foreach (var p in Copies(folder))
            {
                try
                {
                    var f = SharedFile.FromBytes(File.ReadAllBytes(p), mine.MemberKey, mine.MemberPub); if (seen != null && seen.Id.Length > 0 && f.Id != seen.Id) continue;
                    var bad = f.Continues(seen); if (bad != null) { problems.Add(Path.GetFileName(p) + ": " + bad); if (bad.StartsWith("older")) older = bad; continue; }
                    all.Add(f); if (p != MainPath(folder)) merged.Add(p); if (best == null || f.Epoch > best.Epoch) best = f;
                }
                catch (VaultException e) { problems.Add(Path.GetFileName(p) + ": " + e.Message); }
                catch (IOException e) { problems.Add(Path.GetFileName(p) + ": " + e.Message); }
            }
            if (best == null) throw new VaultException(older != null ? "This shared vault's file is " + older + ". Nothing was changed." : problems.Count > 0 ? "Not opened - " + problems[0] : "No shared vault in that folder.");
            // newer epochs: every copy must be at the newest epoch (an older member list's copy is merged only if it's the same key)
            all = all.Where(x => x.Epoch == best.Epoch && SharedFile.KeyHash(x.Key) == SharedFile.KeyHash(best.Key)).ToList();
            var dev = Device.Id;
            foreach (var f in all.Where(x => x != best)) Merge(best.Data, f.Data, dev);
            if (problems.Count > 0) note = string.Join("; ", problems);
            return best;
        }
        // writes the shared vault (after merging what's in the folder now): temp file, then swap in; conflict copies that were merged are moved out
        public static void Save(string folder, SharedFile f, VaultData mine, List<string> mergedCopies)
        {
            Stamp(f.Data, Device.Id);
            var path = MainPath(folder);
            if (File.Exists(path))
            {
                try
                {
                    var now = SharedFile.FromBytes(File.ReadAllBytes(path), mine.MemberKey, mine.MemberPub);
                    if (now.Id == f.Id && now.Epoch == f.Epoch && SharedFile.KeyHash(now.Key) == SharedFile.KeyHash(f.Key)) Merge(f.Data, now.Data, Device.Id);
                    else if (now.Id == f.Id && now.Epoch > f.Epoch) throw new VaultException("Someone changed this shared vault's members meanwhile - open it again.");
                }
                catch (VaultException e) { if (e.Message.StartsWith("Someone")) throw; }   // can't read it any more (removed?): written over only by a current member's file
            }
            var tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllBytes(tmp, f.ToBytes());
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            if (mergedCopies != null)
            {
                var outDir = Path.Combine(KeysSettings.Dir, "shared-merged"); Directory.CreateDirectory(outDir);
                foreach (var c in mergedCopies) try { File.Move(c, Path.Combine(outDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Path.GetFileName(c))); } catch { }
            }
        }

        // ---- 2.0's item rules on a shared vault's items: revisions by version vector, conflict copies, tombstones, an edit beats a delete
        public static void Stamp(VaultData d, string dev)
        {
            var now = DateTime.UtcNow.Ticks;
            foreach (var it in d.Items.Concat(d.Trash))
            {
                var h = Vault.ItemHash(it);
                if (it.Sync == null || string.IsNullOrEmpty(it.Sync.Vv)) it.Sync = new SyncMeta { Vv = VersionVector.Bump("", dev), Hash = h, Time = now, Dev = dev };
                else if (it.Sync.Hash != h) it.Sync = new SyncMeta { Vv = VersionVector.Bump(it.Sync.Vv, dev), Hash = h, Time = now, Dev = dev };
            }
        }
        public static void Delete(VaultData d, Item it, string dev)
        {
            d.Items.Remove(it); d.Trash.Remove(it);
            d.Tombstones.Add(new Tombstone { Id = it.Id, Vv = VersionVector.Bump(it.Sync == null ? "" : it.Sync.Vv, dev), Time = DateTime.UtcNow.Ticks, Dev = dev });
        }
        public static void Merge(VaultData local, VaultData remote, string dev)
        {
            Stamp(local, dev);
            foreach (var r in remote.Items.Concat(remote.Trash)) { if (r.Sync == null) r.Sync = new SyncMeta { Vv = "" }; r.Sync.Hash = Vault.ItemHash(r); }
            Func<VaultData, Dictionary<string, Item>> byId = x => { var m = new Dictionary<string, Item>(); foreach (var i in x.Items.Concat(x.Trash)) if (!m.ContainsKey(i.Id)) m[i.Id] = i; return m; };
            var L = byId(local); var R = byId(remote);
            var LT = local.Tombstones.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First()); var RT = remote.Tombstones.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var items = new List<Item>(); var tombs = new List<Tombstone>(); var copies = new List<Item>();
            foreach (var id in L.Keys.Concat(R.Keys).Concat(LT.Keys).Concat(RT.Keys).Distinct().ToList())
            {
                Item l, r; Tombstone lt, rt; L.TryGetValue(id, out l); R.TryGetValue(id, out r); LT.TryGetValue(id, out lt); RT.TryGetValue(id, out rt);
                var tomb = lt == null ? rt : rt == null ? lt : new Tombstone { Id = id, Vv = VersionVector.Max(lt.Vv, rt.Vv), Time = Math.Max(lt.Time, rt.Time), Dev = lt.Time >= rt.Time ? lt.Dev : rt.Dev };
                Item item;
                if (l == null) item = r; else if (r == null) item = l;
                else
                {
                    int c = VersionVector.Compare(l.Sync.Vv, r.Sync.Vv);
                    if (c == 1) item = l; else if (c == -1) item = r;
                    else if (l.Sync.Hash == r.Sync.Hash) { item = l; l.Sync.Vv = VersionVector.Max(l.Sync.Vv, r.Sync.Vv); }
                    else
                    {
                        bool lWins = l.Sync.Time != r.Sync.Time ? l.Sync.Time > r.Sync.Time : string.CompareOrdinal(l.Sync.Hash, r.Sync.Hash) >= 0;
                        item = lWins ? l : r; var lose = lWins ? r : l; item.Sync = item.Sync.Clone(); item.Sync.Vv = VersionVector.Max(l.Sync.Vv, r.Sync.Vv);
                        if (c == 2)
                        {
                            var cp = ItemTypes.Clone(lose, false);
                            using (var sha = SHA256.Create()) cp.Id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("conflict|" + id + "|" + lose.Sync.Hash)), 0, 16).Replace("-", "").ToLowerInvariant();
                            cp.Title = (cp.Title.Length > 0 ? cp.Title : "Untitled") + " (conflict copy)"; cp.Sync = new SyncMeta { Vv = lose.Sync.Vv, Time = lose.Sync.Time, Dev = lose.Sync.Dev }; cp.Sync.Hash = Vault.ItemHash(cp);
                            copies.Add(cp);
                        }
                    }
                }
                if (item != null && tomb != null)
                {
                    int c = VersionVector.Compare(tomb.Vv, item.Sync.Vv);
                    if (c == 0 || c == 1) item = null; else { if (c == 2) { item.Sync = item.Sync.Clone(); item.Sync.Vv = VersionVector.Max(item.Sync.Vv, tomb.Vv); } tomb = null; }
                }
                if (item != null) items.Add(item);
                if (tomb != null) tombs.Add(tomb);
            }
            foreach (var cp in copies) if (!items.Any(i => i.Id == cp.Id) && !tombs.Any(t => t.Id == cp.Id)) items.Add(cp);
            local.Items = items.Where(i => i.Deleted == null).ToList(); local.Trash = items.Where(i => i.Deleted != null).ToList(); local.Tombstones = tombs.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        }
        // a copy of one of your items to put in a shared vault: no vault id, no history, no usage, never a passkey's key
        public static Item CopyIn(Item it)
        {
            var c = ItemTypes.Clone(it, true); c.VaultId = null; c.Sync = null; c.Title = it.Title; return c;
        }
    }

    // ---- one item as a file with its own passphrase and an expiry
    public static class ItemShare
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKITEM1\0");
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        public const int ArgonM = 65536, ArgonT = 3, ArgonP = 4;   // 64 MiB: quicker to open on a friend's slower PC; the passphrase is long and generated
        public static bool CanShare(Item it) { return it != null && it.Type != "passkey"; }
        public static byte[] Make(Item it, string passphrase, DateTime expiresUtc, string from)
        {
            if (!CanShare(it)) throw new VaultException("Passkeys can't be shared - their keys stay in your vault.");
            var c = ItemTypes.Clone(it, true); c.VaultId = null; c.Sync = null; c.Apps.Clear();   // no history (Clone(asNew) drops it), no vault, no apps
            var salt = Rng.Bytes(16);
            var header = Encoding.UTF8.GetBytes(js.Serialize(new Dictionary<string, object> { { "v", 1 }, { "kdf", "argon2id" }, { "m", ArgonM }, { "t", ArgonT }, { "p", ArgonP }, { "salt", Passkeys.B64u(salt) },
                { "expires", expiresUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") }, { "made", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }, { "from", Members.CleanName(from) } }));
            byte[] body; using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(Item)).WriteObject(ms, c); body = ms.ToArray(); }
            var pw = Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormKC));
            var key = Argon2.Hash(2, pw, salt, null, null, ArgonM, ArgonT, ArgonP, 32); Array.Clear(pw, 0, pw.Length);
            try
            {
                var nonce = Rng.Bytes(12); byte[] ct, tag; Gcm.EncryptRaw(key, nonce, body, Magic.Concat(header).ToArray(), out ct, out tag);
                var o = new MemoryStream(); o.Write(Magic, 0, 8); o.Write(BitConverter.GetBytes(header.Length), 0, 4); o.Write(header, 0, header.Length); o.Write(nonce, 0, 12); o.Write(ct, 0, ct.Length); o.Write(tag, 0, 16);
                return o.ToArray();
            }
            finally { Array.Clear(key, 0, key.Length); Array.Clear(body, 0, body.Length); }
        }
        public class Header { public DateTime Expires, Made; public string From = ""; }
        public static Header Peek(byte[] b)
        {
            if (b == null || b.Length < 40 || !b.Take(8).SequenceEqual(Magic)) throw new VaultException("That isn't a Hallow Keys shared item.");
            int hl = BitConverter.ToInt32(b, 8); if (hl <= 0 || hl > b.Length - 40) throw new VaultException("The shared item file is damaged.");
            var h = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(b, 12, hl));
            return new Header { Expires = DateTime.Parse(h["expires"] as string, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), Made = DateTime.Parse(h["made"] as string, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal), From = h.ContainsKey("from") ? h["from"] as string : "" };
        }
        public static Item Open(byte[] b, string passphrase, DateTime nowUtc, out Header head)
        {
            head = Peek(b); int hl = BitConverter.ToInt32(b, 8);
            var h = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(b, 12, hl));
            int m = Convert.ToInt32(h["m"]), t = Convert.ToInt32(h["t"]), p = Convert.ToInt32(h["p"]);
            if (m < 8192 || m > 1048576 || t < 1 || t > 10 || p < 1 || p > 16) throw new VaultException("The shared item file is damaged.");
            var pw = Encoding.UTF8.GetBytes((passphrase ?? "").Normalize(NormalizationForm.FormKC));
            var key = Argon2.Hash(2, pw, Passkeys.FromB64u(h["salt"] as string), null, null, m, t, p, 32); Array.Clear(pw, 0, pw.Length);
            byte[] body;
            try { body = Gcm.DecryptRaw(key, b.Skip(12 + hl).Take(12).ToArray(), b.Skip(12 + hl + 12).Take(b.Length - 12 - hl - 28).ToArray(), b.Skip(b.Length - 16).ToArray(), Magic.Concat(b.Skip(12).Take(hl)).ToArray()); }
            catch { throw new VaultException("Wrong passphrase, or the file was changed."); }
            finally { Array.Clear(key, 0, key.Length); }
            if (nowUtc > head.Expires) { Array.Clear(body, 0, body.Length); throw new VaultException("This shared item expired on " + head.Expires.ToLocalTime().ToString("d MMM yyyy HH:mm") + " - ask for a new one."); }
            Item it; using (var ms = new MemoryStream(body)) it = (Item)new DataContractJsonSerializer(typeof(Item)).ReadObject(ms);
            Array.Clear(body, 0, body.Length);
            it.Id = Guid.NewGuid().ToString("N"); it.Sync = null; it.VaultId = null;
            if (it.Type == "passkey") throw new VaultException("Passkeys can't be shared.");
            return it;
        }
    }
}
