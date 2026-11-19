# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|

## Next

### 1.0 - Vault
- Logins, secure notes, cards, identities, folders, favourites, search, small file attachments (encrypted)
- Generator (characters or passphrases), Password Health (weak, reused, old), built-in 2FA codes (TOTP)
- Import from NordPass (CSV), any CSV, Bitwarden (JSON); encrypted backup; recovery sheet; master password change
- AES-256-GCM vault with a PBKDF2 master key, auto-lock, clipboard clearing, optional Windows Hello

### 1.1 - Browser autofill
- Extension for Chrome, Edge and Brave, talking to the running app through native messaging; strict site matching; fill only when you click; offer to save new logins

### 1.2 - Leak check and phone
- Check for leaked passwords (Have I Been Pwned, only a 5-character hash prefix leaves the PC)
- A read-only phone view through Hallow Remote (needs Remote's HTTPS)

### Later
- Email breach search (needs a paid Have I Been Pwned API key)