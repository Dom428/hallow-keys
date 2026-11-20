// Hallow Keys 2.6 - "Developer tools", the window's side: the SSH agent (SshAgent26.cs) and the command line (CliClient26.cs) both ask
// here before anything leaves the vault - a small prompt naming the program and the item (or Windows Hello, if you pick it), only while
// the vault is unlocked. The Activity list shows what was asked (titles, programs, allowed or not - never a secret).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowKeys
{
    public class ActivityEntry { public DateTime When; public string Kind = "", Text = ""; }

    // what the command line may read: an item by its title (or id) and one field
    public static class CliLookup
    {
        public class Found { public Item Item; public string Field = "", Value; public string Error; public int Code; }
        static readonly string[] Builtin = { "password", "username", "user", "totp", "otp", "code", "notes", "note", "url", "website", "title", "email", "phone", "cardnumber", "number", "cvc", "expiry", "pin", "cardholder", "name", "fullname", "address" };
        public static Found Resolve(IEnumerable<Item> shown, string reference, string field)
        {
            var items = shown.Where(i => i.Type != "passkey").ToList();
            reference = (reference ?? "").Trim(); if (reference.Length == 0) return new Found { Error = "no item named", Code = CliClient.Usage };
            if (field == null)
            {
                int s = reference.LastIndexOf('/');
                if (s > 0 && s < reference.Length - 1)
                {
                    var head = Match(items, reference.Substring(0, s));
                    if (head.Count == 1) return Read(head[0], reference.Substring(s + 1).Trim());
                    if (Match(items, reference).Count == 0) return head.Count > 1 ? Several(reference.Substring(0, s)) : new Found { Error = "no item called \"" + reference.Substring(0, s) + "\"", Code = CliClient.NotFound };
                }
            }
            var m = Match(items, reference);
            if (m.Count == 0) return new Found { Error = "no item called \"" + reference + "\"", Code = CliClient.NotFound };
            if (m.Count > 1) return Several(reference);
            return Read(m[0], field);
        }
        static Found Several(string r) { return new Found { Error = "several items are called \"" + r + "\" - give one of them another title", Code = CliClient.NotFound }; }
        static List<Item> Match(List<Item> items, string r)
        {
            var byId = items.Where(i => i.Id == r).ToList(); if (byId.Count > 0) return byId;
            return items.Where(i => string.Equals(i.Title.Trim(), r, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
        public static Found Read(Item it, string field)
        {
            var f = (field ?? "").Trim(); var lf = f.ToLowerInvariant().Replace(" ", "");
            Func<string, string, Found> ok = (name, v) => v == null ? null : new Found { Item = it, Field = name, Value = v };
            Found r = null;
            if (f.Length == 0)
            {
                if (it.Type == "login") r = ok("password", it.Password);
                else if (it.Type == "note") r = ok("notes", it.Notes);
                else if (it.Type == "card") r = ok("card number", it.CardNumber);
                else
                {
                    var h = ItemTypes.Ordered(it).FirstOrDefault(x => ItemTypes.IsHidden(x) && !ItemTypes.IsSeed(it, x) && x.Value.Length > 0);
                    r = h != null ? ok(h.Label, h.Value) : ok("notes", it.Notes);
                }
                return r;
            }
            // your own / the type's fields first, by key or label (a wallet's recovery phrase never)
            var cf = ItemTypes.Ordered(it).FirstOrDefault(x => string.Equals(x.Key, f, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Label, f, StringComparison.CurrentCultureIgnoreCase));
            if (cf == null && (lf == "password" || lf == "username" || lf == "user")) cf = ItemTypes.Ordered(it).FirstOrDefault(x => x.Key == (lf == "password" ? "password" : "username") || (lf == "password" && it.Type != "login" && ItemTypes.IsHidden(x) && !ItemTypes.IsSeed(it, x)));
            if (cf != null && !(it.Type == "login" && (lf == "password" || lf == "username" || lf == "user")))
            {
                if (ItemTypes.IsSeed(it, cf)) return new Found { Error = "a wallet's recovery phrase is never given to the command line", Code = CliClient.NotFound };
                if (cf.Kind == "totp") { var sp = Totp.Parse(cf.Value); return sp == null ? new Found { Error = "that 2FA field has no valid key", Code = CliClient.NotFound } : ok(cf.Label, Totp.Code(sp, Totp.Now())); }
                return ok(cf.Label, cf.Value);
            }
            switch (lf)
            {
                case "password": r = ok("password", it.Type == "login" ? it.Password : null); break;
                case "username": case "user": r = ok("username", it.Type == "login" ? it.Username : null); break;
                case "totp": case "otp": case "code":
                    var t = it.Totp.Length > 0 ? it.Totp : (ItemTypes.Ordered(it).Where(x => x.Kind == "totp").Select(x => x.Value).FirstOrDefault() ?? "");
                    var spec = Totp.Parse(t); if (spec == null) return new Found { Error = "\"" + it.Title + "\" has no 2FA key", Code = CliClient.NotFound };
                    r = ok("2FA code", Totp.Code(spec, Totp.Now())); break;
                case "notes": case "note": r = ok("notes", it.Notes); break;
                case "url": case "website": r = ok("website", it.Urls.FirstOrDefault()); break;
                case "title": r = ok("title", it.Title); break;
                case "email": r = ok("email", it.Email); break;
                case "phone": r = ok("phone", it.Phone); break;
                case "cardnumber": case "number": r = ok("card number", it.Type == "card" ? it.CardNumber : null); break;
                case "cvc": r = ok("security code", it.Type == "card" ? it.Cvc : null); break;
                case "expiry": r = ok("expiry", it.Type == "card" ? it.Expiry : null); break;
                case "pin": r = ok("PIN", it.Type == "card" ? it.Pin : null); break;
                case "cardholder": r = ok("name on card", it.Type == "card" ? it.CardHolder : null); break;
                case "name": case "fullname": r = ok("name", it.Type == "identity" ? it.FullName : null); break;
                case "address": r = ok("address", it.Type == "identity" ? string.Join(", ", new[] { it.Address, it.City, it.PostCode, it.Country }.Where(x => x.Length > 0)) : null); break;
            }
            return r ?? new Found { Error = "\"" + it.Title + "\" has no field called \"" + f + "\"", Code = CliClient.NotFound };
        }
    }

    public partial class MainWindow
    {
        SshAgent agent; Thread cliThread;
        readonly Dictionary<string, DateTime> remembered = new Dictionary<string, DateTime>();   // "item|program" -> until (cleared when locked)
        readonly List<ActivityEntry> activity = new List<ActivityEntry>();                     // this session only (phone changes are kept in the vault - see 2.7)
        static readonly object promptLock = new object();
        DateTime lastLockedNote;

        void Init26()
        {
            if (Snap.On) return;
            StartCliServer();
            if (S.SshAgentOn) StartAgent();
        }
        void Lock26() { lock (remembered) remembered.Clear(); }
        void Quit26() { if (agent != null) agent.Stop(); }

        public void AddActivity(string kind, string text)
        {
            lock (activity) { activity.Insert(0, new ActivityEntry { When = DateTime.Now, Kind = kind, Text = text }); if (activity.Count > 200) activity.RemoveAt(200); }
            TestLog("activity: " + kind + ": " + text);
        }
        T OnUiT<T>(Func<T> f) { if (Dispatcher.CheckAccess()) return f(); return (T)Dispatcher.Invoke(new Func<object>(() => f())); }

        // ---- the SSH agent
        void StartAgent()
        {
            if (agent != null) agent.Stop();
            agent = new SshAgent
            {
                Offers = () => OnUiT(() => SshOffers()),
                LoadKey = id => { var pem = OnUiT(() => { if (vault == null) return null; var it = vault.Data.Items.FirstOrDefault(i => i.Id == id && IsShown(i) && i.Type == "sshkey"); return it == null ? null : ItemTypes.Val(it, "privateKey"); }); if (pem == null) return null; try { return AgentKey.Parse(pem); } catch { return null; } },
                Approve = (o, c) =>
                {
                    var key = o.ItemId + "|" + (c.Path.Length > 0 ? c.Path : c.Name).ToLowerInvariant() + "|" + c.Parent.ToLowerInvariant();
                    lock (remembered) { DateTime until; if (remembered.TryGetValue(key, out until) && until > DateTime.Now) { AddActivity("ssh", c.Describe() + " used \"" + o.Title + "\" (remembered)"); return true; } }
                    bool remember;
                    var yes = Approve("SSH key", c.Describe() + " wants to use your SSH key \"" + o.Title + "\" (" + o.Fingerprint + ").", S.ApproveMinutes > 0, out remember);
                    if (yes && remember && S.ApproveMinutes > 0) lock (remembered) remembered[key] = DateTime.Now.AddMinutes(S.ApproveMinutes);
                    AddActivity("ssh", c.Describe() + (yes ? " used" : " was refused") + " \"" + o.Title + "\"" + (yes && remember ? " (remembered for " + S.ApproveMinutes + " min)" : ""));
                    return yes;
                },
                Locked = () => OnUiT(() =>
                {
                    if (vault != null || (DateTime.Now - lastLockedNote).TotalSeconds < 60) return 0;
                    lastLockedNote = DateTime.Now; Notify("Hallow Keys", "A program asked for your SSH keys, but Hallow Keys is locked - unlock it and try again."); return 0;
                }),
                Log = s => TestLog(s)
            };
            var name = S.SshOwnPipe ? SshAgent.CustomPipe : SshAgent.DefaultPipe;
            if (KeysSettings.TestDir != null) name = "hallowkeys-test-" + name;   // developer tests never touch the real pipes
            agent.Start(name);
        }
        void StopAgent() { if (agent != null) { agent.Stop(); agent = null; TestLog("ssh agent: stopped"); } }
        List<SshAgent.Offer> SshOffers()
        {
            var o = new List<SshAgent.Offer>();
            if (vault == null || !S.SshAgentOn) return o;
            foreach (var it in vault.Data.Items.Where(i => i.Type == "sshkey" && IsShown(i) && S.SshKeyIds.Contains(i.Id)))
            {
                byte[] blob; try { using (var k = AgentKey.Parse(ItemTypes.Val(it, "privateKey"))) blob = k.Blob; } catch { continue; }   // only keys it can sign with (not ones with a passphrase)
                o.Add(new SshAgent.Offer { ItemId = it.Id, Title = it.Title, Blob = blob, Fingerprint = SshKeys.Fingerprint(blob) });
            }
            return o;
        }

        // ---- "may it?" - a small prompt (or Windows Hello). Blocks the caller's thread, never the window's. One question at a time.
        bool Approve(string what, string text, bool offerRemember, out bool remember)
        {
            remember = false;
            lock (promptLock)
            {
                if (OnUiT(() => vault == null)) return false;
                if (KeysSettings.TestDir != null && Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_NOSHOW") == "1")
                {
                    // developer tests: nothing on the screen - the answer comes from test-approve.txt (allow / remember / deny / hello)
                    string a = "deny"; try { a = File.ReadAllText(Path.Combine(KeysSettings.Dir, "test-approve.txt")).Trim(); } catch { }
                    TestLog("prompt: " + what + ": " + text + " -> " + a);
                    if (a == "hello") { string err; return Hello.Verify(text, out err); }
                    remember = a == "remember" && offerRemember;
                    return a == "allow" || remember;
                }
                if (S.ApproveWithHello && Hello.Available()) { string err; var ok = Hello.Verify("Hallow Keys: " + text, out err); return ok; }
                bool rem = false;
                var yes = OnUiT(() => ShowApproval(what, text, offerRemember, out rem));
                remember = rem;
                return yes;
            }
        }
        bool ShowApproval(string what, string text, bool offerRemember, out bool remember)
        {
            var w = ApprovalWindow(what, text, offerRemember); bool? answer = null;
            var allow = (Button)w.Tag; var cb = (CheckBox)((StackPanel)allow.Parent).Tag;
            allow.Click += (s, e) => { answer = true; w.Close(); };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) }; timer.Tick += (s, e) => { timer.Stop(); w.Close(); }; timer.Start();
            w.ShowDialog(); timer.Stop();
            remember = cb != null && cb.IsChecked == true;
            return answer == true;
        }
        Window ApprovalWindow(string what, string text, bool offerRemember)
        {
            var w = new Window { Title = "Hallow Keys - " + what, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = (Brush)FindResource("Bg"), Foreground = (Brush)FindResource("Text"), FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, ShowInTaskbar = true };
            try { w.Icon = AppIcon(); } catch { }
            var sp = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var ic = Icons.Make(what == "SSH key" ? "terminal" : "code", 16, B(Program.AccentHex)); ic.Margin = new Thickness(0, 0, 10, 0); ic.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = what == "SSH key" ? "Use an SSH key?" : "Give a secret to the command line?", FontWeight = FontWeights.SemiBold, FontSize = 15 });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = "Only allow it if you just started this yourself. No answer in 60 seconds counts as no.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 10) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            CheckBox cb = null;
            if (offerRemember) { cb = new CheckBox { Content = "Don't ask again for " + S.ApproveMinutes + " minutes for this program", Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 0, 0, 10) }; sp.Children.Add(cb); }
            var deny = new Button { Content = "Don't allow", Style = St("Btn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true }; deny.Click += (s, e) => w.Close();
            var allow = new Button { Content = "Allow", Style = St("Btn"), IsDefault = false };

            row.Children.Add(deny); row.Children.Add(allow); row.Tag = cb; sp.Children.Add(row);
            w.Content = sp; w.Tag = allow;
            return w;
        }

        // ---- the command line's pipe (only this Windows user)
        void StartCliServer()
        {
            cliThread = new Thread(() =>
            {
                int fails = 0; var name = CliClient.PipeName();
                while (true)
                {
                    NamedPipeServerStream pipe = null;
                    try
                    {
                        var sec = new PipeSecurity();
                        sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                        pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 65536, 65536, sec);
                        pipe.WaitForConnection(); fails = 0; var p = pipe; pipe = null;
                        new Thread(() => CliServe(p)) { IsBackground = true, Name = "cli client" }.Start();
                    }
                    catch { if (pipe != null) try { pipe.Dispose(); } catch { } Thread.Sleep(Math.Min(30000, 500 * ++fails)); }
                }
            }) { IsBackground = true, Name = "cli pipe" };
            cliThread.Start();
        }
        static readonly JavaScriptSerializer cliJs = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        void CliServe(NamedPipeServerStream p)
        {
            try
            {
                var who = SshAgent.WhoIs(p);
                var r = new StreamReader(p, new UTF8Encoding(false)); var t = r.ReadLineAsync();
                string reply = t.Wait(3000) && t.Result != null && t.Result.Length < 65536 ? CliAnswer(t.Result, who) : Fail(CliClient.Usage, "bad request");
                var b = new UTF8Encoding(false).GetBytes(reply + "\n"); p.Write(b, 0, b.Length); p.Flush(); Array.Clear(b, 0, b.Length);
                try { p.WaitForPipeDrain(); } catch { }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        static string Fail(int code, string msg) { return cliJs.Serialize(new Dictionary<string, object> { { "ok", false }, { "code", code }, { "msg", msg } }); }
        // public for the tests: one request from the command line -> the reply
        public string CliAnswer(string json, SshAgent.Caller who)
        {
            Dictionary<string, object> m; try { m = cliJs.Deserialize<Dictionary<string, object>>(json); } catch { return Fail(CliClient.Usage, "bad request"); }
            var op = m.ContainsKey("op") ? m["op"] as string : ""; var refs = m.ContainsKey("refs") ? m["refs"] as System.Collections.ArrayList : null;
            var caller = who.Describe();
            if (!OnUiT(() => S.CliOn)) { AddActivity("cli", caller + " asked, but the command line is off"); return Fail(CliClient.Off, "the command line is turned off - turn it on in Hallow Keys: Settings > Developer tools"); }
            if (refs == null || refs.Count == 0 || refs.Count > 50 || (op != "get" && op != "env" && op != "run")) return Fail(CliClient.Usage, "bad request");
            if (OnUiT(() => vault == null)) { AddActivity("cli", caller + " asked while Hallow Keys was locked"); return Fail(CliClient.IsLocked, "Hallow Keys is locked - unlock it and try again"); }
            var found = OnUiT(() =>
            {
                var shown = Shown().Items;
                return refs.Cast<object>().Select(o => { var d = o as Dictionary<string, object>; if (d == null) return new CliLookup.Found { Error = "bad request", Code = CliClient.Usage }; return CliLookup.Resolve(shown, d.ContainsKey("ref") ? d["ref"] as string : "", d.ContainsKey("field") ? d["field"] as string : null); }).ToList();
            });
            var bad = found.FirstOrDefault(f => f.Error != null);
            if (bad != null) { AddActivity("cli", caller + " asked for something that isn't there"); return Fail(bad.Code, bad.Error); }
            var names = found.Select(f => "\"" + f.Item.Title + "\" (" + f.Field + ")").Distinct().ToList();
            var list = names.Count <= 4 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + " and " + (names.Count - 3) + " more";
            var verb = op == "get" ? "--get" : op == "env" ? "--env (fill a template)" : "--run (secrets for a command it starts)";
            bool rem;
            var yes = Approve("Command line", caller + " asks, with " + verb + ", for " + list + ".", false, out rem);
            AddActivity("cli", caller + (yes ? " was given " : " was refused ") + list);
            if (!yes) return Fail(CliClient.Denied, "not allowed in Hallow Keys (or no answer in 60 seconds)");
            OnUiT(() => { foreach (var f in found) Vaults.MarkUsed(vault.Data, f.Item.Id, DateTime.UtcNow); return 0; });
            return cliJs.Serialize(new Dictionary<string, object> { { "ok", true }, { "values", found.Select(f => f.Value).ToList() } });
        }

        // ---- Settings > Developer tools
        void DevToolsCard(StackPanel page)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Developer tools", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            box.Children.Add(new TextBlock { Text = "An SSH agent for ssh, git and anything else that uses Windows' OpenSSH, and a command line for scripts and .env files. Both are off until you turn them on, work only while Hallow Keys is unlocked, and ask you before every use.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            // SSH agent
            box.Children.Add(Switch("SSH agent: serve the SSH keys I pick below", S.SshAgentOn, v => { S.SshAgentOn = v; S.Save(); if (v) StartAgent(); else StopAgent(); ShowMain(); }));
            var svc = Snap.On ? "Disabled" : SshAgent.ServiceState();
            string status;
            if (!S.SshAgentOn) status = "Off." + (svc == "Running" ? " Windows' own OpenSSH Authentication Agent service is running on this PC - Hallow Keys will use its own pipe next to it." : "");
            else if (agent != null && agent.Problem != null) status = agent.Problem + " Hallow Keys doesn't change that service (it's a Windows setting) - switch to Hallow Keys' own pipe below and point ssh at it.";
            else status = "Serving \\\\.\\pipe\\" + (S.SshOwnPipe ? SshAgent.CustomPipe : SshAgent.DefaultPipe) + (S.SshOwnPipe ? " - point ssh and git at it with one of the lines below." : " - Windows' ssh, ssh-add, ssh-keygen and git use it without any set-up.");
            box.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Foreground = agent != null && agent.Problem != null ? B("#F0BE5A") : Muted, Margin = new Thickness(0, 2, 0, 6) });
            box.Children.Add(Switch("Use Hallow Keys' own pipe (\\\\.\\pipe\\" + SshAgent.CustomPipe + ") instead of Windows' standard one", S.SshOwnPipe, v => { S.SshOwnPipe = v; S.Save(); if (S.SshAgentOn) StartAgent(); ShowMain(); }));
            if (S.SshOwnPipe || (agent != null && agent.Problem != null) || Snap.On)
            {
                CopyLine(box, "PowerShell (this window only):", "$env:SSH_AUTH_SOCK = '\\\\.\\pipe\\" + SshAgent.CustomPipe + "'");
                CopyLine(box, "Every program - add to %USERPROFILE%\\.ssh\\config:", "Host *\n  IdentityAgent //./pipe/" + SshAgent.CustomPipe);
            }
            // keys on offer
            var keys = vault == null ? new List<Item>() : vault.Data.Items.Where(i => i.Type == "sshkey" && IsShown(i)).OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            box.Children.Add(new TextBlock { Text = keys.Count == 0 ? "No SSH keys in your vault yet - add one with New item > SSH key (paste it, or make an RSA key there)." : "Keys on offer (only ticked ones are shown to ssh):", Foreground = Muted, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });
            foreach (var it in keys)
            {
                var item = it; string why = null; string desc = "";
                try { using (var k = AgentKey.Parse(ItemTypes.Val(it, "privateKey"))) { var info = SshKeys.Describe(k.Blob); desc = info == null ? k.Type : info.Pretty + "  " + info.Fingerprint; } } catch (Exception e) { why = e.Message; }
                var cb = new CheckBox { Content = it.Title + (desc.Length > 0 ? "   (" + desc + ")" : ""), IsChecked = why == null && S.SshKeyIds.Contains(it.Id), IsEnabled = why == null, Foreground = (Brush)FindResource("Text"), Margin = new Thickness(4, 2, 0, 2) };
                cb.Checked += (s, e) => { if (!S.SshKeyIds.Contains(item.Id)) S.SshKeyIds.Add(item.Id); S.Save(); };
                cb.Unchecked += (s, e) => { S.SshKeyIds.Remove(item.Id); S.Save(); };
                box.Children.Add(cb);
                if (why != null) box.Children.Add(new TextBlock { Text = why, Foreground = B("#F0BE5A"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 0, 0, 4) });
            }
            // approvals
            var ar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
            ar.Children.Add(new TextBlock { Text = "The SSH prompt may offer \"don't ask again\" for", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var mins = new[] { 0, 1, 5, 10, 30, 60 };
            var cbm = new ComboBox { Width = 150, Style = St("Combo"), ItemsSource = new[] { "never (ask every time)", "1 minute", "5 minutes", "10 minutes", "30 minutes", "1 hour" }, SelectedIndex = Math.Max(0, Array.IndexOf(mins, S.ApproveMinutes)) };
            cbm.SelectionChanged += (s, e) => { S.ApproveMinutes = mins[Math.Max(0, cbm.SelectedIndex)]; S.Save(); lock (remembered) remembered.Clear(); };
            ar.Children.Add(cbm); box.Children.Add(ar);
            box.Children.Add(new TextBlock { Text = "per program and key. Remembered approvals are forgotten when Hallow Keys locks. The command line asks every time.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            if (Snap.On || Hello.Available()) box.Children.Add(Switch("Approve with Windows Hello instead of the prompt", S.ApproveWithHello, v => { S.ApproveWithHello = v; S.Save(); }));
            box.Children.Add(new TextBlock { Text = "Git commit signing with an SSH key: git config --global gpg.format ssh, git config --global user.signingkey \"key::<the public key line>\", git config --global commit.gpgsign true (and gpg.ssh.program C:/Windows/System32/OpenSSH/ssh-keygen.exe if git uses its own ssh). Each commit then asks here once.", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            // command line
            box.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new Thickness(0, 12, 0, 10) });
            box.Children.Add(Switch("Command line: hk.exe (and HallowKeys.exe) --get, --env and --run may ask for secrets", S.CliOn, v => { S.CliOn = v; S.Save(); }));
            box.Children.Add(new TextBlock { Text = "hk --get \"GitHub\" --field password   |   hk --env .env.template --out .env   ({{hk:Item/field}} placeholders)   |   $env:DB_PASS = 'hk:Database/password'; hk --run -- npm start\nhk.exe is in Hallow Keys' folder. Every use shows a prompt naming the program and the items. Exit codes: 0 ok, 2 not running, 3 locked, 4 not found, 5 not allowed, 6 turned off (see README).", Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            page.Children.Add(Card(box));
            ActivityCard(page);
        }
        void CopyLine(StackPanel box, string label, string line)
        {
            box.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 2) });
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var copy = new Button { Content = "Copy", Style = St("Btn") }; copy.Click += (s, e) => { try { Clipboard.SetText(line.Replace("\n", "\r\n")); Toast("Copied."); } catch { } };
            DockPanel.SetDock(copy, Dock.Right); row.Children.Add(copy);
            row.Children.Add(new TextBox { Text = line, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), Style = St("Field"), Margin = new Thickness(0, 0, 8, 0), AcceptsReturn = true });
            box.Children.Add(row);
        }
        // the Activity list: this session's SSH / command line uses, and (v2.7) changes from the phone
        void ActivityCard(StackPanel page)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Activity", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            List<ActivityEntry> list; lock (activity) list = activity.ToList();
            list.AddRange(PhoneActivity());
            list = list.OrderByDescending(a => a.When).Take(25).ToList();
            if (list.Count == 0) box.Children.Add(new TextBlock { Text = "Nothing yet. SSH key uses and command line requests (this session) and changes from your phone show up here - never a password.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            foreach (var a in list)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var when = new TextBlock { Text = a.When.ToString(a.When.Date == DateTime.Today ? "HH:mm" : "d MMM HH:mm"), Foreground = Muted, Width = 90 }; DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
                var kind = new TextBlock { Text = a.Kind == "ssh" ? "SSH" : a.Kind == "cli" ? "Command line" : a.Kind == "phone" ? "Phone" : a.Kind, Foreground = Muted, Width = 100 }; DockPanel.SetDock(kind, Dock.Left); row.Children.Add(kind);
                row.Children.Add(new TextBlock { Text = a.Text, TextWrapping = TextWrapping.Wrap });
                box.Children.Add(row);
            }
            PhoneUndoRow(box);
            page.Children.Add(Card(box));
        }

        // ---- --snap pictures (made-up keys only; nothing served)
        void SnapExtra26(MainWindow w)
        {
            string priv, pub; SshKeys.GenerateRsa(3072, "made-up@example", out priv, out pub);
            var it = w.vault.Data.Items.FirstOrDefault(i => i.Type == "sshkey");
            if (it != null) { ItemTypes.Set(it, "privateKey", priv); ItemTypes.Set(it, "publicKey", pub); w.S.SshKeyIds.Add(it.Id); }
            w.S.SshAgentOn = true; w.S.CliOn = true; w.S.SshOwnPipe = true;
            w.AddActivity("ssh", "ssh.exe (from git.exe) used \"Home server key\" (remembered for 10 min)");
            w.AddActivity("cli", "hk.exe (from powershell.exe) was given \"Example API\" (API key / token)");
            w.view = "settings"; w.ShowMain(); Snap.Save(w, Path.Combine(snapDir, "v26-settings.png"), 1280, 3600);
            var pw = w.ApprovalWindow("SSH key", "ssh.exe (from git.exe) wants to use your SSH key \"Home server key\" (SHA256:made-up-fingerprint).", true);
            Snap.Save(pw, Path.Combine(snapDir, "v26-ssh-prompt.png"), 460, 0);
            var cw = w.ApprovalWindow("Command line", "hk.exe (from powershell.exe) asks, with --run (secrets for a command it starts), for \"Example database\" (password), \"Example API\" (API key / token).", false);
            Snap.Save(cw, Path.Combine(snapDir, "v26-cli-prompt.png"), 460, 0);
        }
    }
}
