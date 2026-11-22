// Hallow Keys 2.11 - Hallow Authenticator, the family's 2FA app on your phone (served by Hallow Remote 1.19+).
//   - "Send my 2FA codes to my phone": copy or MOVE the 2FA keys of the logins you pick. Keys makes a one-time 32-letter code (160 bits),
//     shows it and its QR code on this screen, and hands Hallow Remote only a lookup id and the keys sealed with AES-256-GCM under a key
//     made from that code. Remote keeps the sealed blob in memory, gives it to the phone once (10 minutes), and passes back the phone's
//     "done" proof (an HMAC only the code's holder can make). Only then does a MOVE take the keys out of this vault: Keys stops filling
//     those codes and shows "on your phone" instead.
//   - PC mirror (off unless turned on): the phone can send a sealed, read-only copy of the accounts you pick; Keys opens it with a key kept
//     inside this vault, so the codes show here only while Keys is unlocked (master password or Windows Hello). Convenience over strength.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HallowKeys
{
    public static class AuthLink
    {
        const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        public static string Base32(byte[] b)
        {
            var sb = new StringBuilder(); int buf = 0, bits = 0;
            foreach (var x in b) { buf = (buf << 8) | x; bits += 8; while (bits >= 5) { sb.Append(B32[(buf >> (bits - 5)) & 31]); bits -= 5; } buf &= 0xffff; }
            if (bits > 0) sb.Append(B32[(buf << (5 - bits)) & 31]);
            return sb.ToString();
        }
        public static string NewCode() { return Base32(Rng.Bytes(20)); }   // 32 letters, 160 bits
        public static string Grouped(string code) { return string.Join("-", Enumerable.Range(0, code.Length / 4).Select(i => code.Substring(i * 4, 4))); }
        static byte[] Sha(string s) { using (var h = SHA256.Create()) return h.ComputeHash(Encoding.ASCII.GetBytes(s)); }
        static string Hex(byte[] b) { return string.Concat(b.Select(x => x.ToString("x2"))); }
        public static string Id(string code) { return Hex(Sha("HallowAuthXferId" + code)).Substring(0, 32); }
        public static byte[] Key(string code) { return Sha("HallowAuthXferKey" + code); }
        public static string Proof(string code) { using (var h = new HMACSHA256(Sha("HallowAuthXferAck" + code))) return Hex(h.ComputeHash(Encoding.ASCII.GetBytes(Id(code)))); }
        public static bool SameProof(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }

        // a 2FA key on an item: the main one ("main") or a 2FA field ("f<index>")
        public class Code { public Item Item; public string Ref, Uri, Label; }
        public static List<Code> Codes(IEnumerable<Item> items)
        {
            var list = new List<Code>();
            foreach (var it in items.Where(i => i.Deleted == null && i.Type != "passkey"))
            {
                if (Totp.Parse(it.Totp) != null) list.Add(new Code { Item = it, Ref = it.Id + "|main", Uri = it.Totp.Trim(), Label = "2FA" });
                for (int i = 0; i < it.Fields.Count; i++) { var f = it.Fields[i]; if (f.Kind == "totp" && Totp.Parse(f.Value) != null) list.Add(new Code { Item = it, Ref = it.Id + "|f" + i, Uri = f.Value.Trim(), Label = f.Label.Length > 0 ? f.Label : "2FA" }); }
            }
            return list;
        }
        public static string Payload(IEnumerable<Code> codes, bool move)
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "v", 1 }, { "mode", move ? "move" : "copy" }, { "from", "Hallow Keys" }, { "made", DateTime.Now.ToString("s") },
                { "entries", codes.Select(c => (object)new Dictionary<string, object> { { "ref", c.Ref }, { "title", c.Item.Title }, { "username", c.Item.Username }, { "uri", c.Uri } }).ToList() } });
        }
        public static string Seal(string json, string code)
        {
            var key = Key(code); var nonce = Rng.Bytes(12); byte[] c, t;
            try { Gcm.EncryptRaw(key, nonce, Encoding.UTF8.GetBytes(json), Encoding.ASCII.GetBytes("HallowAuthXfer1:" + Id(code)), out c, out t); }
            finally { Gcm.Wipe(key); }
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "v", 1 }, { "iv", Convert.ToBase64String(nonce) }, { "ct", Convert.ToBase64String(c.Concat(t).ToArray()) } });
        }
        // a MOVE: the keys leave this vault (only after the phone proved it has them)
        public static int ApplyMove(VaultData d, IEnumerable<string> refs)
        {
            int n = 0;
            foreach (var r in refs)
            {
                var p = r.Split('|'); if (p.Length != 2) continue;
                var it = d.Items.FirstOrDefault(i => i.Id == p[0]); if (it == null) continue;
                if (p[1] == "main") { if (it.Totp.Length == 0) continue; it.Totp = ""; }
                else { int i; if (!p[1].StartsWith("f") || !int.TryParse(p[1].Substring(1), out i) || i >= it.Fields.Count || it.Fields[i].Kind != "totp") continue; it.Fields[i].Value = ""; }
                it.TotpOnPhone = DateTime.Now.ToString("s"); it.Updated = DateTime.Now; n++;
            }
            return n;
        }

        // ---- PC mirror
        public static string MirrorPath { get { return Path.Combine(Family.DataDir("Authenticator"), "mirror.bin"); } }
        public static string MirrorQr(byte[] key) { return "HALLOWMIRROR1:" + Base32(key); }
        public class Mirrored { public string Type, Issuer, Label, Secret, Algo; public int Digits, Period; public string Name { get { return Issuer.Length > 0 ? Issuer : Label; } } }
        public static List<Mirrored> ReadMirror(byte[] key, out DateTime made)
        {
            made = DateTime.MinValue;
            if (key == null || !File.Exists(MirrorPath)) return null;
            var js = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var box = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(MirrorPath));
            var all = Convert.FromBase64String((string)box["ct"]); if (all.Length < 16) return null;
            var plain = Gcm.DecryptRaw(key, Convert.FromBase64String((string)box["iv"]), all.Take(all.Length - 16).ToArray(), all.Skip(all.Length - 16).ToArray(), Encoding.ASCII.GetBytes("HallowAuthMirror1"));
            var d = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(plain)); Gcm.Wipe(plain);
            object m; if (d.TryGetValue("made", out m)) DateTime.TryParse(m as string, null, System.Globalization.DateTimeStyles.RoundtripKind, out made);
            var list = new List<Mirrored>();
            foreach (var o in (System.Collections.ArrayList)d["entries"])
            {
                var e = o as Dictionary<string, object>; if (e == null) continue;
                Func<string, string> s = k => { object v; return e.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; };
                Func<string, int, int> n = (k, def) => { int v; return int.TryParse(s(k), out v) ? v : def; };
                list.Add(new Mirrored { Type = s("type"), Issuer = s("issuer"), Label = s("label"), Secret = s("secret"), Algo = s("algo").Length > 0 ? s("algo") : "SHA1", Digits = n("digits", 6), Period = n("period", 30) });
            }
            return list;
        }
        const string SteamChars = "23456789BCDFGHJKMNPQRTVWXY";
        public static string CodeOf(Mirrored m, long now)
        {
            var secret = Totp.Base32(m.Secret); if (secret == null || secret.Length == 0) return null;
            if (m.Type == "steam")
            {
                var msg = BitConverter.GetBytes(now / 30); if (BitConverter.IsLittleEndian) Array.Reverse(msg);
                byte[] h; using (var mac = new HMACSHA1(secret)) h = mac.ComputeHash(msg);
                int off = h[h.Length - 1] & 15, v = ((h[off] & 0x7f) << 24) | (h[off + 1] << 16) | (h[off + 2] << 8) | h[off + 3];
                var sb = new StringBuilder(); for (int i = 0; i < 5; i++) { sb.Append(SteamChars[v % SteamChars.Length]); v /= SteamChars.Length; }
                return sb.ToString();
            }
            var spec = new Totp.Spec { Secret = secret, Algorithm = m.Algo.ToUpperInvariant(), Digits = m.Digits == 8 ? 8 : 6, Period = m.Period >= 10 && m.Period <= 300 ? m.Period : 30 };
            if (spec.Algorithm != "SHA1" && spec.Algorithm != "SHA256" && spec.Algorithm != "SHA512") return null;
            return Totp.Code(spec, now);
        }
    }

    public partial class MainWindow
    {
        // ---- one transfer at a time: the code on screen, Remote holding the sealed blob, polling for the phone's "done"
        class Transfer { public string Code, Id; public bool Move; public List<string> Refs; public DateTime Until; public string State = "waiting"; public DispatcherTimer Timer; public Action<Transfer> Changed; public int Count; }
        Transfer xfer;
        // returns null and says why, or the running transfer
        Transfer StartTransfer(List<AuthLink.Code> codes, bool move, out string err)
        {
            err = null; StopTransfer(true);
            if (codes.Count == 0) { err = "Pick at least one login with a 2FA key."; return null; }
            var code = AuthLink.NewCode(); var id = AuthLink.Id(code);
            var blob = AuthLink.Seal(AuthLink.Payload(codes, move), code);
            var until = DateTime.UtcNow.AddMinutes(10);
            var reply = Family.Send("Remote", "auth-offer " + id + " " + (long)(until - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(blob)), 5000);
            var e = Family.Error(reply, "Hallow Remote isn't running on this PC - it carries the codes to your phone (sealed).");
            if (e != null) { err = e.Contains("unknown command") ? "This needs Hallow Remote 1.19 or newer - update it first." : "Hallow Remote: " + e; return null; }
            var t = new Transfer { Code = code, Id = id, Move = move, Refs = codes.Select(c => c.Ref).ToList(), Until = until, Count = codes.Count };
            t.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Timer.Tick += (s, a) => PollTransfer(t);
            t.Timer.Start(); xfer = t;
            AddActivity("phone", (move ? "moving " : "copying ") + codes.Count + " 2FA code" + (codes.Count == 1 ? "" : "s") + " to Hallow Authenticator");
            return t;
        }
        void PollTransfer(Transfer t)
        {
            if (t != xfer || t.State != "waiting") return;
            if (DateTime.UtcNow > t.Until) { t.State = "expired"; StopTransfer(true); if (t.Changed != null) t.Changed(t); return; }
            var r = Family.Send("Remote", "auth-offer-state " + t.Id, 1500);
            if (r == null || !r.StartsWith("ok {")) return;
            Dictionary<string, object> d; try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Substring(3)); } catch { return; }
            var state = d["state"] as string; var proof = d["proof"] as string;
            if (state == "gone") { t.State = "expired"; StopTransfer(false); if (t.Changed != null) t.Changed(t); return; }
            if (state != "done") return;
            if (!AuthLink.SameProof(proof, AuthLink.Proof(t.Code))) { t.State = "bad"; StopTransfer(true); if (t.Changed != null) t.Changed(t); return; }
            StopTransfer(true);
            if (t.Move && vault != null)
            {
                var n = AuthLink.ApplyMove(vault.Data, t.Refs);
                SaveVault(n + " 2FA code" + (n == 1 ? " is" : "s are") + " now on your phone only - Hallow Keys no longer fills " + (n == 1 ? "it" : "them") + ".");
                AddActivity("phone", "moved " + n + " 2FA code" + (n == 1 ? "" : "s") + " to Hallow Authenticator");
                TestLog("auth: moved " + n);
            }
            else { Toast(t.Count + " 2FA code" + (t.Count == 1 ? "" : "s") + " copied to your phone."); TestLog("auth: copied " + t.Count); }
            t.State = "done"; if (t.Changed != null) t.Changed(t);
            if (selected == null && editing == null) ShowMain();
        }
        void StopTransfer(bool cancelAtRemote)
        {
            var t = xfer; if (t == null) return; xfer = null;
            if (t.Timer != null) t.Timer.Stop();
            if (cancelAtRemote) try { Family.Send("Remote", "auth-offer-cancel " + t.Id, 1500); } catch { }
        }

        static ImageSource QrImage(string text, int px)
        {
            var m = Qr.Encode(text); int n = m.GetLength(0), q = 4, size = (n + 2 * q) * px;
            var pixels = new byte[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { int mx = x / px - q, my = y / px - q; pixels[y * size + x] = (byte)(mx >= 0 && my >= 0 && mx < n && my < n && m[my, mx] ? 0 : 255); }
            var b = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size); b.Freeze(); return b;
        }

        // Settings: the Hallow Authenticator card
        void AuthCard(StackPanel sp)
        {
            var c = new StackPanel();
            c.Children.Add(new TextBlock { Text = "Hallow Authenticator (2FA codes on your phone)", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            c.Children.Add(new TextBlock { Text = "Two-factor works best when the code lives somewhere else than the password. If your passwords and their 2FA codes are in one vault on one PC, anyone who gets that vault (or that PC) gets both. Hallow Authenticator keeps the codes on your phone instead - encrypted, and working without this PC. It opens from Hallow Remote (1.19+, with HTTPS).", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var all = vault == null ? new List<AuthLink.Code>() : AuthLink.Codes(Shown().Items);
            int onPhone = vault == null ? 0 : vault.Data.Items.Count(i => i.Deleted == null && !string.IsNullOrEmpty(i.TotpOnPhone) && Totp.Parse(i.Totp) == null);
            c.Children.Add(new TextBlock { Text = all.Count + " 2FA key" + (all.Count == 1 ? "" : "s") + " in Hallow Keys" + (onPhone > 0 ? ", " + onPhone + " login" + (onPhone == 1 ? "" : "s") + " moved to your phone" : "") + ".", Margin = new Thickness(0, 0, 0, 6) });
            var send = new Button { Content = "Send my 2FA codes to my phone...", Style = St("Accent"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = all.Count > 0 };
            send.Click += (s, e) => SendCodesDialog();
            c.Children.Add(send);
            // mirror
            c.Children.Add(new TextBlock { Text = "PC mirror (convenience over strength)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            c.Children.Add(new TextBlock { Text = "Off by default. When on, your phone can send a read-only copy of the accounts you pick to this PC, and Hallow Keys shows their codes here while it's unlocked. Handy at the desk - but it puts codes back on the same PC as your passwords, so it's weaker 2FA. Leave important accounts out.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            bool on = vault != null && vault.Data.AuthMirrorKey != null;
            c.Children.Add(Switch("Show codes my phone mirrors to this PC", on, v =>
            {
                if (vault == null) return;
                if (v) { vault.Data.AuthMirrorKey = Rng.Bytes(32); SaveVault("PC mirror is on - scan the code with Hallow Authenticator (Settings > PC mirror)."); ShowMirrorCode(); }
                else { vault.Data.AuthMirrorKey = null; try { if (File.Exists(AuthLink.MirrorPath)) File.Delete(AuthLink.MirrorPath); } catch { } SaveVault("PC mirror is off and the copy on this PC is deleted. Turn it off on the phone too."); }
                ShowMain();
            }));
            if (on)
            {
                var again = new Button { Content = "Show the mirror code", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6) }; again.Click += (s, e) => ShowMirrorCode(); c.Children.Add(again);
                MirrorList(c);
            }
            sp.Children.Add(Card(c));
        }
        // the mirrored codes, live while this page is open
        void MirrorList(StackPanel c)
        {
            DateTime made; List<AuthLink.Mirrored> list = null; string err = null;
            try { list = AuthLink.ReadMirror(vault.Data.AuthMirrorKey, out made); } catch (Exception e) { made = DateTime.MinValue; err = e is CryptographicException ? "The copy on this PC doesn't open with this vault's mirror key - scan the mirror code again on the phone." : "Couldn't read the mirror: " + e.Message; }
            if (err != null || list == null) { c.Children.Add(new TextBlock { Text = err ?? "Nothing mirrored yet - pick accounts in Hallow Authenticator on your phone (Settings > PC mirror).", Foreground = Muted, TextWrapping = TextWrapping.Wrap }); return; }
            c.Children.Add(new TextBlock { Text = list.Count + " mirrored account" + (list.Count == 1 ? "" : "s") + (made > DateTime.MinValue ? ", sent " + made.ToLocalTime().ToString("d MMM HH:mm") : "") + " (read-only):", Foreground = Muted, Margin = new Thickness(0, 0, 0, 4) });
            var rows = new List<Tuple<TextBlock, AuthLink.Mirrored>>();
            foreach (var m in list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var copy = new Button { Content = "Copy", Style = St("Btn"), Padding = new Thickness(10, 2, 10, 2) }; DockPanel.SetDock(copy, Dock.Right);
                var code = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 18, FontWeight = FontWeights.Bold, Width = 120, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(code, Dock.Right);
                var mm = m; copy.Click += (s, e) => { var v = AuthLink.CodeOf(mm, Totp.Now()); if (v != null) { SafeClip.Copy(v); Toast("Code copied (clears in " + SafeClip.ClearSeconds + " s)."); } };
                d.Children.Add(copy); d.Children.Add(code);
                d.Children.Add(new TextBlock { Text = m.Name + (m.Issuer.Length > 0 && m.Label.Length > 0 ? "  " + m.Label : ""), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                c.Children.Add(d); rows.Add(Tuple.Create(code, m));
            }
            Action fill = () => { var now = Totp.Now(); foreach (var r in rows) { var v = AuthLink.CodeOf(r.Item2, now); r.Item1.Text = v == null ? "-" : v.Length == 6 ? v.Substring(0, 3) + " " + v.Substring(3) : v; } };
            fill();
            var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tm.Tick += (s, e) => { if (vault == null || !c.IsVisible) { tm.Stop(); return; } fill(); }; tm.Start();
        }
        void ShowMirrorCode()
        {
            if (vault == null || vault.Data.AuthMirrorKey == null) return;
            var w = new Window { Title = "Hallow Keys - PC mirror", Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = new FontFamily("Segoe UI") };
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp;
            sp.Children.Add(new TextBlock { Text = "In Hallow Authenticator on your phone: Settings > PC mirror > Turn on, and scan this. It's the key the phone seals the mirror with - don't photograph or share it.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new Image { Source = QrImage(AuthLink.MirrorQr(vault.Data.AuthMirrorKey), 5), Width = 300, Height = 300, Stretch = Stretch.Uniform });
            var ok = new Button { Content = "Done", Style = St("Accent"), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), MinWidth = 80 }; ok.Click += (s, e) => w.Close(); sp.Children.Add(ok);
            w.ShowDialog();
        }
        // pick logins, copy or move, then the code + QR while waiting for the phone
        void SendCodesDialog()
        {
            if (vault == null) return;
            var codes = AuthLink.Codes(Shown().Items);
            var w = new Window { Title = "Hallow Keys - send 2FA codes to your phone", Width = 520, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null, Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = new FontFamily("Segoe UI") };
            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) }; w.Content = root;
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            head.Children.Add(new TextBlock { Text = "Pick the logins whose 2FA codes go to Hallow Authenticator on your phone:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var mv = new RadioButton { Content = new TextBlock { Text = "Move (recommended): after your phone confirms it has them, the 2FA keys are taken out of Hallow Keys. Keys stops filling those codes and shows \"on your phone\". Your password and code are then on different devices.", TextWrapping = TextWrapping.Wrap }, IsChecked = true, Margin = new Thickness(0, 2, 0, 4), Foreground = (Brush)FindResource("Text") };
            var cp = new RadioButton { Content = new TextBlock { Text = "Copy: the phone gets them and Hallow Keys keeps filling them too (both places hold the key).", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 2, 0, 8), Foreground = (Brush)FindResource("Text") };
            head.Children.Add(mv); head.Children.Add(cp);
            var foot = new StackPanel(); DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
            var list = new StackPanel(); var boxes = new List<Tuple<CheckBox, AuthLink.Code>>();
            foreach (var c in codes.OrderBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                var cb = new CheckBox { Content = new TextBlock { Text = c.Item.Title + (c.Item.Username.Length > 0 ? "  -  " + c.Item.Username : "") + (c.Ref.EndsWith("|main") ? "" : "  (" + c.Label + ")"), TextTrimming = TextTrimming.CharacterEllipsis }, IsChecked = true, Margin = new Thickness(0, 3, 0, 3), Foreground = (Brush)FindResource("Text") };
                list.Children.Add(cb); boxes.Add(Tuple.Create(cb, c));
            }
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            var go = new Button { Content = "Show the code for my phone", Style = St("Accent"), MinWidth = 120, Margin = new Thickness(0, 10, 8, 0) };
            var close = new Button { Content = "Close", Style = St("Btn"), IsCancel = true, Margin = new Thickness(0, 10, 0, 0) }; close.Click += (s, e) => w.Close();
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(go); row.Children.Add(close);
            foot.Children.Add(status); foot.Children.Add(row);
            go.Click += (s, e) =>
            {
                var pick = boxes.Where(b => b.Item1.IsChecked == true).Select(b => b.Item2).ToList(); string err;
                var t = StartTransfer(pick, mv.IsChecked == true, out err);
                if (t == null) { status.Text = err; status.Foreground = B("#F0BE5A"); return; }
                // swap the list for the code
                root.Children.Clear(); var sp = new StackPanel(); root.Children.Add(sp);
                sp.Children.Add(new TextBlock { Text = "On your phone, open Hallow Authenticator (Hallow Remote's Home > Authenticator): + > From Hallow Keys, then scan this or type the code.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                sp.Children.Add(new Image { Source = QrImage("HALLOWAUTH1:" + t.Code, 5), Width = 260, Height = 260, Stretch = Stretch.Uniform });
                sp.Children.Add(new TextBox { Text = AuthLink.Grouped(t.Code), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 17, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6), Style = St("Field") });
                var st = new TextBlock { Text = "Waiting for your phone... (" + pick.Count + " code" + (pick.Count == 1 ? "" : "s") + ", " + (t.Move ? "move" : "copy") + "; the code works once, for 10 minutes)", Foreground = Muted, TextWrapping = TextWrapping.Wrap };
                sp.Children.Add(st);
                sp.Children.Add(new TextBlock { Text = "End-to-end encrypted: Hallow Remote only passes on the sealed codes and never sees this code.", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                var done = new Button { Content = "Cancel", Style = St("Btn"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), MinWidth = 90 }; done.Click += (s2, e2) => w.Close(); sp.Children.Add(done);
                t.Changed = tt =>
                {
                    done.Content = "Close";
                    if (tt.State == "done") { st.Text = tt.Move ? "Done - the codes are on your phone and out of Hallow Keys." : "Done - copied to your phone."; st.Foreground = B("#6ED28C"); }
                    else if (tt.State == "bad") { st.Text = "The phone's answer didn't match this code - nothing was changed here."; st.Foreground = B("#F0BE5A"); }
                    else { st.Text = "The code ran out (or Hallow Remote restarted) - nothing was changed. Close and try again."; st.Foreground = B("#F0BE5A"); }
                };
            };
            w.Closed += (s, e) => { if (xfer != null && xfer.State == "waiting") StopTransfer(true); };
            w.ShowDialog();
        }
        // the item page: a 2FA key that went to the phone
        void OnPhoneRow(StackPanel sp, Item it)
        {
            var d = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            d.Children.Add(new TextBlock { Text = "2FA code", Foreground = Muted, FontSize = 11.5 });
            string mirrored = null;
            if (vault != null && vault.Data.AuthMirrorKey != null)
                try { DateTime made; var list = AuthLink.ReadMirror(vault.Data.AuthMirrorKey, out made); var m = list == null ? null : list.FirstOrDefault(x => string.Equals(x.Issuer, it.Title, StringComparison.CurrentCultureIgnoreCase) || string.Equals(x.Label, it.Username, StringComparison.CurrentCultureIgnoreCase) && it.Username.Length > 0); if (m != null) mirrored = AuthLink.CodeOf(m, Totp.Now()); } catch { }
            d.Children.Add(new TextBlock { Text = "On your phone (Hallow Authenticator)" + (mirrored != null ? " - PC mirror: " + mirrored : ""), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(d);
        }

        // ---- developer tests only (inside a test folder): drive a transfer / the mirror without clicking
        string AuthTestCmd(string cmd, string arg)
        {
            if (KeysSettings.TestDir == null) return "error unknown command";
            if (vault == null) return "error locked";
            if (cmd == "authtest-send")
            {
                string err; var t = StartTransfer(AuthLink.Codes(Shown().Items), arg.StartsWith("move"), out err);
                return t == null ? "error " + err : "ok " + Family.Json(new Dictionary<string, object> { { "code", t.Code }, { "count", t.Count } });
            }
            if (cmd == "authtest-state") return "ok " + Family.Json(new Dictionary<string, object> { { "state", xfer == null ? "none" : xfer.State }, { "codes", AuthLink.Codes(Shown().Items).Count },
                { "onPhone", vault.Data.Items.Count(i => !string.IsNullOrEmpty(i.TotpOnPhone)) } });
            if (cmd == "authtest-mirror-on") { if (vault.Data.AuthMirrorKey == null) { vault.Data.AuthMirrorKey = Rng.Bytes(32); SaveVault(null); } return "ok " + Family.Json(new Dictionary<string, object> { { "qr", AuthLink.MirrorQr(vault.Data.AuthMirrorKey) } }); }
            if (cmd == "authtest-mirror")
            {
                DateTime made; var list = AuthLink.ReadMirror(vault.Data.AuthMirrorKey, out made);
                return "ok " + Family.Json(new Dictionary<string, object> { { "items", list == null ? null : list.Select(m => (object)new Dictionary<string, object> { { "name", m.Name }, { "code", AuthLink.CodeOf(m, Totp.Now()) } }).ToList() } });
            }
            return "error unknown command";
        }
    }
}
