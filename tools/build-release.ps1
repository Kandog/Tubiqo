<#
.SYNOPSIS
  Builds a Tubiqo release: publishes the app, builds the installer and (optionally) signs the release.

.DESCRIPTION
  1. dotnet publish (Release, win-x64, self-contained, so friends don't need .NET installed)
  2. Inno Setup installer -> artifacts\installer\Tubiqo-Setup-<version>.exe
  3. With -Sign: tubiqo-release.json + .sig in artifacts\release, ready to attach to a GitHub release.

.EXAMPLE
  .\tools\build-release.ps1 -Sign
  .\tools\build-release.ps1 -Sign -RulePack .\src\Tubiqo.App\Rules\default-rulepack.json
#>
param(
    [switch]$Sign,
    [string]$RulePack,
    [string]$Key = (Join-Path $env:APPDATA 'Tubiqo-signing\tubiqo-signing-key.pem')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$installerOut = Join-Path $artifacts 'installer'
$releaseOut = Join-Path $artifacts 'release'

[xml]$proj = Get-Content (Join-Path $root 'src\Tubiqo.App\Tubiqo.App.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Tubiqo.App.csproj' }
Write-Host "Tubiqo $version"

Remove-Item -Recurse -Force $publish, $installerOut, $releaseOut -ErrorAction SilentlyContinue

Write-Host '1/3 Publishing...'
dotnet publish (Join-Path $root 'src\Tubiqo.App\Tubiqo.App.csproj') -c Release -r win-x64 --self-contained true -o $publish -nologo -v q
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

Write-Host '2/3 Building installer...'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it: winget install --id JRSoftware.InnoSetup -e --scope user' }
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$installerOut" (Join-Path $root 'installer\Tubiqo.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
$installer = Join-Path $installerOut "Tubiqo-Setup-$version.exe"
Write-Host ("   {0} ({1:N1} MB)" -f $installer, ((Get-Item $installer).Length / 1MB))

if ($Sign) {
    Write-Host '3/3 Signing release...'
    $signArgs = @('release', '--key', $Key, '--out', $releaseOut, '--installer', $installer, '--app-version', $version)
    if ($RulePack) { $signArgs += @('--rulepack', $RulePack) }
    dotnet run --project (Join-Path $root 'tools\Tubiqo.Sign') -c Release -- @signArgs
    if ($LASTEXITCODE) { throw 'Signing failed' }
    Write-Host "Attach every file in $releaseOut to a GitHub release of Kandog/Tubiqo."
} else {
    Write-Host '3/3 Skipped signing (use -Sign).'
}
