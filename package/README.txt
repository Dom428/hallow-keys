HALLOW KEYS - your passwords, on your PC

Not audited by security experts: keep using a tried-and-tested password manager for anything that
matters, and keep backups.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)
4. Pick a master password and print your recovery sheet.

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