// Hallow Keys - "--snap <folder>": pictures of every screen with made-up items (no vault is read, nothing is saved).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HallowKeys
{
    public partial class MainWindow
    {
        public static VaultData FakeData()
        {
            var d = new VaultData();
            d.Folders.AddRange(new[] { "Games", "Work" });
            d.Items.Add(new Item { Title = "Steam", Type = "login", Username = "player.one@example.com", Password = "correct-Horse-battery-7", Urls = { "https://store.steampowered.com" }, Folder = "Games", Favorite = true, Totp = "otpauth://totp/Steam:player?secret=JBSWY3DPEHPK3PXP", Apps = { "steam.exe" } });
            d.Items.Add(new Item { Title = "Example Mail", Type = "login", Username = "me@example.com", Password = "hunter2", Urls = { "https://mail.example.com" }, PasswordChanged = DateTime.Now.AddDays(-500) });
            d.Items.Add(new Item { Title = "Example Shop", Type = "login", Username = "me@example.com", Password = "hunter2", Urls = { "https://shop.example.net" } });
            d.Items.Add(new Item { Title = "Battle.net", Type = "login", Username = "playerone", Password = "Zq8!vL2#nW5@rT9x", Urls = { "https://battle.net" }, Folder = "Games" });
            d.Items.Add(new Item { Title = "Wi-Fi at home", Type = "note", Notes = "Network: Example-5G\nPassword: (made up for the picture)" });
            d.Items.Add(new Item { Title = "Everyday card", Type = "card", CardHolder = "A N OTHER", CardNumber = "4111 1111 1111 1111", Expiry = "08/29", Cvc = "123" });
            d.Items.Add(new Item { Title = "Me", Type = "identity", FullName = "Alex Example", Email = "alex@example.com", Phone = "+1 555 0100", City = "Springfield", Country = "Example" });
            // v1.3: tags, password history, a duplicate and something in the trash (all made up)
            d.Items[0].Tags.AddRange(new[] { "games", "2fa" }); d.Items[3].Tags.Add("games"); d.Items[1].Tags.Add("email");
            Organize.RecordOldPassword(d.Items[0], "old-Steam-pass-2", DateTime.Now.AddDays(-40)); Organize.RecordOldPassword(d.Items[0], "old-Steam-pass-1", DateTime.Now.AddDays(-400));
            d.Items[0].OldPasswords.Reverse(); d.Items[0].OldPasswordDates.Reverse();
            d.Items.Add(new Item { Title = "Example Mail (old)", Type = "login", Username = "me@example.com", Password = "hunter3", Urls = { "https://www.mail.example.com" }, Updated = DateTime.Now.AddDays(-90) });
            var gone = new Item { Title = "Old forum", Type = "login", Username = "player1", Password = "made-up", Urls = { "https://forum.example.org" } };
            d.Items.Add(gone); Organize.MoveToTrash(d, gone, DateTime.Now.AddDays(-12));
            return d;
        }
        public static void SnapAll(string dir)
        {
            snapDir = dir;
            Action<MainWindow, string> save = (w, n) => Snap.Save(w, System.IO.Path.Combine(dir, n), 1060, 700);
            var w1 = new MainWindow(false, true); w1.snapVaultExists = true; w1.ShowLock(); save(w1, "lock.png");
            var w2 = new MainWindow(false, true); w2.snapVaultExists = false; w2.ShowLock(); save(w2, "create.png");
            var w3 = new MainWindow(false, true); w3.vault = new Vault { Data = FakeData() };
            w3.selected = w3.vault.Data.Items[0]; w3.ShowMain(); save(w3, "main.png");
            w3.StartEdit(w3.vault.Data.Items[0], false); save(w3, "edit.png");
            foreach (var v in new[] { "health", "generator", "transfer", "settings" }) { w3.view = v; w3.ShowMain(); save(w3, v + ".png"); }
            w3.SnapExtra(save);
            w3.ShowRecoverySheet("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789", true);
        }
        bool? snapVaultExists;
        void SnapExtra(Action<MainWindow, string> save) { SnapExtraImpl(save); }
        partial void SnapExtraImpl(Action<MainWindow, string> save);
    }
}
