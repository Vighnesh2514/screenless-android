<#
.SYNOPSIS
  Check that this Windows laptop is ready to mirror a phone, without starting a mirror.

.DESCRIPTION
  Prints the version and location of scrcpy and adb, the saved Wi-Fi IP, any connected
  phone, and whether the Desktop shortcut exists. Exits 1 if a required tool is missing.

.EXAMPLE
  .\doctor.ps1
#>
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '_common.ps1')

$problems = 0

$os = (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption
if (-not $os) { $os = [System.Environment]::OSVersion.VersionString }
Write-Info "Windows: $os"
Write-Info "PowerShell: $($PSVersionTable.PSVersion)"

$scrcpy = Get-ScrcpyPath
if ($scrcpy) {
  $version = (& $scrcpy --version 2>$null | Select-Object -First 1)
  Write-Info "scrcpy: $version"
  Write-Info "  at $scrcpy"
} else {
  Write-Warn 'scrcpy: NOT FOUND. Run windows\install.ps1.'
  $problems++
}

$adb = Get-AdbPath
if ($adb) {
  $version = (& $adb version 2>$null | Select-Object -First 1)
  Write-Info "adb: $version"
  Write-Info "  at $adb"
} else {
  Write-Warn 'adb: NOT FOUND. Run windows\install.ps1.'
  $problems++
}

$ipFile = Join-Path $env:LOCALAPPDATA 'screenless\last-ip.txt'
if (Test-Path -LiteralPath $ipFile) {
  Write-Info "Saved Wi-Fi IP: $((Get-Content -LiteralPath $ipFile -Raw).Trim())"
} else {
  Write-Info 'Saved Wi-Fi IP: none yet (connect once over USB to save it)'
}

if ($adb) {
  $devices = @(Get-AdbDevices -Adb $adb)
  if ($devices.Count -eq 0) {
    Write-Info 'Phone: not connected (plug in a data cable, or go wireless after one USB run)'
  } else {
    foreach ($device in $devices) {
      $link = if ($device.Usb) { 'USB' } else { 'Wi-Fi' }
      $model = if ($device.Model) { " ($($device.Model))" } else { '' }
      Write-Info "Phone: $($device.Serial) $($device.State)$model over $link"
    }
  }
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Phone.lnk'
if (Test-Path -LiteralPath $shortcut) {
  Write-Info 'Desktop shortcut: OK'
} else {
  Write-Info 'Desktop shortcut: missing (run windows\install.ps1)'
}

if ($problems -gt 0) {
  Write-Warn 'Not ready yet. Fix the lines marked NOT FOUND above.'
  exit 1
}
Write-Info 'Ready. Double-click "Phone" on the Desktop, or run windows\phone.bat.'
