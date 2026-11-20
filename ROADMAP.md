# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |
| **1.1** | Browser autofill: Manifest V3 extension (Chrome, Edge, Brave) with a fixed ID; native messaging host (HallowKeys.exe, pinned to that ID, per-user HKCU registration) relaying to the running app over a pipe with a per-session secret; exact-host or registrable-domain matching, no http for https logins, fill only on click into visible fields in the top / same-origin frames; save and update offers after a login form is sent |
| **1.2** | Check for leaked passwords (HIBP range API with Add-Padding, 5-character SHA-1 prefix only, on click or weekly opt-in) in Password Health; opt-in phone copy (AES-256-GCM, own PBKDF2-SHA256 key from the master password) served by Hallow Remote 1.10 over HTTPS and opened on the phone with WebCrypto |
| **1.3** | Password history (last 10 per login with dates, show / copy / clear); trash with 30-day restore (purged on unlock); tags (sidebar, editor, `#tag` search) and multi-word ranked search; duplicate finder (same site + username, card number, note, identity) that moves extras to the trash; encrypted export with its own password; plain CSV export in NordPass's layout behind a warning and the master password (formula-safe); global quick search shortcut (Ctrl+Shift+Space, rebindable, left off if taken); Ctrl+F; 2FA code fill from the extension (one-time-code boxes, split digit boxes, the key icon and a 2FA button in the popup); reuse warnings in the password box |
| **1.4** | Quick search shortcut off by default (opt-in in Settings) so it never grabs a combination from games or other apps |
| **1.7** | Watchtower-level health: one security score (0-100, formula on the page) and "Fix these first" with one-click actions (the site's /.well-known/change-password page, a new generated password in the editor, the site's 2FA instructions, switch to https); breached sites since the password was set (Have I Been Pwned's public breach list), 2FA available but not set up (2fa.directory's public TOTP list) - both downloaded whole on click or weekly if opted in (off by default), cached in the data folder and matched locally by registrable domain, nothing about your logins sent; unsecured http addresses (your own network's devices left alone); expired / expiring items; old passwords after a period you pick (1 year by default) |
| **2.0** | Sync between your devices, no Hallow server: the vault in a folder you choose (OneDrive, Google Drive, Dropbox, NAS, Syncthing) with a local copy per PC; per-item version vectors, content hashes and tombstones, merged item by item (concurrent edits kept as a deterministic "conflict copy", edit beats delete); cloud clients' conflict files merged in; FileSystemWatcher + every minute, atomic temp-and-replace writes retried while the file is held, never written over a half-copied file; Secret Key (128-bit, DPAPI on each PC, HKDF-SHA256 with the Argon2id key) and Emergency Kit; Argon2id (RFC 9106, own C# code, RFC and reference vectors) for new vaults, 1.x PBKDF2 vaults offered the upgrade; open-a-synced-vault screen for a new PC; recovery code opens without the Secret Key; Restore an older version (30 encrypted snapshots); vault format 3 (1.7 refuses it, 1.7 file kept as .format2.bak); sync state in status.json |
| **2.2** | Every browser: the native messaging host registered for Opera, Opera GX, Vivaldi and Arc (through Chrome's key, which they read), Chromium and Yandex (their own keys, only when installed) next to Chrome, Edge, Brave and Firefox (Firefox, Edge and Brave keys now only when installed or already there); install detection by program folders, Windows' browser list and Arc's Store package; a browser table on the Browser page (installed, connected, pointing at this copy, extensions page); per-browser setup and store-publishing steps in the README; store zips (no fixed key, forward-slash paths), listing and privacy policy drafts in the package |
| **2.3** | Passkeys: the extension (1.6) wraps WebAuthn create / get for passkey requests on https pages (top page and same-origin frames, never cross-origin frames) and shows Hallow Keys' own prompt (save / pick / the browser's own as a fallback; locked: unlock and carry on); passkey autofill (mediation "conditional") in the suggestions under username boxes; the app checks https, the rpId (own host or a registrable parent, never a public suffix or IP) and builds clientDataJSON from the frame's address; ES256 keys from CNG kept only in the encrypted vault, signing in the app; attestation "none", own AAGUID, flags UP UV BE BS, sign count 0, DER signatures, getPublicKey / getAuthenticatorData / getPublicKeyAlgorithm / getTransports / toJSON; Windows Hello first when that unlock is on; Passkey item type and Passkeys tab, listed with the login for the same site, synced, trash, encrypted export (not CSV, not the phone copy); tested end to end in headless Edge against a local relying party with its own verification code (WebCrypto and Node crypto cross-checked) |
| **2.1** | NordPass look, plus vaults: a new main screen (search bar with a Ctrl+F hint, big title with Create item, category tabs All items / Logins / Secure notes / Cards / Contact info / Documents / Other, a sortable table with Title (site icon or letter tile + username), Last used, Folder and vault, Modified; tick boxes and Ctrl / Shift / Space / Ctrl+A for bulk move to folder or vault, tag, favourite, trash; details panel on the right; Up/Down, Delete, Esc); sidebar with Vault, Favourites, Trash, Folders with a + and the tools at the bottom; Last used (fills, copies, opening the site) inside the encrypted vault, merged by the latest time and never a revision; opt-in site icons fetched only from each login's own https site (public names and addresses only, same-site redirects, no cookies, size and type limits) cached encrypted with a key from the vault key; Document type; several vaults (Personal, Gaming, Work, Shared: make, rename, look, delete to the trash, restore, move items, export per vault) inside the one encrypted file, merged setting by setting in sync; Travel Mode per PC (vaults not safe for travel hidden from the window, extension, desktop autofill, phone copy, exports, backups and Password Health; turning it off needs the master password; hide-only, said plainly); vault format 4 (2.0 refuses it and its sync waits; the 2.0 file kept as .format3.bak) |
| **1.6** | Every kind of item: SSH keys (key type and SHA256 fingerprint from the key, RSA 3072 / 4096 made with CNG in OpenSSH format, Ed25519 paste-only), API credentials, software licences, bank accounts, passports, driving licences, Wi-Fi networks, servers / databases, memberships, crypto wallets (recovery phrase hidden, asks before showing or copying, never searched, phone copy and CSV only if you allow it); your own fields on any item (text, hidden, URL, date, 2FA with live codes, email, phone - add, rename, reorder, delete; hidden ones masked, never searched, with history); your own templates; several websites per login with the same strict matching; expiry dates with an Expiring view, a Password Health section and a reminder N days before (default 30, off switch, counts only); Duplicate; Bitwarden SSH keys, identity numbers and custom fields; vault format 2 (1.5 refuses it instead of dropping data; 1.6 upgrades format 1 and keeps unknown data) |
| **1.5** | Autofill everywhere: cards (cc-* / names / labels, split number and expiry boxes, month and year lists; the security code only on a second click or an extension setting; any https page, never http) and addresses (names, email, phone, address lines, city, postcode, country lists) in the browser; inline suggestions under the focused box (closed shadow root, titles only until you pick, keyboard, Esc, off switch); two-step sign-ins (the pick remembered per tab for 5 minutes), sign-up / change-password forms offered for saving, the 2FA code filled on the next page (same tab and site, 2 minutes, once); desktop app autofill (opt-in, no shortcut until you pick one; Apps field on logins with a running-program picker; SendInput with KEYEVENTF_UNICODE; refuses when locked, unknown program, elevated, full screen or a game running); Firefox MV3 build and its native host registration; extension 1.5.0 |

## Road to NordPass / 1Password level

Where Keys stands at 2.3 (1.5 added autofill everywhere, 1.6 every kind of item, 1.7 Watchtower-level health, 2.0 sync between your PCs with a Secret Key and Argon2id, 2.1 NordPass's main-screen layout, several vaults and Travel Mode, 2.2 every Chromium browser, 2.3 passkeys): a solid vault that syncs between your PCs through your own cloud folder, with browser autofill for logins, cards and addresses (Chrome, Edge, Brave, Firefox), desktop app autofill, Password Health, 2FA codes and a read-only phone copy. Already matched: password history, trash, tags, duplicate finder, exports, quick access shortcut, 2FA fill, inline suggestions, two-step sign-ins, strength meter and reuse warnings, leaked-password check.

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

### 1.7 - Watchtower-level health (done in 1.7)
- Done: "2FA available": sites that support 2FA where the login has none (the public 2fa.directory list, downloaded and cached, no key)
- Done: breached sites: "this site was breached after you last changed this password" (Have I Been Pwned's free breach list, no key)
- Done: unsecured (http) sites, expiring items and old passwords next to weak / reused / leaked, with one security score and the fixes in order

### 2.0 - Sync between your devices (no Hallow server) (done in 2.0)
- Done: the vault in a folder you choose (OneDrive, Google Drive, Dropbox, a NAS, Syncthing); several PCs open the same vault
- Done: a per-item change log (version vectors, tombstones), so edits on two PCs merge instead of one winning; real conflicts kept as copies; cloud clients' conflict files merged in
- Done: **Secret Key** like 1Password's: a random 128-bit key on each device, mixed with the master password and printed on the Emergency Kit, so a stolen cloud copy can't be guessed offline
- Done: Argon2id for the master password (memory-hard, checked against RFC 9106 and the reference implementation's test vectors) alongside PBKDF2
- Done: Restore an older version (encrypted snapshots on each PC)

### 2.1 - Vaults and Travel Mode (done in 2.1)
- Done: several vaults (Personal, Gaming, Work, Shared), moving items between them (one or many), export per vault - all inside the one encrypted vault file, merged correctly by sync
- Done: Travel Mode (1Password): vaults not marked "safe for travel" disappear from this PC (list, search, extension, desktop autofill, phone copy, exports) until you turn it off with your master password. Hide-only: unlike 1Password the hidden items stay inside the encrypted file on the PC - the app says so

### NordPass main-screen comparison (done in 2.1)
- Done: **category tabs** like NordPass's (All items, Logins, Secure notes, Cards, Contact info, Documents, Other; Passkeys since 2.3)
- Done: a **Documents** tab (a Document item type, plus every item with files attached)
- Done: **Last used** column (filled, copied or opened), sortable with Title, Folder and Modified; tick boxes for bulk actions; Create item; Ctrl+F hint; folders with a +
- Done: **site icons** (opt-in, fetched only from each login's own site, cached encrypted) with coloured letter tiles otherwise
- Not copied: Shared items (needs 2.4 sharing), Email Masking and Data Breach Scanner (need your own API keys - see "Opt-in extras")

### 2.2 - Every browser (done in 2.2)
- Done: Opera, Opera GX, Vivaldi, Arc, Chromium and Yandex next to Chrome, Edge, Brave and Firefox - the native host registered where each browser actually reads it (Chrome's key always, serving Opera, Opera GX, Vivaldi and Arc; Chromium's, Yandex's, Edge's, Brave's and Firefox's own keys only when installed)
- Done: the Browser page shows every browser - installed, connected (and pointing at this copy), its extensions page; other Chromium browsers Windows lists are named too
- Done: setup steps per browser in the README; ready-to-upload store zips, a draft listing and privacy policy in the package (publishing itself is your call - see README)

### 2.3 - Passkeys (done in 2.3)
- Done: the extension answers websites' passkey requests (WebAuthn create / get) with Hallow Keys' own prompt, and passkey autofill under username boxes. The keys (ES256) live in the vault and sign in the app, never in the page
- Done: tested against a local WebAuthn relying party (own verification code) in headless Edge; real sites (webauthn.io, GitHub, Google) still to try by hand
- Not yet: importing passkeys from other managers (FIDO's Credential Exchange Format isn't final); EdDSA / RSA keys (ES256 covers the sites seen so far)
- Later: Windows 11's own third-party passkey provider support, once it's generally available

### 2.4 - Sharing (no server)
- Share one item as an encrypted file or a Hallow Remote link with its own passphrase and an expiry (like 1Password's item sharing)
- A family vault in a shared cloud folder: each person runs their own Keys, and items are encrypted to each member's public key (ECDH P-256 + AES-GCM)

### 2.5 - Emergency access
- Done early (2.0): Emergency Kit (recovery code, Secret Key and how to restore) as a printable sheet
- Trusted-contact package: an encrypted copy only your contact's Keys can open. NordPass's "release after a waiting period" needs a server, so that part isn't possible here

### 2.6 - Developer tools
- SSH agent (like 1Password's): SSH keys from the vault over Windows' OpenSSH agent pipe, each use approved with a prompt or Windows Hello; git commit signing
- A command line (`HallowKeys.exe --get`) to put secrets into scripts and .env files, approved per use

### 2.7 - Phone, properly
- The phone page in Hallow Remote becomes a full editor (add, edit, generate) that syncs back to the PC
- A native Android app with system-wide autofill is **not buildable here** (needs Android Studio and Play signing); an iPhone app needs a Mac

### Opt-in extras that need your own key
- Email breach monitoring (a paid Have I Been Pwned API key)
- Masked email addresses (your own SimpleLogin or addy.io API key), like NordPass's and 1Password's email masking

### What can't be matched
- An independent security audit and a bug bounty (NordPass and 1Password pay for these). Keys publishes its threat model and test suite instead
- Account recovery by a company, a business admin console, cloud backups run by someone else
- Native phone autofill on Android and iPhone (see 2.7)

## Next

### Still to check by hand
- 2.3: passkeys on real sites (webauthn.io, GitHub, Google, Microsoft) in a real Chrome / Edge / Brave / Firefox with the extension reloaded; Windows Hello's real prompt before a passkey (tested with the prompt stubbed); the prompt and the suggestions with a real mouse (tested with DevTools' trusted clicks in headless Edge); Firefox's MAIN-world content script (not installed here)
- 2.2: Opera, Opera GX, Vivaldi, Arc, Chromium, Yandex and Firefox with the extension loaded (not installed on this PC - tested at the registry level: which keys are written, install detection with made-up program folders, status); Edge and Brave load the unpacked extension headless with its fixed ID; Chrome (installed) no longer allows command-line extension loading, so it was checked at the registry level too
- 2.1: the new main screen with real clicks and keys (tick boxes, Shift/Ctrl+click, sorting, the bulk menus, Up/Down from the search box, the vault switcher's menu, the Look menu) - checked with `--snap` pictures and the code behind them; Travel Mode's on/off questions and the master-password prompt (the filtering itself is tested against the running app with a fake extension, the phone copy and status); site icons from real sites (tested against a local HTTPS stand-in with every rule, plus one real fetch to check a real icon's format); vaults syncing through a real cloud folder between two real PCs (tested with 2 and 3 simulated PCs and a 400-step stress run)
- 2.0: sync through a real OneDrive / Google Drive / Dropbox / Syncthing folder between two real PCs (tested with two simulated PCs - separate data folders - and a temp "cloud" folder, including held files, half-written files and every cloud client's conflict-file naming), the folder pickers, printing the Emergency Kit, typing a Secret Key on a second PC, Upgrade protection on your real vault (tested on a real 1.7 vault made by the 1.7 code), and Argon2id's unlock time on a slower PC (about 0.5 s here)
- 1.7: the real downloads from Have I Been Pwned and 2fa.directory inside the app (their real formats were checked once and the app was tested against a local stand-in), and the one-click actions opening real sites' change-password and 2FA pages
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
