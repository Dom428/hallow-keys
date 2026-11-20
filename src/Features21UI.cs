// Hallow Keys 2.1 - the window parts of "NordPass look, plus vaults":
//  - the main list as a table: search bar (Ctrl+F), a big title with Create item, category tabs (All items, Logins, Secure notes, Cards,
//    Contact info, Documents, Other - Passkeys stays hidden until 2.3), sortable columns (Title, Last used, Folder, Modified), tick boxes
//    for several items at once (move to a folder or vault, tag, favourite, trash), keyboard (Up/Down, Space, Ctrl+A, Delete, Esc)
//  - site icons or a coloured letter tile per login; the details panel opens on the right
//  - the vault switcher, the Vaults and travel page, Travel Mode, the site icons setting, and "last used" (see Vaults21.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        string tab = "all", otherType, curVault;          // category tab, one type inside Other, the vault picked in the sidebar (null = every vault)
        string sortCol; bool sortDesc;                     // null = favourites first, then by title
        readonly HashSet<string> picked = new HashSet<string>(); string pickAnchor;
        ColumnDefinition detailCol; Border headerHost; WrapPanel bulkBar; Border bulkBox; ScrollViewer listScroll;
        List<Item> lastList = new List<Item>();
        readonly Dictionary<string, FrameworkElement> rowById = new Dictionary<string, FrameworkElement>();
        Dictionary<string, DateTime> usageMap = new Dictionary<string, DateTime>();

        bool Travel { get { return S.TravelMode; } }
        // what this PC shows (Travel Mode), and that narrowed to the vault picked in the sidebar
        VaultData Shown() { return vault == null ? new VaultData() : Vaults.View(vault.Data, Travel, null); }
        VaultData Scope()
        {
            if (vault == null) return new VaultData();
            if (curVault != null) { var v = Vaults.Get(vault.Data, curVault); if (v == null || v.Deleted || !VaultVisible(v)) curVault = null; }
            return Vaults.View(vault.Data, Travel, curVault);
        }
        bool IsShown(Item it) { return vault != null && it != null && Vaults.Shown(vault.Data, it, Travel); }
        bool VaultVisible(VaultInfo v) { return v != null && (!Travel || v.Travel); }
        bool SeveralShown() { return VisibleVaults().Count > 1; }
        List<VaultInfo> VisibleVaults() { return Vaults.Live(vault.Data).Where(VaultVisible).ToList(); }
        VaultData ExportData() { return Scope(); }
        string ExportWhat()
        {
            if (curVault != null) return "the " + Vaults.Get(vault.Data, curVault).Name + " vault";
            if (Travel) return "the vaults shown while travelling";
            return SeveralShown() ? "every vault" : "your vault";
        }
        bool DetailOpen() { return editing != null || (selected != null && vault != null && vault.Data.Items.Contains(selected) && IsShown(selected)); }

        // older views by type (Logins, SSH keys...) are category tabs now
        void NormalizeView()
        {
            if (TypeName.ContainsKey(view)) { tab = Vaults.TabOfType(view); otherType = tab == "other" ? view : null; view = "all"; }
            if (tab == "passkey" && !Vaults.PasskeysReady) tab = "all";
            if (!Vaults.Tabs.Contains(tab)) tab = "all";
        }
        string ListTitle()
        {
            if (view == "fav") return "Favourites";
            if (view.StartsWith("folder:")) return view.Substring(7);
            if (view.StartsWith("tag:")) return "#" + view.Substring(4);
            if (curVault != null) return Vaults.Get(vault.Data, curVault).Name;
            return SeveralShown() ? "All vaults" : "Vault";
        }

        // ---- the list area: search, title + Create item, tabs, bulk bar, column headings, rows; the details panel on the right
        void ListArea(DockPanel dock)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailCol = new ColumnDefinition { Width = new GridLength(0) }; grid.ColumnDefinitions.Add(detailCol);
            var main = new DockPanel { Margin = new Thickness(22, 16, 16, 0) };
            // search bar with the Ctrl+F hint
            var sg = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            search = new TextBox { Style = St("Search"), Text = lastSearch, ToolTip = "Search titles, usernames, sites, folders, tags and notes (every word has to match; #word finds a tag). Ctrl+F" + (S.QuickSearchKey && !hotkeyTaken ? ", or " + S.QuickSearchCombo + " from anywhere" : "") + ". Up/Down pick an item." };
            var mag = Icons.Make("magnifying-glass", 13, Muted); mag.HorizontalAlignment = HorizontalAlignment.Left; mag.VerticalAlignment = VerticalAlignment.Center; mag.Margin = new Thickness(13, 0, 0, 0); mag.IsHitTestVisible = false;
            var ph = new TextBlock { Text = "Search " + (curVault != null ? Vaults.Get(vault.Data, curVault).Name : SeveralShown() ? "all vaults" : "your vault"), Foreground = Muted, Margin = new Thickness(36, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = lastSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            var hint = new Border { Background = (Brush)FindResource("PanelHi"), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false, Child = new TextBlock { Text = "Ctrl+F", FontSize = 11, Foreground = Muted } };
            search.TextChanged += (s, e) => { lastSearch = search.Text; ph.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; FillList(); };
            search.KeyDown += (s, e) =>   // Enter opens the best match, Esc clears the search
            {
                if (e.Key == Key.Enter) { var first = lastList.FirstOrDefault(); if (first != null) { if (selected != first) showHistory = false; selected = first; editing = null; FillList(); ShowDetail(); } e.Handled = true; }
                else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Text = ""; e.Handled = true; }
            };
            sg.Children.Add(search); sg.Children.Add(mag); sg.Children.Add(ph); sg.Children.Add(hint);
            DockPanel.SetDock(sg, Dock.Top); main.Children.Add(sg);
            // title and Create item
            var trow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var create = new Button { Style = St("Accent"), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(14, 7, 16, 7) };
            var cc = new StackPanel { Orientation = Orientation.Horizontal }; var pl = Icons.Make("plus", 12, (Brush)FindResource("Bg")); pl.Margin = new Thickness(0, 0, 8, 0); pl.VerticalAlignment = VerticalAlignment.Center; cc.Children.Add(pl); cc.Children.Add(new TextBlock { Text = "Create item" }); create.Content = cc;
            create.Click += (s, e) => NewItemMenu(create);
            DockPanel.SetDock(create, Dock.Right); trow.Children.Add(create);
            var tt = new StackPanel();
            tt.Children.Add(new TextBlock { Text = ListTitle(), FontSize = 26, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            var scope = Scope(); var basis = BaseItems().ToList();
            int nv = curVault == null && SeveralShown() ? VisibleVaults().Count : 0;
            tt.Children.Add(new TextBlock { Text = basis.Count + " item" + (basis.Count == 1 ? "" : "s") + (nv > 1 ? " in " + nv + " vaults" : "") + (Travel ? "  -  Travel Mode: only vaults safe for travel" : ""), Foreground = Travel ? B("#F0BE5A") : Muted });
            trow.Children.Add(tt);
            DockPanel.SetDock(trow, Dock.Top); main.Children.Add(trow);
            // category tabs
            var tabs = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var t in Vaults.Tabs)
            {
                if (t == "passkey" && !Vaults.PasskeysReady) continue;   // 2.3
                var id = t; int n = basis.Count(i => Vaults.InTab(i, id));
                var c = new StackPanel { Orientation = Orientation.Horizontal };
                c.Children.Add(new TextBlock { Text = Vaults.TabName(id), FontWeight = tab == id ? FontWeights.SemiBold : FontWeights.Normal });
                if (tab == id) c.Children.Add(new TextBlock { Text = " " + n, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 1, 0, 0) });   // the count on the open tab only (they fit next to the details panel)
                var b = new Button { Style = St("Tab"), Tag = tab == id ? "on" : null, Content = c, ToolTip = n + " item" + (n == 1 ? "" : "s") };
                b.Click += (s, e) => { tab = id; otherType = null; picked.Clear(); ShowMain(); };
                tabs.Children.Add(b);
            }
            var tabLine = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = tabs, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(tabLine, Dock.Top); main.Children.Add(tabLine);
            if (tab == "other")   // the types inside Other, as chips
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                foreach (var g in basis.Where(i => Vaults.InTab(i, "other")).GroupBy(i => i.Type).OrderBy(g => ItemTypes.Name(g.Key)))
                {
                    var type = g.Key; var on = otherType == type;
                    var ch = new Button { Content = (ItemTypes.Get(type) != null ? ItemTypes.Get(type).Plural : type) + "  " + g.Count(), Style = St(on ? "Accent" : "Btn"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 11.5 };
                    ch.Click += (s, e) => { otherType = on ? null : type; picked.Clear(); ShowMain(); };
                    chips.Children.Add(ch);
                }
                if (chips.Children.Count > 0) { DockPanel.SetDock(chips, Dock.Top); main.Children.Add(chips); }
            }
            // the bulk bar (shown when items are ticked)
            bulkBar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            bulkBox = new Border { Background = (Brush)FindResource("PanelHi"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8), Child = bulkBar, Visibility = Visibility.Collapsed };
            DockPanel.SetDock(bulkBox, Dock.Top); main.Children.Add(bulkBox);
            headerHost = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(headerHost, Dock.Top); main.Children.Add(headerHost);
            listPanel = new StackPanel();
            listScroll = new ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            main.Children.Add(listScroll);
            Grid.SetColumn(main, 0); grid.Children.Add(main);
            detail = new Border { Margin = new Thickness(0, 14, 14, 0), Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), Visibility = Visibility.Collapsed };
            Grid.SetColumn(detail, 1); grid.Children.Add(detail);
            dock.Children.Add(grid);
            FillList(); ShowDetail();
        }
        void ShowDetailPanel(bool on)
        {
            if (detail == null) return;
            detail.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (detailCol != null) detailCol.Width = new GridLength(on ? 420 : 0);
        }

        // the scope with the sidebar's choice (Vault, Favourites, a folder, a tag), before the tab and the search
        IEnumerable<Item> BaseItems()
        {
            var items = Scope().Items.AsEnumerable();
            if (view == "fav") items = items.Where(i => i.Favorite);
            else if (view.StartsWith("folder:")) { var f = view.Substring(7); items = items.Where(i => i.Folder == f); }
            else if (view.StartsWith("tag:")) { var t = view.Substring(4); items = items.Where(i => Organize.HasTag(i, t)); }
            return items;
        }
        IEnumerable<Item> Filtered()
        {
            var items = BaseItems().Where(i => Vaults.InTab(i, tab));
            if (tab == "other" && otherType != null) items = items.Where(i => i.Type == otherType);
            var q = (lastSearch ?? "").Trim();
            if (q.Length > 0)   // titles, usernames, sites, folders, tags and notes - never passwords; every word has to match
            {
                items = items.Where(i => Organize.Matches(i, q, TypeName.ContainsKey(i.Type) ? TypeName[i.Type] : ""));
                if (sortCol == null) return items.OrderBy(i => Organize.Rank(i, q)).ThenByDescending(i => i.Favorite).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
            }
            return Sorted(items);
        }
        IEnumerable<Item> Sorted(IEnumerable<Item> items)
        {
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            Func<Item, DateTime> used = i => { DateTime t; return usageMap.TryGetValue(i.Id, out t) ? t : DateTime.MinValue; };
            switch (sortCol)
            {
                case "title": return sortDesc ? items.OrderByDescending(i => i.Title, cmp) : items.OrderBy(i => i.Title, cmp);
                case "used": return (sortDesc ? items.OrderBy(i => used(i) == DateTime.MinValue ? 1 : 0).ThenByDescending(used) : items.OrderBy(i => used(i) == DateTime.MinValue ? 1 : 0).ThenBy(used)).ThenBy(i => i.Title, cmp);   // never used: last either way
                case "folder": return (sortDesc ? items.OrderBy(i => i.Folder.Length == 0 ? 1 : 0).ThenByDescending(i => i.Folder, cmp) : items.OrderBy(i => i.Folder.Length == 0 ? 1 : 0).ThenBy(i => i.Folder, cmp)).ThenBy(i => i.Title, cmp);
                case "modified": return (sortDesc ? items.OrderByDescending(i => i.Updated) : items.OrderBy(i => i.Updated)).ThenBy(i => i.Title, cmp);
                default: return items.OrderByDescending(i => i.Favorite).ThenBy(i => i.Title, cmp);
            }
        }
        Grid Columns(bool narrow)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(narrow ? 96 : 110) });
            if (!narrow) { g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); }
            return g;
        }
        void FillList()
        {
            if (listPanel == null || vault == null) return;
            listPanel.Children.Clear(); rowById.Clear();
            usageMap = Vaults.UsageMap(vault.Data);
            bool narrow = DetailOpen(), showVault = curVault == null && SeveralShown() && !narrow;
            var list = Filtered().ToList(); lastList = list;
            var ids = new HashSet<string>(list.Select(i => i.Id)); picked.RemoveWhere(id => !ids.Contains(id));
            if (headerHost != null) headerHost.Child = HeaderRow(narrow, list);
            if (list.Count == 0) listPanel.Children.Add(new TextBlock { Text = vault.Data.Items.Count == 0 ? "Nothing here yet. Add an item with Create item, or import from NordPass in Import and export." : (lastSearch ?? "").Trim().Length > 0 ? "Nothing matches." : "Nothing in " + Vaults.TabName(tab).ToLowerInvariant() + " here.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 12, 8, 0) });
            foreach (var it in list) listPanel.Children.Add(Row(it, narrow, showVault));
            FillBulkBar();
            FrameworkElement r;
            if (selected != null && rowById.TryGetValue(selected.Id, out r)) Dispatcher.BeginInvoke(new Action(() => { try { r.BringIntoView(); } catch { } }), DispatcherPriority.Loaded);
        }
        UIElement HeaderRow(bool narrow, List<Item> list)
        {
            var g = Columns(narrow); g.Margin = new Thickness(4, 0, 8, 2);
            bool all = list.Count > 0 && list.All(i => picked.Contains(i.Id));
            var tick = new CheckBox { Style = St("Tick"), IsChecked = all ? true : picked.Count > 0 ? (bool?)null : false, VerticalAlignment = VerticalAlignment.Center, ToolTip = all ? "Untick all" : "Tick everything in the list (Ctrl+A)" };
            tick.Click += (s, e) => { if (all) picked.Clear(); else foreach (var i in list) picked.Add(i.Id); FillList(); };
            g.Children.Add(tick);
            Action<string, string, int> head = (key, label, col) =>
            {
                var c = new StackPanel { Orientation = Orientation.Horizontal };
                c.Children.Add(new TextBlock { Text = label, FontWeight = sortCol == key ? FontWeights.SemiBold : FontWeights.Normal });
                var ic = Icons.Make(sortCol == key ? (sortDesc ? "caret-down" : "caret-up") : "sort", 9, sortCol == key ? (Brush)FindResource("Text") : (Brush)FindResource("Line"));
                ic.Margin = new Thickness(6, 0, 0, 0); ic.VerticalAlignment = VerticalAlignment.Center; c.Children.Add(ic);
                var b = new Button { Style = St("Head"), Tag = sortCol == key ? "on" : null, Content = c, Margin = new Thickness(col == 1 ? 6 : 0, 0, 0, 0), ToolTip = "Sort by " + label.ToLowerInvariant() + " (click again to reverse, a third time for the usual order)" };
                b.Click += (s, e) =>
                {
                    if (sortCol != key) { sortCol = key; sortDesc = key == "used" || key == "modified"; }
                    else if (sortDesc == (key == "used" || key == "modified")) sortDesc = !sortDesc;
                    else sortCol = null;
                    FillList();
                };
                Grid.SetColumn(b, col); g.Children.Add(b);
            };
            head("title", "Title", 1); head("used", "Last used", 2);
            if (!narrow) { head("folder", SeveralShown() && curVault == null ? "Folder and vault" : "Folder", 3); head("modified", "Modified", 4); }
            return g;
        }
        UIElement Row(Item it, bool narrow, bool showVault)
        {
            var item = it;
            var b = new Button { Style = St("Nav"), Tag = selected == it ? "on" : null, Padding = new Thickness(4, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch, Focusable = false };
            var g = Columns(narrow);
            var tick = new CheckBox { Style = St("Tick"), IsChecked = picked.Contains(it.Id), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Tick to move, tag or delete several items at once" };
            tick.Click += (s, e) => { if (tick.IsChecked == true) picked.Add(item.Id); else picked.Remove(item.Id); pickAnchor = item.Id; FillBulkBar(); RefreshHeader(); };
            g.Children.Add(tick);
            var tc = new DockPanel { Margin = new Thickness(6, 0, 10, 0) };
            var tile = Tile(it, 30); tile.Margin = new Thickness(0, 0, 12, 0); tc.Children.Add(tile);
            if (it.Favorite) { var st = Icons.Make("star", 11, B("#F0BE5A")); st.VerticalAlignment = VerticalAlignment.Center; st.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(st, Dock.Right); tc.Children.Add(st); }
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = Subtitle(it); if (narrow && it.Folder.Length > 0 && it.Type == "login") sub = sub.Length > 0 ? sub + "  -  " + it.Folder : it.Folder;
            tx.Children.Add(new TextBlock { Text = sub.Length > 0 ? sub : ItemTypes.Name(it.Type), Foreground = Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
            tc.Children.Add(tx); Grid.SetColumn(tc, 1); g.Children.Add(tc);
            DateTime u; var used = new TextBlock { Text = usageMap.TryGetValue(it.Id, out u) ? WhenText(u) : "-", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = usageMap.ContainsKey(it.Id) ? "Last filled, copied or opened " + u.ToString("d MMM yyyy HH:mm") : "Not filled, copied or opened yet" };
            Grid.SetColumn(used, 2); g.Children.Add(used);
            if (!narrow)
            {
                var fc = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                fc.Children.Add(new TextBlock { Text = it.Folder.Length > 0 ? it.Folder : "-", Foreground = it.Folder.Length > 0 ? (Brush)FindResource("Text") : Muted, TextTrimming = TextTrimming.CharacterEllipsis });
                if (showVault)
                {
                    var v = Vaults.Of(vault.Data, it); var vr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    vr.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = B(VaultColor(v)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
                    vr.Children.Add(new TextBlock { Text = v.Name, Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                    fc.Children.Add(vr);
                }
                Grid.SetColumn(fc, 3); g.Children.Add(fc);
                var mod = new TextBlock { Text = WhenText(it.Updated), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Changed " + it.Updated.ToString("d MMM yyyy HH:mm") };
                Grid.SetColumn(mod, 4); g.Children.Add(mod);
            }
            b.Content = g;
            b.Click += (s, e) => RowClick(item);
            rowById[it.Id] = b;
            return b;
        }
        void RefreshHeader() { if (headerHost != null) headerHost.Child = HeaderRow(DetailOpen(), lastList); }
        void RowClick(Item it)
        {
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Control) != 0) { if (!picked.Remove(it.Id)) picked.Add(it.Id); pickAnchor = it.Id; FillList(); return; }
            if ((mods & ModifierKeys.Shift) != 0 && pickAnchor != null)
            {
                int a = lastList.FindIndex(x => x.Id == pickAnchor), z = lastList.IndexOf(it);
                if (a >= 0 && z >= 0) { for (int i = Math.Min(a, z); i <= Math.Max(a, z); i++) picked.Add(lastList[i].Id); FillList(); return; }
            }
            if (selected != it) showHistory = false;
            selected = it; editing = null; pickAnchor = it.Id; FillList(); ShowDetail();
        }
        void MoveSelection(int d)
        {
            if (lastList.Count == 0) return;
            int i = selected == null ? -1 : lastList.IndexOf(selected);
            i = i < 0 ? (d > 0 ? 0 : lastList.Count - 1) : Math.Max(0, Math.Min(lastList.Count - 1, i + d));
            if (selected != lastList[i]) showHistory = false;
            selected = lastList[i]; editing = null; pickAnchor = selected.Id; FillList(); ShowDetail();
        }
        // keys in the list: Up/Down pick (also from the search box), Space ticks, Ctrl+A ticks all, Delete trashes, Esc unticks / closes the panel
        void ListKeys(object sender, KeyEventArgs e)
        {
            if (vault == null || listPanel == null || capturingKey || editing != null) return;
            var focus = Keyboard.FocusedElement; bool inSearch = search != null && focus == search;
            bool inText = focus is TextBox || focus is PasswordBox || focus is ComboBox || focus is ComboBoxItem;
            if ((e.Key == Key.Down || e.Key == Key.Up) && (inSearch || !inText)) { MoveSelection(e.Key == Key.Down ? 1 : -1); e.Handled = true; }
            else if (e.Key == Key.Space && !inText && selected != null) { if (!picked.Remove(selected.Id)) picked.Add(selected.Id); FillList(); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && !inText) { foreach (var i in lastList) picked.Add(i.Id); FillList(); e.Handled = true; }
            else if (e.Key == Key.Delete && !inText) { if (picked.Count > 0) BulkTrash(); else if (selected != null) { TrashItem(selected); ShowMain(); } e.Handled = true; }
            else if (e.Key == Key.Escape && !(inSearch && search.Text.Length > 0))
            {
                if (picked.Count > 0) { picked.Clear(); FillList(); e.Handled = true; }
                else if (selected != null) { selected = null; FillList(); ShowDetail(); e.Handled = true; }
            }
        }

        // ---- the bulk bar
        List<Item> PickedItems() { return vault.Data.Items.Where(i => picked.Contains(i.Id) && IsShown(i)).ToList(); }
        void FillBulkBar()
        {
            if (bulkBar == null) return;
            bulkBar.Children.Clear();
            var items = PickedItems();
            if (items.Count == 0) { bulkBox.Visibility = Visibility.Collapsed; return; }
            bulkBox.Visibility = Visibility.Visible;
            bulkBar.Children.Add(new TextBlock { Text = items.Count + " selected", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            Func<string, string, Action<Button>, Button> btn = (icon, text, act) =>
            {
                var c = new StackPanel { Orientation = Orientation.Horizontal }; var ic = Icons.Make(icon, 11, (Brush)FindResource("Text")); ic.VerticalAlignment = VerticalAlignment.Center; ic.Margin = new Thickness(0, 0, 6, 0); c.Children.Add(ic); c.Children.Add(new TextBlock { Text = text });
                var b = new Button { Style = St("Btn"), Content = c, Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(10, 4, 10, 4) }; b.Click += (s, e) => act(b); bulkBar.Children.Add(b); return b;
            };
            btn("folder", "Move to folder", b =>
            {
                var m = new ContextMenu();
                foreach (var f in vault.Data.Folders.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) { var name = f; var mi = new MenuItem { Header = name }; mi.Click += (s, e) => BulkFolder(name); m.Items.Add(mi); }
                if (vault.Data.Folders.Count > 0) m.Items.Add(new Separator());
                var none = new MenuItem { Header = "No folder" }; none.Click += (s, e) => BulkFolder(""); m.Items.Add(none);
                var nf = new MenuItem { Header = "New folder..." }; nf.Click += (s, e) => { var n = Ask.Text(this, "New folder", "A name for the folder:", "Folder"); if (!string.IsNullOrWhiteSpace(n)) BulkFolder(n.Trim()); }; m.Items.Add(nf);
                m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
            });
            if (VisibleVaults().Count > 1)
                btn("layer-group", "Move to vault", b =>
                {
                    var m = new ContextMenu();
                    foreach (var v in VisibleVaults()) { var vid = v.Id; var mi = new MenuItem { Header = v.Name, Icon = VaultTile(v, 16) }; mi.Click += (s, e) => BulkVault(vid); m.Items.Add(mi); }
                    m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
                });
            btn("tag", "Add tag", b =>
            {
                var t = Ask.Text(this, "Add a tag", "A tag for the " + PickedItems().Count + " items (several: separate them with commas):", "Tag"); if (string.IsNullOrWhiteSpace(t)) return;
                var tags = Organize.ParseTags(t); var list = PickedItems();
                foreach (var it in list) { bool ch = false; foreach (var tg in tags) if (!it.Tags.Any(x => string.Equals(x, tg, StringComparison.CurrentCultureIgnoreCase))) { it.Tags.Add(tg); ch = true; } if (ch) it.Updated = DateTime.Now; }
                SaveVault("Tagged " + list.Count + " item" + (list.Count == 1 ? "" : "s") + " #" + string.Join(" #", tags) + "."); ShowMain();
            });
            bool allFav = items.All(i => i.Favorite);
            btn("star", allFav ? "Unfavourite" : "Favourite", b => { var list = PickedItems(); foreach (var it in list) if (it.Favorite == allFav) { it.Favorite = !allFav; it.Updated = DateTime.Now; } SaveVault((allFav ? "Unfavourited " : "Favourited ") + list.Count + " item" + (list.Count == 1 ? "" : "s") + "."); ShowMain(); });
            btn("trash-can", "Move to trash", b => BulkTrash());
            var clear = new Button { Style = St("Nav"), Padding = new Thickness(7, 5, 7, 5), Content = Icons.Make("xmark", 12, Muted), ToolTip = "Untick all (Esc)", VerticalAlignment = VerticalAlignment.Center };
            clear.Click += (s, e) => { picked.Clear(); FillList(); };
            bulkBar.Children.Add(clear);
        }
        void BulkFolder(string name)
        {
            var list = PickedItems(); if (list.Count == 0) return;
            if (name.Length > 0 && !vault.Data.Folders.Contains(name)) vault.Data.Folders.Add(name);
            foreach (var it in list) if (it.Folder != name) { it.Folder = name; it.Updated = DateTime.Now; }
            SaveVault("Moved " + list.Count + " item" + (list.Count == 1 ? "" : "s") + (name.Length > 0 ? " to the folder " + name : " out of their folders") + "."); ShowMain();
        }
        void BulkVault(string vid)
        {
            var list = PickedItems(); var v = Vaults.Get(vault.Data, vid); if (list.Count == 0 || v == null || !VaultVisible(v)) return;
            foreach (var it in list) if (Vaults.IdOf(vault.Data, it) != v.Id) { Vaults.Put(it, v.Id); it.Updated = DateTime.Now; }
            picked.Clear();
            SaveVault("Moved " + list.Count + " item" + (list.Count == 1 ? "" : "s") + " to the " + v.Name + " vault."); ShowMain();
        }
        void BulkTrash()
        {
            var list = PickedItems(); if (list.Count == 0) return;
            if (MessageBox.Show(this, "Move " + list.Count + " item" + (list.Count == 1 ? "" : "s") + " to the trash?\n\nYou can restore them from Trash for " + Organize.TrashDays + " days.", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
            foreach (var it in list) { Organize.MoveToTrash(vault.Data, it, DateTime.Now); if (selected == it) selected = null; }
            picked.Clear(); SaveVault(list.Count + " item" + (list.Count == 1 ? "" : "s") + " moved to the trash - restore them from Trash within " + Organize.TrashDays + " days."); ShowMain();
        }

        // ---- tiles: the site's icon (if turned on and fetched), a coloured letter for a login, the type's icon otherwise
        FrameworkElement Tile(Item it, double size)
        {
            var host = S.SiteIcons ? SiteIcons.HostFor(it) : null;
            var img = host != null ? IconImage(host) : null;
            if (img != null)
                return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = B("#F4F2F8"), Child = new Image { Source = img, Width = size * 0.7, Height = size * 0.7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, VerticalAlignment = VerticalAlignment.Center };
            var h = it.Type == "login" ? it.Host : null;
            if (h != null)
                return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = B(SiteIcons.TileColor(Domain.Registrable(h) ?? h)), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = SiteIcons.TileLetter(it), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = size * 0.46, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var ic = Icons.Make(ItemTypes.Icon(it.Type), size * 0.48, B(Program.AccentHex)); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = (Brush)FindResource("PanelHi"), Child = ic, VerticalAlignment = VerticalAlignment.Center };
        }
        string VaultColor(VaultInfo v) { return v == null || string.IsNullOrEmpty(v.Color) ? Program.AccentHex : v.Color; }
        FrameworkElement VaultTile(VaultInfo v, double size)
        {
            var ic = Icons.Make(v == null ? "layer-group" : (v.Icon.Length > 0 ? v.Icon : "vault"), size * 0.52, v == null ? (Brush)FindResource("Text") : Brushes.White);
            ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = v == null ? (Brush)FindResource("PanelHi") : B(VaultColor(v)), Child = ic, VerticalAlignment = VerticalAlignment.Center };
        }
        static string WhenText(DateTime t)
        {
            var now = DateTime.Now; var days = (now.Date - t.Date).TotalDays;
            if (days < 0) return t.ToString("d MMM yyyy");
            if (days == 0) return "Today " + t.ToString("HH:mm");
            if (days == 1) return "Yesterday";
            if (days < 7) return (int)days + " days ago";
            return t.Year == now.Year ? t.ToString("d MMM") : t.ToString("d MMM yyyy");
        }

        // ---- folders
        void NewFolder()
        {
            var n = Ask.Text(this, "New folder", "A name for the folder:", "Folder"); if (string.IsNullOrWhiteSpace(n)) return;
            n = n.Trim(); if (!vault.Data.Folders.Contains(n)) vault.Data.Folders.Add(n);
            view = "folder:" + n; selected = null; editing = null; SaveVault("Folder \"" + n + "\" is ready - move items into it with the tick boxes, or pick it when you edit an item."); ShowMain();
        }

        // ---- the vault switcher (top of the sidebar)
        void VaultSwitcher(StackPanel top)
        {
            var several = SeveralShown();
            var cur = curVault == null ? (several ? null : VisibleVaults().FirstOrDefault()) : Vaults.Get(vault.Data, curVault);
            var b = new Button { Style = St("Btn"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(8, 6, 10, 6), ToolTip = "Switch vault, make a new one, or manage them" };
            var d = new DockPanel { Width = 186 };
            var caret = Icons.Make("caret-down", 11, Muted); caret.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(caret, Dock.Right); d.Children.Add(caret);
            var tile = VaultTile(cur, 28); tile.Margin = new Thickness(0, 0, 10, 0); d.Children.Add(tile);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = cur == null ? "All vaults" : cur.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            tx.Children.Add(new TextBlock { Text = Scope().Items.Count + " items" + (cur != null && cur.Travel ? "  -  safe for travel" : ""), Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            d.Children.Add(tx); b.Content = d;
            b.Click += (s, e) =>
            {
                var m = new ContextMenu();
                var shown = Shown();
                if (several)
                {
                    var all = new MenuItem { Header = "All vaults  (" + shown.Items.Count + ")", Icon = VaultTile(null, 16), FontWeight = curVault == null ? FontWeights.SemiBold : FontWeights.Normal }; all.Click += (a, c) => PickVault(null); m.Items.Add(all);
                    m.Items.Add(new Separator());
                }
                foreach (var v in VisibleVaults())
                {
                    var vid = v.Id; int n = shown.Items.Count(i => Vaults.IdOf(vault.Data, i) == vid);
                    var mi = new MenuItem { Header = v.Name + "  (" + n + ")" + (v.Travel ? "  - safe for travel" : ""), Icon = VaultTile(v, 16), FontWeight = curVault == vid ? FontWeights.SemiBold : FontWeights.Normal };
                    mi.Click += (a, c) => PickVault(vid); m.Items.Add(mi);
                }
                m.Items.Add(new Separator());
                var nv = new MenuItem { Header = "New vault...", Icon = Icons.Make("plus", 12, Muted) }; nv.Click += (a, c) => NewVault(); m.Items.Add(nv);
                var mg = new MenuItem { Header = "Manage vaults and Travel Mode", Icon = Icons.Make("gear", 12, Muted) }; mg.Click += (a, c) => { view = "vaults"; selected = null; editing = null; ShowMain(); }; m.Items.Add(mg);
                m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
            };
            top.Children.Add(b);
        }
        void PickVault(string id)
        {
            curVault = id; picked.Clear(); editing = null;
            if (selected != null && id != null && Vaults.IdOf(vault.Data, selected) != id) selected = null;
            if (view != "fav" && !view.StartsWith("folder:") && !view.StartsWith("tag:") && view != "trash") view = "all";
            ShowMain();
        }
        void TravelBanner(StackPanel top)
        {
            if (!Travel) return;
            int hidden = Vaults.Live(vault.Data).Count(v => !v.Travel);
            var box = new Border { Background = B("#3A2E14"), BorderBrush = B("#8A6A20"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 10, 0, 0) };
            var sp = new StackPanel();
            var r = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("suitcase", 13, B("#F0BE5A")); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; r.Children.Add(ic);
            r.Children.Add(new TextBlock { Text = "Travel Mode is on", FontWeight = FontWeights.SemiBold, Foreground = B("#F0BE5A") });
            sp.Children.Add(r);
            sp.Children.Add(new TextBlock { Text = hidden + " vault" + (hidden == 1 ? " is" : "s are") + " hidden on this PC (still in the encrypted file).", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
            var off = new Button { Content = "Turn off...", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) }; off.Click += (s, e) => TravelOff();
            sp.Children.Add(off); box.Child = sp; top.Children.Add(box);
        }

        // ---- vaults: make, rename, look, delete (to the trash), restore, export, safe for travel
        static string GuessIcon(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("gam") || n.Contains("steam") || n.Contains("play")) return "gamepad";
            if (n.Contains("work") || n.Contains("job") || n.Contains("office") || n.Contains("business")) return "briefcase";
            if (n.Contains("shar") || n.Contains("family") || n.Contains("team")) return "users";
            if (n.Contains("home") || n.Contains("house")) return "house";
            if (n.Contains("travel") || n.Contains("trip")) return "plane";
            if (n.Contains("money") || n.Contains("bank") || n.Contains("finance") || n.Contains("crypto")) return "wallet";
            return "vault";
        }
        void NewVault()
        {
            var n = Ask.Text(this, "New vault", "A name for the new vault - Gaming, Work, Shared...", "Name"); if (n == null) return;
            string why; var color = Vaults.ColorChoices[1 + (Vaults.Live(vault.Data).Count - 1) % (Vaults.ColorChoices.Length - 1)];
            var v = Vaults.Create(vault.Data, n, GuessIcon(n), color, Device.Id, out why);
            if (v == null) { Toast(why, true); return; }
            if (Travel) Vaults.SetTravel(vault.Data, v.Id, true, Device.Id);   // made while travelling: it has to be safe for travel, or it would vanish
            curVault = v.Id; view = "all"; selected = null; editing = null; picked.Clear();
            SaveVault("The " + v.Name + " vault is ready" + (Travel ? " (safe for travel)" : "") + " - move items into it with the tick boxes, or create new ones here."); ShowMain();
        }
        void ExportVault(string id)
        {
            var keep = curVault; curVault = id;
            try { ExportEncrypted(); } finally { curVault = keep; }
        }
        UIElement VaultsPage()
        {
            ScrollViewer sv; var sp = Page("Vaults and Travel Mode", "Keep things apart - Personal, Gaming, Work, Shared... Every item is in one vault. They all live inside your one encrypted vault file and sync together; pick one at the top of the sidebar to see only its items, and export one on its own.", out sv);
            TravelCard(sp);
            var head = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
            var nb = new Button { Content = "New vault", Style = St("Accent") }; nb.Click += (s, e) => NewVault();
            DockPanel.SetDock(nb, Dock.Right); head.Children.Add(nb);
            head.Children.Add(new TextBlock { Text = "Your vaults", FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            foreach (var v0 in VisibleVaults())
            {
                var v = v0; var box = new StackPanel();
                var row = new DockPanel();
                var bs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Func<string, Action<Button>, Button> btn = (text, act) => { var b = new Button { Content = text, Style = St("Btn"), Margin = new Thickness(6, 0, 0, 0) }; b.Click += (s, e) => act(b); bs.Children.Add(b); return b; };
                btn("Open", b => PickVault(v.Id));
                btn("Rename", b => { var n = Ask.Text(this, "Rename vault", "A new name for \"" + v.Name + "\":", "Name"); if (n == null) return; var err = Vaults.Rename(vault.Data, v.Id, n, Device.Id); if (err != null) { Toast(err, true); return; } SaveVault("Renamed."); ShowMain(); });
                btn("Look", b =>
                {
                    var m = new ContextMenu();
                    foreach (var icn in Vaults.IconChoices) { var i2 = icn; var mi = new MenuItem { Header = "Icon: " + i2, Icon = Icons.Make(i2, 13, Muted) }; mi.Click += (s, e) => { Vaults.SetLook(vault.Data, v.Id, i2, null, Device.Id); SaveVault(null); ShowMain(); }; m.Items.Add(mi); }
                    m.Items.Add(new Separator());
                    foreach (var col in Vaults.ColorChoices) { var c2 = col; var mi = new MenuItem { Header = c2.Length == 0 ? "Colour: the accent" : "Colour " + c2, Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = B(c2.Length == 0 ? Program.AccentHex : c2) } }; mi.Click += (s, e) => { Vaults.SetLook(vault.Data, v.Id, null, c2, Device.Id); SaveVault(null); ShowMain(); }; m.Items.Add(mi); }
                    m.PlacementTarget = b; m.Placement = PlacementMode.Bottom; m.IsOpen = true;
                });
                btn("Export", b => ExportVault(v.Id));
                if (v.Id != Vaults.PersonalId)
                    btn("Delete", b =>
                    {
                        int n = vault.Data.Items.Count(i => i.VaultId == v.Id);
                        if (Travel && v.Travel && Vaults.Live(vault.Data).Count(x => x.Travel) == 1) { Toast("That's the only vault safe for travel - turn Travel Mode off first.", true); return; }
                        if (MessageBox.Show(this, "Delete the vault \"" + v.Name + "\"?\n\nIts " + n + " item" + (n == 1 ? "" : "s") + " go to the trash (still encrypted in your vault) - you can restore the whole vault from this page, or single items from Trash, for " + Organize.TrashDays + " days.", "Delete vault", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                        Vaults.Delete(vault.Data, v.Id, Device.Id, DateTime.Now); if (curVault == v.Id) curVault = null; selected = null; picked.Clear();
                        SaveVault("Deleted the " + v.Name + " vault - its items are in the trash."); ShowMain();
                    });
                DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                var tile = VaultTile(v, 36); tile.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(tile);
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                int count = vault.Data.Items.Count(i => Vaults.IdOf(vault.Data, i) == v.Id), trash = vault.Data.Trash.Count(i => Vaults.IdOf(vault.Data, i) == v.Id);
                tx.Children.Add(new TextBlock { Text = v.Name + (v.Id == Vaults.PersonalId ? "  (default)" : ""), FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                tx.Children.Add(new TextBlock { Text = count + " item" + (count == 1 ? "" : "s") + (trash > 0 ? ", " + trash + " in the trash" : ""), Foreground = Muted, FontSize = 11.5 });
                row.Children.Add(tx); box.Children.Add(row);
                var sw = Switch("Safe for travel - stays on this PC while Travel Mode is on", v.Travel, on => { Vaults.SetTravel(vault.Data, v.Id, on, Device.Id); SaveVault(on ? v.Name + " is safe for travel." : v.Name + " is hidden while Travel Mode is on."); ShowMain(); });
                sw.Margin = new Thickness(48, 8, 0, 0);
                if (Travel) { sw.IsEnabled = false; sw.ToolTip = "Turn Travel Mode off to change which vaults are safe for travel."; }
                box.Children.Add(sw);
                sp.Children.Add(Card(box));
            }
            if (Travel)
            {
                int hidden = Vaults.Live(vault.Data).Count(v => !v.Travel);
                if (hidden > 0) sp.Children.Add(new TextBlock { Text = hidden + " more vault" + (hidden == 1 ? " is" : "s are") + " hidden while Travel Mode is on.", Foreground = Muted, Margin = new Thickness(2, 4, 0, 8) });
            }
            var gone = Vaults.DeletedOnes(vault.Data).Where(v => !Travel || v.Travel).ToList();
            if (gone.Count > 0)
            {
                sp.Children.Add(new TextBlock { Text = "Deleted vaults", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 12, 0, 6) });
                foreach (var g0 in gone)
                {
                    var g = g0; var row = new DockPanel();
                    int inTrash = vault.Data.Trash.Count(i => i.VaultId == g.Id);
                    var rs = new Button { Content = "Restore", Style = St("Btn") };
                    rs.Click += (s, e) => { var n = Vaults.Undelete(vault.Data, g.Id, Device.Id); SaveVault("Restored the " + Vaults.Get(vault.Data, g.Id).Name + " vault with " + Math.Max(0, n) + " item" + (n == 1 ? "" : "s") + "."); ShowMain(); };
                    DockPanel.SetDock(rs, Dock.Right); row.Children.Add(rs);
                    row.Children.Add(new TextBlock { Text = g.Name + "  -  " + (inTrash > 0 ? inTrash + " item" + (inTrash == 1 ? "" : "s") + " still in the trash" : "nothing left in the trash"), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    sp.Children.Add(Card(row));
                }
            }
            return sv;
        }
        // in the editor: which vault the item is in (only once there's more than one, or while travelling)
        void VaultPicker(StackPanel sp, Item e, List<Action> fields)
        {
            var vs = VisibleVaults(); if (vs.Count < 2) return;
            sp.Children.Add(new TextBlock { Text = "Vault", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            var cb = new ComboBox { Style = St("Combo"), ItemsSource = vs.Select(v => v.Name + (v.Travel && Travel ? "  (safe for travel)" : "")).ToList(), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
            var cur = Vaults.IdOf(vault.Data, e); cb.SelectedIndex = Math.Max(0, vs.FindIndex(v => v.Id == cur));
            sp.Children.Add(cb);
            fields.Add(() => { if (cb.SelectedIndex >= 0 && cb.SelectedIndex < vs.Count) Vaults.Put(e, vs[cb.SelectedIndex].Id); });
        }

        // ---- Travel Mode
        void TravelCard(StackPanel sp)
        {
            var box = new StackPanel();
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var ic = Icons.Make("suitcase", 14, Travel ? B("#F0BE5A") : Muted); ic.Margin = new Thickness(0, 0, 8, 0); ic.VerticalAlignment = VerticalAlignment.Center; h.Children.Add(ic);
            h.Children.Add(new TextBlock { Text = "Travel Mode", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            box.Children.Add(h);
            box.Children.Add(new TextBlock { Text = "Before a trip, mark the vaults you need as \"Safe for travel\" (Vaults and travel), then turn this on. Every other vault disappears from this PC - the list, search, the browser extension, desktop autofill, the phone copy, exports and Password Health - until you turn it off again with your master password.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            box.Children.Add(new TextBlock { Text = "Be aware: this hides, it doesn't remove. Hallow Keys keeps your whole vault in one encrypted file on each PC, so the hidden items are still inside that file on this disk (and keep syncing) - unlike 1Password, which takes them off the device. Anyone who can make you type your master password could turn Travel Mode off.", Foreground = B("#F0BE5A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var safe = Vaults.Live(vault.Data).Where(v => v.Travel).Select(v => v.Name).ToList();
            box.Children.Add(new TextBlock { Text = safe.Count == 0 ? "No vault is marked safe for travel yet." : "Safe for travel: " + string.Join(", ", safe) + ".", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
            box.Children.Add(Switch("Travel Mode on this PC" + (Travel && S.TravelSince.Length > 0 ? " (since " + S.TravelSince + ")" : ""), Travel, on => { if (on) TravelOn(); else TravelOff(); }));
            sp.Children.Add(Card(box));
        }
        void TravelOn()
        {
            if (Travel) return;
            if (!Vaults.AnyTravelSafe(vault.Data)) { Toast("Mark at least one vault \"Safe for travel\" first (Vaults and travel).", true); ShowMain(); return; }
            var hide = Vaults.Live(vault.Data).Where(v => !v.Travel).ToList();
            int n = vault.Data.Items.Count(i => !Vaults.Of(vault.Data, i).Travel);
            if (!Snap.On && MessageBox.Show(this, "Turn on Travel Mode?\n\nHidden on this PC until you turn it off with your master password: " + (hide.Count == 0 ? "nothing" : string.Join(", ", hide.Select(v => v.Name))) + " (" + n + " item" + (n == 1 ? "" : "s") + ").\n\nThey stay inside the encrypted vault file on this PC and keep syncing - Travel Mode hides them, it doesn't remove them.", "Travel Mode", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) { ShowMain(); return; }
            SetTravel(true);
            Toast("Travel Mode is on - " + hide.Count + " vault" + (hide.Count == 1 ? "" : "s") + " hidden on this PC.");
        }
        void TravelOff()
        {
            if (!Travel) return;
            var pw = Ask.Password(this, "Turn off Travel Mode", "Type your master password to show every vault on this PC again:");
            if (pw == null) { ShowMain(); return; }
            IsEnabled = false;
            new Thread(() =>
            {
                string err = null;
                try { using (Vault.Open(KeysSettings.VaultPath, pw)) { } } catch (Exception e) { err = e.Message; } finally { Gcm.Wipe(pw); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true;
                    if (err != null) { Toast(err, true); ShowMain(); return; }
                    SetTravel(false); Toast("Travel Mode is off - every vault is back.");
                }));
            }) { IsBackground = true }.Start();
        }
        void SetTravel(bool on)
        {
            S.TravelMode = on; S.TravelSince = on ? DateTime.Now.ToString("d MMM yyyy HH:mm") : ""; S.Save();
            TestLog(on ? "travel on" : "travel off");
            if (on) { if (selected != null && !IsShown(selected)) selected = null; picked.Clear(); var cv = curVault == null ? null : Vaults.Get(vault.Data, curVault); if (cv != null && !cv.Travel) curVault = null; }
            editing = null; WriteStatus(); AfterSave();   // the phone copy and status follow at once
            ShowMain();
        }

        // ---- last used
        DispatcherTimer usageTimer; bool usageDirty;
        void MarkUsed(Item it) { if (it != null) MarkUsedId(it.Id); }
        void MarkUsedId(string id)
        {
            if (vault == null || string.IsNullOrEmpty(id) || !vault.Data.Items.Any(i => i.Id == id)) return;
            if (!Vaults.MarkUsed(vault.Data, id, DateTime.UtcNow)) return;
            usageDirty = true; TestLog("item used");
            if (usageTimer == null)
            {
                usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                usageTimer.Tick += (s, e) => { usageTimer.Stop(); if (usageDirty && vault != null) { usageDirty = false; SaveVault(null); if (listPanel != null && editing == null) FillList(); } };
            }
            usageTimer.Stop(); usageTimer.Start();
        }
        void Init21()   // from ExtraInit (TestHooks.cs)
        {
            PreviewKeyDown += ListKeys;
            SafeClip.Copied += () => { if (vault != null && detail != null && selected != null && editing == null && vault.Data.Items.Contains(selected)) MarkUsed(selected); };
        }
        void AfterUnlock21()
        {
            picked.Clear(); if (curVault != null) { var v = Vaults.Get(vault.Data, curVault); if (v == null || v.Deleted || !VaultVisible(v)) curVault = null; }
            StartIcons();
        }
        void Lock21()
        {
            if (usageTimer != null) usageTimer.Stop();
            if (usageDirty && vault != null) { usageDirty = false; try { vault.Save(); } catch { } }
            StopIcons(); picked.Clear(); curVault = null; tab = "all"; otherType = null;
        }
        void AfterSave21()
        {
            // a login with a new site: fetch its icon (only if site icons are on)
            if (!S.SiteIcons || vault == null || icons == null || iconBusy) return;
            bool k, f;
            if (vault.Data.Items.Select(SiteIcons.HostFor).Where(h => h != null).Distinct().Any(h => { icons.Get(h, out k, out f); return !k; })) StartIcons();
        }

        // ---- site icons (Settings > Site icons; off by default)
        IconCache icons; int iconGen; volatile bool iconBusy;
        readonly Dictionary<string, ImageSource> iconImages = new Dictionary<string, ImageSource>();
        static string IconCachePath { get { return Path.Combine(KeysSettings.Dir, "icons.hkcache"); } }
        ImageSource IconImage(string host)
        {
            ImageSource img; if (iconImages.TryGetValue(host, out img)) return img;
            if (icons == null) return null;
            bool k, f; var png = icons.Get(host, out k, out f);
            if (png != null) try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(png); bi.EndInit(); bi.Freeze(); img = bi; } catch { img = null; }
            iconImages[host] = img; return img;
        }
        void StartIcons()
        {
            StopIcons();
            if (!S.SiteIcons || vault == null || Snap.On) return;
            var key = vault.DataKeyCopy(); if (key == null) return;
            try { icons = IconCache.Open(IconCachePath, key); } finally { Gcm.Wipe(key); }
            var gen = iconGen; var cache = icons;
            var hosts = vault.Data.Items.Concat(vault.Data.Trash).Select(SiteIcons.HostFor).Where(h => h != null).Distinct().ToList();
            cache.Keep(hosts);
            var todo = hosts.Where(h => { bool k, f; cache.Get(h, out k, out f); return !f; }).ToList();
            TestLog("icons: " + hosts.Count + " sites, " + todo.Count + " to fetch");
            if (todo.Count == 0) { try { cache.Save(); } catch { } return; }
            iconBusy = true;
            var th = new Thread(() =>
            {
                int got = 0, done = 0;
                try
                {
                    foreach (var h in todo)
                    {
                        if (gen != iconGen) return;
                        string why = null; byte[] png = null;
                        try { png = SiteIcons.Fetch(h, out why); } catch { png = null; }
                        if (gen != iconGen) return;
                        cache.Put(h, png, DateTime.UtcNow); done++; if (png != null) got++;
                        if (png != null) { var host = h; Dispatcher.BeginInvoke(new Action(() => { if (gen == iconGen) { iconImages.Remove(host); if (listPanel != null && editing == null) FillList(); } })); }
                        if (done % 10 == 0) try { cache.Save(); } catch { }
                        Thread.Sleep(250);
                    }
                    if (gen == iconGen) try { cache.Save(); } catch { }
                    Dispatcher.BeginInvoke(new Action(() => TestLog("icons: fetched " + done + ", with an icon " + got)));
                }
                finally { if (gen == iconGen) iconBusy = false; }
            }) { IsBackground = true, Name = "site icons" };
            th.SetApartmentState(ApartmentState.STA); th.Start();
        }
        void StopIcons() { iconGen++; iconBusy = false; if (icons != null) { icons.Close(); icons = null; } iconImages.Clear(); }
        void SiteIconsCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Site icons", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Show each login's own icon instead of a coloured letter. It's off unless you turn it on, because it connects to your sites: Hallow Keys asks each login's own site for its icon, directly - no third-party icon service ever sees your list of sites. Each site just sees one visit from your PC, without cookies, like a browser opening its home page.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Only https addresses with a public name are asked - never http, IP addresses, local network names or anything that points into your network. Icons are kept encrypted next to your vault (icons.hkcache, unreadable without unlocking), refreshed once a month, and deleted when you turn this off.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            box.Children.Add(Switch("Fetch site icons" + (S.SiteIcons && icons != null ? " (" + icons.Count + " sites checked)" : ""), S.SiteIcons, on =>
            {
                S.SiteIcons = on; S.Save();
                if (on) { StartIcons(); Toast("Fetching icons from your logins' sites in the background..."); }
                else { StopIcons(); IconCache.Delete(IconCachePath); Toast("Site icons are off and their cache is deleted."); }
                ShowMain();
            }));
            sp.Children.Add(Card(box));
        }

        // ---- developer snapshots (made-up data only, nothing fetched, nothing saved)
        public static VaultData FakeData21()
        {
            var d = FakeData();
            var personal = Vaults.Personal(d); personal.Travel = true;
            string w;
            var gaming = Vaults.Create(d, "Gaming", "gamepad", "#3FB97A", "snap", out w);
            var work = Vaults.Create(d, "Work", "briefcase", "#4C8DF6", "snap", out w);
            Vaults.Create(d, "Shared", "users", "#E0A030", "snap", out w);
            foreach (var it in d.Items)
            {
                if (it.Folder == "Games" || it.Title == "Example wallet") Vaults.Put(it, gaming.Id);
                else if (it.Type == "apikey" || it.Type == "server" || it.Type == "sshkey" || it.Title == "Example Studio") Vaults.Put(it, work.Id);
            }
            d.Items.Add(new Item { Type = "document", Title = "Home insurance policy", Fields = { }, Files = { new Attachment { Name = "policy-made-up.pdf", Data = new byte[2048] } } });
            ItemTypes.Ensure(d.Items[d.Items.Count - 1]); ItemTypes.Set(d.Items[d.Items.Count - 1], "kind", "Insurance policy"); ItemTypes.Set(d.Items[d.Items.Count - 1], "number", "POL-000-MADE-UP");
            d.Items.Add(new Item { Title = "Example Cloud", Type = "login", Username = "alex@example.com", Password = "made-up-cloud-pass", Urls = { "https://cloud.example.org" }, Folder = "Work", Updated = DateTime.Now.AddDays(-3) });
            Vaults.Put(d.Items[d.Items.Count - 1], work.Id);
            var now = DateTime.UtcNow; int k = 0;
            foreach (var it in d.Items.Where(i => i.Type == "login").ToList()) { Vaults.MarkUsed(d, it.Id, now.AddHours(-(k * k * 7 + 1))); k++; }
            Vaults.MarkUsed(d, d.Items.First(i => i.Type == "card").Id, now.AddDays(-12));
            return d;
        }
        void SnapExtra21()
        {
            Action<MainWindow, string> big = (w, n) => Snap.Save(w, Path.Combine(snapDir, n), 1280, 760);
            Action<MainWindow, string> tall = (w, n) => Snap.Save(w, Path.Combine(snapDir, n), 1280, 2000);
            var w7 = new MainWindow(false, true); w7.vault = new Vault { Data = FakeData21() };
            var sk = w7.S; sk.SyncOn = false;
            w7.view = "all"; w7.ShowMain(); big(w7, "v21-main-all.png");
            foreach (var t in Vaults.Tabs.Where(x => x != "all")) { w7.tab = t; w7.otherType = null; w7.ShowMain(); big(w7, "v21-tab-" + t + ".png"); }
            w7.tab = "other"; w7.otherType = "sshkey"; w7.ShowMain(); big(w7, "v21-tab-other-sshkey.png");
            w7.tab = "all"; w7.otherType = null; w7.sortCol = "used"; w7.sortDesc = true; w7.ShowMain(); big(w7, "v21-sort-lastused.png");
            w7.sortCol = "folder"; w7.sortDesc = false; w7.ShowMain(); big(w7, "v21-sort-folder.png");
            w7.sortCol = "title"; w7.sortDesc = true; w7.ShowMain(); big(w7, "v21-sort-title-desc.png");
            w7.sortCol = null;
            foreach (var it in w7.vault.Data.Items.Where(i => i.Type == "login").Take(3)) w7.picked.Add(it.Id);
            w7.ShowMain(); big(w7, "v21-bulk-bar.png"); w7.picked.Clear();
            w7.selected = w7.vault.Data.Items[0]; w7.ShowMain(); big(w7, "v21-detail-open.png");
            w7.StartEdit(w7.vault.Data.Items[0], false); big(w7, "v21-edit-vault-picker.png"); tall(w7, "v21-edit-tall.png"); w7.editing = null;
            w7.selected = null; w7.curVault = w7.vault.Data.Vaults.First(v => v.Name == "Gaming").Id; w7.ShowMain(); big(w7, "v21-vault-gaming.png");
            w7.curVault = null; w7.lastSearch = "example"; w7.ShowMain(); big(w7, "v21-search.png"); w7.lastSearch = "";
            w7.view = "vaults"; w7.ShowMain(); tall(w7, "v21-vaults-page.png");
            // site icons: made-up icons drawn here (nothing is fetched in --snap)
            sk.SiteIcons = true;
            foreach (var h in w7.vault.Data.Items.Select(SiteIcons.HostFor).Where(x => x != null).Distinct().Take(4)) w7.iconImages[h] = FakeIcon(h);
            w7.view = "all"; w7.ShowMain(); big(w7, "v21-site-icons.png");
            w7.view = "settings"; w7.ShowMain(); tall(w7, "v21-settings.png");
            sk.SiteIcons = false; w7.iconImages.Clear();
            sk.TravelMode = true; sk.TravelSince = DateTime.Now.ToString("d MMM yyyy HH:mm");
            w7.view = "all"; w7.ShowMain(); big(w7, "v21-travel-main.png");
            w7.view = "vaults"; w7.ShowMain(); tall(w7, "v21-travel-vaults.png");
            sk.TravelMode = false;
            var w8 = new MainWindow(false, true); w8.vault = new Vault { Data = FakeData() }; w8.view = "all"; w8.ShowMain(); big(w8, "v21-one-vault.png");
            SnapExtra23();   // v2.3
        }
        static ImageSource FakeIcon(string seed)
        {
            var dv = new DrawingVisual(); var col = (Color)ColorConverter.ConvertFromString(SiteIcons.TileColor(seed + "x"));
            using (var dc = dv.RenderOpen()) { dc.DrawEllipse(new SolidColorBrush(col), null, new Point(16, 16), 14, 14); dc.DrawRectangle(Brushes.White, null, new Rect(11, 11, 10, 10)); }
            var rt = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); rt.Render(dv); rt.Freeze(); return rt;
        }
    }
}
