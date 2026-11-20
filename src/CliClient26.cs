// Hallow Keys 2.6 - the command line's client side (in HallowKeys.exe and in hk.exe, the small console version for scripts):
//   hk --get "<item>" [--field password|username|totp|notes|url|<your field>]   prints one secret
//   hk --env <template> [--out <new file>]    fills {{hk:item/field}} placeholders (stdout, or a new file - never overwrites one)
//   hk --run [--env <template>] -- <command> [args]   runs the command with secrets in its environment: variables whose value is
//                                                    hk:item/field (or holds {{hk:...}}), and the template's NAME=value lines
// Every use is approved in the running Hallow Keys (a prompt naming the program and the items), only while it's unlocked. This side
// only talks to the app's pipe ("HallowKeys.Cli.<user>", only this Windows user) and never writes a secret anywhere it wasn't asked to.
// Exit codes: 0 ok, 1 usage, 2 Hallow Keys isn't running, 3 locked, 4 item or field not found (or several items match),
// 5 denied / no answer in time, 6 the command line is turned off in Hallow Keys, 7 other error; --run: the command's own exit code.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HallowKeys
{
    public static class CliClient
    {
        public const int Ok = 0, Usage = 1, NotRunning = 2, IsLocked = 3, NotFound = 4, Denied = 5, Off = 6, Other = 7;
        public static string PipeName()
        {
            var t = Environment.GetEnvironmentVariable("HALLOW_KEYS_TESTDIR");
            return "HallowKeys.Cli." + Environment.UserName + (string.IsNullOrEmpty(t) ? "" : ".test");
        }
        public static bool IsCli(string[] args) { return args.Length > 0 && (args[0] == "--get" || args[0] == "--env" || args[0] == "--run"); }
        public static readonly Regex Placeholder = new Regex(@"\{\{\s*hk:([^}]+?)\s*\}\}", RegexOptions.Compiled);
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        static TextWriter Out, Err;
        public static int Run(string[] args, bool gui)
        {
            if (gui) { var h = GetStdHandle(-11); if (h == IntPtr.Zero || h == new IntPtr(-1)) AttachConsole(-1); }
            Out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            Err = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            try { return Go(args); }
            catch (Exception e) { Err.WriteLine("hk: " + e.Message); return Other; }
        }
        static int Fail(int code, string msg) { Err.WriteLine("hk: " + msg); return code; }
        const string Help = "usage: hk --get \"<item>\" [--field <field>] | hk --env <template> [--out <new file>] | hk --run [--env <template>] -- <command> [args]";

        static int Go(string[] args)
        {
            var op = args[0]; string item = null, field = null, tpl = null, outFile = null; var cmd = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                if (op == "--run" && a == "--") { cmd.AddRange(args.Skip(i + 1)); break; }
                if (a == "--field" && i + 1 < args.Length) field = args[++i];
                else if (a == "--out" && i + 1 < args.Length) outFile = args[++i];
                else if (a == "--env" && i + 1 < args.Length && op == "--run") tpl = args[++i];
                else if (op == "--get" && item == null && !a.StartsWith("--")) item = a;
                else if (op == "--env" && tpl == null && !a.StartsWith("--")) tpl = a;
                else return Fail(Usage, "unknown argument " + a + "\n" + Help);
            }
            if (op == "--get")
            {
                if (string.IsNullOrEmpty(item)) return Fail(Usage, Help);
                string[] vals; int c = Ask("get", new List<Dictionary<string, object>> { Ref(item, field) }, out vals); if (c != Ok) return c;
                Out.Write(vals[0]); if (!Console.IsOutputRedirected) Out.WriteLine(); return Ok;
            }
            if (op == "--env")
            {
                if (tpl == null) return Fail(Usage, Help);
                if (!File.Exists(tpl)) return Fail(Other, "no such template: " + tpl);
                if (outFile != null && File.Exists(outFile)) return Fail(Other, outFile + " already exists - pick a new file name (it's never overwritten)");
                var text = File.ReadAllText(tpl);
                var refs = Placeholder.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
                if (refs.Count == 0) return Fail(NotFound, "the template has no {{hk:item/field}} placeholders");
                string[] vals; int c = Ask("env", refs.Select(r => Ref(r, null)).ToList(), out vals); if (c != Ok) return c;
                var filled = Placeholder.Replace(text, m => vals[refs.IndexOf(m.Groups[1].Value.Trim())]);
                if (outFile == null) Out.Write(filled);
                else using (var f = new FileStream(outFile, FileMode.CreateNew, FileAccess.Write)) { var b = new UTF8Encoding(false).GetBytes(filled); f.Write(b, 0, b.Length); }
                return Ok;
            }
            if (op == "--run")
            {
                if (cmd.Count == 0) return Fail(Usage, Help);
                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // name -> value with placeholders
                foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                {
                    var v = kv.Value as string ?? ""; var k = kv.Key as string;
                    if (v.StartsWith("hk:") && v.Length > 3) env[k] = "{{" + v + "}}"; else if (Placeholder.IsMatch(v)) env[k] = v;
                }
                if (tpl != null)
                {
                    if (!File.Exists(tpl)) return Fail(Other, "no such template: " + tpl);
                    foreach (var line in File.ReadAllLines(tpl))
                    {
                        var l = line.Trim(); if (l.Length == 0 || l.StartsWith("#")) continue; int eq = l.IndexOf('='); if (eq <= 0) continue;
                        var name = l.Substring(0, eq).Trim(); if (name.StartsWith("export ")) name = name.Substring(7).Trim();
                        var val = l.Substring(eq + 1).Trim(); if (val.Length >= 2 && (val[0] == '"' && val[val.Length - 1] == '"' || val[0] == '\'' && val[val.Length - 1] == '\'')) val = val.Substring(1, val.Length - 2);
                        env[name] = val;
                    }
                }
                var refs = env.Values.SelectMany(v => Placeholder.Matches(v).Cast<Match>().Select(m => m.Groups[1].Value.Trim())).Distinct().ToList();
                var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = false };
                if (refs.Count > 0)
                {
                    string[] vals; int c = Ask("run", refs.Select(r => Ref(r, null)).ToList(), out vals); if (c != Ok) return c;
                    foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = Placeholder.Replace(kv.Value, m => vals[refs.IndexOf(m.Groups[1].Value.Trim())]);
                }
                else foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                psi.FileName = cmd[0]; psi.Arguments = string.Join(" ", cmd.Skip(1).Select(Quote));
                System.Diagnostics.Process p;
                try { p = System.Diagnostics.Process.Start(psi); }
                catch (System.ComponentModel.Win32Exception)
                {
                    psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"); psi.Arguments = "/d /s /c \"" + string.Join(" ", cmd.Select(Quote)) + "\"";   // .cmd / .bat (npm, yarn...)
                    try { p = System.Diagnostics.Process.Start(psi); } catch (Exception e) { return Fail(Other, "couldn't start " + cmd[0] + ": " + e.Message); }
                }
                p.WaitForExit(); var code = p.ExitCode; p.Dispose(); return code;
            }
            return Fail(Usage, Help);
        }
        static Dictionary<string, object> Ref(string item, string field) { var d = new Dictionary<string, object> { { "ref", item } }; if (field != null) d["field"] = field; return d; }
        static string Quote(string a) { if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a; return "\"" + Regex.Replace(Regex.Replace(a, @"(\\*)""", "$1$1\\\""), @"(\\+)$", "$1$1") + "\""; }

        // asks the running app; values in the same order as refs
        public static int Ask(string op, List<Dictionary<string, object>> refs, out string[] values)
        {
            values = null;
            var name = PipeName();
            using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            {
                try { c.Connect(1500); } catch { return Fail(NotRunning, "Hallow Keys isn't running (or is older than 2.6)"); }
                var req = new UTF8Encoding(false).GetBytes(js.Serialize(new Dictionary<string, object> { { "op", op }, { "refs", refs } }) + "\n");
                c.Write(req, 0, req.Length); c.Flush();
                var buf = new List<byte>(); var one = new byte[4096];
                while (true) { int n = c.Read(one, 0, one.Length); if (n <= 0) break; for (int i = 0; i < n; i++) { if (one[i] == (byte)'\n') goto done; buf.Add(one[i]); } }
                done:
                var arr = buf.ToArray(); var line = Encoding.UTF8.GetString(arr); Array.Clear(arr, 0, arr.Length); Array.Clear(one, 0, one.Length);
                Dictionary<string, object> r; try { r = js.Deserialize<Dictionary<string, object>>(line); } catch { return Fail(Other, "no answer from Hallow Keys"); }
                object ok; r.TryGetValue("ok", out ok);
                if (!(ok is bool) || !(bool)ok) { object code, msg; r.TryGetValue("code", out code); r.TryGetValue("msg", out msg); return Fail(code is int ? (int)code : Other, msg as string ?? "refused"); }
                values = ((System.Collections.ArrayList)r["values"]).Cast<object>().Select(o => o as string ?? "").ToArray();
                if (values.Length != refs.Count) return Fail(Other, "unexpected answer");
                return Ok;
            }
        }
    }
}
