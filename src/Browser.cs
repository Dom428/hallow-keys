// Hallow Keys v1.1 - the Browser page and the app's side of the extension link.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HallowKeys
{
    public partial class MainWindow
    {
        NativeServer native;
        void StartNative()
        {
            native = new NativeServer(f => OnUi(f), () => vault, () => ShowFromTray(), msg => { SaveVault(msg); if (selected == null) ShowMain(); else FillList(); Notify("Hallow Keys", msg); });
            native.Start();
        }
        partial void OnQuit() { if (native != null) native.Stop(); }
        partial void SnapExtraImpl(Action<MainWindow, string> save) { view = "browser"; ShowMain(); save(this, "browser.png"); SnapExtra2(save); }
        partial void SnapExtra2(Action<MainWindow, string> save);
        static string ExtensionFolder { get { return Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "extension"); } }

        partial void ExtraNavImpl(StackPanel nav, Action<string, string, string, int> navBtn) { navBtn("browser", "puzzle-piece", "Browser", -1); ExtraNav2(nav, navBtn); }
        partial void ExtraNav2(StackPanel nav, Action<string, string, string, int> navBtn);
        partial void ExtraPageImpl(string v, DockPanel dock, ref bool handled)
        {
            if (v == "browser") { dock.Children.Add(BrowserPage()); handled = true; return; }
            ExtraPage2(v, dock, ref handled);
        }
        partial void ExtraPage2(string v, DockPanel dock, ref bool handled);

        UIElement BrowserPage()
        {
            ScrollViewer sv; var sp = Page("Browser autofill", "Fill logins in Chrome, Edge and Brave with one click. Hallow Keys fills only when you click the extension or the key icon in a password box - never by itself, never into hidden boxes, and only on the site a login was saved for.", out sv);
            var s1 = new StackPanel();
            s1.Children.Add(new TextBlock { Text = "1. Connect your browsers", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            s1.Children.Add(new TextBlock { Text = "Lets the Hallow Keys extension talk to this app (a small setting for your Windows account; nothing in the browsers is changed).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var con = new Button { Content = "Connect browsers", Style = St(NativeHost.Registered ? "Btn" : "Accent"), Margin = new Thickness(0, 0, 8, 0) };
            con.Click += (s, e) => { try { NativeHost.Register(System.Reflection.Assembly.GetExecutingAssembly().Location); Toast("Connected - Chrome, Edge and Brave can now reach Hallow Keys."); ShowMain(); } catch (Exception ex) { Toast("Couldn't connect: " + ex.Message, true); } };
            row.Children.Add(con);
            row.Children.Add(new TextBlock { Text = NativeHost.Registered ? "Connected" : "Not connected yet", Foreground = NativeHost.Registered ? B("#6ED28C") : B("#F0BE5A"), VerticalAlignment = VerticalAlignment.Center });
            s1.Children.Add(row); sp.Children.Add(Card(s1));
            var s2 = new StackPanel();
            s2.Children.Add(new TextBlock { Text = "2. Add the extension (you do this in the browser)", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            s2.Children.Add(new TextBlock { Text = "In Chrome, Edge or Brave: open the extensions page (chrome://extensions, edge://extensions or brave://extensions), turn on Developer mode, click Load unpacked and pick this folder. Then pin Hallow Keys next to the address bar.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var fr = new DockPanel();
            var cp = new Button { Content = "Copy folder path", Style = St("Btn"), Margin = new Thickness(8, 0, 0, 0) }; cp.Click += (s, e) => { Clipboard.SetText(ExtensionFolder); Toast("Copied: " + ExtensionFolder); };
            DockPanel.SetDock(cp, Dock.Right); fr.Children.Add(cp);
            fr.Children.Add(new TextBox { Style = St("Field"), Text = ExtensionFolder, IsReadOnly = true });
            s2.Children.Add(fr);
            if (!Directory.Exists(ExtensionFolder)) s2.Children.Add(new TextBlock { Text = "The extension folder is missing - reinstall Hallow Keys.", Foreground = B("#F0BE5A"), Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(Card(s2));
            var s3 = new StackPanel();
            s3.Children.Add(new TextBlock { Text = "3. Use it", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            s3.Children.Add(new TextBlock { Text = "On a login page, click the Hallow Keys button (or the key in the password box) and pick a login. On a page that asks for a 2FA code, click the key in the code box or the 2FA button next to the login. After you sign in somewhere new, the button shows a 1 - click it to save the login. The extension only works while Hallow Keys is running and unlocked. After updating Hallow Keys, click the reload button on the extension in the browser's extensions page.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            sp.Children.Add(Card(s3));
            var dis = new Button { Content = "Disconnect browsers", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Visibility = NativeHost.Registered ? Visibility.Visible : Visibility.Collapsed };
            dis.Click += (s, e) => { NativeHost.Unregister(); Toast("Disconnected."); ShowMain(); };
            sp.Children.Add(dis);
            return sv;
        }
    }
}
