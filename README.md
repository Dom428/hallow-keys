# Hallow Keys

A personal password manager for Windows - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Remote](https://github.com/Dom428/hallow-remote), [Hallow Setup](https://github.com/Dom428/hallow-setup) and the others. Everything stays on your PC, in one encrypted file - no account, no cloud.

> **Not audited.** Hallow Keys hasn't been reviewed by security experts. Keep using a tried-and-tested password manager for anything that matters, and keep backups.

- **Logins, secure notes, cards, identities** with folders, **tags**, favourites, search and small encrypted file attachments (up to 10 MB each)
- **Quick search** - an optional global shortcut (off by default; turn it on in Settings, Ctrl+Shift+Space or your own) opens Hallow Keys with the search box ready; every word you type has to match, `#tag` finds a tag, Enter opens the best match
- **Password history** - each login keeps its last 10 passwords (with the date each was replaced), to show or copy
- **Trash** - deleted items can be restored for 30 days; a **duplicate finder** for the same account saved twice
- **Generator** - random characters or passphrases, made with Windows' cryptographic random numbers
- **Password Health** - weak, reused, old and leaked passwords (an opt-in check against Have I Been Pwned)
- **2FA codes** built in (TOTP from `otpauth://` links or setup keys)
- **Import** from NordPass (CSV export), other CSV exports and Bitwarden (unencrypted JSON); after importing it offers to delete the plain-text file
- **Encrypted backup** of the whole vault, an **encrypted export** with its own password, a **plain CSV export** (behind a warning and your master password), a printable **recovery sheet**, master password change
- **Auto-lock** after idle minutes, when Windows locks and when the PC sleeps; copied passwords leave the clipboard after 20 seconds and never go into Windows' clipboard history
- **Browser autofill** (Chrome, Edge, Brave) - fills only when you click, only on the site a login was saved for, never into hidden boxes; fills 2FA codes too; offers to save new logins
- **On your phone** through [Hallow Remote](https://github.com/Dom428/hallow-remote): an optional read-only copy, opened on the phone with your master password (HTTPS only)
- Optional **Windows Hello** unlock (convenience, not extra security - see below)

## What's new

- **1.4** - The quick search shortcut is now off by default, so Hallow Keys never takes Ctrl+Shift+Space away from games or other apps unless you turn it on (Settings > Quick search shortcut).
- **1.3** - Password history (show or copy any of a login's last 10 passwords); a trash that keeps deleted items for 30 days; tags and a smarter search; a duplicate finder; an encrypted export with its own password and a plain CSV export (with a strong warning); a global quick search shortcut (Ctrl+Shift+Space, changeable, left off if another app already uses it); the browser extension fills 2FA codes on the page that asks for them (reload the extension once after updating). The password box now also warns when a password is used by another login or was used here before.
- **1.2** - Check for leaked passwords (Have I Been Pwned - only a 5-character hash prefix leaves your PC), shown in Password Health; an optional read-only phone copy you open in Hallow Remote's Keys tab (needs Remote's HTTPS).
- **1.1** - Browser autofill for Chrome, Edge and Brave: an extension (in the `extension` folder) that fills a login when you click it or the key in a password box, and offers to save new logins after you sign in.
- **1.0** - First version: the vault, generator, Password Health, 2FA codes, import from NordPass / CSV / Bitwarden, encrypted backup and the recovery sheet.

## How it's built

- **Master key:** PBKDF2-HMAC-SHA256 (`Rfc2898DeriveBytes`), 600,000 rounds, a random 16-byte salt; the parameters are stored in the file header.
- **Envelope:** a random 256-bit data key encrypts the vault; the master key (and, separately, the recovery code) wraps the data key. Changing the master password only re-wraps the key.
- **Encryption:** AES-256-GCM from Windows' CNG (`bcrypt.dll`) - no hand-written cryptography - with a fresh random 96-bit nonce every save, and the whole header authenticated as associated data.
- **File:** `%APPDATA%\HallowKeys\vault.hkvault`, a versioned format with magic bytes. Saves are atomic (temp file, then replace) and the previous good file is kept as `vault.hkvault.bak`. A wrong password or a changed file is reported and never "repaired".
- **Windows Hello** (off by default): the data key is protected with DPAPI for your Windows account and handed back only after Windows Hello confirms it's you. Anything that can run as you on this PC could get at it, so it's for convenience, not stronger security.
- **Memory:** key and plaintext byte arrays are wiped after use, and the vault is dropped when it locks. .NET strings (the items while the vault is open, text in the window) can't be wiped - they stay in memory until the garbage collector reuses it.
- **New in 1.3, all inside the encrypted vault:** password history (and the dates), tags and the trash are part of the encrypted items - the file format and its encryption are unchanged. Items in the trash don't fill, don't count in Password Health and don't go to the phone copy; after 30 days they're removed when you unlock.
- **Exports:** the encrypted export is the same file format with its own random data key, its own salt and PBKDF2-SHA256 (600,000 rounds) from the password you pick, and no recovery code - your master password doesn't open it. The plain CSV export asks for your master password first; text that a spreadsheet would treat as a formula gets a leading `'` (Hallow Keys' import removes it), passwords are written exactly as they are, and the trash isn't exported.
- **Quick search shortcut:** registered with Windows' RegisterHotKey on a hidden message window. If another app already has the combination, Windows refuses and Hallow Keys leaves it off (it never takes a shortcut away from another app) and tries again every few minutes.
- **Nothing secret leaves the vault:** not in logs, crash reports (anything from the open vault is blanked out of them), status files, the family pipe (it answers only ping, status - locked or unlocked and the item count -, open, menu and invoke), window titles or `--snap` pictures (made-up items only).
- **Generator:** `RNGCryptoServiceProvider` with rejection sampling (no modulo bias).
- **Leak check:** Have I Been Pwned's range API with the `Add-Padding` header; only the first 5 hex characters of each password's SHA-1 leave the PC, and the match happens locally. Only when you click, or weekly if you turn that on.
- **Phone copy:** `phone.bin` next to the vault, AES-256-GCM under its own PBKDF2-SHA256 key (600,000 rounds, own salt) from your master password; Hallow Remote serves only that file and only over HTTPS; the phone decrypts it with WebCrypto (view and copy only, locks after 2 minutes or when you leave the page). The phone key is kept inside the encrypted vault.
- **Browser extension:** a Manifest V3 extension with a fixed ID. The browser starts `HallowKeys.exe` as a native messaging host (registered per user under HKCU when you click Connect browsers); it answers only that extension ID, never reads the vault, and passes requests to the running, unlocked app over a pipe with a per-session secret. Logins are offered only on the exact host or the same registrable domain (look-alike Unicode names don't match, and https logins are never offered on http pages), filled only after a click, only into visible boxes in the top page or same-origin frames. For 2FA the extension gets only the current 6- or 8-digit code of a login that matches the page - never the 2FA key.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 6.7.2 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).
