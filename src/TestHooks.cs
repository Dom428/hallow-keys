// Hallow Keys - hooks for the developer tests. They only work inside a test folder (HALLOW_KEYS_TESTDIR), never for a real vault:
//   HALLOW_KEYS_TEST_UNLOCK=<password>  unlock the test vault at start (the tests can't type into the window)
//   HALLOW_KEYS_CRASHTEST=1             crash on purpose after unlocking, with a test password in the message (checks crash reports are cleaned)
using System;
using System.Linq;
using System.Windows.Threading;

namespace HallowKeys
{
    public partial class MainWindow
    {
        partial void ExtraInit()
        {
            Init21();   // v2.1: list keys, copies count as "used"
            Init26();   // v2.6: the command line's pipe, the SSH agent if it's on
            if (KeysSettings.TestDir == null) return;
            var pw = Environment.GetEnvironmentVariable("HALLOW_KEYS_TEST_UNLOCK");
            if (string.IsNullOrEmpty(pw)) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var b = System.Text.Encoding.UTF8.GetBytes(pw);
                try { Unlocked(Vault.Open(KeysSettings.VaultPath, b)); } catch (Exception e) { if (lockStatus != null) lockStatus.Text = e.Message; }
                finally { Gcm.Wipe(b); }
                if (vault != null && Environment.GetEnvironmentVariable("HALLOW_KEYS_CRASHTEST") == "1")
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var hidden = vault.Data.Items.SelectMany(i => i.Fields).FirstOrDefault(f => ItemTypes.IsHidden(f) && f.Value.Length > 0);   // v1.6: a hidden field's value too
                        throw new InvalidOperationException("Crash test with " + (vault.Data.Items.Count > 0 ? vault.Data.Items[0].Password : "") + " inside" + (hidden != null ? " and " + hidden.Value : ""));
                    }), DispatcherPriority.ApplicationIdle);
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
