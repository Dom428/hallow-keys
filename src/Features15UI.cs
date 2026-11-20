// Hallow Keys v1.5 - the window parts of desktop app autofill: its shortcut (off, no combination until you pick one), the Settings
// card, the small chooser when several logins match, and the "Apps" field on logins (with a picker of running programs).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        GlobalHotkey fillKey; bool fillKeyTaken, capturingFillKey; DispatcherTimer fillRetry;
        IForeground fgApi; IKeyTyper typer; IModifierState mods;
        public const int DesktopFillHotId = 0x4B5A;

        void StartDesktopFill()
        {
            fgApi = new WinForeground(); typer = new SendInputTyper(); mods = new WinModifiers();
            if (KeysSettings.TestDir != null)
            {
                // developer tests: never real key presses - a stand-in that only logs how many characters it would type;
                // HALLOW_KEYS_TEST_FOREGROUND="exe[|elevated][|fullscreen]" pretends that program is in front
                typer = new LogTyper(); mods = new NoModifiers();
                var f = Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_FOREGROUND"); if (!string.IsNullOrEmpty(f)) fgApi = new FakeForeground(f);
            }
            try { fillKey = new GlobalHotkey(DesktopFillPressed, "HallowKeysDesktopFill", DesktopFillHotId); } catch { fillKey = null; return; }
            ApplyFillKey();
            fillRetry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            fillRetry.Tick += (s, e) => { if (fillKeyTaken && S.DesktopFill && !capturingFillKey) ApplyFillKey(); };
            fillRetry.Start();
        }
        void ApplyFillKey()
        {
            if (fillKey == null) return;
            if (!S.DesktopFill || string.IsNullOrEmpty(S.DesktopFillCombo)) { fillKey.Clear(); fillKeyTaken = false; TestLog("desktop fill key off"); return; }
            if (S.QuickSearchKey && string.Equals(S.QuickSearchCombo, S.DesktopFillCombo, StringComparison.OrdinalIgnoreCase)) { fillKey.Clear(); fillKeyTaken = true; TestLog("desktop fill key same as quick search"); return; }
            bool was = fillKeyTaken;
            fillKeyTaken = !fillKey.Set(S.DesktopFillCombo);
            if (fillKeyTaken != was || !fillKeyTaken) TestLog(fillKeyTaken ? "desktop fill key taken " + S.DesktopFillCombo : "desktop fill key on " + S.DesktopFillCombo);
        }
        void StopDesktopFill() { if (fillRetry != null) fillRetry.Stop(); if (fillKey != null) fillKey.Close(); fillKey = null; }
        static bool TestNoShow { get { return KeysSettings.TestDir != null && Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_NOSHOW") == "1"; } }

        void DesktopFillPressed()
        {
            TestLog("desktop fill pressed");
            if (!S.DesktopFill) return;
            var fg = fgApi.Get();   // what's in front right now, before Keys does anything
            var appData = KeysSettings.TestDir ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var game = DesktopFill.ReadGameState(appData, DateTime.Now);
            var d = DesktopFill.Decide(vault == null ? null : Shown(), fg, WinForeground.SelfElevated(), game, System.Diagnostics.Process.GetCurrentProcess().Id);
            TestLog("desktop fill: " + d.Refusal + (d.Refusal == FillRefusal.None ? " " + d.Matches.Count + " match" + (d.Matches.Count == 1 ? "" : "es") : ""));
            if (d.Refusal == FillRefusal.Locked)
            {
                Notify("Hallow Keys is locked", "Unlock it, then click back into " + DesktopFill.Describe(fg.Exe) + " and press " + S.DesktopFillCombo + " again.");
                if (!TestNoShow) ShowFromTray();   // offer to unlock (only for a normal program in front - games were refused above)
                return;
            }
            if (d.Refusal != FillRefusal.None) { Notify("Hallow Keys didn't type anything", d.Message); return; }
            if (d.Matches.Count == 1) { TypeInto(d.Matches[0], fg); return; }
            if (TestNoShow)
            {
                int pick; var c = Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_CHOOSE");
                TestLog("desktop fill chooser " + d.Matches.Count);
                if (int.TryParse(c, out pick) && pick >= 0 && pick < d.Matches.Count) TypeInto(d.Matches[pick], fg);
                return;
            }
            ShowChooser(d.Matches, fg);
        }
        void TypeInto(Item it, ForegroundInfo fg)
        {
            var copy = new Item { Username = it.Username, Password = it.Password, Apps = it.Apps.ToList() }; var usedId = it.Id;
            bool self = WinForeground.SelfElevated(); var api = fgApi; var t = typer; var m = mods;
            new System.Threading.Thread(() =>
            {
                FillRefusal r;
                try { r = DesktopFill.TypeLogin(copy, fg, self, api, t, m); } catch { r = FillRefusal.TypingBlocked; }
                copy.Password = null; copy.Username = null;
                Dispatcher.BeginInvoke(new Action(() => { TestLog("desktop fill typed: " + r); if (r != FillRefusal.None) Notify("Hallow Keys", DesktopFill.Say(r)); else MarkUsedId(usedId); }));
            }) { IsBackground = true }.Start();
        }

        // several logins for the program in front: a small list (titles and usernames), Enter or a click types the one picked
        void ShowChooser(List<Item> items, ForegroundInfo fg)
        {
            var w = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, SizeToContent = SizeToContent.Height, Width = 340,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = (Brush)FindResource("Bg"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), Title = "Hallow Keys" };
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Type which login into " + DesktopFill.Describe(fg.Exe) + "?", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Foreground = (Brush)FindResource("Text") });
            var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 300 };
            foreach (var it in items)
            {
                var row = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
                row.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Text") });
                row.Children.Add(new TextBlock { Text = it.Username, Foreground = Muted, FontSize = 11.5 });
                list.Items.Add(new ListBoxItem { Content = row, Tag = it });
            }
            list.SelectedIndex = 0; sp.Children.Add(list);
            sp.Children.Add(new TextBlock { Text = "Enter types it - Esc cancels", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
            w.Content = sp;
            bool done = false;
            Action<Item> pick = it =>
            {
                if (done) return; done = true; w.Close();
                fgApi.Activate(fg.Hwnd);   // back to the program (allowed: Keys is in front right now)
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; t.Tick += (s, e) => { t.Stop(); TypeInto(it, fg); }; t.Start();
            };
            w.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { done = true; w.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && list.SelectedItem != null) { e.Handled = true; pick((Item)((ListBoxItem)list.SelectedItem).Tag); }
            };
            list.MouseDoubleClick += (s, e) => { if (list.SelectedItem != null) pick((Item)((ListBoxItem)list.SelectedItem).Tag); };
            w.Deactivated += (s, e) => { if (!done) { done = true; w.Close(); } };
            w.Loaded += (s, e) => { w.Activate(); list.Focus(); var c = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem; if (c != null) c.Focus(); };
            w.Show();
        }

        // ---- Settings card
        void DesktopFillCard(StackPanel sp)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Desktop app autofill", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "Click into a program's sign-in box (Steam, Battle.net, Riot Client, Epic, EA app, Ubisoft Connect...) and press your shortcut: Hallow Keys types the username, Tab and the password - never Enter, never through the clipboard. It only types into programs you've added under Apps on a login, and never into games, full-screen windows or programs running as administrator. Off until you turn it on and pick a combination.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            Action show = () =>
            {
                if (!S.DesktopFill) { status.Text = "Off"; status.Foreground = Muted; }
                else if (string.IsNullOrEmpty(S.DesktopFillCombo)) { status.Text = "Click the box and press the combination you want."; status.Foreground = B("#F0BE5A"); }
                else if (!Snap.On && (fillKey == null || fillKeyTaken)) { status.Text = S.DesktopFillCombo + " is already used (by another app or quick search) - it's off. Pick another combination."; status.Foreground = B("#F0BE5A"); }
                else { status.Text = "On"; status.Foreground = B("#6ED28C"); }
            };
            box.Children.Add(Switch("Type logins into desktop apps with a shortcut", S.DesktopFill, v => { S.DesktopFill = v; S.Save(); ApplyFillKey(); show(); }));
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            Func<string> shown = () => string.IsNullOrEmpty(S.DesktopFillCombo) ? "Not set" : S.DesktopFillCombo;
            var keyBox = new TextBox { Style = St("Field"), Text = shown(), IsReadOnly = true, Width = 210, Cursor = Cursors.Hand, ToolTip = "Click, then press the combination you want (Esc to cancel, Delete to clear)" };
            keyBox.GotKeyboardFocus += (s, e) => { capturingFillKey = true; if (fillKey != null) fillKey.Clear(); keyBox.Text = "Press a combination..."; };
            keyBox.LostKeyboardFocus += (s, e) => { capturingFillKey = false; keyBox.Text = shown(); ApplyFillKey(); show(); };
            keyBox.Unloaded += (s, e) => { if (capturingFillKey) { capturingFillKey = false; ApplyFillKey(); } };
            keyBox.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Escape || k == Key.Tab) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); return; }
                if (k == Key.Delete || k == Key.Back) { S.DesktopFillCombo = ""; S.Save(); Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); return; }
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                var combo = GlobalHotkey.Format(Keyboard.Modifiers, win, k);
                if (combo == null) return;
                uint m, vk;
                if (!GlobalHotkey.Parse(combo, out m, out vk)) { status.Text = "Use Ctrl, Alt or Win with a key (" + combo + " isn't allowed)."; status.Foreground = B("#F0BE5A"); return; }
                if (S.QuickSearchKey && string.Equals(combo, S.QuickSearchCombo, StringComparison.OrdinalIgnoreCase)) { status.Text = combo + " is your quick search shortcut - pick another one."; status.Foreground = B("#F0BE5A"); return; }
                if (fillKey != null && fillKey.Set(combo)) { fillKey.Clear(); S.DesktopFillCombo = combo; S.Save(); keyBox.Text = combo; Toast("Desktop autofill shortcut: " + combo + "."); }
                else { status.Text = combo + " is used by another app - try another one."; status.Foreground = B("#F0BE5A"); return; }
                Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this);
            };
            DockPanel.SetDock(keyBox, Dock.Left); row.Children.Add(keyBox); row.Children.Add(status);
            box.Children.Add(row); show();
            sp.Children.Add(Card(box));
        }

        // ---- the Apps field on a login (editor) and in its details
        void AppsEditor(StackPanel sp, Item e, List<Action> fields)
        {
            sp.Children.Add(new TextBlock { Text = "Apps - desktop programs this login types into (Settings > Desktop app autofill)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox { Style = St("Field"), Text = string.Join(", ", e.Apps), ToolTip = "Program file names, separated by commas, e.g. steam.exe" };
            sp.Children.Add(box); fields.Add(() => e.Apps = DesktopFill.ParseApps(box.Text));
            Action<string> add = exe => { var cur = DesktopFill.ParseApps(box.Text); if (!cur.Contains(exe)) { cur.Add(exe); box.Text = string.Join(", ", cur); } };
            var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var x in DesktopFill.Suggested) { var exe = x; var c = new Button { Content = DesktopFill.SuggestedNames[exe], Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4), ToolTip = "Add " + exe }; c.Click += (s, a) => add(exe); chips.Children.Add(c); }
            var pickBtn = new Button { Content = "Pick a running program...", Style = St("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4) };
            pickBtn.Click += (s, a) =>
            {
                var menu = new ContextMenu { PlacementTarget = pickBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                var running = RunningApps.List();
                if (running.Count == 0) menu.Items.Add(new MenuItem { Header = "No programs with a window found", IsEnabled = false });
                foreach (var r in running) { var exe = r; var mi = new MenuItem { Header = DesktopFill.Describe(exe) }; mi.Click += (s2, a2) => add(exe); menu.Items.Add(mi); }
                menu.IsOpen = true;
            };
            chips.Children.Add(pickBtn);
            sp.Children.Add(chips);
        }
        void AppsRow(StackPanel sp, Item it)
        {
            if (it.Apps.Count == 0) return;
            sp.Children.Add(new TextBlock { Text = "Apps (desktop autofill)", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) });
            sp.Children.Add(new TextBlock { Text = string.Join(", ", it.Apps.Select(DesktopFill.Describe)), FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
        }
    }

    // ---- test stand-ins (used only inside HALLOW_KEYS_TESTDIR): never press keys, never log item data
    public class LogTyper : IKeyTyper
    {
        static void Log(string l) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(KeysSettings.Dir, "test-events.log"), l + "\r\n"); } catch { } }
        public bool Type(IntPtr expected, string text) { Log("stand-in typed " + text.Length + " characters"); return true; }
        public bool Tab(IntPtr expected) { Log("stand-in typed Tab"); return true; }
    }
    public class NoModifiers : IModifierState { public bool AnyDown() { return false; } }
    // "exe[|elevated][|fullscreen]" from HALLOW_KEYS_TEST_FOREGROUND, or from test-foreground.txt in the test folder when it exists (read each time)
    public class FakeForeground : IForeground
    {
        readonly string spec;
        public FakeForeground(string spec) { this.spec = spec; }
        public ForegroundInfo Get()
        {
            var s = spec; var file = System.IO.Path.Combine(KeysSettings.Dir, "test-foreground.txt");
            try { if (System.IO.File.Exists(file)) s = System.IO.File.ReadAllText(file).Trim(); } catch { }
            var p = s.Split('|');
            return new ForegroundInfo { Hwnd = new IntPtr(0x5151), Pid = 424242, Exe = p[0].ToLowerInvariant(), Elevated = p.Contains("elevated"), Fullscreen = p.Contains("fullscreen"), Ok = p[0].Length > 0 };
        }
        public bool Activate(IntPtr hwnd) { return true; }
    }
}
