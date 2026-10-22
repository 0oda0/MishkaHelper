# Installs the published exe into Program Files on this PC (run elevated).
# Usage: install-local.ps1 -Source <publish\MishkaHelper.exe> [-SelfTest <report.txt>]
param([Parameter(Mandatory)] [string]$Source, [string]$SelfTest, [string]$Only = "")
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:ProgramFiles 'Mishka Helper'
$exe = Join-Path $dir 'MishkaHelper.exe'
$log = Join-Path (Split-Path $Source) 'install.log'
"start $(Get-Date)" | Out-File $log -Encoding utf8

# Stop any running copy (old builds don't know --quit).
Get-Process MishkaHelper -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item $Source $exe -Force
"copied to $exe" | Out-File $log -Append -Encoding utf8

# Start menu shortcut
$lnk = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Mishka Helper.lnk'
$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($lnk); $s.TargetPath = $exe; $s.WorkingDirectory = $dir; $s.IconLocation = "$exe,0"; $s.Save()
"shortcut $lnk" | Out-File $log -Append -Encoding utf8

# Uninstall entry (Settings → Apps)
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MishkaHelper'
New-Item $k -Force | Out-Null
Set-ItemProperty $k DisplayName 'Mishka Helper'
Set-ItemProperty $k Publisher 'Mishka'
Set-ItemProperty $k DisplayIcon $exe
Set-ItemProperty $k DisplayVersion ((Get-Item $exe).VersionInfo.ProductVersion)
Set-ItemProperty $k UninstallString "powershell -NoProfile -Command `"Start-Process '$exe' --quit -Wait; schtasks /delete /tn MishkaHelper /f; Remove-Item '$lnk','$dir' -Recurse -Force; Remove-Item '$k' -Force`""

if ($SelfTest) {
  "selftest -> $SelfTest" | Out-File $log -Append -Encoding utf8
  Start-Process $exe -ArgumentList "--selftest `"$SelfTest`" `"$Only`"" -Wait
}

# Normal start; the app registers its logon task pointing at this exe.
Start-Process $exe
"done $(Get-Date)" | Out-File $log -Append -Encoding utf8
