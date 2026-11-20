// Hallow Keys v1.7 - the window parts of "Watchtower-level health": Password Health with one security score (and its formula),
// tiles for every kind of problem, the two public lists (download on click or weekly if turned on), and "Fix these first" with
// one-click actions: the site's change-password page, a new password in the editor, how to add 2FA, switch an address to https.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        // the parsed lists, kept in memory and read again only when the file changes (snapshots use made-up lists)
        static Dictionary<string, Watchtower.TwoFaSite> twoFaData; static Dictionary<string, List<Watchtower.Breach>> breachData; static DateTime? twoFaStamp, breachStamp;
        public static Dictionary<string, Watchtower.TwoFaSite> SnapTwoFa; public static Dictionary<string, List<Watchtower.Breach>> SnapBreaches;
        Dictionary<string, Watchtower.TwoFaSite> TwoFaData()
        {
            if (Snap.On) return SnapTwoFa;
            var t = Watchtower.CachedOn(Watchtower.TwoFaCache); if (t == null) { twoFaData = null; twoFaStamp = null; return null; }
            if (twoFaStamp != t) { twoFaData = Watchtower.LoadTwoFa(); twoFaStamp = t; }
            return twoFaData;
        }
        Dictionary<string, List<Watchtower.Breach>> BreachData()
        {
            if (Snap.On) return SnapBreaches;
            var t = Watchtower.CachedOn(Watchtower.BreachCache); if (t == null) { breachData = null; breachStamp = null; return null; }
            if (breachStamp != t) { breachData = Watchtower.LoadBreaches(); breachStamp = t; }
            return breachData;
        }
        Watchtower.Report WatchReport() { return Watchtower.Build(vault.Data, S.OldDays, LeakedIds(), TwoFaData(), BreachData(), S.ExpiryDays, DateTime.Now); }
        string pendingPassword;   // "New password": put into the editor's password box (so the old one goes to the history on Save)

        UIElement WatchPage()
        {
            var r = WatchReport();
            ScrollViewer sv; var sp = Page("Password Health", null, out sv);
            // the score
            var sc = new DockPanel();
            var big = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            var color = r.Score >= 90 ? "#6ED28C" : r.Score >= 75 ? "#A6D96A" : r.Score >= 50 ? "#F0BE5A" : "#FF6B6B";
            var num = new TextBlock { FontSize = 40, FontWeight = FontWeights.Bold, Foreground = B(color) }; num.Inlines.Add(new System.Windows.Documents.Run(r.Score.ToString())); num.Inlines.Add(new System.Windows.Documents.Run(" / 100") { FontSize = 16, Foreground = Muted });
            big.Children.Add(num); big.Children.Add(new TextBlock { Text = Watchtower.Rating(r.Score), Foreground = B(color), FontWeight = FontWeights.SemiBold });
            DockPanel.SetDock(big, Dock.Left); sc.Children.Add(big);
            var why = new StackPanel();
            why.Children.Add(new TextBlock { Text = "Security score", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            why.Children.Add(new TextBlock { Text = Watchtower.Formula(S.OldDays), Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            if (!r.HaveTwoFa || !r.HaveBreaches) why.Children.Add(new TextBlock { Text = "Breached sites and 2FA aren't counted until you download the lists below.", Foreground = B("#F0BE5A"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            sc.Children.Add(why);
            sp.Children.Add(Card(sc));
            // the tiles
            var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
            Action<string, int, string, bool> tile = (label, n, c, known) =>
            {
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = known ? n.ToString() : "-", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = !known ? Muted : n > 0 ? B(c) : B("#6ED28C") });
                t.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
                tiles.Children.Add(new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), Child = t });
            };
            tile("Leaked", r.Basic.Leaked.Count, "#FF6B6B", true); tile("Breached sites", r.Breached.Count, "#FF6B6B", r.HaveBreaches); tile("Reused", r.Basic.Reused.Distinct().Count(), "#F0BE5A", true); tile("Weak", r.Basic.Weak.Count, "#F0BE5A", true);
            tile("2FA available", r.TwoFa.Count, "#A98BE0", r.HaveTwoFa); tile("Unsecured (http)", r.Http.Count, "#F0BE5A", true); tile("Old (" + S.OldDays + "+ days)", r.Basic.Old.Count, "#A98BE0", true); tile("Expired / expiring", r.Expired.Count + r.Expiring.Count, "#F0BE5A", true);
            sp.Children.Add(tiles);
            // fix these first
            sp.Children.Add(new TextBlock { Text = "Fix these first", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 4, 0, 6) });
            if (r.Rows.Count == 0) sp.Children.Add(new TextBlock { Text = "Nothing to fix - all good.", Foreground = B("#6ED28C"), Margin = new Thickness(0, 0, 0, 10) });
            foreach (var row in r.Rows.Take(40)) sp.Children.Add(FixRow(row));
            if (r.Rows.Count > 40) sp.Children.Add(new TextBlock { Text = "... and " + (r.Rows.Count - 40) + " more once these are done.", Foreground = Muted, Margin = new Thickness(0, 0, 0, 10) });
            // the lists, the leak check, expiring
            WatchListsCard(sp);
            HealthExtra(sp);
            ExpirySection(sp);
            return sv;
        }

        UIElement FixRow(Watchtower.Row row)
        {
            var it = row.Item; var box = new StackPanel();
            var head = new DockPanel();
            if (row.Points > 0) { var pts = new TextBlock { Text = "+" + Math.Min(100, row.Points) + (it.Type == "login" ? " for this login" : ""), Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(pts, Dock.Right); head.Children.Add(pts); }
            var ic = Icons.Make(ItemTypes.Icon(it.Type), 15, B(Program.AccentHex)); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = it.Title + (it.Username.Length > 0 ? "  -  " + it.Username : ""), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(head);
            foreach (var i in row.Issues)
            {
                var c = i.Kind == "leaked" || i.Kind == "breached" || i.Kind == "expired" ? "#FF6B6B" : i.Kind == "2fa" || i.Kind == "old" ? "#A98BE0" : "#F0BE5A";
                var line = new DockPanel { Margin = new Thickness(25, 2, 0, 0) };
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = B(c), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; line.Children.Add(dot);
                line.Children.Add(new TextBlock { Text = i.Text, Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
                box.Children.Add(line);
            }
            var bs = new WrapPanel { Margin = new Thickness(25, 8, 0, 0) };
            Func<string, string, Action, Button> btn = (label, tip, act) => { var b = new Button { Content = label, Style = St("Btn"), Margin = new Thickness(0, 0, 6, 4), ToolTip = tip }; b.Click += (s, e) => act(); bs.Children.Add(b); return b; };
            var kinds = new HashSet<string>(row.Issues.Select(i => i.Kind));
            if (it.Type == "login" && kinds.Overlaps(new[] { "leaked", "breached", "reused", "weak", "old" }))
            {
                var cp = Watchtower.ChangePasswordUrl(it);
                if (cp != null) btn("Change on site", "Opens " + cp + " - the standard address sites use for their change-password page", () => { try { System.Diagnostics.Process.Start(cp); } catch { } });
                btn("New password", "Opens the login with a new generated password in the box - Save it after you've changed it on the site (the old one goes to the history)", () =>
                {
                    pendingPassword = S.GenWords ? Generator.Passphrase(S.GenWordCount, "-", true, true) : Generator.Password(S.GenLength, S.GenLower, S.GenUpper, S.GenDigits, S.GenSymbols, S.GenAvoid);
                    view = "all"; lastSearch = ""; selected = it; ShowMain(); StartEdit(it, false);
                });
            }
            if (kinds.Contains("2fa"))
                btn("Add 2FA", "Opens the site's own instructions for turning on 2FA (from 2fa.directory) and this login's editor - paste the setup key or otpauth:// link into its 2FA box", () =>
                {
                    var doc = row.TwoFa != null ? row.TwoFa.Doc : ""; var u = Domain.Parse(doc);
                    if (u != null && u.Scheme == "https") try { System.Diagnostics.Process.Start(u.AbsoluteUri); } catch { }
                    view = "all"; lastSearch = ""; selected = it; ShowMain(); StartEdit(it, false);
                });
            if (kinds.Contains("http"))
                btn("Use https", "Changes this login's http address" + (row.Http.Count == 1 ? "" : "es") + " to https - most sites support it; the browser will then fill it only on the https page", () =>
                {
                    it.Urls = it.Urls.Select(x => row.Http.Contains(x) ? Watchtower.ToHttps(x) : x).Distinct().ToList(); it.Updated = DateTime.Now;
                    SaveVault("\"" + it.Title + "\" now uses https."); ShowMain();
                });
            btn("Show item", null, () => { view = "all"; lastSearch = ""; selected = it; editing = null; ShowMain(); });
            box.Children.Add(bs);
            return Card(box);
        }

        bool watchRunning;
        void WatchListsCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Breached sites and 2FA lists", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            box.Children.Add(new TextBlock { Text = "Downloads two public lists in full - Have I Been Pwned's list of breached sites and 2fa.directory's list of sites that offer 2FA codes - and checks your logins against them here, on this PC. Nothing about your logins (no site, username or password, not even a hash) is sent.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
            Func<string, string> when = p => { var t = Watchtower.CachedOn(p); return t == null ? "not downloaded" : "from " + t.Value.ToString("d MMM yyyy"); };
            box.Children.Add(new TextBlock { Text = "Breached sites: " + (Snap.On ? "from " + DateTime.Now.ToString("d MMM yyyy") : when(Watchtower.BreachCache)) + "   -   2FA list: " + (Snap.On ? "from " + DateTime.Now.ToString("d MMM yyyy") : when(Watchtower.TwoFaCache)), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 8) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var b = new Button { Content = "Download the lists", Style = St("Accent"), Margin = new Thickness(0, 0, 12, 0) };
            var status = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            b.Click += (s, e) => UpdateWatchLists(b, status);
            row.Children.Add(b); row.Children.Add(status); box.Children.Add(row);
            box.Children.Add(Switch("Download them again once a week by itself (while Hallow Keys is unlocked)", S.WatchWeekly, v => { S.WatchWeekly = v; S.Save(); }));
            sp.Children.Add(Card(box));
        }
        void UpdateWatchLists(Button b, TextBlock status)
        {
            if (watchRunning) return; watchRunning = true;
            if (b != null) b.IsEnabled = false; if (status != null) status.Text = "Downloading...";
            new System.Threading.Thread(() =>
            {
                string e1, e2; int n2 = Watchtower.UpdateTwoFa(out e2); int n1 = Watchtower.UpdateBreaches(out e1);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    watchRunning = false; if (b != null) b.IsEnabled = true;
                    if (n1 > 0 || n2 > 0) { S.LastWatchUpdate = DateTime.Now.ToString("o"); S.Save(); }
                    var msg = (n1 > 0 ? "Breached sites: " + n1 + " sites." : "Breached sites: couldn't download (" + e1 + ").") + " " + (n2 > 0 ? "2FA list: " + n2 + " sites." : "2FA list: couldn't download (" + e2 + ").");
                    TestLog("watch lists: " + (n1 > 0 ? "breaches ok " + n1 : "breaches failed") + ", " + (n2 > 0 ? "2fa ok " + n2 : "2fa failed"));
                    if (status != null) status.Text = msg; else if (n1 <= 0 || n2 <= 0) Toast(msg, true);
                    if (vault != null && view == "health" && status == null) ShowMain();
                    else if (vault != null && view == "health") { var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; t.Tick += (s, e) => { t.Stop(); if (view == "health" && editing == null) ShowMain(); }; t.Start(); }
                }));
            }) { IsBackground = true }.Start();
        }
        // weekly, if turned on (off by default): when unlocked and every few hours
        DispatcherTimer watchTimer;
        void AfterUnlock17()
        {
            if (watchTimer == null) { watchTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) }; watchTimer.Tick += (s, e) => MaybeWatchWeekly(); watchTimer.Start(); }
            Dispatcher.BeginInvoke(new Action(MaybeWatchWeekly), DispatcherPriority.ApplicationIdle);
        }
        void MaybeWatchWeekly()
        {
            if (!S.WatchWeekly || vault == null) return;
            DateTime last; if (DateTime.TryParse(S.LastWatchUpdate, out last) && (DateTime.Now - last).TotalDays < 7) return;
            UpdateWatchLists(null, null);
        }

        // Settings: how old is "old"
        void HealthSettingsCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Password Health", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = "A password is old after", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var days = new[] { 90, 180, 365, 730, 1095 }; var names = new[] { "90 days", "180 days", "1 year (365 days)", "2 years", "3 years" };
            var cb = new ComboBox { Width = 170, Style = St("Combo"), ItemsSource = names, SelectedIndex = Array.IndexOf(days, S.OldDays) < 0 ? 2 : Array.IndexOf(days, S.OldDays) };
            cb.SelectionChanged += (s, e) => { S.OldDays = days[cb.SelectedIndex]; S.Save(); };
            row.Children.Add(cb); box.Children.Add(row);
            box.Children.Add(Switch("Download the breached-sites and 2FA lists once a week (off: only when you click Download the lists in Password Health)", S.WatchWeekly, v => { S.WatchWeekly = v; S.Save(); }));
            sp.Children.Add(Card(box));
        }

        // ---- developer snapshots (made-up lists and items only)
        void SnapExtra17(Action<MainWindow, string> save)
        {
            SnapTwoFa = new Dictionary<string, Watchtower.TwoFaSite>(StringComparer.OrdinalIgnoreCase) { { "battle.net", new Watchtower.TwoFaSite { Name = "Battle.net", Domain = "battle.net", Doc = "https://example.com/2fa-help" } }, { "example.com", new Watchtower.TwoFaSite { Name = "Example", Domain = "example.com" } } };
            SnapBreaches = new Dictionary<string, List<Watchtower.Breach>>(StringComparer.OrdinalIgnoreCase) { { "example.net", new List<Watchtower.Breach> { new Watchtower.Breach { Name = "ExampleShop", Title = "Example Shop", Domain = "example.net", BreachDate = DateTime.Now.AddDays(-20).Date, Passwords = true } } } };
            var shop = vault.Data.Items.FirstOrDefault(i => i.Title == "Example Shop"); if (shop != null) shop.PasswordChanged = DateTime.Now.AddDays(-60);
            vault.Data.Items.Add(new Item { Title = "Example forum", Type = "login", Username = "admin", Password = "Long-made-up-pass-42!", Urls = { "http://forum.example.org" } });
            Action<MainWindow, string> tall = (w, n) => Snap.Save(w, System.IO.Path.Combine(snapDir, n), 1060, 1700);
            editing = null; view = "health"; ShowMain(); tall(this, "watchtower.png"); save(this, "watchtower-top.png");
            view = "settings"; ShowMain(); tall(this, "settings-17.png");
            SnapTwoFa = null; SnapBreaches = null; view = "health"; ShowMain(); save(this, "watchtower-no-lists.png");
        }
    }
}
