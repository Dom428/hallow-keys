// Hallow Keys v1.3 - the window parts: Trash and Duplicates pages, password history in an item, exports, the quick search shortcut.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        partial void ExtraNav2(StackPanel nav, Action<string, string, string, int> navBtn)
        {
            navBtn("duplicates", "clone", "Duplicates", Organize.Duplicates(Shown()).Sum(g => g.Count - 1));   // v2.1: Trash moved up, next to Vault and Favourites
            ExtraNav212(navBtn);   // v2.12: Passkeys on Windows
        }
        partial void ExtraPage2(string v, DockPanel dock, ref bool handled)
        {
            if (v == "trash") { dock.Children.Add(TrashPage()); handled = true; }
            else if (v == "duplicates") { dock.Children.Add(DuplicatesPage()); handled = true; }
            else if (v == "passkeys-windows") { dock.Children.Add(PasskeysWindowsPage()); handled = true; }   // v2.12
        }
        partial void SnapExtra2(Action<MainWindow, string> save)
        {
            editing = null;
            foreach (var v in new[] { "trash", "duplicates" }) { view = v; ShowMain(); save(this, v + ".png"); }
            view = "all"; selected = vault.Data.Items[0]; showHistory = true; ShowMain(); save(this, "history.png"); showHistory = false;
            view = "tag:games"; lastSearch = ""; ShowMain(); save(this, "tag.png");
            view = "all"; lastSearch = "mail"; selected = null; ShowMain(); save(this, "search.png"); lastSearch = "";
            SnapExtra16(save);   // v1.6
        }

        // ---- the trash
        void TrashItem(Item it)
        {
            Organize.MoveToTrash(vault.Data, it, DateTime.Now);
            if (selected == it) selected = null;
            SaveVault("\"" + it.Title + "\" is in the trash - restore it from Trash within " + Organize.TrashDays + " days.");
        }
        UIElement TrashPage()
        {
            ScrollViewer sv; var sp = Page("Trash", "Deleted items wait here for " + Organize.TrashDays + " days, still encrypted in your vault, then they're removed for good. They don't fill in the browser, count in Password Health or go to the phone copy.", out sv);
            var trash = Scope().Trash; var now = DateTime.Now;   // v2.1: only what this PC shows (Travel Mode) and the vault picked in the sidebar
            if (trash.Count == 0) { sp.Children.Add(new TextBlock { Text = "The trash is empty.", Foreground = Muted }); return sv; }
            var empty = new Button { Content = "Empty trash", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            empty.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Delete the " + trash.Count + " item" + (trash.Count == 1 ? "" : "s") + " in the trash for good?\n\nThis can't be undone (only the previous save, vault.hkvault.bak, still has them until the next change).", "Empty trash", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                foreach (var i in trash.ToList()) Organize.DeleteForever(vault.Data, i);
                SaveVault("The trash is empty."); ShowMain();
            };
            sp.Children.Add(empty);
            foreach (var it in trash.ToList())
            {
                var item = it; var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var bs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var rs = new Button { Content = "Restore", Style = St("Accent"), Margin = new Thickness(0, 0, 6, 0) };
                rs.Click += (s, e) => { Organize.Restore(vault.Data, item); selected = item; SaveVault("Restored \"" + item.Title + "\"."); ShowMain(); };
                var df = new Button { Content = "Delete forever", Style = St("Btn") };
                df.Click += (s, e) =>
                {
                    if (MessageBox.Show(this, "Delete \"" + item.Title + "\" for good? This can't be undone.", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    Organize.DeleteForever(vault.Data, item); SaveVault("Deleted for good."); ShowMain();
                };
                bs.Children.Add(rs); bs.Children.Add(df); DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                var ic = Icons.Make(ItemTypes.Icon(it.Type), 16, Muted); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(ic);
                var tx = new StackPanel();
                tx.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var left = Organize.DaysLeft(it, now);
                tx.Children.Add(new TextBlock { Text = ItemTypes.Name(it.Type) + (Subtitle(it).Length > 0 ? "  -  " + Subtitle(it) : "") + "  -  deleted " + (it.Deleted.HasValue ? it.Deleted.Value.ToString("d MMM yyyy") : "") + ", " + (left <= 1 ? "goes tomorrow" : left + " days left"), Foreground = Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(tx);
                sp.Children.Add(Card(row));
            }
            return sv;
        }
        // on unlock: anything in the trash for 30 days goes
        void PurgeTrash()
        {
            if (vault == null) return;
            if (Organize.Purge(vault.Data, DateTime.Now) > 0) SaveVault(null);
        }

        // ---- duplicates
        UIElement DuplicatesPage()
        {
            ScrollViewer sv; var sp = Page("Duplicates", "The same account saved more than once: logins for the same site and username, cards with the same number, notes with the same title and text, identities with the same name and email. Extra copies go to the trash, so you can still restore them.", out sv);
            var groups = Organize.Duplicates(Shown());
            if (groups.Count == 0) { sp.Children.Add(new TextBlock { Text = "No duplicates.", Foreground = B("#6ED28C") }); return sv; }
            foreach (var g in groups)
            {
                var group = g; var box = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var keep = new Button { Content = "Keep newest, trash the rest", Style = St("Btn") };
                keep.Click += (s, e) => { foreach (var x in group.Skip(1)) Organize.MoveToTrash(vault.Data, x, DateTime.Now); SaveVault((group.Count - 1) + " extra cop" + (group.Count == 2 ? "y" : "ies") + " of \"" + group[0].Title + "\" moved to the trash."); ShowMain(); };
                DockPanel.SetDock(keep, Dock.Right); head.Children.Add(keep);
                bool samePw = group[0].Type == "login" && group.All(x => x.Password == group[0].Password);
                head.Children.Add(new TextBlock { Text = group[0].Title + "  (" + group.Count + " copies" + (group[0].Type == "login" ? samePw ? ", same password" : ", different passwords - check which one is right" : "") + ")", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                box.Children.Add(head);
                bool first = true;
                foreach (var it in group)
                {
                    var item = it; var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var bs = new StackPanel { Orientation = Orientation.Horizontal };
                    var open = new Button { Content = "Show item", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; open.Click += (s, e) => { view = "all"; lastSearch = ""; selected = item; ShowMain(); };
                    var tr = new Button { Content = "Move to trash", Style = St("Btn") }; tr.Click += (s, e) => { TrashItem(item); ShowMain(); };
                    bs.Children.Add(open); bs.Children.Add(tr); DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                    var bits = new List<string>(); if (it.Username.Length > 0) bits.Add(it.Username); if (it.Host != null) bits.Add(it.Host); if (it.Folder.Length > 0) bits.Add("folder " + it.Folder);
                    bits.Add("changed " + it.Updated.ToString("d MMM yyyy")); if (first) bits.Add("newest"); first = false;
                    row.Children.Add(new TextBlock { Text = string.Join("  -  ", bits), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    box.Children.Add(row);
                }
                sp.Children.Add(Card(box));
            }
            return sv;
        }

        // ---- password history in an item's details: hidden until you open it; each old password can be shown or copied
        bool showHistory;
        void HistorySection(StackPanel sp, Item it)
        {
            if (it.Type != "login" || it.OldPasswords.Count == 0) return;
            var head = new DockPanel { Margin = new Thickness(0, 12, 0, 2) };
            var toggle = new Button { Content = (showHistory ? "Hide" : "Show") + " password history (" + it.OldPasswords.Count + ")", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left };
            toggle.Click += (s, e) => { showHistory = !showHistory; ShowDetail(); };
            head.Children.Add(toggle); sp.Children.Add(head);
            if (!showHistory) return;
            for (int i = 0; i < it.OldPasswords.Count; i++)
            {
                var when = Organize.ReplacedOn(it, i);
                Field(sp, "Used until " + (when.HasValue ? when.Value.ToLocalTime().ToString("d MMM yyyy HH:mm") : "an unknown date"), it.OldPasswords[i], true);
            }
            var clear = new Button { Content = "Clear history", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Forget the " + it.OldPasswords.Count + " old password" + (it.OldPasswords.Count == 1 ? "" : "s") + " of \"" + it.Title + "\"?", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Organize.ClearHistory(it); it.Updated = DateTime.Now; SaveVault("Password history cleared."); ShowDetail();
            };
            sp.Children.Add(clear);
        }

        // ---- exports
        void ExportEncrypted()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save an encrypted export", FileName = "Hallow Keys export " + DateTime.Now.ToString("yyyy-MM-dd") + ".hkbackup", Filter = "Hallow Keys backup (*.hkbackup)|*.hkbackup" };
            if (dlg.ShowDialog(this) != true) return;
            var pw = Ask.NewPassword(this, "Password for this export");
            if (pw == null) return;
            var ed = ExportData(); var items = ed.Items.ToList(); var folders = vault.Data.Folders.ToList(); var templates = vault.Data.Templates.ToList(); var path = dlg.FileName;   // v2.1: one vault, or every vault this PC shows
            var vlist = Vaults.For(vault.Data, items); var what = ExportWhat();
            IsEnabled = false;
            new System.Threading.Thread(() =>
            {
                string err = null;
                try { Vault.WriteExport(path, pw, items, folders, templates, vlist); } catch (Exception e) { err = e.Message; } finally { Gcm.Wipe(pw); }
                Dispatcher.BeginInvoke(new Action(() => { IsEnabled = true; if (err != null) Toast("Couldn't save the export: " + err, true); else Toast("Encrypted export of " + what + " saved (" + items.Count + " items) - Restore backup opens it with the password you just picked."); }));
            }) { IsBackground = true }.Start();
        }
        void ExportCsv()
        {
            if (MessageBox.Show(this, "A CSV export is NOT encrypted.\n\nEvery password, card number, note and 2FA key in your vault will be readable by anyone - and any program - that can open the file. Cloud sync, backups and the Recycle Bin can keep copies of it.\n\nOnly do this to move to another password manager, keep the file off cloud folders, and delete it as soon as you've imported it.\n\nExport everything as plain text?", "Plain CSV export", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            // v1.6: crypto wallets' recovery phrases stay out unless you tick the box
            int wallets = ExportData().Items.Count(i => i.Type == "wallet" && ItemTypes.Val(i, "seed").Length > 0); bool seeds;
            var pw = Ask.Password(this, "Plain CSV export", "Type your master password to confirm it's you:", wallets > 0 ? "Also put the recovery (seed) phrase" + (wallets == 1 ? " of 1 crypto wallet" : "s of " + wallets + " crypto wallets") + " in the file. Anyone with a recovery phrase can empty that wallet - leave this off unless you really need it." : null, out seeds);
            if (pw == null) return;
            IsEnabled = false;
            new System.Threading.Thread(() =>
            {
                string err = null;
                try { using (Vault.Open(KeysSettings.VaultPath, pw)) { } } catch (Exception e) { err = e.Message; } finally { Gcm.Wipe(pw); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true;
                    if (err != null) { Toast(err, true); return; }
                    if (vault == null) return;
                    var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save the plain-text export (NOT encrypted)", FileName = "Hallow Keys plain export " + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", Filter = "CSV (*.csv)|*.csv" };
                    if (dlg.ShowDialog(this) != true) return;
                    try
                    {
                        var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(Organize.ToCsv(ExportData(), seeds))).ToArray();
                        Vault.WriteAtomic(dlg.FileName, bytes, false); Array.Clear(bytes, 0, bytes.Length);
                        Toast("Exported " + ExportData().Items.Count + " items (" + ExportWhat() + ") as plain text - delete the file as soon as you're done with it.", true);
                    }
                    catch (Exception e) { Toast("Couldn't save: " + e.Message, true); }
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- quick search: a global shortcut (Ctrl+Shift+Space unless changed) opens Hallow Keys with the search box ready.
        // If another app already uses the combination it stays off (and Settings says so); it's tried again every few minutes.
        GlobalHotkey hotkey; bool hotkeyTaken, searchAfterUnlock; DispatcherTimer hotkeyRetry;
        void StartHotkey()
        {
            try { hotkey = new GlobalHotkey(QuickSearch); } catch { hotkey = null; return; }
            ApplyHotkey();
            hotkeyRetry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            hotkeyRetry.Tick += (s, e) => { if (hotkeyTaken && S.QuickSearchKey && !capturingKey) ApplyHotkey(); };
            hotkeyRetry.Start();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && vault != null && !capturingKey) { e.Handled = true; OpenSearch(); }
            };
        }
        void ApplyHotkey()
        {
            if (hotkey == null) return;
            if (!S.QuickSearchKey) { hotkey.Clear(); hotkeyTaken = false; TestLog("hotkey off"); return; }
            bool was = hotkeyTaken;
            hotkeyTaken = !hotkey.Set(S.QuickSearchCombo);
            if (hotkeyTaken != was || !hotkeyTaken) TestLog(hotkeyTaken ? "hotkey taken " + S.QuickSearchCombo : "hotkey on " + S.QuickSearchCombo);
        }
        void StopHotkey() { if (hotkeyRetry != null) hotkeyRetry.Stop(); if (hotkey != null) hotkey.Close(); hotkey = null; }
        void QuickSearch()
        {
            TestLog("hotkey pressed");
            if (KeysSettings.TestDir != null && Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_NOSHOW") == "1") return;   // tests: no window on the screen
            ShowFromTray();
            if (vault == null) { searchAfterUnlock = true; return; }
            OpenSearch();
        }
        void OpenSearch()
        {
            if (editing != null) return;   // never throw away an item that's being edited
            if (detail == null) { view = "all"; ShowMain(); }
            Dispatcher.BeginInvoke(new Action(() => { if (search != null) { search.Focus(); Keyboard.Focus(search); search.SelectAll(); } }), DispatcherPriority.Input);
        }
        void AfterUnlock13()
        {
            PurgeTrash();
            if (searchAfterUnlock) { searchAfterUnlock = false; OpenSearch(); }
        }
        bool capturingKey;
        void HotkeyCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Quick search shortcut", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Press it anywhere to open Hallow Keys with the search box ready (Ctrl+F does the same inside the window). If another app already uses the combination, it stays off - pick another one.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action show = () =>
            {
                if (!S.QuickSearchKey) { status.Text = "Off"; status.Foreground = Muted; }
                else if (!Snap.On && (hotkey == null || hotkeyTaken)) { status.Text = "Another app already uses " + S.QuickSearchCombo + " - it's off. Click the box and press another combination."; status.Foreground = B("#F0BE5A"); }
                else { status.Text = "On"; status.Foreground = B("#6ED28C"); }
            };
            box.Children.Add(Switch("Use a shortcut for quick search", S.QuickSearchKey, v => { S.QuickSearchKey = v; S.Save(); ApplyHotkey(); show(); }));
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var keyBox = new TextBox { Style = St("Field"), Text = S.QuickSearchCombo, IsReadOnly = true, Width = 210, Cursor = Cursors.Hand, ToolTip = "Click, then press the combination you want (Esc to cancel)" };
            keyBox.GotKeyboardFocus += (s, e) => { capturingKey = true; if (hotkey != null) hotkey.Clear(); keyBox.Text = "Press a combination..."; };
            keyBox.LostKeyboardFocus += (s, e) => { capturingKey = false; keyBox.Text = S.QuickSearchCombo; ApplyHotkey(); show(); };
            keyBox.Unloaded += (s, e) => { if (capturingKey) { capturingKey = false; ApplyHotkey(); } };   // the page went away mid-change
            keyBox.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Escape || k == Key.Tab) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); return; }
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                var combo = GlobalHotkey.Format(Keyboard.Modifiers, win, k);
                if (combo == null) return;   // just a modifier so far
                uint m, vk;
                if (!GlobalHotkey.Parse(combo, out m, out vk)) { status.Text = "Use Ctrl, Alt or Win with a key (" + combo + " isn't allowed)."; status.Foreground = B("#F0BE5A"); return; }
                if (hotkey != null && hotkey.Set(combo)) { hotkey.Clear(); S.QuickSearchCombo = combo; S.QuickSearchKey = true; S.Save(); keyBox.Text = combo; Toast("Quick search shortcut: " + combo + "."); }
                else { status.Text = combo + " is used by another app - try another one."; status.Foreground = B("#F0BE5A"); return; }
                Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this);
            };
            DockPanel.SetDock(keyBox, Dock.Left); row.Children.Add(keyBox); row.Children.Add(status);
            box.Children.Add(row); show();
            sp.Children.Add(Card(box));
        }

        // developer tests only (inside HALLOW_KEYS_TESTDIR): a log of events, never any item data
        static void TestLog(string line)
        {
            if (KeysSettings.TestDir == null) return;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(KeysSettings.Dir, "test-events.log"), line + "\r\n"); } catch { }
        }
    }
}
