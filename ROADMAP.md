# Hallow Keys roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Vault (AES-256-GCM via CNG, PBKDF2-SHA256 600k, envelope with a data key, atomic saves with .bak), logins / notes / cards / identities, folders, favourites, search, encrypted files, generator, Password Health, TOTP, NordPass / CSV / Bitwarden import, encrypted backup, recovery sheet, master password change, auto-lock, clipboard clearing, optional Windows Hello |
| **1.1** | Browser autofill: Manifest V3 extension (Chrome, Edge, Brave) with a fixed ID; native messaging host (HallowKeys.exe, pinned to that ID, per-user HKCU registration) relaying to the running app over a pipe with a per-session secret; exact-host or registrable-domain matching, no http for https logins, fill only on click into visible fields in the top / same-origin frames; save and update offers after a login form is sent |
| **1.2** | Check for leaked passwords (HIBP range API with Add-Padding, 5-character SHA-1 prefix only, on click or weekly opt-in) in Password Health; opt-in phone copy (AES-256-GCM, own PBKDF2-SHA256 key from the master password) served by Hallow Remote 1.10 over HTTPS and opened on the phone with WebCrypto |

## Next

### Still to check by hand
- The real Have I Been Pwned API (tested with a local stand-in) and the phone copy on a real phone over Remote's HTTPS with a real Tailscale certificate (tested with WebCrypto in Node and a self-signed certificate)
- The extension in a real Chrome / Edge / Brave on real sites (tested with a fake extension client and a local test page)
- Windows Hello's real prompt (tested with the prompt stubbed)
- Importing a real NordPass export (tested with a made-up file in NordPass's column layout)
- Printing the recovery sheet

### Later
- Email breach search (needs a paid Have I Been Pwned API key)