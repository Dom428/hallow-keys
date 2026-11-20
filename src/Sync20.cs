// Hallow Keys 2.0 - sync between your PCs through a folder you choose (OneDrive, Google Drive, Dropbox, a NAS, Syncthing...), no Hallow server.
// Every item carries a revision: a version vector (how many changes each PC has made to it), a hash of its content, when and on which PC.
// Deleting an item for good leaves a tombstone. Merging two copies of the vault goes item by item:
//   - one side has seen everything the other has -> the newer one is kept
//   - both changed the same item since they last met -> a real conflict: the later change stays as the item and the other is kept next to
//     it as "(conflict copy from PC-NAME)" - nothing is thrown away; both PCs make the same copy with the same id, so it appears once
//   - an edit on one PC and a delete on the other -> the edit wins (the item is kept)
// The copy in the sync folder is always written whole (temp file, then replace), retried when a cloud client holds the file, and never
// written while the copy there can't be read (a cloud client still writing it). Cloud clients' conflict files ("vault (1).hkvault",
// "vault-DESKTOP-ABC.hkvault", Dropbox's "conflicted copy", Syncthing's ".sync-conflict-") are merged in and then moved to this PC's
// data folder (sync-merged). This PC keeps its own copy (vault.hkvault, .bak, and encrypted snapshots in versions\) - it works offline.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowKeys
{
    [DataContract] public class SyncMeta : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Vv = "";       // version vector: "deviceid:count,deviceid:count" (sorted)
        [DataMember] public string Hash = "";     // SHA-256 of the item's content (without this block)
        [DataMember] public long Time;            // UTC ticks of the change
        [DataMember] public string Dev = "";      // the PC that made it (random id)
        [DataMember] public string DevName = "";  // and its name, for "conflict copy from ..."
        public SyncMeta Clone() { return new SyncMeta { Vv = Vv, Hash = Hash, Time = Time, Dev = Dev, DevName = DevName }; }
        [OnDeserialized] void Fix(StreamingContext c) { if (Vv == null) Vv = ""; if (Hash == null) Hash = ""; if (Dev == null) Dev = ""; if (DevName == null) DevName = ""; }
    }
    [DataContract] public class Tombstone : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = "";
        [DataMember] public string Vv = "";
        [DataMember] public long Time;
        [DataMember] public string Dev = "";
        [OnDeserialized] void Fix(StreamingContext c) { if (Id == null) Id = ""; if (Vv == null) Vv = ""; if (Dev == null) Dev = ""; }
    }

    // ---- version vectors
    public static class VersionVector
    {
        public static SortedDictionary<string, long> Parse(string s)
        {
            var d = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var part in (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = part.LastIndexOf(':'); long n;
                if (c > 0 && long.TryParse(part.Substring(c + 1), out n) && n > 0) { var k = part.Substring(0, c); long old; d[k] = d.TryGetValue(k, out old) ? Math.Max(old, n) : n; }
            }
            return d;
        }
        public static string Format(IDictionary<string, long> d) { return string.Join(",", d.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value)); }
        public static string Bump(string vv, string dev) { var d = Parse(vv); long n; d[dev] = (d.TryGetValue(dev, out n) ? n : 0) + 1; return Format(d); }
        public static string Max(string a, string b)
        {
            var x = Parse(a); foreach (var kv in Parse(b)) { long n; x[kv.Key] = x.TryGetValue(kv.Key, out n) ? Math.Max(n, kv.Value) : kv.Value; }
            return Format(x);
        }
        // 0 = the same, 1 = a has seen everything b has and more, -1 = the other way round, 2 = both have changes the other hasn't seen
        public static int Compare(string a, string b)
        {
            var x = Parse(a); var y = Parse(b); bool aMore = false, bMore = false;
            foreach (var k in x.Keys.Union(y.Keys))
            {
                long p, q; x.TryGetValue(k, out p); y.TryGetValue(k, out q);
                if (p > q) aMore = true; else if (q > p) bMore = true;
            }
            return aMore && bMore ? 2 : aMore ? 1 : bMore ? -1 : 0;
        }
        public static long Total(string vv) { return Parse(vv).Values.Sum(); }
    }

    public partial class Vault
    {
        // what this PC last wrote or merged for each item: a change to an item since then is a local edit (its revision goes up on the next save)
        Dictionary<string, SyncMeta> known = new Dictionary<string, SyncMeta>();
        void LoadKnown()
        {
            known = new Dictionary<string, SyncMeta>();
            foreach (var it in Data.Items.Concat(Data.Trash)) if (it.Sync != null && it.Sync.Hash.Length > 0 && !string.IsNullOrEmpty(it.Id) && !known.ContainsKey(it.Id)) known[it.Id] = it.Sync.Clone();
        }
        public Dictionary<string, SyncMeta> Known { get { return known; } }

        static readonly DataContractJsonSerializer itemSer = new DataContractJsonSerializer(typeof(Item));
        static readonly Regex dateOffset = new Regex(@"\\/Date\((-?\d+)[+-]\d{4}\)\\/", RegexOptions.Compiled);
        // the item's content hash - the same on every PC (dates are compared in UTC, whatever the time zone)
        public static string ItemHash(Item it)
        {
            var s = it.Sync; it.Sync = null;
            try
            {
                string json;
                lock (itemSer) using (var ms = new MemoryStream()) { itemSer.WriteObject(ms, it); json = Encoding.UTF8.GetString(ms.ToArray()); }
                json = dateOffset.Replace(json, "\\/Date($1)\\/");
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json)), 0, 16).Replace("-", "").ToLowerInvariant();
            }
            finally { it.Sync = s; }
        }

        // brings every item's revision up to date: new items, edited items (content changed since last time), items deleted for good (tombstones)
        public void Stamp(string dev, string devName)
        {
            var now = DateTime.UtcNow.Ticks;
            var tombs = new HashSet<string>(Data.Tombstones.Select(t => t.Id));
            var seen = new HashSet<string>();
            foreach (var rec in Data.Items.Concat(Data.Trash).ToList())
            {
                // an id that's already used, or one that was deleted for good and comes back from a backup: it becomes a new item
                if (string.IsNullOrEmpty(rec.Id) || seen.Contains(rec.Id) || (!known.ContainsKey(rec.Id) && tombs.Contains(rec.Id))) { rec.Id = Guid.NewGuid().ToString("N"); rec.Sync = null; }
                seen.Add(rec.Id);
                var h = ItemHash(rec); SyncMeta k;
                if (!known.TryGetValue(rec.Id, out k)) rec.Sync = new SyncMeta { Vv = VersionVector.Bump("", dev), Hash = h, Time = now, Dev = dev, DevName = devName };
                else if (k.Hash != h) rec.Sync = new SyncMeta { Vv = VersionVector.Bump(k.Vv, dev), Hash = h, Time = now, Dev = dev, DevName = devName };
                else rec.Sync = k.Clone();
                known[rec.Id] = rec.Sync.Clone();
            }
            foreach (var id in known.Keys.Where(x => !seen.Contains(x)).ToList())
            {
                if (!tombs.Contains(id)) Data.Tombstones.Add(new Tombstone { Id = id, Vv = VersionVector.Bump(known[id].Vv, dev), Time = now, Dev = dev });
                known.Remove(id);
            }
            // tombstones older than two years are let go (a PC that was away longer than that could bring an item back - never lose it)
            Data.Tombstones.RemoveAll(t => t.Time > 0 && t.Time < now - TimeSpan.FromDays(730).Ticks);
        }
        // a restored older version: an item deleted for good comes back as a new revision that wins over the tombstone on every PC
        internal void Revive(string id)
        {
            var t = Data.Tombstones.FirstOrDefault(x => x.Id == id); if (t == null) return;
            Data.Tombstones.Remove(t); known[id] = new SyncMeta { Vv = t.Vv, Hash = "", Time = t.Time, Dev = t.Dev };
        }
    }

    public class MergeReport
    {
        public int Added, Updated, Removed, Conflicts, Kept;   // Kept: edits that won over a delete on another PC
        public bool KeysAdopted, LocalChanged, RemoteBehind;
        public int Total { get { return Added + Updated + Removed + Conflicts; } }
        public void Add(MergeReport o) { Added += o.Added; Updated += o.Updated; Removed += o.Removed; Conflicts += o.Conflicts; Kept += o.Kept; KeysAdopted |= o.KeysAdopted; LocalChanged |= o.LocalChanged; RemoteBehind |= o.RemoteBehind; }
    }

    public static class SyncMerge
    {
        // merges another copy of the same vault into this one (in memory). Both PCs get the same result whichever merges first.
        public static MergeReport Merge(Vault local, Vault remote, string dev, string devName)
        {
            var rep = new MergeReport();
            local.Stamp(dev, devName);
            foreach (var r in remote.Data.Items.Concat(remote.Data.Trash)) { if (r.Sync == null) r.Sync = new SyncMeta(); if (string.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N"); r.Sync.Hash = Vault.ItemHash(r); }
            var before = Digest(local.Data); var remoteDigest = Digest(remote.Data);
            var L = ById(local.Data); var R = ById(remote.Data);
            var LT = local.Data.Tombstones.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First()); var RT = remote.Data.Tombstones.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            var order = L.Keys.Concat(R.Keys).Concat(LT.Keys).Concat(RT.Keys).Distinct().ToList();
            var outItems = new Dictionary<string, Item>(); var outTombs = new Dictionary<string, Tombstone>(); var outOrder = new List<string>();
            var copies = new List<Item>();
            foreach (var id in order)
            {
                Item l, r; Tombstone lt, rt; L.TryGetValue(id, out l); R.TryGetValue(id, out r); LT.TryGetValue(id, out lt); RT.TryGetValue(id, out rt);
                var tomb = MergeTombs(lt, rt);
                var item = MergeItems(l, r, devName, copies, rep);
                Resolve(id, ref item, ref tomb, rep);
                if (item != null) { outItems[id] = item; outOrder.Add(id); }
                if (tomb != null) outTombs[id] = tomb;
            }
            foreach (var c in copies)
            {
                if (outItems.ContainsKey(c.Id)) continue;
                Tombstone t; outTombs.TryGetValue(c.Id, out t); var item = c;
                Resolve(c.Id, ref item, ref t, null);
                if (item != null) { outItems[c.Id] = item; outOrder.Add(c.Id); rep.Conflicts++; }
                if (t != null) outTombs[c.Id] = t; else outTombs.Remove(c.Id);
            }
            // counts against this PC's copy
            foreach (var id in outOrder) { Item l; if (!L.TryGetValue(id, out l)) { if (!copies.Any(c => c.Id == id)) rep.Added++; } else if (outItems[id] != l || l.Sync.Vv != outItems[id].Sync.Vv) { if (outItems[id] != l) rep.Updated++; } }
            foreach (var id in L.Keys) if (!outItems.ContainsKey(id)) rep.Removed++;
            // write back into the local vault
            local.Data.Items = outOrder.Select(id => outItems[id]).Where(i => i.Deleted == null).ToList();
            local.Data.Trash = outOrder.Select(id => outItems[id]).Where(i => i.Deleted != null).ToList();
            local.Data.Tombstones = outTombs.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
            var known = local.Known; known.Clear();
            foreach (var it in outItems.Values) known[it.Id] = it.Sync.Clone();
            // the vault's own lists: kept from both
            foreach (var f in remote.Data.Folders) if (!local.Data.Folders.Contains(f)) local.Data.Folders.Add(f);
            foreach (var t in remote.Data.Templates) if (!local.Data.Templates.Any(x => x.Id == t.Id)) local.Data.Templates.Add(t);
            var ids = new HashSet<string>(outItems.Keys);
            local.Data.LeakedIds = local.Data.LeakedIds.Union(remote.Data.LeakedIds).Where(ids.Contains).Distinct().ToList();
            foreach (var e in remote.Data.ExpiryNotified) if (!local.Data.ExpiryNotified.Contains(e)) local.Data.ExpiryNotified.Add(e);
            if (local.Data.PhoneKey == null && remote.Data.PhoneKey != null && remote.Data.PhoneSalt != null) { local.Data.PhoneKey = (byte[])remote.Data.PhoneKey.Clone(); local.Data.PhoneSalt = (byte[])remote.Data.PhoneSalt.Clone(); local.Data.PhoneIterations = remote.Data.PhoneIterations; }
            rep.KeysAdopted = local.AdoptKeysFrom(remote);
            var after = Digest(local.Data);
            rep.LocalChanged = after != before || rep.KeysAdopted;
            rep.RemoteBehind = after != remoteDigest || local.MasterTime > remote.MasterTime || local.RecoveryTime > remote.RecoveryTime;
            return rep;
        }
        static Dictionary<string, Item> ById(VaultData d)
        {
            var m = new Dictionary<string, Item>();
            foreach (var it in d.Items.Concat(d.Trash)) if (!m.ContainsKey(it.Id)) m[it.Id] = it;
            return m;
        }
        static Tombstone MergeTombs(Tombstone a, Tombstone b)
        {
            if (a == null) return b; if (b == null) return a;
            var w = a.Time >= b.Time ? a : b;
            return new Tombstone { Id = a.Id, Vv = VersionVector.Max(a.Vv, b.Vv), Time = w.Time, Dev = w.Dev };
        }
        // an item against a delete: deleted if the delete has seen this version; an edit the delete hadn't seen wins (the item is kept)
        static void Resolve(string id, ref Item item, ref Tombstone tomb, MergeReport rep)
        {
            if (item == null || tomb == null) return;
            int c = VersionVector.Compare(tomb.Vv, item.Sync.Vv);
            if (c == 0 || c == 1) { item = null; return; }
            if (c == 2) { item.Sync = item.Sync.Clone(); item.Sync.Vv = VersionVector.Max(item.Sync.Vv, tomb.Vv); if (rep != null) rep.Kept++; }
            tomb = null;
        }
        static Item MergeItems(Item l, Item r, string devName, List<Item> copies, MergeReport rep)
        {
            if (l == null) return r; if (r == null) return l;
            int c = VersionVector.Compare(l.Sync.Vv, r.Sync.Vv);
            if (c == 1) return l;
            if (c == -1) return r;
            if (l.Sync.Hash == r.Sync.Hash) { if (c == 0) return l; l.Sync = l.Sync.Clone(); l.Sync.Vv = VersionVector.Max(l.Sync.Vv, r.Sync.Vv); return l; }
            // a real conflict: the later change stays, the other is kept as a copy
            var lWins = Later(l.Sync, r.Sync);
            var win = lWins ? l : r; var lose = lWins ? r : l;
            win.Sync = win.Sync.Clone(); win.Sync.Vv = VersionVector.Max(l.Sync.Vv, r.Sync.Vv);
            if (c == 0) return win;   // same revision, different content: can't happen in normal use; the winner is picked the same way on every PC
            copies.Add(ConflictCopy(win.Id, lose));
            return win;
        }
        static bool Later(SyncMeta a, SyncMeta b)
        {
            if (a.Time != b.Time) return a.Time > b.Time;
            int d = string.CompareOrdinal(a.Dev, b.Dev); if (d != 0) return d > 0;
            return string.CompareOrdinal(a.Hash, b.Hash) >= 0;
        }
        // the losing side of a conflict as its own item - the same id and content on every PC that makes it
        static Item ConflictCopy(string id, Item lose)
        {
            var c = ItemTypes.Clone(lose, false);
            using (var sha = SHA256.Create()) c.Id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("conflict|" + id + "|" + lose.Sync.Hash)), 0, 16).Replace("-", "").ToLowerInvariant();
            var from = string.IsNullOrEmpty(lose.Sync.DevName) ? "another PC" : lose.Sync.DevName;
            c.Title = (c.Title.Length > 0 ? c.Title : "Untitled") + " (conflict copy from " + from + ")";
            c.Sync = new SyncMeta { Vv = lose.Sync.Vv, Time = lose.Sync.Time, Dev = lose.Sync.Dev, DevName = lose.Sync.DevName };
            c.Sync.Hash = Vault.ItemHash(c);
            return c;
        }
        // what a copy holds, as far as sync cares: items and their revisions, tombstones, the vault's lists (not content hashes, which each PC works out itself)
        public static string Digest(VaultData d)
        {
            var sb = new StringBuilder();
            foreach (var it in d.Items.Concat(d.Trash).OrderBy(i => i.Id, StringComparer.Ordinal)) sb.Append(it.Id).Append('=').Append(it.Sync == null ? "" : it.Sync.Vv).Append(';');
            sb.Append('|'); foreach (var t in d.Tombstones.OrderBy(t => t.Id, StringComparer.Ordinal)) sb.Append(t.Id).Append('=').Append(t.Vv).Append(';');
            sb.Append('|').Append(string.Join("\u0001", d.Folders.OrderBy(x => x, StringComparer.Ordinal)));
            sb.Append('|').Append(string.Join(",", d.Templates.Select(t => t.Id).OrderBy(x => x, StringComparer.Ordinal)));
            sb.Append('|').Append(string.Join(",", d.LeakedIds.OrderBy(x => x, StringComparer.Ordinal)));
            sb.Append('|').Append(d.ExpiryNotified.Count).Append('|').Append(d.PhoneKey != null);
            return sb.ToString();
        }
    }

    // ---- encrypted snapshots of this PC's vault, for "Restore an older version" (kept only on this PC, in versions\)
    public static class Versions
    {
        public static int Keep = 30;
        public static TimeSpan Every = TimeSpan.FromMinutes(10);
        public static string Folder { get { return Path.Combine(KeysSettings.Dir, "versions"); } }
        public class Entry { public string Path; public DateTime When; public long Bytes; }
        // keeps a copy of the vault file as it is now (before it's replaced): at most every 10 minutes, or always when forced (before a merge removes or changes items)
        public static void MaybeKeep(string vaultPath, bool force)
        {
            try
            {
                if (!File.Exists(vaultPath) || !string.Equals(System.IO.Path.GetFullPath(vaultPath), System.IO.Path.GetFullPath(KeysSettings.VaultPath), StringComparison.OrdinalIgnoreCase)) return;
                var list = List();
                if (!force && list.Count > 0 && (DateTime.Now - list[0].When) < Every) return;
                Directory.CreateDirectory(Folder);
                var dest = System.IO.Path.Combine(Folder, "vault-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".hkvault");
                File.Copy(vaultPath, dest, true);
                foreach (var old in List().Skip(Keep)) try { File.Delete(old.Path); } catch { }
            }
            catch { }
        }
        public static List<Entry> List()
        {
            var o = new List<Entry>();
            if (!Directory.Exists(Folder)) return o;
            foreach (var f in Directory.GetFiles(Folder, "vault-*.hkvault"))
            {
                DateTime t; var n = System.IO.Path.GetFileNameWithoutExtension(f).Substring(6);
                if (!DateTime.TryParseExact(n, "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) t = File.GetLastWriteTime(f);
                o.Add(new Entry { Path = f, When = t, Bytes = new FileInfo(f).Length });
            }
            return o.OrderByDescending(e => e.When).ToList();
        }
        // what's in a snapshot (it opens with this vault's data key): items, trash
        public static VaultData Peek(Vault v, string path)
        {
            var k = v.DataKeyCopy();
            try { using (var s = Vault.OpenWithDataKey(path, k)) { var d = s.Data; s.Data = new VaultData(); return d; } }
            finally { Gcm.Wipe(k); }
        }
        // puts the vault back the way it was in that snapshot, as new changes (so they go to your other PCs too): items come back as they were,
        // items made since then go to the trash (not deleted). The vault as it is now is kept as a snapshot first, so this can be undone.
        public static int Restore(Vault v, string path)
        {
            var snap = Peek(v, path);
            MaybeKeep(v.Path, true);
            var cur = v.Data.Items.Concat(v.Data.Trash).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var all = new List<Item>(); var used = new HashSet<string>(); int changed = 0;
            foreach (var rec in snap.Items.Concat(snap.Trash))
            {
                if (used.Contains(rec.Id)) continue; used.Add(rec.Id);
                rec.Sync = null; Item c;
                if (cur.TryGetValue(rec.Id, out c)) { if (Vault.ItemHash(c) != Vault.ItemHash(rec)) changed++; }
                else { v.Revive(rec.Id); changed++; }
                all.Add(rec);
            }
            foreach (var c in cur.Values.Where(x => !used.Contains(x.Id)))
            {
                if (c.Deleted == null) { c.Deleted = DateTime.Now; changed++; }
                all.Add(c);
            }
            v.Data.Items = all.Where(i => i.Deleted == null).ToList(); v.Data.Trash = all.Where(i => i.Deleted != null).ToList();
            foreach (var f in snap.Folders) if (!v.Data.Folders.Contains(f)) v.Data.Folders.Add(f);
            foreach (var t in snap.Templates) if (!v.Data.Templates.Any(x => x.Id == t.Id)) v.Data.Templates.Add(t);
            return changed;
        }
    }

    // ---- the sync folder
    public class SyncResult
    {
        public string State = "synced";   // synced, waiting, error
        public string Message = "";
        public MergeReport Merge = new MergeReport();
        public int ConflictFiles;
        public bool Wrote;
        public DateTime When = DateTime.Now;
    }
    public static class SyncFolder
    {
        public const string FileName = "vault.hkvault";
        public static TimeSpan DamagedAfter = TimeSpan.FromMinutes(10);   // a copy that stays unreadable (same size and time) this long is set aside
        public static int WriteTries = 8;
        public static string MainPath(string folder) { return Path.Combine(folder, FileName); }
        // the cloud service the folder belongs to, for "Waiting for OneDrive"
        public static string Provider(string folder)
        {
            var f = (folder ?? "").ToLowerInvariant();
            if (f.Contains("onedrive")) return "OneDrive";
            if (f.Contains("google drive") || f.Contains("googledrive") || f.Contains("my drive")) return "Google Drive";
            if (f.Contains("dropbox")) return "Dropbox";
            if (f.Contains("icloud")) return "iCloud Drive";
            if (f.Contains("sync")) return "Syncthing";
            if (f.StartsWith(@"\\")) return "the network folder";
            return "the sync folder";
        }
        // cloud clients' conflict copies of the vault, and other PCs' copies (never our own temp files)
        public static List<string> ConflictFiles(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.hkvault").Where(f =>
                {
                    var n = Path.GetFileName(f);
                    return !n.Equals(FileName, StringComparison.OrdinalIgnoreCase) && n.StartsWith("vault", StringComparison.OrdinalIgnoreCase);
                }).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }
        static readonly Dictionary<string, Tuple<long, DateTime, DateTime>> unreadable = new Dictionary<string, Tuple<long, DateTime, DateTime>>(StringComparer.OrdinalIgnoreCase);
        static Tuple<long, DateTime> FileStamp(string path) { try { var i = new FileInfo(path); return i.Exists ? Tuple.Create(i.Length, i.LastWriteTimeUtc) : null; } catch { return null; } }

        // one round: read what's in the folder, merge it in, save this PC's copy if anything came in, write the folder's copy if it's behind
        public static SyncResult Run(Vault v, string folder, string dev, string devName)
        {
            var res = new SyncResult(); var prov = Provider(folder);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (the folder isn't there right now)"; return res; }
            var main = MainPath(folder); var key = v.DataKeyCopy();
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    v.Stamp(dev, devName);
                    bool remoteBehind = true; Tuple<long, DateTime> seen = null; var mergedFiles = new List<string>(); var round = new MergeReport();
                    if (File.Exists(main))
                    {
                        seen = FileStamp(main);
                        byte[] bytes;
                        try { bytes = Vault.ReadFile(main); }
                        catch (IOException) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (the vault file is in use)"; return res; }
                        Vault r = null; string problem = null; bool different = false, newer = false;
                        var hdr = Vault.PeekHeader(bytes);
                        if (hdr == null)
                        {
                            if (bytes.Length >= 10 && Encoding.ASCII.GetString(bytes, 0, 7) == "HKVAULT" && BitConverter.ToUInt16(bytes, 8) > Vault.Version) newer = true;
                            else problem = "unreadable";
                        }
                        else if (!hdr.VaultId.SequenceEqual(v.VaultId)) different = true;
                        else { try { r = Vault.OpenBytes(main, bytes, null, false, key, null); } catch { problem = "unreadable"; } }
                        if (newer) { res.State = "error"; res.Message = "The vault in " + prov + " was saved by a newer Hallow Keys - update Hallow Keys on this PC"; return res; }
                        if (different) { res.State = "error"; res.Message = "The sync folder holds a different vault - nothing was changed there"; return res; }
                        if (problem != null)
                        {
                            // probably still being written by the cloud client: wait. If it stays the same and unreadable for long, set it aside (never deleted) and write ours.
                            Tuple<long, DateTime, DateTime> u; var now = DateTime.UtcNow;
                            if (seen != null && unreadable.TryGetValue(main, out u) && u.Item1 == seen.Item1 && u.Item2 == seen.Item2 && now - u.Item3 >= DamagedAfter)
                            {
                                var aside = Path.Combine(folder, "vault (unreadable " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ").hkvault-damaged");
                                try { File.Move(main, aside); unreadable.Remove(main); res.Message = "Set aside an unreadable copy in " + prov + ": " + Path.GetFileName(aside); continue; }
                                catch { }
                            }
                            else if (seen != null && (!unreadable.TryGetValue(main, out u) || u.Item1 != seen.Item1 || u.Item2 != seen.Item2)) unreadable[main] = Tuple.Create(seen.Item1, seen.Item2, now);
                            res.State = "waiting"; res.Message = "Waiting for " + prov + " to finish copying the vault"; return res;
                        }
                        unreadable.Remove(main);
                        var rep = SyncMerge.Merge(v, r, dev, devName); r.Dispose();
                        round.Add(rep); remoteBehind = rep.RemoteBehind;
                    }
                    foreach (var cf in ConflictFiles(folder))
                    {
                        byte[] b; try { b = Vault.ReadFile(cf); } catch { continue; }
                        var hdr = Vault.PeekHeader(b); if (hdr == null || !hdr.VaultId.SequenceEqual(v.VaultId)) continue;   // still being written, or not this vault: left alone
                        Vault r; try { r = Vault.OpenBytes(cf, b, null, false, key, null); } catch { continue; }
                        var rep = SyncMerge.Merge(v, r, dev, devName); r.Dispose();
                        round.Add(rep); mergedFiles.Add(cf); remoteBehind = true;
                    }
                    if (round.LocalChanged)
                    {
                        if (round.Removed > 0 || round.Updated > 0 || round.Conflicts > 0) Versions.MaybeKeep(v.Path, true);
                        v.Save();
                    }
                    res.Merge.Add(round); res.ConflictFiles += mergedFiles.Count;
                    if (!remoteBehind) { res.State = "synced"; return res; }
                    var now2 = FileStamp(main);
                    if (seen != null && (now2 == null || now2.Item1 != seen.Item1 || now2.Item2 != seen.Item2)) continue;   // changed while we merged: merge again
                    if (seen == null && now2 != null) continue;
                    if (!WriteShared(main, v.Serialize(), dev)) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (it's holding the vault file - trying again soon)"; return res; }
                    res.Wrote = true;
                    foreach (var cf in mergedFiles) Archive(cf);
                    res.State = "synced"; return res;
                }
                res.State = "waiting"; res.Message = "Waiting for " + prov + " (the vault keeps changing - trying again soon)"; return res;
            }
            finally { Gcm.Wipe(key); }
        }
        // the whole file to a temp file next to it, then replace - retried while a cloud client holds the file
        public static bool WriteShared(string path, byte[] bytes, string dev)
        {
            var dir = Path.GetDirectoryName(path);
            var tmp = Path.Combine(dir, "~" + Path.GetFileName(path) + "." + (dev.Length > 8 ? dev.Substring(0, 8) : dev) + ".tmp");
            try
            {
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            }
            catch { try { File.Delete(tmp); } catch { } return false; }
            for (int i = 0; i < WriteTries; i++)
            {
                try { if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path); return true; }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                System.Threading.Thread.Sleep(150 * (i + 1));
            }
            try { File.Delete(tmp); } catch { }
            return false;
        }
        // a merged conflict file: kept on this PC (sync-merged), then removed from the sync folder
        static void Archive(string path)
        {
            try
            {
                var keep = Path.Combine(KeysSettings.Dir, "sync-merged"); Directory.CreateDirectory(keep);
                File.Copy(path, Path.Combine(keep, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Path.GetFileName(path)), true);
                File.Delete(path);
                foreach (var old in Directory.GetFiles(keep).OrderByDescending(f => f, StringComparer.Ordinal).Skip(20)) try { File.Delete(old); } catch { }
            }
            catch { }
        }
    }
}
