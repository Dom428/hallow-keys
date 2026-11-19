# Hallow Keys

A personal password manager for Windows - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Remote](https://github.com/Dom428/hallow-remote), [Hallow Setup](https://github.com/Dom428/hallow-setup) and the others. Everything stays on your PC, in one encrypted file - no account, no cloud.

> **Not audited.** Hallow Keys hasn't been reviewed by security experts. Keep using a tried-and-tested password manager for anything that matters, and keep backups.

- **Logins, secure notes, cards, identities** with folders, favourites, search and small encrypted file attachments (up to 10 MB each)
- **Generator** - random characters or passphrases, made with Windows' cryptographic random numbers
- **Password Health** - weak, reused and old passwords
- **2FA codes** built in (TOTP from `otpauth://` links or setup keys)
- **Import** from NordPass (CSV export), other CSV exports and Bitwarden (unencrypted JSON); after importing it offers to delete the plain-text file
- **Encrypted backup** of the whole vault, a printable **recovery sheet**, master password change
- **Auto-lock** after idle minutes, when Windows locks and when the PC sleeps; copied passwords leave the clipboard after 20 seconds and never go into Windows' clipboard history
- **Browser autofill** (Chrome, Edge, Brave) - fills only when you click, only on the site a login was saved for, never into hidden boxes; offers to save new logins
- Optional **Windows Hello** unlock (convenience, not extra security - see below)

## What's new

- **1.1** - Browser autofill for Chrome, Edge and Brave: an extension (in the `extension` folder) that fills a login when you click it or the key in a password box, and offers to save new logins after you sign in.
- **1.0** - First version: the vault, generator, Password Health, 2FA codes, import from NordPass / CSV / Bitwarden, encrypted backup and the recovery sheet.

## How it's built

- **Master key:** PBKDF2-HMAC-SHA256 (`Rfc2898DeriveBytes`), 600,000 rounds, a random 16-byte salt; the parameters are stored in the file header.
- **Envelope:** a random 256-bit data key encrypts the vault; the master key (and, separately, the recovery code) wraps the data key. Changing the master password only re-wraps the key.
- **Encryption:** AES-256-GCM from Windows' CNG (`bcrypt.dll`) - no hand-written cryptography - with a fresh random 96-bit nonce every save, and the whole header authenticated as associated data.
- **File:** `%APPDATA%\HallowKeys\vault.hkvault`, a versioned format with magic bytes. Saves are atomic (temp file, then replace) and the previous good file is kept as `vault.hkvault.bak`. A wrong password or a changed file is reported and never "repaired".
- **Windows Hello** (off by default): the data key is protected with DPAPI for your Windows account and handed back only after Windows Hello confirms it's you. Anything that can run as you on this PC could get at it, so it's for convenience, not stronger security.
- **Memory:** key and plaintext byte arrays are wiped after use, and the vault is dropped when it locks. .NET strings (the items while the vault is open, text in the window) can't be wiped - they stay in memory until the garbage collector reuses it.
- **Nothing secret leaves the vault:** not in logs, crash reports (anything from the open vault is blanked out of them), status files, the family pipe (it answers only ping, status - locked or unlocked and the item count -, open, menu and invoke), window titles or `--snap` pictures (made-up items only).
- **Generator:** `RNGCryptoServiceProvider` with rejection sampling (no modulo bias).
- **Browser extension:** a Manifest V3 extension with a fixed ID. The browser starts `HallowKeys.exe` as a native messaging host (registered per user under HKCU when you click Connect browsers); it answers only that extension ID, never reads the vault, and passes requests to the running, unlocked app over a pipe with a per-session secret. Logins are offered only on the exact host or the same registrable domain (look-alike Unicode names don't match, and https logins are never offered on http pages), filled only after a click, only into visible boxes in the top page or same-origin frames.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 6.7.2 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).