// Hallow Keys 2.5 - "Emergency access", the window's parts: the Emergency access section of the Sharing page (make a package for a
// trusted contact, open one you were given) and its dialog. The package format is in Emergency25.cs; the Emergency Kit is in Features20UI.cs.
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
        partial void EmergencySectionImpl(StackPanel sp)
        {
            sp.Children.Add(new TextBlock { Text = "Emergency access", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 18, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "Leave someone you trust a way into your vaults if something happens to you. An emergency package is an encrypted copy of the vaults you pick that only their Hallow Keys can open (it's made for their member card). You can also make it need your recovery code, so the file alone is useless: keep the code where they can get it only when it's needed - a sealed envelope, a safe, your solicitor.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            sp.Children.Add(new TextBlock { Text = "It's a copy as of the day you make it - make a new one now and then. Not possible without a server: \"release it to them after N days unless I say no\" (NordPass and Bitwarden run a server that waits and asks you). Hallow Keys has no server, so it doesn't offer that.", Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6) });
            var row = new WrapPanel();
            var mk = new Button { Content = "Make an emergency package", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; mk.Click += (s, e) => MakeEmergency();
            var op = new Button { Content = "Open an emergency package", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; op.Click += (s, e) => OpenEmergency();
            var kit = new Button { Content = "Print your Emergency Kit", Style = St("Btn") }; kit.Click += (s, e) => { view = "settings"; ShowMain(); Toast("Settings > Emergency Kit prints it (it asks for your master password)."); };
            row.Children.Add(mk); row.Children.Add(op); row.Children.Add(kit); sp.Children.Add(row);
        }
        // the dialog: the contact's card, the vaults, whether the recovery code is needed too
        Window EmergencyDialog(out TextBox cardBox, out List<CheckBox> picks, out CheckBox needCode, out PasswordBox codeBox, out Button ok)
        {
            var w = new Window { Title = "Make an emergency package", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = FontFamily };
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            sp.Children.Add(new TextBlock { Text = "Make an emergency package", FontSize = 17, FontWeight = FontWeights.Bold });
            sp.Children.Add(new TextBlock { Text = "1. Your contact's member card (they find it on their Sharing page). Compare the fingerprint with them before you send anything.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
            cardBox = new TextBox { Style = St("Area"), Height = 52, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11 }; sp.Children.Add(cardBox);
            var fp = new TextBlock { Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap }; sp.Children.Add(fp);
            var cb = cardBox; cb.TextChanged += (s, e) => { string why; var c = Members.Parse(cb.Text, out why); fp.Text = c == null ? (cb.Text.Trim().Length == 0 ? "" : why) : "For " + c.Name + " - fingerprint " + c.Fingerprint + " (it must match theirs)"; fp.Foreground = c == null ? B("#F0BE5A") : B("#6ED28C"); };
            sp.Children.Add(new TextBlock { Text = "2. The vaults it holds (as they are today):", Margin = new Thickness(0, 12, 0, 4) });
            picks = new List<CheckBox>();
            foreach (var v in VisibleVaults()) { var c = new CheckBox { Content = new TextBlock { Text = v.Name + "  (" + vault.Data.Items.Count(i => Vaults.IdOf(vault.Data, i) == v.Id) + " items)" }, Tag = v.Id, Style = St("Switch"), IsChecked = v.Id == Vaults.PersonalId, Margin = new Thickness(0, 2, 0, 2) }; picks.Add(c); sp.Children.Add(c); }
            sp.Children.Add(new TextBlock { Text = "3. Extra lock (recommended):", Margin = new Thickness(0, 12, 0, 4) });
            needCode = new CheckBox { Content = new TextBlock { Text = "Also needs my recovery code - the package can't be opened with their Hallow Keys alone", TextWrapping = TextWrapping.Wrap }, Style = St("Switch"), IsChecked = true }; sp.Children.Add(needCode);
            codeBox = new PasswordBox { Style = St("Pwd"), Margin = new Thickness(0, 6, 0, 0), ToolTip = "Your recovery code (from your recovery sheet / Emergency Kit) - checked against your vault" }; sp.Children.Add(codeBox);
            var nc = needCode; var cbx = codeBox; nc.Click += (s, e) => cbx.IsEnabled = nc.IsChecked == true;
            sp.Children.Add(new TextBlock { Text = "Your recovery code (checked against your vault, never stored)", Foreground = Muted, FontSize = 11.5 });
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            ok = new Button { Content = "Save the package", Style = St("Accent"), Margin = new Thickness(0, 0, 8, 0) }; var cancel = new Button { Content = "Cancel", Style = St("Btn") }; cancel.Click += (s, e) => w.Close();
            btns.Children.Add(ok); btns.Children.Add(cancel); sp.Children.Add(btns);
            return w;
        }
        void MakeEmergency()
        {
            Members.Ensure(vault.Data, Environment.UserName);
            TextBox cardBox; List<CheckBox> picks; CheckBox needCode; PasswordBox codeBox; Button ok;
            var w = EmergencyDialog(out cardBox, out picks, out needCode, out codeBox, out ok);
            ok.Click += (s, e) =>
            {
                string why; var c = Members.Parse(cardBox.Text, out why); if (c == null) { MessageBox.Show(w, why ?? "Paste their member card.", "Emergency package"); return; }
                var ids = picks.Where(p => p.IsChecked == true).Select(p => (string)p.Tag).ToList(); if (ids.Count == 0) { MessageBox.Show(w, "Pick at least one vault.", "Emergency package"); return; }
                var code = needCode.IsChecked == true ? codeBox.Password : null; if (code != null && code.Trim().Length == 0) { MessageBox.Show(w, "Type your recovery code, or untick the extra lock.", "Emergency package"); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save the emergency package", FileName = "Emergency package for " + c.Name + " " + DateTime.Now.ToString("yyyy-MM-dd") + ".hkemergency", Filter = "Hallow Keys emergency package (*.hkemergency)|*.hkemergency" };
                if (dlg.ShowDialog(w) != true) return;
                w.IsEnabled = false; var data = vault.Data; var path = dlg.FileName;
                new System.Threading.Thread(() =>
                {
                    string err = null; try { File.WriteAllBytes(path, EmergencyPackage.Make(data, ids, c, code, KeysSettings.VaultPath)); } catch (Exception ex) { err = ex.Message; }
                    Dispatcher.BeginInvoke(new Action(() => { w.IsEnabled = true; if (err != null) { MessageBox.Show(w, err, "Emergency package"); return; } w.Close(); Toast("Saved. Give the file to " + c.Name + (code != null ? " - and keep your recovery code where they can get it only when it's needed." : ".")); }));
                }) { IsBackground = true }.Start();
            };
            if (Snap.On) { Snap.Save(w, Path.Combine(snapDir, "v25-emergency-dialog.png"), 560, 0); return; }
            w.ShowDialog();
        }
        void OpenEmergency()
        {
            Members.Ensure(vault.Data, Environment.UserName);
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Open an emergency package", Filter = "Hallow Keys emergency package (*.hkemergency)|*.hkemergency" };
            if (dlg.ShowDialog(this) != true) return;
            byte[] b; EmergencyPackage.Info info;
            try { b = File.ReadAllBytes(dlg.FileName); info = EmergencyPackage.Peek(b); } catch (Exception ex) { Toast(ex.Message, true); return; }
            string code = null;
            if (info.NeedsCode)
            {
                var pw = Ask.Password(this, "Open " + info.From + "'s emergency package", "From " + info.From + " (fingerprint " + info.FromFingerprint + "), made " + info.Made.ToLocalTime().ToString("d MMM yyyy") + ": " + info.Vaults + ", " + info.Items + " items.\n\nIt also needs " + info.From + "'s recovery code:");
                if (pw == null) return; code = System.Text.Encoding.UTF8.GetString(pw); Gcm.Wipe(pw);
            }
            else if (MessageBox.Show(this, "Open " + info.From + "'s emergency package?\n\nMade " + info.Made.ToLocalTime().ToString("d MMM yyyy") + ": " + info.Vaults + ", " + info.Items + " items. They go into a new vault in your Hallow Keys.", "Emergency package", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            IsEnabled = false; var mine = vault.Data;
            new System.Threading.Thread(() =>
            {
                VaultData d = null; string err = null; EmergencyPackage.Info i2 = null;
                try { d = EmergencyPackage.Open(b, mine, code, out i2); } catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true; if (err != null) { Toast(err, true); return; }
                    try { var v = EmergencyPackage.Import(vault.Data, d, i2, Device.Id); curVault = v.Id; view = "all"; SaveVault("Added " + d.Items.Count + " items from " + i2.From + "'s emergency package to the vault \"" + v.Name + "\"."); ShowMain(); }
                    catch (Exception ex) { Toast(ex.Message, true); }
                }));
            }) { IsBackground = true }.Start();
        }
        partial void SnapExtra25Impl(MainWindow w)
        {
            w.view = "sharing"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v25-sharing-emergency.png"), 1280, 1500);
            w.MakeEmergency();
            w.ShowEmergencyKit("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789", "HK-ABCDEF-GHJKLM-NPQRST-UVWXYZ-2345", "v25");
            SnapExtra26(w);   // v2.6
            SnapExtra27(w);   // v2.7
        }
    }
}
