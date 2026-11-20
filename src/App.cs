// Hallow Keys - a personal password manager for Windows. Everything stays on this PC, in one encrypted file.
// The window: lock screen, create a vault (with a recovery sheet), and the vault itself (Views.cs, Pages.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow Keys")]
[assembly: AssemblyProduct("Hallow Keys")]
[assembly: AssemblyDescription("A personal password manager that keeps everything on your PC")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace HallowKeys
{
    public static class Program
    {
        public const string Version = "2.0";
        public static string AccentHex = Accent.Default;
        [STAThread]
        public static int Main(string[] args)
        {
            // v1.1: started by a browser as the native messaging host (no window, nothing else)
            if (args.Contains("--native-host") || args.Any(a => a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) || args.Contains(NativeHost.FirefoxId) || (args.Length >= 2 && args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))) return NativeHost.Run(args);   // v1.5: Firefox passes the manifest path, then the add-on ID
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0) return Family.RunCmd(ci + 1 < args.Length ? args[ci + 1] : "");
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; KeysSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                MainWindow.SnapAll(args[si + 1]); return 0;
            }
            bool created;
            var mutex = new Mutex(true, @"Local\HallowKeys" + (KeysSettings.TestDir != null ? "Test" : ""), out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowKeysShow" + (KeysSettings.TestDir != null ? "Test" : ""));
            if (!created) { showEvent.Set(); return 0; }
            Crash.Install();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); e.Handled = true; Environment.Exit(1); };
            var w = new MainWindow(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
        public static void LoadTheme(Application app)
        {
            string xaml;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowKeys.Theme.xaml")) using (var r = new System.IO.StreamReader(s)) xaml = r.ReadToEnd();
            AccentHex = Accent.Current(Snap.On ? false : KeysSettings.Load().UseHallowAccent);
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Accent.ApplyTo(xaml, AccentHex)));
        }
    }

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        public static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        public readonly KeysSettings S;
        public Vault vault;                 // null while locked
        readonly bool startHidden; bool exiting, toldTray, hubHosts, balloonOwn;
        WF.NotifyIcon tray; Border root;

        public MainWindow(bool hidden) : this(hidden, false) { }
        MainWindow(bool hidden, bool snap)
        {
            S = snap ? new KeysSettings() : KeysSettings.Load(); startHidden = hidden;
            Title = "Hallow Keys"; Width = 1060; Height = 700; MinWidth = 860; MinHeight = 560;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            root = new Border(); Content = root;
            SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
            if (snap) return;
            Crash.Scrub = ScrubSecrets;
            Closing += (s, e) =>
            {
                if (!exiting && S.TrayOnClose) { e.Cancel = true; Hide(); if (!toldTray) { toldTray = true; Notify("Hallow Keys", "Still running in the tray (it locks itself after " + S.AutoLockMinutes + " idle minutes)."); } }
                else Quit();
            };
            SetupTray(); Family.Serve("Keys", Command); WriteStatus();
            ShowLock();
            // auto-lock: idle minutes (no mouse or keyboard on this PC), Windows lock, sleep
            var idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            idle.Tick += (s, e) => { if (vault != null && S.AutoLockMinutes > 0 && IdleSeconds() >= S.AutoLockMinutes * 60) Lock("Locked after " + S.AutoLockMinutes + " idle minutes."); };
            idle.Start();
            SystemEvents.SessionSwitch += (s, e) => { if (e.Reason == SessionSwitchReason.SessionLock) Dispatcher.BeginInvoke(new Action(() => Lock("Locked because Windows was locked."))); };
            SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(new Action(() => Lock("Locked because the PC went to sleep."))); };
            var host = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; host.Tick += (s, e) => CheckHost(); host.Start(); CheckHost();
            accentWatch = Accent.Watch(() => Dispatcher.BeginInvoke(new Action(ApplyAccent)));
            Loaded += (s, e) => ShowCrashNotice();
            if (startHidden) Dispatcher.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle);
            if (KeysSettings.TestDir == null || Environment.GetEnvironmentVariable("HALLOW_KEYS_API") != null)
            {
                var upd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                upd.Tick += (s, e) => { upd.Interval = TimeSpan.FromHours(6); DateTime last; if (!DateTime.TryParse(S.LastUpdateCheck, out last) || (DateTime.Now - last).TotalHours >= 24 || KeysSettings.TestDir != null) CheckForUpdate(false); };
                upd.Start();
            }
            StartNative();
            StartHotkey();
            StartDesktopFill();
            ExtraInit();
            if (!startHidden) Show();
        }
        partial void ExtraInit();
        static uint IdleSeconds() { var i = new LASTINPUTINFO { cbSize = 8 }; return GetLastInputInfo(ref i) ? (uint)((uint)Environment.TickCount - i.dwTime) / 1000 : 0; }
        public static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        Brush Muted { get { return (Brush)FindResource("Muted"); } }
        Style St(string k) { return (Style)FindResource(k); }

        // ---- lock screen / create vault
        TextBlock lockStatus;
        void ShowLock() { ShowLock(null); }
        void ShowLock(string message)
        {
            var exists = snapVaultExists ?? System.IO.File.Exists(KeysSettings.VaultPath);
            var card = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(16), Padding = new Thickness(30, 26, 30, 26), Width = 440, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel(); card.Child = sp;
            sp.Children.Add(TitleRow(26));
            lockStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = B("#F0BE5A") };
            if (exists)
            {
                sp.Children.Add(new TextBlock { Text = "Master password", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) });
                var pb = new PasswordBox { Style = St("Pwd") }; sp.Children.Add(pb);
                TextBox skTb = null;
                if (LockNeedsSecretKey())   // v2.0: this PC doesn't have the vault's Secret Key (yet)
                {
                    sp.Children.Add(new TextBlock { Text = "Secret Key", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
                    var skb = SecretKeyBox(); skTb = skb.Item1; sp.Children.Add(skb.Item1); sp.Children.Add(skb.Item2);
                }
                var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
                var unlock = new Button { Content = "Unlock", Style = St("Accent"), IsDefault = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
                unlock.Click += (s, e) => UnlockWith(pb, skTb);
                btns.Children.Add(unlock);
                if (S.UseHello && Hello.Enabled) { var hb = new Button { Content = "Windows Hello", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0) }; hb.Click += (s, e) => UnlockHello(); btns.Children.Add(hb); }
                sp.Children.Add(btns);
                var rec = new TextBlock { Margin = new Thickness(0, 14, 0, 0), Foreground = Muted, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, Text = "Forgot it? Use your recovery code" };
                rec.MouseLeftButtonUp += (s, e) => UnlockRecovery();
                sp.Children.Add(rec);
                Loaded += (s, e) => pb.Focus(); Dispatcher.BeginInvoke(new Action(() => pb.Focus()), DispatcherPriority.Input);
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = "Create your vault", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 4) });
                sp.Children.Add(new TextBlock { Text = "Pick a master password you'll remember - a few random words work well. It's never stored, and nobody can reset it for you. You'll also get an Emergency Kit to print: your Secret Key (for adding another PC) and a recovery code.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
                sp.Children.Add(new TextBlock { Text = "Master password", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });
                var p1 = new PasswordBox { Style = St("Pwd") }; sp.Children.Add(p1);
                var meter = new TextBlock { Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), FontSize = 11.5 }; sp.Children.Add(meter);
                p1.PasswordChanged += (s, e) => { var bits = Health.Bits(p1.Password); meter.Text = p1.Password.Length == 0 ? "" : Health.Rating(bits) + " (about " + (int)bits + " bits)" + (bits < 60 ? " - longer is better" : ""); meter.Foreground = bits < 50 ? B("#F0BE5A") : B("#6ED28C"); };
                sp.Children.Add(new TextBlock { Text = "Type it again", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
                var p2 = new PasswordBox { Style = St("Pwd") }; sp.Children.Add(p2);
                var create = new Button { Content = "Create vault", Style = St("Accent"), IsDefault = true, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                create.Click += (s, e) => CreateVault(p1, p2);
                sp.Children.Add(create);
                var join = new TextBlock { Margin = new Thickness(0, 14, 0, 0), Foreground = Muted, Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, Text = "Already use Hallow Keys on another PC? Open a synced vault" };   // v2.0
                join.MouseLeftButtonUp += (s, e) => ShowAddDevice();
                sp.Children.Add(join);
            }
            sp.Children.Add(lockStatus);
            if (message != null) { lockStatus.Text = message; lockStatus.Foreground = Muted; }
            root.Child = new Grid { Children = { card } };
        }
        public StackPanel TitleRow(double size)
        {
            var t = new StackPanel { Orientation = Orientation.Horizontal };
            try { t.Children.Add(new Image { Source = AppIcon(), Width = size + 6, Height = size + 6, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            t.Children.Add(new TextBlock { Text = "HALLOW", FontSize = size * 0.85, FontWeight = FontWeights.Bold, Foreground = B(Program.AccentHex), VerticalAlignment = VerticalAlignment.Center });
            t.Children.Add(new TextBlock { Text = " KEYS", FontSize = size * 0.85, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            return t;
        }
        // the password's UTF-8 bytes from a PasswordBox (wiped by the caller)
        static byte[] Bytes(PasswordBox pb)
        {
            var ss = pb.SecurePassword; IntPtr p = IntPtr.Zero;
            try
            {
                p = Marshal.SecureStringToGlobalAllocUnicode(ss); var chars = new char[ss.Length]; Marshal.Copy(p, chars, 0, chars.Length);
                var b = System.Text.Encoding.UTF8.GetBytes(chars); Array.Clear(chars, 0, chars.Length); return b;
            }
            finally { if (p != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(p); ss.Dispose(); }
        }
        void Busy(string text) { lockStatus.Text = text; lockStatus.Foreground = Muted; }
        void UnlockWith(PasswordBox pb, TextBox skTb)
        {
            if (pb.SecurePassword.Length == 0) return;
            byte[] sk = null;
            if (skTb != null)
            {
                string err; sk = SecretKey.Parse(skTb.Text, out err);
                if (sk == null) { lockStatus.Text = err; lockStatus.Foreground = B("#F0BE5A"); return; }
            }
            var pw = Bytes(pb); pb.Clear(); Busy("Unlocking...");
            RunKdf(() => { try { var v = OpenForUnlock(pw, sk); if (sk != null) SecretKey.Store(v.VaultId, sk); return v; } finally { Gcm.Wipe(sk); } }, pw);
        }
        void UnlockHello()
        {
            Busy("Waiting for Windows Hello...");
            new Thread(() =>
            {
                string err; var key = Hello.Unlock(out err); Vault v = null;
                if (key != null) try { v = Vault.OpenWithDataKey(KeysSettings.VaultPath, key); } catch (Exception e) { err = e.Message; } finally { Gcm.Wipe(key); }
                Dispatcher.BeginInvoke(new Action(() => { if (v != null) Unlocked(v); else { lockStatus.Text = err; lockStatus.Foreground = B("#F0BE5A"); } }));
            }) { IsBackground = true }.Start();
        }
        void UnlockRecovery()
        {
            var code = Ask.Text(this, "Use your recovery code", "Type the recovery code from your recovery sheet (dashes don't matter). You'll pick a new master password next.", "Recovery code");
            if (string.IsNullOrWhiteSpace(code)) return;
            Busy("Checking the recovery code...");
            new Thread(() =>
            {
                Vault v = null; string err = null;
                try { v = Vault.OpenWithRecovery(KeysSettings.VaultPath, code); } catch (Exception e) { err = e.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (v == null) { lockStatus.Text = err; lockStatus.Foreground = B("#F0BE5A"); return; }
                    vault = v; Unlocked(v);
                    ChangeMasterPassword(true);
                }));
            }) { IsBackground = true }.Start();
        }
        void CreateVault(PasswordBox p1, PasswordBox p2)
        {
            if (p1.Password.Length < 8) { lockStatus.Text = "Use at least 8 characters (longer is much better)."; lockStatus.Foreground = B("#F0BE5A"); return; }
            if (p1.Password != p2.Password) { lockStatus.Text = "The two passwords aren't the same."; lockStatus.Foreground = B("#F0BE5A"); return; }
            var pw = Bytes(p1); p1.Clear(); p2.Clear(); Busy("Creating your vault...");
            string code = null, skText = null;
            RunKdf(() => { byte[] sk; var v = Vault.Create(KeysSettings.VaultPath, pw, out code, out sk); try { SecretKey.Store(v.VaultId, sk); skText = SecretKey.Format(sk); } finally { Gcm.Wipe(sk); } v.Save(); return v; }, pw, () => { ShowEmergencyKit(code, skText, "new"); code = null; skText = null; });
        }
        // the key derivation takes a moment (600,000 rounds): off the window's thread
        void RunKdf(Func<Vault> work, byte[] wipe, Action after = null)
        {
            IsEnabled = false;
            new Thread(() =>
            {
                Vault v = null; string err = null;
                try { v = work(); } catch (VaultException e) { err = e.Message; } catch (Exception e) { err = "Couldn't open the vault: " + e.Message; }
                finally { Gcm.Wipe(wipe); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled = true;
                    if (v == null) { lockStatus.Text = err; lockStatus.Foreground = B("#F0BE5A"); return; }
                    Unlocked(v); if (after != null) after();
                }));
            }) { IsBackground = true }.Start();
        }
        void Unlocked(Vault v)
        {
            vault = v; WriteStatus(); ShowMain(); OnUnlocked(); AfterUnlock13(); AfterUnlock16(); AfterUnlock17(); AfterUnlock20();
        }
        partial void OnUnlocked();
        partial void OnLocking();
        public void Lock(string why)
        {
            if (vault == null) return;
            OnLocking(); StopSync();
            SafeClip.ClearIfOurs();
            vault.Dispose(); vault = null; selected = null; editing = null; showHistory = false; lastSearch = "";
            ShowLock(why); WriteStatus();
            GC.Collect();
        }

        // ---- tray, the Hallow icon, the family pipe
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Keys", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Keys"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            var lk = new WF.ToolStripMenuItem("Lock now"); lk.Click += (s, e) => Lock("Locked."); menu.Items.Add(lk);
            var gen = new WF.ToolStripMenuItem("Generate password (copies it)"); gen.Click += (s, e) => MenuInvoke("generate"); menu.Items.Add(gen);
            menu.Opening += (s, e) => lk.Enabled = vault != null;
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
        void Quit() { exiting = true; Lock("Locked."); if (tray != null) { tray.Visible = false; tray.Dispose(); } StopHotkey(); StopDesktopFill(); StopSync(); OnQuit(); Application.Current.Shutdown(); }
        partial void OnQuit();

        // pipe commands: never any item data
        string Command(string line)
        {
            int sp = line.IndexOf(' '); var cmd = sp < 0 ? line : line.Substring(0, sp); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "ping": return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Keys" }, { "version", Program.Version } });
                case "status": return "ok " + OnUi(() => StatusJson());
                case "open": return OnUi(() => { ShowFromTray(); return "ok"; });
                case "menu": return OnUi(() => "ok " + Family.Json(new Dictionary<string, object> { { "app", "Keys" }, { "items", new List<object> {
                    new Dictionary<string, object> { { "id", "lock" }, { "label", "Lock now" }, { "enabled", vault != null } },
                    new Dictionary<string, object> { { "id", "generate" }, { "label", "Generate password" } } } } }));
                case "invoke": return OnUi(() => MenuInvoke(arg));
                case "quit": new Thread(() => { Thread.Sleep(300); Dispatcher.BeginInvoke(new Action(Quit)); }) { IsBackground = true }.Start(); return "ok";
            }
            return "error unknown command";
        }
        string OnUi(Func<string> f)
        {
            string r = "error no answer in time";
            var op = Dispatcher.BeginInvoke(new Action(() => { try { r = f(); } catch (Exception e) { r = "error " + e.GetType().Name; } }));
            return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed ? r : "error no answer in time";
        }
        string MenuInvoke(string id)
        {
            switch (id)
            {
                case "lock": Lock("Locked."); return "ok";
                case "open": ShowFromTray(); return "ok";
                case "generate":
                    var p = S.GenWords ? Generator.Passphrase(S.GenWordCount, "-", true, true) : Generator.Password(S.GenLength, S.GenLower, S.GenUpper, S.GenDigits, S.GenSymbols, S.GenAvoid);
                    SafeClip.Copy(p); Notify("Hallow Keys", "A new password is on the clipboard for " + SafeClip.ClearSeconds + " seconds."); return "ok";
            }
            return "error unknown item";
        }
        string StatusJson()
        {
            var t = Crash.LastTime();
            return Family.Json(new Dictionary<string, object> { { "app", "Keys" }, { "version", Program.Version }, { "updated", Family.Time(DateTime.Now) }, { "running", true },
                { "locked", vault == null }, { "items", vault == null ? (object)null : vault.Data.Items.Count }, { "hallowIcon", hubHosts }, { "lastCrash", t == null ? null : Family.Time(t.Value) },
                { "sync", SyncStatus() } });   // v2.0: sync state only - never a path, key or item
        }
        public void WriteStatus() { if (!Snap.On) Family.WriteStatus(StatusJson()); }

        void CheckHost()
        {
            if (tray == null) return;
            if (!S.UseHallowIcon) { SetHosted(false); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool on = false;
                try
                {
                    var r = Family.Send("Hub", "tray-host", 1500);
                    if (r != null && r.StartsWith("ok {")) { var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Substring(3)); object o; on = d.TryGetValue("on", out o) && o is bool && (bool)o; }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(() => SetHosted(on && S.UseHallowIcon)));
            });
        }
        void SetHosted(bool on) { var was = hubHosts; hubHosts = on; if (tray != null && !balloonOwn) tray.Visible = !on; if (was != on) WriteStatus(); }
        public void Notify(string title, string text)
        {
            if (tray == null) return;
            if (TestNoShow) { TestLog("notify: " + title); return; }   // developer tests: nothing on the screen
            if (!hubHosts) { tray.ShowBalloonTip(4000, title, text, WF.ToolTipIcon.None); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = Family.Send("Hub", "notify Keys\t" + title + "\t" + text, 2000);
                if (Family.Error(r, "no answer") == null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    balloonOwn = true; tray.Visible = true; tray.ShowBalloonTip(4000, title, text, WF.ToolTipIcon.None);
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) }; t.Tick += (s, e) => { t.Stop(); balloonOwn = false; SetHosted(hubHosts); }; t.Start();
                }));
            });
        }

        // ---- accent, crash notice, updates
        System.IO.FileSystemWatcher accentWatch;
        void ApplyAccent()
        {
            var a = Accent.Current(S.UseHallowAccent); if (a == Program.AccentHex) return;
            Program.LoadTheme(Application.Current);
            if (vault == null) ShowLock(); else ShowMain();
        }
        bool crashShown;
        void ShowCrashNotice()
        {
            if (crashShown) return; crashShown = true;
            var f = Crash.Last(); if (f == null) return;
            var name = System.IO.Path.GetFileName(f); if (name == S.LastCrashShown) return;
            S.LastCrashShown = name; S.Save();
            if (MessageBox.Show(this.IsVisible ? this : null, "Hallow Keys closed unexpectedly last time. A short report was saved on this PC (no passwords or items are in it) - nothing was sent anywhere.\n\nOpen the report?", "Hallow Keys", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + f + "\""); } catch { }
        }
        GhRelease pending;
        public void CheckForUpdate(bool manual)
        {
            new Thread(() =>
            {
                var r = Updater.Latest();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (r == null) { if (manual) Toast("Couldn't reach GitHub - try again later."); return; }
                    if (!Updater.IsNewer(r)) { if (manual) Toast("You have the newest version (v" + Program.Version + ")."); return; }
                    pending = r; if (vault != null) ShowMain();
                    if (!IsVisible) Notify("Hallow Keys", "Version " + r.Version.ToString(2) + " is out - open Hallow Keys to update.");
                }));
            }) { IsBackground = true }.Start();
        }
        void InstallUpdate()
        {
            if (pending == null) return;
            if (MessageBox.Show(this, "Install Hallow Keys v" + pending.Version.ToString(2) + " now?\n\nIt downloads from GitHub, checks the file against GitHub's checksum, and reopens by itself. Your vault isn't touched.", "Update Hallow Keys", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var r = pending;
            new Thread(() => { var err = Updater.Install(r); Dispatcher.BeginInvoke(new Action(() => { if (err != null) Toast(err); else Toast("Installing the update - Hallow Keys reopens in a few seconds."); })); }) { IsBackground = true }.Start();
        }

        // anything from the open vault that might appear in an error message is blanked out of crash reports
        string ScrubSecrets(string text)
        {
            var v = vault; if (v == null) return text;
            foreach (var it in v.Data.Items.Concat(v.Data.Trash).ToList())
                foreach (var s in new[] { it.Password, it.Username, it.Totp, it.CardNumber, it.Cvc, it.Pin, it.Notes, it.Title, it.Email, it.Phone, it.Address }.Concat(it.Urls).Concat(it.OldPasswords).Concat(it.Tags).Concat(ItemTypes.AllValues(it)))
                    if (!string.IsNullOrEmpty(s) && s.Length >= 3) text = text.Replace(s, "<hidden>");
            return text;
        }
    }
}
