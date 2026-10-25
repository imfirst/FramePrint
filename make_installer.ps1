# Builds FramePrint_Setup.exe (single file, plugin embedded). No IExpress needed.
# Usage: powershell -ExecutionPolicy Bypass -File make_installer.ps1
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build.ps1")
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
$dll = Join-Path $here "bin\FramePrint.dll"
$pkg = Join-Path $here "bundle\FramePrint.bundle\PackageContents.xml"
$out = Join-Path $here "FramePrint_Setup.exe"
$cargs = @("/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/codepage:65001",
  "/r:$fw\System.dll", "/r:$fw\System.Windows.Forms.dll",
  "/resource:$dll,FramePrint.dll", "/resource:$pkg,PackageContents.xml",
  "/out:$out", (Join-Path $here "installer\Setup.cs"))
& $csc $cargs
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed - send the errors above to Claude" }
Write-Host "OK: $out" -ForegroundColor Green
