# Store listing draft - Hallow Keys (browser extension)

Use for the Chrome Web Store, Microsoft Edge Add-ons, Opera add-ons and addons.mozilla.org. Nothing has been submitted.

**Name:** Hallow Keys

**Short description (132 characters max):**
Fill logins, 2FA codes, cards and addresses from Hallow Keys, the password manager that keeps your vault on your own PC.

**Category:** Productivity (Chrome) / Privacy & Security (Firefox)

**Language:** English

**Full description:**
Hallow Keys is a password manager for Windows that keeps everything in one encrypted file on your own PCs - no account and no
Hallow server. This extension connects your browser to the Hallow Keys app running on the same PC.

- Fills logins only when you pick one: in the suggestions under the box you click, the key icon or the toolbar button
- Logins only on the site they were saved for (exact site or the same main domain; never look-alikes, never an https login on http)
- 2FA codes, cards (the security code only after a second click) and addresses on secure (https) pages
- Offers to save new logins and changed passwords after you sign in or sign up
- Never fills hidden boxes or boxes inside other sites' frames

Requires the free Hallow Keys app for Windows (https://github.com/Dom428/hallow-keys), running and unlocked. Without it the extension
does nothing.

Not audited by security experts: a personal project. Keep a tried-and-tested password manager for anything that matters.

**Single purpose (Chrome):** Fill and save passwords and other items from the Hallow Keys desktop app into web pages.

**Permission justifications (Chrome):**
- nativeMessaging: talks to the Hallow Keys app on this PC (its native messaging host); this is the only place items come from.
- storage: remembers the extension's own settings (suggestions on/off, fill the security code) and a short-lived note of the login you
  picked during a two-step sign-in.
- activeTab: fills the tab you clicked the toolbar button in.
- Content script on http/https pages: finds sign-in, card and address boxes and shows the suggestions; nothing is read from pages
  except the boxes' names and labels, and nothing is sent anywhere but the app on this PC.

**Data use (Chrome privacy practices):** The extension handles passwords, card details and addresses ("Authentication information",
"Financial and payment information", "Personally identifiable information") only to fill them into the page you choose and to pass
new logins to the app on your PC. Nothing is sold, sent to any server or used for anything else.

**Screenshots needed (1280x800 or 640x400, PNG/JPEG, 1-5):** the suggestions under a sign-in box, the toolbar popup with a matched login,
a card being filled on a checkout page, the save-login offer - taken on made-up test pages with made-up items only.

**Small promo tile (Chrome, 440x280):** the Hallow Keys icon and name on the dark background.

**Support / homepage URL:** https://github.com/Dom428/hallow-keys
**Privacy policy URL:** publish PRIVACY.md (for example as a page on https://dom428.github.io/hallow/) and use that address.