// Hallow Keys 2.12 - "Passkeys on Windows", the page: where Keys answers passkey requests today (the browser extension, Microsoft's
// sign-in included through related origins), what Windows 11's own passkey provider list needs that Keys doesn't have yet (a package
// identity: a signed MSIX), how to hand a sign-in to your phone, and Hallow Browser. Read-only checks of Windows; nothing registered.
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
        static WindowsPasskeys.Info winPk;
        void ExtraNav212(Action<string, string, string, int> navBtn) { navBtn("passkeys-windows", "fingerprint", "Passkeys on Windows", -1); }

        UIElement PasskeysWindowsPage()
        {
            ScrollViewer sv; var sp = Page("Passkeys on Windows", "Where Hallow Keys can answer a website's passkey request today, and what isn't possible yet.", out sv);
            if (winPk == null) winPk = Snap.On ? new WindowsPasskeys.Info { PluginApi = true, ApiVersion = 9, Packaged = false } : WindowsPasskeys.Detect();
            int n = vault == null ? 0 : vault.Data.Items.Count(i => IsShown(i) && i.Type == "passkey" && i.Deleted == null);
            Func<string, string, StackPanel> section = (icon, title) =>
            {
                var s = new StackPanel(); var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                var ic = Icons.Make(icon, 15, B(Program.AccentHex)); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; h.Children.Add(ic);
                h.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center }); s.Children.Add(h);
                sp.Children.Add(Card(s)); return s;
            };
            Action<StackPanel, string> para = (s, t) => s.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            Action<StackPanel, string> muted = (s, t) => s.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 0, 0, 6) });
            Action<StackPanel, string[]> steps = (s, lines) => { for (int i = 0; i < lines.Length; i++) s.Children.Add(new TextBlock { Text = (i + 1) + ".  " + lines[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 4) }); };

            // 1. what works now
            var s1 = section("puzzle-piece", "In your browsers - works now");
            para(s1, "When a website asks to make or use a passkey in Chrome, Edge, Brave, Opera, Opera GX, Vivaldi, Arc, Chromium, Yandex or Firefox, the Hallow Keys extension shows Hallow Keys' own prompt: save the passkey here, pick one of yours, or hand the request to Windows (your phone, a security key or Windows Hello). The key stays in your vault and signs here, in the app.");
            var st = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
            st.Inlines.Add(new System.Windows.Documents.Run(NativeHost.Registered || Snap.On ? "Browsers connected." : "Browsers not connected yet.") { Foreground = NativeHost.Registered || Snap.On ? B("#6ED28C") : B("#F0BE5A"), FontWeight = FontWeights.SemiBold });
            st.Inlines.Add(new System.Windows.Documents.Run("  " + n + " passkey" + (n == 1 ? "" : "s") + " in the vaults this PC shows.") { Foreground = Muted });
            s1.Children.Add(st);
            var gob = new Button { Content = "Browser autofill", Style = St(NativeHost.Registered || Snap.On ? "Btn" : "Accent"), HorizontalAlignment = HorizontalAlignment.Left };
            gob.Click += (s, e) => { view = "browser"; ShowMain(); }; s1.Children.Add(gob);

            // 2. Microsoft
            var s2 = section("key", "Your Microsoft account");
            para(s2, "Microsoft's passkeys belong to login.microsoft.com, but you sign in on login.live.com (or login.microsoftonline.com for work and school). Since 2.12 Hallow Keys allows that only after checking Microsoft's own published list of its sign-in sites (login.microsoft.com/.well-known/webauthn) - the same check Chrome and Edge make - so no other site can borrow the passkey.");
            steps(s2, new[] {
                "Make sure the Hallow Keys extension is on in your browser and Hallow Keys is unlocked (after updating, reload the extension once on your browser's extensions page).",
                "Open your Microsoft account's security settings in that browser and add a new way to sign in: a passkey (Microsoft may call it \"face, fingerprint, PIN or security key\").",
                "Hallow Keys' prompt appears at the top right: click Save passkey in Hallow Keys.",
                "Next time, on Microsoft's sign-in page choose to sign in with a passkey (or click the email box and pick it in the suggestions), then pick it in Hallow Keys' prompt." });
            muted(s2, "Signing in to Windows itself (the lock screen) can't use a passkey from any password manager - that stays Windows Hello or your password.");

            // 3. Windows' own list
            var s3 = section("shield-halved", "Windows' own passkey list - not yet");
            para(s3, "Windows 11 can let a password manager answer passkey requests for every app and browser, listed under Settings > Accounts > Passkeys > Advanced options. " + WindowsPasskeys.Summary(winPk));
            para(s3, "Hallow Keys can't join that list yet: Windows only accepts a passkey manager that is installed as a packaged app with its own identity (an MSIX package signed with a code-signing certificate Windows trusts, or from the Microsoft Store). Hallow Keys is a plain program you unzip, without a certificate, so Windows refuses it - Hallow Keys won't appear in that list, and there's nothing to switch on there for now.");
            muted(s3, "This copy of Hallow Keys " + (winPk.Packaged ? "runs with a package identity." : "runs without a package identity (as expected).") + " Until it can be packaged and signed, the extension above is how Hallow Keys answers passkey requests.");

            // 4. phone
            var s4 = section("mobile-screen", "Hand a sign-in to your phone");
            para(s4, "For a passkey that lives on your phone (iPhone, iPad or Android) and not in Hallow Keys:");
            steps(s4, new[] {
                "In Hallow Keys' prompt click Use another device or the browser's own.",
                "In Windows' window choose iPhone, iPad or Android device. A QR code appears.",
                "Scan it with your phone's camera and confirm on the phone. Bluetooth must be on, on both the PC and the phone - it only checks they're close; the sign-in itself goes over the internet, encrypted." });
            muted(s4, "Passkeys saved in Hallow Keys stay on your PCs (they sync between them); they aren't in the phone copy that Hallow Remote shows.");

            // 5. Hallow Browser
            var s5 = section("globe", "Hallow Browser");
            para(s5, "Hallow Browser fills logins from Hallow Keys over the same private link the extension uses, but it doesn't pass passkey requests to Hallow Keys yet - there, websites get Windows' own passkey window (your phone, a security key or Windows Hello). Hallow Keys 2.12 is ready for it; Hallow Browser needs an update to use it.");
            return sv;
        }

        void SnapExtra212(MainWindow w)
        {
            w.view = "passkeys-windows"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v212-passkeys-windows.png"), 1280, 1700);
            w.view = "all";
        }
    }
}
