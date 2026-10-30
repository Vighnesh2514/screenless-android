# Builds screenless.exe with the C# compiler that ships with Windows.
# No .NET SDK, no NuGet, no admin rights.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) {
  throw "csc.exe not found at $csc"
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$out = Join-Path $dist 'screenless.exe'
$ico = Join-Path $root 'screenless.ico'
$manifest = Join-Path $root 'app.manifest'
$source = Join-Path $root 'ScreenlessApp.cs'

& $csc /nologo /target:winexe /optimize+ `
  "/out:$out" `
  "/win32icon:$ico" `
  "/win32manifest:$manifest" `
  "/resource:$ico,screenless.ico" `
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll `
  /r:System.Drawing.dll /r:System.IO.Compression.dll `
  /r:System.IO.Compression.FileSystem.dll `
  $source

if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$size = [math]::Round((Get-Item $out).Length / 1KB)
Write-Host "Built $out ($size KB)"
