// Hallow Keys 2.13 - passkeys in Hallow Browser. Hallow Browser (1.15+) has no browser extensions with native messaging (WebView2
// leaves that out), so - as for its login autofill since 1.0 - the browser itself plays the part of the extension's native host: it
// talks to this app over the same pipe with the same per-session secret (session.key, readable only by this Windows user), and the
// page address it sends is the real address of the frame that asked, taken from the browser engine, never from the page.
// What 2.13 adds on this side:
//  - who is on the other end of the pipe: Windows tells us the client's process (GetNamedPipeClientProcessId); a request that says it
//    comes from Hallow Browser ("client": "hallow-browser") is refused unless that process really is HallowBrowser.exe, and passkey
//    requests are only answered for the extension's host (HallowKeys.exe) or Hallow Browser - not for any other program that got hold
//    of the secret. (Every program of this Windows user could read the secret anyway; this is defence in depth, not a sandbox.)
//  - a passkey request from a cross-origin frame (Hallow Browser only forwards those when the frame was given the
//    publickey-credentials-* permission policy): "topUrl" is the tab's address, and clientDataJSON says crossOrigin true + topOrigin.
//  - everything else is unchanged: the same checks (https, rpId = the page's domain or listed in the rpId's /.well-known/webauthn),
//    the same "locked" answer, Windows Hello first when it's on; the prompt is Hallow Browser's own (in the browser window, which the
//    page can't reach or click), with the same choices as the extension's.
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace HallowKeys
{
    public static class PipeCallers
    {
        public const string BrowserClient = "hallow-browser";
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        const uint QueryLimited = 0x1000;   // PROCESS_QUERY_LIMITED_INFORMATION

        // the full path of the program on the other end of the pipe (null if Windows won't say)
        public static string ImageOf(NamedPipeServerStream p)
        {
            try
            {
                uint pid; if (!GetNamedPipeClientProcessId(p.SafePipeHandle.DangerousGetHandle(), out pid) || pid == 0) return null;
                var h = OpenProcess(QueryLimited, false, pid); if (h == IntPtr.Zero) return null;
                try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString(0, n) : null; }
                finally { CloseHandle(h); }
            }
            catch { return null; }
        }
        // "browser" (HallowBrowser.exe), "keys" (HallowKeys.exe - the extension's native host), "other"
        public static string Kind(string image)
        {
            if (string.IsNullOrEmpty(image)) return "other";
            string name; try { name = Path.GetFileName(image); } catch { return "other"; }
            if (string.Equals(name, "HallowBrowser.exe", StringComparison.OrdinalIgnoreCase)) return "browser";
            if (string.Equals(name, "HallowKeys.exe", StringComparison.OrdinalIgnoreCase)) return "keys";
            return "other";
        }
        // tests run their own stand-ins on the test pipe; HALLOW_KEYS_TEST_STRICTCALLER=1 turns the passkey caller rule on there too
        public static bool Strict { get { return KeysSettings.TestDir == null || Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_STRICTCALLER") == "1"; } }
        // null = answer it; else why not
        public static string Refuse(string type, string client, string kind, bool strict)
        {
            if (client == BrowserClient && kind != "browser") return "Only Hallow Browser can ask as Hallow Browser";
            if (strict && (type ?? "").StartsWith("passkey-") && kind != "browser" && kind != "keys") return "Passkey requests are only answered for the Hallow Keys extension and Hallow Browser";
            return null;
        }
        // the name shown in Windows Hello's question and the app's messages
        public static string Label(string client, string kind) { return client == BrowserClient && kind == "browser" ? "Hallow Browser" : null; }
    }
}
