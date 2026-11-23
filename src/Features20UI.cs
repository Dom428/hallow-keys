// Hallow Keys 2.0 - the window's side of sync, the Secret Key and the Emergency Kit:
//  - Settings > Sync: the folder, on / off, Sync now, the status, the protection (with the upgrade for 1.x vaults), the Emergency Kit,
//    and "Restore an older version" (this PC's encrypted snapshots)
//  - sync while unlocked: after every save, when the folder changes (FileSystemWatcher) and every minute. It runs in the background on a
//    copy of the vault, and what came in is merged into the open vault on the window's thread (edits made meanwhile are kept)
//  - the first screen on a new PC: "Open a synced vault" (folder + master password + Secret Key)
//  - the lock screen asks for the Secret Key when this PC doesn't have it; a password changed on another PC unlocks via the sync folder
//  - the sidebar's status line ("Synced 2 min ago", "Waiting for OneDrive", "Conflict merged: 1 item") and status.json's "sync"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace HallowKeys
{
    public partial class MainWindow
    {
        string syncState = "off", syncMsg = ""; DateTime? lastSyncOk; int lastConflicts; DateTime lastConflictTime;
        bool syncBusy, syncAgain, vaultBusy; int saveSeq;
        FileSystemWatcher syncWatch; DispatcherTimer syncTimer, syncDebounce, syncTick;
        TextBlock syncLine;

        // ---- after unlocking / locking
        void AfterUnlock20()
        {
            if (vault != null) { var k = vault.CurrentSecretKey(); Gcm.Wipe(k); }   // a Secret Key made on another PC (inside the vault) is kept on this PC
            DateTime t; if (lastSyncOk == null && DateTime.TryParse(S.LastSync, out t)) lastSyncOk = t;
            StartSync();
        }
        void StartSync()
        {
            StopSyncWatch();
            if (vault == null || !S.SyncOn || string.IsNullOrEmpty(S.SyncFolder)) { SetSync(S.SyncOn ? "waiting" : "off", S.SyncOn ? "Choose the sync folder in Settings" : ""); return; }
            try
            {
                if (Directory.Exists(S.SyncFolder))
                {
                    syncWatch = new FileSystemWatcher(S.SyncFolder, "*.hkvault") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, IncludeSubdirectories = false };
                    FileSystemEventHandler h = (s, e) => Dispatcher.BeginInvoke(new Action(() => SyncSoon(1500)));
                    syncWatch.Changed += h; syncWatch.Created += h; syncWatch.Renamed += (s, e) => Dispatcher.BeginInvoke(new Action(() => SyncSoon(1500)));
                    syncWatch.Error += (s, e) => Dispatcher.BeginInvoke(new Action(() => SyncSoon(5000)));
                    syncWatch.EnableRaisingEvents = true;
                }
            }
            catch { syncWatch = null; }
            if (syncTimer == null) { syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) }; syncTimer.Tick += (s, e) => { if (vault != null && S.SyncOn) { if (syncWatch == null) StartSync(); else SyncNow(); } }; }
            syncTimer.Start();
            if (syncTick == null) { syncTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; syncTick.Tick += (s, e) => UpdateSyncLine(); syncTick.Start(); }
            SyncSoon(300);
        }
        void StopSyncWatch() { if (syncWatch != null) { try { syncWatch.EnableRaisingEvents = false; syncWatch.Dispose(); } catch { } syncWatch = null; } }
        void StopSync() { StopSyncWatch(); if (syncTimer != null) syncTimer.Stop(); if (syncDebounce != null) syncDebounce.Stop(); }
        void SyncSoon(int ms)
        {
            if (vault == null || !S.SyncOn) return;
            if (syncDebounce == null) { syncDebounce = new DispatcherTimer(); syncDebounce.Tick += (s, e) => { syncDebounce.Stop(); SyncNow(); }; }
            syncDebounce.Stop(); syncDebounce.Interval = TimeSpan.FromMilliseconds(Math.Max(50, ms)); syncDebounce.Start();
        }
        // one sync round in the background, on a copy of the open vault; what came in is merged into the open vault afterwards
        void SyncNow()
        {
            if (vault == null || !S.SyncOn || string.IsNullOrEmpty(S.SyncFolder)) return;
            if (syncBusy || vaultBusy) { syncAgain = true; if (vaultBusy) SyncSoon(2000); return; }
            syncBusy = true; syncAgain = false;
            byte[] bytes, key; var folder = S.SyncFolder; var path = vault.Path; int seq = saveSeq; var live = vault;
            try { bytes = vault.Serialize(); key = vault.DataKeyCopy(); }
            catch (Exception e) { syncBusy = false; SetSync("error", "Couldn't sync: " + e.Message); return; }
            new Thread(() =>
            {
                SyncResult res = null; Vault shadow = null; string err = null;
                try { shadow = Vault.OpenBytes(path, bytes, null, false, key, null); shadow.Path = path; res = SyncFolder.Run(shadow, folder, Device.Id, Device.Name); }
                catch (Exception e) { err = e.Message; }
                finally { Gcm.Wipe(key); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    syncBusy = false;
                    try
                    {
                        if (res == null) { SetSync("error", "Couldn't sync: " + err); return; }
                        if (vault != null && vault == live)
                        {
                            if (res.Merge.LocalChanged)
                            {
                                var rep = SyncMerge.Merge(vault, shadow, Device.Id, Device.Name);
                                if (rep.KeysAdopted || res.Merge.KeysAdopted) { var k = vault.CurrentSecretKey(); Gcm.Wipe(k); }
                                if (saveSeq != seq || rep.RemoteBehind) { try { vault.Save(); } catch { } syncAgain = true; }   // edits made while it ran: saved, and another round sends them
                                AfterSave(); WriteStatus(); RefreshAfterSync(res);
                            }
                            else if (saveSeq != seq) syncAgain = true;
                        }
                        if (res.Merge.Conflicts > 0) { lastConflicts = res.Merge.Conflicts; lastConflictTime = DateTime.Now; }
                        if (res.State == "synced") { lastSyncOk = DateTime.Now; S.LastSync = DateTime.Now.ToString("o"); S.Save(); }
                        TestLog("sync: " + res.State + " in " + res.Merge.Added + " up " + res.Merge.Updated + " del " + res.Merge.Removed + " conflicts " + res.Merge.Conflicts + " files " + res.ConflictFiles + " wrote " + res.Wrote);
                        SetSync(res.State, res.Message);
                        if (res.Merge.Conflicts > 0) Toast("Conflict merged: " + res.Merge.Conflicts + " item" + (res.Merge.Conflicts == 1 ? "" : "s") + " changed on two PCs at once - both versions are kept (look for \"conflict copy\").");
                    }
                    finally { if (shadow != null) shadow.Dispose(); if (syncAgain && vault != null) SyncSoon(1000); }
                }));
            }) { IsBackground = true, Name = "sync" }.Start();
        }
        void RefreshAfterSync(SyncResult res)
        {
            if (editing != null) { UpdateSyncLine(); return; }   // never pull the editor away; the open item stays as typed
            if (selected != null) selected = vault.Data.Items.FirstOrDefault(i => i.Id == selected.Id);
            ShowMain();
        }
        void SetSync(string state, string msg) { syncState = state; syncMsg = msg ?? ""; UpdateSyncLine(); WriteStatus(); }
        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + " h ago";
            return "on " + t.ToString("d MMM HH:mm");
        }
        string SyncText()
        {
            if (!S.SyncOn) return "Sync is off";
            if (syncState == "synced" || (syncState == "off" && lastSyncOk != null))
            {
                var c = lastConflicts > 0 && (DateTime.Now - lastConflictTime).TotalHours < 1 ? "Conflict merged: " + lastConflicts + " item" + (lastConflicts == 1 ? "" : "s") + " - " : "";
                return c + (lastSyncOk != null ? (c.Length > 0 ? "synced " : "Synced ") + Ago(lastSyncOk.Value) : "Syncing...");
            }
            if (syncState == "off") return "Syncing...";
            return syncMsg.Length > 0 ? syncMsg : "Waiting for " + SyncFolder.Provider(S.SyncFolder);
        }
        void UpdateSyncLine()
        {
            if (syncLine == null) return;
            syncLine.Text = SyncText();
            syncLine.Foreground = syncState == "error" ? B("#F0BE5A") : syncState == "waiting" ? B("#F0BE5A") : Muted;
        }
        // the sidebar: the status line (sync on) above Lock now
        UIElement SyncSidebar()
        {
            if (!S.SyncOn) { syncLine = null; return new StackPanel(); }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8), Cursor = Cursors.Hand, ToolTip = "Sync: " + (S.SyncFolder.Length > 0 ? S.SyncFolder : "no folder") + " - click for Settings" };
            var ic = Icons.Make("arrows-rotate", 12, Muted); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(ic);
            syncLine = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 180, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(syncLine); UpdateSyncLine();
            row.MouseLeftButtonUp += (s, e) => { view = "settings"; selected = null; editing = null; ShowMain(); };
            return row;
        }
        // the sidebar's offer for a 1.x vault (PBKDF2, no Secret Key)
        void UpgradeOffer(StackPanel top)
        {
            if (vault == null || !vault.NeedsUpgrade) return;
            var b = new Button { Content = "Upgrade protection", Style = St("Btn"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Argon2id and a Secret Key (like 1Password's): a copy of the vault file alone can't be attacked by guessing your master password. Needed for sync." };
            b.Click += (s, e) => DoUpgrade(null, null);
            top.Children.Add(b);
        }
        Dictionary<string, object> SyncStatus()
        {
            DateTime t; var last = lastSyncOk ?? (DateTime.TryParse(S.LastSync, out t) ? (DateTime?)t : null);
            return new Dictionary<string, object> { { "on", S.SyncOn }, { "state", !S.SyncOn ? "off" : vault == null ? "locked" : syncState == "off" ? "starting" : syncState },
                { "provider", S.SyncOn ? SyncFolder.Provider(S.SyncFolder) : null }, { "lastSync", last == null ? null : Family.Time(last.Value) },
                { "conflictsMerged", lastConflicts }, { "text", vault == null ? (S.SyncOn ? "Locked - syncs when unlocked" : "Sync is off") : SyncText() } };
        }

        // ---- Settings > Sync
        void SyncCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Sync between your PCs", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Keep the vault in a folder that OneDrive, Google Drive, Dropbox, a NAS or Syncthing copies to your other PCs - no Hallow server, no account. Each PC keeps its own copy and works offline. Changes are merged item by item; if the same item was changed on two PCs at once, both versions are kept (one as a \"conflict copy\").", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            // protection
            var prot = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            prot.Inlines.Add(new System.Windows.Documents.Run("Protection: ") { FontWeight = FontWeights.SemiBold });
            prot.Inlines.Add(new System.Windows.Documents.Run(vault.KdfDescription + (vault.UsesSecretKey ? " + Secret Key" : ", no Secret Key")));
            box.Children.Add(prot);
            if (vault.NeedsUpgrade)
            {
                box.Children.Add(new TextBlock { Text = "This vault still has the protection from Hallow Keys 1.x. Upgrade it to Argon2id with a Secret Key: then a copy of the vault file alone (from a cloud folder, a backup, a stolen laptop) can't be attacked by guessing your master password. Sync needs it.", Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
                var ub = new Button { Content = "Upgrade protection", Style = St("Accent"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) }; ub.Click += (s, e) => DoUpgrade(null, null); box.Children.Add(ub);
            }
            // folder
            var fr = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var choose = new Button { Content = "Choose folder", Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(choose, Dock.Right);
            choose.Click += (s, e) => ChooseSyncFolder();
            fr.Children.Add(choose);
            fr.Children.Add(new TextBlock { Text = "Folder: " + (S.SyncFolder.Length > 0 ? S.SyncFolder + "  (" + SyncFolder.Provider(S.SyncFolder) + ")" : "none chosen yet"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            box.Children.Add(fr);
            box.Children.Add(Switch("Sync this vault through the folder", S.SyncOn, v => ToggleSync(v)));
            if (S.SyncOn)
            {
                var st = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var now = new Button { Content = "Sync now", Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(now, Dock.Right); now.Click += (s, e) => { SyncNow(); Toast("Syncing..."); };
                st.Children.Add(now);
                var line = new TextBlock { Text = SyncText(), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = syncState == "synced" ? B("#6ED28C") : syncState == "off" ? Muted : B("#F0BE5A") };
                st.Children.Add(line); box.Children.Add(st);
            }
            box.Children.Add(new TextBlock { Text = "Add another PC: install Hallow Keys there and click \"Open a synced vault\" on its first screen. You need the folder, your master password and your Secret Key - it's on your Emergency Kit. Your recovery code opens the vault without the Secret Key.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
            var kr = new StackPanel { Orientation = Orientation.Horizontal };
            var kit = new Button { Content = "Emergency Kit", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0), IsEnabled = vault.UsesSecretKey, ToolTip = "Shows your Secret Key on a printable sheet (asks for your master password first)" };
            kit.Click += (s, e) => ShowKitAfterPassword();
            kr.Children.Add(kit); box.Children.Add(kr);
            sp.Children.Add(Card(box));
            // restore an older version
            var rv = new StackPanel();
            rv.Children.Add(new TextBlock { Text = "Restore an older version", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            rv.Children.Add(new TextBlock { Text = "Hallow Keys keeps up to " + Versions.Keep + " encrypted snapshots of your vault on this PC: one every 10 minutes while you make changes, and one before changes from another PC are merged. Restoring puts items back as they were then (on your other PCs too); anything newer goes to the trash, and the vault as it is now is kept as a snapshot first.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var list = Snap.On ? SnapVersions() : Versions.List();
            if (list.Count == 0) rv.Children.Add(new TextBlock { Text = "No snapshots yet - they appear as you make changes.", Foreground = Muted });
            foreach (var en in list.Take(8))
            {
                var e1 = en; var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var rb = new Button { Content = "Restore", Style = St("Btn"), Padding = new Thickness(10, 3, 10, 3) }; DockPanel.SetDock(rb, Dock.Right); rb.Click += (s, e) => RestoreVersion(e1);
                row.Children.Add(rb);
                row.Children.Add(new TextBlock { Text = e1.When.ToString("ddd d MMM yyyy, HH:mm") + "   (" + Math.Max(1, e1.Bytes / 1024) + " KB)", VerticalAlignment = VerticalAlignment.Center });
                rv.Children.Add(row);
            }
            if (list.Count > 8) rv.Children.Add(new TextBlock { Text = "and " + (list.Count - 8) + " older (in the versions folder next to your vault)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(Card(rv));
        }
        static List<Versions.Entry> SnapVersions() { var n = DateTime.Now; return new List<Versions.Entry> { new Versions.Entry { When = n.AddMinutes(-12), Bytes = 48210 }, new Versions.Entry { When = n.AddHours(-3), Bytes = 47980 }, new Versions.Entry { When = n.AddDays(-1).AddHours(-2), Bytes = 46102 } }; }

        void ChooseSyncFolder()
        {
            using (var d = new WF.FolderBrowserDialog { Description = "Pick the folder your cloud service (OneDrive, Google Drive, Dropbox...), NAS or Syncthing keeps in step on your PCs. Hallow Keys keeps vault.hkvault there.", ShowNewFolderButton = true })
            {
                if (S.SyncFolder.Length > 0 && Directory.Exists(S.SyncFolder)) d.SelectedPath = S.SyncFolder;
                else { var od = Environment.GetEnvironmentVariable("OneDrive"); if (!string.IsNullOrEmpty(od) && Directory.Exists(od)) d.SelectedPath = od; }
                if (d.ShowDialog() != WF.DialogResult.OK) return;
                var f = d.SelectedPath;
                if (IsInside(f, KeysSettings.Dir)) { Toast("Pick a folder outside Hallow Keys' own data folder.", true); return; }
                var was = S.SyncOn; S.SyncFolder = f; S.Save();
                if (was) ToggleSync(true); else { Toast("Folder chosen - turn on \"Sync this vault through the folder\" to start."); ShowMain(); }
            }
        }
        static bool IsInside(string a, string b) { try { var x = Path.GetFullPath(a).TrimEnd('\\') + "\\"; var y = Path.GetFullPath(b).TrimEnd('\\') + "\\"; return x.StartsWith(y, StringComparison.OrdinalIgnoreCase) || y.StartsWith(x, StringComparison.OrdinalIgnoreCase); } catch { return false; } }

        void ToggleSync(bool on)
        {
            if (!on) { S.SyncOn = false; S.Save(); StopSync(); SetSync("off", ""); Toast("Sync is off. This PC keeps its own copy; the one in the folder stays there."); ShowMain(); return; }
            if (S.SyncFolder.Length == 0 || !Directory.Exists(S.SyncFolder)) { Toast("Choose the folder first (Choose folder).", true); ShowMain(); return; }
            if (vault.NeedsUpgrade) { DoUpgrade("Sync needs the stronger protection first. ", () => ToggleSync(true)); return; }
            var main = SyncFolder.MainPath(S.SyncFolder);
            if (File.Exists(main))
            {
                Vault h = null; try { h = Vault.PeekHeader(Vault.ReadFile(main)); } catch { }
                if (h != null && !h.VaultId.SequenceEqual(vault.VaultId)) { JoinOtherVault(main); return; }
            }
            S.SyncOn = true; S.Save(); syncState = "off"; StartSync();
            Toast("Sync is on - your vault is kept in " + SyncFolder.Provider(S.SyncFolder) + "."); ShowMain();
        }
        // the folder already has a vault made on another PC: join it (this PC's items are added to it)
        void JoinOtherVault(string main)
        {
            if (MessageBox.Show(this, "That folder already has a Hallow Keys vault, made on another PC.\n\nJoin it? Your items on this PC are added to it, and this PC then uses that vault (its master password and Secret Key). Your vault as it is now is kept in the data folder as a file, just in case.", "Join the synced vault", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { ShowMain(); return; }
            var h = Vault.PeekHeader(Vault.ReadFile(main));
            byte[] sk = null;
            if (h.UsesSecretKey && !SecretKey.Has(h.VaultId, h.SecretKeyId))
            {
                var t = Ask.Text(this, "Join the synced vault", "The Secret Key of that vault (from its Emergency Kit, starts with HK-):", "Secret Key");
                if (t == null) { ShowMain(); return; }
                string err; sk = SecretKey.Parse(t, out err); if (sk == null) { Toast(err, true); ShowMain(); return; }
            }
            var pw = Ask.Password(this, "Join the synced vault", "The master password of that vault:");
            if (pw == null) { Gcm.Wipe(sk); ShowMain(); return; }
            IsEnabled = false; vaultBusy = true;
            var mine = vault; var items = mine.Data.Items.Concat(mine.Data.Trash).Select(i => ItemTypes.Clone(i, false)).ToList(); var folders = mine.Data.Folders.ToList(); var templates = mine.Data.Templates.ToList();
            new Thread(() =>
            {
                Vault other = null; string err = null; int added = 0;
                try
                {
                    other = Vault.Open(main, pw, sk);
                    foreach (var it in items) it.Sync = null;
                    added = Import.Merge(other.Data, new Import.Result { Items = items.Where(i => i.Deleted == null).ToList(), Folders = folders, Templates = templates });
                    foreach (var it in items.Where(i => i.Deleted != null)) other.Data.Trash.Add(it);
                    var keep = Path.Combine(KeysSettings.Dir, "vault-before-joining-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".hkvault");
                    if (File.Exists(KeysSettings.VaultPath)) File.Copy(KeysSettings.VaultPath, keep, true);
                    other.Path = KeysSettings.VaultPath;
                    if (sk != null) SecretKey.Store(other.VaultId, sk); else { var k = other.CurrentSecretKey(); Gcm.Wipe(k); }
                    other.Save();
                }
                catch (Exception e) { err = e.Message; if (other != null) { other.Dispose(); other = null; } }
                finally { Gcm.Wipe(pw); Gcm.Wipe(sk); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true; vaultBusy = false;
                    if (other == null) { Toast(err, true); ShowMain(); return; }
                    if (S.UseHello) { Hello.Disable(); S.UseHello = false; }   // the saved Windows Hello key was the old vault's
                    if (S.PhoneCopy && other.Data.PhoneKey == null) { S.PhoneCopy = false; PhoneCopy.Remove(); }
                    mine.Dispose(); vault = other; selected = null; editing = null;
                    S.SyncOn = true; S.Save(); syncState = "off"; StartSync(); WriteStatus(); ShowMain();
                    Toast("Joined the synced vault - " + added + " of your items were added to it." + (S.UseHello ? "" : ""));
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- the upgrade: Argon2id + a new Secret Key, then the Emergency Kit
        void DoUpgrade(string why, Action after)
        {
            var pw = Ask.Password(this, "Upgrade protection", (why ?? "") + "Type your master password. Hallow Keys switches it to Argon2id and makes your Secret Key (128 random bits, kept on this PC). Print the Emergency Kit that follows: you'll need the Secret Key to add another PC.");
            if (pw == null) { ShowMain(); return; }
            IsEnabled = false; vaultBusy = true; Toast("Upgrading...");
            new Thread(() =>
            {
                string err = null; byte[] sk = null;
                try { using (Vault.Open(KeysSettings.VaultPath, pw)) { } sk = vault.UpgradeProtection(pw); }
                catch (Exception e) { err = e.Message; }
                finally { Gcm.Wipe(pw); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true; vaultBusy = false;
                    if (err != null) { Toast(err, true); ShowMain(); return; }
                    WriteStatus(); AfterSave(); ShowMain(); Toast("Upgraded: Argon2id + Secret Key.");
                    var text = SecretKey.Format(sk); Gcm.Wipe(sk);
                    ShowEmergencyKit(null, text, "upgrade");
                    if (after != null) after(); else SyncSoon(500);
                }));
            }) { IsBackground = true }.Start();
        }
        void ShowKitAfterPassword()
        {
            var pw = Ask.Password(this, "Emergency Kit", "Your master password (your Secret Key is shown next):");
            if (pw == null) return;
            IsEnabled = false;
            new Thread(() =>
            {
                string err = null, text = null;
                try { using (Vault.Open(KeysSettings.VaultPath, pw)) { } var k = vault.CurrentSecretKey(); if (k == null) err = "This PC doesn't have the Secret Key."; else { text = SecretKey.Format(k); Gcm.Wipe(k); } }
                catch (Exception e) { err = e.Message; }
                finally { Gcm.Wipe(pw); }
                Dispatcher.BeginInvoke(new Action(() => { IsEnabled = true; if (err != null) Toast(err, true); else ShowEmergencyKit(null, text, "show"); }));
            }) { IsBackground = true }.Start();
        }
        void RestoreVersion(Versions.Entry e)
        {
            VaultData d;
            try { d = Versions.Peek(vault, e.Path); } catch (Exception ex) { Toast("That snapshot can't be opened: " + ex.Message, true); return; }
            var n = d.Items.Count; d = null;
            if (MessageBox.Show(this, "Restore your vault as it was on " + e.When.ToString("d MMMM yyyy, HH:mm") + " (" + n + " items)?\n\nItems go back to how they were then - on your other PCs too, if sync is on. Anything made since goes to the trash (not deleted). Your vault as it is now is kept as a snapshot first, so you can undo this.", "Restore an older version", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { var c = Versions.Restore(vault, e.Path); selected = null; editing = null; SaveVault("Restored the version from " + e.When.ToString("d MMM HH:mm") + " (" + c + " items changed)."); ShowMain(); }
            catch (Exception ex) { Toast("Couldn't restore: " + ex.Message, true); }
        }

        // ---- the Emergency Kit (recovery code + Secret Key + how to get back in); without a Secret Key it's the 1.x recovery sheet
        void ShowEmergencyKit(string code, string secretKey, string why)
        {
            bool kit = secretKey != null;
            var w = new Window { Title = kit ? "Hallow Keys - Emergency Kit" : "Hallow Keys - recovery sheet", Width = 600, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null,
                Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = FontFamily }; Look.Track(w);
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            string head = why == "new" ? "Your vault is ready. Print your Emergency Kit." : why == "upgrade" ? "Upgraded. Print your new Emergency Kit." : why == "rotated" ? "You have a new Secret Key. Print your new Emergency Kit." : why == "show" ? "Your Emergency Kit" : kit ? "Your new recovery code - print your Emergency Kit" : "Your new recovery sheet";
            if (!kit && why == "new") head = "Your vault is ready. Print your recovery sheet.";
            sp.Children.Add(new TextBlock { Text = head, FontSize = 17, FontWeight = FontWeights.Bold });
            sp.Children.Add(new TextBlock { Text = kit ? (code != null ? "The recovery code is shown only now. Print this sheet (or write it down) and keep it somewhere safe and offline - not on this PC. You need the Secret Key to add another PC, and the recovery code if you ever forget your master password." : "Print this sheet (or write the Secret Key down) and keep it somewhere safe and offline. Your existing recovery code (on your earlier sheet) still works.")
                : "If you forget your master password, this code is the only way back in. It's shown only now. Print it (or write it down) and keep it somewhere safe and offline - not on this PC.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
            sp.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Child = KitVisual(code, secretKey, S.SyncFolder) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var print = new Button { Content = "Print", Style = St("Accent"), Margin = new Thickness(0, 0, 8, 0) };
            print.Click += (s, e) => { var pd = new PrintDialog(); if (pd.ShowDialog() == true) { var v = KitVisual(code, secretKey, S.SyncFolder); v.Measure(new Size(pd.PrintableAreaWidth, double.PositiveInfinity)); v.Arrange(new Rect(new Point(40, 40), v.DesiredSize)); pd.PrintVisual(v, kit ? "Hallow Keys Emergency Kit" : "Hallow Keys recovery sheet"); if (vault != null) { MarkKit(); if (code != null) MarkRecovery("kit"); } } };   // v2.14: printed = the checklist's step 3 (and 1 when the code is on it)
            var done = new Button { Content = "I've saved it", Style = St("Btn") }; done.Click += (s, e) => w.Close();
            row.Children.Add(print); row.Children.Add(done); sp.Children.Add(row);
            w.Content = sp;
            if (Snap.On) { Snap.Save(w, snapDir + "\\" + (kit ? (code != null ? "emergency-kit.png" : "emergency-kit-" + why + ".png") : "recovery.png"), 600, 0); return; }
            if (TestNoShow) { TestLog("emergency kit shown: " + why + (code != null ? " with recovery code" : "") + (kit ? " with Secret Key" : "")); return; }
            w.ShowDialog();
        }
        static FrameworkElement KitVisual(string code, string secretKey, string folder)
        {
            if (secretKey == null) return RecoverySheetVisual(code);
            var sp = new StackPanel { Margin = new Thickness(26), Width = 500 };
            var black = Brushes.Black; var grey = new SolidColorBrush(Color.FromRgb(90, 90, 90));
            sp.Children.Add(new TextBlock { Text = "Hallow Keys - Emergency Kit", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = black });
            sp.Children.Add(new TextBlock { Text = "Made " + DateTime.Now.ToString("d MMMM yyyy") + " on " + Environment.MachineName + " - a newer kit replaces this one", Foreground = grey, Margin = new Thickness(0, 2, 0, 14) });
            sp.Children.Add(new TextBlock { Text = "Secret Key", Foreground = grey });
            sp.Children.Add(new TextBlock { Text = secretKey, FontFamily = new FontFamily("Consolas"), FontSize = 19, FontWeight = FontWeights.Bold, Foreground = black, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "Recovery code", Foreground = grey });
            sp.Children.Add(new TextBlock { Text = code ?? "(on your recovery sheet - or make a new one in Settings > Recovery sheet)", FontFamily = code != null ? new FontFamily("Consolas") : new FontFamily("Segoe UI"), FontSize = code != null ? 19 : 12, FontWeight = code != null ? FontWeights.Bold : FontWeights.Normal, Foreground = black, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "Master password", Foreground = grey });
            sp.Children.Add(new TextBlock { Text = "(never written here - only you know it)", Foreground = black, FontSize = 12, Margin = new Thickness(0, 2, 0, 10) });
            sp.Children.Add(new TextBlock { Text = "Sync folder", Foreground = grey });
            sp.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(folder) ? "______________________________________________" : folder, Foreground = black, FontSize = 12, Margin = new Thickness(0, 2, 0, 14), TextWrapping = TextWrapping.Wrap });
            Action<string, string> step = (h, t) =>
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = black, Margin = new Thickness(0, 0, 0, 6) };
                tb.Inlines.Add(new System.Windows.Documents.Run(h + " ") { FontWeight = FontWeights.Bold }); tb.Inlines.Add(new System.Windows.Documents.Run(t)); sp.Children.Add(tb);
            };
            // v2.5: the full layout - what each thing is, where you keep it, and the restore steps in plain words
            Action<string> section = t => sp.Children.Add(new TextBlock { Text = t, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = black, Margin = new Thickness(0, 10, 0, 6) });
            Action<string> line = t => sp.Children.Add(new TextBlock { Text = t, FontSize = 12, Foreground = black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
            section("Where I keep things (fill in by hand)");
            line("This kit is kept: ____________________________________________");
            line("My master password is: only in my head / written down at: ____________________");
            line("Encrypted backups (.hkbackup) are at: ____________________________________");
            line("My emergency contact (has an emergency package from me): ______________________");
            section("If I get a new PC, or this one breaks");
            step("1.", "Install Hallow Keys from github.com/Dom428/hallow-keys (the zip, then INSTALL.bat).");
            step("2.", "On the first screen click \"Open a synced vault\" and pick the sync folder written above.");
            step("3.", "Type your master password and the Secret Key from this sheet. Everything comes back, including passkeys and shared vaults.");
            step("No sync folder?", "Click \"Restore backup\" instead and pick your latest .hkbackup file; it needs the master password (and this Secret Key) or the recovery code.");
            section("If I forget my master password");
            step("1.", "On the lock screen click \"Forgot it? Use your recovery code\".");
            step("2.", "Type the recovery code from this sheet. It opens the vault even without the Secret Key.");
            step("3.", "Pick a new master password straight away, then print a new Emergency Kit (Settings) and destroy this one.");
            section("If someone I trust needs to get in");
            step("With an emergency package:", "they open it on their own Hallow Keys (Sharing > Open an emergency package); if I made it need my recovery code, they also need the code above.");
            step("Without one:", "they need this sheet, my vault file or sync folder, and the recovery code - which is why this sheet must be kept as safe as a house key.");
            section("Keep it safe");
            line("Anyone with the recovery code and your vault file can open your vault. The Secret Key alone can't - it only stops someone who copies your vault file from guessing your master password.");
            line("Keep this on paper, offline: a safe, a locked drawer, with your will. Not as a photo, not in email, not in the cloud. A newer kit replaces this one.");
            line("Hallow Keys has no company or server behind it: nobody can reset your password for you. This sheet is the way back in.");
            return sp;
        }

        // ---- a new PC: open the vault that's already in a sync folder
        void ShowAddDevice()
        {
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(16), Padding = new Thickness(30, 26, 30, 26), Width = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel(); card.Child = sp;
            sp.Children.Add(TitleRow(26));
            sp.Children.Add(new TextBlock { Text = "Open your synced vault", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });
            sp.Children.Add(new TextBlock { Text = "Use the vault you already have on another PC. Pick the folder it syncs through (OneDrive, Google Drive, Dropbox, a NAS, Syncthing...), then type your master password and your Secret Key from your Emergency Kit.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "Sync folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
            var fr = new DockPanel();
            var browse = new Button { Content = "Choose", Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(browse, Dock.Right); fr.Children.Add(browse);
            var folderBox = new TextBox { Style = St("Field"), Text = addFolder ?? "" }; fr.Children.Add(folderBox); sp.Children.Add(fr);
            var folderNote = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Muted }; sp.Children.Add(folderNote);
            sp.Children.Add(new TextBlock { Text = "Master password", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
            var pb = new PasswordBox { Style = St("Pwd") }; sp.Children.Add(pb);
            sp.Children.Add(new TextBlock { Text = "Secret Key", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
            var skBox = SecretKeyBox(); sp.Children.Add(skBox.Item1); sp.Children.Add(skBox.Item2);
            Action checkFolder = () =>
            {
                var f = folderBox.Text.Trim(); addFolder = f;
                if (f.Length == 0) { folderNote.Text = ""; return; }
                if (Snap.On) { folderNote.Text = "Found vault.hkvault - it needs your Secret Key."; folderNote.Foreground = B("#6ED28C"); return; }
                var m = SyncFolder.MainPath(f);
                if (!Directory.Exists(f)) { folderNote.Text = "That folder isn't there."; folderNote.Foreground = B("#F0BE5A"); }
                else if (!File.Exists(m)) { folderNote.Text = "No vault.hkvault in that folder yet - is your cloud service done downloading?"; folderNote.Foreground = B("#F0BE5A"); }
                else { Vault h = null; try { h = Vault.PeekHeader(Vault.ReadFile(m)); } catch { } folderNote.Text = h == null ? "There's a vault.hkvault, but it can't be read yet (still downloading?)." : "Found vault.hkvault" + (h.UsesSecretKey ? " - it needs your Secret Key." : " (made without a Secret Key - leave that box empty)."); folderNote.Foreground = h == null ? B("#F0BE5A") : B("#6ED28C"); }
            };
            folderBox.LostFocus += (s, e) => checkFolder();
            browse.Click += (s, e) => { using (var d = new WF.FolderBrowserDialog { Description = "Pick the folder your vault syncs through (it holds vault.hkvault)" }) { var od = Environment.GetEnvironmentVariable("OneDrive"); if (!string.IsNullOrEmpty(od) && Directory.Exists(od)) d.SelectedPath = od; if (d.ShowDialog() == WF.DialogResult.OK) { folderBox.Text = d.SelectedPath; checkFolder(); } } };
            var btns = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var open = new Button { Content = "Open vault", Style = St("Accent"), IsDefault = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            var back = new Button { Content = "Back", Style = St("Btn") };
            btns.Children.Add(open); btns.Children.Add(back); sp.Children.Add(btns);
            var rec = new TextBlock { Margin = new Thickness(0, 12, 0, 0), Foreground = Muted, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, Text = "No Secret Key? Use your recovery code" };
            sp.Children.Add(rec);
            lockStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = B("#F0BE5A") }; sp.Children.Add(lockStatus);
            back.Click += (s, e) => ShowLock();
            open.Click += (s, e) => OpenSynced(folderBox.Text.Trim(), pb, skBox.Item1, false);
            rec.MouseLeftButtonUp += (s, e) => OpenSynced(folderBox.Text.Trim(), pb, skBox.Item1, true);
            checkFolder();
            root.Child = new Grid { Children = { card } };
        }
        string addFolder;
        // a Secret Key text box with a live check under it (typos caught by the checksum before anything is tried)
        Tuple<TextBox, TextBlock> SecretKeyBox()
        {
            var tb = new TextBox { Style = St("Field"), FontFamily = new FontFamily("Consolas"), ToolTip = "HK- and 30 letters and digits (no 0, O, 1 or I). Case, spaces and dashes don't matter." };
            var note = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Muted, Text = "Starts with HK- - it's on your Emergency Kit. Paste it or type it; typos are caught." };
            tb.TextChanged += (s, e) =>
            {
                var t = tb.Text.Trim(); if (t.Length == 0) { note.Text = "Starts with HK- - it's on your Emergency Kit. Paste it or type it; typos are caught."; note.Foreground = Muted; return; }
                string err; var k = SecretKey.Parse(t, out err);
                if (k != null) { Gcm.Wipe(k); note.Text = "That's a valid Secret Key."; note.Foreground = B("#6ED28C"); }
                else { var len = new string(t.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()).Length; note.Text = len < 30 ? "Keep going (" + Math.Max(0, (t.ToUpperInvariant().Replace("-", "").Replace(" ", "").StartsWith("HK") ? len - 2 : len)) + " of 30 characters)." : err; note.Foreground = len < 30 ? Muted : B("#F0BE5A"); }
            };
            return Tuple.Create(tb, note);
        }
        void OpenSynced(string folder, PasswordBox pb, TextBox skBox, bool withRecovery)
        {
            var main = SyncFolder.MainPath(folder);
            if (folder.Length == 0 || !File.Exists(main)) { lockStatus.Text = "Pick the folder that holds vault.hkvault first."; return; }
            if (IsInside(folder, KeysSettings.Dir)) { lockStatus.Text = "Pick the sync folder, not Hallow Keys' own data folder."; return; }
            string code = null; byte[] sk = null, pw = null;
            if (withRecovery)
            {
                code = Ask.Text(this, "Use your recovery code", "Type the recovery code from your Emergency Kit or recovery sheet (dashes don't matter). It opens the vault without the Secret Key; you'll pick a new master password next.", "Recovery code");
                if (string.IsNullOrWhiteSpace(code)) return;
            }
            else
            {
                if (pb.SecurePassword.Length == 0) { lockStatus.Text = "Type your master password."; return; }
                if (skBox.Text.Trim().Length > 0) { string err; sk = SecretKey.Parse(skBox.Text, out err); if (sk == null) { lockStatus.Text = err; return; } }
                pw = Bytes(pb); pb.Clear();
            }
            Busy(withRecovery ? "Checking the recovery code..." : "Opening your synced vault...");
            IsEnabled = false;
            new Thread(() =>
            {
                Vault v = null; string err = null;
                try
                {
                    v = withRecovery ? Vault.OpenWithRecovery(main, code) : Vault.Open(main, pw, sk);
                    v.Path = KeysSettings.VaultPath;
                    if (sk != null) SecretKey.Store(v.VaultId, sk); else { var k = v.CurrentSecretKey(); Gcm.Wipe(k); }
                    if (File.Exists(KeysSettings.VaultPath)) File.Copy(KeysSettings.VaultPath, Path.Combine(KeysSettings.Dir, "vault-before-joining-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".hkvault"), true);
                    v.Save();
                }
                catch (Exception e) { err = e.Message; if (v != null) { v.Dispose(); v = null; } }
                finally { Gcm.Wipe(pw); Gcm.Wipe(sk); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true;
                    if (v == null) { lockStatus.Text = err; lockStatus.Foreground = B("#F0BE5A"); return; }
                    S.SyncFolder = folder; S.SyncOn = true; S.Save(); syncState = "off";
                    Unlocked(v);
                    if (withRecovery) ChangeMasterPassword(true);
                    else Toast("This PC now uses your synced vault - changes go both ways.");
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- unlocking: the Secret Key typed when this PC doesn't have it; a password changed on another PC opens via the sync folder
        Vault OpenForUnlock(byte[] pw, byte[] sk)
        {
            try { return Vault.Open(KeysSettings.VaultPath, pw, sk); }
            catch (VaultException e)
            {
                var main = S.SyncOn && S.SyncFolder.Length > 0 ? SyncFolder.MainPath(S.SyncFolder) : null;
                if (main == null || !File.Exists(main) || !(e.Message.StartsWith("Wrong master password") || e is SecretKeyNeededException)) throw;
                Vault r = null;
                try { r = Vault.Open(main, pw, sk); } catch { }
                if (r == null) throw;
                using (r)
                {
                    var lh = Vault.PeekHeader(Vault.ReadFile(KeysSettings.VaultPath));
                    if (lh == null || !lh.VaultId.SequenceEqual(r.VaultId)) throw;
                    var key = r.DataKeyCopy();
                    try { var l = Vault.OpenWithDataKey(KeysSettings.VaultPath, key); SyncMerge.Merge(l, r, Device.Id, Device.Name); if (sk != null) SecretKey.Store(l.VaultId, sk); l.Save(); return l; }
                    finally { Gcm.Wipe(key); }
                }
            }
        }
        // does this PC need to be told the Secret Key before unlocking?
        bool LockNeedsSecretKey()
        {
            if (Snap.On) return snapNeedSk;
            try
            {
                if (!File.Exists(KeysSettings.VaultPath)) return false;
                var h = Vault.PeekHeader(Vault.ReadFile(KeysSettings.VaultPath));
                return h != null && h.UsesSecretKey && !SecretKey.Has(h.VaultId, h.SecretKeyId);
            }
            catch { return false; }
        }
        bool snapNeedSk;

        // ---- developer snapshots (made-up data only)
        void SnapExtra20(Action<MainWindow, string> save)
        {
            Action<MainWindow, string> tall = (w, n) => Snap.Save(w, Path.Combine(snapDir, n), 1060, 2300);
            var fakeSk = SecretKey.Format(new byte[] { 1, 35, 69, 103, 137, 171, 205, 239, 254, 220, 186, 152, 118, 84, 50, 16 });   // a made-up key for the pictures
            S.SyncOn = true; S.SyncFolder = @"C:\Users\you\OneDrive\Hallow Keys"; syncState = "synced"; lastSyncOk = DateTime.Now.AddMinutes(-2);
            var real = vault; vault = Vault.Pretend(real.Data, true);
            editing = null; view = "settings"; ShowMain(); tall(this, "settings-sync.png");
            view = "all"; selected = vault.Data.Items[0]; ShowMain(); save(this, "main-synced.png");
            lastConflicts = 1; lastConflictTime = DateTime.Now; ShowMain(); save(this, "main-conflict.png"); lastConflicts = 0;
            syncState = "waiting"; syncMsg = "Waiting for OneDrive to finish copying the vault"; ShowMain(); save(this, "main-waiting.png");
            vault = Vault.Pretend(real.Data, false); S.SyncOn = false; syncState = "off"; view = "settings"; ShowMain(); tall(this, "settings-upgrade.png");
            view = "all"; ShowMain(); save(this, "main-upgrade-offer.png");
            vault = real;
            ShowEmergencyKit("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789", fakeSk, "new");
            ShowEmergencyKit(null, fakeSk, "upgrade");
            var w4 = new MainWindow(false, true); w4.addFolder = @"C:\Users\you\OneDrive\Hallow Keys"; w4.ShowAddDevice(); save(w4, "add-device.png");
            var w5 = new MainWindow(false, true); w5.snapVaultExists = true; w5.snapNeedSk = true; w5.ShowLock(); save(w5, "lock-secret-key.png");
            var w6 = new MainWindow(false, true); w6.snapVaultExists = false; w6.ShowLock(); save(w6, "create-20.png");
            SnapExtra21();   // v2.1
        }
    }
}
