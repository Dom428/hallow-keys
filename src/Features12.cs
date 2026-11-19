// Hallow Keys v1.2 - the window parts of the leak check and the phone copy.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        partial void LeakedImpl(ref HashSet<string> ids) { if (vault != null && vault.Data.LeakedIds != null) ids = new HashSet<string>(vault.Data.LeakedIds); }
        partial void HealthExtraTextImpl(ref string t) { t = " Leaked: found in known data breaches (Check for leaked passwords)."; }
        partial void HealthExtraImpl(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Check for leaked passwords", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            box.Children.Add(new TextBlock { Text = "Asks Have I Been Pwned whether your passwords appear in known data breaches. Only the first 5 characters of each password's SHA-1 hash leave this PC - never a password, a username or a site - and the answer is matched here. " +
                (S.LastLeakCheck.Length > 0 ? "Last checked " + S.LastLeakCheck + "." : "Not checked yet."), Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var b = new Button { Content = "Check for leaked passwords", Style = St("Accent"), Margin = new Thickness(0, 0, 12, 0) };
            var status = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (s, e) => RunLeakCheck(b, status);
            row.Children.Add(b); row.Children.Add(status); box.Children.Add(row);
            box.Children.Add(Switch("Check once a week by itself (while Hallow Keys is unlocked)", S.WeeklyLeakCheck, v => { S.WeeklyLeakCheck = v; S.Save(); }));
            sp.Children.Add(Card(box));
        }
        bool leakRunning;
        void RunLeakCheck(Button b, TextBlock status)
        {
            if (leakRunning || vault == null) return; leakRunning = true;
            if (b != null) b.IsEnabled = false;
            var items = vault.Data.Items.ToList();
            new System.Threading.Thread(() =>
            {
                string err; var leaked = Pwned.Check(items, p => Dispatcher.BeginInvoke(new Action(() => { if (status != null) status.Text = p; })), out err);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    leakRunning = false; if (b != null) b.IsEnabled = true;
                    if (leaked == null) { if (status != null) status.Text = err; else Toast(err, true); return; }
                    if (vault == null) return;
                    vault.Data.LeakedIds = leaked.ToList(); S.LastLeakCheck = DateTime.Now.ToString("d MMM yyyy HH:mm"); S.Save();
                    SaveVault(leaked.Count == 0 ? "No leaked passwords found." : leaked.Count + " login" + (leaked.Count == 1 ? "" : "s") + " use a leaked password - change them first.");
                    if (view == "health") ShowMain();
                }));
            }) { IsBackground = true }.Start();
        }
        // weekly, if turned on: checked when unlocked and every few hours
        DispatcherTimer weekly;
        partial void OnUnlocked()
        {
            if (weekly == null) { weekly = new DispatcherTimer { Interval = TimeSpan.FromHours(3) }; weekly.Tick += (s, e) => MaybeWeekly(); weekly.Start(); }
            Dispatcher.BeginInvoke(new Action(MaybeWeekly), DispatcherPriority.ApplicationIdle);
            if (S.PhoneCopy && vault != null && vault.Data.PhoneKey != null) try { PhoneCopy.Write(vault.Data); } catch { }
        }
        void MaybeWeekly()
        {
            if (!S.WeeklyLeakCheck || vault == null) return;
            DateTime last; if (DateTime.TryParse(S.LastLeakCheck, out last) && (DateTime.Now - last).TotalDays < 7) return;
            RunLeakCheck(null, null);
        }

        // ---- phone copy (Hallow Remote)
        partial void AfterSaveImpl() { if (S.PhoneCopy && vault != null && vault.Data.PhoneKey != null) try { PhoneCopy.Write(vault.Data); } catch { } }
        partial void SettingsExtraImpl(StackPanel ap)
        {
            ap.Children.Add(Switch("Phone copy for Hallow Remote (read-only, opens with your master password on your phone - needs Hallow Remote with HTTPS)", S.PhoneCopy && vault != null && vault.Data.PhoneKey != null, v =>
            {
                if (v)
                {
                    var pw = Ask.Password(this, "Phone copy", "Your phone opens its copy with your master password. Type it here once to set it up:");
                    if (pw == null) { ShowMain(); return; }
                    IsEnabled = false;
                    new System.Threading.Thread(() =>
                    {
                        string err = null;
                        try { using (Vault.Open(KeysSettings.VaultPath, pw)) { } PhoneCopy.NewKey(vault.Data, pw); }   // checks it's the master password first
                        catch (Exception e) { err = e.Message; }
                        finally { Gcm.Wipe(pw); }
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            IsEnabled = true;
                            if (err != null) { Toast(err, true); ShowMain(); return; }
                            S.PhoneCopy = true; S.Save(); SaveVault("Phone copy is on - open the Keys tab in Hallow Remote on your phone.");
                        }));
                    }) { IsBackground = true }.Start();
                }
                else { S.PhoneCopy = false; S.Save(); if (vault != null) { vault.Data.PhoneKey = null; vault.Data.PhoneSalt = null; } PhoneCopy.Remove(); SaveVault("Phone copy is off and its file is deleted."); }
            }));
        }
        // a new master password: the phone copy follows it (or is turned off if the key can't be made)
        public void PhoneAfterPasswordChange(byte[] newPassword)
        {
            if (!S.PhoneCopy || vault == null) return;
            try { PhoneCopy.NewKey(vault.Data, newPassword); vault.Save(); PhoneCopy.Write(vault.Data); } catch { }
        }
    }
}
