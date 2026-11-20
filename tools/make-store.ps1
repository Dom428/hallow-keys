# Builds the store uploads (nothing is uploaded): package\store\
#   hallow-keys-chrome-web-store.zip  - the extension for the Chrome Web Store (and Edge Add-ons / Opera add-ons), manifest without the
#                                       "key" (stores give their own ID), files at the zip's root
#   hallow-keys-firefox-addons.zip    - the Firefox build for addons.mozilla.org (gecko ID kept)
#   LISTING.md, PRIVACY.md            - draft store listing text and privacy policy (from store\)
# Usage: tools\make-store.ps1 <package folder>   (run after the Firefox build exists in <package>\extension-firefox)
param([Parameter(Mandatory)] [string]$Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot; $out = Join-Path $Package 'store'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $out | Out-Null
$tmp = Join-Path $out 'chrome'; New-Item -ItemType Directory -Force $tmp | Out-Null
Get-ChildItem "$root\extension" | Where-Object Name -ne 'manifest.json' | Copy-Item -Destination $tmp -Recurse
$m = Get-Content "$root\extension\manifest.json" -Raw | ConvertFrom-Json; $m.PSObject.Properties.Remove('key')
[IO.File]::WriteAllText((Join-Path $tmp 'manifest.json'), ($m | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
# zip with forward slashes in the entry names (the stores reject Windows-style paths that Compress-Archive writes)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function Zip($from, $to) {
    $z = [IO.Compression.ZipFile]::Open($to, 'Create')
    try { foreach ($file in Get-ChildItem $from -Recurse -File) { $rel = $file.FullName.Substring((Resolve-Path $from).Path.TrimEnd('\').Length + 1).Replace('\', '/'); [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $file.FullName, $rel) } }
    finally { $z.Dispose() }
}
Zip $tmp (Join-Path $out 'hallow-keys-chrome-web-store.zip'); Remove-Item $tmp -Recurse -Force
Zip (Join-Path $Package 'extension-firefox') (Join-Path $out 'hallow-keys-firefox-addons.zip')
Copy-Item "$root\store\LISTING.md", "$root\store\PRIVACY.md" $out
"store files -> $out (extension $($m.version))"