<#
.SYNOPSIS
  One-time setup on a Windows laptop: installs scrcpy (with adb), optionally installs a
  trusted adb key, and puts a "Phone" shortcut on the Desktop.

.DESCRIPTION
  Downloads the official scrcpy Windows build into %LOCALAPPDATA%\Programs\scrcpy and adds
  it to your user PATH. No admin rights, no Microsoft Store, no winget needed.
  If the download fails and winget is available, winget is used instead.

.EXAMPLE
  .\install.ps1                         # install scrcpy + shortcut
  .\install.ps1 -KeyFolder D:\phone-key # also trust an existing key (no prompt on the phone)
#>
param([string]$KeyFolder)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

function Install-ScrcpyPortable {
  $release = Invoke-RestMethod `
    -Uri 'https://api.github.com/repos/Genymobile/scrcpy/releases/latest' `
    -Headers @{ 'User-Agent' = 'screenless-install' } -TimeoutSec 60
  $asset = $release.assets | Where-Object { $_.name -match '^scrcpy-win64-.*\.zip$' } | Select-Object -First 1
  if (-not $asset) { throw "No scrcpy win64 build in release $($release.tag_name)." }

  $dest = Join-Path $env:LOCALAPPDATA 'Programs\scrcpy'
  $zip = Join-Path $env:TEMP $asset.name
  $tmp = Join-Path $env:TEMP ('scrcpy-' + [guid]::NewGuid().ToString('N'))

  Write-Info "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB)) MB) ..."
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -TimeoutSec 300
  Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force

  $inner = Get-ChildItem -Path $tmp -Directory | Select-Object -First 1
  if (-not $inner) { throw 'The scrcpy archive had an unexpected layout.' }
  if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
  Move-Item -LiteralPath $inner.FullName -Destination $dest

  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

  Add-ToUserPath $dest
  return (Join-Path $dest 'scrcpy.exe')
}

function Install-ScrcpyWithWinget {
  $winget = Resolve-ToolPath -Name 'winget' -Candidates @(
    (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe')
  )
  if (-not $winget) { return $null }

  Write-Info 'Trying winget ...'
  & $winget install --exact --id Genymobile.scrcpy --accept-source-agreements --accept-package-agreements --disable-interactivity
  Update-SessionPath
  return Get-ScrcpyPath
}

# 1. scrcpy (bundles adb)
$scrcpy = Get-ScrcpyPath
if ($scrcpy) {
  $version = (& $scrcpy --version 2>$null | Select-Object -First 1)
  Write-Info "scrcpy already installed: $version"
} else {
  try {
    $scrcpy = Install-ScrcpyPortable
  } catch {
    Write-Warn "Download failed: $($_.Exception.Message)"
    $scrcpy = Install-ScrcpyWithWinget
  }
  if (-not $scrcpy) {
    Write-Fail 'Could not install scrcpy. Download scrcpy-win64 from https://github.com/Genymobile/scrcpy/releases, extract it, and add the folder to PATH.'
  }
  Update-SessionPath
  $version = (& $scrcpy --version 2>$null | Select-Object -First 1)
  Write-Info "scrcpy installed: $version"
}

$adb = Get-AdbPath
if (-not $adb) {
  Write-Fail 'adb was not found next to scrcpy. Re-run this installer, or install Android SDK Platform-Tools and add it to PATH.'
}
$adbVersion = (& $adb version 2>$null | Select-Object -First 1)
Write-Info "adb ready: $adbVersion"

# 2. Optional trusted key
if ($KeyFolder) {
  foreach ($file in 'adbkey', 'adbkey.pub') {
    if (-not (Test-Path -LiteralPath (Join-Path $KeyFolder $file))) {
      Write-Fail "Missing $file in $KeyFolder"
    }
  }
  $adbDir = Join-Path $env:USERPROFILE '.android'
  New-Item -ItemType Directory -Force -Path $adbDir | Out-Null
  $stamp = Get-Date -Format yyyyMMddHHmmss
  foreach ($file in 'adbkey', 'adbkey.pub') {
    $target = Join-Path $adbDir $file
    if (Test-Path -LiteralPath $target) {
      Rename-Item -LiteralPath $target -NewName "$file.backup-$stamp"
      Write-Info "Backed up existing $file"
    }
    Copy-Item -LiteralPath (Join-Path $KeyFolder $file) -Destination $target
  }
  & $adb kill-server | Out-Null
  Write-Info 'Trusted key installed.'
}

# 3. Desktop shortcut
$bat = Join-Path $PSScriptRoot 'phone.bat'
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop) { Write-Fail 'Could not find your Desktop folder.' }
$lnk = Join-Path $desktop 'Phone.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $bat
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Mirror and control your Android phone'
$shortcut.Save()
Write-Info "Desktop shortcut created: $lnk"

Write-Info 'Done. Plug in the phone and double-click "Phone" on the Desktop.'
