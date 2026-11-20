// Hallow Keys v1.6 - "Every kind of item", the parts without a window:
//  - the item types (SSH key, API credential, software licence, bank account, passport, driving licence, Wi-Fi network,
//    server / database, membership, crypto wallet) and their fields; your own fields on any item; your own templates
//  - hidden fields: masked, never searched, their old values kept in a history; a wallet's recovery phrase is extra careful
//    (never searched, never in the phone copy unless switched on for that wallet, left out of CSV exports unless ticked)
//  - SSH keys: key type and SHA256 fingerprint from the public key (or from an OpenSSH private key's public part), and new RSA keys
//    made with Windows' CNG (RSACng) written in OpenSSH's own formats. Ed25519 isn't in .NET Framework / CNG, so those are paste-only.
//  - expiry dates (cards, passports, licences, memberships, software licences, your own date fields marked "expires") and reminders
//  - the phone copy, CSV and duplicate keys for the new types
// Everything lives in the vault's encrypted items (Item.Fields), so nothing new is ever written anywhere else.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public class FieldDef
    {
        public string Key, Label, Kind = "text"; public bool Expires, Computed; public string Hint = "";
        public FieldDef(string key, string label, string kind) { Key = key; Label = label; Kind = kind; }
    }
    public class TypeDef
    {
        public string Id, Name, Lower, Plural, Icon; public bool Legacy; public FieldDef[] Fields = new FieldDef[0];
        public string[] Sub = new string[0];     // visible fields shown under the title in the list (never hidden ones)
        public string[] Dupe = new string[0];    // fields that make two items "the same" for the duplicate finder
    }

    public static class ItemTypes
    {
        public static readonly string[] Kinds = { "text", "hidden", "url", "date", "totp", "email", "phone" };   // what you can add yourself
        public static readonly Dictionary<string, string> KindName = new Dictionary<string, string> {
            { "text", "Text" }, { "hidden", "Hidden" }, { "url", "Web address" }, { "date", "Date" }, { "totp", "2FA code" }, { "email", "Email" }, { "phone", "Phone" }, { "note", "Text (several lines)" }, { "secretnote", "Hidden (several lines)" } };
        public static bool IsHidden(string kind) { return kind == "hidden" || kind == "totp" || kind == "secretnote"; }
        public static bool IsHidden(CustomField f) { return f != null && IsHidden(f.Kind); }

        static FieldDef F(string key, string label, string kind) { return new FieldDef(key, label, kind); }
        static FieldDef Exp(string key, string label) { return new FieldDef(key, label, "date") { Expires = true }; }
        static FieldDef C(string key, string label) { return new FieldDef(key, label, "text") { Computed = true }; }
        public static readonly List<TypeDef> All = new List<TypeDef> {
            new TypeDef { Id = "login", Name = "Login", Lower = "login", Plural = "Logins", Icon = "key", Legacy = true },
            new TypeDef { Id = "note", Name = "Secure note", Lower = "secure note", Plural = "Secure notes", Icon = "note-sticky", Legacy = true },
            new TypeDef { Id = "card", Name = "Card", Lower = "card", Plural = "Cards", Icon = "credit-card", Legacy = true },
            new TypeDef { Id = "identity", Name = "Identity", Lower = "identity", Plural = "Identities", Icon = "id-card", Legacy = true },
            new TypeDef { Id = "sshkey", Name = "SSH key", Lower = "SSH key", Plural = "SSH keys", Icon = "terminal", Sub = new[] { "keyType", "fingerprint" }, Dupe = new[] { "fingerprint" },
                Fields = new[] { F("privateKey", "Private key", "secretnote"), F("publicKey", "Public key", "note"), C("fingerprint", "Fingerprint"), C("keyType", "Key type"), F("passphrase", "Passphrase", "hidden") } },
            new TypeDef { Id = "apikey", Name = "API credential", Lower = "API credential", Plural = "API credentials", Icon = "code", Sub = new[] { "host", "username" }, Dupe = new[] { "host", "username", "credential" },
                Fields = new[] { F("username", "Username / key ID", "text"), F("credential", "API key / token", "hidden"), F("secret", "Secret", "hidden"), F("host", "Website / host", "url"), Exp("expires", "Expires") } },
            new TypeDef { Id = "licence", Name = "Software licence", Lower = "software licence", Plural = "Software licences", Icon = "certificate", Sub = new[] { "product", "version" }, Dupe = new[] { "product", "key" },
                Fields = new[] { F("product", "Product", "text"), F("version", "Version", "text"), F("key", "Licence key", "hidden"), F("name", "Licensed to", "text"), F("email", "Registered email", "email"), F("bought", "Bought on", "date"), Exp("expires", "Expires"), F("url", "Download page", "url") } },
            new TypeDef { Id = "bank", Name = "Bank account", Lower = "bank account", Plural = "Bank accounts", Icon = "building-columns", Sub = new[] { "bank", "accountType" }, Dupe = new[] { "account", "routing", "iban" },
                Fields = new[] { F("bank", "Bank", "text"), F("holder", "Account holder", "text"), F("accountType", "Account type", "text"), F("account", "Account number", "hidden"), F("routing", "Sort code / routing number", "hidden"), F("iban", "IBAN", "hidden"), F("swift", "SWIFT / BIC", "text"), F("pin", "PIN", "hidden"), F("phone", "Bank's phone", "phone") } },
            new TypeDef { Id = "passport", Name = "Passport", Lower = "passport", Plural = "Passports", Icon = "passport", Sub = new[] { "name", "country" }, Dupe = new[] { "number" },
                Fields = new[] { F("name", "Full name", "text"), F("number", "Passport number", "hidden"), F("country", "Issuing country", "text"), F("nationality", "Nationality", "text"), F("birth", "Date of birth", "date"), F("issued", "Issued on", "date"), Exp("expires", "Expires"), F("authority", "Issuing authority", "text") } },
            new TypeDef { Id = "driving", Name = "Driving licence", Lower = "driving licence", Plural = "Driving licences", Icon = "car", Sub = new[] { "name", "country" }, Dupe = new[] { "number" },
                Fields = new[] { F("name", "Full name", "text"), F("number", "Licence number", "hidden"), F("country", "Country / state", "text"), F("categories", "Categories", "text"), F("birth", "Date of birth", "date"), F("issued", "Issued on", "date"), Exp("expires", "Expires") } },
            new TypeDef { Id = "wifi", Name = "Wi-Fi network", Lower = "Wi-Fi network", Plural = "Wi-Fi networks", Icon = "wifi", Sub = new[] { "ssid", "security" }, Dupe = new[] { "ssid" },
                Fields = new[] { F("ssid", "Network name (SSID)", "text"), F("password", "Password", "hidden"), new FieldDef("security", "Security", "text") { Hint = "WPA2, WPA3, open..." } } },
            new TypeDef { Id = "server", Name = "Server / database", Lower = "server / database", Plural = "Servers and databases", Icon = "server", Sub = new[] { "host", "username" }, Dupe = new[] { "host", "port", "username" },
                Fields = new[] { F("host", "Host / address", "text"), F("port", "Port", "text"), F("username", "Username", "text"), F("password", "Password", "hidden"), F("database", "Database", "text"), new FieldDef("kind", "Type", "text") { Hint = "SSH, PostgreSQL, MySQL..." }, F("url", "Admin page", "url") } },
            new TypeDef { Id = "membership", Name = "Membership", Lower = "membership", Plural = "Memberships", Icon = "address-card", Sub = new[] { "org", "name" }, Dupe = new[] { "org", "number" },
                Fields = new[] { F("org", "Organisation", "text"), F("name", "Member name", "text"), F("number", "Member number", "text"), F("pin", "PIN / password", "hidden"), F("since", "Member since", "date"), Exp("expires", "Expires"), F("phone", "Phone", "phone"), F("url", "Website", "url") } },
            // v2.1: a document - its files (scans, PDFs) are the point; a few fields to find it again
            new TypeDef { Id = "document", Name = "Document", Lower = "document", Plural = "Documents", Icon = "file-lines", Sub = new[] { "kind", "number" }, Dupe = new[] { "kind", "number" },
                Fields = new[] { new FieldDef("kind", "Kind of document", "text") { Hint = "Contract, insurance policy, certificate..." }, F("number", "Reference number", "text"), F("issuer", "Issued by", "text"), F("issued", "Issued on", "date"), Exp("expires", "Expires") } },
            new TypeDef { Id = "wallet", Name = "Crypto wallet", Lower = "crypto wallet", Plural = "Crypto wallets", Icon = "wallet", Sub = new[] { "app", "network" }, Dupe = new[] { "address", "seed" },
                Fields = new[] { F("app", "Wallet app", "text"), F("network", "Network / coin", "text"), F("address", "Receiving address", "text"), F("seed", "Recovery (seed) phrase", "secretnote"), F("password", "Wallet password / PIN", "hidden") } },
        };
        // v2.3: passkeys are made by websites (through the extension), never typed in - only the names can be changed
        public static readonly TypeDef PasskeyType = new TypeDef { Id = "passkey", Name = "Passkey", Lower = "passkey", Plural = "Passkeys", Icon = "fingerprint", Sub = new[] { "userName", "rpId" }, Dupe = new[] { "credentialId" },
            Fields = new[] { C("rpId", "Website (rpId)"), F("userName", "User name", "text"), F("displayName", "Display name", "text"), C("algorithm", "Key type"), C("credentialId", "Credential ID"), C("userHandle", "User handle"),
                C("publicKey", "Public key"), new FieldDef("privateKey", "Private key", "hidden") { Computed = true } } };
        static ItemTypes() { All.Insert(4, PasskeyType); }
        public static TypeDef Get(string id) { return All.FirstOrDefault(t => t.Id == id); }
        public static string Name(string id) { var t = Get(id); return t != null ? t.Name : "Item"; }
        public static string Icon(string id) { var t = Get(id); return t != null ? t.Icon : "shapes"; }
        public static bool IsNew(string id) { var t = Get(id); return t != null && !t.Legacy; }

        // a wallet's recovery phrase
        public static bool IsSeed(Item it, CustomField f) { return it != null && f != null && it.Type == "wallet" && f.Key == "seed"; }
        public static CustomField Field(Item it, string key) { return it.Fields.FirstOrDefault(f => f.Key == key); }
        public static string Val(Item it, string key) { var f = Field(it, key); return f == null ? "" : f.Value ?? ""; }
        public static void Set(Item it, string key, string value) { Ensure(it); var f = Field(it, key); if (f != null) f.Value = value ?? ""; }

        // the type's built-in fields exist (in order, first); your own fields after them
        public static void Ensure(Item it)
        {
            var t = Get(it.Type); if (t == null || t.Legacy) return;
            var builtIn = new List<CustomField>();
            foreach (var d in t.Fields)
            {
                var f = Field(it, d.Key) ?? new CustomField { Key = d.Key, Label = d.Label, Kind = d.Kind, Expires = d.Expires };
                f.Kind = d.Kind; if (string.IsNullOrEmpty(f.Label)) f.Label = d.Label; if (d.Expires) f.Expires = true;
                builtIn.Add(f);
            }
            var known = new HashSet<string>(t.Fields.Select(d => d.Key));
            it.Fields = builtIn.Concat(it.Fields.Where(f => !known.Contains(f.Key ?? ""))).ToList();
        }
        // fields to show, in order: the type's own, then yours (anything with an unknown key counts as yours)
        public static List<CustomField> Ordered(Item it)
        {
            var t = Get(it.Type); if (t == null || t.Legacy) return it.Fields.ToList();
            var o = new List<CustomField>();
            foreach (var d in t.Fields) { var f = Field(it, d.Key); if (f != null) o.Add(f); }
            return o.Concat(it.Fields.Where(f => !o.Contains(f))).ToList();
        }
        public static bool IsBuiltIn(Item it, CustomField f) { var t = Get(it.Type); return t != null && !t.Legacy && f.Key.Length > 0 && t.Fields.Any(d => d.Key == f.Key); }
        public static FieldDef Def(Item it, CustomField f) { var t = Get(it.Type); return t == null || f.Key.Length == 0 ? null : t.Fields.FirstOrDefault(d => d.Key == f.Key); }

        // the list's second line: visible fields only
        public static string Subtitle(Item it)
        {
            var t = Get(it.Type); if (t == null || t.Legacy) return it.Folder;
            return string.Join(" - ", t.Sub.Select(k => { var f = Field(it, k); return f == null || IsHidden(f) ? "" : f.Value; }).Where(x => !string.IsNullOrEmpty(x)));
        }

        // a deep copy (new field ids when asked, for "Duplicate"); the editor edits a copy with the same ids
        public static Item Clone(Item it, bool asNew)
        {
            Item c;
            using (var ms = new MemoryStream())
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Item));
                ser.WriteObject(ms, it); ms.Position = 0; c = (Item)ser.ReadObject(ms);
            }
            if (asNew)
            {
                c.Id = Guid.NewGuid().ToString("N"); c.Created = c.Updated = DateTime.Now; c.Deleted = null;
                c.OldPasswords.Clear(); c.OldPasswordDates.Clear();
                foreach (var f in c.Fields) { f.Id = Guid.NewGuid().ToString("N"); f.Old.Clear(); f.OldDates.Clear(); }
            }
            return c;
        }

        // ---- templates: the layout without values
        public static ItemTemplate MakeTemplate(Item it, string name)
        {
            return new ItemTemplate { Name = name, Type = it.Type, Fields = Ordered(it).Select(f => new CustomField { Key = f.Key, Label = f.Label, Kind = f.Kind, Expires = f.Expires }).ToList() };
        }
        public static Item FromTemplate(ItemTemplate t)
        {
            var it = new Item { Type = t.Type, Fields = t.Fields.Select(f => new CustomField { Key = f.Key ?? "", Label = f.Label ?? "", Kind = string.IsNullOrEmpty(f.Kind) ? "text" : f.Kind, Expires = f.Expires }).ToList() };
            Ensure(it); return it;
        }

        // ---- history for hidden fields: when a hidden value changes, the old one is kept (newest first, 10 at most)
        public static void RecordFieldChanges(Item before, Item after, DateTime when)
        {
            if (before == null) return;
            foreach (var f in after.Fields)
            {
                var old = before.Fields.FirstOrDefault(x => x.Id == f.Id);
                if (old == null || !IsHidden(old) || string.IsNullOrEmpty(old.Value) || old.Value == f.Value) continue;
                f.Old.Insert(0, old.Value); f.OldDates.Insert(0, when.ToString("o"));
                while (f.OldDates.Count < f.Old.Count) f.OldDates.Add("");
                while (f.Old.Count > Organize.HistoryMax) { f.Old.RemoveAt(f.Old.Count - 1); f.OldDates.RemoveAt(f.OldDates.Count - 1); }
            }
        }
        public static int HistoryCount(Item it) { return it.Fields.Sum(f => f.Old.Count); }

        // ---- search: labels and visible values only - never a hidden field, never a recovery phrase
        public static IEnumerable<string> SearchText(Item it)
        {
            foreach (var f in it.Fields)
            {
                if (it.Type == "passkey" && (f.Key == "credentialId" || f.Key == "userHandle" || f.Key == "publicKey")) continue;   // v2.3: ids and keys aren't words
                yield return f.Label;
                if (!IsHidden(f) && !IsSeed(it, f)) yield return f.Value;
            }
        }
        // every value (for the crash-report scrub)
        public static IEnumerable<string> AllValues(Item it) { return it.Fields.SelectMany(f => new[] { f.Value }.Concat(f.Old)); }

        // ---- duplicates
        public static string DuplicateKey(Item it)
        {
            var t = Get(it.Type); Func<string, string> n = s => (s ?? "").Trim().ToLowerInvariant();
            var parts = t == null ? new string[0] : t.Dupe.Select(k => n(Val(it, k))).ToArray();
            if (parts.All(p => p.Length == 0)) return it.Type + "|t|" + n(it.Title);
            return it.Type + "|" + string.Join("|", parts);
        }

        // ---- the phone copy. Hallow Remote's phone page (1.10) only knows the 1.5 fields, so the new types and your own fields go
        // in a way it already shows: visible fields as "Label: value" lines at the top of the notes, the first hidden field in the
        // password slot (Show / Copy), further hidden fields together in the PIN slot (also hidden until Show), 2FA as the 2FA code.
        // A "fields" list is sent as well for a later Remote. A wallet's recovery phrase is left out unless that wallet allows it.
        public static Dictionary<string, object> PhoneItem(Item i)
        {
            var d = new Dictionary<string, object> {
                { "type", i.Type }, { "title", i.Title }, { "folder", i.Folder }, { "fav", i.Favorite }, { "user", i.Username }, { "pw", i.Password }, { "urls", i.Urls.ToList() }, { "totp", i.Totp }, { "notes", i.Notes },
                { "cardHolder", i.CardHolder }, { "cardNumber", i.CardNumber }, { "expiry", i.Expiry }, { "cvc", i.Cvc }, { "pin", i.Pin },
                { "name", i.FullName }, { "email", i.Email }, { "phone", i.Phone }, { "address", string.Join(", ", new[] { i.Address, i.City, i.PostCode, i.Country }.Where(x => !string.IsNullOrEmpty(x))) } };
            var fields = Ordered(i).Where(f => !string.IsNullOrEmpty(f.Value) && (!IsSeed(i, f) || i.SeedOnPhone)).ToList();
            if (!IsNew(i.Type) && fields.Count == 0) return d;
            if (IsNew(i.Type) || Get(i.Type) == null) { d["type"] = Name(i.Type); d["kind"] = i.Type; }   // Remote shows the type in the list; "kind" keeps the id
            var lines = new List<string>(); var hidden = new List<CustomField>(); var where = new List<string>();
            foreach (var f in fields)
            {
                if (f.Kind == "totp" && string.IsNullOrEmpty((string)d["totp"]) && Totp.Parse(f.Value) != null) { d["totp"] = f.Value; continue; }
                if (IsHidden(f)) { hidden.Add(f); continue; }
                if (f.Kind == "url" && Domain.Parse(f.Value) != null) { ((List<string>)d["urls"]).Add(f.Value); continue; }
                lines.Add(f.Label + ": " + (f.Kind == "date" ? Dates.Show(f.Value) : f.Value));
            }
            if (hidden.Count > 0)
            {
                var slots = new[] { "pw", "pin", "cvc" }.Where(s => string.IsNullOrEmpty(d[s] as string)).ToList();
                if (slots.Count > 0 && string.IsNullOrEmpty(d["pw"] as string)) { d["pw"] = hidden[0].Value; where.Add("Password = " + hidden[0].Label); hidden.RemoveAt(0); slots.Remove("pw"); }
                if (hidden.Count > 0)
                {
                    var slot = slots.FirstOrDefault() ?? "pw";
                    var block = string.Join("\n", hidden.Select(f => f.Label + ": " + f.Value));
                    d[slot] = string.IsNullOrEmpty(d[slot] as string) ? block : d[slot] + "\n" + block;
                    where.Add((slot == "pin" ? "PIN" : slot == "cvc" ? "Security code" : "Password") + " = " + string.Join(", ", hidden.Select(f => f.Label)));
                }
            }
            if (where.Count > 0) lines.Add("Hidden: " + string.Join("; ", where) + " (tap Show)");
            if (lines.Count > 0) d["notes"] = string.Join("\n", lines) + (string.IsNullOrEmpty(i.Notes) ? "" : "\n\n" + i.Notes);
            d["fields"] = fields.Select(f => (object)new Dictionary<string, object> { { "label", f.Label }, { "kind", f.Kind }, { "value", f.Value }, { "hidden", IsHidden(f) } }).ToList();
            return d;
        }

        // ---- CSV: a readable block in the note, and the exact fields as JSON in "hallow_fields" (Hallow Keys reads them back)
        public const string CsvMarker = "--- Hallow Keys fields ---";
        public static List<CustomField> CsvFields(Item i, bool includeSeeds) { return Ordered(i).Where(f => !string.IsNullOrEmpty(f.Value) && (includeSeeds || !IsSeed(i, f))).ToList(); }
        public static string CsvNote(Item i, bool includeSeeds)
        {
            var fs = CsvFields(i, includeSeeds); if (fs.Count == 0) return i.Notes;
            var block = CsvMarker + "\n" + string.Join("\n", fs.Select(f => f.Label + ": " + f.Value));
            return string.IsNullOrEmpty(i.Notes) ? block : i.Notes + "\n\n" + block;
        }
        public static string CsvJson(Item i, bool includeSeeds)
        {
            var fs = CsvFields(i, includeSeeds); if (fs.Count == 0) return "";
            return new JavaScriptSerializer().Serialize(fs.Select(f => new Dictionary<string, object> { { "key", f.Key }, { "label", f.Label }, { "kind", f.Kind }, { "value", f.Value }, { "expires", f.Expires } }).ToList());
        }
        public static List<CustomField> FromCsvJson(string json)
        {
            var o = new List<CustomField>(); if (string.IsNullOrWhiteSpace(json)) return o;
            try
            {
                var list = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
                foreach (var d in list)
                {
                    Func<string, string> s = k => { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; };
                    object e; var kind = s("kind"); if (!KindName.ContainsKey(kind)) kind = "text";
                    o.Add(new CustomField { Key = s("key"), Label = s("label"), Kind = kind, Value = s("value"), Expires = d.TryGetValue("expires", out e) && e is bool && (bool)e });
                }
            }
            catch { }
            return o;
        }
        public static bool SafeTypeId(string s) { return !string.IsNullOrEmpty(s) && s.Length <= 30 && s.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')); }
    }

    // ---- dates: stored as yyyy-MM-dd
    public static class Dates
    {
        static readonly string[] Formats = { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d", "yyyy.MM.dd", "d MMM yyyy", "d MMMM yyyy", "MMM yyyy", "yyyy-MM" };
        public static bool TryParse(string s, out DateTime d)
        {
            s = (s ?? "").Trim(); d = DateTime.MinValue; if (s.Length == 0) return false;
            if (DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
            return DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d) && d.Year > 1800 && d.Year < 3000;
        }
        public static string Normalize(string s) { DateTime d; return TryParse(s, out d) ? d.ToString("yyyy-MM-dd") : (s ?? "").Trim(); }
        public static string Show(string s) { DateTime d; return TryParse(s, out d) ? d.ToString("d MMM yyyy") : s; }
    }

    // ---- expiry dates and reminders
    public class ExpiryEntry { public Item Item; public string FieldId = "", Label = ""; public DateTime Date; public int DaysLeft; public string Key; }
    public static class Expiry
    {
        // every expiry date in the vault (trash not included)
        public static List<ExpiryEntry> All(VaultData d, DateTime now)
        {
            var o = new List<ExpiryEntry>();
            foreach (var it in d.Items)
            {
                string m, y;
                if (it.Type == "card" && FormData.ParseExpiry(it.Expiry, out m, out y))
                {
                    var last = new DateTime(int.Parse(y), int.Parse(m), 1).AddMonths(1).AddDays(-1);
                    o.Add(Entry(it, "card", "Card expiry", last, now));
                }
                foreach (var f in it.Fields)
                {
                    DateTime dt; if (f.Kind != "date" || !f.Expires || !Dates.TryParse(f.Value, out dt)) continue;
                    o.Add(Entry(it, f.Id, string.IsNullOrEmpty(f.Label) ? "Expires" : f.Label, dt, now));
                }
            }
            return o.OrderBy(e => e.Date).ToList();
        }
        static ExpiryEntry Entry(Item it, string fid, string label, DateTime date, DateTime now)
        {
            return new ExpiryEntry { Item = it, FieldId = fid, Label = label, Date = date.Date, DaysLeft = (int)(date.Date - now.Date).TotalDays, Key = it.Id + "|" + fid + "|" + date.ToString("yyyy-MM-dd") };
        }
        // expired, or expiring within `days`
        public static List<ExpiryEntry> Soon(VaultData d, DateTime now, int days) { return All(d, now).Where(e => e.DaysLeft <= days).ToList(); }

        // reminders not shown yet: each date is mentioned once when it comes within `days`, and once more when it has passed.
        // Marks them as shown (in the vault). Returns the text for the notification - counts and kinds only, never titles or values.
        public static string DueMessage(VaultData d, DateTime now, int days)
        {
            var soon = Soon(d, now, days); var fresh = new List<ExpiryEntry>(); var expiredNew = new List<ExpiryEntry>();
            foreach (var e in soon)
            {
                var stage = e.DaysLeft < 0 ? "expired" : "soon";
                if (d.ExpiryNotified.Contains(e.Key + "|" + stage)) continue;
                d.ExpiryNotified.Add(e.Key + "|" + stage);
                if (stage == "expired")
                {
                    if (!d.ExpiryNotified.Contains(e.Key + "|soon")) d.ExpiryNotified.Add(e.Key + "|soon");
                    if (e.DaysLeft >= -90) expiredNew.Add(e);   // long-expired dates (an old card) are marked quietly, not announced
                }
                else fresh.Add(e);
            }
            // forget reminders for dates that no longer exist (edited or deleted), so the list doesn't grow
            var live = new HashSet<string>(All(d, now).Select(e => e.Key));
            d.ExpiryNotified.RemoveAll(k => { var i = k.LastIndexOf('|'); return i < 0 || !live.Contains(k.Substring(0, i)); });
            if (fresh.Count == 0 && expiredNew.Count == 0) return null;
            var parts = new List<string>();
            if (fresh.Count > 0) parts.Add(Count(fresh) + (fresh.Count == 1 ? " expires" : " expire") + " within " + days + " days");
            if (expiredNew.Count > 0) parts.Add(Count(expiredNew) + (expiredNew.Count == 1 ? " has" : " have") + " expired");
            return string.Join("; ", parts) + ". Open Hallow Keys > Expiring to see which.";
        }
        static string Count(List<ExpiryEntry> es)
        {
            var g = es.GroupBy(e => e.Item.Type).Select(x => x.Count() + " " + (x.Count() == 1 ? ItemTypes.Get(x.Key) != null ? ItemTypes.Get(x.Key).Lower : "item" : ItemTypes.Get(x.Key) != null ? ItemTypes.Get(x.Key).Plural.ToLowerInvariant().Replace("ssh", "SSH").Replace("api", "API").Replace("wi-fi", "Wi-Fi") : "items")).ToList();
            return g.Count == 1 ? g[0] : string.Join(", ", g.Take(g.Count - 1)) + " and " + g[g.Count - 1];
        }
        public static string Describe(int daysLeft)
        {
            if (daysLeft < 0) return "expired " + (-daysLeft == 1 ? "yesterday" : -daysLeft + " days ago");
            if (daysLeft == 0) return "expires today";
            return daysLeft == 1 ? "expires tomorrow" : "expires in " + daysLeft + " days";
        }
    }

    // ---- SSH keys
    public static class SshKeys
    {
        public class Info { public string Type = "", Pretty = "", Fingerprint = "", Comment = ""; public int Bits; }

        // "ssh-ed25519 AAAA... comment" -> the key blob, or null
        public static byte[] PublicBlob(string line)
        {
            var parts = (line ?? "").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                byte[] b; try { b = Convert.FromBase64String(parts[i + 1]); } catch { continue; }
                try { int p = 0; var t = Encoding.ASCII.GetString(ReadString(b, ref p)); if (t == parts[i]) return b; } catch { }
            }
            return null;
        }
        // an OpenSSH private key ("-----BEGIN OPENSSH PRIVATE KEY-----", encrypted or not): its public part, or null
        public static byte[] PublicBlobFromPrivate(string pem)
        {
            var s = (pem ?? "").Trim(); const string begin = "-----BEGIN OPENSSH PRIVATE KEY-----", end = "-----END OPENSSH PRIVATE KEY-----";
            int a = s.IndexOf(begin), z = s.IndexOf(end); if (a < 0 || z < a) return null;
            byte[] b; try { b = Convert.FromBase64String(new string(s.Substring(a + begin.Length, z - a - begin.Length).Where(c => !char.IsWhiteSpace(c)).ToArray())); } catch { return null; }
            var magic = Encoding.ASCII.GetBytes("openssh-key-v1\0"); if (b.Length < magic.Length || !b.Take(magic.Length).SequenceEqual(magic)) return null;
            try
            {
                int p = magic.Length; ReadString(b, ref p); ReadString(b, ref p); ReadString(b, ref p);
                uint n = ReadU32(b, ref p); if (n < 1) return null;
                return ReadString(b, ref p);
            }
            catch { return null; }
        }
        public static string Fingerprint(byte[] blob) { using (var h = SHA256.Create()) return "SHA256:" + Convert.ToBase64String(h.ComputeHash(blob)).TrimEnd('='); }
        public static Info Describe(byte[] blob)
        {
            if (blob == null) return null;
            var i = new Info { Fingerprint = Fingerprint(blob) };
            try
            {
                int p = 0; i.Type = Encoding.ASCII.GetString(ReadString(blob, ref p));
                if (i.Type == "ssh-rsa") { ReadString(blob, ref p); var n = ReadString(blob, ref p); i.Bits = BitLength(n); i.Pretty = "RSA " + i.Bits; }
                else if (i.Type == "ssh-ed25519") { i.Bits = 256; i.Pretty = "ED25519"; }
                else if (i.Type == "sk-ssh-ed25519@openssh.com") { i.Bits = 256; i.Pretty = "ED25519-SK"; }
                else if (i.Type.StartsWith("ecdsa-sha2-nistp")) { int.TryParse(i.Type.Substring(16), out i.Bits); i.Pretty = "ECDSA " + i.Bits; }
                else if (i.Type.StartsWith("sk-ecdsa-sha2-nistp256")) { i.Bits = 256; i.Pretty = "ECDSA-SK 256"; }
                else if (i.Type == "ssh-dss") { var pp = ReadString(blob, ref p); i.Bits = BitLength(pp); i.Pretty = "DSA " + i.Bits; }
                else i.Pretty = i.Type;
            }
            catch { return null; }
            return i;
        }
        // the key's description from whatever was pasted: the public key line first, else the private key's public part
        public static Info FromItem(string publicKey, string privateKey)
        {
            return Describe(PublicBlob(publicKey)) ?? Describe(PublicBlobFromPrivate(privateKey));
        }
        // "ssh-ed25519 AAAA..." from the private key's public part (when only the private key was pasted)
        public static string PublicLine(byte[] blob, string comment)
        {
            int p = 0; var t = Encoding.ASCII.GetString(ReadString(blob, ref p));
            return t + " " + Convert.ToBase64String(blob) + (string.IsNullOrEmpty(comment) ? "" : " " + comment);
        }

        // a new RSA key (CNG), as OpenSSH writes it: "-----BEGIN OPENSSH PRIVATE KEY-----" (not encrypted - it's kept inside the
        // encrypted vault) and "ssh-rsa AAAA... comment"
        public static void GenerateRsa(int bits, string comment, out string privateKey, out string publicKey)
        {
            if (bits != 2048 && bits != 3072 && bits != 4096) throw new ArgumentException("bits");
            comment = new string((comment ?? "").Where(c => c >= 32 && c < 127).ToArray());
            RSAParameters k;
            using (var rsa = new RSACng(bits)) k = rsa.ExportParameters(true);
            try
            {
                var pub = new MemoryStream(); PutString(pub, Encoding.ASCII.GetBytes("ssh-rsa")); PutMpint(pub, k.Exponent); PutMpint(pub, k.Modulus);
                var blob = pub.ToArray();
                var priv = new MemoryStream(); var check = Rng.Bytes(4);
                priv.Write(check, 0, 4); priv.Write(check, 0, 4);
                PutString(priv, Encoding.ASCII.GetBytes("ssh-rsa")); PutMpint(priv, k.Modulus); PutMpint(priv, k.Exponent); PutMpint(priv, k.D);
                PutMpint(priv, k.InverseQ); PutMpint(priv, k.P); PutMpint(priv, k.Q); PutString(priv, Encoding.ASCII.GetBytes(comment));
                for (byte pad = 1; priv.Length % 8 != 0; pad++) priv.WriteByte(pad);
                var privBytes = priv.ToArray();
                var all = new MemoryStream(); var magic = Encoding.ASCII.GetBytes("openssh-key-v1\0"); all.Write(magic, 0, magic.Length);
                PutString(all, Encoding.ASCII.GetBytes("none")); PutString(all, Encoding.ASCII.GetBytes("none")); PutString(all, new byte[0]);
                PutU32(all, 1); PutString(all, blob); PutString(all, privBytes);
                var allBytes = all.ToArray();
                var b64 = Convert.ToBase64String(allBytes);
                var sb = new StringBuilder("-----BEGIN OPENSSH PRIVATE KEY-----\n");
                for (int i = 0; i < b64.Length; i += 70) sb.Append(b64.Substring(i, Math.Min(70, b64.Length - i))).Append('\n');
                sb.Append("-----END OPENSSH PRIVATE KEY-----\n");
                privateKey = sb.ToString(); publicKey = "ssh-rsa " + Convert.ToBase64String(blob) + (comment.Length > 0 ? " " + comment : "");
                Array.Clear(privBytes, 0, privBytes.Length); Array.Clear(allBytes, 0, allBytes.Length); priv.SetLength(0); all.SetLength(0);
            }
            finally { foreach (var a in new[] { k.D, k.P, k.Q, k.DP, k.DQ, k.InverseQ }) if (a != null) Array.Clear(a, 0, a.Length); }
        }

        static byte[] ReadString(byte[] b, ref int p) { uint n = ReadU32(b, ref p); if (n > b.Length - p) throw new FormatException(); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, (int)n); p += (int)n; return r; }
        static uint ReadU32(byte[] b, ref int p) { if (p + 4 > b.Length) throw new FormatException(); uint v = (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); p += 4; return v; }
        static void PutU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void PutString(Stream s, byte[] b) { PutU32(s, (uint)b.Length); s.Write(b, 0, b.Length); }
        // SSH mpint: big-endian, no leading zero bytes, one 0 byte in front when the top bit is set (positive numbers)
        static void PutMpint(Stream s, byte[] be)
        {
            int i = 0; while (i < be.Length && be[i] == 0) i++;
            var len = be.Length - i; bool pad = len > 0 && (be[i] & 0x80) != 0;
            PutU32(s, (uint)(len + (pad ? 1 : 0))); if (pad) s.WriteByte(0); s.Write(be, i, len);
        }
        static int BitLength(byte[] be) { int i = 0; while (i < be.Length && be[i] == 0) i++; if (i == be.Length) return 0; int bits = (be.Length - i - 1) * 8; byte top = be[i]; while (top != 0) { bits++; top >>= 1; } return bits; }
    }
}
