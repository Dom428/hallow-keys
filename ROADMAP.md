# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |
| **1.1** | Browser autofill: Manifest V3 extension (Chrome, Edge, Brave) with a fixed ID; native messaging host (HallowKeys.exe, pinned to that ID, per-user HKCU registration) relaying to the running app over a pipe with a per-session secret; exact-host or registrable-domain matching, no http for https logins, fill only on click into visible fields in the top / same-origin frames; save and update offers after a login form is sent |

## Next

### 1.2 - Leak check and phone
- Check for leaked passwords (Have I Been Pwned, only a 5-character hash prefix leaves the PC)
- A read-only phone view through Hallow Remote (needs Remote's HTTPS)

### Still to check by hand
- The extension in a real Chrome / Edge / Brave on real sites (tested with a fake extension client and a local test page)
- Windows Hello's real prompt (tested with the prompt stubbed)
- Importing a real NordPass export (tested with a made-up file in NordPass's column layout)
- Printing the recovery sheet

### Later
- Email breach search (needs a paid Have I Been Pwned API key)