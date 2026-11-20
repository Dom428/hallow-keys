// Hallow Keys 2.3 - "Passkeys", the window's parts: the Passkeys tab (Vaults21.cs), a passkey's details (the private key is never shown
// or copied - it only signs, here in the app), a login's passkeys listed next to it, and the --snap pictures.
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowKeys
{
    public partial class MainWindow
    {
        // the private key's row: no Show, no Copy
        void PasskeyKeyRow(StackPanel sp)
        {
            sp.Children.Add(new TextBlock { Text = "Private key", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("lock", 12, Muted); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(ic);
            row.Children.Add(new TextBlock { Text = "Kept inside your vault - it signs in here when the site asks, and is never shown, copied or exported as text.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
            sp.Children.Add(row);
        }
        // above a passkey's fields: what it is and when it was made
        void PasskeyHead(StackPanel sp, Item it)
        {
            var rp = ItemTypes.Val(it, "rpId");
            var b = new Border { Background = (Brush)FindResource("PanelHi"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6) };
            b.Child = new TextBlock { Text = "Sign in to " + rp + " with this passkey: click the username box or the site's passkey button in your browser, then pick it in Hallow Keys' prompt. Made " + it.Created.ToString("d MMM yyyy") + ".", TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(b);
            var login = vault == null ? null : vault.Data.Items.Where(IsShown).FirstOrDefault(i => i.Type == "login" && Passkeys.OfLogin(new[] { it }, i).Count > 0);
            if (login != null)
            {
                var go = new Button { Content = "Login for this site: " + login.Title, Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4) };
                go.Click += (s, e) => { selected = login; FillList(); ShowDetail(); };
                sp.Children.Add(go);
            }
        }
        // a login's passkeys (same site), listed with it
        void LoginPasskeys(StackPanel sp, Item login)
        {
            if (vault == null) return;
            var keys = Passkeys.OfLogin(vault.Data.Items.Where(IsShown), login); if (keys.Count == 0) return;
            sp.Children.Add(new TextBlock { Text = keys.Count == 1 ? "Passkey" : "Passkeys (" + keys.Count + ")", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 3) });
            foreach (var k in keys)
            {
                var pk = k; var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var open = new Button { Content = "Open", Style = St("Btn") }; open.Click += (s, e) => { selected = pk; FillList(); ShowDetail(); };
                DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
                var ic = Icons.Make("fingerprint", 13, B(Program.AccentHex)); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ic, Dock.Left); row.Children.Add(ic);
                var u = ItemTypes.Val(pk, "userName");
                row.Children.Add(new TextBlock { Text = (u.Length > 0 ? u : "(no user name)") + "  -  " + ItemTypes.Val(pk, "rpId"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(row);
            }
        }

        void SnapExtra23()
        {
            Action<MainWindow, string> big = (mw, n) => Snap.Save(mw, Path.Combine(snapDir, n), 1280, 760);
            var w = new MainWindow(false, true); w.vault = new Vault { Data = FakeData21() };
            var d = w.vault.Data; byte[] cid, pt;
            var gh = Passkeys.NewItem("github.com", "GitHub", Rng.Bytes(16), "alex-example", "Alex Example", out cid, out pt); d.Items.Add(gh);
            d.Items.Add(new Item { Title = "GitHub", Type = "login", Username = "alex-example", Password = "made-up-github-pass", Urls = { "https://github.com" } });
            var g2 = Passkeys.NewItem("google.com", "Google", Rng.Bytes(16), "alex@example.com", "Alex Example", out cid, out pt); d.Items.Add(g2);
            var g3 = Passkeys.NewItem("webauthn.io", "webauthn.io", Rng.Bytes(16), "alex-test", "alex-test", out cid, out pt); d.Items.Add(g3);
            Vaults.MarkUsed(d, gh.Id, DateTime.UtcNow.AddHours(-2));
            w.S.SyncOn = false;
            w.tab = "passkey"; w.view = "all"; w.ShowMain(); big(w, "v23-tab-passkeys.png");
            w.selected = gh; w.ShowMain(); big(w, "v23-passkey-detail.png");
            w.StartEdit(gh, false); big(w, "v23-passkey-edit.png"); w.editing = null;
            w.tab = "login"; w.selected = d.Items.Last(i => i.Type == "login" && i.Title == "GitHub"); w.ShowMain(); big(w, "v23-login-with-passkey.png");
            w.tab = "all"; w.selected = null; w.ShowMain(); big(w, "v23-all-items.png");
        }
    }
}
