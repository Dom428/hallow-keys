// Hallow Keys 2.1 - several vaults inside the one encrypted vault file, Travel Mode, and when each item was last used.
//  - A vault (Personal, Gaming, Work, Shared...) is a named collection with its own settings (colour, icon, safe for travel). Every item
//    belongs to exactly one (Item.VaultId, null = Personal). Moving an item is an edit of that item, so it syncs like any other edit.
//  - The vault list syncs vault by vault and setting by setting: the latest name, look (icon + colour), travel flag and deleted flag each
//    win on their own (a rename on one PC and "safe for travel" on another both stay). A deleted vault stays
//    as a marker, so another PC deletes it too instead of bringing it back. An item whose vault is gone (deleted on another PC while the item
//    was moved into it there) is shown in Personal - nothing is ever lost.
//  - Travel Mode (a setting of this PC) hides every vault that isn't marked "Safe for travel" from this PC: the list, search, the browser
//    extension, desktop autofill, the phone copy, exports and Password Health. It's hide-only: the hidden items are still in the encrypted
//    vault file on this PC (and keep syncing), unlike 1Password, which removes them from the device. Turning it off needs the master password.
//  - Last used: when a login was filled (extension, desktop autofill), copied (username, password, 2FA code) or opened on its site. Kept in
//    VaultData.Usage inside the encrypted vault (never in status.json or any plain file), merged by the latest time, and never part of an
//    item's revision (using an item doesn't make it "changed" or cause sync conflicts).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace HallowKeys
{
    [DataContract] public class VaultInfo : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = "";
        [DataMember] public string Name = "";
        [DataMember] public string Icon = "";      // vault, gamepad, briefcase, users, house, user-shield
        [DataMember] public string Color = "";     // #RRGGBB, or "" for the accent colour
        [DataMember] public bool Travel;           // safe for travel: stays visible while Travel Mode is on
        [DataMember] public bool Deleted;          // deleted (its items went to the trash); kept so other PCs delete it too
        [DataMember] public long NameT, LookT, TravelT, DelT;   // UTC ticks each setting last changed - the latest of each wins in sync
        [DataMember] public long Time;             // the latest of those (for the digest)
        [DataMember] public string Dev = "";       // the PC that made the last change
        public VaultInfo Clone() { return new VaultInfo { Id = Id, Name = Name, Icon = Icon, Color = Color, Travel = Travel, Deleted = Deleted, NameT = NameT, LookT = LookT, TravelT = TravelT, DelT = DelT, Time = Time, Dev = Dev, ExtensionData = ExtensionData }; }
        [OnDeserialized] void Fix(StreamingContext c) { if (Id == null) Id = ""; if (Name == null) Name = ""; if (Icon == null) Icon = ""; if (Color == null) Color = ""; if (Dev == null) Dev = ""; }
    }
    [DataContract] public class UsageMark : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = "";        // the item
        [DataMember] public long T;                // UTC ticks it was last used
        [OnDeserialized] void Fix(StreamingContext c) { if (Id == null) Id = ""; }
    }

    public static class Vaults
    {
        public const string PersonalId = "personal";
        public const int MaxVaults = 50, MaxName = 40;
        public static readonly string[] IconChoices = { "vault", "user-shield", "gamepad", "briefcase", "users", "house", "plane", "wallet" };
        public static readonly string[] ColorChoices = { "", "#4C8DF6", "#3FB97A", "#E0A030", "#D9534F", "#9B6CF0", "#2BB3C0", "#E36FB0" };

        // the vault list, with Personal always there (made the same way on every PC, so two PCs' copies are equal)
        public static List<VaultInfo> All(VaultData d)
        {
            if (d.Vaults == null) d.Vaults = new List<VaultInfo>();
            if (!d.Vaults.Any(v => v.Id == PersonalId)) d.Vaults.Insert(0, new VaultInfo { Id = PersonalId, Name = "Personal", Icon = "user-shield" });
            return d.Vaults;
        }
        public static VaultInfo Personal(VaultData d) { return All(d).First(v => v.Id == PersonalId); }
        public static VaultInfo Get(VaultData d, string id) { return All(d).FirstOrDefault(v => v.Id == (string.IsNullOrEmpty(id) ? PersonalId : id)); }
        // live vaults: Personal first, then by name
        public static List<VaultInfo> Live(VaultData d)
        {
            var l = All(d).Where(v => !v.Deleted || v.Id == PersonalId).ToList();
            return l.Where(v => v.Id == PersonalId).Concat(l.Where(v => v.Id != PersonalId).OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
        }
        public static bool Several(VaultData d) { return Live(d).Count > 1; }
        // the vault an item is in (Personal if it names none, or one that's gone)
        public static VaultInfo Of(VaultData d, Item it)
        {
            if (it == null || string.IsNullOrEmpty(it.VaultId) || it.VaultId == PersonalId) return Personal(d);
            var v = All(d).FirstOrDefault(x => x.Id == it.VaultId);
            return v == null || v.Deleted ? Personal(d) : v;
        }
        public static string IdOf(VaultData d, Item it) { return Of(d, it).Id; }
        public static void Put(Item it, string vaultId) { it.VaultId = string.IsNullOrEmpty(vaultId) || vaultId == PersonalId ? null : vaultId; }

        // a change of one setting: its time (always later than before, even if the clock went back), the record's time, the PC
        static long Stamp(VaultInfo v, long old, string dev) { var t = Math.Max(DateTime.UtcNow.Ticks, Math.Max(old, v.Time) + 1); v.Time = t; v.Dev = dev ?? ""; return t; }
        static void Touch(VaultInfo v, string dev) { var t = Stamp(v, 0, dev); v.NameT = v.LookT = v.TravelT = v.DelT = t; }
        public static string CleanName(string name)
        {
            var n = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            return n.Length > MaxName ? n.Substring(0, MaxName).Trim() : n;
        }
        // a new vault; null and a reason if the name is empty, taken or there are too many
        public static VaultInfo Create(VaultData d, string name, string icon, string color, string dev, out string why)
        {
            why = null; name = CleanName(name);
            if (name.Length == 0) { why = "Give the vault a name."; return null; }
            if (Live(d).Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) { why = "There's already a vault called \"" + name + "\"."; return null; }
            if (Live(d).Count >= MaxVaults) { why = "That's the most vaults Hallow Keys keeps (" + MaxVaults + ")."; return null; }
            var v = new VaultInfo { Id = Guid.NewGuid().ToString("N"), Name = name, Icon = IconChoices.Contains(icon) ? icon : "vault", Color = ColorChoices.Contains(color) ? color : "" };
            Touch(v, dev); All(d).Add(v); return v;
        }
        public static string Rename(VaultData d, string id, string name, string dev)
        {
            var v = Get(d, id); name = CleanName(name);
            if (v == null || v.Deleted) return "That vault isn't there any more.";
            if (name.Length == 0) return "Give the vault a name.";
            if (Live(d).Any(x => x != v && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) return "There's already a vault called \"" + name + "\".";
            if (v.Name == name) return null;
            v.Name = name; v.NameT = Stamp(v, v.NameT, dev); return null;
        }
        public static void SetLook(VaultData d, string id, string icon, string color, string dev)
        {
            var v = Get(d, id); if (v == null) return;
            if (icon != null && IconChoices.Contains(icon)) v.Icon = icon;
            if (color != null && ColorChoices.Contains(color)) v.Color = color;
            v.LookT = Stamp(v, v.LookT, dev);
        }
        public static void SetTravel(VaultData d, string id, bool travel, string dev) { var v = Get(d, id); if (v == null || v.Travel == travel) return; v.Travel = travel; v.TravelT = Stamp(v, v.TravelT, dev); }
        // deletes a vault: its items go to the trash (restorable for 30 days, still encrypted in here); Personal can't be deleted
        public static int Delete(VaultData d, string id, string dev, DateTime now)
        {
            var v = Get(d, id); if (v == null || v.Id == PersonalId || v.Deleted) return -1;
            var items = d.Items.Where(i => i.VaultId == v.Id).ToList();
            foreach (var it in items) Organize.MoveToTrash(d, it, now);
            v.Deleted = true; v.DelT = Stamp(v, v.DelT, dev);
            return items.Count;
        }
        // brings a deleted vault back, with its items that are still in the trash
        public static int Undelete(VaultData d, string id, string dev)
        {
            var v = Get(d, id); if (v == null || !v.Deleted) return -1;
            if (Live(d).Any(x => string.Equals(x.Name, v.Name, StringComparison.CurrentCultureIgnoreCase))) { v.Name = CleanName(v.Name + " (restored)"); v.NameT = Stamp(v, v.NameT, dev); }
            v.Deleted = false; v.DelT = Stamp(v, v.DelT, dev);
            var back = d.Trash.Where(i => i.VaultId == v.Id).ToList();
            foreach (var it in back) Organize.Restore(d, it);
            return back.Count;
        }
        // a restored backup or export: its items keep their vault if this vault has it (live), get it made here from the backup's list
        // (same id, so it's the same vault on every PC), or go to Personal
        public static void Adopt(VaultData into, VaultData from, string dev)
        {
            foreach (var it in from.Items.Concat(from.Trash))
            {
                if (string.IsNullOrEmpty(it.VaultId) || it.VaultId == PersonalId) { it.VaultId = null; continue; }
                var mine = All(into).FirstOrDefault(v => v.Id == it.VaultId);
                if (mine != null) { if (mine.Deleted) it.VaultId = null; continue; }
                var theirs = from.Vaults == null ? null : from.Vaults.FirstOrDefault(v => v.Id == it.VaultId && !v.Deleted);
                if (theirs == null || Live(into).Count >= MaxVaults) { it.VaultId = null; continue; }
                var c = theirs.Clone(); c.ExtensionData = null; c.Name = CleanName(c.Name);
                if (c.Name.Length == 0 || Live(into).Any(v => string.Equals(v.Name, c.Name, StringComparison.CurrentCultureIgnoreCase))) c.Name = CleanName((c.Name.Length == 0 ? "Vault" : c.Name) + " (restored)");
                Touch(c, dev); All(into).Add(c);
            }
        }
        // the vault records an export of these items needs
        public static List<VaultInfo> For(VaultData d, IEnumerable<Item> items) { var ids = new HashSet<string>(items.Select(i => IdOf(d, i))); return Live(d).Where(v => ids.Contains(v.Id)).ToList(); }
        public static List<VaultInfo> DeletedOnes(VaultData d) { return All(d).Where(v => v.Deleted && v.Id != PersonalId).OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); }
        // an item restored from the trash whose vault was deleted goes back to Personal
        public static void FixRestored(VaultData d, Item it) { var v = string.IsNullOrEmpty(it.VaultId) ? null : All(d).FirstOrDefault(x => x.Id == it.VaultId); if (v == null || v.Deleted) it.VaultId = null; }

        // ---- Travel Mode: what this PC shows
        public static bool Shown(VaultData d, Item it, bool travel) { return !travel || Of(d, it).Travel; }
        public static bool AnyTravelSafe(VaultData d) { return Live(d).Any(v => v.Travel); }
        // where a new item goes: the vault you're looking at, else Personal - and while travelling, a vault that's safe for travel
        public static string DefaultFor(VaultData d, string current, bool travel)
        {
            var v = string.IsNullOrEmpty(current) ? null : Get(d, current);
            if (v == null || v.Deleted) v = Personal(d);
            if (travel && !v.Travel) { var t = Live(d).FirstOrDefault(x => x.Travel); if (t != null) v = t; }
            return v.Id;
        }
        // a read-only view of the vault with only what this PC may show (and only one vault, when asked): for the extension, desktop
        // autofill, the phone copy, exports, Password Health and the lists. The item objects are the real ones; never save this view.
        public static VaultData View(VaultData d, bool travel, string onlyVault)
        {
            Func<Item, bool> keep = it => Shown(d, it, travel) && (onlyVault == null || Of(d, it).Id == onlyVault);
            if (!travel && onlyVault == null) return d;
            return new VaultData
            {
                Items = d.Items.Where(keep).ToList(), Trash = d.Trash.Where(keep).ToList(), Folders = d.Folders, LeakedIds = d.LeakedIds, PhoneSalt = d.PhoneSalt, PhoneKey = d.PhoneKey,
                PhoneIterations = d.PhoneIterations, Templates = d.Templates, ExpiryNotified = d.ExpiryNotified, Tombstones = d.Tombstones, Vaults = d.Vaults, Usage = d.Usage
            };
        }

        // ---- sync: each vault's latest change wins; deleted vaults stay deleted unless one is brought back later
        // a is the later of two versions of one setting (same result on every PC: time, then the value itself)
        static bool Wins(long at, string av, long bt, string bv) { return at != bt ? at > bt : string.CompareOrdinal(av, bv) >= 0; }
        public static VaultInfo MergeOne(VaultInfo a, VaultInfo b)
        {
            var o = a.Clone();
            if (!Wins(a.NameT, a.Name, b.NameT, b.Name)) { o.Name = b.Name; o.NameT = b.NameT; }
            if (!Wins(a.LookT, a.Icon + "|" + a.Color, b.LookT, b.Icon + "|" + b.Color)) { o.Icon = b.Icon; o.Color = b.Color; o.LookT = b.LookT; }
            if (!Wins(a.TravelT, a.Travel ? "1" : "0", b.TravelT, b.Travel ? "1" : "0")) { o.Travel = b.Travel; o.TravelT = b.TravelT; }
            if (!Wins(a.DelT, a.Deleted ? "1" : "0", b.DelT, b.Deleted ? "1" : "0")) { o.Deleted = b.Deleted; o.DelT = b.DelT; }
            if (!Wins(a.Time, a.Dev, b.Time, b.Dev)) { o.Time = b.Time; o.Dev = b.Dev; }
            return o;
        }
        public static void Merge(VaultData local, VaultData remote)
        {
            if (remote.Vaults == null || remote.Vaults.Count == 0) { if (local.Vaults != null) All(local); return; }
            var mine = All(local).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in remote.Vaults.Where(v => !string.IsNullOrEmpty(v.Id)))
            {
                VaultInfo l;
                mine[r.Id] = mine.TryGetValue(r.Id, out l) ? MergeOne(l, r) : r.Clone();
            }
            local.Vaults = mine.Values.OrderBy(v => v.Id == PersonalId ? 0 : 1).ThenBy(v => v.Id, StringComparer.Ordinal).ToList();
        }

        // ---- last used
        public static DateTime? LastUsed(VaultData d, string id)
        {
            if (d.Usage == null || string.IsNullOrEmpty(id)) return null;
            var m = d.Usage.FirstOrDefault(u => u.Id == id);
            return m == null || m.T <= 0 ? (DateTime?)null : new DateTime(m.T, DateTimeKind.Utc).ToLocalTime();
        }
        public static Dictionary<string, DateTime> UsageMap(VaultData d)
        {
            var o = new Dictionary<string, DateTime>();
            if (d.Usage != null) foreach (var u in d.Usage) if (u.T > 0 && !string.IsNullOrEmpty(u.Id)) { DateTime t = new DateTime(u.T, DateTimeKind.Utc).ToLocalTime(), x; if (!o.TryGetValue(u.Id, out x) || t > x) o[u.Id] = t; }
            return o;
        }
        // records a use (a later time only); true if it changed
        public static bool MarkUsed(VaultData d, string id, DateTime utc)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (d.Usage == null) d.Usage = new List<UsageMark>();
            var t = utc.ToUniversalTime().Ticks; var m = d.Usage.FirstOrDefault(u => u.Id == id);
            if (m == null) { d.Usage.Add(new UsageMark { Id = id, T = t }); return true; }
            if (t <= m.T) return false;
            m.T = t; return true;
        }
        // both PCs' times, the latest for each item; times of items that are gone are let go
        public static void MergeUsage(VaultData local, VaultData remote, HashSet<string> ids)
        {
            if ((local.Usage == null || local.Usage.Count == 0) && (remote.Usage == null || remote.Usage.Count == 0)) return;
            var m = new Dictionary<string, long>();
            foreach (var u in (local.Usage ?? new List<UsageMark>()).Concat(remote.Usage ?? new List<UsageMark>()))
            { if (string.IsNullOrEmpty(u.Id) || u.T <= 0 || (ids != null && !ids.Contains(u.Id))) continue; long x; m[u.Id] = m.TryGetValue(u.Id, out x) ? Math.Max(x, u.T) : u.T; }
            local.Usage = m.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new UsageMark { Id = kv.Key, T = kv.Value }).ToList();
        }
        // for SyncMerge.Digest: the vault list and the last-used times
        public static string Digest(VaultData d)
        {
            var sb = new StringBuilder();
            if (d.Vaults != null) foreach (var v in d.Vaults.OrderBy(x => x.Id, StringComparer.Ordinal)) sb.Append(v.Id).Append('=').Append(v.NameT).Append(',').Append(v.LookT).Append(',').Append(v.TravelT).Append(',').Append(v.DelT).Append(';');
            sb.Append('|');
            if (d.Usage != null) foreach (var u in d.Usage.OrderBy(x => x.Id, StringComparer.Ordinal)) sb.Append(u.Id).Append('=').Append(u.T).Append(';');
            return sb.ToString();
        }

        // ---- category tabs of the main list
        public static readonly string[] Tabs = { "all", "login", "passkey", "note", "card", "identity", "document", "other" };
        public static string TabName(string tab)
        {
            switch (tab)
            {
                case "login": return "Logins"; case "passkey": return "Passkeys"; case "note": return "Secure notes"; case "card": return "Cards";
                case "identity": return "Contact info"; case "document": return "Documents"; case "other": return "Other"; default: return "All items";
            }
        }
        public static bool PasskeysReady = false;   // the Passkeys tab stays hidden until passkeys arrive (2.3)
        // which tab an item shows in (besides All); Documents also holds every item that has files attached
        public static bool InTab(Item it, string tab)
        {
            switch (tab)
            {
                case "all": return true;
                case "login": case "note": case "card": case "identity": return it.Type == tab;
                case "passkey": return it.Type == "passkey";
                case "document": return it.Type == "document" || it.Files.Count > 0;
                case "other": return it.Type != "login" && it.Type != "note" && it.Type != "card" && it.Type != "identity" && it.Type != "document" && it.Type != "passkey";
                default: return true;
            }
        }
        public static string TabOfType(string type)
        {
            if (type == "login" || type == "note" || type == "card" || type == "identity" || type == "document" || type == "passkey") return type;
            return "other";
        }
    }
}
