HALLOW KEYS - your passwords, on your PC

Not audited by security experts: keep using a tried-and-tested password manager for anything that
matters, and keep backups.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)
4. Pick a master password and print your Emergency Kit (or click "Open a synced vault" if you use Keys on another PC).

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