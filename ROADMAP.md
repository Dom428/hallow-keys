# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |

## Next

### 1.1 - Browser autofill
- Extension for Chrome, Edge and Brave, talking to the running app through native messaging; strict site matching; fill only when you click; offer to save new logins

### 1.2 - Leak check and phone
- Check for leaked passwords (Have I Been Pwned, only a 5-character hash prefix leaves the PC)
- A read-only phone view through Hallow Remote (needs Remote's HTTPS)

### Still to check by hand
- Windows Hello's real prompt (tested with the prompt stubbed)
- Importing a real NordPass export (tested with a made-up file in NordPass's column layout)
- Printing the recovery sheet

### Later
- Email breach search (needs a paid Have I Been Pwned API key)