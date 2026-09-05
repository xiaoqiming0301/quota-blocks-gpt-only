#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes GPT Version into %LOCALAPPDATA%\Programs\GPT Version, registers
  launch at login, and starts it. Safe to re-run to update in place.
#>
[CmdletBinding()]
param(
    [switch]$NoLaunchAtLogin,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'QuotaBlocksWin.csproj'
$oldInstall = Join-Path $env:LOCALAPPDATA 'Programs\GPTVersion'
$install = Join-Path $env:LOCALAPPDATA 'Programs\GPT Version'
$exe = Join-Path $install 'GPT Version.exe'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$oldShortcut = Join-Path $startMenu 'QuotaBlocks.lnk'
$previousShortcut = Join-Path $startMenu 'GPTVersion.lnk'
$shortcutPath = Join-Path $startMenu 'GPT Version.lnk'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET 8 SDK first: winget install Microsoft.DotNet.SDK.8'
}

Write-Host 'Stopping running instances...' -ForegroundColor Cyan
Get-Process QuotaBlocks,GPTVersion,'GPT Version' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $oldInstall) {
    Remove-Item -LiteralPath $oldInstall -Recurse -Force
}

Write-Host "Publishing to $install ..." -ForegroundColor Cyan
dotnet publish $project -c Release -o $install --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)" }

# Replace the old dual-provider shortcut so it cannot launch QuotaBlocks.exe again.
Remove-Item $oldShortcut -Force -ErrorAction SilentlyContinue
Remove-Item $previousShortcut -Force -ErrorAction SilentlyContinue
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $install
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Quota Blocks - GPT-only personal edition'
$shortcut.Save()

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoLaunchAtLogin) {
    Remove-ItemProperty $runKey -Name QuotaBlocks -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey -Name GPTVersion -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey -Name 'GPT Version' -ErrorAction SilentlyContinue
    Write-Host 'Launch at login: disabled' -ForegroundColor Yellow
}
else {
    Remove-ItemProperty $runKey -Name QuotaBlocks -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey -Name GPTVersion -ErrorAction SilentlyContinue
    Set-ItemProperty $runKey -Name 'GPT Version' -Value "`"$exe`""
    Write-Host 'Launch at login: enabled' -ForegroundColor Green
}

if (-not $NoStart) {
    Start-Process $exe
    Write-Host 'Started. Check the bottom-left taskbar area.' -ForegroundColor Green
}
