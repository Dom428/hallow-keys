// Hallow Keys - the cryptography, all from Windows itself (nothing hand-rolled):
//  - AES-256-GCM through CNG (bcrypt.dll), a fresh random 96-bit nonce for every encryption
//  - PBKDF2-HMAC-SHA256 (Rfc2898DeriveBytes) for keys from passwords
//  - random numbers from the system CSPRNG (RNGCryptoServiceProvider), with rejection sampling (no modulo bias)
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HallowKeys
{
    public class AuthFailedException : Exception { public AuthFailedException() : base("Authentication failed") { } }

    public static class Gcm
    {
        public const int KeySize = 32, NonceSize = 12, TagSize = 16;
        const uint STATUS_AUTH_TAG_MISMATCH = 0xC000A002;

        [StructLayout(LayoutKind.Sequential)]
        struct AUTH_INFO   // BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
        {
            public int cbSize, dwInfoVersion;
            public IntPtr pbNonce; public int cbNonce;
            public IntPtr pbAuthData; public int cbAuthData;
            public IntPtr pbTag; public int cbTag;
            public IntPtr pbMacContext; public int cbMacContext;
            public int cbAAD; public long cbData; public int dwFlags;
        }
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] static extern uint BCryptOpenAlgorithmProvider(out IntPtr alg, string id, string impl, uint flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] static extern uint BCryptSetProperty(IntPtr h, string prop, byte[] input, int size, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptGenerateSymmetricKey(IntPtr alg, out IntPtr key, IntPtr obj, int objSize, byte[] secret, int secretSize, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptEncrypt(IntPtr key, byte[] input, int inSize, ref AUTH_INFO info, IntPtr iv, int ivSize, byte[] output, int outSize, out int result, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptDecrypt(IntPtr key, byte[] input, int inSize, ref AUTH_INFO info, IntPtr iv, int ivSize, byte[] output, int outSize, out int result, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptDestroyKey(IntPtr key);
        [DllImport("bcrypt.dll")] static extern uint BCryptCloseAlgorithmProvider(IntPtr alg, uint flags);

        static IntPtr alg = IntPtr.Zero; static readonly object gate = new object();
        static IntPtr Alg()
        {
            lock (gate)
            {
                if (alg != IntPtr.Zero) return alg;
                IntPtr a; Check(BCryptOpenAlgorithmProvider(out a, "AES", null, 0));
                var mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(a, "ChainingMode", mode, mode.Length, 0));
                alg = a; return alg;
            }
        }
        static void Check(uint st) { if (st != 0) throw new CryptographicException("CNG error 0x" + st.ToString("X8")); }

        // ciphertext and tag come back separately; the caller supplies the nonce (Encrypt below makes a fresh random one)
        public static void EncryptRaw(byte[] key, byte[] nonce, byte[] plain, byte[] aad, out byte[] cipher, out byte[] tag)
        {
            if (key == null || key.Length != KeySize) throw new ArgumentException("key");
            if (nonce == null || nonce.Length != NonceSize) throw new ArgumentException("nonce");
            plain = plain ?? new byte[0]; aad = aad ?? new byte[0];
            IntPtr k; Check(BCryptGenerateSymmetricKey(Alg(), out k, IntPtr.Zero, 0, key, key.Length, 0));
            var t = new byte[TagSize]; var c = new byte[plain.Length];
            GCHandle hn = GCHandle.Alloc(nonce, GCHandleType.Pinned), ha = GCHandle.Alloc(aad, GCHandleType.Pinned), ht = GCHandle.Alloc(t, GCHandleType.Pinned);
            try
            {
                var info = Info(hn, nonce.Length, ha, aad.Length, ht);
                int got; Check(BCryptEncrypt(k, plain, plain.Length, ref info, IntPtr.Zero, 0, c, c.Length, out got, 0));
            }
            finally { hn.Free(); ha.Free(); ht.Free(); BCryptDestroyKey(k); }
            cipher = c; tag = t;
        }
        // throws AuthFailedException when the tag doesn't match (wrong key, or anything changed)
        public static byte[] DecryptRaw(byte[] key, byte[] nonce, byte[] cipher, byte[] tag, byte[] aad)
        {
            if (key == null || key.Length != KeySize || nonce == null || nonce.Length != NonceSize || tag == null || tag.Length != TagSize) throw new AuthFailedException();
            cipher = cipher ?? new byte[0]; aad = aad ?? new byte[0];
            IntPtr k; Check(BCryptGenerateSymmetricKey(Alg(), out k, IntPtr.Zero, 0, key, key.Length, 0));
            var p = new byte[cipher.Length]; var t = (byte[])tag.Clone();
            GCHandle hn = GCHandle.Alloc(nonce, GCHandleType.Pinned), ha = GCHandle.Alloc(aad, GCHandleType.Pinned), ht = GCHandle.Alloc(t, GCHandleType.Pinned);
            uint st;
            try { var info = Info(hn, nonce.Length, ha, aad.Length, ht); int got; st = BCryptDecrypt(k, cipher, cipher.Length, ref info, IntPtr.Zero, 0, p, p.Length, out got, 0); }
            finally { hn.Free(); ha.Free(); ht.Free(); BCryptDestroyKey(k); }
            if (st == STATUS_AUTH_TAG_MISMATCH) { Wipe(p); throw new AuthFailedException(); }
            if (st != 0) { Wipe(p); throw new AuthFailedException(); }
            return p;
        }
        static AUTH_INFO Info(GCHandle n, int nl, GCHandle a, int al, GCHandle t)
        {
            return new AUTH_INFO { cbSize = Marshal.SizeOf(typeof(AUTH_INFO)), dwInfoVersion = 1, pbNonce = n.AddrOfPinnedObject(), cbNonce = nl,
                pbAuthData = al > 0 ? a.AddrOfPinnedObject() : IntPtr.Zero, cbAuthData = al, pbTag = t.AddrOfPinnedObject(), cbTag = TagSize };
        }

        // nonce || ciphertext || tag, with a fresh random nonce
        public static byte[] Seal(byte[] key, byte[] plain, byte[] aad)
        {
            var nonce = Rng.Bytes(NonceSize); byte[] c, t; EncryptRaw(key, nonce, plain, aad, out c, out t);
            var o = new byte[NonceSize + c.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, o, 0, NonceSize); Buffer.BlockCopy(c, 0, o, NonceSize, c.Length); Buffer.BlockCopy(t, 0, o, NonceSize + c.Length, TagSize);
            return o;
        }
        public static byte[] Open(byte[] key, byte[] sealedData, byte[] aad)
        {
            if (sealedData == null || sealedData.Length < NonceSize + TagSize) throw new AuthFailedException();
            var nonce = new byte[NonceSize]; var c = new byte[sealedData.Length - NonceSize - TagSize]; var t = new byte[TagSize];
            Buffer.BlockCopy(sealedData, 0, nonce, 0, NonceSize); Buffer.BlockCopy(sealedData, NonceSize, c, 0, c.Length); Buffer.BlockCopy(sealedData, NonceSize + c.Length, t, 0, TagSize);
            return DecryptRaw(key, nonce, c, t, aad);
        }

        public static void Wipe(byte[] b) { if (b != null) Array.Clear(b, 0, b.Length); }
    }

    public static class Kdf
    {
        public const int MinIterations = 600000, SaltSize = 16;
        // PBKDF2-HMAC-SHA256 -> 32-byte key. The password bytes are the caller's to wipe.
        public static byte[] Derive(byte[] password, byte[] salt, int iterations)
        {
            if (iterations < MinIterations) throw new CryptographicException("Too few key derivation rounds");
            if (salt == null || salt.Length < SaltSize) throw new CryptographicException("Salt too short");
            using (var k = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256)) return k.GetBytes(32);
        }
    }

    public static class Rng
    {
        static readonly RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
        public static byte[] Bytes(int n) { var b = new byte[n]; lock (rng) rng.GetBytes(b); return b; }
        // a uniform number in [0, n) - rejection sampling, so every value is equally likely
        public static int Below(int n)
        {
            if (n <= 0 || n > 1 << 24) throw new ArgumentOutOfRangeException("n");
            uint limit = uint.MaxValue - (uint)((((ulong)uint.MaxValue) + 1) % (uint)n);   // largest multiple of n, minus one
            var b = new byte[4];
            while (true)
            {
                lock (rng) rng.GetBytes(b);
                uint v = BitConverter.ToUInt32(b, 0);
                if (v <= limit) return (int)(v % (uint)n);
            }
        }
    }
}
