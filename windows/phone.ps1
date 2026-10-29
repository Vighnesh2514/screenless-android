<#
.SYNOPSIS
  Mirror and control an Android phone from this Windows laptop.

.DESCRIPTION
  USB plugged in  -> mirrors over USB, remembers the phone's Wi-Fi IP and re-arms wireless mode.
  No cable        -> connects over Wi-Fi to the last remembered IP.

.EXAMPLE
  .\phone.ps1              # video only (smoothest)
  .\phone.ps1 -Audio       # also play the phone's sound on the laptop
  .\phone.ps1 -Ip 192.168.1.42
#>
param(
  [switch]$Audio,
  [string]$Ip
)

# adb writes normal status lines to stderr; don't let PowerShell treat them as fatal
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '_common.ps1')

$Port = 5555
$ConfigDir = Join-Path $env:LOCALAPPDATA 'screenless'
$IpFile = Join-Path $ConfigDir 'last-ip.txt'

$adb = Get-AdbPath
if (-not $adb) {
  Write-Fail 'adb not found. Run windows\install.ps1 first (or open a new terminal after installing).'
}
$scrcpy = Get-ScrcpyPath
if (-not $scrcpy) {
  Write-Fail 'scrcpy not found. Run windows\install.ps1 first (or open a new terminal after installing).'
}

$common = @('--video-codec=h264', '--max-fps=60', '--turn-screen-off')
if (-not $Audio) { $common += '--no-audio' }

function Save-PhoneIp([string]$Address) {
  if (-not $Address) { return }
  New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
  Set-Content -LiteralPath $IpFile -Value $Address -Encoding Ascii
  Write-Info "Phone Wi-Fi IP: $Address (saved)"
}

$devices = @(Get-AdbDevices -Adb $adb)
$connected = @($devices | Where-Object { $_.State -eq 'device' })
$usb = $connected | Where-Object { $_.Usb } | Select-Object -First 1
$wifi = $connected | Where-Object { $_.Tcp } | Select-Object -First 1

if (-not $usb -and -not $wifi) {
  $broken = $devices | Select-Object -First 1
  if ($broken -and $broken.State -eq 'unauthorized') {
    Write-Fail "Phone is connected but not trusted. Tick 'Always allow' on the phone's prompt, or install a trusted key (docs\WINDOWS.md, step 3)."
  }
  if ($broken -and $broken.State -eq 'offline') {
    Write-Fail "Phone is listed as 'offline'. Unplug, wait 5 seconds, plug back in."
  }
}

if ($usb) {
  # Remember the Wi-Fi IP for cable-free use later
  $addr = (& $adb -s $usb.Serial shell ip -f inet addr show wlan0 2>$null | Out-String)
  $match = [regex]::Match($addr, 'inet (\d+\.\d+\.\d+\.\d+)')
  if ($match.Success) { Save-PhoneIp $match.Groups[1].Value }

  # Wireless mode turns off on every phone restart; re-arm it while we're plugged in.
  # adb tcpip restarts adbd, so wait for the USB serial to come back as "device".
  & $adb -s $usb.Serial tcpip $Port | Out-Null
  if (-not (Wait-AdbDevice -Adb $adb -Serial $usb.Serial -TimeoutSeconds 20)) {
    Write-Fail 'The phone did not reconnect after switching on wireless mode. Unplug it, wait 5 seconds, plug it back in.'
  }
  & $adb -s $usb.Serial shell svc power stayon true | Out-Null
  & $adb -s $usb.Serial shell input keyevent KEYCODE_WAKEUP | Out-Null

  $target = $usb.Serial
  $quality = @('--video-bit-rate=8M')
  Write-Info 'Mirroring over USB. Close the window to stop.'
} else {
  if ($wifi) {
    # Already connected over Wi-Fi (Wireless debugging, or an earlier `adb connect`)
    $target = $wifi.Serial
    Save-PhoneIp ($target -split ':')[0]
  } else {
    if (-not $Ip -and (Test-Path -LiteralPath $IpFile)) {
      $Ip = (Get-Content -LiteralPath $IpFile -Raw).Trim()
    }
    if (-not $Ip) {
      Write-Fail 'No USB phone and no saved Wi-Fi IP. Plug the phone in once and run this again.'
    }
    if ($Ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
      Write-Fail "That does not look like an IPv4 address: '$Ip'. Pass one with -Ip 192.168.1.42."
    }

    $target = "${Ip}:$Port"
    Write-Info "Connecting over Wi-Fi to $target ..."
    $result = Connect-AdbWifi -Adb $adb -Target $target
    if ($result -notmatch 'connected to') {
      Write-Fail "Could not reach $target. If the phone restarted, wireless mode is off: plug in USB once and run this again. ($($result.Trim()))"
    }
    if (-not (Wait-AdbDevice -Adb $adb -Serial $target -TimeoutSeconds 10)) {
      Write-Fail "Connected to $target but the phone is not responding yet. Unplug and replug the cable once, then run this again."
    }
  }

  & $adb -s $target shell svc power stayon true | Out-Null
  & $adb -s $target shell input keyevent KEYCODE_WAKEUP | Out-Null

  # Less data per frame so a weak network drops fewer frames
  $quality = @('--video-bit-rate=3M', '--max-size=1024')
  Write-Info 'Mirroring over Wi-Fi. Close the window to stop.'
}

& $scrcpy -s $target --stay-awake @quality @common
exit $LASTEXITCODE
