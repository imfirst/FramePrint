# Install autoload bundle into %APPDATA%\Autodesk\ApplicationPlugins\FramePrint.bundle
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll = Join-Path $here "bin\FramePrint.dll"
if (-not (Test-Path $dll)) { throw "Run build.ps1 first" }
$dst = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\FramePrint.bundle"
New-Item -ItemType Directory -Force (Join-Path $dst "Contents") | Out-Null
Copy-Item (Join-Path $here "bundle\FramePrint.bundle\PackageContents.xml") $dst -Force
Copy-Item $dll (Join-Path $dst "Contents") -Force
Write-Host "Installed to $dst. Restart AutoCAD and type FPRINT." -ForegroundColor Green
