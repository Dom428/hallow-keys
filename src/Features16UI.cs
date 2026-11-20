// Hallow Keys v1.6 - the window parts of "Every kind of item": the new types' fields in an item's details and editor, your own
// fields (add, rename, move, remove), several websites per login, hidden fields (masked until Show; a recovery phrase asks first),
// field history, Duplicate and Save as template, the Expiring page, expiry reminders, and the Settings cards for both.
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
        const string Dots = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
        TextBlock Label16(StackPanel sp, string text) { var t = new TextBlock { Text = text, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap }; sp.Children.Add(t); return t; }

        // a recovery phrase is shown or copied only after a yes (developer snapshots: never asked, never shown)
        bool ConfirmSeed(string what)
        {
            if (Snap.On) return false;
            return MessageBox.Show(this, what + " the recovery phrase?\n\nAnyone who sees it - over your shoulder, in a screen share, a recording or a photo - can take everything in this wallet. Make sure nobody is watching.",
                "Recovery phrase", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        // ---- details
        void FieldsDetail(StackPanel sp, Item it)
        {
            foreach (var f in ItemTypes.Ordered(it))
            {
                if (string.IsNullOrEmpty(f.Value)) continue;
                if (Passkeys.IsKeyField(it, f)) { PasskeyKeyRow(sp); continue; }   // v2.3: never shown or copied
                var label = string.IsNullOrEmpty(f.Label) ? ItemTypes.KindName[f.Kind] : f.Label;
                bool seed = ItemTypes.IsSeed(it, f);
                switch (f.Kind)
                {
                    case "totp": { var spec = Totp.Parse(f.Value); if (spec != null) TotpRow(sp, spec, label); else Field(sp, label, f.Value, true); break; }
                    case "url": UrlRow(sp, f.Value, label); break;
                    case "date":
                    {
                        DateTime d; string note = null;
                        if (f.Expires && Dates.TryParse(f.Value, out d)) note = Expiry.Describe((int)(d.Date - DateTime.Now.Date).TotalDays);
                        Field(sp, label, Dates.Show(f.Value), false, note); break;
                    }
                    case "note": NoteRow(sp, label, f.Value); break;
                    case "secretnote": SecretNoteRow(sp, label, f.Value, seed); break;
                    case "hidden": Field(sp, label, f.Value, true); break;
                    default: Field(sp, label, f.Value, false); break;
                }
            }
            if (it.Type == "wallet" && it.SeedOnPhone && ItemTypes.Val(it, "seed").Length > 0)
                sp.Children.Add(new TextBlock { Text = "This wallet's recovery phrase goes to the phone copy (Edit to change).", Foreground = B("#F0BE5A"), FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        void NoteRow(StackPanel sp, string label, string value)
        {
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 3) };
            var copy = new Button { Content = "Copy", Style = St("Btn") }; copy.Click += (s, e) => { if (SafeClip.Copy(value)) Toast(label + " copied - the clipboard clears in " + SafeClip.ClearSeconds + " seconds."); };
            DockPanel.SetDock(copy, Dock.Right); head.Children.Add(copy);
            head.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom });
            sp.Children.Add(head);
            sp.Children.Add(new TextBox { Style = St("Area"), Text = value, IsReadOnly = true, MaxHeight = 160, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        }
        // a hidden text of several lines (a private key, a recovery phrase): dots until Show
        void SecretNoteRow(StackPanel sp, string label, string value, bool seed)
        {
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 3) };
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            var show = new Button { Content = "Show", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) };
            var copy = new Button { Content = "Copy", Style = St("Btn") };
            bs.Children.Add(show); bs.Children.Add(copy); DockPanel.SetDock(bs, Dock.Right); head.Children.Add(bs);
            head.Children.Add(new TextBlock { Text = label + (seed ? "  -  hidden; asks before showing" : ""), Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom });
            sp.Children.Add(head);
            var masked = new TextBlock { Text = Dots, FontSize = 14, FontFamily = new FontFamily("Consolas") };
            var box = new TextBox { Style = St("Area"), Text = "", IsReadOnly = true, MaxHeight = 200, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            sp.Children.Add(masked); sp.Children.Add(box);
            show.Click += (s, e) =>
            {
                bool shown = box.Visibility == Visibility.Visible;
                if (!shown && seed && !ConfirmSeed("Show")) return;
                box.Text = shown ? "" : value; box.Visibility = shown ? Visibility.Collapsed : Visibility.Visible; masked.Visibility = shown ? Visibility.Visible : Visibility.Collapsed; show.Content = shown ? "Show" : "Hide";
            };
            copy.Click += (s, e) => { if (seed && !ConfirmSeed("Copy")) return; if (SafeClip.Copy(value)) Toast(label + " copied - the clipboard clears in " + SafeClip.ClearSeconds + " seconds."); };
        }

        // old values of hidden fields (masked, Show / Copy), behind the same toggle as password history
        void FieldHistorySection(StackPanel sp, Item it)
        {
            int n = ItemTypes.HistoryCount(it); if (n == 0) return;
            var toggle = new Button { Content = (showHistory ? "Hide" : "Show") + " field history (" + n + ")", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 2) };
            toggle.Click += (s, e) => { showHistory = !showHistory; ShowDetail(); };
            sp.Children.Add(toggle);
            if (!showHistory) return;
            foreach (var f in ItemTypes.Ordered(it).Where(x => x.Old.Count > 0))
                for (int i = 0; i < f.Old.Count; i++)
                {
                    DateTime d; var when = i < f.OldDates.Count && DateTime.TryParse(f.OldDates[i], null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d.ToLocalTime().ToString("d MMM yyyy HH:mm") : "an unknown date";
                    if (ItemTypes.IsSeed(it, f)) SecretNoteRow(sp, f.Label + " - used until " + when, f.Old[i], true);
                    else Field(sp, f.Label + " - used until " + when, f.Old[i], true);
                }
            var clear = new Button { Content = "Clear field history", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Forget the " + n + " old value" + (n == 1 ? "" : "s") + " of \"" + it.Title + "\"'s hidden fields?", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                foreach (var f in it.Fields) { f.Old.Clear(); f.OldDates.Clear(); }
                it.Updated = DateTime.Now; SaveVault("Field history cleared."); ShowDetail();
            };
            sp.Children.Add(clear);
        }

        void DetailExtraButtons(StackPanel sp, Item it)
        {
            if (it.Type == "passkey") return;   // v2.3: a copy of a passkey would be the same key twice
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var dup = new Button { Content = "Duplicate", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "A copy of this item (without its history), to change into a similar one" };
            dup.Click += (s, e) =>
            {
                var c = ItemTypes.Clone(it, true); c.Title = it.Title + " (copy)";
                vault.Data.Items.Insert(vault.Data.Items.IndexOf(it) + 1, c); selected = c; SaveVault("Made a copy of \"" + it.Title + "\"."); ShowMain();
            };
            var tpl = new Button { Content = "Save as template", Style = St("Btn"), ToolTip = "Keeps this item's type and fields (names and kinds - never the values) as a template for New item" };
            tpl.Click += (s, e) =>
            {
                var name = Ask.Text(this, "Save as template", "A name for the template. It keeps the type and the fields' names and kinds - none of the values.", "Name");
                if (string.IsNullOrWhiteSpace(name)) return;
                vault.Data.Templates.Add(ItemTypes.MakeTemplate(it, name.Trim())); SaveVault("Template \"" + name.Trim() + "\" saved - it's under New item.");
            };
            row.Children.Add(dup); row.Children.Add(tpl); sp.Children.Add(row);
        }

        // ---- editor
        // a hidden one-line value: dots, Show to see it; every change goes straight into the field
        UIElement SecretInput(string value, Action<string> set, bool mono)
        {
            var row = new DockPanel();
            var show = new Button { Content = "Show", Style = St("Btn"), Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(show, Dock.Right); row.Children.Add(show);
            var grid = new Grid();
            var pb = new PasswordBox { Style = St("Pwd"), Password = value ?? "" }; var tb = new TextBox { Style = St("Field"), Text = value ?? "", Visibility = Visibility.Collapsed, FontFamily = new FontFamily("Consolas") };
            grid.Children.Add(pb); grid.Children.Add(tb); row.Children.Add(grid);
            pb.PasswordChanged += (s, a) => { if (tb.Visibility != Visibility.Visible) { tb.Text = pb.Password; set(pb.Password); } };
            tb.TextChanged += (s, a) => { if (tb.Visibility == Visibility.Visible) { pb.Password = tb.Text; set(tb.Text); } };
            show.Click += (s, a) => { bool vis = tb.Visibility == Visibility.Visible; tb.Visibility = vis ? Visibility.Collapsed : Visibility.Visible; pb.Visibility = vis ? Visibility.Visible : Visibility.Collapsed; show.Content = vis ? "Show" : "Hide"; };
            return row;
        }
        // a hidden value of several lines: dots until Show (a recovery phrase asks first); typing replaces it once shown
        UIElement SecretArea(Func<string> get, Action<string> set, bool seed)
        {
            var sp = new StackPanel();
            var head = new DockPanel();
            var show = new Button { Content = "Show", Style = St("Btn"), Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(show, Dock.Right); head.Children.Add(show);
            var masked = new TextBlock { Text = string.IsNullOrEmpty(get()) ? "(empty - click Show to type or paste it)" : Dots + "  (hidden - click Show to see or change it)", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(masked); sp.Children.Add(head);
            var box = new TextBox { Style = St("Area"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, MinHeight = 70, MaxHeight = 220, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            box.TextChanged += (s, a) => { if (box.Visibility == Visibility.Visible) set(box.Text); };
            sp.Children.Add(box);
            show.Click += (s, a) =>
            {
                bool vis = box.Visibility == Visibility.Visible;
                if (!vis && seed && !string.IsNullOrEmpty(get()) && !ConfirmSeed("Show")) return;
                if (vis) { box.Visibility = Visibility.Collapsed; box.Text = ""; masked.Visibility = Visibility.Visible; masked.Text = string.IsNullOrEmpty(get()) ? "(empty - click Show to type or paste it)" : Dots + "  (hidden - click Show to see or change it)"; show.Content = "Show"; }
                else { box.Text = get(); box.Visibility = Visibility.Visible; masked.Visibility = Visibility.Collapsed; show.Content = "Hide"; box.Focus(); }
            };
            return sp;
        }
        UIElement ValueInput(Item e, CustomField f)
        {
            bool seed = ItemTypes.IsSeed(e, f);
            switch (f.Kind)
            {
                case "hidden": return SecretInput(f.Value, v => f.Value = v, false);
                case "totp": return SecretInput(f.Value, v => f.Value = v, true);
                case "secretnote": return SecretArea(() => f.Value, v => f.Value = v, seed);
                case "note": { var a = new TextBox { Style = St("Area"), Text = f.Value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, MinHeight = 60, MaxHeight = 160 }; a.TextChanged += (s, x) => f.Value = a.Text; return a; }
                default: { var t = new TextBox { Style = St("Field"), Text = f.Kind == "date" ? Dates.Normalize(f.Value) : f.Value }; t.TextChanged += (s, x) => f.Value = t.Text; return t; }
            }
        }
        static string KindHint(string kind) { return kind == "date" ? " (YYYY-MM-DD)" : ""; }

        void TypeFieldsEditor(StackPanel sp, Item e)
        {
            var t = ItemTypes.Get(e.Type); if (t == null || t.Legacy) return;
            ItemTypes.Ensure(e);
            foreach (var d in t.Fields)
            {
                var f = ItemTypes.Field(e, d.Key);
                if (d.Computed && e.Type == "passkey")   // v2.3: set by the website; the private key is never shown
                {
                    Label16(sp, d.Label + " - set when the site made the passkey");
                    var pv = Passkeys.IsKeyField(e, f) ? "Kept inside your vault - never shown" : f.Value;
                    sp.Children.Add(new TextBlock { Text = pv.Length > 0 ? pv : "-", FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = Passkeys.IsKeyField(e, f) ? Muted : (Brush)FindResource("Text") });
                    continue;
                }
                if (d.Computed)
                {
                    Label16(sp, d.Label + " - worked out from the public key when you save");
                    sp.Children.Add(new TextBlock { Text = f.Value.Length > 0 ? f.Value : "-", FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = f.Value.Length > 0 ? (Brush)FindResource("Text") : Muted });
                    continue;
                }
                Label16(sp, d.Label + (d.Hint.Length > 0 ? " (" + d.Hint + ")" : KindHint(d.Kind)) + (ItemTypes.IsSeed(e, f) ? " - never searched, not in the phone copy or CSV exports unless you allow it" : ""));
                sp.Children.Add(ValueInput(e, f));
            }
            if (e.Type == "sshkey")
            {
                var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var bits in new[] { 3072, 4096 })
                {
                    var b = bits; var g = new Button { Content = "Generate RSA " + b, Style = St("Btn"), Margin = new Thickness(0, 0, 6, 4), ToolTip = "A new RSA key made by Windows (CNG), in OpenSSH's format. Ed25519 keys can be pasted but not made here." };
                    g.Click += (s, a) =>
                    {
                        if ((ItemTypes.Val(e, "privateKey").Length > 0 || ItemTypes.Val(e, "publicKey").Length > 0) && MessageBox.Show(this, "Replace this item's key with a new one?", "SSH key", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                        string priv, pub; SshKeys.GenerateRsa(b, "hallow-keys", out priv, out pub);
                        foreach (var fl in editorFields.ToList()) fl();   // keep what was typed in the other boxes
                        ItemTypes.Set(e, "privateKey", priv); ItemTypes.Set(e, "publicKey", pub); PrepareFields(e);
                        detail.Child = Editor(e); Toast("New RSA " + b + " key made - copy the public key to the server, then Save.");
                    };
                    row.Children.Add(g);
                }
                sp.Children.Add(row);
            }
            if (e.Type == "wallet")
            {
                var c = new CheckBox { Content = new TextBlock { Text = "Put this wallet's recovery phrase in the phone copy (off: the phone never gets it)", TextWrapping = TextWrapping.Wrap }, IsChecked = e.SeedOnPhone, Style = St("Switch"), Margin = new Thickness(0, 8, 0, 0) };
                c.Click += (s, a) => e.SeedOnPhone = c.IsChecked == true; sp.Children.Add(c);
            }
        }

        // your own fields: add (any kind), rename, move up / down, remove
        void CustomFieldsEditor(StackPanel sp, Item e)
        {
            var head = new TextBlock { Text = "Your own fields", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) }; sp.Children.Add(head);
            var panel = new StackPanel(); sp.Children.Add(panel);
            Action fill = null;
            fill = () =>
            {
                panel.Children.Clear();
                var mine = e.Fields.Where(f => !ItemTypes.IsBuiltIn(e, f)).ToList();
                if (mine.Count == 0) panel.Children.Add(new TextBlock { Text = "None yet - Add field puts a text, hidden, web address, date, 2FA, email or phone field on this item. Hidden ones are masked and never searched.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                for (int k = 0; k < mine.Count; k++)
                {
                    var f = mine[k]; int idx = k;
                    var row = new DockPanel { Margin = new Thickness(0, 10, 0, 3) };
                    var bs = new StackPanel { Orientation = Orientation.Horizontal };
                    Action<int> move = dir =>
                    {
                        var other = mine[idx + dir]; int a = e.Fields.IndexOf(f), b = e.Fields.IndexOf(other);
                        e.Fields[a] = other; e.Fields[b] = f; fill();
                    };
                    var up = new Button { Content = "\u2191", Style = St("Btn"), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Move up", IsEnabled = idx > 0 }; up.Click += (s, a) => move(-1);
                    var down = new Button { Content = "\u2193", Style = St("Btn"), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Move down", IsEnabled = idx < mine.Count - 1 }; down.Click += (s, a) => move(1);
                    var rm = new Button { Content = "x", Style = St("Btn"), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Remove this field" };
                    rm.Click += (s, a) => { if (!string.IsNullOrEmpty(f.Value) && MessageBox.Show(this, "Remove the field \"" + f.Label + "\" and its value?", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return; e.Fields.Remove(f); fill(); };
                    bs.Children.Add(up); bs.Children.Add(down); bs.Children.Add(rm); DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                    var kind = new TextBlock { Text = (ItemTypes.KindName.ContainsKey(f.Kind) ? ItemTypes.KindName[f.Kind] : f.Kind) + KindHint(f.Kind), Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    DockPanel.SetDock(kind, Dock.Right); row.Children.Add(kind);
                    var name = new TextBox { Style = St("Field"), Text = f.Label, ToolTip = "The field's name - click to rename it", FontWeight = FontWeights.SemiBold };
                    name.TextChanged += (s, a) => f.Label = name.Text; row.Children.Add(name);
                    panel.Children.Add(row);
                    panel.Children.Add(ValueInput(e, f));
                    if (f.Kind == "date")
                    {
                        var ex = new CheckBox { Content = new TextBlock { Text = "It's an expiry date (shows in Expiring, with a reminder)" }, IsChecked = f.Expires, Style = St("Switch"), Margin = new Thickness(0, 4, 0, 0) };
                        ex.Click += (s, a) => f.Expires = ex.IsChecked == true; panel.Children.Add(ex);
                    }
                }
                var add = new Button { Content = "Add field", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                add.Click += (s, a) =>
                {
                    var m = new ContextMenu { PlacementTarget = add, Placement = PlacementMode.Bottom };
                    foreach (var kd in ItemTypes.Kinds) { var kk = kd; var mi = new MenuItem { Header = ItemTypes.KindName[kk] }; mi.Click += (s2, a2) => { e.Fields.Add(new CustomField { Kind = kk, Label = ItemTypes.KindName[kk] }); fill(); }; m.Items.Add(mi); }
                    m.IsOpen = true;
                };
                panel.Children.Add(add);
            };
            fill();
        }

        // several websites on a login: one box each, add and remove
        void UrlsEditor(StackPanel sp, Item e, List<Action> fields)
        {
            Label16(sp, "Websites - the browser offers this login on each of them (same site, or the same main domain; never on look-alikes or http)");
            var panel = new StackPanel(); sp.Children.Add(panel);
            var boxes = new List<TextBox>();
            Action<List<string>> fill = null;
            fill = urls =>
            {
                panel.Children.Clear(); boxes.Clear();
                if (urls.Count == 0) urls.Add("");
                foreach (var u in urls)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var tb = new TextBox { Style = St("Field"), Text = u }; boxes.Add(tb);
                    var rm = new Button { Content = "x", Style = St("Btn"), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove this website" };
                    rm.Click += (s, a) => { var cur = boxes.Where(b => b != tb).Select(b => b.Text).ToList(); fill(cur); };
                    DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm); row.Children.Add(tb); panel.Children.Add(row);
                }
                var add = new Button { Content = "Add website", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left };
                add.Click += (s, a) => { var cur = boxes.Select(b => b.Text).ToList(); cur.Add(""); fill(cur); boxes.Last().Focus(); };
                panel.Children.Add(add);
            };
            fill(e.Urls.ToList());
            fields.Add(() => e.Urls = boxes.Select(b => (b.Text ?? "").Trim()).Where(x => x.Length > 0).Distinct().ToList());
        }

        // before saving: dates tidied, 2FA fields checked, an SSH key's type and fingerprint worked out. Returns a problem, or null.
        string PrepareFields(Item e)
        {
            foreach (var f in e.Fields)
            {
                if (f.Kind != "note" && f.Kind != "secretnote") f.Value = (f.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(f.Label)) f.Label = ItemTypes.KindName.ContainsKey(f.Kind) ? ItemTypes.KindName[f.Kind] : "Field";
                f.Label = f.Label.Trim();
                if (f.Kind == "date" && f.Value.Length > 0) { DateTime d; if (!Dates.TryParse(f.Value, out d)) return "\"" + f.Label + "\" isn't a date - use YYYY-MM-DD, e.g. 2030-04-17."; f.Value = d.ToString("yyyy-MM-dd"); }
                if (f.Kind == "totp" && f.Value.Length > 0 && Totp.Parse(f.Value) == null) return "\"" + f.Label + "\" isn't an otpauth:// link or a valid 2FA setup key.";
            }
            if (e.Type == "sshkey")
            {
                var pub = ItemTypes.Val(e, "publicKey").Trim(); var priv = ItemTypes.Val(e, "privateKey");
                var blob = SshKeys.PublicBlob(pub); bool fromPrivate = false;
                if (blob == null) { blob = SshKeys.PublicBlobFromPrivate(priv); fromPrivate = blob != null; }
                var info = SshKeys.Describe(blob);
                if (info != null && fromPrivate && pub.Length == 0) ItemTypes.Set(e, "publicKey", SshKeys.PublicLine(blob, ""));
                ItemTypes.Set(e, "fingerprint", info != null ? info.Fingerprint : ""); ItemTypes.Set(e, "keyType", info != null ? info.Pretty : "");
            }
            return null;
        }

        // ---- expiry dates
        UIElement ExpiringPage()
        {
            ScrollViewer sv; var sp = Page("Expiring", "Cards, passports, driving licences, memberships, software licences, API credentials and your own date fields marked as expiry dates. " +
                (S.ExpiryReminders ? "You get a reminder " + S.ExpiryDays + " days before (Settings > Expiry reminders)." : "Reminders are off (Settings > Expiry reminders)."), out sv);
            var all = Expiry.All(Shown(), DateTime.Now);
            if (all.Count == 0) { sp.Children.Add(new TextBlock { Text = "Nothing with an expiry date yet.", Foreground = Muted }); return sv; }
            Action<string, IEnumerable<ExpiryEntry>, string> group = (head, es, color) =>
            {
                var list = es.ToList(); if (list.Count == 0) return;
                sp.Children.Add(new TextBlock { Text = head, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4), Foreground = color == null ? (Brush)FindResource("Text") : B(color) });
                foreach (var x in list) sp.Children.Add(ExpiryRow(x));
            };
            group("Expired", all.Where(x => x.DaysLeft < 0), "#FF6B6B");
            group("Within " + S.ExpiryDays + " days", all.Where(x => x.DaysLeft >= 0 && x.DaysLeft <= S.ExpiryDays), "#F0BE5A");
            group("Later", all.Where(x => x.DaysLeft > S.ExpiryDays), null);
            return sv;
        }
        UIElement ExpiryRow(ExpiryEntry x)
        {
            var item = x.Item; var b = new Button { Style = St("Nav"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var d = new DockPanel();
            var w = new TextBlock { Text = x.Date.ToString("d MMM yyyy") + "  -  " + Expiry.Describe(x.DaysLeft), Foreground = x.DaysLeft < 0 ? B("#FF6B6B") : x.DaysLeft <= S.ExpiryDays ? B("#F0BE5A") : Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(w, Dock.Right); d.Children.Add(w);
            var ic = Icons.Make(ItemTypes.Icon(item.Type), 15, Muted); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; d.Children.Add(ic);
            d.Children.Add(new TextBlock { Text = item.Title + "  -  " + x.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            b.Content = d; b.Click += (s, e) => { view = "all"; lastSearch = ""; selected = item; editing = null; ShowMain(); };
            return b;
        }
        // in Password Health
        void ExpirySection(StackPanel sp)
        {
            var soon = Expiry.Soon(Shown(), DateTime.Now, S.ExpiryDays);
            var box = new StackPanel();
            var head = new DockPanel();
            var open = new Button { Content = "Open Expiring", Style = St("Btn") }; open.Click += (s, e) => { view = "expiring"; ShowMain(); };
            DockPanel.SetDock(open, Dock.Right); head.Children.Add(open);
            head.Children.Add(new TextBlock { Text = "Expiring items", FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(head);
            int expired = soon.Count(x => x.DaysLeft < 0), coming = soon.Count - expired;
            box.Children.Add(new TextBlock { Text = soon.Count == 0 ? "Nothing has expired or expires in the next " + S.ExpiryDays + " days." : (expired > 0 ? expired + " expired" : "") + (expired > 0 && coming > 0 ? ", " : "") + (coming > 0 ? coming + " within " + S.ExpiryDays + " days" : "") + ".",
                Foreground = soon.Count == 0 ? B("#6ED28C") : expired > 0 ? B("#FF6B6B") : B("#F0BE5A"), Margin = new Thickness(0, 4, 0, 4) });
            foreach (var x in soon.Take(5)) box.Children.Add(ExpiryRow(x));
            sp.Children.Add(Card(box));
        }

        // reminders: when unlocked and every few hours; once per date when it comes within the window, once more when it has passed.
        // The notification has only counts and kinds ("1 passport and 1 card expire within 30 days") - never names, numbers or values.
        DispatcherTimer expiryTimer;
        void AfterUnlock16()
        {
            if (expiryTimer == null) { expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) }; expiryTimer.Tick += (s, e) => CheckExpiry(); expiryTimer.Start(); }
            Dispatcher.BeginInvoke(new Action(CheckExpiry), DispatcherPriority.ApplicationIdle);
        }
        void CheckExpiry()
        {
            if (vault == null || !S.ExpiryReminders) return;
            var msg = Expiry.DueMessage(Shown(), DateTime.Now, S.ExpiryDays);
            if (msg == null) return;
            SaveVault(null);
            TestLog("expiry notify: " + msg);
            Notify("Hallow Keys - expiry reminder", msg);
        }

        // ---- Settings cards
        void ExpiryCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Expiry reminders", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "A notification (in Hallow Hub's tray when it runs) before a card, passport, licence, membership or your own expiry date runs out, and once more when it has. It only says how many and what kind - never names, numbers or anything secret. Needs Hallow Keys unlocked (the dates are inside your encrypted vault).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            box.Children.Add(Switch("Remind me before things expire", S.ExpiryReminders, v => { S.ExpiryReminders = v; S.Save(); if (v) CheckExpiry(); }));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = "Remind me", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var days = new[] { 7, 14, 30, 60, 90 };
            var cb = new ComboBox { Width = 120, Style = St("Combo"), ItemsSource = days.Select(d => d + " days").ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(days, S.ExpiryDays)) };
            if (Array.IndexOf(days, S.ExpiryDays) < 0) cb.SelectedIndex = 2;
            cb.SelectionChanged += (s, e) => { S.ExpiryDays = days[cb.SelectedIndex]; S.Save(); };
            row.Children.Add(cb); row.Children.Add(new TextBlock { Text = "before", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            box.Children.Add(row);
            sp.Children.Add(Card(box));
        }
        void TemplatesCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Item templates", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Your own layouts for New item: open an item, set up its fields, then click Save as template. A template keeps the type and the fields' names and kinds - never the values.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            if (vault.Data.Templates.Count == 0) box.Children.Add(new TextBlock { Text = "No templates yet.", Foreground = Muted });
            foreach (var t in vault.Data.Templates.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
            {
                var tpl = t; var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var bs = new StackPanel { Orientation = Orientation.Horizontal };
                var ren = new Button { Content = "Rename", Style = St("Btn"), Margin = new Thickness(0, 0, 6, 0) };
                ren.Click += (s, e) => { var n = Ask.Text(this, "Rename template", "A new name for \"" + tpl.Name + "\":", "Name"); if (string.IsNullOrWhiteSpace(n)) return; tpl.Name = n.Trim(); SaveVault("Template renamed."); ShowMain(); };
                var del = new Button { Content = "Delete template", Style = St("Btn") };
                del.Click += (s, e) => { if (MessageBox.Show(this, "Delete the template \"" + tpl.Name + "\"? Items made from it stay as they are.", "Hallow Keys", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return; vault.Data.Templates.Remove(tpl); SaveVault("Template deleted."); ShowMain(); };
                bs.Children.Add(ren); bs.Children.Add(del); DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                var ic = Icons.Make(ItemTypes.Icon(tpl.Type), 14, Muted); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(ic);
                row.Children.Add(new TextBlock { Text = tpl.Name + "  -  " + ItemTypes.Name(tpl.Type) + ", " + tpl.Fields.Count + " field" + (tpl.Fields.Count == 1 ? "" : "s"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                box.Children.Add(row);
            }
            sp.Children.Add(Card(box));
        }

        // ---- developer snapshots: every new type's details and editor, your own fields, the Expiring page (made-up data only)
        void SnapExtra16(Action<MainWindow, string> save)
        {
            Action<MainWindow, string> tall = (w, n) => Snap.Save(w, System.IO.Path.Combine(snapDir, n), 1060, 1250);
            foreach (var it in vault.Data.Items.Where(i => ItemTypes.IsNew(i.Type)).ToList())
            {
                view = "all"; lastSearch = ""; editing = null; selected = it; ShowMain(); tall(this, "type-" + it.Type + ".png");
                StartEdit(it, false); tall(this, "edit-" + it.Type + ".png"); editing = null;
            }
            var custom = vault.Data.Items.FirstOrDefault(i => i.Type == "login" && i.Fields.Count > 0);
            if (custom != null) { view = "all"; selected = custom; showHistory = true; ShowMain(); tall(this, "custom-fields.png"); StartEdit(custom, false); tall(this, "edit-custom-fields.png"); editing = null; showHistory = false; }
            view = "expiring"; ShowMain(); save(this, "expiring.png");
            view = "health"; ShowMain(); tall(this, "health-expiring.png");
            view = "settings"; ShowMain(); tall(this, "settings-16.png");
            view = "sshkey"; selected = null; ShowMain(); save(this, "nav-sshkeys.png");
            view = "all"; lastSearch = "wallet"; selected = null; ShowMain(); save(this, "search-wallet.png"); lastSearch = "";
            SnapExtra17(save);   // v1.7
        }
    }
}
