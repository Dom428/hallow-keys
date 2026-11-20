# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |
| **1.1** | Browser autofill: Manifest V3 extension (Chrome, Edge, Brave) with a fixed ID; native messaging host (HallowKeys.exe, pinned to that ID, per-user HKCU registration) relaying to the running app over a pipe with a per-session secret; exact-host or registrable-domain matching, no http for https logins, fill only on click into visible fields in the top / same-origin frames; save and update offers after a login form is sent |
| **1.3** | Password history (last 10 per login with dates, show / copy / clear); trash with 30-day restore (purged on unlock); tags (sidebar, editor, `#tag` search) and multi-word ranked search; duplicate finder (same site + username, card number, note, identity) that moves extras to the trash; encrypted export with its own password; plain CSV export in NordPass's layout behind a warning and the master password (formula-safe); global quick search shortcut (Ctrl+Shift+Space, rebindable, left off if taken); Ctrl+F; 2FA code fill from the extension (one-time-code boxes, split digit boxes, the key icon and a 2FA button in the popup); reuse warnings in the password box |
| **1.4** | Quick search shortcut off by default (opt-in in Settings) so it never grabs a combination from games or other apps |
| **1.2** | Check for leaked passwords (HIBP range API with Add-Padding, 5-character SHA-1 prefix only, on click or weekly opt-in) in Password Health; opt-in phone copy (AES-256-GCM, own PBKDF2-SHA256 key from the master password) served by Hallow Remote 1.10 over HTTPS and opened on the phone with WebCrypto |

## Gaps vs NordPass

| NordPass has | Hallow Keys |
|---|---|
| Password history per item | **1.3** |
| Trash with restore | **1.3** (30 days) |
| Tags / better search | **1.3** |
| Duplicate finder | **1.3** (NordPass doesn't have one; it's the obvious companion to Password Health) |
| Encrypted and CSV export | **1.3** |
| Quick access shortcut | **1.3** (Ctrl+Shift+Space, rebindable; opt-in since 1.4) |
| 2FA codes filled by the browser extension | **1.3** |
| Password strength while you type | 1.0 (1.3 adds reuse warnings) |
| Breach check for passwords | 1.2 |
| Phone access | 1.2 (read-only, through Hallow Remote) |
| Passkeys | Not planned for now (needs WebAuthn in the extension - big and risky to get wrong) |
| Sharing items with other people | Not planned (needs accounts and a server) |
| Email breach monitoring | Later (needs a paid Have I Been Pwned API key) |
| Sync between devices / cloud | Not planned (everything stays on this PC; use encrypted backups) |
| Autofill of cards and identities in the browser | Later |

## Next

### Still to check by hand
- The quick search shortcut with real key presses, and rebinding it in Settings (tested by posting Windows' hotkey message, and with another program holding the combination)
- The extension's 2FA fill on real sites (tested in headless Edge with made-up 2FA pages and a fake extension client)
- The real Have I Been Pwned API (tested with a local stand-in) and the phone copy on a real phone over Remote's HTTPS with a real Tailscale certificate (tested with WebCrypto in Node and a self-signed certificate)
- The extension in a real Chrome / Edge / Brave on real sites (tested with a fake extension client and a local test page)
- Windows Hello's real prompt (tested with the prompt stubbed)
- Importing a real NordPass export (tested with a made-up file in NordPass's column layout)
- Printing the recovery sheet

### Later
- Email breach search (needs a paid Have I Been Pwned API key)