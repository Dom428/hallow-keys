// Hallow Keys v1.3 - password history, the trash (30 days), tags and better search, the duplicate finder, exports
// (encrypted with its own password, or plain CSV after a warning) and the quick search shortcut.
// Everything new that's stored (history dates, tags, the trash) lives inside the encrypted vault.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace HallowKeys
{
    public static class Organize
    {
        public const int TrashDays = 30, HistoryMax = 10;

        // ---- password history: newest first, at most 10, each with the time it was replaced
        public static void RecordOldPassword(Item e, string old, DateTime when)
        {
            if (string.IsNullOrEmpty(old)) return;
            Align(e);
            e.OldPasswords.Insert(0, old); e.OldPasswordDates.Insert(0, when.ToString("o"));
            while (e.OldPasswords.Count > HistoryMax) { e.OldPasswords.RemoveAt(e.OldPasswords.Count - 1); e.OldPasswordDates.RemoveAt(e.OldPasswordDates.Count - 1); }
        }
        static void Align(Item e)
        {
            while (e.OldPasswordDates.Count < e.OldPasswords.Count) e.OldPasswordDates.Add("");
            while (e.OldPasswordDates.Count > e.OldPasswords.Count) e.OldPasswordDates.RemoveAt(e.OldPasswordDates.Count - 1);
        }
        public static DateTime? ReplacedOn(Item e, int i)
        {
            DateTime d; return i < e.OldPasswordDates.Count && DateTime.TryParse(e.OldPasswordDates[i], null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d : (DateTime?)null;
        }
        public static void ClearHistory(Item e) { e.OldPasswords.Clear(); e.OldPasswordDates.Clear(); }

        // ---- the trash: deleted items wait 30 days (still encrypted in the vault) before they go for good
        public static void MoveToTrash(VaultData d, Item it, DateTime now)
        {
            if (!d.Items.Remove(it)) return;
            it.Deleted = now; d.Trash.Insert(0, it);
        }
        public static void Restore(VaultData d, Item it)
        {
            if (!d.Trash.Remove(it)) return;
            it.Deleted = null;
            if (d.Items.Any(x => x.Id == it.Id)) it.Id = Guid.NewGuid().ToString("N");
            if (it.Folder.Length > 0 && !d.Folders.Contains(it.Folder)) d.Folders.Add(it.Folder);
            d.Items.Add(it);
        }
        public static int DaysLeft(Item it, DateTime now) { return it.Deleted == null ? TrashDays : Math.Max(0, TrashDays - (int)Math.Floor((now - it.Deleted.Value).TotalDays)); }
        // items in the trash for 30 days or more are removed; returns how many
        public static int Purge(VaultData d, DateTime now)
        {
            var old = d.Trash.Where(i => i.Deleted == null || (now - i.Deleted.Value).TotalDays >= TrashDays).ToList();
            foreach (var i in old) { d.Trash.Remove(i); d.LeakedIds.Remove(i.Id); }
            return old.Count;
        }
        public static void DeleteForever(VaultData d, Item it) { d.Trash.Remove(it); d.LeakedIds.Remove(it.Id); }

        // ---- tags
        public static List<string> ParseTags(string text)
        {
            var o = new List<string>();
            foreach (var t in (text ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var x = t.Trim().TrimStart('#').Trim(); if (x.Length > 40) x = x.Substring(0, 40);
                if (x.Length > 0 && !o.Any(y => string.Equals(y, x, StringComparison.CurrentCultureIgnoreCase))) o.Add(x);
            }
            return o;
        }
        public static List<string> AllTags(VaultData d) { return d.Items.SelectMany(i => i.Tags).GroupBy(t => t, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList(); }
        public static bool HasTag(Item i, string tag) { return i.Tags.Any(t => string.Equals(t, tag, StringComparison.CurrentCultureIgnoreCase)); }

        // ---- search: every word has to match somewhere (titles, usernames, sites, folders, tags, notes, names - never passwords);
        // "#word" matches a tag that starts with it
        public static bool Matches(Item i, string query, string typeName)
        {
            foreach (var w in (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.StartsWith("#") && w.Length > 1) { var t = w.Substring(1); if (!i.Tags.Any(x => x.StartsWith(t, StringComparison.CurrentCultureIgnoreCase))) return false; continue; }
                var hit = new[] { i.Title, i.Username, i.Folder, i.Notes, i.CardHolder, i.FullName, i.Email, i.Phone, i.City, typeName }.Concat(i.Urls).Concat(i.Tags)
                    .Any(x => x != null && x.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (!hit) return false;
            }
            return true;
        }
        // better matches first: the title starts with the search, then the title has it, then the rest
        public static int Rank(Item i, string query)
        {
            var q = (query ?? "").Trim(); if (q.Length == 0 || q.StartsWith("#")) return 0;
            if (i.Title.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 0;
            if (i.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return 1;
            var h = i.Host; if (h != null && h.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 3;
        }

        // ---- duplicates: the same account saved more than once
        //   logins: same site (www. ignored) and same username; notes: same title and text; cards: same number; identities: same name and email
        public static string DuplicateKey(Item i)
        {
            Func<string, string> n = s => (s ?? "").Trim().ToLowerInvariant();
            switch (i.Type)
            {
                case "login":
                    var host = i.Host; if (host != null && host.StartsWith("www.")) host = host.Substring(4);
                    if (host == null && i.Username.Length == 0) return "login|t|" + n(i.Title) + "|" + i.Password;
                    return "login|" + (host ?? "t:" + n(i.Title)) + "|" + n(i.Username);
                case "card":
                    var digits = new string(i.CardNumber.Where(char.IsDigit).ToArray());
                    return digits.Length > 0 ? "card|" + digits : "card|t|" + n(i.Title) + "|" + n(i.CardHolder);
                case "identity": return "identity|" + n(i.FullName) + "|" + n(i.Email) + "|" + n(i.Title);
                default: return "note|" + n(i.Title) + "|" + i.Notes.Trim();
            }
        }
        public static List<List<Item>> Duplicates(VaultData d)
        {
            return d.Items.GroupBy(DuplicateKey).Where(g => g.Count() > 1)
                .Select(g => g.OrderByDescending(i => i.Updated).ToList()).OrderBy(g => g[0].Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---- plain CSV export in NordPass's column layout (+ totp, tags), which Hallow Keys, NordPass and most managers import.
        // Text cells that a spreadsheet would run as a formula get a leading ' (Hallow Keys' import takes it off again);
        // secrets (passwords, card numbers, codes) are written exactly as they are.
        public static readonly string[] CsvColumns = { "name", "url", "additional_urls", "username", "password", "note", "cardholdername", "cardnumber", "cvc", "pin", "expirydate", "zipcode", "folder", "full_name", "phone_number", "email", "address1", "address2", "city", "country", "state", "type", "totp", "tags" };
        public static string ToCsv(VaultData d)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", CsvColumns)).Append("\r\n");
            foreach (var f in d.Folders) { Row(sb, new Dictionary<string, string> { { "name", f }, { "type", "folder" } }); }
            foreach (var i in d.Items)
            {
                var type = i.Type == "login" ? "password" : i.Type == "card" ? "credit_card" : i.Type;
                Row(sb, new Dictionary<string, string> {
                    { "name", i.Title }, { "url", i.Urls.FirstOrDefault() ?? "" }, { "additional_urls", string.Join(",", i.Urls.Skip(1)) }, { "username", i.Username }, { "password", i.Password }, { "note", i.Notes },
                    { "cardholdername", i.CardHolder }, { "cardnumber", i.CardNumber }, { "cvc", i.Cvc }, { "pin", i.Pin }, { "expirydate", i.Expiry }, { "zipcode", i.PostCode }, { "folder", i.Folder },
                    { "full_name", i.FullName }, { "phone_number", i.Phone }, { "email", i.Email }, { "address1", i.Address }, { "address2", "" }, { "city", i.City }, { "country", i.Country }, { "state", "" },
                    { "type", type }, { "totp", i.Totp }, { "tags", string.Join(",", i.Tags) } });
            }
            return sb.ToString();
        }
        static readonly HashSet<string> RawColumns = new HashSet<string> { "password", "cardnumber", "cvc", "pin", "totp", "expirydate" };
        static void Row(StringBuilder sb, Dictionary<string, string> v)
        {
            for (int c = 0; c < CsvColumns.Length; c++)
            {
                if (c > 0) sb.Append(',');
                string s; if (!v.TryGetValue(CsvColumns[c], out s) || s == null) s = "";
                if (!RawColumns.Contains(CsvColumns[c]) && s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
                if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || (s.Length > 0 && (s[0] == ' ' || s[s.Length - 1] == ' '))) s = "\"" + s.Replace("\"", "\"\"") + "\"";
                sb.Append(s);
            }
            sb.Append("\r\n");
        }
    }

    // ---- a global shortcut (RegisterHotKey) on a message-only window. If another app already has the combination,
    // registering fails and the shortcut simply stays off - nothing is taken from the other app.
    public class GlobalHotkey : WF.NativeWindow
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int WM_HOTKEY = 0x0312; readonly int HotId = 0x4B59;
        const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public static string Caption { get { return "HallowKeysQuickSearch" + (KeysSettings.TestDir != null ? "Test" : ""); } }
        readonly Action fired; public string Active;   // the combination registered now, or null
        public GlobalHotkey(Action fired) { this.fired = fired; CreateHandle(new WF.CreateParams { Caption = Caption, Parent = new IntPtr(-3) }); }   // HWND_MESSAGE
        // v1.5: a second shortcut (desktop app autofill) with its own window name and id
        public GlobalHotkey(Action fired, string caption, int id) { this.fired = fired; HotId = id; CreateHandle(new WF.CreateParams { Caption = caption + (KeysSettings.TestDir != null ? "Test" : ""), Parent = new IntPtr(-3) }); }
        public bool Set(string combo)
        {
            Clear(); uint m, vk; if (!Parse(combo, out m, out vk)) return false;
            if (!RegisterHotKey(Handle, HotId, m | MOD_NOREPEAT, vk)) return false;
            Active = combo; return true;
        }
        public void Clear() { if (Active != null) UnregisterHotKey(Handle, HotId); Active = null; }
        public void Close() { Clear(); try { DestroyHandle(); } catch { } }
        protected override void WndProc(ref WF.Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotId) { if (fired != null) fired(); return; }
            base.WndProc(ref m);
        }
        // "Ctrl+Shift+Space", "Alt+Win+K"... it needs Ctrl, Alt or Win (Shift alone would take a normal key away from typing)
        public static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0; if (string.IsNullOrWhiteSpace(combo)) return false;
            var parts = combo.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList(); if (parts.Count < 2) return false;
            foreach (var p in parts.Take(parts.Count - 1))
            {
                var l = p.ToLowerInvariant();
                if (l == "ctrl" || l == "control") mods |= MOD_CONTROL; else if (l == "alt") mods |= MOD_ALT; else if (l == "shift") mods |= MOD_SHIFT; else if (l == "win" || l == "windows") mods |= MOD_WIN; else return false;
            }
            if ((mods & (MOD_CONTROL | MOD_ALT | MOD_WIN)) == 0) return false;
            WF.Keys k; var last = parts[parts.Count - 1];
            if (last.Length == 1 && char.IsDigit(last[0])) last = "D" + last;
            if (last.All(char.IsDigit) || last.Contains(",") || !Enum.TryParse(last, true, out k)) return false;
            if (k == WF.Keys.ControlKey || k == WF.Keys.ShiftKey || k == WF.Keys.Menu || k == WF.Keys.LWin || k == WF.Keys.RWin || k == WF.Keys.None || ((int)k & ~0xFF) != 0) return false;
            vk = (uint)k; return true;
        }
        // from a key press in the window
        public static string Format(ModifierKeys mods, bool win, Key key)
        {
            var vk = KeyInterop.VirtualKeyFromKey(key); if (vk == 0) return null;
            var k = (WF.Keys)vk; if (k == WF.Keys.ControlKey || k == WF.Keys.ShiftKey || k == WF.Keys.Menu || k == WF.Keys.LControlKey || k == WF.Keys.RControlKey || k == WF.Keys.LShiftKey || k == WF.Keys.RShiftKey || k == WF.Keys.LMenu || k == WF.Keys.RMenu || k == WF.Keys.LWin || k == WF.Keys.RWin) return null;
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl"); if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt"); if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift"); if (win || (mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            var name = k.ToString(); if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name.Substring(1);
            parts.Add(name); return string.Join("+", parts);
        }
    }
}
