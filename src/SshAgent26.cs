// Hallow Keys 2.6 - "Developer tools": an SSH agent that serves the SSH keys in your vault (like 1Password's).
// The ssh-agent protocol (draft-miller-ssh-agent) over a Windows named pipe - Windows' own OpenSSH (ssh, ssh-add, ssh-keygen, git)
// talks to \\.\pipe\openssh-ssh-agent by default, or to any pipe named in SSH_AUTH_SOCK / IdentityAgent.
//   - only two requests are answered: "which keys do you have" (only the keys you picked, only while Hallow Keys is unlocked) and
//     "sign this" (each one approved first - a small prompt or Windows Hello, optionally remembered for a few minutes per program).
//     Adding, removing or locking keys from outside is refused: the keys live in the vault and nowhere else.
//   - keys: OpenSSH private keys without a passphrase (they're kept inside the encrypted vault): Ed25519 (RFC 8032, Ed25519.cs),
//     RSA (rsa-sha2-256 / rsa-sha2-512 only - never SHA-1 "ssh-rsa" signatures) and ECDSA P-256 / P-384 / P-521 (CNG).
//     Keys protected with a passphrase, and hardware (sk-) keys, aren't offered - the app says why.
//   - the pipe: only this Windows user can open it, never over the network. The private key is read from the vault for each signature
//     and wiped after; nothing is written anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace HallowKeys
{
    // one private key, ready to sign (wipe it with Dispose)
    public class AgentKey : IDisposable
    {
        public string Type = "";        // ssh-ed25519, ssh-rsa, ecdsa-sha2-nistp256 / 384 / 521
        public byte[] Blob;             // the public key blob (what ssh-add -L shows, base64)
        public string Comment = "";
        byte[] seed;                    // Ed25519
        RSAParameters rsa;              // RSA
        ECParameters ec; bool isEc;     // ECDSA
        public const uint RsaSha256 = 2, RsaSha512 = 4;

        // OpenSSH's own format ("-----BEGIN OPENSSH PRIVATE KEY-----"), not encrypted. Throws VaultException with a reason otherwise.
        public static AgentKey Parse(string pem)
        {
            var s = (pem ?? "").Trim(); const string begin = "-----BEGIN OPENSSH PRIVATE KEY-----", end = "-----END OPENSSH PRIVATE KEY-----";
            int a = s.IndexOf(begin), z = s.IndexOf(end);
            if (a < 0 || z < a)
            {
                if (s.Contains("PRIVATE KEY-----")) throw new VaultException("Only OpenSSH's own key format is offered (\"BEGIN OPENSSH PRIVATE KEY\") - convert it with ssh-keygen -p -f <file>.");
                throw new VaultException("There's no private key in this item.");
            }
            byte[] b; try { b = Convert.FromBase64String(new string(s.Substring(a + begin.Length, z - a - begin.Length).Where(c => !char.IsWhiteSpace(c)).ToArray())); } catch { throw new VaultException("The private key is damaged."); }
            byte[] priv = null;
            try
            {
                var magic = Encoding.ASCII.GetBytes("openssh-key-v1\0");
                if (b.Length < magic.Length || !b.Take(magic.Length).SequenceEqual(magic)) throw new VaultException("The private key is damaged.");
                int p = magic.Length;
                var cipher = Encoding.ASCII.GetString(Wire.Str(b, ref p)); var kdf = Encoding.ASCII.GetString(Wire.Str(b, ref p)); Wire.Str(b, ref p);
                if (cipher != "none" || kdf != "none") throw new VaultException("This key is protected with a passphrase, so it isn't offered. It's already inside your encrypted vault: remove its passphrase (ssh-keygen -p -f <file>) and paste it again.");
                if (Wire.U32(b, ref p) != 1) throw new VaultException("Only files with one key are offered.");
                var blob = Wire.Str(b, ref p);
                priv = Wire.Str(b, ref p);
                int q = 0; uint c1 = Wire.U32(priv, ref q), c2 = Wire.U32(priv, ref q);
                if (c1 != c2) throw new VaultException("The private key is damaged.");
                var k = new AgentKey { Type = Encoding.ASCII.GetString(Wire.Str(priv, ref q)) };
                if (k.Type == "ssh-ed25519")
                {
                    var pub = Wire.Str(priv, ref q); var sk = Wire.Str(priv, ref q);
                    if (pub.Length != 32 || sk.Length != 64) throw new VaultException("The private key is damaged.");
                    k.seed = sk.Take(32).ToArray(); Array.Clear(sk, 0, sk.Length);
                    if (!Ed25519.PublicKey(k.seed).SequenceEqual(pub)) { k.Dispose(); throw new VaultException("The private key doesn't match its public key."); }
                }
                else if (k.Type == "ssh-rsa")
                {
                    var n = Wire.Str(priv, ref q); var e = Wire.Str(priv, ref q); var d = Wire.Str(priv, ref q); var iqmp = Wire.Str(priv, ref q); var pp = Wire.Str(priv, ref q); var qq = Wire.Str(priv, ref q);
                    k.rsa = RsaParams(n, e, d, iqmp, pp, qq);
                    foreach (var x in new[] { d, iqmp, pp, qq }) Array.Clear(x, 0, x.Length);
                }
                else if (k.Type.StartsWith("ecdsa-sha2-nistp"))
                {
                    var curve = Encoding.ASCII.GetString(Wire.Str(priv, ref q)); var Q = Wire.Str(priv, ref q); var d = Wire.Str(priv, ref q);
                    int size = curve == "nistp256" ? 32 : curve == "nistp384" ? 48 : curve == "nistp521" ? 66 : 0;
                    if (size == 0 || "ecdsa-sha2-" + curve != k.Type || Q.Length != 1 + 2 * size || Q[0] != 4) throw new VaultException("This ECDSA curve isn't supported.");
                    k.isEc = true;
                    k.ec = new ECParameters { Curve = curve == "nistp256" ? ECCurve.NamedCurves.nistP256 : curve == "nistp384" ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP521,
                        Q = new ECPoint { X = Q.Skip(1).Take(size).ToArray(), Y = Q.Skip(1 + size).ToArray() }, D = Fixed(d, size) };
                    Array.Clear(d, 0, d.Length);
                }
                else if (k.Type.StartsWith("sk-")) throw new VaultException("Hardware security key (sk-) keys need the key itself plugged in, so they aren't offered.");
                else throw new VaultException("Keys of type " + k.Type + " aren't supported (Ed25519, RSA and ECDSA are).");
                k.Comment = Encoding.UTF8.GetString(Wire.Str(priv, ref q));
                k.Blob = blob;
                return k;
            }
            catch (VaultException) { throw; }
            catch { throw new VaultException("The private key is damaged."); }
            finally { if (priv != null) Array.Clear(priv, 0, priv.Length); Array.Clear(b, 0, b.Length); }
        }
        static byte[] Fixed(byte[] mpint, int size)
        {
            int i = 0; while (i < mpint.Length - 1 && mpint[i] == 0 && mpint.Length - i > size) i++;
            var v = mpint.Skip(i).ToArray(); if (v.Length > size) throw new VaultException("The private key is damaged.");
            var o = new byte[size]; Buffer.BlockCopy(v, 0, o, size - v.Length, v.Length); Array.Clear(v, 0, v.Length); return o;
        }
        static BigInteger Big(byte[] be) { var le = be.Reverse().Concat(new byte[] { 0 }).ToArray(); var r = new BigInteger(le); Array.Clear(le, 0, le.Length); return r; }
        static byte[] Be(BigInteger v, int size)
        {
            var le = v.ToByteArray(); int n = le.Length; while (n > 0 && le[n - 1] == 0) n--;
            if (n > size) throw new VaultException("The private key is damaged.");
            var o = new byte[size]; for (int i = 0; i < n; i++) o[size - 1 - i] = le[i];
            Array.Clear(le, 0, le.Length); return o;
        }
        static RSAParameters RsaParams(byte[] n, byte[] e, byte[] d, byte[] iqmp, byte[] p, byte[] q)
        {
            var N = Big(n); var D = Big(d); var P = Big(p); var Q = Big(q);
            if (P * Q != N) throw new VaultException("The private key is damaged.");
            int size = n.SkipWhile(x => x == 0).Count(); int half = (size + 1) / 2;
            var ee = e.SkipWhile(x => x == 0).ToArray(); if (ee.Length == 0) throw new VaultException("The private key is damaged.");
            return new RSAParameters { Modulus = Be(N, size), Exponent = ee, D = Be(D, size),
                P = Be(P, half), Q = Be(Q, half), DP = Be(D % (P - 1), half), DQ = Be(D % (Q - 1), half), InverseQ = Be(BigInteger.ModPow(Q, P - 2, P), half) };
        }

        // the signature blob for "sign this" (flags: rsa-sha2-256 / 512). null = refused (an RSA SHA-1 request)
        public byte[] Sign(byte[] data, uint flags)
        {
            var o = new MemoryStream();
            if (Type == "ssh-ed25519") { Wire.PutStr(o, Encoding.ASCII.GetBytes("ssh-ed25519")); Wire.PutStr(o, Ed25519.Sign(seed, data)); }
            else if (Type == "ssh-rsa")
            {
                HashAlgorithmName h; string name;
                if ((flags & RsaSha512) != 0) { h = HashAlgorithmName.SHA512; name = "rsa-sha2-512"; }
                else if ((flags & RsaSha256) != 0) { h = HashAlgorithmName.SHA256; name = "rsa-sha2-256"; }
                else return null;
                using (var r = new RSACng()) { r.ImportParameters(rsa); Wire.PutStr(o, Encoding.ASCII.GetBytes(name)); Wire.PutStr(o, r.SignData(data, h, RSASignaturePadding.Pkcs1)); }
            }
            else if (isEc)
            {
                int size = ec.D.Length; var h = size == 32 ? HashAlgorithmName.SHA256 : size == 48 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA512;
                byte[] sig; using (var e = new ECDsaCng()) { e.ImportParameters(ec); sig = e.SignData(data, h); }
                var inner = new MemoryStream(); Wire.PutMpint(inner, sig.Take(sig.Length / 2).ToArray()); Wire.PutMpint(inner, sig.Skip(sig.Length / 2).ToArray());
                Wire.PutStr(o, Encoding.ASCII.GetBytes(Type)); Wire.PutStr(o, inner.ToArray());
            }
            else return null;
            return o.ToArray();
        }
        public void Dispose()
        {
            if (seed != null) Array.Clear(seed, 0, seed.Length);
            foreach (var a in new[] { rsa.D, rsa.P, rsa.Q, rsa.DP, rsa.DQ, rsa.InverseQ, ec.D }) if (a != null) Array.Clear(a, 0, a.Length);
        }
    }

    // SSH wire format helpers (RFC 4251)
    public static class Wire
    {
        public static uint U32(byte[] b, ref int p) { if (p + 4 > b.Length) throw new FormatException(); uint v = (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); p += 4; return v; }
        public static byte[] Str(byte[] b, ref int p) { uint n = U32(b, ref p); if (n > b.Length - p) throw new FormatException(); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, (int)n); p += (int)n; return r; }
        public static void PutU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        public static void PutStr(Stream s, byte[] b) { PutU32(s, (uint)b.Length); s.Write(b, 0, b.Length); }
        public static void PutMpint(Stream s, byte[] be)
        {
            int i = 0; while (i < be.Length && be[i] == 0) i++;
            var len = be.Length - i; bool pad = len > 0 && (be[i] & 0x80) != 0;
            PutU32(s, (uint)(len + (pad ? 1 : 0))); if (pad) s.WriteByte(0); s.Write(be, i, len);
        }
    }

    // the agent itself: the protocol over a pipe. The window gives it the keys on offer and the "may this program use it?" question.
    public class SshAgent
    {
        public const string DefaultPipe = "openssh-ssh-agent", CustomPipe = "hallow-keys-ssh-agent";
        public const byte Failure = 5, Success = 6, RequestIdentities = 11, IdentitiesAnswer = 12, SignRequest = 13, SignResponse = 14;
        public class Offer { public string ItemId = "", Title = "", Fingerprint = ""; public byte[] Blob; public string Comment = ""; }
        public class Caller { public int Pid; public string Name = "", Path = "", Parent = ""; public string Describe() { return Name + (Parent.Length > 0 && !Parent.Equals(Name, StringComparison.OrdinalIgnoreCase) ? " (from " + Parent + ")" : ""); } }

        public Func<List<Offer>> Offers = () => new List<Offer>();        // the keys on offer now (empty while locked)
        public Func<Offer, Caller, bool> Approve = (o, c) => false;       // asked for every signature (may block: a prompt)
        public Func<string, AgentKey> LoadKey = id => null;              // the private key of an offered item (read from the vault for this one signature)
        public Action<string> Log = s => { };                            // never anything secret: titles, programs, results
        public Action Locked = () => { };                                // a request came in while Hallow Keys was locked
        public string Pipe { get; private set; }
        public string Problem;                                           // why it isn't serving (another agent has the pipe...)
        volatile bool stopped; Thread loop;

        // is someone else serving this pipe (Windows' OpenSSH Authentication Agent service, another agent)?
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        public static bool PipeExists(string name) { if (WaitNamedPipe(@"\\.\pipe\" + name, 1)) return true; return Marshal.GetLastWin32Error() != 2; }
        // Windows' own agent service (we never change it - that's a system setting): "Running", "Stopped", "Disabled"... or null if not installed
        public static string ServiceState()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ssh-agent"))
                {
                    if (k == null) return null;
                    var start = k.GetValue("Start"); bool disabled = start is int && (int)start == 4;
                    bool running = PipeExists(DefaultPipe) && ServicePid() > 0;
                    return running ? "Running" : disabled ? "Disabled" : "Stopped";
                }
            }
            catch { return null; }
        }
        static int ServicePid()
        {
            try { foreach (var p in System.Diagnostics.Process.GetProcessesByName("ssh-agent")) { try { if (p.SessionId == 0) return p.Id; } catch { } } } catch { }
            return 0;
        }

        public bool Start(string pipe)
        {
            Stop(); Problem = null; Pipe = pipe;
            if (PipeExists(pipe))
            {
                Problem = pipe == DefaultPipe && ServicePid() > 0 ? "Windows' OpenSSH Authentication Agent service is running and owns the standard pipe." : "Another program already serves the pipe \\\\.\\pipe\\" + pipe + ".";
                Log("ssh agent: pipe taken (" + pipe + ")"); return false;
            }
            stopped = false;
            loop = new Thread(Loop) { IsBackground = true, Name = "ssh agent" }; loop.Start();
            Log("ssh agent: serving " + pipe);
            return true;
        }
        public void Stop() { stopped = true; var p = Pipe; if (loop != null && p != null) { try { using (var c = new NamedPipeClientStream(".", p, PipeDirection.InOut)) c.Connect(200); } catch { } } loop = null; }
        public bool Running { get { return loop != null && !stopped; } }

        void Loop()
        {
            int fails = 0; var name = Pipe;
            while (!stopped)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    var sec = new PipeSecurity();
                    sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.None, 65536, 65536, sec);
                    pipe.WaitForConnection(); fails = 0;
                    if (stopped) { pipe.Dispose(); break; }
                    var p = pipe; pipe = null;
                    new Thread(() => Serve(p)) { IsBackground = true, Name = "ssh agent client" }.Start();
                }
                catch
                {
                    if (pipe != null) try { pipe.Dispose(); } catch { }
                    if (stopped) break;
                    if (++fails > 20) { Problem = "The pipe couldn't be opened."; break; }
                    Thread.Sleep(Math.Min(10000, 300 * fails));
                }
            }
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
        public static Caller WhoIs(PipeStream p)
        {
            var c = new Caller();
            try { uint pid; if (GetNamedPipeClientProcessId(p.SafePipeHandle.DangerousGetHandle(), out pid)) c = Callers.Describe((int)pid); } catch { }
            return c;
        }
        void Serve(NamedPipeServerStream p)
        {
            try
            {
                var who = WhoIs(p);
                while (!stopped)
                {
                    var msg = ReadMsg(p); if (msg == null) break;
                    var reply = Answer(msg, who);
                    var o = new MemoryStream(); Wire.PutU32(o, (uint)reply.Length); o.Write(reply, 0, reply.Length);
                    var b = o.ToArray(); p.Write(b, 0, b.Length); p.Flush();
                }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        static byte[] ReadMsg(Stream s)
        {
            var h = ReadN(s, 4); if (h == null) return null;
            int q = 0; uint n = Wire.U32(h, ref q);
            if (n == 0 || n > 256 * 1024) return null;
            return ReadN(s, (int)n);
        }
        static byte[] ReadN(Stream s, int n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) return null; got += r; }
            return b;
        }
        // one request -> one reply (the message type and its body). Public for the tests.
        public byte[] Answer(byte[] msg, Caller who)
        {
            try
            {
                byte type = msg[0]; int p = 1;
                if (type == RequestIdentities)
                {
                    var offers = Offers();
                    if (offers.Count == 0) Locked();
                    var o = new MemoryStream(); o.WriteByte(IdentitiesAnswer); Wire.PutU32(o, (uint)offers.Count);
                    foreach (var k in offers) { Wire.PutStr(o, k.Blob); Wire.PutStr(o, Encoding.UTF8.GetBytes(k.Title.Length > 0 ? k.Title : k.Comment)); }
                    Log("ssh agent: listed " + offers.Count + " key(s) for " + who.Describe());
                    return o.ToArray();
                }
                if (type == SignRequest)
                {
                    var blob = Wire.Str(msg, ref p); var data = Wire.Str(msg, ref p); uint flags = p + 4 <= msg.Length ? Wire.U32(msg, ref p) : 0;
                    var offer = Offers().FirstOrDefault(k => k.Blob.SequenceEqual(blob));
                    if (offer == null) { Locked(); Log("ssh agent: sign refused - key not on offer (or locked)"); return new[] { Failure }; }
                    if (Encoding.ASCII.GetString(blob, 4, Math.Min(blob.Length - 4, 7)) == "ssh-rsa" && (flags & (AgentKey.RsaSha256 | AgentKey.RsaSha512)) == 0) { Log("ssh agent: refused an SHA-1 RSA signature"); return new[] { Failure }; }
                    if (!Approve(offer, who)) { Log("ssh agent: sign denied - " + offer.Title + " for " + who.Describe()); return new[] { Failure }; }
                    using (var key = LoadKey(offer.ItemId))
                    {
                        if (key == null || !key.Blob.SequenceEqual(blob)) { Log("ssh agent: key gone"); return new[] { Failure }; }
                        var sig = key.Sign(data, flags); if (sig == null) return new[] { Failure };
                        var o = new MemoryStream(); o.WriteByte(SignResponse); Wire.PutStr(o, sig);
                        Log("ssh agent: signed with " + offer.Title + " for " + who.Describe());
                        return o.ToArray();
                    }
                }
                return new[] { Failure };   // add / remove / lock / extensions: the keys live in the vault, not here
            }
            catch { return new[] { Failure }; }
        }
    }

    // which program is on the other end of a pipe (and what started it - "ssh.exe (from git.exe)")
    public static class Callers
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32 { public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        static readonly string[] Plumbing = { "ssh.exe", "ssh-add.exe", "ssh-keygen.exe", "hk.exe", "hallowkeys.exe", "cmd.exe", "conhost.exe", "sh.exe", "bash.exe", "git-remote-https.exe", "git-remote-ssh.exe" };
        public static SshAgent.Caller Describe(int pid)
        {
            var c = new SshAgent.Caller { Pid = pid };
            var map = new Dictionary<uint, PROCESSENTRY32>();
            var snap = CreateToolhelp32Snapshot(2, 0);
            if (snap != IntPtr.Zero && snap != new IntPtr(-1))
                try { var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) }; if (Process32FirstW(snap, ref e)) do { map[e.th32ProcessID] = e; } while (Process32NextW(snap, ref e)); }
                finally { CloseHandle(snap); }
            PROCESSENTRY32 me; if (!map.TryGetValue((uint)pid, out me)) { c.Name = "process " + pid; return c; }
            c.Name = me.szExeFile;
            try { using (var pr = System.Diagnostics.Process.GetProcessById(pid)) c.Path = pr.MainModule.FileName; } catch { }
            // walk up past the plumbing (shells, ssh itself) to the program that asked - at most 6 steps
            var cur = me; var seen = new HashSet<uint> { me.th32ProcessID };
            for (int i = 0; i < 6; i++)
            {
                PROCESSENTRY32 par; if (!map.TryGetValue(cur.th32ParentProcessID, out par) || seen.Contains(par.th32ProcessID)) break;
                seen.Add(par.th32ProcessID); cur = par;
                if (!Plumbing.Contains(par.szExeFile.ToLowerInvariant())) { c.Parent = par.szExeFile; break; }
                if (c.Parent.Length == 0 || par.szExeFile.Equals("git.exe", StringComparison.OrdinalIgnoreCase)) c.Parent = par.szExeFile;
            }
            return c;
        }
    }
}
