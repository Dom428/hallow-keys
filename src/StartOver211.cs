// Hallow Keys 2.11 - "Lost both? Start over" on the lock screen: for a vault whose master password AND recovery code are lost.
// Nothing is deleted: the vault and everything tied to it (its .bak, snapshots, Secret Key files, the Windows Hello key, the phone copy,
// the site-icon cache, sync / sharing merge leftovers, copies kept before joining) move into %APPDATA%\HallowKeys\abandoned-<yyyyMMdd-HHmm>.
// Settings tied to that vault are reset (sync off - the copy in the sync folder is left as it is, Windows Hello off, phone copy off, SSH
// key picks cleared); then the normal "Create your vault" screen shows. Turning sync on again later with the same folder asks first
// (it holds the old vault - joining it needs that vault's password), so the new vault can never silently replace the synced one.
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowKeys
{
    public static class StartOver
    {
        public const string Confirm = "ERASE";
        static readonly string[] FileNames = { "vault.hkvault", "vault.hkvault.bak", "vault.hkvault.tmp", "phone.bin", "hello.bin", "icons.hkcache" };
        static readonly string[] Patterns = { "secret-key-*.bin", "vault-before-joining-*.hkvault" };
        static readonly string[] DirNames = { "versions", "sync-merged", "shared-merged" };
        // moves the vault's files aside; returns the folder (null when there was nothing to move)
        public static string MoveAside(DateTime now)
        {
            var dir = KeysSettings.Dir; if (!Directory.Exists(dir)) return null;
            var files = FileNames.Select(f => Path.Combine(dir, f)).Where(File.Exists).Concat(Patterns.SelectMany(p => Directory.GetFiles(dir, p))).Distinct().ToList();
            var dirs = DirNames.Select(d => Path.Combine(dir, d)).Where(Directory.Exists).ToList();
            if (files.Count == 0 && dirs.Count == 0) return null;
            var dest = Path.Combine(dir, "abandoned-" + now.ToString("yyyyMMdd-HHmm"));
            for (int i = 2; Directory.Exists(dest); i++) dest = Path.Combine(dir, "abandoned-" + now.ToString("yyyyMMdd-HHmm") + "-" + i);
            Directory.CreateDirectory(dest);
            foreach (var f in files) File.Move(f, Path.Combine(dest, Path.GetFileName(f)));
            foreach (var d in dirs) Directory.Move(d, Path.Combine(dest, Path.GetFileName(d)));
            File.WriteAllText(Path.Combine(dest, "README.txt"), "Hallow Keys moved these files here on " + now.ToString("d MMM yyyy HH:mm") + " when you chose \"Start over\" on the lock screen.\r\n" +
                "They are the old vault - still encrypted, and unreadable without its master password or recovery code. Nothing was deleted.\r\n" +
                "If you ever find the password again: close Hallow Keys, move vault.hkvault back into the HallowKeys folder (rename the new one first).\r\n");
            return dest;
        }
    }

    public partial class MainWindow
    {
        // the confirmed reset (window thread, locked only). typed must be exactly ERASE.
        string StartOverNow(string typed)
        {
            if (vault != null) return "error unlocked - Start over is only on the lock screen";
            if (typed != StartOver.Confirm) return "error not confirmed - nothing was changed";
            string dest;
            try { StopSync(); dest = StartOver.MoveAside(DateTime.Now); }
            catch (Exception e) { return "error couldn't move the old vault aside (" + e.Message + ") - close anything using the HallowKeys folder and try again"; }
            S.SyncOn = false; S.LastSync = ""; S.UseHello = false; S.PhoneCopy = false; S.PhoneEdit = false; S.SshKeyIds.Clear(); S.LastLeakCheck = ""; S.Save();
            Hello.Disable(); PhoneCopy.Remove(); syncState = "off";
            TestLog("start over: " + (dest == null ? "nothing to move" : Path.GetFileName(dest)));
            WriteStatus();
            ShowLock(dest == null ? "Start a new vault." : "The old vault was moved to " + dest + " (nothing deleted). Sync is off. Create your new vault:");
            return "ok " + Family.Json(new System.Collections.Generic.Dictionary<string, object> { { "folder", dest } });
        }
        void StartOverDialog()
        {
            var w = new Window { Title = "Hallow Keys - start over", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = new FontFamily("Segoe UI") };
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            sp.Children.Add(new TextBlock { Text = "Lost both the master password and the recovery code?", FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock { Text = "Then nobody can open this vault - not you, not Hallow, not anyone. Starting over makes a new, empty vault. Everything in the old one stays unreadable forever.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock { Text = "Nothing is deleted: the old vault and its files are moved into a dated \"abandoned\" folder next to it, in case the password turns up. Sync is turned off (the copy in your sync folder is left alone), and Windows Hello unlock and the phone copy for Hallow Remote are turned off - set them up again for the new vault.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock { Text = "Type ERASE to start over:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var tb = new TextBox { Style = St("Field"), FontFamily = new FontFamily("Consolas") }; sp.Children.Add(tb);
            var msg = new TextBlock { Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; sp.Children.Add(msg);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var go = new Button { Content = "Start over", Style = St("Btn"), IsEnabled = false, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Style = St("Accent"), IsCancel = true, IsDefault = true, MinWidth = 90 };
            tb.TextChanged += (s, e) => go.IsEnabled = tb.Text == StartOver.Confirm;
            go.Click += (s, e) => { var r = StartOverNow(tb.Text); if (r.StartsWith("ok")) w.Close(); else msg.Text = r.Substring(6); };
            cancel.Click += (s, e) => w.Close();
            row.Children.Add(go); row.Children.Add(cancel); sp.Children.Add(row);
            w.Loaded += (s, e) => tb.Focus();
            w.ShowDialog();
        }
    }
}
