// Hallow Keys 2.14 - "Don't lose access":
//  - the safety checklist (a card on the Vault page and its own page): recovery code saved (checked by typing 4 of its characters - or the
//    whole code for a code made before 2.14 - or by marking the printed Emergency Kit), a backup or sync on (with the last backup's date),
//    and the Emergency Kit / Secret Key printed. A quiet tray reminder at most once a day until all three are done (never during a game or
//    a full-screen program), then never again; "Remind me in 7 days" hides the card and the reminder for a week.
//  - automatic encrypted backups: a dated copy of the vault file (still encrypted - it opens with the master password and Secret Key, or
//    the recovery code) into a folder you pick, when it changed and at most once a day; the newest N of this vault are kept. "Test restore"
//    opens a backup read-only in memory and checks every item decrypts.
//  - leaving NordPass: after an import, a comparison of the export file and the vault (items by type, items with 2FA, logins without a
//    password) - counts only are kept - and a reminder to keep NordPass for about a month.
// Nothing here stores the recovery code or a key: the 4-character check is a slow hash of 4 characters (20 of the code's 160 bits) inside the
// encrypted vault, and the settings keep only markers (the recovery code's time, the Secret Key's id).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    // ---- 4 characters of the recovery code (positions picked at random when the code is made)
    [DataContract] public class RecoveryCheck
    {
        [DataMember] public int[] Pos;
        [DataMember] public byte[] Salt, Hash;
        [DataMember] public long RecTime;
        public static RecoveryCheck Make(string code, long recTime)
        {
            var c = Norm(code); if (c.Length < 16) return null;
            var all = Enumerable.Range(0, c.Length).ToList(); var pos = new List<int>();
            while (pos.Count < 4) { var i = Rng.Below(all.Count); pos.Add(all[i]); all.RemoveAt(i); }
            pos.Sort();
            var r = new RecoveryCheck { Pos = pos.ToArray(), Salt = Rng.Bytes(Kdf.SaltSize), RecTime = recTime };
            var b = Encoding.ASCII.GetBytes(new string(r.Pos.Select(p => c[p]).ToArray()));
            try { r.Hash = Kdf.Derive(b, r.Salt, Kdf.MinIterations); } finally { Gcm.Wipe(b); }
            return r;
        }
        public static string Norm(string s) { return new string((s ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()); }
        public bool Matches(string four)
        {
            var c = Norm(four); if (c.Length != 4 || Salt == null || Hash == null) return false;
            var b = Encoding.ASCII.GetBytes(c); byte[] h = null;
            try { h = Kdf.Derive(b, Salt, Kdf.MinIterations); int d = 0; for (int i = 0; i < h.Length && i < Hash.Length; i++) d |= h[i] ^ Hash[i]; return d == 0 && h.Length == Hash.Length; }
            finally { Gcm.Wipe(b); if (h != null) Gcm.Wipe(h); }
        }
        public string Positions { get { return string.Join(", ", Pos.Take(3).Select(p => (p + 1).ToString())) + " and " + (Pos[3] + 1); } }
    }

    // ---- the last import, counts only (no titles, no passwords)
    [DataContract] public class ImportCompare
    {
        [DataMember] public string Source = "", When = "";
        [DataMember] public int[] File = new int[5], InVault = new int[5];    // logins, notes, cards, identities, other
        [DataMember] public int File2fa, Vault2fa, FileNoPw, VaultNoPw, Folders, Skipped, Added;
        public static readonly string[] Kinds = { "Logins", "Secure notes", "Cards", "Identities", "Other items" };
        public static int Kind(Item i) { return i.Type == "login" ? 0 : i.Type == "note" ? 1 : i.Type == "card" ? 2 : i.Type == "identity" ? 3 : 4; }
        public static bool Has2fa(Item i) { return !string.IsNullOrEmpty(i.Totp) || i.Fields.Any(f => f.Kind == "totp" && !string.IsNullOrEmpty(f.Value)); }
        public static bool NoPw(Item i) { return i.Type == "login" && string.IsNullOrEmpty(i.Password); }
        public int FileTotal { get { return File.Sum(); } }
        public int VaultTotal { get { return InVault.Sum(); } }
        public bool AllThere { get { return VaultTotal == FileTotal && Vault2fa == File2fa; } }
        public DateTime KeepUntil { get { DateTime t; return DateTime.TryParse(When, out t) ? t.AddDays(30) : DateTime.Now.AddDays(30); } }
        // compares an import with the vault after the merge: each item of the file is looked for in the vault (same rule as the merge)
        public static ImportCompare Make(Import.Result r, VaultData d, int added, DateTime now, List<string> missing)
        {
            var c = new ImportCompare { Source = r.Source ?? "CSV", When = now.ToString("o"), Folders = r.Folders.Count, Skipped = r.Skipped, Added = added };
            foreach (var it in r.Items)
            {
                int k = Kind(it); c.File[k]++; if (Has2fa(it)) c.File2fa++; if (NoPw(it)) c.FileNoPw++;
                var m = d.Items.FirstOrDefault(x => x.Deleted == null && Import.Same(x, it));
                if (m == null) { if (missing != null) missing.Add(it.Title); continue; }
                c.InVault[k]++; if (Has2fa(m)) c.Vault2fa++; if (NoPw(m)) c.VaultNoPw++;
            }
            return c;
        }
    }

    public static class Safety
    {
        public class State
        {
            public bool Recovery, Backup, Kit, HasVault, HasSecretKey, Snoozed, Done;
            public string RecoveryHow = "", BackupText = "", BackupDetail = "";
            public DateTime? LastBackup;
            public int Left { get { return (Recovery ? 0 : 1) + (Backup ? 0 : 1) + (Kit ? 0 : 1); } }
        }
        public static string KitMarker(long recTime, byte[] skId) { return recTime + ":" + (skId == null ? "none" : BitConverter.ToString(skId).Replace("-", "").ToLowerInvariant()); }
        // the header of the vault file (no key needed): which recovery code and Secret Key are current
        public static bool Header(out long recTime, out byte[] skId, out bool hasRecovery)
        {
            recTime = 0; skId = null; hasRecovery = false;
            try { if (!File.Exists(KeysSettings.VaultPath)) return false; var v = Vault.PeekHeader(Vault.ReadFile(KeysSettings.VaultPath)); if (v == null) return false; recTime = v.RecoveryTime; skId = v.SecretKeyId; hasRecovery = v.HasRecovery; return true; }
            catch { return false; }
        }
        public static State Get(KeysSettings s, Vault open, bool syncOk, DateTime now)
        {
            var st = new State(); long rt; byte[] sk; bool hr;
            if (open != null) { rt = open.RecoveryTime; sk = open.SecretKeyId; hr = open.HasRecovery; st.HasVault = true; }
            else st.HasVault = Header(out rt, out sk, out hr);
            st.HasSecretKey = sk != null;
            st.Recovery = hr && s.SafetyRecovery == rt.ToString();
            st.RecoveryHow = st.Recovery ? s.SafetyRecoveryHow : "";
            st.Kit = s.SafetyKit == KitMarker(rt, sk);
            DateTime t;
            if (DateTime.TryParse(s.LastAutoBackup, out t)) st.LastBackup = t;
            bool backupOk = s.AutoBackupOn && s.AutoBackupPicked && st.LastBackup != null && string.IsNullOrEmpty(s.AutoBackupError);
            bool syncOn = s.SyncOn && !string.IsNullOrEmpty(s.SyncFolder) && syncOk;
            st.Backup = backupOk || syncOn;
            var parts = new List<string>();
            if (syncOn) { DateTime ls; parts.Add("Sync is on" + (DateTime.TryParse(s.LastSync, out ls) ? " (last synced " + Ago(ls, now) + ")" : "")); }
            if (s.AutoBackupOn) parts.Add("automatic backups " + (s.AutoBackupPicked ? "to " + AutoBackup.Folder(s) : "only into Hallow Keys' own folder on this PC") + (st.LastBackup != null ? " - last backup " + Ago(st.LastBackup.Value, now) : " - none yet"));
            else parts.Add("automatic backups are off");
            st.BackupText = parts.Count == 0 ? "" : char.ToUpper(parts[0][0]) + string.Join("; ", parts).Substring(1);
            if (!string.IsNullOrEmpty(s.AutoBackupError)) st.BackupDetail = "The last backup failed: " + s.AutoBackupError;
            else if (!st.Backup && s.AutoBackupOn && !s.AutoBackupPicked) st.BackupDetail = "A copy on the same drive doesn't survive a dead drive or a stolen PC - pick a folder on another drive, a USB drive or your NAS.";
            st.Done = st.Left == 0;
            DateTime until; st.Snoozed = DateTime.TryParse(s.SafetySnoozeUntil, out until) && until > now;
            return st;
        }
        public static string Ago(DateTime t, DateTime now)
        {
            var d = now - t; if (d.TotalMinutes < 1) return "just now"; if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
            if (d.TotalDays < 1) return (int)d.TotalHours + " h ago"; if (t.Date == now.Date.AddDays(-1)) return "yesterday"; if (d.TotalDays < 30) return (int)Math.Round(d.TotalDays) + " days ago";
            return "on " + t.ToString("d MMM yyyy");
        }
        // the once-a-day reminder: null = show it now, otherwise why not
        public static string Hold(KeysSettings s, State st, DateTime now, bool game, bool fullscreen)
        {
            if (!st.HasVault) return "no vault";
            if (!string.IsNullOrEmpty(s.SafetyDoneAt)) return "done before";
            if (st.Done) return "done";
            if (st.Snoozed) return "snoozed";
            if (s.SafetyLastReminder == now.ToString("yyyy-MM-dd")) return "already today";
            if (game) return "game";
            if (fullscreen) return "full screen";
            return null;
        }
        public static string ReminderText(State st)
        {
            var left = new List<string>();
            if (!st.Recovery) left.Add("confirm your recovery code is saved");
            if (!st.Backup) left.Add("turn on a backup to another drive or sync");
            if (!st.Kit) left.Add(st.HasSecretKey ? "print your Emergency Kit" : "print your recovery sheet");
            return st.Left + " of 3 safety steps left: " + string.Join(", ", left) + ". Open Hallow Keys > Don't lose access.";
        }
    }

    // ---- automatic encrypted backups: dated copies of the vault file
    public static class AutoBackup
    {
        public const string Prefix = "Hallow Keys backup ";
        public static string DefaultFolder { get { return Path.Combine(KeysSettings.Dir, "backups"); } }
        public static string Folder(KeysSettings s) { return string.IsNullOrEmpty(s.AutoBackupFolder) ? DefaultFolder : s.AutoBackupFolder; }
        public class Entry { public string Path; public DateTime When; public long Bytes; }
        static string Tag(byte[] id) { return BitConverter.ToString(id, 0, 4).Replace("-", "").ToLowerInvariant(); }
        // this vault's backups in the folder, newest first (another vault's backups there are never touched)
        public static List<Entry> List(string folder, byte[] vaultId)
        {
            var o = new List<Entry>();
            try
            {
                if (!Directory.Exists(folder)) return o;
                var tag = vaultId == null ? null : Tag(vaultId);
                foreach (var f in Directory.GetFiles(folder, Prefix + "*.hkbackup"))
                {
                    var n = System.IO.Path.GetFileNameWithoutExtension(f).Substring(Prefix.Length).Split(' ');
                    if (n.Length != 3 || (tag != null && n[2] != tag)) continue;
                    DateTime t; if (!DateTime.TryParseExact(n[0] + " " + n[1], "yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)) continue;
                    o.Add(new Entry { Path = f, When = t, Bytes = new FileInfo(f).Length });
                }
            }
            catch { }
            return o.OrderByDescending(e => e.When).ToList();
        }
        // Windows Security's ransomware protection (Controlled folder access) guards these by default: writes from apps it doesn't know are blocked
        public static bool Guarded(string folder)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\";
                var guarded = new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.Favorites }
                    .Select(Environment.GetFolderPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var n in new[] { "Documents", "Desktop", "Pictures", "Videos", "Music" }) guarded.Add(System.IO.Path.Combine(home, n));
                try { foreach (var od in Directory.GetDirectories(home, "OneDrive*")) foreach (var n in new[] { "Documents", "Desktop", "Pictures" }) guarded.Add(System.IO.Path.Combine(od, n)); } catch { }
                return guarded.Any(g => full.StartsWith(g.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
        // a folder to suggest: another fixed drive if this PC has one, else a folder in your user folder (not Documents)
        public static string Suggest()
        {
            if (KeysSettings.TestDir != null) return Path.Combine(KeysSettings.TestDir, "Backups");
            try
            {
                var sys = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
                var other = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed && !string.Equals(d.Name, sys, StringComparison.OrdinalIgnoreCase) && d.AvailableFreeSpace > 200L * 1024 * 1024);
                if (other != null) return System.IO.Path.Combine(other.Name, "Hallow Keys backups");
            }
            catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Hallow Keys backups");
        }
        public class Result { public bool Wrote; public string Path, Error, Skipped; }
        // writes a dated copy when the vault changed since the last one and (unless forced) the last one is a day old. Never while it's written.
        public static Result Run(KeysSettings s, DateTime now, bool force)
        {
            var r = new Result();
            try
            {
                if (!File.Exists(KeysSettings.VaultPath)) { r.Skipped = "no vault"; return r; }
                var bytes = Vault.ReadFile(KeysSettings.VaultPath);
                var head = Vault.PeekHeader(bytes); if (head == null) { r.Error = "the vault file couldn't be read"; return r; }
                string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                DateTime last; bool hasLast = DateTime.TryParse(s.LastAutoBackup, out last);
                var folder = Folder(s);
                var mine = List(folder, head.VaultId);
                if (!force)
                {
                    if (hash == s.LastAutoBackupHash && mine.Count > 0) { r.Skipped = "no changes since the last backup"; return r; }
                    if (hasLast && (now - last).TotalHours < 23 && mine.Count > 0) { r.Skipped = "one a day"; return r; }
                }
                Directory.CreateDirectory(folder);
                var name = Prefix + now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture) + " " + Tag(head.VaultId) + ".hkbackup";
                var dest = System.IO.Path.Combine(folder, name); var tmp = dest + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                var back = File.ReadAllBytes(tmp);   // read it back: a NAS or USB drive that drops writes is caught here
                if (back.Length != bytes.Length || !back.SequenceEqual(bytes)) { try { File.Delete(tmp); } catch { } r.Error = "the copy in the backup folder didn't match - check the drive"; return r; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                r.Wrote = true; r.Path = dest;
                s.LastAutoBackup = now.ToString("o"); s.LastAutoBackupHash = hash; s.AutoBackupError = "";
                foreach (var old in List(folder, head.VaultId).Skip(Math.Max(1, s.AutoBackupKeep))) try { File.Delete(old.Path); } catch { }   // only this vault's own dated backups
            }
            catch (UnauthorizedAccessException) { r.Error = Guarded(Folder(s)) ? "Windows Security's ransomware protection blocks writing into " + Folder(s) + " - pick a folder outside Documents, Desktop and Pictures" : "no permission to write into " + Folder(s); }
            catch (IOException e) { r.Error = e.Message.TrimEnd('.'); }
            catch (Exception e) { r.Error = e.Message.TrimEnd('.'); }
            if (r.Error != null) s.AutoBackupError = r.Error;
            return r;
        }
        // "Test restore": opens the backup read-only in memory with this vault's key and checks it decrypts (nothing is restored or written)
        public static string Verify(Vault open, string path, out bool ok)
        {
            ok = false;
            try
            {
                var bytes = Vault.ReadFile(path); var head = Vault.PeekHeader(bytes);
                if (head == null) return "Not a Hallow Keys vault file.";
                if (!head.VaultId.SequenceEqual(open.VaultId)) return "It's a backup of a different vault.";
                var k = open.DataKeyCopy(); VaultData d;
                try { using (var v = Vault.OpenBytes(path, bytes, null, false, k)) { d = v.Data; v.Data = new VaultData(); } }
                finally { Gcm.Wipe(k); }
                int items = d.Items.Count(i => i.Deleted == null), trash = d.Trash.Count + d.Items.Count(i => i.Deleted != null);
                ok = true;
                return "Opens and decrypts: " + items + " item" + (items == 1 ? "" : "s") + (trash > 0 ? " (+" + trash + " in the trash)" : "") + ", " + (bytes.Length / 1024) + " KB. It opens with your master password and Secret Key, or your recovery code.";
            }
            catch (Exception e) { return "It doesn't open: " + e.Message; }
        }
    }

    public partial class MainWindow
    {
        DispatcherTimer safetyTimer; bool backupBusy; DateTime startedAt = DateTime.Now; int fourTries;

        void Init214()   // from ExtraInit (TestHooks.cs)
        {
            if (Snap.On) return;
            safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(KeysSettings.TestDir != null ? 1 : 10) };
            safetyTimer.Tick += (s, e) => { BackupSoon(false); SafetyReminder(false); };
            safetyTimer.Start();
            var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(KeysSettings.TestDir != null ? 3 : 45) };
            first.Tick += (s, e) => { first.Stop(); BackupSoon(false); };
            first.Start();
        }
        Safety.State SafetyNow() { return Safety.Get(S, vault, syncState != "error", DateTime.Now); }
        void SafetyChanged()
        {
            var st = SafetyNow();
            if (st.Done && string.IsNullOrEmpty(S.SafetyDoneAt)) { S.SafetyDoneAt = DateTime.Now.ToString("o"); TestLog("safety: all done"); }
            S.Save(); WriteStatus();
        }
        // the quiet reminder (tray balloon, or Hallow Hub's notification): at most once a day, held while a game or a full-screen program runs
        string SafetyReminder(bool now)
        {
            if (!now && (DateTime.Now - startedAt).TotalMinutes < 3) return "starting";
            var st = SafetyNow();
            var appData = KeysSettings.TestDir ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            bool game = DesktopFill.ReadGameState(appData, DateTime.Now).Running, full = false;
            try { if (fgApi != null) { var fg = fgApi.Get(); full = fg != null && fg.Ok && fg.Fullscreen; } } catch { }
            var hold = Safety.Hold(S, st, DateTime.Now, game, full);
            if (hold != null) { if (hold == "game" || hold == "full screen") TestLog("safety reminder held: " + hold); return "held " + hold; }
            S.SafetyLastReminder = DateTime.Now.ToString("yyyy-MM-dd"); S.Save();
            TestLog("safety reminder: " + st.Left + " left");
            Notify("Hallow Keys - don't lose access", Safety.ReminderText(st));
            return "shown";
        }
        // automatic backup in the background (the vault file is already encrypted - it works while locked too)
        void BackupSoon(bool force) { BackupSoon(force, null); }
        void BackupSoon(bool force, Action<AutoBackup.Result> done)
        {
            if (Snap.On || backupBusy || (!S.AutoBackupOn && !force)) { if (done != null) done(null); return; }
            backupBusy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                AutoBackup.Result r = null;
                try { lock (S) r = AutoBackup.Run(S, DateTime.Now, force); } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    backupBusy = false;
                    if (r != null && (r.Wrote || r.Error != null)) { TestLog(r.Wrote ? "backup: " + Path.GetFileName(r.Path) : "backup failed: " + r.Error); SafetyChanged(); if (r.Error != null && force) Toast("Backup failed: " + r.Error, true); }
                    if (done != null) done(r);
                }));
            });
        }

        // ---- the card on the Vault page (until all three are done, or hidden for 7 days)
        void SafetyBanner(DockPanel main)
        {
            if (vault == null) return;
            var st = SafetyNow();
            if (st.Done || st.Snoozed || !string.IsNullOrEmpty(lastSearch)) return;
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12), BorderBrush = B("#F0BE5A"), BorderThickness = new Thickness(1) };
            var d = new DockPanel(); card.Child = d;
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var open = new Button { Content = "Finish setting up", Style = St("Accent"), Margin = new Thickness(8, 0, 0, 0) }; open.Click += (s, e) => { view = "safety"; selected = null; editing = null; ShowMain(); };
            var later = new Button { Content = "Remind me in 7 days", Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) }; later.Click += (s, e) => { S.SafetySnoozeUntil = DateTime.Now.AddDays(7).ToString("o"); S.Save(); ShowMain(); };
            btns.Children.Add(open); btns.Children.Add(later);
            DockPanel.SetDock(btns, Dock.Right); d.Children.Add(btns);
            var tx = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("shield-halved", 15, B("#F0BE5A")); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; h.Children.Add(ic);
            h.Children.Add(new TextBlock { Text = "Don't lose access: " + (3 - st.Left) + " of 3 done", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            tx.Children.Add(h);
            var line = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            Action<bool, string> step = (ok, text) =>
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                var i2 = Icons.Make(ok ? "check" : "xmark", 12, ok ? B("#6ED28C") : Muted); i2.Margin = new Thickness(0, 0, 5, 0); i2.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(i2);
                sp.Children.Add(new TextBlock { Text = text, Foreground = ok ? Muted : (Brush)FindResource("Text") }); line.Children.Add(sp);
            };
            step(st.Recovery, "Recovery code saved"); step(st.Backup, "Backup or sync on"); step(st.Kit, st.HasSecretKey ? "Emergency Kit printed" : "Recovery sheet printed");
            tx.Children.Add(line); d.Children.Add(tx);
            DockPanel.SetDock(card, Dock.Top); main.Children.Add(card);
        }

        // ---- the page: the checklist, automatic backups, leaving NordPass
        UIElement SafetyPage()
        {
            ScrollViewer sv; var sp = Page("Don't lose access", "Three things that make sure you can always get back into your vault - even with a dead drive, a forgotten master password or a new PC.", out sv);
            var st = SafetyNow();
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = st.Done ? "All done - you can always get back in." : (3 - st.Left) + " of 3 done", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = st.Done ? B("#6ED28C") : (Brush)FindResource("Text") });
            if (!st.Done) head.Children.Add(new TextBlock { Text = st.Snoozed ? "Reminders are paused until " + DateTime.Parse(S.SafetySnoozeUntil).ToString("d MMM") + "." : "Until these are done, Hallow Keys reminds you quietly at most once a day (never during a game).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            sp.Children.Add(Card(head));
            // 1. the recovery code
            var c1 = Step(sp, st.Recovery, "1. Recovery code saved", st.Recovery ? (st.RecoveryHow == "kit" ? "You marked your printed Emergency Kit (with the recovery code) as kept safe." : "You typed it correctly - it's the code that opens this vault.")
                : "If you forget your master password, the recovery code from your Emergency Kit is the only way back in. Check that you have it: type a few of its characters.");
            var r1 = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (!st.Recovery)
            {
                var chk = new Button { Content = "Check my recovery code", Style = St("Accent"), Margin = new Thickness(0, 0, 8, 6) }; chk.Click += (s, e) => RecoveryCheckDialog(); r1.Children.Add(chk);
                var kitb = new Button { Content = "It's on my printed Emergency Kit", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) };
                kitb.Click += (s, e) => { if (MessageBox.Show(this, "Is your recovery code printed (or written down) and kept somewhere safe, away from this PC?\n\nWithout it, a forgotten master password means the vault is lost for good.", "Recovery code", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; MarkRecovery("kit"); ShowMain(); };
                r1.Children.Add(kitb);
            }
            var lost = new Button { Content = "I don't have it - make a new one", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6), ToolTip = "Makes a new recovery code (the old one stops working) and shows it to print" };
            lost.Click += (s, e) => NewRecoverySheet(); r1.Children.Add(lost);
            c1.Children.Add(r1);
            // 2. a backup or sync
            var c2 = Step(sp, st.Backup, "2. Backup or sync on", st.BackupText + ".");
            if (st.BackupDetail.Length > 0) c2.Children.Add(new TextBlock { Text = st.BackupDetail, Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            BackupControls(c2, true);
            // 3. the Emergency Kit
            var c3 = Step(sp, st.Kit, st.HasSecretKey ? "3. Emergency Kit printed (Secret Key)" : "3. Recovery sheet printed",
                st.Kit ? "Printed or written down. Keep it somewhere safe and offline - not on this PC." : st.HasSecretKey ? "The Emergency Kit has your Secret Key - you need it (with your master password) to open the vault on a new PC or from a backup. Print it and keep it with the recovery code." : "Print your recovery sheet and keep it somewhere safe and offline.");
            var r3 = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (st.HasSecretKey) { var pk = new Button { Content = "Show and print the Emergency Kit", Style = St(st.Kit ? "Btn" : "Accent"), Margin = new Thickness(0, 0, 8, 6) }; pk.Click += (s, e) => ShowKitAfterPassword(); r3.Children.Add(pk); }
            if (!st.Kit)
            {
                var mk = new Button { Content = "I've printed it / written it down", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) };
                mk.Click += (s, e) => { if (MessageBox.Show(this, (st.HasSecretKey ? "Is your Emergency Kit (Secret Key and recovery code)" : "Is your recovery sheet") + " printed or written down and kept somewhere safe, away from this PC?", "Emergency Kit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; MarkKit(); ShowMain(); };
                r3.Children.Add(mk);
            }
            c3.Children.Add(r3);
            if (!st.Done && !st.Snoozed)
            {
                var later = new Button { Content = "Remind me in 7 days", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 10) };
                later.Click += (s, e) => { S.SafetySnoozeUntil = DateTime.Now.AddDays(7).ToString("o"); S.Save(); ShowMain(); }; sp.Children.Add(later);
            }
            CompareSection(sp);
            return sv;
        }
        StackPanel Step(StackPanel page, bool ok, string title, string text)
        {
            var s = new StackPanel(); var h = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make(ok ? "check" : "triangle-exclamation", 16, ok ? B("#6ED28C") : B("#F0BE5A")); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; h.Children.Add(ic);
            h.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); s.Children.Add(h);
            var body = new StackPanel { Margin = new Thickness(26, 4, 0, 0) }; s.Children.Add(body);
            body.Children.Add(new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            page.Children.Add(Card(s));
            return body;
        }

        void MarkRecovery(string how)
        {
            if (vault == null) return;
            S.SafetyRecovery = vault.RecoveryTime.ToString(); S.SafetyRecoveryHow = how; TestLog("safety: recovery code confirmed (" + how + ")"); SafetyChanged();
        }
        void MarkKit()
        {
            if (vault == null) return;
            S.SafetyKit = Safety.KitMarker(vault.RecoveryTime, vault.SecretKeyId); TestLog("safety: kit printed"); SafetyChanged();
        }
        void NewRecoverySheet()
        {
            if (vault == null) return;
            if (MessageBox.Show(this, "Make a new recovery code? The old one stops working.", "Hallow Keys", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            IsEnabled = false; string code = null; vaultBusy = true;
            new Thread(() => { code = vault.NewRecoveryCode(); Dispatcher.BeginInvoke(new Action(() => { IsEnabled = true; vaultBusy = false; SaveVault(null); SafetyChanged(); ShowRecoverySheet(code, false); ShowMain(); })); }) { IsBackground = true }.Start();
        }
        // "type characters 3, 11, 19 and 27" (a code made by 2.14 or newer) - or the whole code (an older code: nothing of it is stored)
        string CheckRecoveryTyped(string typed, bool four)
        {
            if (vault == null) return "error locked";
            var rc = vault.Data.RecoveryCheck;
            if (four)
            {
                if (rc == null || rc.RecTime != vault.RecoveryTime) return "error no four-character check for this code - type the whole code";
                if (fourTries >= 5) return "error too many tries - type the whole code";
                if (!rc.Matches(typed)) { fourTries++; return "error those characters don't match"; }
            }
            else if (!vault.IsRecoveryCode(typed)) return "error that isn't this vault's recovery code";
            fourTries = 0; MarkRecovery("check"); return "ok";
        }
        void RecoveryCheckDialog()
        {
            if (vault == null) return;
            var rc = vault.Data.RecoveryCheck; bool four = rc != null && rc.RecTime == vault.RecoveryTime && fourTries < 5;
            var w = new Window { Title = "Hallow Keys - check your recovery code", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = new FontFamily("Segoe UI") }; Look.Track(w);
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            sp.Children.Add(new TextBlock { Text = "Find your recovery code", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = four ? "On your printed Emergency Kit (or wherever you wrote it down). Type characters " + rc.Positions + " of it - count letters and digits only, not the dashes." : "This code was made before Hallow Keys 2.14, so type the whole code (dashes don't matter). It's checked against your vault and never stored.", TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 0, 0, 10) });
            var boxes = new List<TextBox>(); PasswordBox whole = null;
            if (four)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var p in rc.Pos)
                {
                    var col = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                    col.Children.Add(new TextBlock { Text = "#" + (p + 1), Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center });
                    var tb = new TextBox { Style = St("Field"), Width = 46, MaxLength = 1, FontFamily = new FontFamily("Consolas"), FontSize = 18, TextAlignment = TextAlignment.Center, CharacterCasing = CharacterCasing.Upper };
                    var me = boxes.Count; tb.TextChanged += (s, e) => { if (tb.Text.Length == 1 && me + 1 < boxes.Count) boxes[me + 1].Focus(); };
                    boxes.Add(tb); col.Children.Add(tb); row.Children.Add(col);
                }
                sp.Children.Add(row);
            }
            else { whole = new PasswordBox { Style = St("Pwd"), FontFamily = new FontFamily("Consolas") }; sp.Children.Add(whole); }
            var msg = new TextBlock { Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }; sp.Children.Add(msg);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Check", Style = St("Accent"), IsDefault = true, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Style = St("Btn"), IsCancel = true, MinWidth = 90 };
            ok.Click += (s, e) =>
            {
                var typed = four ? string.Concat(boxes.Select(b => b.Text)) : whole.Password;
                ok.IsEnabled = false; msg.Foreground = Muted; msg.Text = "Checking...";
                new Thread(() =>
                {
                    string r; bool f = four;
                    if (!f) { r = vault != null && vault.IsRecoveryCode(typed) ? "ok" : "error that isn't this vault's recovery code"; }
                    else r = null;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (r == null) r = CheckRecoveryTyped(typed, true); else if (r == "ok") { fourTries = 0; MarkRecovery("check"); }
                        if (r == "ok") { w.Close(); Toast("Recovery code confirmed - keep it safe."); ShowMain(); return; }
                        ok.IsEnabled = true; msg.Foreground = B("#F0BE5A"); msg.Text = r.Substring(6) + (r.Contains("too many") ? "." : ". Look again at your Emergency Kit - or make a new recovery code if you can't find it.");
                        if (r.Contains("too many")) { w.Close(); RecoveryCheckDialog(); }
                    }));
                }) { IsBackground = true }.Start();
            };
            cancel.Click += (s, e) => w.Close();
            btns.Children.Add(ok); btns.Children.Add(cancel); sp.Children.Add(btns);
            w.Loaded += (s, e) => { if (boxes.Count > 0) boxes[0].Focus(); else whole.Focus(); };
            if (Snap.On) { Snap.Save(w, Path.Combine(snapDir, four ? "v214-recovery-check-4.png" : "v214-recovery-check.png"), 470, 0); return; }
            w.ShowDialog();
        }

        // ---- automatic backups (also on Import and export)
        void BackupControls(StackPanel host, bool compact)
        {
            var abOn = Switch("Automatic encrypted backups (a dated copy when the vault changed, at most once a day)", S.AutoBackupOn, v => { S.AutoBackupOn = v; SafetyChanged(); if (v) BackupSoon(true); ShowMain(); });
            abOn.Margin = new Thickness(0, 8, 0, 2); host.Children.Add(abOn);
            var folder = AutoBackup.Folder(S);
            var mine = vault == null ? new List<AutoBackup.Entry>() : AutoBackup.List(folder, vault.VaultId);
            host.Children.Add(new TextBlock { Text = "Folder: " + folder + (S.AutoBackupPicked ? "" : " (Hallow Keys' own folder - same drive as the vault)") + "\nKeeps the newest " + S.AutoBackupKeep + " - " + mine.Count + " there now" + (mine.Count > 0 ? ", newest " + Safety.Ago(mine[0].When, DateTime.Now) : "") + ".", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
            if (AutoBackup.Guarded(folder)) host.Children.Add(new TextBlock { Text = "This folder is protected by Windows Security's ransomware protection (Controlled folder access), which can block Hallow Keys from writing there. Pick a folder outside Documents, Desktop and Pictures.", Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var row = new WrapPanel();
            var sug = AutoBackup.Suggest();
            if (!S.AutoBackupPicked || !string.Equals(folder, sug, StringComparison.OrdinalIgnoreCase))
            {
                host.Children.Add(new TextBlock { Text = "Suggested: " + sug + " (" + (Path.GetPathRoot(sug) != Path.GetPathRoot(KeysSettings.Dir) ? "another drive" : "outside Documents - Windows' ransomware protection can block writes there") + ")", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
                var use = new Button { Content = "Use the suggested folder", Style = St(S.AutoBackupPicked ? "Btn" : "Accent"), Margin = new Thickness(0, 0, 8, 6), ToolTip = sug };
                use.Click += (s, e) => SetBackupFolder(sug); row.Children.Add(use);
            }
            var pick = new Button { Content = "Choose a folder...", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) };
            pick.Click += (s, e) =>
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Pick a folder for Hallow Keys' encrypted backups - another drive, a USB drive or your NAS (not Documents)", ShowNewFolderButton = true })
                {
                    if (Directory.Exists(folder)) dlg.SelectedPath = folder;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetBackupFolder(dlg.SelectedPath);
                }
            };
            row.Children.Add(pick);
            var now = new Button { Content = "Back up now", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) };
            now.Click += (s, e) => { now.IsEnabled = false; BackupSoon(true, r => { if (r != null && r.Wrote) Toast("Backup saved: " + Path.GetFileName(r.Path)); ShowMain(); }); };
            row.Children.Add(now);
            var test = new Button { Content = "Test restore", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6), IsEnabled = mine.Count > 0, ToolTip = "Opens the newest backup read-only in memory and checks every item decrypts - nothing is restored or changed" };
            test.Click += (s, e) => { var r = TestRestore(); MessageBox.Show(this, r, "Test restore", MessageBoxButton.OK, r.StartsWith("Opens") ? MessageBoxImage.Information : MessageBoxImage.Warning); ShowMain(); };
            row.Children.Add(test);
            if (Directory.Exists(folder)) { var of = new Button { Content = "Open folder", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 6) }; of.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } }; row.Children.Add(of); }
            host.Children.Add(row);
            var kr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            kr.Children.Add(new TextBlock { Text = "Keep the newest", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var keeps = new[] { 7, 14, 30, 60 }; var kc = new ComboBox { Width = 90, Style = St("Combo"), ItemsSource = keeps.Select(k => k.ToString()).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(keeps, S.AutoBackupKeep)) };
            kc.SelectionChanged += (s, e) => { S.AutoBackupKeep = keeps[kc.SelectedIndex]; S.Save(); };
            kr.Children.Add(kc); host.Children.Add(kr);
            if (S.LastBackupTest.Length > 0) { DateTime t; host.Children.Add(new TextBlock { Text = "Last restore test" + (DateTime.TryParse(S.LastBackupTest, out t) ? " (" + Safety.Ago(t, DateTime.Now) + ")" : "") + ": " + S.LastBackupTestResult, Foreground = S.LastBackupTestResult.StartsWith("Opens") ? B("#6ED28C") : B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }); }
            host.Children.Add(new TextBlock { Text = "Backups are copies of the encrypted vault file: they open with your master password and Secret Key, or your recovery code - in Import and export > Restore backup.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }
        string SetBackupFolder(string f)
        {
            try
            {
                f = Path.GetFullPath(f);
                if (f.StartsWith(Path.GetFullPath(KeysSettings.Dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && !string.Equals(f, AutoBackup.DefaultFolder, StringComparison.OrdinalIgnoreCase)) { Toast("Pick a folder outside Hallow Keys' own folder.", true); return "error inside the data folder"; }
                if (!string.IsNullOrEmpty(S.SyncFolder) && string.Equals(f.TrimEnd('\\'), Path.GetFullPath(S.SyncFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { Toast("Pick a different folder from the sync folder.", true); return "error the sync folder"; }
                Directory.CreateDirectory(f);
            }
            catch (Exception e) { var m = AutoBackup.Guarded(f) ? "Windows Security's ransomware protection may block that folder - pick one outside Documents." : e.Message; Toast("Can't use that folder: " + m, true); return "error " + m; }
            S.AutoBackupFolder = f; S.AutoBackupPicked = true; S.AutoBackupOn = true; S.AutoBackupError = ""; S.LastAutoBackupHash = ""; S.Save();
            BackupSoon(true, r => { if (r != null && r.Wrote) Toast("Backups go to " + f + " - the first one is there."); ShowMain(); });
            if (AutoBackup.Guarded(f)) Toast("Note: Windows' ransomware protection guards that folder and may block the backups.", true);
            return "ok";
        }
        string TestRestore()
        {
            if (vault == null) return "Unlock first.";
            var list = AutoBackup.List(AutoBackup.Folder(S), vault.VaultId);
            if (list.Count == 0) return "No backup to test yet.";
            bool ok; var r = AutoBackup.Verify(vault, list[0].Path, out ok);
            var cur = vault.Data.Items.Count(i => i.Deleted == null);
            if (ok) r += " (The vault has " + cur + " now; the backup is from " + list[0].When.ToString("d MMM HH:mm") + ".)";
            S.LastBackupTest = DateTime.Now.ToString("o"); S.LastBackupTestResult = r; S.Save(); TestLog("restore test: " + (ok ? "ok" : "failed"));
            return r;
        }

        // ---- leaving NordPass: the comparison after an import
        void CompareSection(StackPanel sp)
        {
            var c = S.LastImport; if (c == null) return;
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = "Leaving " + (c.Source == "CSV" ? "your old password manager" : c.Source) + " safely", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            DateTime when; DateTime.TryParse(c.When, out when);
            s.Children.Add(new TextBlock { Text = "Your import of " + when.ToString("d MMM yyyy HH:mm") + ": what was in the export file, and what is in Hallow Keys now (counts only).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            int row = 0;
            Action<string, string, string, bool, bool> line = (a, b, d, bold, bad) =>
            {
                g.RowDefinitions.Add(new RowDefinition());
                var cells = new[] { a, b, d };
                for (int i = 0; i < 3; i++) { var t = new TextBlock { Text = cells[i], Margin = new Thickness(0, 2, 0, 2), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = i == 2 && bad ? B("#F0BE5A") : bold ? Muted : (Brush)FindResource("Text") }; Grid.SetRow(t, row); Grid.SetColumn(t, i); g.Children.Add(t); }
                row++;
            };
            line("", "In the export file", "In Hallow Keys", true, false);
            for (int i = 0; i < 5; i++) if (c.File[i] > 0 || c.InVault[i] > 0) line(ImportCompare.Kinds[i], c.File[i].ToString(), c.InVault[i].ToString(), false, c.InVault[i] < c.File[i]);
            line("Items with 2FA (authenticator) keys", c.File2fa.ToString(), c.Vault2fa.ToString(), false, c.Vault2fa < c.File2fa);
            line("Logins without a password", c.FileNoPw.ToString(), c.VaultNoPw.ToString(), false, false);
            line("Total", c.FileTotal.ToString(), c.VaultTotal.ToString(), true, c.VaultTotal < c.FileTotal);
            s.Children.Add(g);
            var notes = new List<string>();
            if (c.AllThere) notes.Add("Everything in the export file is in Hallow Keys.");
            else notes.Add((c.FileTotal - c.VaultTotal) + " item(s) from the file aren't in Hallow Keys (changed or deleted since?) - import the file again or add them by hand.");
            if (c.Skipped > 0) notes.Add(c.Skipped + " empty note(s) in the file were skipped.");
            if (c.File2fa == 0) notes.Add("The export had no 2FA keys. If NordPass makes 2FA codes for any of your sites, set those up again in Hallow Authenticator (or Keys) before you leave - a CSV export doesn't carry them.");
            if (c.FileNoPw > 0) notes.Add(c.FileNoPw + " login(s) have no password in the export (passkey or social sign-in only?) - check how you sign in there.");
            notes.Add("Passkeys and attached files aren't in a CSV export - move those by hand.");
            foreach (var n in notes) s.Children.Add(new TextBlock { Text = "- " + n, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var keep = c.KeepUntil;
            s.Children.Add(new TextBlock { Text = DateTime.Now < keep ? "Keep your NordPass account until about " + keep.ToString("d MMMM yyyy") + " (a month): sign in to your important sites with Hallow Keys first, then delete it." : "It's been a month - if every sign-in worked with Hallow Keys, you can delete your NordPass account now.", FontWeight = FontWeights.SemiBold, Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            sp.Children.Add(Card(s));
        }
        // the import itself, without dialogs (DoImport asks first; the developer tests call this directly)
        ImportCompare ImportAndCompare(Import.Result r, List<string> missing)
        {
            var added = Import.Merge(vault.Data, r); SaveVault(added + " items imported.");
            var c = ImportCompare.Make(r, vault.Data, added, DateTime.Now, missing);
            S.LastImport = c; S.Save();
            BackupSoon(true);   // a safety copy right after a big change
            return c;
        }

        // ---- developer tests only (inside HALLOW_KEYS_TESTDIR): never any item data, never the code itself
        string SafetyTestCmd(string cmd, string arg)
        {
            if (KeysSettings.TestDir == null) return "error unknown command";
            switch (cmd)
            {
                case "safetytest-state":
                    {
                        var st = SafetyNow(); var rc = vault == null ? null : vault.Data.RecoveryCheck;
                        return "ok " + Family.Json(new Dictionary<string, object> { { "recovery", st.Recovery }, { "how", st.RecoveryHow }, { "backup", st.Backup }, { "kit", st.Kit }, { "left", st.Left }, { "done", st.Done }, { "doneAt", S.SafetyDoneAt },
                            { "snoozed", st.Snoozed }, { "lastBackup", st.LastBackup == null ? null : Family.Time(st.LastBackup.Value) }, { "folder", AutoBackup.Folder(S) }, { "picked", S.AutoBackupPicked }, { "error", S.AutoBackupError },
                            { "positions", rc == null || vault == null || rc.RecTime != vault.RecoveryTime ? null : rc.Pos.Select(p => (object)p).ToList() }, { "backups", vault == null ? -1 : AutoBackup.List(AutoBackup.Folder(S), vault.VaultId).Count },
                            { "banner", vault != null && !st.Done && !st.Snoozed }, { "lastReminder", S.SafetyLastReminder } });
                    }
                case "safetytest-check4": return CheckRecoveryTyped(arg, true);
                case "safetytest-checkall": return CheckRecoveryTyped(arg, false);
                case "safetytest-kit": if (vault == null) return "error locked"; MarkKit(); return "ok";
                case "safetytest-kitrec": if (vault == null) return "error locked"; MarkRecovery("kit"); return "ok";
                case "safetytest-newcode": if (vault == null) return "error locked"; vault.NewRecoveryCode(); SaveVault(null); SafetyChanged(); return "ok";
                case "safetytest-folder": return SetBackupFolder(arg);
                case "safetytest-backup": { var r = AutoBackup.Run(S, DateTime.Now, arg == "force"); SafetyChanged(); return r.Wrote ? "ok " + Family.Json(new Dictionary<string, object> { { "file", Path.GetFileName(r.Path) } }) : r.Error != null ? "error " + r.Error : "skipped " + r.Skipped; }
                case "safetytest-verify": { var r = TestRestore(); return (r.StartsWith("Opens") ? "ok " : "error ") + r; }
                case "safetytest-remind": return SafetyReminder(true);
                case "safetytest-snooze": S.SafetySnoozeUntil = arg == "off" ? "" : DateTime.Now.AddDays(7).ToString("o"); S.Save(); return "ok";
                case "safetytest-day": S.SafetyLastReminder = ""; S.Save(); return "ok";   // pretend a new day
                case "safetytest-import":
                    {
                        if (vault == null) return "error locked";
                        Import.Result r; try { r = Import.FromFile(arg); } catch (Exception e) { return "error " + e.Message; }
                        var missing = new List<string>(); var c = ImportAndCompare(r, missing);
                        return "ok " + Family.Json(new Dictionary<string, object> { { "file", c.File.Select(x => (object)x).ToList() }, { "vault", c.InVault.Select(x => (object)x).ToList() }, { "file2fa", c.File2fa }, { "vault2fa", c.Vault2fa }, { "fileNoPw", c.FileNoPw }, { "vaultNoPw", c.VaultNoPw }, { "added", c.Added }, { "skipped", c.Skipped }, { "missing", missing.Count }, { "source", c.Source }, { "keepUntil", c.KeepUntil.ToString("yyyy-MM-dd") } });
                    }
                case "safetytest-trash": { if (vault == null) return "error locked"; var it = vault.Data.Items.FirstOrDefault(i => i.Title == arg); if (it == null) return "error no such item"; vault.Data.Items.Remove(it); SaveVault(null); return "ok"; }
            }
            return "error unknown command";
        }
        Dictionary<string, object> SafetyStatus()
        {
            if (Snap.On) return null;
            var st = SafetyNow(); if (!st.HasVault) return null;
            return new Dictionary<string, object> { { "done", 3 - st.Left }, { "of", 3 }, { "lastBackup", st.LastBackup == null ? null : Family.Time(st.LastBackup.Value) } };   // counts and a date only
        }

        // ---- developer snapshots (made-up data only)
        void SnapExtra214(MainWindow w)
        {
            w.S.AutoBackupOn = true; w.S.LastAutoBackup = DateTime.Now.AddHours(-3).ToString("o");
            w.view = "all"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v214-home-card.png"), 1280, 760);
            w.view = "safety"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v214-safety.png"), 1280, 1500);
            w.S.LastImport = new ImportCompare { Source = "NordPass", When = DateTime.Now.AddDays(-2).ToString("o"), File = new[] { 212, 14, 3, 2, 0 }, InVault = new[] { 212, 14, 3, 2, 0 }, File2fa = 0, Vault2fa = 0, FileNoPw = 4, VaultNoPw = 4, Folders = 9, Skipped = 1, Added = 231 };
            w.S.SafetyRecovery = w.vault.RecoveryTime.ToString(); w.S.SafetyRecoveryHow = "check"; w.S.AutoBackupPicked = true; w.S.AutoBackupFolder = @"D:\Hallow Keys backups";
            w.S.LastBackupTest = DateTime.Now.AddMinutes(-5).ToString("o"); w.S.LastBackupTestResult = "Opens and decrypts: 231 items, 84 KB. It opens with your master password and Secret Key, or your recovery code.";
            w.view = "safety"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v214-safety-nordpass.png"), 1280, 1700);
            w.view = "transfer"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v214-transfer.png"), 1280, 1300);
            w.RecoveryCheckDialog();
            w.vault.Data.RecoveryCheck = RecoveryCheck.Make("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", w.vault.RecoveryTime); w.RecoveryCheckDialog();
            w.vault.Data.RecoveryCheck = null; w.view = "all";
        }
    }
}
