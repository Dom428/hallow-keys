// Hallow Keys 2.7 - "Phone, properly": the phone (Hallow Remote's Keys tab) may send changes back - add, edit, move to the trash.
// Off unless you turn on "Let the phone edit" (separate from the read-only phone copy). End to end: the phone encrypts each change with
// the phone copy's key (AES-256-GCM, AAD "HallowKeysPhoneChange1"; the key comes from your master password on the phone and never
// leaves it), Hallow Remote only passes the sealed record on through the family pipe, and Hallow Keys opens it here. Everything in it is
// checked before anything changes:
//   - it must open with this vault's phone key (a wrong key or one changed byte -> refused), and be new: each record has a random id
//     that is remembered for 48 hours (a replay -> refused) and a time within the last 24 hours (an old capture -> refused)
//   - only known fields, sane lengths, no control characters in one-line fields, real web addresses, a valid 2FA key, an existing
//     vault you can see (Travel Mode), and only items the phone may edit (never passkeys, SSH keys or crypto wallets)
//   - an edit names the revision the phone saw: if the item changed on the PC (or another PC) since, the phone's version is kept next
//     to it as "(conflict copy from phone)" - nothing is overwritten; a delete of a changed item is not done
// Applied changes are ordinary edits on this PC, so 2.0's per-item sync carries them to your other PCs. Each one goes into the
// activity list (kept inside the vault), and a snapshot is kept before the first phone change of a session, so "Undo phone changes"
// puts the vault back with 2.0's Restore an older version.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    [DataContract] public class PhoneLogEntry : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public DateTime When;
        [DataMember] public string Text = "";
        [DataMember(EmitDefaultValue = false)] public string Snapshot;   // the snapshot file kept just before (the first change of a session)
    }

    public static class PhoneEdit
    {
        public const string ChangeAad = "HallowKeysPhoneChange1";
        public static readonly string[] AddTypes = { "login", "note", "card", "identity" };
        public static bool Editable(Item i) { return i != null && i.Type != "passkey" && i.Type != "wallet" && i.Type != "sshkey"; }
        public static string Rev(Item i) { return Vault.ItemHash(i); }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        // what the phone copy carries on top of 1.10's fields, so the phone can edit: the item's id and revision, its vault, the raw
        // values the 1.10 view merges (notes, address parts) and its own / type fields by id (never a wallet's recovery phrase)
        public static void AddEditInfo(Dictionary<string, object> d, Item i, VaultData all)
        {
            d["id"] = i.Id; d["rev"] = Rev(i); d["vault"] = Vaults.IdOf(all, i); d["kind"] = i.Type; d["editable"] = Editable(i);
            d["rawNotes"] = i.Notes;
            if (i.Type == "identity") d["addr"] = new Dictionary<string, object> { { "address", i.Address }, { "city", i.City }, { "postCode", i.PostCode }, { "country", i.Country } };
            if (Editable(i))
                d["efields"] = ItemTypes.Ordered(i).Where(f => !ItemTypes.IsSeed(i, f) && !(ItemTypes.Get(i.Type) != null && ItemTypes.Get(i.Type).Fields != null && ItemTypes.Get(i.Type).Fields.Any(x => x.Key == f.Key && x.Computed)))
                    .Select(f => (object)new Dictionary<string, object> { { "id", f.Id }, { "label", f.Label }, { "kind", f.Kind }, { "value", f.Value }, { "hidden", ItemTypes.IsHidden(f) } }).ToList();
        }
        public static List<object> VaultList(VaultData d) { return Vaults.Live(d).Select(v => (object)new Dictionary<string, object> { { "id", v.Id }, { "name", v.Name } }).ToList(); }

        public class Result { public bool Ok; public string Status = "", Msg = "", Id = "", Title = "", Op = ""; }
        static Result No(string msg) { return new Result { Ok = false, Status = "refused", Msg = msg }; }

        // one sealed record from the phone -> applied to d (in memory; the caller saves). nowUtc for the tests.
        public static Result Apply(VaultData d, string recordJson, DateTime nowUtc, Func<Item, bool> visible, bool travel)
        {
            if (d.PhoneKey == null) return No("The phone copy is off.");
            Dictionary<string, object> outer; try { outer = js.Deserialize<Dictionary<string, object>>(recordJson); } catch { return No("That isn't a change from Hallow Keys' phone page."); }
            if (outer == null || !(outer.ContainsKey("v") && outer["v"] is int && (int)outer["v"] == 1)) return No("That isn't a change from Hallow Keys' phone page.");
            byte[] iv, ct; try { iv = Convert.FromBase64String(outer["iv"] as string ?? ""); ct = Convert.FromBase64String(outer["ct"] as string ?? ""); } catch { return No("The change is damaged."); }
            if (iv.Length != 12 || ct.Length < 17 || ct.Length > 2 * 1024 * 1024) return No("The change is damaged.");
            byte[] plain;
            try { plain = Gcm.DecryptRaw(d.PhoneKey, iv, ct.Take(ct.Length - 16).ToArray(), ct.Skip(ct.Length - 16).ToArray(), Encoding.ASCII.GetBytes(ChangeAad)); }
            catch { return No("The change couldn't be opened with this vault's phone key (a different vault or master password, or it was changed on the way) - nothing was changed."); }
            Dictionary<string, object> m;
            try { m = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(plain)); } catch { return No("The change is damaged."); }
            finally { Array.Clear(plain, 0, plain.Length); }
            // new, and recent
            var rid = S(m, "rid"); byte[] ridb; try { ridb = Convert.FromBase64String(rid); } catch { ridb = null; }
            if (ridb == null || ridb.Length != 16) return No("The change has no id.");
            double ts; if (!(m.ContainsKey("ts") && double.TryParse(Convert.ToString(m["ts"], System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ts))) return No("The change has no time.");
            var when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts);
            if (when < nowUtc.AddHours(-24) || when > nowUtc.AddMinutes(10)) return No("The change is too old (or from the future) - it was refused. Open the Keys tab again and redo it.");
            if (d.PhoneSeen == null) d.PhoneSeen = new List<string>();
            var cutoff = nowUtc.AddHours(-48).Ticks; d.PhoneSeen.RemoveAll(x => { long t; var p = x.Split('|'); return p.Length != 2 || !long.TryParse(p[1], out t) || t < cutoff; });
            if (d.PhoneSeen.Any(x => x.StartsWith(rid + "|"))) return No("That change was already applied (a repeat) - ignored.");
            var op = S(m, "op"); var r = new Result { Op = op };
            string err = null;
            if (op == "add") err = Add(d, m, nowUtc, visible, travel, r);
            else if (op == "edit") err = Edit(d, m, nowUtc, visible, travel, r);
            else if (op == "trash") err = Trash(d, m, nowUtc, visible, r);
            else err = "Unknown change.";
            if (err != null) return No(err);
            d.PhoneSeen.Add(rid + "|" + nowUtc.Ticks);
            r.Ok = true; return r;
        }
        static string S(Dictionary<string, object> m, string k) { object o; return m != null && m.TryGetValue(k, out o) && o != null ? o as string ?? "" : ""; }

        static readonly string[] Keys = { "type", "title", "folder", "vault", "fav", "user", "pw", "urls", "totp", "notes", "cardHolder", "cardNumber", "expiry", "cvc", "pin", "name", "email", "phone", "address", "city", "postCode", "country", "fields" };
        static readonly string[] Multi = { "notes", "address" };
        // checks a phone item; null = fine
        static string Check(Dictionary<string, object> it, VaultData d, Func<Item, bool> visible, bool travel, string type)
        {
            if (it == null) return "The change has no item.";
            foreach (var k in it.Keys) if (!Keys.Contains(k)) return "Unknown field \"" + k + "\" - refused.";
            foreach (var k in it.Keys)
            {
                if (k == "urls" || k == "fields" || k == "fav") continue;
                if (!(it[k] is string)) return "\"" + k + "\" isn't text.";
                var v = (string)it[k];
                int max = k == "notes" ? 100000 : k == "title" ? 300 : 4000;
                if (v.Length > max) return "\"" + k + "\" is too long.";
                if (v.Any(c => c < 32 && !(Multi.Contains(k) && (c == '\n' || c == '\r' || c == '\t')))) return "\"" + k + "\" has control characters.";
            }
            if (it.ContainsKey("fav") && !(it["fav"] is bool)) return "\"fav\" isn't true / false.";
            if (it.ContainsKey("urls"))
            {
                var l = it["urls"] as ArrayList; if (l == null || l.Count > 30) return "Too many web addresses.";
                foreach (var u in l) { var s = u as string; if (s == null || s.Length > 2000 || s.Any(c => c < 32) || (s.Trim().Length > 0 && Domain.Parse(s) == null)) return "\"" + (s ?? "?") + "\" isn't a web address."; }
            }
            var totp = S(it, "totp"); if (totp.Trim().Length > 0 && Totp.Parse(totp) == null) return "The 2FA key isn't valid.";
            if (it.ContainsKey("vault"))
            {
                var vid = S(it, "vault"); var v = Vaults.Live(d).FirstOrDefault(x => x.Id == (vid.Length == 0 ? Vaults.PersonalId : vid));
                if (v == null || (travel && !v.Travel)) return "That vault doesn't exist (or is hidden by Travel Mode).";
            }
            if (S(it, "folder").Length > 200) return "The folder name is too long.";
            if (type != "login" && (it.ContainsKey("user") || it.ContainsKey("pw") || it.ContainsKey("urls") || it.ContainsKey("totp"))) return "Login fields on an item that isn't a login.";
            if (type != "card" && (it.ContainsKey("cardHolder") || it.ContainsKey("cardNumber") || it.ContainsKey("expiry") || it.ContainsKey("cvc") || it.ContainsKey("pin"))) return "Card fields on an item that isn't a card.";
            if (type != "identity" && (it.ContainsKey("name") || it.ContainsKey("email") || it.ContainsKey("phone") || it.ContainsKey("address") || it.ContainsKey("city") || it.ContainsKey("postCode") || it.ContainsKey("country"))) return "Contact fields on an item that isn't an identity.";
            return null;
        }
        // copies the checked values into an item (only the ones the phone sent)
        static void Put(Item t, Dictionary<string, object> it, VaultData d, DateTime now)
        {
            Func<string, bool> has = k => it.ContainsKey(k);
            if (has("title")) t.Title = S(it, "title").Trim();
            if (has("folder")) { t.Folder = S(it, "folder").Trim(); if (t.Folder.Length > 0 && !d.Folders.Contains(t.Folder)) d.Folders.Add(t.Folder); }
            if (has("vault")) Vaults.Put(t, S(it, "vault"));
            if (has("fav")) t.Favorite = (bool)it["fav"];
            if (has("user")) t.Username = S(it, "user");
            if (has("pw")) { var p = S(it, "pw"); if (p != t.Password) { if (t.Password.Length > 0) Organize.RecordOldPassword(t, t.Password, now); t.Password = p; t.PasswordChanged = now; } }
            if (has("urls")) t.Urls = ((ArrayList)it["urls"]).Cast<string>().Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
            if (has("totp")) t.Totp = S(it, "totp").Trim();
            if (has("notes")) t.Notes = S(it, "notes");
            if (has("cardHolder")) t.CardHolder = S(it, "cardHolder"); if (has("cardNumber")) t.CardNumber = S(it, "cardNumber"); if (has("expiry")) t.Expiry = S(it, "expiry"); if (has("cvc")) t.Cvc = S(it, "cvc"); if (has("pin")) t.Pin = S(it, "pin");
            if (has("name")) t.FullName = S(it, "name"); if (has("email")) t.Email = S(it, "email"); if (has("phone")) t.Phone = S(it, "phone");
            if (has("address")) t.Address = S(it, "address"); if (has("city")) t.City = S(it, "city"); if (has("postCode")) t.PostCode = S(it, "postCode"); if (has("country")) t.Country = S(it, "country");
            t.Updated = now;
        }
        // the item's own fields by id (edit only): every id must be one of this item's fields the phone may change
        static string Fields(Item t, Dictionary<string, object> it, bool apply, DateTime now)
        {
            if (!it.ContainsKey("fields")) return null;
            var l = it["fields"] as ArrayList; if (l == null || l.Count > 100) return "The fields are damaged.";
            foreach (var o in l)
            {
                var f = o as Dictionary<string, object>; if (f == null || f.Keys.Any(k => k != "id" && k != "value")) return "The fields are damaged.";
                var id = S(f, "id"); var val = f.ContainsKey("value") ? f["value"] as string : null; if (val == null) return "A field has no value.";
                var cf = t.Fields.FirstOrDefault(x => x.Id == id);
                if (cf == null || ItemTypes.IsSeed(t, cf)) return "A field the phone sent isn't on this item.";
                var def = ItemTypes.Get(t.Type) != null && ItemTypes.Get(t.Type).Fields != null ? ItemTypes.Get(t.Type).Fields.FirstOrDefault(x => x.Key == cf.Key && cf.Key.Length > 0) : null;
                if (def != null && def.Computed) return "That field is worked out by Hallow Keys.";
                bool multi = cf.Kind == "note" || cf.Kind == "secretnote";
                if (val.Length > (multi ? 100000 : 4000) || val.Any(c => c < 32 && !(multi && (c == '\n' || c == '\r' || c == '\t')))) return "\"" + cf.Label + "\" isn't valid.";
                if (cf.Kind == "totp" && val.Trim().Length > 0 && Totp.Parse(val) == null) return "\"" + cf.Label + "\" isn't a valid 2FA key.";
                if (cf.Kind == "url" && val.Trim().Length > 0 && Domain.Parse(val) == null) return "\"" + cf.Label + "\" isn't a web address.";
                if (apply && cf.Value != val)
                {
                    if (ItemTypes.IsHidden(cf) && cf.Value.Length > 0) { cf.Old.Insert(0, cf.Value); cf.OldDates.Insert(0, now.ToString("o")); while (cf.Old.Count > 10) { cf.Old.RemoveAt(10); if (cf.OldDates.Count > 10) cf.OldDates.RemoveAt(10); } }
                    cf.Value = val;
                }
            }
            return null;
        }
        static string Add(VaultData d, Dictionary<string, object> m, DateTime nowUtc, Func<Item, bool> visible, bool travel, Result r)
        {
            var it = m.ContainsKey("item") ? m["item"] as Dictionary<string, object> : null; if (it == null) return "The change has no item.";
            var type = S(it, "type"); if (!AddTypes.Contains(type)) return "The phone can add logins, secure notes, cards and identities.";
            if (it.ContainsKey("fields")) return "New items from the phone can't have their own fields yet.";
            var e = Check(it, d, visible, travel, type); if (e != null) return e;
            if (S(it, "title").Trim().Length == 0) return "The new item needs a title.";
            var now = nowUtc.ToLocalTime();
            var t = new Item { Type = type, Created = now, Updated = now, PasswordChanged = now };
            if (!it.ContainsKey("vault")) Vaults.Put(t, Vaults.DefaultFor(d, null, travel));
            Put(t, it, d, now);
            d.Items.Add(t);
            r.Status = "added"; r.Id = t.Id; r.Title = t.Title; return null;
        }
        static Item Find(VaultData d, Dictionary<string, object> m, Func<Item, bool> visible, out string err)
        {
            err = null; var id = S(m, "id");
            var t = d.Items.FirstOrDefault(i => i.Id == id && id.Length > 0);
            if (t == null) { err = d.Trash.Any(i => i.Id == id) ? "That item is in the trash on the PC." : "That item isn't on the PC (any more)."; return null; }
            if (!visible(t)) { err = "That item is hidden by Travel Mode."; return null; }
            if (!Editable(t)) { err = "Passkeys, SSH keys and crypto wallets can only be changed on the PC."; return null; }
            return t;
        }
        static string Edit(VaultData d, Dictionary<string, object> m, DateTime nowUtc, Func<Item, bool> visible, bool travel, Result r)
        {
            string err; var t = Find(d, m, visible, out err); if (t == null) return err;
            var it = m.ContainsKey("item") ? m["item"] as Dictionary<string, object> : null; if (it == null) return "The change has no item.";
            if (it.ContainsKey("type") && S(it, "type") != t.Type) return "The phone can't change what kind of item it is.";
            var e = Check(it, d, visible, travel, t.Type) ?? Fields(t, it, false, nowUtc); if (e != null) return e;
            if (it.ContainsKey("title") && S(it, "title").Trim().Length == 0) return "The item needs a title.";
            var now = nowUtc.ToLocalTime();
            if (S(m, "base") != Rev(t))
            {
                // changed on the PC since the phone's copy: keep both
                var c = ItemTypes.Clone(t, true); c.Id = Guid.NewGuid().ToString("N"); c.Sync = null;
                foreach (var f in t.Fields) { var cf = c.Fields.FirstOrDefault(x => x.Key == f.Key && x.Label == f.Label && x.Kind == f.Kind && x.Value == f.Value); if (cf != null) cf.Id = f.Id; }
                Fields(c, it, true, now); Put(c, it, d, now);
                c.Title = (c.Title.Length > 0 ? c.Title : "Untitled") + " (conflict copy from phone)";
                foreach (var f in c.Fields) f.Id = Guid.NewGuid().ToString("N");
                d.Items.Add(c);
                r.Status = "conflict"; r.Id = c.Id; r.Title = t.Title; r.Msg = "\"" + t.Title + "\" was changed on the PC since your phone's copy - your version is kept next to it as a conflict copy."; return null;
            }
            Fields(t, it, true, now); Put(t, it, d, now);
            r.Status = "edited"; r.Id = t.Id; r.Title = t.Title; return null;
        }
        static string Trash(VaultData d, Dictionary<string, object> m, DateTime nowUtc, Func<Item, bool> visible, Result r)
        {
            string err; var t = Find(d, m, visible, out err); if (t == null) return err;
            if (S(m, "base") != Rev(t)) { r.Status = "conflict"; r.Id = t.Id; r.Title = t.Title; r.Msg = "\"" + t.Title + "\" was changed on the PC since your phone's copy, so it wasn't moved to the trash."; return null; }
            Organize.MoveToTrash(d, t, nowUtc.ToLocalTime());
            r.Status = "trashed"; r.Id = t.Id; r.Title = t.Title; return null;
        }
        public static string Describe(Result r)
        {
            if (!r.Ok) return "refused a change: " + r.Msg;
            if (r.Status == "added") return "added \"" + r.Title + "\"";
            if (r.Status == "edited") return "changed \"" + r.Title + "\"";
            if (r.Status == "trashed") return "moved \"" + r.Title + "\" to the trash";
            return r.Op == "trash" ? "didn't trash \"" + r.Title + "\" (changed on the PC since)" : "conflict on \"" + r.Title + "\" - kept as a conflict copy";
        }
    }
}
