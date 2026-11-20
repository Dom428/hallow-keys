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
        [OnDeserialized] void Fix(StreamingContext c) { if (Items == null) Items = new List<Item>(); if (Folders == null) Folders = new List<string>(); if (LeakedIds == null) LeakedIds = new List<string>(); if (Trash == null) Trash = new List<Item>(); if (Templates == null) Templates = new List<ItemTemplate>(); if (ExpiryNotified == null) ExpiryNotified = new List<string>(); }
    }

    public class VaultException : Exception { public VaultException(string m) : base(m) { } }

    // an open vault: the data key (in memory only while unlocked) and the header blocks that don't change between saves
    public class Vault : IDisposable
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HKVAULT\0");
        public const ushort Version = 2, OldestVersion = 1;   // v1.6 writes 2, reads 1 and 2
        public const long MaxFileBytes = 10L * 1024 * 1024;
        public string Path;
        public VaultData Data = new VaultData();
        byte[] dataKey;
        int iterations; byte[] salt; byte[] masterWrap;            // master: kdf params + nonce|ct|tag
        int recIterations; byte[] recSalt; byte[] recWrap;          // recovery (optional)
        public byte[] LastNonce;                                    // for tests: the data nonce of the last save
        public bool HasRecovery { get { return recWrap != null; } }

        static byte[] AadWrap(char which) { return Encoding.ASCII.GetBytes("HKWRAP1" + which); }

        // ---- new vault: random data key, wrapped by the master password (and a recovery code, returned once)
        public static Vault Create(string path, byte[] password, out string recoveryCode)
        {
            var v = new Vault { Path = path, dataKey = Rng.Bytes(32) };
            v.SetMaster(password);
            recoveryCode = v.NewRecoveryCode();
            return v;
        }
        void SetMaster(byte[] password)
        {
            var s = Rng.Bytes(Kdf.SaltSize); var mk = Kdf.Derive(password, s, Kdf.MinIterations);
            try { masterWrap = Gcm.Seal(mk, dataKey, AadWrap('M')); salt = s; iterations = Kdf.MinIterations; } finally { Gcm.Wipe(mk); }
        }
        // a new recovery code (20 random bytes, base32 in groups): a second wrapper of the data key, shown once
        public string NewRecoveryCode()
        {
            var raw = Rng.Bytes(20); var code = Base32(raw); Gcm.Wipe(raw);
            var pretty = string.Join("-", Enumerable.Range(0, 8).Select(i => code.Substring(i * 4, 4)));
            var bytes = RecoveryBytes(pretty);
            try { var s = Rng.Bytes(Kdf.SaltSize); var rk = Kdf.Derive(bytes, s, Kdf.MinIterations); try { recWrap = Gcm.Seal(rk, dataKey, AadWrap('R')); recSalt = s; recIterations = Kdf.MinIterations; } finally { Gcm.Wipe(rk); } }
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

        public void ChangePassword(byte[] newPassword) { SetMaster(newPassword); Save(); }   // re-wraps the same data key

        // v1.3: an encrypted export with its own password - the same file format (its own random data key and salt, no recovery code),
        // so Restore backup opens it. Only items and folders go in (no trash, no phone key, no leak results).
        public static void WriteExport(string path, byte[] password, IEnumerable<Item> items, IEnumerable<string> folders) { WriteExport(path, password, items, folders, null); }
        public static void WriteExport(string path, byte[] password, IEnumerable<Item> items, IEnumerable<string> folders, IEnumerable<ItemTemplate> templates)
        {
            var v = new Vault { Path = path, dataKey = Rng.Bytes(32) };
            v.Data = new VaultData { Items = items.ToList(), Folders = folders.ToList(), Templates = templates == null ? new List<ItemTemplate>() : templates.ToList() };
            try { v.SetMaster(password); WriteAtomic(path, v.Serialize(), false); }
            finally { Gcm.Wipe(v.dataKey); v.dataKey = null; v.Data = new VaultData(); }
        }

        // ---- open
        public static Vault Open(string path, byte[] password) { return OpenBytes(path, ReadFile(path), password, false, null); }
        public static Vault OpenWithRecovery(string path, string code) { var b = RecoveryBytes(code); try { return OpenBytes(path, ReadFile(path), b, true, null); } finally { Gcm.Wipe(b); } }
        // with the data key itself (Windows Hello; tests with a key they already derived)
        public static Vault OpenWithDataKey(string path, byte[] dataKey) { return OpenBytes(path, ReadFile(path), null, false, dataKey); }
        static byte[] ReadFile(string path)
        {
            if (!File.Exists(path)) throw new VaultException("There's no vault file at " + path);
            var b = File.ReadAllBytes(path);
            if (b.Length > 64L * 1024 * 1024 * 4) throw new VaultException("The vault file is too big to be a Hallow Keys vault.");
            return b;
        }
        public ushort FileVersion = Version;   // the format of the file when it was opened (1 = made by 1.5 or older; saved as 2 next time)
        public static Vault OpenBytes(string path, byte[] file, byte[] secret, bool recovery, byte[] givenKey)
        {
            var v = new Vault { Path = path }; int p = 0; int headerEnd; byte[] dataNonce; int clen;
            try
            {
                Func<int, byte[]> take = n => { if (n < 0 || p + n > file.Length) throw new VaultException("bad"); var r = new byte[n]; Buffer.BlockCopy(file, p, r, 0, n); p += n; return r; };
                if (!take(8).SequenceEqual(Magic)) throw new VaultException("This isn't a Hallow Keys vault.");
                var ver = BitConverter.ToUInt16(take(2), 0); v.FileVersion = ver; if (ver < OldestVersion || ver > Version) throw new VaultException("This vault was made by a newer Hallow Keys (format " + ver + ").");
                if (take(1)[0] != 1) throw new VaultException("Unknown key derivation in the vault header.");
                v.iterations = BitConverter.ToInt32(take(4), 0); int sl = take(1)[0]; v.salt = take(sl);
                v.masterWrap = take(Gcm.NonceSize + 32 + Gcm.TagSize);
                var hasRec = take(1)[0];
                if (hasRec == 1) { v.recIterations = BitConverter.ToInt32(take(4), 0); int rl = take(1)[0]; v.recSalt = take(rl); v.recWrap = take(Gcm.NonceSize + 32 + Gcm.TagSize); }
                else if (hasRec != 0) throw new VaultException("bad");
                dataNonce = take(Gcm.NonceSize); clen = BitConverter.ToInt32(take(4), 0); headerEnd = p;
                if (clen < 0 || headerEnd + clen + Gcm.TagSize != file.Length) throw new VaultException("bad");
            }
            catch (VaultException e) { if (e.Message == "bad") throw new VaultException("The vault file is damaged or was changed - it can't be opened. Your last good copy is the .bak file next to it."); throw; }
            // the data key
            if (givenKey != null) v.dataKey = (byte[])givenKey.Clone();
            else
            {
                int it = recovery ? v.recIterations : v.iterations; var s = recovery ? v.recSalt : v.salt; var wrap = recovery ? v.recWrap : v.masterWrap;
                if (recovery && wrap == null) throw new VaultException("This vault has no recovery code.");
                if (it < Kdf.MinIterations || it > 50000000 || s == null || s.Length < Kdf.SaltSize) throw new VaultException("The vault file is damaged or was changed - it can't be opened. Your last good copy is the .bak file next to it.");
                var k = Kdf.Derive(secret, s, it);
                try { v.dataKey = Gcm.Open(k, wrap, AadWrap(recovery ? 'R' : 'M')); }
                catch (AuthFailedException) { throw new VaultException(recovery ? "That recovery code doesn't open this vault." : "Wrong master password (or the vault file was changed)."); }
                finally { Gcm.Wipe(k); }
            }
            // the items, with the whole header as AAD
            var header = new byte[headerEnd]; Buffer.BlockCopy(file, 0, header, 0, headerEnd);
            var ct = new byte[clen]; Buffer.BlockCopy(file, headerEnd, ct, 0, clen);
            var tag = new byte[Gcm.TagSize]; Buffer.BlockCopy(file, headerEnd + clen, tag, 0, Gcm.TagSize);
            byte[] plain;
            try { plain = Gcm.DecryptRaw(v.dataKey, dataNonce, ct, tag, header); }
            catch (AuthFailedException) { v.Dispose(); throw new VaultException(givenKey != null ? "This vault doesn't match the saved Windows Hello key - unlock with your master password." : "The vault file is damaged or was changed - it can't be opened. Your last good copy is the .bak file next to it."); }
            try { using (var ms = new MemoryStream(plain)) v.Data = (VaultData)new DataContractJsonSerializer(typeof(VaultData)).ReadObject(ms); }
            catch { v.Dispose(); throw new VaultException("The vault opened but its contents couldn't be read."); }
            finally { Gcm.Wipe(plain); }
            v.LastNonce = dataNonce;
            return v;
        }

        // ---- save: fresh nonce, header as AAD, atomic replace, previous good file kept as .bak
        public byte[] Serialize()
        {
            if (dataKey == null) throw new VaultException("The vault is locked.");
            byte[] plain;
            using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(VaultData)).WriteObject(ms, Data); plain = ms.ToArray(); }
            try
            {
                var nonce = Rng.Bytes(Gcm.NonceSize);
                var h = new MemoryStream();
                Action<byte[]> put = b => h.Write(b, 0, b.Length);
                put(Magic); put(BitConverter.GetBytes(Version)); put(new byte[] { 1 }); put(BitConverter.GetBytes(iterations)); put(new[] { (byte)salt.Length }); put(salt); put(masterWrap);
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
        public void Save() { WriteAtomic(Path, Serialize(), true); }
        public static void WriteAtomic(string path, byte[] bytes, bool keepBak)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            if (File.Exists(path)) File.Replace(tmp, path, keepBak ? path + ".bak" : null, true); else File.Move(tmp, path);
        }

        // the data key, for Windows Hello (DPAPI) - the caller wipes it
        public byte[] DataKeyCopy() { return dataKey == null ? null : (byte[])dataKey.Clone(); }
        public bool Unlocked { get { return dataKey != null; } }
        public void Dispose() { Gcm.Wipe(dataKey); dataKey = null; if (Data != null) Gcm.Wipe(Data.PhoneKey); Data = new VaultData(); }
    }
}
