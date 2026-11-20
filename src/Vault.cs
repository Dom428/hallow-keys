// Hallow Keys - the vault file and what's in it.
// File format v1 (all integers little-endian):
//   "HKVAULT\0" | u16 version=1 | u8 kdf=1 (PBKDF2-HMAC-SHA256) | u32 iterations | u8 saltLen | salt
//   | master wrap: nonce(12) ct(32) tag(16)                    - the data key, AES-256-GCM under the master key
//   | u8 hasRecovery | [u32 iterations | u8 saltLen | salt | nonce(12) ct(32) tag(16)]   - the data key under the recovery code
//   | data nonce(12) | u32 cipherLength                         = header (all of it is the AAD of the data)
//   | ciphertext | tag(16)                                     - the items (JSON), AES-256-GCM under the data key
// v1.3 adds password history dates, tags and a trash to the items JSON - all inside the encrypted part; the file format is unchanged.
// v1.6 writes format 2 (the same layout): 1.6 items have fields that older versions would drop when they save, so Hallow Keys 1.5 and
// older refuse a format-2 file ("made by a newer Hallow Keys") instead of opening it and losing data. Format 1 files open and are saved
// as format 2. Every class keeps members it doesn't know (IExtensibleDataObject), so a newer version's extra data survives a save.
// A new random nonce for every save. Saves are atomic (temp file, then replace) and keep the previous good file as .bak.
// v2.0 writes format 3 (see Vault below): Argon2id or PBKDF2 for the master password, the Secret Key mixed in with HKDF-SHA256,
// a vault id, and per-item sync data (revisions, tombstones) inside the encrypted part. 1.7 refuses format 3 ("made by a newer
// Hallow Keys"); 2.0 opens formats 1-3, and the first save of an older file keeps it as vault.hkvault.format2.bak (or format1).
// v2.1 writes format 4 (the same layout as 3): items belong to a vault (Item.VaultId), the vault list (VaultData.Vaults: names, travel-safe)
// and when each login was last used (VaultData.Usage) are new, inside the encrypted part. 2.0 would keep them when it saves but its sync
// would drop other PCs' vault lists and last-used times, so 2.0 refuses format 4 (its sync waits: "saved by a newer Hallow Keys") instead.
// The first save of a 2.0 file keeps it as vault.hkvault.format3.bak.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HallowKeys
{
    [DataContract] public class Attachment : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Name = "";
        [DataMember] public byte[] Data = new byte[0];
    }
    // v1.6: one field of an item - a built-in field of the item's type (Key set, e.g. "privateKey") or one of your own (Key "")
    [DataContract] public class CustomField : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Key = "";
        [DataMember] public string Label = "";
        [DataMember] public string Kind = "text";      // text, hidden, url, date, totp, email, phone; built-in fields also: note, secretnote
        [DataMember] public string Value = "";
        [DataMember] public bool Expires;               // date fields: an expiry date (reminders, the Expiring view)
        [DataMember] public List<string> Old = new List<string>(), OldDates = new List<string>();   // hidden fields: previous values (newest first, at most 10)
        [OnDeserialized] void Fix(StreamingContext c)
        {
            if (Id == null) Id = Guid.NewGuid().ToString("N"); if (Key == null) Key = ""; if (Label == null) Label = ""; if (string.IsNullOrEmpty(Kind)) Kind = "text"; if (Value == null) Value = "";
            if (Old == null) Old = new List<string>(); if (OldDates == null) OldDates = new List<string>();
        }
    }
    // v1.6: your own item template - a type and a field layout, never any values
    [DataContract] public class ItemTemplate : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "";
        [DataMember] public string Type = "login";
        [DataMember] public List<CustomField> Fields = new List<CustomField>();
        [OnDeserialized] void Fix(StreamingContext c) { if (Id == null) Id = Guid.NewGuid().ToString("N"); if (Name == null) Name = ""; if (string.IsNullOrEmpty(Type)) Type = "login"; if (Fields == null) Fields = new List<CustomField>(); }
    }
    [DataContract] public class Item : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Type = "login";   // login, note, card, identity
        [DataMember] public string Title = "";
        [DataMember] public string Folder = "";
        [DataMember] public bool Favorite;
        [DataMember] public DateTime Created = DateTime.Now, Updated = DateTime.Now, PasswordChanged = DateTime.Now;
        // login
        [DataMember] public string Username = "", Password = "", Totp = "";
        [DataMember] public List<string> Urls = new List<string>();
        // card
        [DataMember] public string CardHolder = "", CardNumber = "", Expiry = "", Cvc = "", Pin = "";
        // identity
        [DataMember] public string FullName = "", Email = "", Phone = "", Address = "", City = "", PostCode = "", Country = "";
        [DataMember] public string Notes = "";
        [DataMember] public List<Attachment> Files = new List<Attachment>();
        [DataMember] public List<string> OldPasswords = new List<string>();   // previous passwords (for "reused" and history)
        [DataMember] public List<string> OldPasswordDates = new List<string>();   // v1.3: when each old password was replaced (ISO time, same order; "" = not known)
        [DataMember] public List<string> Tags = new List<string>();               // v1.3
        [DataMember] public DateTime? Deleted;                                   // v1.3: when it went to the trash (null = not in the trash)
        [DataMember] public List<string> Apps = new List<string>();              // v1.5: desktop programs this login types into (exe names, e.g. steam.exe)
        [DataMember] public List<CustomField> Fields = new List<CustomField>();  // v1.6: the new types' fields and your own fields on any item
        [DataMember] public bool SeedOnPhone;                                    // v1.6: crypto wallet - put the recovery phrase in the phone copy (off unless switched on)
        [DataMember(EmitDefaultValue = false)] public SyncMeta Sync;           // v2.0: this item's revision for sync (see Sync20.cs) - not part of its content
        [DataMember(EmitDefaultValue = false)] public string VaultId;          // v2.1: the vault it belongs to (null = Personal; see Vaults21.cs). Part of its content: a move is an edit
        [OnDeserialized] void Fix(StreamingContext c)
        {
            if (Urls == null) Urls = new List<string>(); if (Files == null) Files = new List<Attachment>(); if (OldPasswords == null) OldPasswords = new List<string>();
            if (OldPasswordDates == null) OldPasswordDates = new List<string>(); if (Tags == null) Tags = new List<string>(); if (Apps == null) Apps = new List<string>(); if (Fields == null) Fields = new List<CustomField>();
            foreach (var f in new[] { "Username", "Password", "Totp", "CardHolder", "CardNumber", "Expiry", "Cvc", "Pin", "FullName", "Email", "Phone", "Address", "City", "PostCode", "Country", "Notes", "Title", "Folder", "Type" })
            { var p = GetType().GetField(f); if (p.GetValue(this) == null) p.SetValue(this, f == "Type" ? "login" : ""); }
        }
        public string Host { get { return Urls.Select(Domain.HostOf).FirstOrDefault(h => h != null); } }
    }
    [DataContract] public class VaultData : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public List<Item> Items = new List<Item>();
        [DataMember] public List<string> Folders = new List<string>();
        [DataMember] public List<string> LeakedIds = new List<string>();   // v1.2: logins whose password was found in a breach
        [DataMember] public byte[] PhoneSalt, PhoneKey; [DataMember] public int PhoneIterations;   // v1.2: the phone copy's key (only inside the encrypted vault)
        [DataMember] public List<Item> Trash = new List<Item>();           // v1.3: deleted items, kept (encrypted, in here) for 30 days
        [DataMember] public List<ItemTemplate> Templates = new List<ItemTemplate>();   // v1.6: your own item templates (layouts, no values)
        [DataMember] public List<string> ExpiryNotified = new List<string>();          // v1.6: expiry reminders already shown (item|field|date|stage)
        [DataMember] public List<Tombstone> Tombstones = new List<Tombstone>();        // v2.0: items deleted for good (so another PC deletes them too, not brings them back)
        [DataMember(EmitDefaultValue = false)] public byte[] AccountKey;               // v2.0: the Secret Key (only ever inside the encrypted vault), so a PC that's unlocked learns a new one
        [DataMember(EmitDefaultValue = false)] public List<VaultInfo> Vaults;          // v2.1: your vaults (Personal, Gaming, Work...): name, colour, safe for travel; deleted ones stay as markers
        [DataMember(EmitDefaultValue = false)] public List<UsageMark> Usage;
        // v2.4: your member identity (ECDH P-256 - the private half only ever inside the encrypted vault) and the shared vaults you're in
        [DataMember(EmitDefaultValue = false)] public byte[] MemberKey, MemberPub;
        [DataMember(EmitDefaultValue = false)] public string MemberName;
        [DataMember(EmitDefaultValue = false)] public List<SharedRef> Shared;           // v2.1: when each item was last filled, copied or opened (merged by the latest time, never part of an item's revision)
        [OnDeserialized] void Fix(StreamingContext c) { if (Tombstones == null) Tombstones = new List<Tombstone>(); if (Items == null) Items = new List<Item>(); if (Folders == null) Folders = new List<string>(); if (LeakedIds == null) LeakedIds = new List<string>(); if (Trash == null) Trash = new List<Item>(); if (Templates == null) Templates = new List<ItemTemplate>(); if (ExpiryNotified == null) ExpiryNotified = new List<string>(); }
    }

    public class VaultException : Exception { public VaultException(string m) : base(m) { } }
    // v2.0: the vault wants a Secret Key this PC doesn't have (type it from the Emergency Kit)
    public class SecretKeyNeededException : VaultException { public SecretKeyNeededException(string m) : base(m) { } }

    // an open vault: the data key (in memory only while unlocked) and the header blocks that don't change between saves.
    // File format 3 (v2.0; all integers little-endian):
    //   "HKVAULT\0" | u16 version=3 | u8 kdf (1 = PBKDF2-HMAC-SHA256: u32 iterations | 2 = Argon2id: u32 memory KiB, u32 passes, u32 lanes)
    //   | u8 saltLen | salt | u8 flags (1 = Secret Key mixed in, 2 = master wrap from format 1/2, 4 = recovery wrap from format 1/2)
    //   | vault id (16) | Secret Key id (8, zeros without one) | i64 master time | i64 recovery time (UTC ticks: which PC's change is newest)
    //   | master wrap: nonce(12) ct(32) tag(16) | u8 hasRecovery | [u32 iterations | u8 saltLen | salt | nonce(12) ct(32) tag(16)]
    //   | data nonce(12) | u32 cipherLength = header (all of it is the AAD of the data) | ciphertext | tag(16)
    // Key-encryption key: K = Argon2id(password, salt) (or PBKDF2); with the Secret Key, KEK = HKDF-SHA256(K || Secret Key, salt = vault id,
    // info = "HallowKeys 2SKD v1"). The master wrap's AAD binds the vault id, the KDF and its parameters, the flags and the Secret Key id.
    // The recovery code (160 random bits, PBKDF2) wraps the same data key on its own - it opens the vault without the Secret Key.
    public partial class Vault : IDisposable
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKVAULT\0");
        public const ushort Version = 4, OldestVersion = 1;   // v2.1 writes 4, reads 1 to 4 (v2.0 wrote 3)
        public const byte KdfPbkdf2 = 1, KdfArgon2id = 2;
        const byte FlagSecretKey = 1, FlagLegacyMaster = 2, FlagLegacyRecovery = 4;
        // Argon2id for new vaults and upgrades: 256 MiB, 4 passes, 4 lanes - about half a second on the PC this was tuned on
        public static int ArgonMemoryKiB = 262144, ArgonPasses = 4, ArgonLanes = 4;
        public const int MaxArgonMemoryKiB = 1048576, MaxArgonPasses = 64, MaxArgonLanes = 16;
        public const long MaxFileBytes = 10L * 1024 * 1024;
        public string Path;
        public VaultData Data = new VaultData();
        byte[] dataKey;
        byte kdf = KdfPbkdf2; int iterations, argM, argT, argP; byte[] salt; byte[] masterWrap;     // master: kdf params + nonce|ct|tag
        int recIterations; byte[] recSalt; byte[] recWrap;                                          // recovery (optional)
        byte flags; byte[] vaultId = new byte[16]; byte[] skId = new byte[8]; long masterTime, recTime;
        public byte[] LastNonce;                                    // for tests: the data nonce of the last save
        public bool HasRecovery { get { return recWrap != null; } }
        public bool UsesSecretKey { get { return (flags & FlagSecretKey) != 0; } }
        public bool UsesArgon2 { get { return kdf == KdfArgon2id; } }
        public bool NeedsUpgrade { get { return !UsesArgon2 || !UsesSecretKey; } }       // older protection: offer the upgrade
        public byte[] VaultId { get { return (byte[])vaultId.Clone(); } }
        public byte[] SecretKeyId { get { return UsesSecretKey ? (byte[])skId.Clone() : null; } }
        public string KdfDescription { get { return kdf == KdfArgon2id ? "Argon2id (" + (argM / 1024) + " MiB, " + argT + " passes, " + argP + " lanes)" : "PBKDF2-SHA256 (" + iterations.ToString("N0") + " rounds)"; } }
        public bool KeepVersions = true;                            // the main vault keeps encrypted snapshots in versions\ (Restore an older version)
        public bool Tracking = true;                                // sync revisions are kept up to date on every save (not for exports, which share the items)

        static byte[] AadWrapLegacy(char which) { return Encoding.ASCII.GetBytes("HKWRAP1" + which); }
        byte[] KdfParamBytes() { var m = new MemoryStream(); m.WriteByte(kdf); if (kdf == KdfArgon2id) { W(m, argM); W(m, argT); W(m, argP); } else W(m, iterations); return m.ToArray(); }
        static void W(Stream s, int v) { var b = BitConverter.GetBytes(v); s.Write(b, 0, 4); }
        byte[] AadMaster()
        {
            if ((flags & FlagLegacyMaster) != 0) return AadWrapLegacy('M');
            var m = new MemoryStream(); var t = Encoding.ASCII.GetBytes("HKWRAP3M"); m.Write(t, 0, t.Length); m.Write(vaultId, 0, 16);
            var k = KdfParamBytes(); m.Write(k, 0, k.Length); m.WriteByte((byte)(flags & FlagSecretKey)); m.Write(skId, 0, 8); return m.ToArray();
        }
        byte[] AadRecovery()
        {
            if ((flags & FlagLegacyRecovery) != 0) return AadWrapLegacy('R');
            return Encoding.ASCII.GetBytes("HKWRAP3R").Concat(vaultId).ToArray();
        }

        // ---- new vault: random data key, wrapped by the master password + a new Secret Key (Argon2id), and a recovery code (returned once)
        public static Vault Create(string path, byte[] password, out string recoveryCode)
        {
            byte[] sk; var v = Create(path, password, out recoveryCode, out sk);
            SecretKey.Store(v.vaultId, sk); Gcm.Wipe(sk);
            return v;
        }
        // the same, handing back the new Secret Key (for the Emergency Kit; the caller wipes it) - it's also kept on this PC by the caller
        public static Vault Create(string path, byte[] password, out string recoveryCode, out byte[] secretKey)
        {
            var v = new Vault { Path = path, dataKey = Rng.Bytes(32), vaultId = Rng.Bytes(16) };
            secretKey = SecretKey.New();
            v.SetMaster(password, secretKey, KdfArgon2id);
            v.Data.AccountKey = (byte[])secretKey.Clone();
            recoveryCode = v.NewRecoveryCode();
            return v;
        }
        // derives the key-encryption key for the master wrap
        byte[] MasterKek(byte[] password, byte[] sk)
        {
            byte[] k;
            if (kdf == KdfArgon2id)
            {
                if (argM < 8 * argP || argM > MaxArgonMemoryKiB || argT < 1 || argT > MaxArgonPasses || argP < 1 || argP > MaxArgonLanes || salt == null || salt.Length < Kdf.SaltSize) throw new VaultException(Damaged);
                k = Argon2.Id(password, salt, argM, argT, argP, 32);
            }
            else
            {
                if (iterations < Kdf.MinIterations || iterations > 50000000 || salt == null || salt.Length < Kdf.SaltSize) throw new VaultException(Damaged);
                k = Kdf.Derive(password, salt, iterations);
            }
            if ((flags & FlagSecretKey) == 0) return k;
            if (sk == null || sk.Length != SecretKey.Size) throw new SecretKeyNeededException("This vault needs its Secret Key, and this PC doesn't have it - type it from your Emergency Kit.");
            var ikm = new byte[32 + SecretKey.Size]; Buffer.BlockCopy(k, 0, ikm, 0, 32); Buffer.BlockCopy(sk, 0, ikm, 32, SecretKey.Size);
            try { return Hkdf.Derive(ikm, vaultId, Encoding.ASCII.GetBytes("HallowKeys 2SKD v1"), 32); }
            finally { Gcm.Wipe(ikm); Gcm.Wipe(k); }
        }
        // a new master wrap: kdfType KdfArgon2id (new vaults, upgrades) or KdfPbkdf2 (exports keep it simple to open anywhere: Argon2id too, see WriteExport)
        void SetMaster(byte[] password, byte[] sk, byte kdfType)
        {
            kdf = kdfType; salt = Rng.Bytes(Kdf.SaltSize);
            if (kdf == KdfArgon2id) { argM = ArgonMemoryKiB; argT = ArgonPasses; argP = ArgonLanes; } else iterations = Kdf.MinIterations;
            flags = (byte)(flags & ~(FlagSecretKey | FlagLegacyMaster));
            if (sk != null) { flags |= FlagSecretKey; skId = SecretKey.Id(sk); } else skId = new byte[8];
            var kek = MasterKek(password, sk);
            try { masterWrap = Gcm.Seal(kek, dataKey, AadMaster()); masterTime = DateTime.UtcNow.Ticks; } finally { Gcm.Wipe(kek); }
        }
        // a new recovery code (20 random bytes, base32 in groups): a second wrapper of the data key, shown once
        public string NewRecoveryCode()
        {
            var raw = Rng.Bytes(20); var code = Base32(raw); Gcm.Wipe(raw);
            var pretty = string.Join("-", Enumerable.Range(0, 8).Select(i => code.Substring(i * 4, 4)));
            var bytes = RecoveryBytes(pretty);
            try
            {
                var s = Rng.Bytes(Kdf.SaltSize); var rk = Kdf.Derive(bytes, s, Kdf.MinIterations);
                try { flags = (byte)(flags & ~FlagLegacyRecovery); recWrap = Gcm.Seal(rk, dataKey, AadRecovery()); recSalt = s; recIterations = Kdf.MinIterations; recTime = DateTime.UtcNow.Ticks; } finally { Gcm.Wipe(rk); }
            }
            finally { Gcm.Wipe(bytes); }
            return pretty;
        }
        // the code as typed: case and dashes / spaces don't matter
        public static byte[] RecoveryBytes(string code) { return Encoding.ASCII.GetBytes(new string((code ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray())); }
        static string Base32(byte[] b)
        {
            const string a = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I
            var sb = new StringBuilder(); int buf = 0, bits = 0;
            foreach (var x in b) { buf = (buf << 8) | x; bits += 8; while (bits >= 5) { sb.Append(a[(buf >> (bits - 5)) & 31]); bits -= 5; } }
            if (bits > 0) sb.Append(a[(buf << (5 - bits)) & 31]);
            return sb.ToString();
        }

        // re-wraps the same data key with the new password (Argon2id, and the Secret Key this vault uses - kept from this PC)
        public void ChangePassword(byte[] newPassword)
        {
            var sk = CurrentSecretKey();
            if (UsesSecretKey && sk == null) throw new SecretKeyNeededException("This PC doesn't have this vault's Secret Key, so the password can't be changed here - unlock with the master password and Secret Key first.");
            try { SetMaster(newPassword, sk, KdfArgon2id); } finally { Gcm.Wipe(sk); }
            Save();
        }
        // v2.0: the upgrade for a 1.x vault (or a 2.0 vault without a Secret Key): Argon2id + a new Secret Key. Returns the Secret Key (caller wipes).
        // The password must already have been checked. The new key is kept on this PC and inside the vault.
        public byte[] UpgradeProtection(byte[] password)
        {
            var sk = CurrentSecretKey() ?? SecretKey.New();
            SetMaster(password, sk, KdfArgon2id);
            Data.AccountKey = (byte[])sk.Clone(); SecretKey.Store(vaultId, sk);
            Save();
            return sk;
        }
        // after the recovery code: a new master password. Keeps this PC's Secret Key if it has the right one, otherwise makes a new one
        // (returned so it can be printed; null when the old one was kept)
        public byte[] SetPasswordAfterRecovery(byte[] newPassword)
        {
            var sk = CurrentSecretKey(); byte[] fresh = null;
            if (sk == null) { sk = SecretKey.New(); fresh = (byte[])sk.Clone(); }
            try { SetMaster(newPassword, sk, KdfArgon2id); Data.AccountKey = (byte[])sk.Clone(); SecretKey.Store(vaultId, sk); } finally { Gcm.Wipe(sk); }
            Save();
            return fresh;
        }
        // the Secret Key this vault uses, from this PC (or from inside the vault when it matches) - null when it has none
        public byte[] CurrentSecretKey()
        {
            if (!UsesSecretKey) return null;
            var k = SecretKey.Load(vaultId, skId);
            if (k == null && Data.AccountKey != null && Data.AccountKey.Length == SecretKey.Size && SecretKey.Id(Data.AccountKey).SequenceEqual(skId)) { k = (byte[])Data.AccountKey.Clone(); SecretKey.Store(vaultId, k); }
            return k;
        }

        // v1.3: an encrypted export with its own password - the same file format (its own random data key, vault id and salt, no recovery code,
        // no Secret Key: only the password you pick opens it), so Restore backup opens it. Only items and folders go in.
        public static void WriteExport(string path, byte[] password, IEnumerable<Item> items, IEnumerable<string> folders) { WriteExport(path, password, items, folders, null); }
        public static void WriteExport(string path, byte[] password, IEnumerable<Item> items, IEnumerable<string> folders, IEnumerable<ItemTemplate> templates) { WriteExport(path, password, items, folders, templates, null); }
        // v2.1: with the vaults the items are in (names and settings), so Restore backup puts them back in the same vaults
        public static void WriteExport(string path, byte[] password, IEnumerable<Item> items, IEnumerable<string> folders, IEnumerable<ItemTemplate> templates, IEnumerable<VaultInfo> vaults)
        {
            var v = new Vault { Path = path, dataKey = Rng.Bytes(32), vaultId = Rng.Bytes(16), KeepVersions = false, Tracking = false };
            v.Data = new VaultData { Items = items.ToList(), Folders = folders.ToList(), Templates = templates == null ? new List<ItemTemplate>() : templates.ToList(), Vaults = vaults == null ? null : vaults.Select(x => x.Clone()).ToList() };
            try { v.SetMaster(password, null, KdfArgon2id); WriteAtomic(path, v.Serialize(), false); }
            finally { Gcm.Wipe(v.dataKey); v.dataKey = null; v.Data = new VaultData(); }
        }

        // ---- open
        public static Vault Open(string path, byte[] password) { return OpenBytes(path, ReadFile(path), password, false, null, null); }
        // v2.0: with a Secret Key typed on this PC (adding a device); null = the one kept on this PC
        public static Vault Open(string path, byte[] password, byte[] secretKey) { return OpenBytes(path, ReadFile(path), password, false, null, secretKey); }
        public static Vault OpenWithRecovery(string path, string code) { var b = RecoveryBytes(code); try { return OpenBytes(path, ReadFile(path), b, true, null, null); } finally { Gcm.Wipe(b); } }
        // with the data key itself (Windows Hello; sync reading another copy of the same vault; tests)
        public static Vault OpenWithDataKey(string path, byte[] dataKey) { return OpenBytes(path, ReadFile(path), null, false, dataKey, null); }
        public static byte[] ReadFile(string path)
        {
            if (!File.Exists(path)) throw new VaultException("There's no vault file at " + path);
            byte[] b = null;
            for (int i = 0; ; i++)
            {
                try { using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { if (f.Length > 64L * 1024 * 1024 * 4) throw new VaultException("The vault file is too big to be a Hallow Keys vault."); b = new byte[f.Length]; int got = 0; while (got < b.Length) { int n = f.Read(b, got, b.Length - got); if (n <= 0) break; got += n; } if (got != b.Length) Array.Resize(ref b, got); } break; }
                catch (IOException) { if (i >= 4) throw; System.Threading.Thread.Sleep(150 * (i + 1)); }
            }
            return b;
        }
        public ushort FileVersion = Version;   // the format of the file when it was opened (1 = made by 1.5 or older, 2 = 1.6 / 1.7; saved as 3 next time)
        const string Damaged = "The vault file is damaged or was changed - it can't be opened. Your last good copy is the .bak file next to it.";
        public static Vault OpenBytes(string path, byte[] file, byte[] secret, bool recovery, byte[] givenKey) { return OpenBytes(path, file, secret, recovery, givenKey, null); }
        // reads only the header (no key needed): the vault id, whether it needs a Secret Key and which one. null if it isn't a vault this version reads.
        public static Vault PeekHeader(byte[] file) { try { int he; byte[] dn; int cl; return ParseHeader(null, file, out he, out dn, out cl); } catch { return null; } }
        static Vault ParseHeader(string path, byte[] file, out int headerEnd, out byte[] dataNonce, out int clen)
        {
            var v = new Vault { Path = path }; int p = 0;
            try
            {
                Func<int, byte[]> take = n => { if (n < 0 || p + n > file.Length) throw new VaultException("bad"); var r = new byte[n]; Buffer.BlockCopy(file, p, r, 0, n); p += n; return r; };
                if (file.Length < 8 || !take(8).SequenceEqual(Magic)) throw new VaultException("This isn't a Hallow Keys vault.");
                var ver = BitConverter.ToUInt16(take(2), 0); v.FileVersion = ver; if (ver < OldestVersion || ver > Version) throw new VaultException("This vault was made by a newer Hallow Keys (format " + ver + ") - update Hallow Keys on this PC to open it.");
                v.kdf = take(1)[0];
                if (v.kdf == KdfPbkdf2) v.iterations = BitConverter.ToInt32(take(4), 0);
                else if (v.kdf == KdfArgon2id && ver >= 3) { v.argM = BitConverter.ToInt32(take(4), 0); v.argT = BitConverter.ToInt32(take(4), 0); v.argP = BitConverter.ToInt32(take(4), 0); }
                else throw new VaultException("Unknown key derivation in the vault header.");
                int sl = take(1)[0]; v.salt = take(sl);
                if (ver >= 3)
                {
                    v.flags = take(1)[0]; if ((v.flags & ~7) != 0) throw new VaultException("bad");
                    v.vaultId = take(16); v.skId = take(8); v.masterTime = BitConverter.ToInt64(take(8), 0); v.recTime = BitConverter.ToInt64(take(8), 0);
                }
                else v.flags = FlagLegacyMaster | FlagLegacyRecovery;   // formats 1 and 2: their wraps keep their AAD until re-made; a vault id is given on the first save
                v.masterWrap = take(Gcm.NonceSize + 32 + Gcm.TagSize);
                var hasRec = take(1)[0];
                if (hasRec == 1) { v.recIterations = BitConverter.ToInt32(take(4), 0); int rl = take(1)[0]; v.recSalt = take(rl); v.recWrap = take(Gcm.NonceSize + 32 + Gcm.TagSize); }
                else if (hasRec != 0) throw new VaultException("bad");
                dataNonce = take(Gcm.NonceSize); clen = BitConverter.ToInt32(take(4), 0); headerEnd = p;
                if (clen < 0 || (long)headerEnd + clen + Gcm.TagSize != file.Length) throw new VaultException("bad");
            }
            catch (VaultException e) { if (e.Message == "bad") throw new VaultException(Damaged); throw; }
            return v;
        }
        public static Vault OpenBytes(string path, byte[] file, byte[] secret, bool recovery, byte[] givenKey, byte[] secretKey)
        {
            int headerEnd; byte[] dataNonce; int clen;
            var v = ParseHeader(path, file, out headerEnd, out dataNonce, out clen);
            // the data key
            if (givenKey != null) v.dataKey = (byte[])givenKey.Clone();
            else if (recovery)
            {
                if (v.recWrap == null) throw new VaultException("This vault has no recovery code.");
                if (v.recIterations < Kdf.MinIterations || v.recIterations > 50000000 || v.recSalt == null || v.recSalt.Length < Kdf.SaltSize) throw new VaultException(Damaged);
                var k = Kdf.Derive(secret, v.recSalt, v.recIterations);
                try { v.dataKey = Gcm.Open(k, v.recWrap, v.AadRecovery()); }
                catch (AuthFailedException) { throw new VaultException("That recovery code doesn't open this vault."); }
                finally { Gcm.Wipe(k); }
            }
            else
            {
                byte[] sk = null; bool typed = secretKey != null;
                if (v.UsesSecretKey)
                {
                    sk = typed ? (byte[])secretKey.Clone() : SecretKey.Load(v.vaultId, v.skId);
                    if (sk == null) throw new SecretKeyNeededException("This vault needs its Secret Key, and this PC doesn't have it - type it from your Emergency Kit.");
                    if (!SecretKey.Id(sk).SequenceEqual(v.skId)) { Gcm.Wipe(sk); throw new SecretKeyNeededException(typed ? "That Secret Key isn't the one for this vault - check your Emergency Kit (a newer kit replaces older ones)." : "This vault now uses a different Secret Key (it was changed on another PC) - type the new one from your Emergency Kit."); }
                }
                try
                {
                    var k = v.MasterKek(secret, sk);
                    try { v.dataKey = Gcm.Open(k, v.masterWrap, v.AadMaster()); }
                    catch (AuthFailedException) { throw new VaultException("Wrong master password (or the vault file was changed)."); }
                    finally { Gcm.Wipe(k); }
                }
                finally { Gcm.Wipe(sk); }
            }
            // the items, with the whole header as AAD
            var header = new byte[headerEnd]; Buffer.BlockCopy(file, 0, header, 0, headerEnd);
            var ct = new byte[clen]; Buffer.BlockCopy(file, headerEnd, ct, 0, clen);
            var tag = new byte[Gcm.TagSize]; Buffer.BlockCopy(file, headerEnd + clen, tag, 0, Gcm.TagSize);
            byte[] plain;
            try { plain = Gcm.DecryptRaw(v.dataKey, dataNonce, ct, tag, header); }
            catch (AuthFailedException) { v.Dispose(); throw new VaultException(givenKey != null ? "This vault doesn't match the saved Windows Hello key - unlock with your master password." : Damaged); }
            try { using (var ms = new MemoryStream(plain)) v.Data = (VaultData)new DataContractJsonSerializer(typeof(VaultData)).ReadObject(ms); }
            catch { v.Dispose(); throw new VaultException("The vault opened but its contents couldn't be read."); }
            finally { Gcm.Wipe(plain); }
            if (v.FileVersion < 3) v.vaultId = Rng.Bytes(16);
            v.LastNonce = dataNonce;
            v.LoadKnown();
            return v;
        }

        // ---- save: fresh nonce, header as AAD, atomic replace, previous good file kept as .bak
        public byte[] Serialize()
        {
            if (dataKey == null) throw new VaultException("The vault is locked.");
            if (Tracking) Stamp(Device.Id, Device.Name);
            byte[] plain;
            using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(VaultData)).WriteObject(ms, Data); plain = ms.ToArray(); }
            try
            {
                var nonce = Rng.Bytes(Gcm.NonceSize);
                var h = new MemoryStream();
                Action<byte[]> put = b => h.Write(b, 0, b.Length);
                put(Magic); put(BitConverter.GetBytes(Version)); put(KdfParamBytes()); put(new[] { (byte)salt.Length }); put(salt);
                put(new[] { flags }); put(vaultId); put(skId); put(BitConverter.GetBytes(masterTime)); put(BitConverter.GetBytes(recTime)); put(masterWrap);
                if (recWrap != null) { put(new byte[] { 1 }); put(BitConverter.GetBytes(recIterations)); put(new[] { (byte)recSalt.Length }); put(recSalt); put(recWrap); } else put(new byte[] { 0 });
                put(nonce); put(BitConverter.GetBytes(plain.Length));
                var header = h.ToArray();
                byte[] c, t; Gcm.EncryptRaw(dataKey, nonce, plain, header, out c, out t);
                var file = new byte[header.Length + c.Length + t.Length];
                Buffer.BlockCopy(header, 0, file, 0, header.Length); Buffer.BlockCopy(c, 0, file, header.Length, c.Length); Buffer.BlockCopy(t, 0, file, header.Length + c.Length, t.Length);
                LastNonce = nonce;
                return file;
            }
            finally { Gcm.Wipe(plain); }
        }
        public void Save()
        {
            var bytes = Serialize();
            if (FileVersion < Version && File.Exists(Path))   // the first save of a 1.x vault: its old file stays as vault.hkvault.format2.bak (for going back)
            {
                var keep = Path + ".format" + FileVersion + ".bak";
                try { if (!File.Exists(keep)) File.Copy(Path, keep); } catch { }
            }
            lock (saveGate)
            {
                if (KeepVersions) Versions.MaybeKeep(Path, false);
                WriteAtomic(Path, bytes, true);
            }
            FileVersion = Version;
        }
        static readonly object saveGate = new object();   // this PC's vault file is written by one save at a time (the window, and sync in the background)
        // --snap pictures only: a vault with made-up items and no key (modern = Argon2id + Secret Key, otherwise a 1.x vault's PBKDF2)
        public static Vault Pretend(VaultData d, bool modern) { var v = new Vault { Data = d }; if (modern) { v.kdf = KdfArgon2id; v.argM = ArgonMemoryKiB; v.argT = ArgonPasses; v.argP = ArgonLanes; v.flags = FlagSecretKey; } else v.iterations = Kdf.MinIterations; return v; }
        public static void WriteAtomic(string path, byte[] bytes, bool keepBak)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            for (int i = 0; ; i++)
            {
                try { if (File.Exists(path)) File.Replace(tmp, path, keepBak ? path + ".bak" : null, true); else File.Move(tmp, path); break; }
                catch (IOException) { if (i >= 4) { try { File.Delete(tmp); } catch { } throw; } System.Threading.Thread.Sleep(100 * (i + 1)); }
            }
        }

        // v2.0 sync: take the newer key blocks (a password change, a new recovery code or Secret Key made on another PC) - the data key is the same
        public bool AdoptKeysFrom(Vault other)
        {
            bool changed = false;
            if (!other.vaultId.SequenceEqual(vaultId)) return false;
            if (other.masterTime > masterTime)
            {
                kdf = other.kdf; iterations = other.iterations; argM = other.argM; argT = other.argT; argP = other.argP; salt = (byte[])other.salt.Clone(); masterWrap = (byte[])other.masterWrap.Clone();
                flags = (byte)((flags & FlagLegacyRecovery) | (other.flags & (FlagSecretKey | FlagLegacyMaster))); skId = (byte[])other.skId.Clone(); masterTime = other.masterTime;
                if (other.Data.AccountKey != null) Data.AccountKey = (byte[])other.Data.AccountKey.Clone();
                changed = true;
            }
            if (other.recTime > recTime)
            {
                recIterations = other.recIterations; recSalt = other.recSalt == null ? null : (byte[])other.recSalt.Clone(); recWrap = other.recWrap == null ? null : (byte[])other.recWrap.Clone();
                flags = (byte)((flags & ~FlagLegacyRecovery) | (other.flags & FlagLegacyRecovery)); recTime = other.recTime;
                changed = true;
            }
            return changed;
        }
        public long MasterTime { get { return masterTime; } }
        public long RecoveryTime { get { return recTime; } }
        // v2.0: a vault that was copied in from an older file gets the id of the synced vault it's joining (tests and the join flow)
        internal void SetVaultId(byte[] id) { vaultId = (byte[])id.Clone(); }

        // the data key, for Windows Hello (DPAPI) and reading other copies of this vault in the sync folder - the caller wipes it
        public byte[] DataKeyCopy() { return dataKey == null ? null : (byte[])dataKey.Clone(); }
        public bool Unlocked { get { return dataKey != null; } }
        public void Dispose() { Gcm.Wipe(dataKey); dataKey = null; if (Data != null) { Gcm.Wipe(Data.PhoneKey); Gcm.Wipe(Data.AccountKey); } Data = new VaultData(); }
    }
}