# Hallow Keys installer: copies the app to your user folder (no admin needed) and adds Start menu + Desktop shortcuts.
# Your vault (%APPDATA%\HallowKeys) is never touched by installing or updating.
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Keys'
New-Item -ItemType Directory -Force $dest | Out-Null
Get-Process HallowKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item (Join-Path $PSScriptRoot 'HallowKeys.exe') $dest -Force
foreach ($f in 'README.txt', 'Uninstall.ps1', 'FontAwesome-LICENSE.txt') { Copy-Item (Join-Path $PSScriptRoot $f) $dest -Force -ErrorAction SilentlyContinue }
foreach ($x in 'extension', 'extension-firefox') { if (Test-Path (Join-Path $PSScriptRoot $x)) { Remove-Item (Join-Path $dest $x) -Recurse -Force -ErrorAction SilentlyContinue; Copy-Item (Join-Path $PSScriptRoot $x) $dest -Recurse -Force } }
$exe = Join-Path $dest 'HallowKeys.exe'
$u = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowKeys'
New-Item $u -Force | Out-Null
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0\.0$', ''
$vals = @{ DisplayName = 'Hallow Keys'; DisplayVersion = $ver; Publisher = 'Hallow'; DisplayIcon = $exe; InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $dest 'Uninstall.ps1')`""
    URLInfoAbout = 'https://github.com/Dom428/hallow-keys' }
foreach ($k in $vals.Keys) { Set-ItemProperty $u -Name $k -Value $vals[$k] }
Set-ItemProperty $u -Name NoModify -Value 1 -Type DWord; Set-ItemProperty $u -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty $u -Name EstimatedSize -Value ([int]((Get-Item $exe).Length / 1KB)) -Type DWord
$w = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
    $s = $w.CreateShortcut((Join-Path $dir 'Hallow Keys.lnk'))
    $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.Description = 'Your passwords, on your PC'; $s.Save()
}
Start-Process $exe
Write-Host "Hallow Keys is installed and open. It's in your Start menu and on your Desktop."