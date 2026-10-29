<#
.SYNOPSIS
  Make a slow phone feel faster while mirroring. Every change is reversible with -Undo.

.DESCRIPTION
  - Animation scales 1.0 -> 0.5 (screens open about twice as fast)
  - Keep the phone awake so the mirror never goes black
  - Optional: disable pre-installed bloatware listed in a text file (one package per line)

.EXAMPLE
  .\tune.ps1
  .\tune.ps1 -Debloat ..\debloat\motorola.txt
  .\tune.ps1 -Undo -Debloat ..\debloat\motorola.txt
#>
param(
  [string]$Debloat,
  [switch]$Undo
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot '_common.ps1')

$adb = Get-AdbPath
if (-not $adb) {
  Write-Fail 'adb not found. Run windows\install.ps1 first, then connect the phone.'
}

$devices = @(Get-AdbDevices -Adb $adb | Where-Object { $_.State -eq 'device' })
if ($devices.Count -ne 1) {
  Write-Host 'Connect exactly one phone (USB or Wi-Fi) first.' -ForegroundColor Red
  exit 1
}
$serial = $devices[0].Serial

# Accept paths relative to this script as well as the current folder.
if ($Debloat -and -not [System.IO.Path]::IsPathRooted($Debloat)) {
  $relative = Join-Path $PSScriptRoot $Debloat
  if (Test-Path -LiteralPath $relative) { $Debloat = $relative }
}
if ($Debloat -and -not (Test-Path -LiteralPath $Debloat)) {
  Write-Fail "Debloat list not found: $Debloat"
}

function Get-FreeRamMB {
  $meminfo = (& $adb -s $serial shell cat /proc/meminfo 2>$null | Out-String)
  $match = [regex]::Match($meminfo, 'MemAvailable:\s+(\d+)')
  if (-not $match.Success) { return 0 }
  [int]$match.Groups[1].Value / 1024
}

$before = Get-FreeRamMB

$scale = if ($Undo) { '1.0' } else { '0.5' }
foreach ($key in 'window_animation_scale', 'transition_animation_scale', 'animator_duration_scale') {
  & $adb -s $serial shell settings put global $key $scale | Out-Null
}
Write-Info "Animation scale: $scale"

$stay = if ($Undo) { 'false' } else { 'true' }
& $adb -s $serial shell svc power stayon $stay | Out-Null
Write-Info "Stay awake: $stay"

if ($Debloat) {
  $pmArgs = if ($Undo) { @('enable') } else { @('disable-user', '--user', '0') }
  Get-Content -LiteralPath $Debloat |
    Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') } |
    ForEach-Object {
      $package = $_.Trim()
      $output = (& $adb -s $serial shell pm @pmArgs $package 2>&1 | Out-String).Trim()
      Write-Host "  $package -> $output"
    }
}

if (-not $Undo) {
  & $adb -s $serial shell am kill-all | Out-Null
  Start-Sleep -Seconds 3
  Write-Info ("Free RAM: {0:N0} MB -> {1:N0} MB" -f $before, (Get-FreeRamMB))
}
