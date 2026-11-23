// Hallow Keys - settings (never secrets: the vault itself is the only place items live).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HallowKeys
{
    [DataContract]
    public class KeysSettings
    {
        [DataMember] public int AutoLockMinutes = 5;
        [DataMember] public bool LockOnSleep = true;
        [DataMember] public bool UseHello = false;          // Windows Hello unlock (off by default)
        [DataMember] public bool TrayOnClose = true;
        [DataMember] public bool UseHallowIcon = true;
        [DataMember] public bool UseHallowAccent = true;
        [DataMember] public int OldDays = 365;
        [DataMember] public string LastCrashShown = "";
        [DataMember] public string LastUpdateCheck = "";
        [DataMember] public int GenLength = 20; [DataMember] public bool GenLower = true, GenUpper = true, GenDigits = true, GenSymbols = true, GenAvoid = true;
        [DataMember] public bool GenWords; [DataMember] public int GenWordCount = 5;
        [DataMember] public bool PhoneCopy;            // v1.2: an encrypted copy for Hallow Remote's phone page
        [DataMember] public bool WeeklyLeakCheck;      // v1.2
        [DataMember] public string LastLeakCheck = "";
        [DataMember] public bool QuickSearchKey = false;            // v1.3: a global shortcut that opens quick search (left off if another app has it); off by default since 1.4
        [DataMember] public string QuickSearchCombo = "Ctrl+Shift+Space";
        [DataMember] public bool DesktopFill = false;                // v1.5: desktop app autofill (off by default)
        [DataMember] public string DesktopFillCombo = "";            // v1.5: its shortcut - none until you pick one, so nothing is grabbed
        [DataMember] public bool ExpiryReminders = true;             // v1.6: a tray notification before cards, passports, licences... expire
        [DataMember] public int ExpiryDays = 30;                     // v1.6: how many days before
        [DataMember] public bool WatchWeekly = false;                // v1.7: download the breached-sites and 2FA lists weekly (off: only on click)
        [DataMember] public string LastWatchUpdate = "";             // v1.7
        [DataMember] public bool SyncOn = false;                     // v2.0: sync through a folder (off until you turn it on)
        [DataMember] public string SyncFolder = "";                  // v2.0: the folder (OneDrive, Google Drive, Dropbox, a NAS, Syncthing...)
        [DataMember] public string LastSync = "";                    // v2.0: when it last synced
        [DataMember] public bool SiteIcons = false;                  // v2.1: fetch each login's icon from its own site (off until you turn it on)
        [DataMember] public bool TravelMode = false;                 // v2.1: Travel Mode on this PC (only vaults marked safe for travel are shown)
        [DataMember] public string TravelSince = "";                 // v2.1: when it was turned on
        [DataMember] public bool SshAgentOn = false;                 // v2.6: the SSH agent (off until you turn it on)
        [DataMember] public bool SshOwnPipe = false;                 // v2.6: serve Hallow Keys' own pipe instead of Windows' standard one
        [DataMember] public System.Collections.Generic.List<string> SshKeyIds = new System.Collections.Generic.List<string>();   // v2.6: the SSH key items on offer (ids only)
        [DataMember] public int ApproveMinutes = 10;                 // v2.6: the prompt offers "don't ask again for N minutes" for that program (0 = never offered)
        [DataMember] public bool ApproveWithHello = false;           // v2.6: approve with Windows Hello instead of the prompt
        [DataMember] public bool CliOn = false;                      // v2.6: the command line (--get, --env, --run) may ask (off until you turn it on)
        [DataMember] public bool PhoneEdit = false;                  // v2.7: the phone may send changes back (separate from the read-only phone copy)
        [DataMember] public string Look = "follow";                  // v2.9: follow Hallow (theme.json) / light / dark
        // v2.14: "Don't lose access" - the safety checklist (markers only, never the code or a key) and automatic encrypted backups
        [DataMember] public string SafetyRecovery = "";              // the recovery code's time (from the vault) when you confirmed it's saved ("" = not yet)
        [DataMember] public string SafetyRecoveryHow = "";           // "check" (typed characters / the whole code) or "kit" (you marked the printed kit)
        [DataMember] public string SafetyKit = "";                   // the recovery code's time + Secret Key id when you printed / marked the Emergency Kit
        [DataMember] public string SafetySnoozeUntil = "";           // the card and the reminder are hidden until then (7 days at a time)
        [DataMember] public string SafetyLastReminder = "";          // the day of the last reminder (at most one a day)
        [DataMember] public string SafetyDoneAt = "";                // all three were done once: never reminded again
        [DataMember] public bool AutoBackupOn = true;                // dated encrypted copies of the vault file (on by default)
        [DataMember] public string AutoBackupFolder = "";            // "" = HallowKeys\backups on this PC (pick another drive or a NAS)
        [DataMember] public bool AutoBackupPicked = false;           // you picked the folder yourself
        [DataMember] public int AutoBackupKeep = 14;                 // keep the newest N (of this vault)
        [DataMember] public string LastAutoBackup = "";              // when the last one was written
        [DataMember] public string LastAutoBackupHash = "";          // the vault file's SHA-256 then (no new copy when nothing changed)
        [DataMember] public string AutoBackupError = "";             // the last failure (shown; the checklist stays open)
        [DataMember] public string LastBackupTest = "";              // the last restore test: when
        [DataMember] public string LastBackupTestResult = "";        // and what it found
        [DataMember] public ImportCompare LastImport;                // the last import (counts only) for "Leaving NordPass"
        [OnDeserializing] void D(StreamingContext c) { SafetyRecovery = SafetyRecoveryHow = SafetyKit = SafetySnoozeUntil = SafetyLastReminder = SafetyDoneAt = ""; AutoBackupOn = true; AutoBackupFolder = ""; AutoBackupKeep = 14; LastAutoBackup = LastAutoBackupHash = AutoBackupError = LastBackupTest = LastBackupTestResult = "";Look = "follow"; AutoLockMinutes = 5; LockOnSleep = true; TrayOnClose = true; UseHallowIcon = true; UseHallowAccent = true; OldDays = 365; LastCrashShown = ""; LastUpdateCheck = ""; GenLength = 20; GenLower = GenUpper = GenDigits = GenSymbols = GenAvoid = true; GenWordCount = 5; LastLeakCheck = ""; QuickSearchKey = false; QuickSearchCombo = "Ctrl+Shift+Space"; DesktopFill = false; DesktopFillCombo = ""; ExpiryReminders = true; ExpiryDays = 30; WatchWeekly = false; LastWatchUpdate = ""; SyncOn = false; SyncFolder = ""; LastSync = ""; SshKeyIds = new System.Collections.Generic.List<string>(); ApproveMinutes = 10; }
        [OnDeserialized] void After26(StreamingContext c) { if (SshKeyIds == null) SshKeyIds = new System.Collections.Generic.List<string>(); }

        // HALLOW_KEYS_TESTDIR (developer tests only): everything lives in that folder instead, so tests never touch the real vault
        public static string TestDir { get { var t = Environment.GetEnvironmentVariable("HALLOW_KEYS_TESTDIR"); return string.IsNullOrEmpty(t) ? null : t; } }
        public static string Dir { get { return TestDir != null ? Path.Combine(TestDir, "HallowKeys") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowKeys"); } }
        public static string VaultPath { get { return Path.Combine(Dir, "vault.hkvault"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }
        public static bool Frozen;
        public static KeysSettings Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) return (KeysSettings)new DataContractJsonSerializer(typeof(KeysSettings)).ReadObject(f); } catch { }
            return new KeysSettings();
        }
        public void Save()
        {
            if (Frozen) return;
            try
            {
                Directory.CreateDirectory(Dir); var tmp = FilePath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(KeysSettings)).WriteObject(f, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    // ---- the clipboard: kept out of Windows' clipboard history and cloud clipboard, and cleared after 20 s if nobody copied anything else
    public static class SafeClip
    {
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        static uint ours; static DispatcherTimer timer;
        public static int ClearSeconds = 20;
        public static event Action Copied;
        public static bool Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var d = new DataObject();
            d.SetData(DataFormats.UnicodeText, text);
            d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 0, 0, 0, 0 }));   // clipboard managers / history: skip this
            d.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));                    // Win+V history: no
            d.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));                      // cloud clipboard: no
            for (int i = 0; i < 5; i++) { try { Clipboard.SetDataObject(d, true); break; } catch { if (i == 4) return false; Thread.Sleep(60); } }
            ours = GetClipboardSequenceNumber();
            if (Copied != null) try { Copied(); } catch { }   // v2.1: the window counts it as a use of the item shown
            if (timer == null) { timer = new DispatcherTimer(); timer.Tick += (s, e) => { timer.Stop(); ClearIfOurs(); }; }
            timer.Stop(); timer.Interval = TimeSpan.FromSeconds(ClearSeconds); timer.Start();
            return true;
        }
        public static void ClearIfOurs()
        {
            if (ours != 0 && GetClipboardSequenceNumber() == ours) { try { Clipboard.Clear(); } catch { } }
            ours = 0;
        }
    }

    // ---- Windows Hello: convenience, not extra security. The data key is kept with DPAPI (this Windows account only)
    // and only handed back after Windows Hello says it's you. Anyone who can run programs as you could get it.
    public static class Hello
    {
        static string FilePath { get { return Path.Combine(KeysSettings.Dir, "hello.bin"); } }
        static readonly byte[] Entropy = System.Text.Encoding.ASCII.GetBytes("HallowKeys-Hello-v1");
        public static bool Enabled { get { return File.Exists(FilePath); } }
        public static void Enable(byte[] dataKey) { Directory.CreateDirectory(KeysSettings.Dir); File.WriteAllBytes(FilePath, ProtectedData.Protect(dataKey, Entropy, DataProtectionScope.CurrentUser)); }
        public static void Disable() { try { if (File.Exists(FilePath)) { File.WriteAllBytes(FilePath, new byte[256]); File.Delete(FilePath); } } catch { } }
        // test stub (developer tests only, in a test folder): HALLOW_KEYS_HELLO_STUB = ok | cancel
        static string Stub { get { return KeysSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_KEYS_HELLO_STUB") : null; } }
        public static bool Available()
        {
            if (Stub != null) return true;
            try { var op = Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync(); return Wait(op, 5000) == Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available; }
            catch { return false; }
        }
        // asks Windows Hello (blocks this thread until answered) and returns the data key, or null
        public static byte[] Unlock(out string error)
        {
            error = null;
            if (!Enabled) { error = "Windows Hello isn't set up for Hallow Keys."; return null; }
            bool ok;
            if (Stub != null) ok = Stub == "ok";
            else
            {
                try { var op = Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync("Unlock Hallow Keys"); ok = Wait(op, 120000) == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified; }
                catch (Exception e) { error = "Windows Hello didn't work: " + e.Message; return null; }
            }
            if (!ok) { error = "Windows Hello didn't confirm it's you."; return null; }
            try { return ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser); }
            catch { error = "The saved Windows Hello key can't be read - unlock with your master password and turn Windows Hello on again."; return null; }
        }
        // v2.3: just "is it you?" (passkeys, when Windows Hello unlock is on) - blocks this thread until answered
        public static bool Verify(string why, out string error)
        {
            error = null;
            if (Stub != null) return Stub == "ok";
            try { var op = Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync(why); return Wait(op, 120000) == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified; }
            catch (Exception e) { error = "Windows Hello didn't work: " + e.Message; return false; }
        }
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int ms)
        {
            var t0 = Environment.TickCount;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && Environment.TickCount - t0 < ms) Thread.Sleep(20);
            return op.Status == Windows.Foundation.AsyncStatus.Completed ? op.GetResults() : default(T);
        }
    }
}
