// Hallow Keys - the unlocked window: sidebar, item list with search, and an item's details / editor.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        string view = "all";        // all, fav, login, note, card, identity, folder:<name>, health, generator, transfer, settings
        Item selected, editing; bool isNew;
        TextBox search; StackPanel listPanel; Border detail; TextBlock toast; DispatcherTimer toastTimer, totpTimer;
        static readonly Dictionary<string, string> TypeIcon = ItemTypes.All.ToDictionary(t => t.Id, t => t.Icon);    // v1.6: every type (ItemTypes)
        static readonly Dictionary<string, string> TypeName = ItemTypes.All.ToDictionary(t => t.Id, t => t.Name);

        public void ShowMain()
        {
            if (vault == null) { ShowLock(); return; }
            var dock = new DockPanel();
            // ---- sidebar
            var side = new DockPanel { Width = 230, Background = (Brush)FindResource("Panel") };
            var top = new StackPanel { Margin = new Thickness(16, 16, 12, 0) };
            top.Children.Add(TitleRow(20));
            if (pending != null) { var ub = new Button { Content = "Update to v" + pending.Version.ToString(2), Style = St("Accent"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left }; ub.Click += (s, e) => InstallUpdate(); top.Children.Add(ub); }
            DockPanel.SetDock(top, Dock.Top); side.Children.Add(top);
            var bottom = new StackPanel { Margin = new Thickness(12, 0, 12, 14) };
            var lockBtn = new Button { Content = "Lock now", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Stretch }; lockBtn.Click += (s, e) => Lock("Locked.");
            bottom.Children.Add(lockBtn);
            DockPanel.SetDock(bottom, Dock.Bottom); side.Children.Add(bottom);
            var nav = new StackPanel { Margin = new Thickness(8, 14, 8, 8) };
            var items = vault.Data.Items;
            Action<string, string, string, int> navBtn = (id, icon, label, count) =>
            {
                var b = new Button { Style = St("Nav"), Tag = id == view ? "on" : null, Content = NavContent(icon, label, count) };
                b.Click += (s, e) => { view = id; selected = null; editing = null; ShowMain(); };
                nav.Children.Add(b);
            };
            navBtn("all", "list", "All items", items.Count);
            navBtn("fav", "star", "Favourites", items.Count(i => i.Favorite));
            navBtn("login", "key", "Logins", items.Count(i => i.Type == "login"));
            navBtn("note", "note-sticky", "Secure notes", items.Count(i => i.Type == "note"));
            navBtn("card", "credit-card", "Cards", items.Count(i => i.Type == "card"));
            navBtn("identity", "id-card", "Identities", items.Count(i => i.Type == "identity"));
            foreach (var t in ItemTypes.All.Where(x => !x.Legacy)) { var n = items.Count(i => i.Type == t.Id); if (n > 0 || view == t.Id) navBtn(t.Id, t.Icon, t.Plural, n); }   // v1.6: the new types once you have one
            if (vault.Data.Folders.Count > 0)
            {
                nav.Children.Add(new TextBlock { Text = "FOLDERS", FontSize = 11, Foreground = Muted, Margin = new Thickness(10, 12, 0, 4) });
                foreach (var f in vault.Data.Folders.OrderBy(x => x)) navBtn("folder:" + f, "folder", f, items.Count(i => i.Folder == f));
            }
            var tags = Organize.AllTags(vault.Data);
            if (tags.Count > 0)
            {
                nav.Children.Add(new TextBlock { Text = "TAGS", FontSize = 11, Foreground = Muted, Margin = new Thickness(10, 12, 0, 4) });
                foreach (var t in tags) { var tag = t; navBtn("tag:" + tag, "tag", tag, items.Count(i => Organize.HasTag(i, tag))); }
            }
            nav.Children.Add(new TextBlock { Text = "TOOLS", FontSize = 11, Foreground = Muted, Margin = new Thickness(10, 12, 0, 4) });
            var watch = WatchReport();   // v1.7: everything Password Health checks
            navBtn("health", "heart-pulse", "Password Health", watch.Rows.Count(x => x.Points > 0));
            navBtn("expiring", "hourglass-half", "Expiring", Expiry.Soon(vault.Data, DateTime.Now, S.ExpiryDays).Count);   // v1.6
            navBtn("generator", "dice", "Generator", -1);
            navBtn("transfer", "file-import", "Import and export", -1);
            ExtraNav(nav, navBtn);
            navBtn("settings", "gear", "Settings", -1);
            side.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            DockPanel.SetDock(side, Dock.Left); dock.Children.Add(side);
            // ---- toast line at the bottom
            toast = new TextBlock { Margin = new Thickness(16, 6, 16, 8), Foreground = B("#6ED28C"), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(toast, Dock.Bottom); dock.Children.Add(toast);
            // ---- pages
            if (view == "health") dock.Children.Add(WatchPage());
            else if (view == "generator") dock.Children.Add(GeneratorPage(null));
            else if (view == "transfer") dock.Children.Add(TransferPage());
            else if (view == "settings") dock.Children.Add(SettingsPage());
            else if (view == "expiring") dock.Children.Add(ExpiringPage());
            else if (ExtraPage(view, dock)) { }
            else
            {
                var listCol = new DockPanel { Width = 330, Margin = new Thickness(14, 14, 0, 0) };
                var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var add = new Button { Content = "New item", Style = St("Accent"), Margin = new Thickness(8, 0, 0, 0) };
                add.Click += (s, e) => NewItemMenu(add);
                DockPanel.SetDock(add, Dock.Right); bar.Children.Add(add);
                search = new TextBox { Style = St("Search"), Text = lastSearch, ToolTip = "Search titles, usernames, sites, folders, tags and notes (every word has to match; #word finds a tag). Ctrl+F" + (S.QuickSearchKey && !hotkeyTaken ? ", or " + S.QuickSearchCombo + " from anywhere" : "") + "." };
                search.TextChanged += (s, e) => { lastSearch = search.Text; FillList(); };
                search.KeyDown += (s, e) =>   // Enter opens the best match, Esc clears the search
                {
                    if (e.Key == Key.Enter) { var first = Filtered().FirstOrDefault(); if (first != null) { if (selected != first) showHistory = false; selected = first; editing = null; FillList(); ShowDetail(); } e.Handled = true; }
                    else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Text = ""; e.Handled = true; }
                };
                bar.Children.Add(search);
                DockPanel.SetDock(bar, Dock.Top); listCol.Children.Add(bar);
                listPanel = new StackPanel();
                listCol.Children.Add(new ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                DockPanel.SetDock(listCol, Dock.Left); dock.Children.Add(listCol);
                detail = new Border { Margin = new Thickness(14, 14, 14, 0), Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12) };
                dock.Children.Add(detail);
                FillList(); ShowDetail();
            }
            root.Child = dock;
        }
        partial void ExtraNavImpl(StackPanel nav, Action<string, string, string, int> navBtn);
        void ExtraNav(StackPanel nav, Action<string, string, string, int> navBtn) { ExtraNavImpl(nav, navBtn); }
        bool ExtraPage(string v, DockPanel dock) { bool handled = false; ExtraPageImpl(v, dock, ref handled); return handled; }
        partial void ExtraPageImpl(string v, DockPanel dock, ref bool handled);
        HashSet<string> LeakedIds() { HashSet<string> h = null; LeakedImpl(ref h); return h; }
        partial void LeakedImpl(ref HashSet<string> ids);
        string lastSearch = "";

        UIElement NavContent(string icon, string label, int count)
        {
            var d = new DockPanel { Width = 190 };
            if (count >= 0) { var c = new TextBlock { Text = count.ToString(), Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(c, Dock.Right); d.Children.Add(c); }
            var ic = Icons.Make(icon, 14, (Brush)FindResource("Muted")); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; d.Children.Add(ic);
            d.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            return d;
        }
        public void Toast(string text) { Toast(text, false); }
        public void Toast(string text, bool bad)
        {
            if (toast == null) { if (lockStatus != null) lockStatus.Text = text; return; }
            toast.Text = text; toast.Foreground = bad ? B("#F0BE5A") : B("#6ED28C");
            if (toastTimer == null) { toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) }; toastTimer.Tick += (s, e) => { toastTimer.Stop(); if (toast != null) toast.Text = ""; }; }
            toastTimer.Stop(); toastTimer.Start();
        }
        void SaveVault(string msg)
        {
            try { vault.Save(); WriteStatus(); AfterSave(); if (msg != null) Toast(msg); }
            catch (Exception e) { Toast("Couldn't save the vault: " + e.Message, true); }
        }
        partial void AfterSaveImpl();
        void AfterSave() { AfterSaveImpl(); }

        IEnumerable<Item> Filtered()
        {
            var items = vault.Data.Items.AsEnumerable();
            if (view == "fav") items = items.Where(i => i.Favorite);
            else if (view.StartsWith("folder:")) { var f = view.Substring(7); items = items.Where(i => i.Folder == f); }
            else if (view.StartsWith("tag:")) { var t = view.Substring(4); items = items.Where(i => Organize.HasTag(i, t)); }
            else if (TypeName.ContainsKey(view)) items = items.Where(i => i.Type == view);
            var q = (lastSearch ?? "").Trim();
            if (q.Length > 0)   // titles, usernames, sites, folders, tags and notes - never passwords; every word has to match
            {
                items = items.Where(i => Organize.Matches(i, q, TypeName.ContainsKey(i.Type) ? TypeName[i.Type] : ""));
                return items.OrderBy(i => Organize.Rank(i, q)).ThenByDescending(i => i.Favorite).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
            }
            return items.OrderByDescending(i => i.Favorite).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
        }
        void FillList()
        {
            if (listPanel == null) return;
            listPanel.Children.Clear();
            var list = Filtered().ToList();
            if (list.Count == 0) listPanel.Children.Add(new TextBlock { Text = vault.Data.Items.Count == 0 ? "Nothing here yet. Add an item with New item, or import from NordPass in Import and export." : "Nothing matches.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 0) });
            foreach (var it in list)
            {
                var item = it;
                var b = new Button { Style = St("Nav"), Tag = selected == it ? "on" : null, Padding = new Thickness(10, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var d = new DockPanel();
                var ic = Icons.Make(ItemTypes.Icon(it.Type), 18, B(Program.AccentHex)); ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                d.Children.Add(ic);
                if (it.Favorite) { var st = Icons.Make("star", 11, B("#F0BE5A")); st.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(st, Dock.Right); d.Children.Add(st); }
                var tx = new StackPanel();
                tx.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                tx.Children.Add(new TextBlock { Text = Subtitle(it), Foreground = Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
                d.Children.Add(tx); b.Content = d;
                b.Click += (s, e) => { if (selected != item) showHistory = false; selected = item; editing = null; FillList(); ShowDetail(); };
                listPanel.Children.Add(b);
            }
        }
        static string Subtitle(Item i)
        {
            if (i.Type == "login") return string.Join(" - ", new[] { i.Username, i.Host }.Where(x => !string.IsNullOrEmpty(x)));
            if (i.Type == "card") return i.CardNumber.Length >= 4 ? "**** " + i.CardNumber.Replace(" ", "").Substring(Math.Max(0, i.CardNumber.Replace(" ", "").Length - 4)) : i.CardHolder;
            if (i.Type == "identity") return i.FullName;
            if (ItemTypes.IsNew(i.Type)) { var s = ItemTypes.Subtitle(i); return s.Length > 0 ? s : i.Folder; }
            return i.Folder;
        }
        void NewItemMenu(Button anchor)
        {
            var m = new ContextMenu();
            Action<Item> start = ni => { ni.Folder = view.StartsWith("folder:") ? view.Substring(7) : ""; ni.Favorite = view == "fav"; if (view.StartsWith("tag:")) ni.Tags.Add(view.Substring(4)); StartEdit(ni, true); };
            foreach (var td in ItemTypes.All)
            {
                var t = td.Id; var mi = new MenuItem { Header = td.Name, Icon = Icons.Make(td.Icon, 13, Muted) }; mi.Click += (s, e) => start(new Item { Type = t }); m.Items.Add(mi);
                if (t == "identity") m.Items.Add(new Separator());
            }
            if (vault.Data.Templates.Count > 0)   // v1.6: your own templates
            {
                m.Items.Add(new Separator());
                foreach (var tp in vault.Data.Templates.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) { var tpl = tp; var mi = new MenuItem { Header = tpl.Name + "  (template)", Icon = Icons.Make("layer-group", 13, Muted) }; mi.Click += (s, e) => start(ItemTypes.FromTemplate(tpl)); m.Items.Add(mi); }
            }
            m.PlacementTarget = anchor; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
        }

        // ---- details
        void ShowDetail()
        {
            if (totpTimer != null) totpTimer.Stop(); totpTicks.Clear();
            if (detail == null) return;
            if (editing != null) { detail.Child = Editor(editing); return; }
            if (selected == null || !vault.Data.Items.Contains(selected)) { selected = null; detail.Child = new TextBlock { Text = "Pick an item on the left.", Foreground = Muted, Margin = new Thickness(20) }; return; }
            var it = selected;
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var fav = new Button { Content = it.Favorite ? "Unfavourite" : "Favourite", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) };
            fav.Click += (s, e) => { it.Favorite = !it.Favorite; it.Updated = DateTime.Now; SaveVault(null); FillList(); ShowDetail(); };
            var ed = new Button { Content = "Edit", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; ed.Click += (s, e) => StartEdit(it, false);
            var del = new Button { Content = "Delete", Style = St("Btn"), ToolTip = "Moves it to the trash - you can restore it for " + Organize.TrashDays + " days" };
            del.Click += (s, e) => { TrashItem(it); ShowMain(); };
            btns.Children.Add(fav); btns.Children.Add(ed); btns.Children.Add(del);
            DockPanel.SetDock(btns, Dock.Right); head.Children.Add(btns);
            var ht = new StackPanel();
            ht.Children.Add(new TextBlock { Text = it.Title, FontSize = 19, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            ht.Children.Add(new TextBlock { Text = ItemTypes.Name(it.Type) + (it.Folder.Length > 0 ? "  -  " + it.Folder : ""), Foreground = Muted });
            if (it.Tags.Count > 0)
            {
                var tagRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var t in it.Tags) { var tag = t; var c = new Button { Content = "#" + tag, Style = St("Btn"), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 4), FontSize = 11.5, ToolTip = "Show everything tagged " + tag }; c.Click += (s, e) => { view = "tag:" + tag; lastSearch = ""; ShowMain(); }; tagRow.Children.Add(c); }
                ht.Children.Add(tagRow);
            }
            head.Children.Add(ht); sp.Children.Add(head);
            if (it.Type == "login")
            {
                Field(sp, "Username", it.Username, false);
                Field(sp, "Password", it.Password, true, it.Password.Length > 0 ? Health.Rating(Health.Bits(it.Password)) : null);
                var spec = Totp.Parse(it.Totp);
                if (spec != null) TotpRow(sp, spec);
                foreach (var u in it.Urls) UrlRow(sp, u);
                AppsRow(sp, it);   // v1.5
                HistorySection(sp, it);
            }
            else if (it.Type == "card")
            {
                Field(sp, "Name on card", it.CardHolder, false); Field(sp, "Card number", it.CardNumber, true); Field(sp, "Expiry", it.Expiry, false); Field(sp, "Security code", it.Cvc, true); Field(sp, "PIN", it.Pin, true);
            }
            else if (it.Type == "identity")
            {
                Field(sp, "Full name", it.FullName, false); Field(sp, "Email", it.Email, false); Field(sp, "Phone", it.Phone, false); Field(sp, "Address", it.Address, false);
                Field(sp, "City", it.City, false); Field(sp, "Postcode", it.PostCode, false); Field(sp, "Country", it.Country, false);
            }
            FieldsDetail(sp, it);   // v1.6: the new types' fields and your own fields
            if (it.Notes.Length > 0)
            {
                sp.Children.Add(new TextBlock { Text = it.Type == "note" ? "Note" : "Notes", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) });
                sp.Children.Add(new TextBox { Style = St("Area"), Text = it.Notes, IsReadOnly = true, MaxHeight = 220 });
            }
            if (it.Files.Count > 0)
            {
                sp.Children.Add(new TextBlock { Text = "Files", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 4) });
                foreach (var f in it.Files) { var file = f; var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var sv = new Button { Content = "Save file", Style = St("Btn") }; sv.Click += (s, e) => SaveAttachment(file); DockPanel.SetDock(sv, Dock.Right); row.Children.Add(sv); row.Children.Add(new TextBlock { Text = f.Name + "  (" + Size(f.Data.Length) + ")", VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(row); }
            }
            FieldHistorySection(sp, it);   // v1.6
            sp.Children.Add(new TextBlock { Text = "Changed " + it.Updated.ToString("d MMM yyyy") + (it.Type == "login" && it.Password.Length > 0 ? "  -  password set " + it.PasswordChanged.ToString("d MMM yyyy") : ""), Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 16, 0, 0) });
            DetailExtraButtons(sp, it);    // v1.6: Duplicate, Save as template
            detail.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        static string Size(long b) { return b < 1024 ? b + " B" : b < 1048576 ? (b / 1024) + " KB" : (b / 1048576.0).ToString("0.0") + " MB"; }
        // one field: label, value (hidden for secrets until Show), Copy
        void Field(StackPanel sp, string label, string value, bool secret, string note = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            sp.Children.Add(new TextBlock { Text = label + (note != null ? "  -  " + note : ""), Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var row = new DockPanel();
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            var tb = new TextBlock { Text = secret ? new string('\u2022', Math.Min(16, Math.Max(8, value.Length))) : value, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = secret ? new FontFamily("Consolas") : FontFamily };
            if (secret)
            {
                var show = new Button { Content = "Show", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) };
                show.Click += (s, e) => { bool shown = (string)show.Content == "Hide"; tb.Text = shown ? new string('\u2022', Math.Min(16, Math.Max(8, value.Length))) : value; show.Content = shown ? "Show" : "Hide"; };
                bs.Children.Add(show);
            }
            var copy = new Button { Content = "Copy", Style = St("Btn") };
            copy.Click += (s, e) => { if (SafeClip.Copy(value)) Toast(label + " copied - the clipboard clears in " + SafeClip.ClearSeconds + " seconds."); };
            bs.Children.Add(copy);
            DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs); row.Children.Add(tb);
            sp.Children.Add(row);
        }
        void UrlRow(StackPanel sp, string url, string label = "Website")
        {
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var row = new DockPanel(); var u = Domain.Parse(url);
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            if (u != null) { var open = new Button { Content = "Open", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) }; open.Click += (s, e) => { try { System.Diagnostics.Process.Start(u.AbsoluteUri); } catch { } }; bs.Children.Add(open); }
            var copy = new Button { Content = "Copy", Style = St("Btn") }; copy.Click += (s, e) => { Clipboard.SetText(url); Toast("Address copied."); }; bs.Children.Add(copy);
            DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs); row.Children.Add(new TextBlock { Text = url, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(row);
        }
        readonly List<Action> totpTicks = new List<Action>();   // v1.6: several live codes on one item (your own 2FA fields), one timer
        void TotpRow(StackPanel sp, Totp.Spec spec, string label = "2FA code")
        {
            sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var row = new DockPanel();
            var code = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var left = new TextBlock { Foreground = Muted, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var copy = new Button { Content = "Copy", Style = St("Btn") };
            Action tick = () => { var now = Totp.Now(); var c = Totp.Code(spec, now); code.Text = c.Length == 6 ? c.Substring(0, 3) + " " + c.Substring(3) : c.Substring(0, 4) + " " + c.Substring(4); left.Text = (spec.Period - now % spec.Period) + " s"; };
            copy.Click += (s, e) => { if (SafeClip.Copy(Totp.Code(spec, Totp.Now()))) Toast("2FA code copied."); };
            DockPanel.SetDock(copy, Dock.Right); row.Children.Add(copy);
            var lp = new StackPanel { Orientation = Orientation.Horizontal }; lp.Children.Add(code); lp.Children.Add(left); row.Children.Add(lp);
            sp.Children.Add(row); tick(); totpTicks.Add(tick);
            if (totpTimer == null) { totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; totpTimer.Tick += (s, e) => { foreach (var t in totpTicks.ToList()) t(); }; }
            totpTimer.Start();
        }
        void SaveAttachment(Attachment f)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = f.Name, Title = "Save the file (it's stored unencrypted wherever you save it)" };
            if (dlg.ShowDialog(this) != true) return;
            try { System.IO.File.WriteAllBytes(dlg.FileName, f.Data); Toast("Saved " + System.IO.Path.GetFileName(dlg.FileName) + "."); } catch (Exception e) { Toast("Couldn't save: " + e.Message, true); }
        }

        // ---- editor
        void StartEdit(Item it, bool fresh)
        {
            isNew = fresh;
            // edit a copy (every field, including ones this version doesn't know), so Cancel leaves the item as it was
            editing = ItemTypes.Clone(it, false); ItemTypes.Ensure(editing);
            if (detail == null) { view = it.Type; ShowMain(); }
            detail.Child = Editor(editing);
        }
        List<Action> editorFields = new List<Action>();   // v1.6: the open editor's box readers (run before the editor is rebuilt)
        UIElement Editor(Item e)
        {
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(new TextBlock { Text = (isNew ? "New " : "Edit ") + (ItemTypes.Get(e.Type) != null ? ItemTypes.Get(e.Type).Lower : "item"), FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            var fields = new List<Action>(); editorFields = fields;
            Func<string, string, Action<string>, TextBox> text = (label, val, set) =>
            {
                sp.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
                var tb = new TextBox { Style = St("Field"), Text = val }; sp.Children.Add(tb); fields.Add(() => set(tb.Text.Trim())); return tb;
            };
            var title = text("Title", e.Title, v => e.Title = v);
            if (e.Type == "login")
            {
                text("Username or email", e.Username, v => e.Username = v);
                sp.Children.Add(new TextBlock { Text = "Password", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
                var prow = new DockPanel();
                var gen = new Button { Content = "Generate", Style = St("Btn"), Margin = new Thickness(6, 0, 0, 0) };
                var showP = new Button { Content = "Show", Style = St("Btn"), Margin = new Thickness(6, 0, 0, 0) };
                DockPanel.SetDock(gen, Dock.Right); DockPanel.SetDock(showP, Dock.Right); prow.Children.Add(gen); prow.Children.Add(showP);
                var grid = new Grid();
                var pb = new PasswordBox { Style = St("Pwd"), Password = e.Password }; var tb = new TextBox { Style = St("Field"), Text = e.Password, Visibility = Visibility.Collapsed, FontFamily = new FontFamily("Consolas") };
                grid.Children.Add(pb); grid.Children.Add(tb); prow.Children.Add(grid); sp.Children.Add(prow);
                var meter = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) }; sp.Children.Add(meter);
                Action<string> rate = p =>
                {
                    var b = Health.Bits(p); int reused = p.Length == 0 ? 0 : vault.Data.Items.Count(x => x.Id != e.Id && x.Type == "login" && x.Password == p);
                    bool before = p.Length > 0 && p != e.Password && e.OldPasswords.Contains(p);
                    meter.Text = p.Length == 0 ? "" : Health.Rating(b) + " (about " + (int)b + " bits)" + (reused > 0 ? " - also used by " + reused + " other login" + (reused == 1 ? "" : "s") : "") + (before ? " - you've used this one here before" : "");
                    meter.Foreground = b < 40 || reused > 0 || before ? B("#F0BE5A") : b < 60 ? Muted : B("#6ED28C");
                };
                pb.PasswordChanged += (s, a) => { if (tb.Visibility != Visibility.Visible) { tb.Text = pb.Password; rate(pb.Password); } };
                tb.TextChanged += (s, a) => { if (tb.Visibility == Visibility.Visible) { pb.Password = tb.Text; rate(tb.Text); } };
                showP.Click += (s, a) => { bool vis = tb.Visibility == Visibility.Visible; tb.Visibility = vis ? Visibility.Collapsed : Visibility.Visible; pb.Visibility = vis ? Visibility.Visible : Visibility.Collapsed; showP.Content = vis ? "Show" : "Hide"; };
                gen.Click += (s, a) => { var p = S.GenWords ? Generator.Passphrase(S.GenWordCount, "-", true, true) : Generator.Password(S.GenLength, S.GenLower, S.GenUpper, S.GenDigits, S.GenSymbols, S.GenAvoid); pb.Password = p; tb.Text = p; rate(p); };
                rate(e.Password);
                if (pendingPassword != null) { var np0 = pendingPassword; pendingPassword = null; pb.Password = np0; tb.Text = np0; rate(np0); }   // v1.7: "New password" from Password Health
                fields.Add(() => { var np = pb.Password; if (np != e.Password) { Organize.RecordOldPassword(e, e.Password, DateTime.Now); e.Password = np; e.PasswordChanged = DateTime.Now; } });
                UrlsEditor(sp, e, fields);   // v1.6: several websites, one box each
                text("2FA (an otpauth:// link or the setup key)", e.Totp, v => e.Totp = v);
                AppsEditor(sp, e, fields);   // v1.5
            }
            else if (e.Type == "card")
            {
                text("Name on card", e.CardHolder, v => e.CardHolder = v); text("Card number", e.CardNumber, v => e.CardNumber = v);
                text("Expiry (MM/YY)", e.Expiry, v => e.Expiry = v); text("Security code", e.Cvc, v => e.Cvc = v); text("PIN", e.Pin, v => e.Pin = v);
            }
            else if (e.Type == "identity")
            {
                text("Full name", e.FullName, v => e.FullName = v); text("Email", e.Email, v => e.Email = v); text("Phone", e.Phone, v => e.Phone = v);
                text("Address", e.Address, v => e.Address = v); text("City", e.City, v => e.City = v); text("Postcode", e.PostCode, v => e.PostCode = v); text("Country", e.Country, v => e.Country = v);
            }
            TypeFieldsEditor(sp, e);     // v1.6: the new types' own fields
            CustomFieldsEditor(sp, e);   // v1.6: your own fields (any item)
            var notes = text(e.Type == "note" ? "Note" : "Notes", e.Notes, v => e.Notes = v); notes.Style = St("Area"); notes.AcceptsReturn = true;
            // folder and favourite
            sp.Children.Add(new TextBlock { Text = "Folder (type a new name to make one)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var folder = new TextBox { Style = St("Field"), Text = e.Folder };
            sp.Children.Add(folder); fields.Add(() => e.Folder = (folder.Text ?? "").Trim());
            if (vault.Data.Folders.Count > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var fn in vault.Data.Folders.OrderBy(x => x)) { var name = fn; var c = new Button { Content = name, Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4) }; c.Click += (s, a) => folder.Text = name; chips.Children.Add(c); }
                var none = new Button { Content = "No folder", Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4) }; none.Click += (s, a) => folder.Text = ""; chips.Children.Add(none);
                sp.Children.Add(chips);
            }
            // tags (v1.3)
            sp.Children.Add(new TextBlock { Text = "Tags (separate them with commas)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var tagBox = new TextBox { Style = St("Field"), Text = string.Join(", ", e.Tags) };
            sp.Children.Add(tagBox); fields.Add(() => e.Tags = Organize.ParseTags(tagBox.Text));
            var known = Organize.AllTags(vault.Data);
            if (known.Count > 0)
            {
                var tchips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var tn in known.Take(30))
                {
                    var name = tn; var c = new Button { Content = "#" + name, Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4) };
                    c.Click += (s, a) => { var cur = Organize.ParseTags(tagBox.Text); if (!cur.Any(x => string.Equals(x, name, StringComparison.CurrentCultureIgnoreCase))) { cur.Add(name); tagBox.Text = string.Join(", ", cur); } };
                    tchips.Children.Add(c);
                }
                sp.Children.Add(tchips);
            }
            var favC = new CheckBox { Content = new TextBlock { Text = "Favourite" }, IsChecked = e.Favorite, Style = St("Switch"), Margin = new Thickness(0, 10, 0, 0) }; sp.Children.Add(favC); fields.Add(() => e.Favorite = favC.IsChecked == true);
            // files
            sp.Children.Add(new TextBlock { Text = "Files (up to 10 MB each, kept encrypted in the vault)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 12, 0, 3) });
            var filesPanel = new StackPanel(); sp.Children.Add(filesPanel);
            Action fillFiles = null;
            fillFiles = () =>
            {
                filesPanel.Children.Clear();
                foreach (var f in e.Files.ToList()) { var file = f; var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var rm = new Button { Content = "Remove", Style = St("Btn") }; rm.Click += (s, a) => { e.Files.Remove(file); fillFiles(); }; DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm); row.Children.Add(new TextBlock { Text = f.Name + "  (" + Size(f.Data.Length) + ")", VerticalAlignment = VerticalAlignment.Center }); filesPanel.Children.Add(row); }
                var add = new Button { Content = "Add file", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                add.Click += (s, a) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Add a file to this item" }; if (dlg.ShowDialog(this) != true) return;
                    var fi = new System.IO.FileInfo(dlg.FileName); if (fi.Length > Vault.MaxFileBytes) { Toast("That file is over 10 MB.", true); return; }
                    e.Files.Add(new Attachment { Name = fi.Name, Data = System.IO.File.ReadAllBytes(fi.FullName) }); fillFiles();
                };
                filesPanel.Children.Add(add);
            };
            fillFiles();
            // save / cancel
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var save = new Button { Content = "Save", Style = St("Accent"), MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Style = St("Btn") };
            save.Click += (s, a) =>
            {
                foreach (var f in fields) f();
                var problem = PrepareFields(e); if (problem != null) { Toast(problem, true); return; }   // v1.6: dates, 2FA fields, SSH fingerprint
                if (e.Title.Length == 0) e.Title = e.Type == "login" ? (Domain.HostOf(e.Urls.FirstOrDefault()) ?? e.Username) : ItemTypes.IsNew(e.Type) ? ItemTypes.Subtitle(e).Split(new[] { " - " }, StringSplitOptions.None)[0] : ItemTypes.Name(e.Type);
                if (e.Title.Length == 0) e.Title = ItemTypes.Name(e.Type);
                if (e.Totp.Length > 0 && Totp.Parse(e.Totp) == null) { Toast("The 2FA setting isn't an otpauth:// link or a valid setup key.", true); return; }
                e.Updated = DateTime.Now;
                if (e.Folder.Length > 0 && !vault.Data.Folders.Contains(e.Folder)) vault.Data.Folders.Add(e.Folder);
                var old = vault.Data.Items.FirstOrDefault(x => x.Id == e.Id);
                ItemTypes.RecordFieldChanges(old, e, DateTime.Now);   // v1.6: hidden fields keep their old values
                if (old != null) vault.Data.Items[vault.Data.Items.IndexOf(old)] = e; else vault.Data.Items.Add(e);
                selected = e; editing = null; SaveVault("Saved."); ShowMain();
            };
            cancel.Click += (s, a) => { editing = null; ShowDetail(); };
            bar.Children.Add(save); bar.Children.Add(cancel); sp.Children.Add(bar);
            Dispatcher.BeginInvoke(new Action(() => title.Focus()), DispatcherPriority.Input);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
