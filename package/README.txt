HALLOW KEYS - your passwords, on your PC

Not audited by security experts: keep using a tried-and-tested password manager for anything that
matters, and keep backups.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)
4. Pick a master password and print your Emergency Kit (or click "Open a synced vault" if you use Keys on another PC).

WHAT'S NEW IN 2.11 - HALLOW AUTHENTICATOR
- Move (or copy) your 2FA codes to Hallow Authenticator on your phone: Settings > Hallow Authenticator > Send my 2FA
  codes to my phone. Scan the code Keys shows with the app (needs Hallow Remote 1.19 with HTTPS). End-to-end sealed;
  after a move Keys stops filling those codes and shows "On your phone" - password and code on different devices.
- Optional PC mirror (off by default): read-only codes the phone sends, shown only while Keys is unlocked.
- Lost both the master password and the recovery code? The lock screen has "Lost both? Start over": type ERASE and
  the old vault is moved (not deleted) into %APPDATA%HallowKeysabandoned-<date>, sync / Windows Hello / phone copy
  are turned off (your sync folder is left alone), and you create a new vault.

WHAT'S NEW IN 2.10 - LIGHT MODE READABILITY FIXES
- In the light look text reads clearly whatever your Hallow accent: buttons in a light
  accent like violet use a slightly deeper shade, the selected item's highlight is lighter so its
  grey text reads, and off switches and empty tick boxes have clearer outlines. Dark is unchanged.

WHAT'S NEW IN 2.9 - LIGHT AND DARK MODE
- A light look as well as the dark one. It follows Hallow Toolkit's switch (Light / Dark / Same as
  Windows), live; Settings > App > Look picks Light or Dark for Hallow Keys alone.

WHAT'S NEW IN 2.8 - PASSWORD HEALTH FOR HALLOW GUARD
- Hallow Guard 1.1's one safety score can include your passwords: while unlocked, Hallow Keys
  puts COUNTS ONLY in its status file (how many logins are weak, reused, leaked, on a breached
  site or missing 2FA, and the Password Health score). Never a name, address or password.
  While locked it reports the last counts and how old they are.

WHAT'S NEW IN 2.7 - PHONE, PROPERLY
- With Hallow Remote 1.12, the phone's Keys tab can edit: turn on "Let the phone edit" (Settings > App, under the phone
  copy; off by default). Add, edit, move to the trash, generate passwords, 2FA keys, folders and vaults.
- Each change is sealed on the phone with your phone key and checked by Hallow Keys before anything changes. If the item
  changed on the PC meanwhile, the phone's version is kept as a "(conflict copy from phone)". Passkeys, SSH keys and
  wallets stay view-only. The PC has to be unlocked.
- Settings > Activity lists every phone change; "Undo phone changes" restores the snapshot from just before them.
- Share > As a one-time Hallow Remote link: opens once, in a browser, with a passphrase.

WHAT'S NEW IN 2.6 - DEVELOPER TOOLS
- Settings > Developer tools (both off until you turn them on, only while Hallow Keys is unlocked):
- SSH agent: tick the SSH keys to offer; Windows' ssh, ssh-add, ssh-keygen and git find them on Windows' standard agent pipe.
  Every use asks first (a small prompt naming the program and key, optional "don't ask again for N minutes", or Windows Hello).
  Ed25519, RSA and ECDSA keys in OpenSSH format without a passphrase. If Windows' OpenSSH Authentication Agent service is
  running, Hallow Keys uses its own pipe instead and shows the line to point ssh at it (it never changes the service).
- Git: git config --global core.sshCommand C:/Windows/System32/OpenSSH/ssh.exe ; for signed commits also gpg.format ssh,
  gpg.ssh.program C:/Windows/System32/OpenSSH/ssh-keygen.exe, user.signingkey "key::<public key line>", commit.gpgsign true.
- Command line (hk.exe in Hallow Keys' folder): hk --get "<item>" [--field ...], hk --env <template> [--out <new file>]
  ({{hk:item/field}} placeholders), hk --run -- <command> (variables set to hk:item/field are filled in). Each use asks first.
  Exit codes: 0 ok, 1 usage, 2 not running, 3 locked, 4 not found, 5 not allowed, 6 turned off, 7 other.

WHAT'S NEW IN 2.5 - EMERGENCY ACCESS
- The Emergency Kit is a full sheet now: Secret Key, recovery code, blanks to fill in, and plain-language restore steps.
- Emergency packages (Sharing > Emergency access): an encrypted copy of the vaults you pick that only a trusted contact's
  Hallow Keys can open - and, if you tick it (on by default), only together with your recovery code. They open it with
  "Open an emergency package". It's a copy as of today: make a new one now and then.
- Not possible without a server: "release to my contact after N days unless I say no". Hallow Keys has no server.

WHAT'S NEW IN 2.4 - SHARING (NO SERVER)
- New Sharing page. Your member card (a short code with a fingerprint) is what others need to add you.
- Shared vaults: one file in a folder you share with family or a team (shared OneDrive/Dropbox folder, NAS). Each member runs
  their own Hallow Keys; everything is encrypted to each member's own key. Put items in with Share > Copy into the shared vault.
- Removing a member changes the shared vault's key: they can't read anything new, but may have kept copies of what they saw.
- One item as a file: Share > As an encrypted file - locked with a passphrase, opens until the date you pick. Send the file and
  the passphrase separately. Passkeys can't be shared.

WHAT'S NEW IN 2.3 - PASSKEYS
- Hallow Keys keeps passkeys. When a site asks to make or use one, the browser extension shows Hallow Keys' prompt in the
  corner of the page: Save passkey in Hallow Keys, pick one of yours, or "Use another device or the browser's own".
  Sites with passkey autofill get your passkeys at the top of the suggestions under the username box.
- RELOAD THE EXTENSION after updating (it's 1.6 now): your browser's extensions page > Hallow Keys > Reload.
- Passkeys are on the new Passkeys tab and next to the login for the same site; they sync, go to the trash and are in
  encrypted exports and backups. Their private keys never leave the app (not shown, copied, in CSV exports or the phone copy).
- Locked: the prompt offers to open Hallow Keys and carries on once it's unlocked. Windows Hello unlock on: Windows Hello is
  asked first. Importing passkeys from other password managers isn't possible yet (the standard for it isn't final).
- Update every PC that syncs to 2.3 before saving passkeys.

WHAT'S NEW IN 2.2 - EVERY BROWSER
- Connect browsers (Browser page) now also covers Opera, Opera GX, Vivaldi, Arc, Chromium and Yandex, next to Chrome, Edge,
  Brave and Firefox - set up only where each browser actually looks (Opera, Opera GX, Vivaldi and Arc use Chrome's link).
- The Browser page lists every browser: installed or not, connected or not, and its extensions page. After installing a new
  browser, click Connect browsers again.
- Load the extension in any of them: open its extensions page (chrome://, edge://, brave://, opera://, vivaldi://, arc:// or
  browser://extensions), turn on Developer mode, Load unpacked, pick the extension folder next to HallowKeys.exe.
- The store folder has ready-to-upload zips for the Chrome Web Store and Firefox Add-ons, a draft listing and privacy policy -
  nothing has been published; see the README on GitHub for what publishing needs (a developer account, Chrome's $5 fee...).

WHAT'S NEW IN 2.1 - NORDPASS LOOK, PLUS VAULTS
- A new main screen: search bar (Ctrl+F), Create item, category tabs (All items, Logins, Secure notes, Cards, Contact info,
  Documents, Other) and a table you can sort by Title, Last used, Folder and Modified. Tick boxes (or Ctrl/Shift+click, Space,
  Ctrl+A) to move several items to a folder or vault, tag, favourite or trash them at once. Details open on the right.
- Last used: when a login was filled, copied or opened on its site - kept inside your encrypted vault only.
- Site icons (off by default, Settings > Site icons): each login's icon from its own https site, never through another
  service; otherwise a coloured letter. Kept encrypted next to the vault.
- A Document item type; the Documents tab also shows items with files attached.
- Several vaults: Personal, plus your own (Gaming, Work, Shared...). Switch at the top of the sidebar, move items, export one
  vault, delete a vault to the trash (and restore it). They sync with your other PCs like everything else.
- Travel Mode (Vaults and travel): only vaults marked "Safe for travel" stay on this PC's screen, extension, autofill, phone
  copy and exports until you turn it off with your master password. It hides, it doesn't remove: the hidden items are still
  inside the encrypted vault file on this PC (unlike 1Password, which takes them off the device).
- The vault is upgraded when it's next saved; Hallow Keys 2.0 can't open it afterwards (it says so, and its sync waits instead of
  writing over it), so update every PC that syncs. The 2.0 file is kept next to it as vault.hkvault.format3.bak.

WHAT'S NEW IN 2.0 - SYNC BETWEEN YOUR PCS (NO HALLOW SERVER)
- Settings > Sync: pick a folder that OneDrive, Google Drive, Dropbox, a NAS or Syncthing keeps in step, turn sync on, and your
  PCs share one vault. Changes merge item by item; if the same item changed on two PCs, both versions are kept (one as a
  "conflict copy"). Each PC keeps its own copy and works offline. The sidebar shows "Synced 2 min ago" / "Waiting for OneDrive".
- New vaults get a Secret Key (kept on this PC, printed on the new Emergency Kit) and Argon2id for the master password, so a
  copy of your vault in the cloud can't be cracked by guessing your password. Older vaults keep working - click
  "Upgrade protection" (needed for sync) and print the Emergency Kit.
- Another PC: install Hallow Keys there, click "Open a synced vault", pick the folder, type your master password and Secret Key.
- Your recovery code still opens the vault without the Secret Key.
- Settings > Restore an older version: the last 30 encrypted snapshots on this PC.
- The vault is upgraded when it's next saved; Hallow Keys 1.7 can't open it afterwards (it says so). The 1.7 file is kept next
  to it as vault.hkvault.format2.bak.
WHAT'S NEW IN 1.7 - WATCHTOWER-LEVEL HEALTH
- Password Health has one security score (out of 100, the formula is on the page) and a "Fix these first" list with
  one-click fixes: the site's change-password page, a new password, how to add 2FA, switch to https.
- New checks: sites breached since you set the password, sites that offer 2FA you haven't set up, http addresses,
  expired / expiring items, and old passwords after a period you pick (Settings > Password Health).
- The breached-sites and 2FA lists are public lists downloaded whole - only when you click "Download the lists" (or weekly
  if you turn that on) - and checked on your PC. Nothing about your logins is ever sent.

WHAT'S NEW IN 1.6 - EVERY KIND OF ITEM
- New item types: SSH key (fingerprint worked out; Generate RSA makes a new key), API credential, software licence, bank account,
  passport, driving licence, Wi-Fi network, server / database, membership and crypto wallet (the recovery phrase stays hidden,
  asks before it's shown, isn't searched, and only goes to the phone copy or a CSV export if you allow it).
- Your own fields on any item (text, hidden, web address, date, 2FA, email, phone) and your own templates (Save as template).
- Several websites per login - the browser offers it on each one.
- Expiry dates: an Expiring page, a section in Password Health and a reminder before things run out (Settings > Expiry reminders).
- Your vault is upgraded when it's next saved; Hallow Keys 1.5 and older can't open it afterwards (they say so instead of losing
  anything), so don't go back to an older version.

WHAT'S NEW IN 1.5 - AUTOFILL EVERYWHERE
- Cards and addresses fill in the browser on https pages (a card's security code only after a second click).
- Suggestions show under the box you click into; two-step sign-ins; sign-ups offered for saving; 2FA codes fill on the next page.
- Desktop app autofill (off by default): a shortcut you pick types a login into Steam, Battle.net, Riot Client, Epic, EA app,
  Ubisoft Connect or any program you add under Apps on a login - never into games.
- Firefox: the extension-firefox folder (see FIREFOX below).
- Reload the extension once on your browser's extensions page (it's now 1.5.0).

WHAT'S NEW IN 1.4
- The quick search shortcut is off by default now - turn it on in Settings if you want it.

WHAT'S NEW IN 1.3
- Password history, a trash (30 days to restore), tags, a smarter search and a duplicate finder.
- Encrypted export with its own password, and a plain CSV export (with a strong warning).
- Quick search from anywhere with Ctrl+Shift+Space (change it in Settings).
- The browser extension fills 2FA codes - reload it once on your browser's extensions page.

WHAT'S NEW IN 1.2
- Check for leaked passwords (Password Health).
- A read-only copy on your phone through Hallow Remote (Settings > Phone copy).

WHAT'S NEW IN 1.1
- Browser autofill for Chrome, Edge and Brave (see the Browser page in Hallow Keys).

WHAT'S NEW IN 1.0
- First version.

WHAT IT DOES
- Logins, secure notes, cards and identities, with folders, tags, favourites, search and small files
- SSH keys, API credentials, software licences, bank accounts, passports, driving licences, Wi-Fi networks, servers, memberships
  and crypto wallets; your own fields and templates; expiry dates with reminders
- Quick search from anywhere with Ctrl+Shift+Space (off by default since 1.4 - turn it on in Settings)
- Password history, a trash (30 days to restore) and a duplicate finder
- A password generator, Password Health (a security score; weak, reused, old, leaked, breached sites, 2FA available, http) and 2FA codes
- Check for leaked passwords (Password Health) - only a short hash prefix leaves your PC
- A read-only copy on your phone through Hallow Remote (Settings > Phone copy; needs Remote's HTTPS)
- Import from NordPass (Settings > Export items gives a CSV), other CSV exports and Bitwarden JSON
- Encrypted backups and exports, a plain CSV export (with a warning), a recovery sheet, auto-lock and a clipboard that clears itself
- Browser autofill for Chrome, Edge, Brave and Firefox - logins, 2FA codes, cards and addresses: see the Browser page in
  Hallow Keys (the extension is in the extension folder next to HallowKeys.exe; you load it with Developer mode > Load unpacked)
- Desktop app autofill (off by default): Settings > Desktop app autofill, then add programs under Apps on a login

FIREFOX
Click Connect browsers in Hallow Keys first. To try the add-on: about:debugging > This Firefox > Load Temporary Add-on >
extension-firefox\manifest.json (it lasts until Firefox closes). To keep it, Firefox needs it signed by Mozilla - free, once,
with your own account: addons.mozilla.org/developers > Submit a New Add-on > On your own (unlisted) > upload a zip of the
CONTENTS of extension-firefox > download the signed .xpi > drag it into Firefox. Then about:addons > Hallow Keys > Permissions
> allow it on all websites.

YOUR VAULT
%APPDATA%\HallowKeys\vault.hkvault (the previous save is next to it as .bak). Uninstalling never
deletes it.

CREDITS
Icons: Font Awesome Free 6.7.2 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.