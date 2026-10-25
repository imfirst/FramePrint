# Build FramePrint.dll for AutoCAD 2021-2024 (.NET Framework 4.8)
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-AcadDir "C:\Program Files\Autodesk\AutoCAD 2021"]
param([string]$AcadDir = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $AcadDir) {
  $AcadDir = Get-ChildItem "C:\Program Files\Autodesk" -Directory -Filter "AutoCAD 20*" -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "acmgd.dll") } | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $AcadDir) { throw "AutoCAD not found. Use -AcadDir ""path to AutoCAD folder""" }
Write-Host "AutoCAD: $AcadDir"
if ($AcadDir -match "20(2[5-9]|[3-9]\d)") { throw "AutoCAD 2025+ uses .NET 8 - this script supports 2021-2024 only" }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $fw "csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
$out = Join-Path $here "bin"
New-Item -ItemType Directory -Force $out | Out-Null
$refList = @("$AcadDir\accoremgd.dll", "$AcadDir\acdbmgd.dll", "$AcadDir\acmgd.dll",
  "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Xml.dll", "$fw\System.Design.dll", "$fw\System.Xaml.dll",
  "$fw\WPF\PresentationFramework.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll")
$refs = $refList | ForEach-Object { "/r:$_" }
$src = Get-ChildItem (Join-Path $here "src") -Filter *.cs | ForEach-Object { $_.FullName }
$cargs = @("/nologo", "/target:library", "/platform:x64", "/optimize+", "/codepage:65001", "/out:$out\FramePrint.dll") + $refs + $src
& $csc $cargs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed - copy the errors above and send them to Claude" }
Write-Host "OK: $out\FramePrint.dll" -ForegroundColor Green
