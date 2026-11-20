# Hallow Keys uninstaller (run from Windows Settings > Apps, or directly). Your vault is NOT deleted.
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Keys'
$data = Join-Path $env:APPDATA 'HallowKeys'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Keys?`n`nYour vault stays in $data - delete that folder yourself if you're sure you don't need it (keep a backup first).", 'Uninstall Hallow Keys', 'YesNo', 'Question') -ne 'Yes') { return }
Get-Process HallowKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowKeys' -ErrorAction SilentlyContinue
foreach ($b in 'Software\Google\Chrome', 'Software\Microsoft\Edge', 'Software\BraveSoftware\Brave-Browser', 'Software\Mozilla') { Remove-Item "HKCU:\$b\NativeMessagingHosts\com.hallow.keys" -Recurse -ErrorAction SilentlyContinue }
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Keys.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowKeys' -Recurse -ErrorAction SilentlyContinue
foreach ($f in 'hello.bin', 'session.key', 'native-host.json', 'native-host-firefox.json') { Remove-Item (Join-Path $data $f) -ErrorAction SilentlyContinue }
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show("Hallow Keys is uninstalled. Your vault is still in $data.", 'Hallow Keys') | Out-Null