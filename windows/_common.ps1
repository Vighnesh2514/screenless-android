# Shared helpers for the screenless Windows kit.
# Dot-source from a script in this folder:  . (Join-Path $PSScriptRoot '_common.ps1')

function Write-Info([string]$Message) {
  Write-Host "[screenless] $Message" -ForegroundColor Cyan
}

function Write-Warn([string]$Message) {
  Write-Host "[screenless] $Message" -ForegroundColor Yellow
}

function Write-Fail([string]$Message) {
  Write-Host "`n[screenless] $Message" -ForegroundColor Red
  exit 1
}

# Reload PATH from the registry so tools installed a moment ago are found.
function Update-SessionPath {
  $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
  $user = [Environment]::GetEnvironmentVariable('Path', 'User')
  $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

function Add-ToUserPath([string]$Directory) {
  $user = [Environment]::GetEnvironmentVariable('Path', 'User')
  $parts = @()
  if ($user) { $parts = $user -split ';' | Where-Object { $_ } }
  if ($parts -notcontains $Directory) {
    [Environment]::SetEnvironmentVariable('Path', (($parts + $Directory) -join ';'), 'User')
  }
  if (($env:Path -split ';') -notcontains $Directory) { $env:Path = "$env:Path;$Directory" }
}

# Find a tool on PATH first, then in the given candidate paths.
function Resolve-ToolPath {
  param(
    [string]$Name,
    [string[]]$Candidates = @()
  )
  $cmd = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cmd -and $cmd.Source -and (Test-Path -LiteralPath $cmd.Source)) { return $cmd.Source }
  foreach ($candidate in $Candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  return $null
}

# scrcpy lives on PATH, in our own install folder, or in a winget package folder.
function Get-ScrcpyPath {
  $candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\scrcpy\scrcpy.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\scrcpy.exe')
  )
  $found = Resolve-ToolPath -Name 'scrcpy' -Candidates $candidates
  if ($found) { return $found }

  $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
  if (Test-Path -LiteralPath $packages) {
    $hit = Get-ChildItem -Path $packages -Filter 'scrcpy.exe' -Recurse -ErrorAction SilentlyContinue |
      Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  return $null
}

# adb ships inside scrcpy, and may also be in the Android SDK.
function Get-AdbPath {
  $candidates = @()
  $scrcpy = Get-ScrcpyPath
  if ($scrcpy) { $candidates += (Join-Path (Split-Path -Parent $scrcpy) 'adb.exe') }
  $candidates += (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe')
  return Resolve-ToolPath -Name 'adb' -Candidates $candidates
}

# Parse `adb devices -l` into objects. Never throws when no phone is connected.
function Get-AdbDevices {
  param([Parameter(Mandatory = $true)][string]$Adb)

  $lines = & $Adb devices -l 2>$null
  foreach ($line in $lines) {
    if (-not $line) { continue }
    if ($line -match '^List of devices') { continue }
    $parts = ($line.Trim() -split '\s+')
    if ($parts.Count -lt 2) { continue }
    $serial = $parts[0]
    # TCP/IP serials are always "<ip>:<port>". USB serials are hardware ids.
    # Some Windows drivers omit the "usb:" field from `adb devices -l`, so never
    # rely on that field alone to tell USB from Wi-Fi.
    $isTcp = $serial -match ':'
    [pscustomobject]@{
      Serial = $serial
      State  = $parts[1]
      Usb    = -not $isTcp
      Tcp    = $isTcp
      Model  = $(if ($line -match '\bmodel:(\S+)') { $Matches[1] } else { '' })
    }
  }
}

# Poll until a serial reports state "device" (adb tcpip restarts the daemon).
function Wait-AdbDevice {
  param(
    [Parameter(Mandatory = $true)][string]$Adb,
    [Parameter(Mandatory = $true)][string]$Serial,
    [int]$TimeoutSeconds = 20
  )
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    $state = (& $Adb -s $Serial get-state 2>$null | Out-String).Trim()
    if ($state -eq 'device') { return $true }
    Start-Sleep -Milliseconds 500
  }
  return $false
}

# `adb connect` can answer before the phone is reachable; retry a few times.
function Connect-AdbWifi {
  param(
    [Parameter(Mandatory = $true)][string]$Adb,
    [Parameter(Mandatory = $true)][string]$Target,
    [int]$Attempts = 3
  )
  $output = ''
  for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $output = (& $Adb connect $Target 2>&1 | Out-String).Trim()
    if ($output -match 'connected to') { return $output }
    Start-Sleep -Milliseconds 800
  }
  return $output
}
