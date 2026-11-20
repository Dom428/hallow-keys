// Hallow Keys 2.4 - "Sharing (no server)", the window's parts: the Sharing page (your member card, shared vaults with their members
// and items, opening a shared item file) and the Share button on an item. The crypto and file formats are in Sharing24.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowKeys
{
    public partial class MainWindow
    {
        SharedFile sharedOpen; SharedRef sharedRef; List<string> sharedMerged; string sharedNote;
        List<SharedRef> SharedRefs() { if (vault.Data.Shared == null) vault.Data.Shared = new List<SharedRef>(); return vault.Data.Shared; }
        MemberCard MyCard() { Members.Ensure(vault.Data, Environment.UserName); return Members.Mine(vault.Data); }

        // the Share button on an item (not on passkeys)
        void ShareButton(StackPanel row, Item it)
        {
            if (!ItemShare.CanShare(it)) return;
            var b = new Button { Content = "Share", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Send a copy of this item: an encrypted file with its own passphrase, or into a shared vault" };
            b.Click += (s, e) =>
            {
                var m = new ContextMenu();
                var f = new MenuItem { Header = "As an encrypted file (passphrase + expiry)...", Icon = Icons.Make("file-export", 12, Muted) }; f.Click += (a, c) => ShareAsFile(it); m.Items.Add(f);
                var l = new MenuItem { Header = "As a one-time Hallow Remote link (passphrase + expiry)...", Icon = Icons.Make("link", 12, Muted) }; l.Click += (a, c) => ShareAsRemoteLink(it); m.Items.Add(l);   // v2.7
                foreach (var r0 in SharedRefs()) { var r = r0; var mi = new MenuItem { Header = "Copy into the shared vault \"" + r.Name + "\"", Icon = Icons.Make("users", 12, Muted) }; mi.Click += (a, c) => CopyIntoShared(r, it); m.Items.Add(mi); }
                if (SharedRefs().Count == 0) m.Items.Add(new MenuItem { Header = "No shared vaults yet (Sharing page)", IsEnabled = false });
                m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
            };
            row.Children.Insert(0, b);
        }
        void ShareAsFile(Item it)
        {
            var pass = Generator.Passphrase(6, "-", false, false);
            var days = Ask.Text(this, "Share \"" + it.Title + "\" as a file", "The file is encrypted with this passphrase (Argon2id + AES-256-GCM):\n\n    " + pass + "\n\nSend the file and the passphrase by DIFFERENT routes (for example the file by email, the passphrase read out on the phone). The passphrase is copied when you click OK.\n\nHow many days may it be opened? Hallow Keys refuses it after that - but it can't make a copy forget itself: anyone who keeps the file and the passphrase could still read it with other software.", "Days (1-90)");
            if (days == null) return; int n; if (!int.TryParse(days.Trim(), out n) || n < 1 || n > 90) { Toast("Pick 1 to 90 days.", true); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save the shared item", FileName = "Shared item " + DateTime.Now.ToString("yyyy-MM-dd") + ".hkshare", Filter = "Hallow Keys shared item (*.hkshare)|*.hkshare" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllBytes(dlg.FileName, ItemShare.Make(it, pass, DateTime.UtcNow.AddDays(n), vault.Data.MemberName ?? Environment.UserName)); SafeClip.Copy(pass); Toast("Saved - the passphrase is on the clipboard for " + SafeClip.ClearSeconds + " seconds. It can be opened for " + n + " day" + (n == 1 ? "" : "s") + "."); }
            catch (Exception ex) { Toast("Couldn't save it: " + ex.Message, true); }
        }
        void OpenItemShare()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Open a shared item", Filter = "Hallow Keys shared item (*.hkshare)|*.hkshare" };
            if (dlg.ShowDialog(this) != true) return;
            byte[] b; ItemShare.Header h;
            try { b = File.ReadAllBytes(dlg.FileName); h = ItemShare.Peek(b); } catch (Exception ex) { Toast(ex.Message, true); return; }
            var pass = Ask.Text(this, "Open a shared item", "From " + (h.From.Length > 0 ? h.From : "someone") + ", made " + h.Made.ToLocalTime().ToString("d MMM yyyy") + ", can be opened until " + h.Expires.ToLocalTime().ToString("d MMM yyyy HH:mm") + ".\n\nType the passphrase they gave you:", "Passphrase");
            if (pass == null) return;
            IsEnabled = false;
            new System.Threading.Thread(() =>
            {
                Item it = null; string err = null; ItemShare.Header hh;
                try { it = ItemShare.Open(b, pass, DateTime.UtcNow, out hh); } catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true; if (err != null) { Toast(err, true); return; }
                    Vaults.Put(it, Vaults.DefaultFor(vault.Data, curVault, Travel)); vault.Data.Items.Add(it); selected = it; view = "all";
                    SaveVault("Added the shared item \"" + it.Title + "\" to your vault."); ShowMain();
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- shared vaults
        bool OpenShared(SharedRef r, bool quiet)
        {
            try
            {
                List<string> merged; string note; var f = SharedVaults.Open(r.Folder, vault.Data, r, out merged, out note);
                sharedOpen = f; sharedRef = r; sharedMerged = merged; sharedNote = note;
                if (f.Epoch != r.Epoch || f.Name != r.Name || r.Key == null || SharedFile.KeyHash(r.Key) != SharedFile.KeyHash(f.Key)) { r.Epoch = f.Epoch; r.Name = f.Name; r.Key = (byte[])f.Key.Clone(); SaveVault(null); }
                return true;
            }
            catch (Exception ex) { if (!quiet) Toast(ex.Message, true); sharedOpen = null; return false; }
        }
        bool SaveShared(string msg)
        {
            try { SharedVaults.Save(sharedRef.Folder, sharedOpen, vault.Data, sharedMerged); sharedMerged = null; if (sharedOpen.Epoch != sharedRef.Epoch || sharedRef.Key == null) { sharedRef.Epoch = sharedOpen.Epoch; sharedRef.Key = (byte[])sharedOpen.Key.Clone(); SaveVault(null); } if (msg != null) Toast(msg); return true; }
            catch (Exception ex) { Toast("Couldn't write the shared vault: " + ex.Message, true); return false; }
        }
        void CopyIntoShared(SharedRef r, Item it)
        {
            if (!OpenShared(r, false)) return;
            sharedOpen.Data.Items.Add(SharedVaults.CopyIn(it));
            SaveShared("A copy of \"" + it.Title + "\" is in the shared vault \"" + r.Name + "\" - every member sees it once their cloud service has synced.");
        }
        string PickFolder(string why)
        {
            using (var d = new WF.FolderBrowserDialog { Description = why, ShowNewFolderButton = true }) { var od = Environment.GetEnvironmentVariable("OneDrive"); if (!string.IsNullOrEmpty(od) && Directory.Exists(od)) d.SelectedPath = od; return d.ShowDialog() == WF.DialogResult.OK ? d.SelectedPath : null; }
        }
        void NewShared()
        {
            var name = Ask.Text(this, "New shared vault", "A name everyone in it will see (Family, Flat, Team...).", "Name"); if (string.IsNullOrWhiteSpace(name)) return;
            var folder = PickFolder("Pick a folder that you and the other members share (a shared OneDrive / Dropbox / Google Drive folder, or a NAS). The shared vault is one file there: shared.hkshared."); if (folder == null) return;
            if (File.Exists(SharedVaults.MainPath(folder))) { Toast("That folder already has a shared vault - use Join instead.", true); return; }
            var f = SharedFile.Create(name, MyCard()); var r = new SharedRef { Id = f.Id, Name = f.Name, Folder = folder, Epoch = f.Epoch, Key = (byte[])f.Key.Clone() };
            sharedOpen = f; sharedRef = r; sharedMerged = null;
            if (!SaveShared(null)) return;
            SharedRefs().Add(r); SaveVault("Made the shared vault \"" + f.Name + "\". Add members with their member cards."); ShowMain();
        }
        void JoinShared()
        {
            var folder = PickFolder("Pick the shared folder that holds shared.hkshared (a member has to add your member card first)."); if (folder == null) return;
            MyCard();
            try
            {
                List<string> merged; string note; var f = SharedVaults.Open(folder, vault.Data, null, out merged, out note);
                if (SharedRefs().Any(x => x.Id == f.Id)) { Toast("You've already joined \"" + f.Name + "\"."); return; }
                var r = new SharedRef { Id = f.Id, Name = f.Name, Folder = folder, Epoch = f.Epoch, Key = (byte[])f.Key.Clone() }; SharedRefs().Add(r); sharedOpen = f; sharedRef = r; sharedMerged = merged;
                SaveVault("Joined the shared vault \"" + f.Name + "\"."); ShowMain();
            }
            catch (Exception ex) { Toast(ex.Message, true); }
        }
        void AddMember()
        {
            var code = Ask.Text(this, "Add a member to \"" + sharedOpen.Name + "\"", "Paste their member card (it starts with HKM1-; they find it on their Sharing page).", "Member card"); if (code == null) return;
            string why; var c = Members.Parse(code, out why); if (c == null) { Toast(why, true); return; }
            if (sharedOpen.Has(c.Id)) { Toast(c.Name + " is already a member."); return; }
            if (!Snap.On && MessageBox.Show(this, "Add " + c.Name + "?\n\nAsk them to read out the fingerprint on their Sharing page. It must be exactly:\n\n    " + c.Fingerprint + "\n\nIf it's different, someone may have swapped the card - don't add it.", "Check the fingerprint", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            sharedOpen.AddMember(c); SaveShared(c.Name + " can open \"" + sharedOpen.Name + "\" now (once the folder is shared with them and synced)."); ShowMain();
        }
        void RemoveMember(SharedMember m)
        {
            if (MessageBox.Show(this, "Remove " + m.Name + " from \"" + sharedOpen.Name + "\"?\n\nThe shared vault gets a new key that " + m.Name + " doesn't have, so they can't read anything added or changed after this. But anything they could already see, they may have kept a copy of - change those passwords if that matters.", "Remove member", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            sharedOpen.RemoveMember(m.Id, DateTime.Now); SaveShared(m.Name + " was removed and the shared vault has a new key."); ShowMain();
        }

        UIElement SharingPage()
        {
            ScrollViewer sv; var sp = Page("Sharing", "Share without a server: a single item as an encrypted file, or a shared vault in a folder you and your family or team already share. Everything is encrypted to each member's own key - the cloud service sees only scrambled files.", out sv);
            var me = Snap.On && vault.Data.MemberPub == null ? null : MyCard();
            // your member card
            var card = new StackPanel { Margin = new Thickness(0, 4, 0, 14) };
            card.Children.Add(new TextBlock { Text = "Your member card", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            card.Children.Add(new TextBlock { Text = "Give this to whoever adds you to a shared vault or makes you their emergency contact. It holds only your name and a public key - it can't open anything.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
            if (me != null)
            {
                var code = Members.Code(me);
                var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rn = new Button { Content = "Rename", Style = St("Btn") }; rn.Click += (s, e) => { var n = Ask.Text(this, "Your name on your member card", "What the others see.", "Name"); if (string.IsNullOrWhiteSpace(n)) return; vault.Data.MemberName = Members.CleanName(n); SaveVault("Your member card now says " + vault.Data.MemberName + "."); ShowMain(); };
                DockPanel.SetDock(rn, Dock.Right); nameRow.Children.Add(rn); nameRow.Children.Add(new TextBlock { Text = me.Name, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); card.Children.Add(nameRow);
                card.Children.Add(new TextBox { Style = St("Area"), Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11 });
                var fr = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                var cp = new Button { Content = "Copy card", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; cp.Click += (s, e) => { Clipboard.SetText(code); Toast("Member card copied (it isn't secret)."); };
                var sv2 = new Button { Content = "Save card file", Style = St("Btn"), Margin = new Thickness(0, 0, 12, 0) }; sv2.Click += (s, e) => { var d = new Microsoft.Win32.SaveFileDialog { FileName = me.Name + ".hkmember", Filter = "Hallow Keys member card (*.hkmember)|*.hkmember" }; if (d.ShowDialog(this) == true) { File.WriteAllText(d.FileName, code); Toast("Saved."); } };
                fr.Children.Add(cp); fr.Children.Add(sv2);
                fr.Children.Add(new TextBlock { Text = "Fingerprint  " + me.Fingerprint, FontFamily = new FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                card.Children.Add(fr);
            }
            sp.Children.Add(card);
            // shared vaults
            var head = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            var nb = new Button { Content = "New shared vault", Style = St("Accent"), Margin = new Thickness(0, 0, 6, 0) }; nb.Click += (s, e) => NewShared();
            var jb = new Button { Content = "Join", Style = St("Btn") }; jb.Click += (s, e) => JoinShared();
            bs.Children.Add(nb); bs.Children.Add(jb); DockPanel.SetDock(bs, Dock.Right); head.Children.Add(bs);
            head.Children.Add(new TextBlock { Text = "Shared vaults", FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            if (SharedRefs().Count == 0) sp.Children.Add(new TextBlock { Text = "None yet. New shared vault puts one in a folder you share with others (a shared OneDrive or Dropbox folder, a NAS); each member runs their own Hallow Keys and is added with their member card.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            foreach (var r0 in SharedRefs())
            {
                var r = r0; var open = sharedOpen != null && sharedRef != null && sharedRef.Id == r.Id;
                var box = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8) };
                var bp = new StackPanel(); box.Child = bp;
                var top = new DockPanel();
                var ob = new Button { Content = open ? "Refresh" : "Open", Style = St("Btn") }; ob.Click += (s, e) => { if (OpenShared(r, false)) ShowMain(); };
                DockPanel.SetDock(ob, Dock.Right); top.Children.Add(ob);
                var ic = Icons.Make("users", 15, Muted); ic.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ic, Dock.Left); top.Children.Add(ic);
                var tt = new StackPanel(); tt.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 }); tt.Children.Add(new TextBlock { Text = r.Folder, Foreground = Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }); top.Children.Add(tt);
                bp.Children.Add(top);
                if (open) SharedDetail(bp);
                sp.Children.Add(box);
            }
            sp.Children.Add(new TextBlock { Text = "A shared vault is one file in the folder; each member's Hallow Keys opens it with their own key and merges everyone's changes item by item (like sync between your PCs). Copies you put in are copies: changing your own item later doesn't change the shared one.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 14) });
            // a single item
            sp.Children.Add(new TextBlock { Text = "One item as a file", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            sp.Children.Add(new TextBlock { Text = "Share sends one item as a .hkshare file with its own passphrase and an expiry (open an item, click Share). Send the file and the passphrase by different routes. Passkeys can't be shared. Or as a one-time link served by Hallow Remote 1.12 (Share > As a one-time Hallow Remote link): it opens once, in a browser on your phone or another device that can reach your PC.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
            var oi = new Button { Content = "Open a shared item file", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left }; oi.Click += (s, e) => OpenItemShare(); sp.Children.Add(oi);
            EmergencySection(sp);   // v2.5
            return sv;
        }
        partial void EmergencySectionImpl(StackPanel sp);
        void EmergencySection(StackPanel sp) { EmergencySectionImpl(sp); }
        void SharedDetail(StackPanel bp)
        {
            var f = sharedOpen; var myId = vault.Data.MemberPub == null ? "" : Ecies.MemberId(vault.Data.MemberPub);
            if (sharedNote != null) bp.Children.Add(new TextBlock { Text = "Some copies couldn't be read: " + sharedNote, Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) });
            bp.Children.Add(new TextBlock { Text = "Members (" + f.Members.Count + ")  -  key " + f.Epoch, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var m0 in f.Members)
            {
                var m = m0; var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                if (m.Id != myId) { var rm = new Button { Content = "Remove", Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2) }; rm.Click += (s, e) => RemoveMember(m); DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm); }
                row.Children.Add(new TextBlock { Text = m.Name + (m.Id == myId ? " (you)" : "") + "   " + Ecies.Fingerprint(m.Pub), FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center });
                bp.Children.Add(row);
            }
            var am = new Button { Content = "Add a member", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) }; am.Click += (s, e) => AddMember(); bp.Children.Add(am);
            if (f.Removed.Count > 0) bp.Children.Add(new TextBlock { Text = "Removed: " + string.Join(", ", f.Removed.Select(x => x.Replace("|", " on "))) + ". They can't read anything written since, but may have kept copies of what they saw before.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            bp.Children.Add(new TextBlock { Text = "Items (" + f.Data.Items.Count + ")", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) });
            if (f.Data.Items.Count == 0) bp.Children.Add(new TextBlock { Text = "Empty - open one of your items and click Share > Copy into this shared vault.", Foreground = Muted, FontSize = 11.5 });
            foreach (var it0 in f.Data.Items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                var it = it0; var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var b2 = new StackPanel { Orientation = Orientation.Horizontal };
                var cp = new Button { Content = "Copy to my vault", Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
                cp.Click += (s, e) => { var c = ItemTypes.Clone(it, true); c.Sync = null; c.VaultId = null; Vaults.Put(c, Vaults.DefaultFor(vault.Data, curVault, Travel)); vault.Data.Items.Add(c); SaveVault("\"" + it.Title + "\" was copied into your vault."); ShowMain(); };
                var rm = new Button { Content = "Remove", Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2) };
                rm.Click += (s, e) => { if (MessageBox.Show(this, "Remove \"" + it.Title + "\" from the shared vault for everyone?", "Shared vault", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return; SharedVaults.Delete(f.Data, it, Device.Id); SaveShared("Removed from the shared vault."); ShowMain(); };
                b2.Children.Add(cp); b2.Children.Add(rm); DockPanel.SetDock(b2, Dock.Right); row.Children.Add(b2);
                var ic = Icons.Make(ItemTypes.Icon(it.Type), 13, Muted); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ic, Dock.Left); row.Children.Add(ic);
                var sub = Subtitle(it); row.Children.Add(new TextBlock { Text = it.Title + (sub.Length > 0 ? "  -  " + sub : ""), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                bp.Children.Add(row);
            }
        }

        void SnapExtra24()
        {
            Action<MainWindow, string> tall = (mw, n) => Snap.Save(mw, Path.Combine(snapDir, n), 1280, 1500);
            var w = new MainWindow(false, true); w.vault = new Vault { Data = FakeData21() }; w.S.SyncOn = false;
            var d = w.vault.Data; Members.Ensure(d, "Alex Example");
            var f = SharedFile.Create("Family", Members.Mine(d)); byte[] k2, p2; Ecies.NewKey(out k2, out p2); f.AddMember(new MemberCard { Name = "Sam Example", Pub = p2 });
            f.Data.Items.Add(SharedVaults.CopyIn(d.Items.First(i => i.Type == "login"))); f.Data.Items.Add(new Item { Type = "note", Title = "Home Wi-Fi and alarm code", Notes = "made up" });
            f.Removed.Add("Old flatmate|2026-10-01");
            var r = new SharedRef { Id = f.Id, Name = "Family", Folder = @"C:\Users\you\OneDrive\Shared\Family keys", Epoch = 2 }; d.Shared = new List<SharedRef> { r, new SharedRef { Id = "x", Name = "Work team", Folder = @"\\nas\team\keys" } };
            w.sharedOpen = f; w.sharedRef = r;
            w.view = "sharing"; w.ShowMain(); tall(w, "v24-sharing.png");
            w.view = "all"; w.selected = d.Items.First(i => i.Type == "login"); w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v24-share-button.png"), 1280, 760);
            SnapExtra25(w);
        }
        partial void SnapExtra25Impl(MainWindow w);
        void SnapExtra25(MainWindow w) { SnapExtra25Impl(w); }
    }
}
