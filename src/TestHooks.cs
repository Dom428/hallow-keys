// Hallow Keys - hooks for the developer tests. They only work inside a test folder (HALLOW_KEYS_TESTDIR), never for a real vault:
//   HALLOW_KEYS_TEST_UNLOCK=<password>  unlock the test vault at start (the tests can't type into the window)
//   HALLOW_KEYS_CRASHTEST=1             crash on purpose after unlocking, with a test password in the message (checks crash reports are cleaned)
using System;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        partial void ExtraInit()
        {
            if (KeysSettings.TestDir == null) return;
            var pw = Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_UNLOCK");
            if (string.IsNullOrEmpty(pw)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var b = System.Text.Encoding.UTF8.GetBytes(pw);
                try { Unlocked(Vault.Open(KeysSettings.VaultPath, b)); } catch (Exception e) { if (lockStatus != null) lockStatus.Text = e.Message; }
                finally { Gcm.Wipe(b); }
                if (vault != null && Environment.GetEnvironmentVariable("HALLOW_KEYS_CRASHTEST") == "1")
                    Dispatcher.BeginInvoke(new Action(() => { throw new InvalidOperationException("Crash test with " + (vault.Data.Items.Count > 0 ? vault.Data.Items[0].Password : "") + " inside"); }), DispatcherPriority.ApplicationIdle);
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
