# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |
| **1.1** | Browser autofill: Manifest V3 extension (Chrome, Edge, Brave) with a fixed ID; native messaging host (HallowKeys.exe, pinned to that ID, per-user HKCU registration) relaying to the running app over a pipe with a per-session secret; exact-host or registrable-domain matching, no http for https logins, fill only on click into visible fields in the top / same-origin frames; save and update offers after a login form is sent |
| **1.2** | Check for leaked passwords (HIBP range API with Add-Padding, 5-character SHA-1 prefix only, on click or weekly opt-in) in Password Health; opt-in phone copy (AES-256-GCM, own PBKDF2-SHA256 key from the master password) served by Hallow Remote 1.10 over HTTPS and opened on the phone with WebCrypto |
| **1.3** | Password history (last 10 per login with dates, show / copy / clear); trash with 30-day restore (purged on unlock); tags (sidebar, editor, `#tag` search) and multi-word ranked search; duplicate finder (same site + username, card number, note, identity) that moves extras to the trash; encrypted export with its own password; plain CSV export in NordPass's layout behind a warning and the master password (formula-safe); global quick search shortcut (Ctrl+Shift+Space, rebindable, left off if taken); Ctrl+F; 2FA code fill from the extension (one-time-code boxes, split digit boxes, the key icon and a 2FA button in the popup); reuse warnings in the password box |
| **1.4** | Quick search shortcut off by default (opt-in in Settings) so it never grabs a combination from games or other apps |
| **1.6** | Every kind of item: SSH keys (key type and SHA256 fingerprint from the key, RSA 3072 / 4096 made with CNG in OpenSSH format, Ed25519 paste-only), API credentials, software licences, bank accounts, passports, driving licences, Wi-Fi networks, servers / databases, memberships, crypto wallets (recovery phrase hidden, asks before showing or copying, never searched, phone copy and CSV only if you allow it); your own fields on any item (text, hidden, URL, date, 2FA with live codes, email, phone - add, rename, reorder, delete; hidden ones masked, never searched, with history); your own templates; several websites per login with the same strict matching; expiry dates with an Expiring view, a Password Health section and a reminder N days before (default 30, off switch, counts only); Duplicate; Bitwarden SSH keys, identity numbers and custom fields; vault format 2 (1.5 refuses it instead of dropping data; 1.6 upgrades format 1 and keeps unknown data) |
| **1.5** | Autofill everywhere: cards (cc-* / names / labels, split number and expiry boxes, month and year lists; the security code only on a second click or an extension setting; any https page, never http) and addresses (names, email, phone, address lines, city, postcode, country lists) in the browser; inline suggestions under the focused box (closed shadow root, titles only until you pick, keyboard, Esc, off switch); two-step sign-ins (the pick remembered per tab for 5 minutes), sign-up / change-password forms offered for saving, the 2FA code filled on the next page (same tab and site, 2 minutes, once); desktop app autofill (opt-in, no shortcut until you pick one; Apps field on logins with a running-program picker; SendInput with KEYEVENTF_UNICODE; refuses when locked, unknown program, elevated, full screen or a game running); Firefox MV3 build and its native host registration; extension 1.5.0 |

## Road to NordPass / 1Password level

Where Keys stands at 1.6 (1.5 added autofill everywhere, 1.6 every kind of item): a solid single-PC vault with browser autofill for logins, cards and addresses (Chrome, Edge, Brave, Firefox), desktop app autofill, Password Health, 2FA codes and a read-only phone copy. Already matched: password history, trash, tags, duplicate finder, exports, quick access shortcut, 2FA fill, inline suggestions, two-step sign-ins, strength meter and reuse warnings, leaked-password check.

What NordPass and 1Password still do better, and the plan to close it. Hallow Keys has no server and no accounts, so everything below either works on this PC or travels through things you already own (a cloud folder, Tailscale, Hallow Remote).

### 1.5 - Autofill everywhere (done in 1.5)
- Done: cards and identities filled in the browser (checkout and address forms); the card's CVC only after a click
- Done: inline suggestions under the field (1Password style), not only the key icon and popup
- Done: two-step logins (username page, then password page), save after "sign up", 2FA box filled right after the password
- Done: **desktop app autofill** (like 1Password's Universal Autofill): a shortcut types the login into the focused Windows app, matched by its program (Steam, Battle.net, Riot Client, Epic, EA app, Ubisoft Connect). Opt-in, never into a game window
- Done: Firefox extension (needs a free Mozilla add-on signature: one step for you - see README)

### 1.6 - Every kind of item (done in 1.6)
- Done: item types like 1Password's: SSH key, API key, software licence, bank account, passport / driving licence, Wi-Fi network, server / database, membership, crypto wallet (seed phrase hidden by default)
- Done: custom fields of any type (text, hidden, URL, date, 2FA, email, phone), your own item templates, several URLs per login
- Done: expiry dates (cards, passports, licences) with reminders

### 1.7 - Watchtower-level health
- "2FA available": sites that support 2FA where the login has none (the public 2fa.directory list, downloaded and cached, no key)
- Breached sites: "this site was breached after you last changed this password" (Have I Been Pwned's free breach list, no key)
- Unsecured (http) sites, expiring items and old passwords next to weak / reused / leaked, with one security score and the fixes in order

### 2.0 - Sync between your devices (no Hallow server)
- The vault in a folder you choose (OneDrive, Google Drive, Dropbox, a NAS, Syncthing); several PCs open the same vault
- A per-item change log, so edits on two PCs merge instead of one winning; real conflicts kept as copies
- **Secret Key** like 1Password's: a random 128-bit key on each device, mixed with the master password and printed on the Emergency Kit, so a stolen cloud copy can't be guessed offline
- Argon2id for the master password (memory-hard, checked against RFC 9106 test vectors) alongside PBKDF2

### 2.1 - Vaults and Travel Mode
- Several vaults (Personal, Gaming, Work, Shared), moving items between them, export per vault
- Travel Mode (1Password): vaults not marked "safe for travel" disappear from this PC until you turn it off

### 2.2 - Passkeys
- The extension answers websites' passkey requests (WebAuthn create / get). The keys (ES256) live in the vault and sign in the app, never in the page
- Tested against a local WebAuthn test server first, then real sites (webauthn.io, GitHub, Google) by hand
- Later: Windows 11's own third-party passkey provider support, once it's generally available

### 2.3 - Sharing (no server)
- Share one item as an encrypted file or a Hallow Remote link with its own passphrase and an expiry (like 1Password's item sharing)
- A family vault in a shared cloud folder: each person runs their own Keys, and items are encrypted to each member's public key (ECDH P-256 + AES-GCM)

### 2.4 - Emergency access
- Emergency Kit (recovery code, Secret Key and how to restore) as a printable sheet
- Trusted-contact package: an encrypted copy only your contact's Keys can open. NordPass's "release after a waiting period" needs a server, so that part isn't possible here

### 2.5 - Developer tools
- SSH agent (like 1Password's): SSH keys from the vault over Windows' OpenSSH agent pipe, each use approved with a prompt or Windows Hello; git commit signing
- A command line (`HallowKeys.exe --get`) to put secrets into scripts and .env files, approved per use

### 2.6 - Phone, properly
- The phone page in Hallow Remote becomes a full editor (add, edit, generate) that syncs back to the PC
- A native Android app with system-wide autofill is **not buildable here** (needs Android Studio and Play signing); an iPhone app needs a Mac

### Opt-in extras that need your own key
- Email breach monitoring (a paid Have I Been Pwned API key)
- Masked email addresses (your own SimpleLogin or addy.io API key), like NordPass's and 1Password's email masking

### What can't be matched
- An independent security audit and a bug bounty (NordPass and 1Password pay for these). Keys publishes its threat model and test suite instead
- Account recovery by a company, a business admin console, cloud backups run by someone else
- Native phone autofill on Android and iPhone (see 2.6)

## Next

### Still to check by hand
- 1.6: the new editors with real typing and clicks (tested with `--snap` pictures and the code behind them), the "show the recovery phrase?" question, a real expiry reminder balloon (tested with the notification logged instead of shown), and the phone page in Hallow Remote showing the new types (the phone copy's contents are tested; Remote itself wasn't changed)
- Desktop app autofill typing into a real Steam / Battle.net / Riot / Epic / EA / Ubisoft sign-in box with real key presses (the matching and every refusal are tested with a pretend program in front and a stand-in that only records what it would type; real SendInput was never run in the tests, so nothing could land in a game), and the chooser window
- Cards, addresses, inline suggestions, two-step sign-ins and the 2FA-after-password fill on real shops and sites in a real browser (tested in headless Edge on made-up pages and with the real background script against the real app)
- The Firefox build in a real Firefox, and signing it on addons.mozilla.org (needs your Mozilla account)
- The quick search shortcut with real key presses, and rebinding it in Settings (tested by posting Windows' hotkey message, and with another program holding the combination)
- The extension's 2FA fill on real sites (tested in headless Edge with made-up 2FA pages and a fake extension client)
- The real Have I Been Pwned API (tested with a local stand-in) and the phone copy on a real phone over Remote's HTTPS with a real Tailscale certificate (tested with WebCrypto in Node and a self-signed certificate)
- The extension in a real Chrome / Edge / Brave on real sites (tested with a fake extension client and a local test page)
- Windows Hello's real prompt (tested with the prompt stubbed)
- Importing a real NordPass export (tested with a made-up file in NordPass's column layout)
- Printing the recovery sheet
