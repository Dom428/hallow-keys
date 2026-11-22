// Hallow Keys - Password Health, the generator, import / backup, settings, the recovery sheet and small dialogs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace HallowKeys
{
    public partial class MainWindow
    {
        StackPanel Page(string title, string sub, out ScrollViewer sv)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 18, 26, 18), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold });
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
            sv = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return sp;
        }
        Border Card(UIElement child) { return new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 10), Child = child }; }
        CheckBox Switch(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = St("Switch"), Margin = new Thickness(0, 4, 0, 4) };
            c.Click += (s, e) => set(c.IsChecked == true); return c;
        }

        // ---- Password Health
        UIElement HealthPage(Health.Report r)
        {
            ScrollViewer sv; var sp = Page("Password Health", "Weak: easy to guess (rough estimate). Reused: the same password on more than one login. Old: not changed for over " + S.OldDays + " days." + HealthExtraText(), out sv);
            var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
            Action<string, int, string> tile = (label, n, color) => { var t = new StackPanel(); t.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = n > 0 ? B(color) : B("#6ED28C") }); t.Children.Add(new TextBlock { Text = label, Foreground = Muted }); tiles.Children.Add(new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 0), Child = t }); };
            tile("Weak", r.Weak.Count, "#F0BE5A"); tile("Reused", r.Reused.Count, "#F0BE5A"); tile("Old", r.Old.Count, "#A98BE0"); tile("Leaked", r.Leaked.Count, "#FF6B6B");
            sp.Children.Add(tiles);
            HealthExtra(sp);
            ExpirySection(sp);   // v1.6
            Action<string, List<Item>, Func<Item, string>> list = (head, items, why) =>
            {
                if (items.Count == 0) return;
                sp.Children.Add(new TextBlock { Text = head, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
                foreach (var it in items.Distinct().OrderBy(x => x.Title))
                {
                    var item = it; var b = new Button { Style = St("Nav"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    var d = new DockPanel(); var w = new TextBlock { Text = why(it), Foreground = Muted, FontSize = 11.5 }; DockPanel.SetDock(w, Dock.Right); d.Children.Add(w);
                    d.Children.Add(new TextBlock { Text = it.Title + (it.Username.Length > 0 ? "  -  " + it.Username : ""), TextTrimming = TextTrimming.CharacterEllipsis });
                    b.Content = d; b.Click += (s, e) => { view = "all"; selected = item; ShowMain(); };
                    sp.Children.Add(b);
                }
            };
            list("Leaked (found in known data breaches - change these first)", r.Leaked, i => "change it");
            list("Weak", r.Weak, i => Health.Rating(Health.Bits(i.Password)));
            list("Reused", r.Reused, i => "used " + r.Reused.Count(x => x.Password == i.Password) + " times");
            list("Old", r.Old, i => "set " + i.PasswordChanged.ToString("MMM yyyy"));
            if (r.Weak.Count + r.Reused.Count + r.Old.Count + r.Leaked.Count == 0) sp.Children.Add(new TextBlock { Text = "All good.", Foreground = B("#6ED28C") });
            return sv;
        }
        string HealthExtraText() { string t = ""; HealthExtraTextImpl(ref t); return t; }
        partial void HealthExtraTextImpl(ref string t);
        void HealthExtra(StackPanel sp) { HealthExtraImpl(sp); }
        partial void HealthExtraImpl(StackPanel sp);

        // ---- generator
        UIElement GeneratorPage(Action<string> use)
        {
            ScrollViewer sv; var sp = Page("Password generator", "Made with Windows' cryptographic random numbers. Long and random is what counts.", out sv);
            var output = new TextBox { Style = St("Field"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 15 };
            var bits = new TextBlock { Foreground = Muted, Margin = new Thickness(0, 4, 0, 10) };
            Action make = () =>
            {
                string p; double b;
                if (S.GenWords) { p = Generator.Passphrase(S.GenWordCount, "-", true, true); b = Generator.PassphraseBits(S.GenWordCount, true); }
                else { p = Generator.Password(S.GenLength, S.GenLower, S.GenUpper, S.GenDigits, S.GenSymbols, S.GenAvoid); b = Generator.PasswordBits(S.GenLength, S.GenLower, S.GenUpper, S.GenDigits, S.GenSymbols, S.GenAvoid); }
                output.Text = p; bits.Text = Health.Rating(b) + " - about " + (int)b + " bits of randomness";
            };
            var row = new DockPanel();
            var bs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            var again = new Button { Content = "Generate", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; again.Click += (s, e) => make();
            var copy = new Button { Content = "Copy", Style = St("Accent") }; copy.Click += (s, e) => { if (SafeClip.Copy(output.Text)) Toast("Copied - the clipboard clears in " + SafeClip.ClearSeconds + " seconds."); };
            bs.Children.Add(again); bs.Children.Add(copy);
            if (use != null) { var u = new Button { Content = "Use this", Style = St("Accent"), Margin = new Thickness(6, 0, 0, 0) }; u.Click += (s, e) => use(output.Text); bs.Children.Add(u); }
            DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs); row.Children.Add(output);
            sp.Children.Add(row); sp.Children.Add(bits);
            var opts = new StackPanel();
            opts.Children.Add(Switch("Words (a passphrase) instead of characters", S.GenWords, v => { S.GenWords = v; S.Save(); make(); }));
            var lenRow = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
            var lenText = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = 3, Maximum = 64, Value = S.GenWords ? S.GenWordCount : S.GenLength, Style = St("HFader"), IsSnapToTickEnabled = true, TickFrequency = 1 };
            Action lenLabel = () => lenText.Text = S.GenWords ? S.GenWordCount + " words" : S.GenLength + " characters";
            slider.ValueChanged += (s, e) => { if (S.GenWords) S.GenWordCount = Math.Max(3, Math.Min(12, (int)slider.Value)); else S.GenLength = Math.Max(8, (int)slider.Value); lenLabel(); S.Save(); make(); };
            DockPanel.SetDock(lenText, Dock.Left); lenRow.Children.Add(lenText); lenRow.Children.Add(slider); opts.Children.Add(lenRow); lenLabel();
            opts.Children.Add(Switch("Lower case (a-z)", S.GenLower, v => { S.GenLower = v; S.Save(); make(); }));
            opts.Children.Add(Switch("Capitals (A-Z)", S.GenUpper, v => { S.GenUpper = v; S.Save(); make(); }));
            opts.Children.Add(Switch("Numbers (0-9)", S.GenDigits, v => { S.GenDigits = v; S.Save(); make(); }));
            opts.Children.Add(Switch("Symbols (!@#$%...)", S.GenSymbols, v => { S.GenSymbols = v; S.Save(); make(); }));
            opts.Children.Add(Switch("Leave out look-alikes (l, 1, I, O, 0)", S.GenAvoid, v => { S.GenAvoid = v; S.Save(); make(); }));
            sp.Children.Add(Card(opts));
            make();
            return sv;
        }

        // ---- import and backup
        UIElement TransferPage()
        {
            ScrollViewer sv; var sp = Page("Import, backup and export", null, out sv);
            var imp = new StackPanel();
            imp.Children.Add(new TextBlock { Text = "Import", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            imp.Children.Add(new TextBlock { Text = "From NordPass: Settings > Export items in NordPass gives a CSV file - pick it here. Also works with other CSV exports (browsers, 1Password, LastPass...) and Bitwarden's unencrypted JSON export. Items you already have aren't added twice.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var ib = new Button { Content = "Import", Style = St("Accent"), HorizontalAlignment = HorizontalAlignment.Left }; ib.Click += (s, e) => DoImport();
            imp.Children.Add(ib); sp.Children.Add(Card(imp));
            var bk = new StackPanel();
            bk.Children.Add(new TextBlock { Text = "Encrypted backup", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            bk.Children.Add(new TextBlock { Text = "A copy of your whole vault, still encrypted: it opens with your master password and Secret Key, or with your recovery code. Keep it on a USB stick or another drive. Restore adds its items to this vault.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var eb = new Button { Content = "Export backup", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0) }; eb.Click += (s, e) => ExportBackup();
            var rb = new Button { Content = "Restore backup", Style = St("Btn") }; rb.Click += (s, e) => RestoreBackup();
            row.Children.Add(eb); row.Children.Add(rb); bk.Children.Add(row); sp.Children.Add(Card(bk));
            var ex = new StackPanel();
            ex.Children.Add(new TextBlock { Text = "Export", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            ex.Children.Add(new TextBlock { Text = "Encrypted export: your items in the same encrypted format, locked with a separate password you pick now - handy for moving to another PC without sharing your master password. Restore backup opens it.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            ex.Children.Add(new TextBlock { Text = "Plain CSV: everything as readable text (NordPass's column layout) for moving to another password manager. It isn't encrypted - anyone who gets the file can read every password - so delete it straight after.", Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var exRow = new StackPanel { Orientation = Orientation.Horizontal };
            var ee = new Button { Content = "Encrypted export", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0) }; ee.Click += (s, e) => ExportEncrypted();
            var ec = new Button { Content = "Plain CSV export", Style = St("Btn") }; ec.Click += (s, e) => ExportCsv();
            exRow.Children.Add(ee); exRow.Children.Add(ec); ex.Children.Add(exRow); sp.Children.Add(Card(ex));
            return sv;
        }
        void DoImport()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a NordPass / CSV / Bitwarden JSON export", Filter = "Password exports (*.csv;*.json)|*.csv;*.json|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            Import.Result r;
            try { r = Import.FromFile(dlg.FileName); } catch (VaultException e) { Toast(e.Message, true); return; } catch (Exception e) { Toast("Couldn't read that file: " + e.Message, true); return; }
            if (MessageBox.Show(this, "Found " + r.Items.Count + " items" + (r.Source != null ? " (" + r.Source + ")" : "") + (r.Skipped > 0 ? ", " + r.Skipped + " skipped" : "") + ". Add them to your vault?", "Import", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var added = Import.Merge(vault.Data, r); SaveVault(added + " items imported.");
            ShowMain();
            if (MessageBox.Show(this, "Delete the export file now?\n\n" + dlg.FileName + "\n\nIt has your passwords in plain text. It goes to the Recycle Bin - empty the bin afterwards to be sure.", "Import", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(dlg.FileName, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); Toast("The export file is in the Recycle Bin."); }
                catch (Exception e) { Toast("Couldn't delete it: " + e.Message, true); }
        }
        void ExportBackup()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save an encrypted backup", FileName = "Hallow Keys backup " + DateTime.Now.ToString("yyyy-MM-dd") + ".hkbackup", Filter = "Hallow Keys backup (*.hkbackup)|*.hkbackup" };
            if (dlg.ShowDialog(this) != true) return;
            if (Travel) { Toast("Travel Mode is on - a full backup would copy the hidden vaults too. Turn Travel Mode off first, or export a vault in Import and export.", true); return; }
            try { Vault.WriteAtomic(dlg.FileName, vault.Serialize(), false); Toast("Backup saved - it opens with your master password (and Secret Key) or your recovery code."); }
            catch (Exception e) { Toast("Couldn't save the backup: " + e.Message, true); }
        }
        void RestoreBackup()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a Hallow Keys backup", Filter = "Hallow Keys backup (*.hkbackup;*.hkvault;*.bak)|*.hkbackup;*.hkvault;*.bak|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            var pw = Ask.Password(this, "Restore backup", "The master password the backup was made with (or the password you picked for an encrypted export):");
            if (pw == null) return;
            Vault b = null;
            try { b = Vault.Open(dlg.FileName, pw); }
            catch (SecretKeyNeededException)   // v2.0: a backup of a vault whose Secret Key this PC doesn't have
            {
                var t = Ask.Text(this, "Restore backup", "This backup needs its Secret Key (from the Emergency Kit of the vault it was made from):", "Secret Key"); string err; var sk = t == null ? null : SecretKey.Parse(t, out err);
                if (sk == null) { Gcm.Wipe(pw); if (t != null) Toast("That isn't a valid Secret Key.", true); return; }
                try { b = Vault.Open(dlg.FileName, pw, sk); } catch (Exception e) { Toast(e.Message, true); return; } finally { Gcm.Wipe(pw); Gcm.Wipe(sk); }
            }
            catch (Exception e) { Toast(e.Message, true); Gcm.Wipe(pw); return; }
            finally { Gcm.Wipe(pw); }
            foreach (var it in b.Data.Items) it.Sync = null;   // v2.0: restored items are new changes here
            Vaults.Adopt(vault.Data, b.Data, Device.Id);   // v2.1: items go back to their vaults (made again here if needed), or to Personal
            var r = new Import.Result { Items = b.Data.Items, Folders = b.Data.Folders, Templates = b.Data.Templates };
            var added = Import.Merge(vault.Data, r); b.Dispose();
            SaveVault(added + " items restored from the backup."); ShowMain();
        }

        // ---- settings
        UIElement SettingsPage()
        {
            ScrollViewer sv; var sp = Page("Settings", null, out sv);
            SyncCard(sp);          // v2.0
            TravelCard(sp);        // v2.1
            SiteIconsCard(sp);     // v2.1
            AuthCard(sp);          // v2.11: Hallow Authenticator
            var lk = new StackPanel();
            lk.Children.Add(new TextBlock { Text = "Locking", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "Lock after", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var cb = new ComboBox { Width = 140, Style = St("Combo"), ItemsSource = new[] { "1 minute", "2 minutes", "5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour" } };
            var mins = new[] { 1, 2, 5, 10, 15, 30, 60 }; cb.SelectedIndex = Math.Max(0, Array.IndexOf(mins, S.AutoLockMinutes));
            cb.SelectionChanged += (s, e) => { S.AutoLockMinutes = mins[cb.SelectedIndex]; S.Save(); };
            row.Children.Add(cb); row.Children.Add(new TextBlock { Text = "with no mouse or keyboard use", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            lk.Children.Add(row);
            lk.Children.Add(new TextBlock { Text = "It also locks when Windows locks or the PC sleeps, and copied passwords leave the clipboard after " + SafeClip.ClearSeconds + " seconds (they never go into Windows' clipboard history).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(Card(lk));
            HotkeyCard(sp);
            DesktopFillCard(sp);   // v1.5
            DevToolsCard(sp);      // v2.6: SSH agent, command line, Activity
            HealthSettingsCard(sp); // v1.7
            ExpiryCard(sp);        // v1.6
            TemplatesCard(sp);     // v1.6
            // Windows Hello
            var wh = new StackPanel();
            wh.Children.Add(new TextBlock { Text = "Windows Hello", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            wh.Children.Add(new TextBlock { Text = "Unlock with your face, fingerprint or PIN instead of typing the master password. This is for convenience, not extra security: the vault's key is kept by Windows for your account, so anything that can run as you on this PC could get at it. Your master password still works, and you need it if Windows Hello stops working.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            wh.Children.Add(Switch("Unlock with Windows Hello", S.UseHello && Hello.Enabled, v =>
            {
                if (v)
                {
                    if (!Hello.Available()) { Toast("Windows Hello isn't set up on this PC (Settings > Accounts > Sign-in options).", true); ShowMain(); return; }
                    var k = vault.DataKeyCopy(); try { Hello.Enable(k); } finally { Gcm.Wipe(k); }
                    S.UseHello = true; Toast("Windows Hello unlock is on.");
                }
                else { Hello.Disable(); S.UseHello = false; Toast("Windows Hello unlock is off."); }
                S.Save();
            }));
            sp.Children.Add(Card(wh));
            // master password and recovery
            var mp = new StackPanel();
            mp.Children.Add(new TextBlock { Text = "Master password and recovery", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            var mr = new StackPanel { Orientation = Orientation.Horizontal };
            var ch = new Button { Content = "Change master password", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0) }; ch.Click += (s, e) => ChangeMasterPassword(false);
            var rs = new Button { Content = "Recovery sheet", Style = St("Btn"), ToolTip = "Makes a new recovery code (the old one stops working) and shows it to print" };
            rs.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Make a new recovery code? The old one stops working.", "Hallow Keys", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                IsEnabled = false; string code = null;
                vaultBusy = true;
                new System.Threading.Thread(() => { code = vault.NewRecoveryCode(); Dispatcher.BeginInvoke(new Action(() => { IsEnabled = true; vaultBusy = false; SaveVault(null); ShowRecoverySheet(code, false); })); }) { IsBackground = true }.Start();
            };
            mr.Children.Add(ch); mr.Children.Add(rs); mp.Children.Add(mr);
            sp.Children.Add(Card(mp));
            // app
            var ap = new StackPanel();
            ap.Children.Add(new TextBlock { Text = "App", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            ap.Children.Add(Switch("Keep running in the tray when I close this window", S.TrayOnClose, v => { S.TrayOnClose = v; S.Save(); }));
            ap.Children.Add(Switch("Use the Hallow icon (lives in Hallow Hub's tray menu when Hub runs)", S.UseHallowIcon, v => { S.UseHallowIcon = v; S.Save(); CheckHost(); }));
            ap.Children.Add(Switch("Use the Hallow accent (the colour picked in Hallow Toolkit)", S.UseHallowAccent, v => { S.UseHallowAccent = v; S.Save(); ApplyAccent(); }));
            ap.Children.Add(LookRow());   // v2.9
            ap.Children.Add(Switch("Start with Windows (in the tray, locked)", StartsWithWindows(), v => SetStartWithWindows(v)));
            SettingsExtra(ap);
            var ar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var upd = new Button { Content = "Check for updates", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0) }; upd.Click += (s, e) => CheckForUpdate(true);
            var fold = new Button { Content = "Copy folder path", Style = St("Btn") }; fold.Click += (s, e) => { Clipboard.SetText(KeysSettings.Dir); Toast("Copied: " + KeysSettings.Dir); };
            ar.Children.Add(upd); ar.Children.Add(fold); ap.Children.Add(ar);
            ap.Children.Add(new TextBlock { Text = "Your vault on this PC: " + KeysSettings.VaultPath + " (the previous save is next to it as .bak, older versions in the versions folder)" + (S.SyncOn && S.SyncFolder.Length > 0 ? "; synced through " + SyncFolder.MainPath(S.SyncFolder) : "") + ". Hallow Keys " + Program.Version + " - icons: Font Awesome Free (CC BY 4.0).", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            sp.Children.Add(Card(ap));
            return sv;
        }
        void SettingsExtra(StackPanel ap) { SettingsExtraImpl(ap); }
        partial void SettingsExtraImpl(StackPanel ap);
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool StartsWithWindows() { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("HallowKeys") != null; }
        static void SetStartWithWindows(bool on)
        {
            if (KeysSettings.TestDir != null) return;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) { if (on) k.SetValue("HallowKeys", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue("HallowKeys", false); } } catch { }
        }

        // ---- change the master password (re-wraps the same data key; with a recovery code, the current one isn't asked)
        void ChangeMasterPassword(bool fromRecovery)
        {
            byte[] current = null;
            if (!fromRecovery) { current = Ask.Password(this, "Change master password", "Your current master password:"); if (current == null) return; }
            var np = Ask.NewPassword(this, fromRecovery ? "Pick a new master password" : "Change master password");
            if (np == null) { Gcm.Wipe(current); if (fromRecovery) Toast("Keep your recovery sheet safe - you used it to unlock.", true); return; }
            IsEnabled = false; vaultBusy = true;
            new System.Threading.Thread(() =>
            {
                string err = null; byte[] fresh = null;
                try
                {
                    if (current != null) using (Vault.Open(KeysSettings.VaultPath, current)) { }   // checks the current password
                    if (fromRecovery && vault.UsesSecretKey) fresh = vault.SetPasswordAfterRecovery(np);   // v2.0: keeps the Secret Key if it's here (or inside the vault), else a new one
                    else vault.ChangePassword(np);
                    PhoneAfterPasswordChange(np);
                }
                catch (Exception e) { err = e.Message; }
                finally { Gcm.Wipe(current); Gcm.Wipe(np); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true; vaultBusy = false;
                    if (err != null) { Toast(err, true); return; }
                    WriteStatus(); AfterSave(); saveSeq++; SyncSoon(500); Toast("Master password changed" + (S.SyncOn ? " - your other PCs take the change when they sync." : "."));
                    if (fresh != null) { var t = SecretKey.Format(fresh); Gcm.Wipe(fresh); ShowEmergencyKit(null, t, "rotated"); }
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- the recovery sheet: shown once, printable
        // v2.0: with a Secret Key this is the Emergency Kit (recovery code + Secret Key); a 1.x vault still gets the recovery sheet
        void ShowRecoverySheet(string code, bool first)
        {
            if (!Snap.On && vault != null && vault.UsesSecretKey) { var k = vault.CurrentSecretKey(); if (k != null) { var t = SecretKey.Format(k); Gcm.Wipe(k); ShowEmergencyKit(code, t, first ? "new" : "recovery"); return; } }
            ShowRecoverySheetOld(code, first);
        }
        void ShowRecoverySheetOld(string code, bool first)
        {
            var w = new Window { Title = "Hallow Keys - recovery sheet", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null,
                Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = FontFamily };
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(new TextBlock { Text = first ? "Your vault is ready. Print your recovery sheet." : "Your new recovery sheet", FontSize = 17, FontWeight = FontWeights.Bold });
            sp.Children.Add(new TextBlock { Text = "If you forget your master password, this code is the only way back in. It's shown only now. Print it (or write it down) and keep it somewhere safe and offline - not on this PC.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
            var sheet = RecoverySheetVisual(code);
            sp.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Child = sheet });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var print = new Button { Content = "Print", Style = St("Accent"), Margin = new Thickness(0, 0, 8, 0) };
            print.Click += (s, e) => { var pd = new PrintDialog(); if (pd.ShowDialog() == true) { var v = RecoverySheetVisual(code); v.Measure(new Size(pd.PrintableAreaWidth, double.PositiveInfinity)); v.Arrange(new Rect(new Point(40, 40), v.DesiredSize)); pd.PrintVisual(v, "Hallow Keys recovery sheet"); } };
            var done = new Button { Content = "I've saved it", Style = St("Btn") }; done.Click += (s, e) => w.Close();
            row.Children.Add(print); row.Children.Add(done); sp.Children.Add(row);
            w.Content = sp;
            if (Snap.On) { Snap.Save(w, snapDir + "\\recovery.png", 560, 0); return; }
            w.ShowDialog();
            code = null;
        }
        public static string snapDir;
        static FrameworkElement RecoverySheetVisual(string code)
        {
            var sp = new StackPanel { Margin = new Thickness(26), Width = 460 };
            var black = Brushes.Black; var grey = new SolidColorBrush(Color.FromRgb(90, 90, 90));
            sp.Children.Add(new TextBlock { Text = "Hallow Keys - recovery sheet", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = black });
            sp.Children.Add(new TextBlock { Text = "Made " + DateTime.Now.ToString("d MMMM yyyy") + " on " + Environment.MachineName, Foreground = grey, Margin = new Thickness(0, 2, 0, 14) });
            sp.Children.Add(new TextBlock { Text = "Recovery code", Foreground = grey });
            sp.Children.Add(new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = black, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "If you forget your master password: open Hallow Keys, click \"Forgot it? Use your recovery code\", type this code and pick a new master password. Anyone with this code and your vault file can open your vault - keep this sheet somewhere safe.", Foreground = black, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            return sp;
        }
    }

    // ---- small dialogs (password boxes are never logged or kept)
    public static class Ask
    {
        static Window Shell(Window owner, string title, out StackPanel sp)
        {
            var w = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner != null && owner.IsVisible ? owner : null, Background = (Brush)Application.Current.FindResource("Bg"), Foreground = (Brush)Application.Current.FindResource("Text"), FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5 };
            sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            sp.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            return w;
        }
        static StackPanel Buttons(Window w, Func<bool> ok)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var b = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("Accent"), IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (s, e) => { if (ok()) { w.DialogResult = true; } };
            var c = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("Btn"), IsCancel = true };
            row.Children.Add(b); row.Children.Add(c); return row;
        }
        public static string Text(Window owner, string title, string text, string label)
        {
            StackPanel sp; var w = Shell(owner, title, out sp);
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Style = (Style)Application.Current.FindResource("Field"), FontFamily = new FontFamily("Consolas") }; sp.Children.Add(tb);
            sp.Children.Add(Buttons(w, () => tb.Text.Trim().Length > 0));
            w.Loaded += (s, e) => tb.Focus();
            return w.ShowDialog() == true ? tb.Text.Trim() : null;
        }
        static byte[] Utf8(PasswordBox pb)
        {
            var ss = pb.SecurePassword; IntPtr p = IntPtr.Zero;
            try { p = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(ss); var ch = new char[ss.Length]; System.Runtime.InteropServices.Marshal.Copy(p, ch, 0, ch.Length); var b = System.Text.Encoding.UTF8.GetBytes(ch); Array.Clear(ch, 0, ch.Length); return b; }
            finally { if (p != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(p); ss.Dispose(); }
        }
        public static byte[] Password(Window owner, string title, string text) { bool x; return Password(owner, title, text, null, out x); }
        // v1.6: with an optional tick box (unticked to start), e.g. "include recovery phrases" for the CSV export
        public static byte[] Password(Window owner, string title, string text, string tick, out bool ticked)
        {
            StackPanel sp; var w = Shell(owner, title, out sp);
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var pb = new PasswordBox { Style = (Style)Application.Current.FindResource("Pwd") }; sp.Children.Add(pb);
            CheckBox cb = null;
            if (tick != null) { cb = new CheckBox { Content = new TextBlock { Text = tick, TextWrapping = TextWrapping.Wrap }, IsChecked = false, Style = (Style)Application.Current.FindResource("Switch"), Margin = new Thickness(0, 10, 0, 0) }; sp.Children.Add(cb); }
            sp.Children.Add(Buttons(w, () => pb.SecurePassword.Length > 0));
            w.Loaded += (s, e) => pb.Focus();
            var r = w.ShowDialog() == true ? Utf8(pb) : null; pb.Clear(); ticked = r != null && cb != null && cb.IsChecked == true; return r;
        }
        public static byte[] NewPassword(Window owner, string title)
        {
            StackPanel sp; var w = Shell(owner, title, out sp);
            sp.Children.Add(new TextBlock { Text = "New master password", Margin = new Thickness(0, 0, 0, 6) });
            var p1 = new PasswordBox { Style = (Style)Application.Current.FindResource("Pwd") }; sp.Children.Add(p1);
            sp.Children.Add(new TextBlock { Text = "Type it again", Margin = new Thickness(0, 10, 0, 6) });
            var p2 = new PasswordBox { Style = (Style)Application.Current.FindResource("Pwd") }; sp.Children.Add(p2);
            var err = new TextBlock { Foreground = MainWindow.B("#F0BE5A"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap }; sp.Children.Add(err);
            sp.Children.Add(Buttons(w, () =>
            {
                if (p1.Password.Length < 8) { err.Text = "Use at least 8 characters."; return false; }
                if (p1.Password != p2.Password) { err.Text = "The two aren't the same."; return false; }
                return true;
            }));
            w.Loaded += (s, e) => p1.Focus();
            var r = w.ShowDialog() == true ? Utf8(p1) : null; p1.Clear(); p2.Clear(); return r;
        }
    }
}
