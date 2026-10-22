# Installs the published app folder into Program Files on this PC (run elevated).
# Usage: install-local.ps1 -Source <publish folder> [-SelfTest <report.txt>] [-Only steps]
param([Parameter(Mandatory)] [string]$Source, [string]$SelfTest, [string]$Only = "")
$ErrorActionPreference = 'Stop'
if (Test-Path $Source -PathType Leaf) { $Source = Split-Path $Source }   # old callers passed the exe
$dir = Join-Path $env:ProgramFiles 'Mishka Helper'
$exe = Join-Path $dir 'MishkaHelper.exe'
$log = Join-Path $Source 'install.log'
"start $(Get-Date)" | Out-File $log -Encoding utf8
trap { "ERROR: $_ at line $($_.InvocationInfo.ScriptLineNumber)" | Out-File $log -Append -Encoding utf8; break }

# Stop any running copy and its helper servers.
Get-Process MishkaHelper, llama-server, piper -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# Clean install of the app folder (models live in %LocalAppData%, settings in %AppData% — untouched).
# Empty the folder rather than deleting it: Explorer windows often hold a handle on the folder itself.
New-Item -ItemType Directory -Force $dir | Out-Null
Get-ChildItem $dir -Force | Remove-Item -Recurse -Force -ErrorAction Continue
Get-ChildItem $Source -Exclude 'install.log', '*.pdb', '*.xml' | Copy-Item -Destination $dir -Recurse -Force
"copied to $dir" | Out-File $log -Append -Encoding utf8

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
