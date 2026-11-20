# Builds the Firefox version of the extension: the same files as extension\, with firefox\manifest.json (gecko ID, background
# scripts instead of a service worker, no Chrome "key"). Usage: tools\make-firefox.ps1 <output folder>
param([Parameter(Mandatory)] [string]$Out)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem "$root\extension" | Where-Object Name -ne 'manifest.json' | Copy-Item -Destination $Out -Recurse
Copy-Item "$root\firefox\manifest.json" $Out
$a = (Get-Content "$root\extension\manifest.json" -Raw | ConvertFrom-Json).version; $b = (Get-Content "$root\firefox\manifest.json" -Raw | ConvertFrom-Json).version
if ($a -ne $b) { throw "extension version $a but Firefox manifest version $b" }
"Firefox extension $b -> $Out"