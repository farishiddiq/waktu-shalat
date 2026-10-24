# Builds dist\WaktuShalat.exe and dist\WaktuShalat-Setup.exe with the .NET Framework compiler built into Windows.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $root 'dist'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$csc = Join-Path $fw 'csc.exe'
New-Item -ItemType Directory -Force $out | Out-Null

$ico = Join-Path $out 'app.ico'
& (Join-Path $src 'make-icon.ps1') -Out $ico

$refs = @(
    "/r:$wpf\PresentationFramework.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\WindowsBase.dll",
    "/r:$fw\System.Xaml.dll", '/r:System.dll', '/r:System.Core.dll', '/r:Microsoft.CSharp.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll'
)
# Inter (SIL OFL) is embedded and extracted at runtime; see src\fonts\LICENSE.txt
$fonts = @(Get-ChildItem "$src\fonts\*.ttf" | ForEach-Object { "/resource:$($_.FullName),font.$($_.Name)" })
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', "/win32icon:$ico",
    "/win32manifest:$src\app.manifest", "/resource:$ico,app.ico") + $fonts + $refs

$app = Join-Path $out 'WaktuShalat.exe'
$appSrc = "$src\App.cs", "$src\Model.cs", "$src\Island.cs", "$src\Widget.cs", "$src\Ui.cs"
& $csc @common "/out:$app" @appSrc
if ($LASTEXITCODE) { throw 'App build failed' }

$setup = Join-Path $out 'WaktuShalat-Setup.exe'
& $csc @common "/out:$setup" "/resource:$app,app.exe" "$src\Installer.cs" "$src\Ui.cs"
if ($LASTEXITCODE) { throw 'Installer build failed' }

Get-Item $app, $setup | Select-Object Name, Length
