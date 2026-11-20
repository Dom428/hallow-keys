# Builds HallowKeys.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - nothing to install.
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"; $wm = "$env:WINDIR\System32\WinMetadata"
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'HallowKeys.exe'
$refs = "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
    "$fw\System.Runtime.Serialization.dll", "$fw\System.Core.dll", "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll", "$fw\System.Web.Extensions.dll",
    "$fw\System.Xml.dll", "$fw\System.Security.dll", "$fw\System.Numerics.dll", "$fw\System.Runtime.dll", "$fw\System.Runtime.WindowsRuntime.dll", "$fw\Microsoft.VisualBasic.dll", "$wpf\ReachFramework.dll", "$wpf\System.Printing.dll",
    "$wm\Windows.Foundation.winmd", "$wm\Windows.Security.winmd"
$args = @('/nologo', '/target:winexe', '/optimize+', "/out:$out", "/win32icon:$src\hallow.ico", "/resource:$src\Theme.xaml,HallowKeys.Theme.xaml") +
    ($refs | ForEach-Object { "/reference:$_" }) + (Get-ChildItem $src -Filter *.cs | ForEach-Object FullName)
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
"Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
# v2.6: hk.exe - the command line as a console program (the shell waits for it and gets its exit code)
$hk = Join-Path $PSScriptRoot 'hk.exe'
& "$fw\csc.exe" /nologo /target:exe /optimize+ "/out:$hk" "/win32icon:$src\hallow.ico" "/reference:$fw\System.Web.Extensions.dll" "/reference:$fw\System.Core.dll" (Join-Path $PSScriptRoot 'cli\hk.cs') "$src\CliClient26.cs"
if ($LASTEXITCODE -ne 0) { throw "hk.exe build failed" }
"Built $hk"
