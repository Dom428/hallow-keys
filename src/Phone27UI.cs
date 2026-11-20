// Hallow Keys 2.7 - "Phone, properly", the window's side: "Let the phone edit" (Settings > App, under the phone copy), changes from the
// phone arriving through Hallow Remote (PhoneEdit27.cs does the checking), their place in the Activity list and "Undo phone changes",
// and sharing one item as a Hallow Remote link (served once over Remote's HTTPS, opened in the phone's browser with Argon2id + WebCrypto).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowKeys
{
    public partial class MainWindow
    {
        // "phone-change <base64 of the sealed record>" on the family pipe (from Hallow Remote) - runs on the window's thread
        string PhoneChangeCmd(string arg)
        {
            if (vault == null) return "error locked: Hallow Keys is locked on your PC - unlock it there, then try again";
            if (!S.PhoneCopy || vault.Data.PhoneKey == null) return "error the phone copy is off in Hallow Keys";
            if (!S.PhoneEdit) return "error editing from the phone is off - turn on \"Let the phone edit\" in Hallow Keys (Settings > App)";
            string json; try { json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(arg)); } catch { return "error the change is damaged"; }
            var d = vault.Data;
            if (d.PhoneLog == null) d.PhoneLog = new List<PhoneLogEntry>();
            // a snapshot before the first phone change of a session (none in the last 10 minutes), for "Undo phone changes"
            string snap = null; var last = d.PhoneLog.Count > 0 ? d.PhoneLog[0].When : DateTime.MinValue;
            var r = PhoneEdit.Apply(d, json, DateTime.UtcNow, IsShown, Travel);
            if (!r.Ok) { AddActivity("phone", PhoneEdit.Describe(r)); return "error " + r.Msg; }
            if ((DateTime.Now - last).TotalMinutes >= 10)
            {
                Versions.MaybeKeep(vault.Path, true);
                var newest = Versions.List().FirstOrDefault(); if (newest != null && (DateTime.Now - newest.When).TotalSeconds < 30) snap = Path.GetFileName(newest.Path);
            }
            d.PhoneLog.Insert(0, new PhoneLogEntry { When = DateTime.Now, Text = PhoneEdit.Describe(r), Snapshot = snap });
            while (d.PhoneLog.Count > 100) d.PhoneLog.RemoveAt(100);
            TestLog("activity: phone: " + PhoneEdit.Describe(r));
            SaveVault(null);
            if (selected == null && editing == null) ShowMain();
            Notify("Hallow Keys", "From your phone: " + PhoneEdit.Describe(r) + ".");
            return "ok " + Family.Json(new Dictionary<string, object> { { "status", r.Status }, { "id", r.Id }, { "msg", r.Msg } });
        }

        IEnumerable<ActivityEntry> PhoneActivity()
        {
            if (vault == null || vault.Data.PhoneLog == null) return new ActivityEntry[0];
            return vault.Data.PhoneLog.Select(p => new ActivityEntry { When = p.When, Kind = "phone", Text = p.Text }).ToList();
        }
        // "Undo phone changes since ..." - the snapshot kept before the latest phone session, through 2.0's Restore an older version
        void PhoneUndoRow(StackPanel box)
        {
            if (vault == null || vault.Data.PhoneLog == null) return;
            var e = vault.Data.PhoneLog.FirstOrDefault(p => !string.IsNullOrEmpty(p.Snapshot)); if (e == null) return;
            var v = Snap.On ? new Versions.Entry { When = e.When, Path = "" } : Versions.List().FirstOrDefault(x => Path.GetFileName(x.Path) == e.Snapshot);
            if (v == null) return;
            var b = new Button { Content = "Undo phone changes since " + e.When.ToString(e.When.Date == DateTime.Today ? "HH:mm" : "d MMM HH:mm") + "...", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Puts the vault back as it was just before (the snapshot kept then) - anything changed on this PC since goes back too; the vault as it is now is kept as a snapshot first" };
            b.Click += (s, a) => RestoreVersion(v);
            box.Children.Add(b);
        }

        // Settings > App, under "Phone copy for Hallow Remote"
        void PhoneEditSwitch(StackPanel ap)
        {
            var on = S.PhoneCopy && vault != null && vault.Data.PhoneKey != null;
            var sw = Switch("Let the phone edit: add, change and move items to the trash from Hallow Remote's Keys tab (each change is checked here and listed in Activity; passkeys, SSH keys and wallets stay view-only)", on && S.PhoneEdit, v =>
            {
                S.PhoneEdit = v; S.Save();
                if (vault != null && S.PhoneCopy && vault.Data.PhoneKey != null) try { PhoneCopy.Write(Shown(), S.PhoneEdit); } catch { }
                Toast(v ? "The phone may now edit - changes come in sealed with your phone key and are checked here." : "The phone is read-only again.");
            });
            sw.IsEnabled = on; sw.Margin = new Thickness(24, 0, 0, 4);
            ap.Children.Add(sw);
        }

        // Share > As a Hallow Remote link: the same encrypted .hkshare, served once at a random https path by Hallow Remote
        void ShareAsRemoteLink(Item it)
        {
            var st = Family.Status("Remote");
            if (st == null) { Toast("Hallow Remote isn't running on this PC (it serves the link).", true); return; }
            object https; if (!st.TryGetValue("https", out https) || !(https is bool) || !(bool)https) { Toast("Hallow Remote needs HTTPS for a share link (Remote > Turn on HTTPS).", true); return; }
            var pass = Generator.Passphrase(6, "-", false, false);
            var hours = Ask.Text(this, "Share \"" + it.Title + "\" as a link", "Hallow Remote serves the encrypted item ONCE at a random https link; it's opened in a browser with this passphrase (Argon2id + AES-256-GCM, worked out on that device):\n\n    " + pass + "\n\nSend the link and the passphrase by DIFFERENT routes. The link only works where your PC can be reached (your Tailscale network, or your home network in Remote's home mode), and only until it's opened once or runs out.\n\nHow many hours should the link work?", "Hours (1-168)");
            if (hours == null) return; int n; if (!int.TryParse(hours.Trim(), out n) || n < 1 || n > 168) { Toast("Pick 1 to 168 hours.", true); return; }
            byte[] file;
            try { file = ItemShare.Make(it, pass, DateTime.UtcNow.AddHours(n), vault.Data.MemberName ?? Environment.UserName); } catch (Exception ex) { Toast(ex.Message, true); return; }
            if (file.Length > 512 * 1024) { Toast("This item is too big for a link (files attached?) - use Share > As an encrypted file.", true); return; }
            var exp = (long)(DateTime.UtcNow.AddHours(n) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            var reply = Family.Send("Remote", "keys-share " + exp + " " + Convert.ToBase64String(file), 5000);
            var err = Family.Error(reply, "Hallow Remote didn't answer.");
            if (err != null) { Toast(err.Contains("unknown command") ? "This needs Hallow Remote 1.12 or newer - update it first." : "Hallow Remote: " + err, true); return; }
            string url = null; try { var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply.Substring(3)); url = d["url"] as string; } catch { }
            if (url == null) { Toast("Hallow Remote didn't give a link back.", true); return; }
            try { Clipboard.SetText(url); } catch { }
            MessageBox.Show(this, "The link is on the clipboard:\n\n" + url + "\n\nIt opens ONCE, until " + DateTime.Now.AddHours(n).ToString("d MMM HH:mm") + ". Passphrase (send it another way):\n\n    " + pass, "Hallow Keys - share link", MessageBoxButton.OK);
            AddActivity("share", "made a one-time link for \"" + it.Title + "\" (" + n + " h)");
        }

        void SnapExtra27(MainWindow w)
        {
            w.S.PhoneCopy = true; w.S.PhoneEdit = true; w.vault.Data.PhoneKey = new byte[32]; w.vault.Data.PhoneSalt = new byte[16];
            w.vault.Data.PhoneLog = new List<PhoneLogEntry> {
                new PhoneLogEntry { When = DateTime.Now.AddMinutes(-3), Text = "changed \"Steam\"" },
                new PhoneLogEntry { When = DateTime.Now.AddMinutes(-5), Text = "added \"Gym locker\"", Snapshot = "vault-made-up.hkvault" } };
            w.view = "settings"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v27-settings.png"), 1280, 4000);
        }
    }
}
