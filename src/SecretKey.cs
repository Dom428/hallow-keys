// Hallow Keys 2.0 - the Secret Key (like 1Password's): 128 random bits made when the vault is created (or upgraded), kept on each
// PC with DPAPI (this Windows account only) and printed on the Emergency Kit. The vault's key-encryption key is
// HKDF-SHA256(Argon2id(master password) || Secret Key), so a copy of the vault file alone - from a cloud folder, a stolen
// laptop backup - can't be attacked by guessing master passwords: the attacker would also have to guess 128 random bits.
// Written as "HK-" and 30 characters in five groups of six: 26 carry the key (base32, no 0/O/1/I), the last 4 are a checksum,
// so a typo is caught before anything is tried.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HallowKeys
{
    public static class SecretKey
    {
        public const int Size = 16;
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public static byte[] New() { return Rng.Bytes(Size); }

        // 8-byte id written in the vault header: says which Secret Key the vault wants (the key itself can't be worked out from it)
        public static byte[] Id(byte[] key)
        {
            using (var sha = SHA256.Create()) { var h = sha.ComputeHash(Encoding.ASCII.GetBytes("HallowKeys-SecretKey-Id").Concat(key).ToArray()); var o = new byte[8]; Buffer.BlockCopy(h, 0, o, 0, 8); Array.Clear(h, 0, h.Length); return o; }
        }
        static int[] Checksum(byte[] key)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.ASCII.GetBytes("HallowKeys-SecretKey-Check").Concat(key).ToArray());
                int v = (h[0] << 12) | (h[1] << 4) | (h[2] >> 4);   // 20 bits
                return new[] { (v >> 15) & 31, (v >> 10) & 31, (v >> 5) & 31, v & 31 };
            }
        }
        public static string Format(byte[] key)
        {
            if (key == null || key.Length != Size) throw new ArgumentException("key");
            var sb = new StringBuilder(); int buf = 0, bits = 0;
            foreach (var x in key) { buf = (buf << 8) | x; bits += 8; while (bits >= 5) { sb.Append(Alphabet[(buf >> (bits - 5)) & 31]); bits -= 5; } buf &= (1 << bits) - 1; }
            sb.Append(Alphabet[(buf << (5 - bits)) & 31]);   // 128 = 25 * 5 + 3: the 26th character has 2 zero bits
            foreach (var c in Checksum(key)) sb.Append(Alphabet[c]);
            var s = sb.ToString();
            return "HK-" + string.Join("-", Enumerable.Range(0, 5).Select(i => s.Substring(i * 6, 6)));
        }
        // what was typed or pasted -> the key, or null with the reason (case, spaces and dashes don't matter; the "HK" in front is optional)
        public static byte[] Parse(string text, out string error)
        {
            error = null;
            var s = new string((text ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (s.Length == 32 && s.StartsWith("HK")) s = s.Substring(2);
            if (s.Length == 0) { error = "Type or paste your Secret Key (it starts with HK-)."; return null; }
            foreach (var c in s) if (Alphabet.IndexOf(c) < 0) { error = "\"" + c + "\" isn't in a Secret Key (they never use 0, O, 1 or I) - check that character."; return null; }
            if (s.Length != 30) { error = "A Secret Key has 30 characters after HK- (this has " + s.Length + ")."; return null; }
            var key = new byte[Size]; int buf = 0, bits = 0, p = 0;
            for (int i = 0; i < 26; i++)
            {
                buf = (buf << 5) | Alphabet.IndexOf(s[i]); bits += 5;
                if (bits >= 8) { if (p < Size) key[p++] = (byte)(buf >> (bits - 8)); bits -= 8; buf &= (1 << bits) - 1; }
            }
            if (buf != 0) { error = "That isn't a valid Secret Key - check it for typos."; return null; }
            var chk = Checksum(key);
            for (int i = 0; i < 4; i++) if (Alphabet[chk[i]] != s[26 + i]) { Array.Clear(key, 0, Size); error = "That Secret Key has a typo somewhere (its check letters don't match) - compare it with your Emergency Kit."; return null; }
            return key;
        }
        public static bool LooksValid(string text) { string e; var k = Parse(text, out e); if (k == null) return false; Array.Clear(k, 0, k.Length); return true; }

        // ---- kept on this PC: secret-key-<vault id>.bin in the data folder, DPAPI (CurrentUser) with its own entropy
        static readonly byte[] Entropy = Encoding.ASCII.GetBytes("HallowKeys-SecretKey-v1");
        static string FileFor(byte[] vaultId) { return Path.Combine(KeysSettings.Dir, "secret-key-" + BitConverter.ToString(vaultId, 0, 8).Replace("-", "").ToLowerInvariant() + ".bin"); }
        public static void Store(byte[] vaultId, byte[] key)
        {
            Directory.CreateDirectory(KeysSettings.Dir);
            var blob = new byte[Size + 16]; Buffer.BlockCopy(vaultId, 0, blob, 0, 16); Buffer.BlockCopy(key, 0, blob, 16, Size);
            try { Vault.WriteAtomic(FileFor(vaultId), ProtectedData.Protect(blob, Entropy, DataProtectionScope.CurrentUser), false); }
            finally { Array.Clear(blob, 0, blob.Length); }
        }
        // the key kept for this vault, if its id matches (null when this PC doesn't have it)
        public static byte[] Load(byte[] vaultId, byte[] wantedId)
        {
            try
            {
                var f = FileFor(vaultId); if (!File.Exists(f)) return null;
                var blob = ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    if (blob.Length != Size + 16 || !blob.Take(16).SequenceEqual(vaultId)) return null;
                    var key = blob.Skip(16).Take(Size).ToArray();
                    if (wantedId != null && !Id(key).SequenceEqual(wantedId)) { Array.Clear(key, 0, key.Length); return null; }
                    return key;
                }
                finally { Array.Clear(blob, 0, blob.Length); }
            }
            catch { return null; }
        }
        public static bool Has(byte[] vaultId, byte[] wantedId) { var k = Load(vaultId, wantedId); if (k == null) return false; Array.Clear(k, 0, k.Length); return true; }
    }

    // ---- this PC in sync: a random id (never the user's name) and the computer's name for "conflict copy from DESKTOP-ABC"
    public static class Device
    {
        static string id;
        static string FilePath { get { return Path.Combine(KeysSettings.Dir, "device-id.txt"); } }
        public static string Id
        {
            get
            {
                if (id != null && KeysSettings.TestDir == null) return id;
                try { if (File.Exists(FilePath)) { var t = File.ReadAllText(FilePath).Trim(); if (t.Length == 32 && t.All(Uri.IsHexDigit)) return id = t; } } catch { }
                var n = Guid.NewGuid().ToString("N");
                try { Directory.CreateDirectory(KeysSettings.Dir); File.WriteAllText(FilePath, n); } catch { }
                return id = n;
            }
        }
        public static string Name
        {
            get
            {
                var t = KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_DEVICE") : null;
                return string.IsNullOrEmpty(t) ? Environment.MachineName : t;
            }
        }
    }
}
