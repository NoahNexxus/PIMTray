#requires -Version 5.1
<#
.SYNOPSIS
    Builds the PIM Tray installer artifacts: PIMTray.msi and the PIMTray-Setup.exe
    download bootstrapper.

.DESCRIPTION
    Steps:
      1. Publishes PIMTray (framework-dependent, win-x64) so the .exe embeds the
         version metadata from PIMTray.csproj.
      2. Builds PIMTray.msi from PIMTray.wxs.
      3. Builds PIMTray-Setup.exe from Bundle.wxs. The bundle detects the installed
         .NET 10 Desktop Runtime and only downloads it (from Microsoft's CDN) when a
         compatible one is missing, then installs the MSI.

    Prerequisites (one-time):
      dotnet tool install --global wix --version 5.0.2
      wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
      wix extension add -g WixToolset.Util.wixext/5.0.2
      wix extension add -g WixToolset.Netfx.wixext/5.0.2

    Bumping the bundled runtime version:
      The runtime is pinned in Bundle.wxs (<ExePackagePayload> Hash/Size/Version/DownloadUrl)
      and in the DetectCondition major version. To move to a newer patch, download the new
      windowsdesktop-runtime-<ver>-win-x64.exe, verify its SHA512 against
      https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json, then
      regenerate the payload element with:
        wix burn remotepayload windowsdesktop-runtime-<ver>-win-x64.exe
      and paste the Hash/Size/Version in, updating the DownloadUrl to match.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerDir = $PSScriptRoot

Write-Host "==> Publishing PIMTray ($Configuration/$Runtime)..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot "PIMTray.csproj") `
    -c $Configuration -r $Runtime --self-contained false --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Push-Location $installerDir
try {
    Write-Host "==> Building PIMTray.msi..." -ForegroundColor Cyan
    wix build PIMTray.wxs -arch x64 -out PIMTray.msi
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

    Write-Host "==> Building PIMTray-Setup.exe (bootstrapper)..." -ForegroundColor Cyan
    wix build Bundle.wxs -arch x64 `
        -ext WixToolset.BootstrapperApplications.wixext `
        -ext WixToolset.Netfx.wixext `
        -ext WixToolset.Util.wixext `
        -out PIMTray-Setup.exe
    if ($LASTEXITCODE -ne 0) { throw "Bundle build failed" }

    Write-Host "==> Done." -ForegroundColor Green
    Get-Item PIMTray.msi, PIMTray-Setup.exe | Select-Object Name, Length, LastWriteTime | Format-Table
}
finally {
    Pop-Location
}
