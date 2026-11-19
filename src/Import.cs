// Hallow Keys - importing from NordPass (CSV export), any CSV with a header row, and Bitwarden (unencrypted JSON export).
// NordPass CSV columns (as its export writes them; any order, missing ones are fine): name, url, additional_urls, username,
// password, note, cardholdername, cardnumber, cvc, pin, expirydate, zipcode, folder, full_name, phone_number, email,
// address1, address2, city, country, state, type ("password", "note", "credit_card", "identity", "folder").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class Import
    {
        public class Result { public List<Item> Items = new List<Item>(); public List<string> Folders = new List<string>(); public int Skipped; public string Source; }

        // RFC 4180: quoted fields, doubled quotes, line breaks inside quotes
        public static List<List<string>> Csv(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var f = new StringBuilder(); bool q = false;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (q) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; } else f.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { row.Add(f.ToString()); f.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { row.Add(f.ToString()); f.Clear(); rows.Add(row); row = new List<string>(); }
                else f.Append(c);
            }
            if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); rows.Add(row); }
            return rows.Where(r => r.Any(x => x.Length > 0)).ToList();
        }

        public static Result FromFile(string path)
        {
            if (new FileInfo(path).Length > 50L * 1024 * 1024) throw new VaultException("That file is too big to import.");
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith("{")) return Bitwarden(text);
            return CsvItems(text);
        }

        static string Col(Dictionary<string, int> h, List<string> r, params string[] names)
        {
            foreach (var n in names) { int i; if (h.TryGetValue(n, out i) && i < r.Count && !string.IsNullOrEmpty(r[i])) return r[i].Trim(); }
            return "";
        }
        // NordPass and generic CSV (Chrome, Edge, Firefox, 1Password, LastPass ... headers)
        public static Result CsvItems(string text)
        {
            var rows = Csv(text); var res = new Result();
            if (rows.Count == 0) throw new VaultException("The file is empty.");
            var h = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows[0].Count; i++) { var k = rows[0][i].Trim().ToLowerInvariant().Replace(" ", "_"); if (!h.ContainsKey(k)) h[k] = i; }
            if (!h.ContainsKey("password") && !h.ContainsKey("cardnumber") && !h.ContainsKey("note") && !h.ContainsKey("notes")) throw new VaultException("That doesn't look like a password export (no password column in the first row).");
            res.Source = h.ContainsKey("cardholdername") || h.ContainsKey("additional_urls") ? "NordPass" : "CSV";
            foreach (var r in rows.Skip(1))
            {
                var type = Col(h, r, "type").ToLowerInvariant();
                var folder = Col(h, r, "folder", "grouping", "group", "category");
                if (type == "folder") { var fn = Col(h, r, "name"); if (fn.Length > 0 && !res.Folders.Contains(fn)) res.Folders.Add(fn); continue; }
                var it = new Item { Title = Col(h, r, "name", "title", "account", "service"), Folder = folder };
                var url = Col(h, r, "url", "website", "login_uri", "uri", "web_site", "login_url");
                if (url.Length > 0) it.Urls.Add(url);
                foreach (var extra in Col(h, r, "additional_urls").Split(new[] { ',', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)) if (!it.Urls.Contains(extra.Trim())) it.Urls.Add(extra.Trim());
                it.Username = Col(h, r, "username", "login", "user", "login_username", "user_name");
                it.Password = Col(h, r, "password", "login_password", "pass");
                it.Notes = Col(h, r, "note", "notes", "extra", "comments", "comment");
                it.Totp = Col(h, r, "totp", "otp", "login_totp", "otpauth");
                it.CardHolder = Col(h, r, "cardholdername", "cardholder", "card_holder"); it.CardNumber = Col(h, r, "cardnumber", "card_number", "number");
                it.Cvc = Col(h, r, "cvc", "cvv", "security_code"); it.Pin = Col(h, r, "pin"); it.Expiry = Col(h, r, "expirydate", "expiry", "expiration");
                it.FullName = Col(h, r, "full_name", "fullname"); it.Email = Col(h, r, "email"); it.Phone = Col(h, r, "phone_number", "phone");
                it.Address = string.Join(", ", new[] { Col(h, r, "address1", "address"), Col(h, r, "address2") }.Where(x => x.Length > 0));
                it.City = Col(h, r, "city"); it.PostCode = Col(h, r, "zipcode", "zip", "postcode", "postal_code"); it.Country = Col(h, r, "country");
                it.Tags = Organize.ParseTags(Col(h, r, "tags", "tag", "labels"));
                Unguard(it);
                if (type == "credit_card" || type == "card" || (type.Length == 0 && it.CardNumber.Length > 0)) it.Type = "card";
                else if (type == "identity" || (type.Length == 0 && it.FullName.Length > 0 && it.Password.Length == 0)) it.Type = "identity";
                else if (type == "note" || type == "secure_note" || (type.Length == 0 && it.Password.Length == 0 && it.Username.Length == 0 && it.Urls.Count == 0)) it.Type = "note";
                else it.Type = "login";
                if (it.Title.Length == 0) it.Title = it.Urls.Select(Domain.HostOf).FirstOrDefault(x => x != null) ?? (it.Type == "card" ? "Card" : "Imported item");
                if (it.Type == "note" && it.Notes.Length == 0) { res.Skipped++; continue; }
                if (folder.Length > 0 && !res.Folders.Contains(folder)) res.Folders.Add(folder);
                res.Items.Add(it);
            }
            return res;
        }

        // v1.3: Hallow Keys' own CSV export puts a ' in front of text that a spreadsheet would run as a formula - take it off again
        static string Ug(string s) { return s != null && s.Length > 1 && s[0] == (char)39 && "=+-@\t\r".IndexOf(s[1]) >= 0 ? s.Substring(1) : s; }
        static void Unguard(Item it)
        {
            it.Title = Ug(it.Title); it.Folder = Ug(it.Folder); it.Username = Ug(it.Username); it.Notes = Ug(it.Notes); it.CardHolder = Ug(it.CardHolder); it.FullName = Ug(it.FullName);
            it.Email = Ug(it.Email); it.Phone = Ug(it.Phone); it.Address = Ug(it.Address); it.City = Ug(it.City); it.PostCode = Ug(it.PostCode); it.Country = Ug(it.Country);
            it.Urls = it.Urls.Select(Ug).ToList(); it.Tags = it.Tags.Select(Ug).ToList();
        }

        // Bitwarden "JSON" export (not the encrypted one)
        public static Result Bitwarden(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            var d = js.Deserialize<Dictionary<string, object>>(text);
            object o;
            if (d.TryGetValue("encrypted", out o) && o is bool && (bool)o) throw new VaultException("That's an encrypted Bitwarden export - export it again as JSON (not encrypted) to import it.");
            var res = new Result { Source = "Bitwarden" };
            var folders = new Dictionary<string, string>();
            if (d.TryGetValue("folders", out o) && o is System.Collections.ArrayList)
                foreach (Dictionary<string, object> f in ((System.Collections.ArrayList)o).OfType<Dictionary<string, object>>()) { var id = S(f, "id"); var n = S(f, "name"); if (id.Length > 0) folders[id] = n; if (n.Length > 0 && !res.Folders.Contains(n)) res.Folders.Add(n); }
            if (!d.TryGetValue("items", out o) || !(o is System.Collections.ArrayList)) throw new VaultException("That doesn't look like a Bitwarden export.");
            foreach (var x in ((System.Collections.ArrayList)o).OfType<Dictionary<string, object>>())
            {
                var it = new Item { Title = S(x, "name"), Notes = S(x, "notes"), Favorite = x.ContainsKey("favorite") && x["favorite"] is bool && (bool)x["favorite"] };
                string fid = S(x, "folderId"), fname; if (fid.Length > 0 && folders.TryGetValue(fid, out fname)) it.Folder = fname;
                int type = x.ContainsKey("type") && x["type"] is int ? (int)x["type"] : 1;
                var login = x.ContainsKey("login") ? x["login"] as Dictionary<string, object> : null;
                var card = x.ContainsKey("card") ? x["card"] as Dictionary<string, object> : null;
                var idn = x.ContainsKey("identity") ? x["identity"] as Dictionary<string, object> : null;
                if (type == 1 && login != null)
                {
                    it.Type = "login"; it.Username = S(login, "username"); it.Password = S(login, "password"); it.Totp = S(login, "totp");
                    if (login.ContainsKey("uris") && login["uris"] is System.Collections.ArrayList) foreach (var u in ((System.Collections.ArrayList)login["uris"]).OfType<Dictionary<string, object>>()) { var s = S(u, "uri"); if (s.Length > 0) it.Urls.Add(s); }
                }
                else if (type == 3 && card != null)
                {
                    it.Type = "card"; it.CardHolder = S(card, "cardholderName"); it.CardNumber = S(card, "number"); it.Cvc = S(card, "code");
                    var m = S(card, "expMonth"); var y = S(card, "expYear"); it.Expiry = m.Length > 0 || y.Length > 0 ? m.PadLeft(2, '0') + "/" + y : "";
                }
                else if (type == 4 && idn != null)
                {
                    it.Type = "identity"; it.FullName = string.Join(" ", new[] { S(idn, "firstName"), S(idn, "middleName"), S(idn, "lastName") }.Where(s => s.Length > 0));
                    it.Email = S(idn, "email"); it.Phone = S(idn, "phone"); it.Address = string.Join(", ", new[] { S(idn, "address1"), S(idn, "address2") }.Where(s => s.Length > 0));
                    it.City = S(idn, "city"); it.PostCode = S(idn, "postalCode"); it.Country = S(idn, "country");
                }
                else if (type == 2) it.Type = "note";
                else { res.Skipped++; continue; }
                if (it.Title.Length == 0) it.Title = "Imported item";
                res.Items.Add(it);
            }
            return res;
        }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) : ""; }

        // adds the result to the vault; exact duplicates (same title, username, password and first URL) are skipped
        public static int Merge(VaultData into, Result r)
        {
            int added = 0;
            foreach (var f in r.Folders) if (!into.Folders.Contains(f)) into.Folders.Add(f);
            foreach (var it in r.Items)
            {
                if (into.Items.Any(x => x.Type == it.Type && x.Title == it.Title && x.Username == it.Username && x.Password == it.Password && (x.Urls.FirstOrDefault() ?? "") == (it.Urls.FirstOrDefault() ?? "") && x.CardNumber == it.CardNumber && x.Notes == it.Notes)) continue;
                if (it.Folder.Length > 0 && !into.Folders.Contains(it.Folder)) into.Folders.Add(it.Folder);
                into.Items.Add(it); added++;
            }
            return added;
        }
    }
}
