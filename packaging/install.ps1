$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$installDir = $env:QUICKTEXT_INSTALL_DIR
if ([string]::IsNullOrWhiteSpace($installDir)) {
    $installDir = Join-Path $env:LOCALAPPDATA 'QuickPrompt'
}
$desktop = $env:QUICKTEXT_DESKTOP_DIR
if ([string]::IsNullOrWhiteSpace($desktop)) {
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
}
$programs = $env:QUICKTEXT_STARTMENU_DIR
if ([string]::IsNullOrWhiteSpace($programs)) {
    $programs = [Environment]::GetFolderPath('Programs')
}
$startMenuDir = Join-Path $programs 'QuickText'
$targetExe = Join-Path $installDir 'QuickText.exe'

# Close only the copy being upgraded; do not stop a separate portable copy.
Get-Process -Name 'QuickText' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe } | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $installDir, $desktop, $startMenuDir | Out-Null
foreach ($name in @('QuickText.exe','QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    $source = Join-Path $packageRoot $name
    if (-not (Test-Path $source)) { throw "Required package file is missing: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $installDir $name) -Force
}

$uninstallSource = Join-Path $packageRoot 'uninstall.ps1'
if (-not (Test-Path $uninstallSource)) { throw 'Uninstaller script is missing.' }
$uninstallPath = Join-Path $installDir 'uninstall.ps1'
Copy-Item -LiteralPath $uninstallSource -Destination $uninstallPath -Force

$ws = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in @(
    (Join-Path $desktop 'QuickText.lnk'),
    (Join-Path $startMenuDir 'QuickText.lnk')
)) {
    $shortcut = $ws.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $installDir 'QuickText.exe'
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = (Join-Path $installDir 'QuickText.ico') + ',0'
    $shortcut.Description = 'QuickText - lightweight offline prompt manager'
    $shortcut.Save()
}

if ($env:QUICKTEXT_SKIP_REGISTRY -ne '1') {
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickText'
    New-Item -Path $uninstallKey -Force | Out-Null
    $uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $uninstallPath + '"'
    New-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value 'QuickText' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value '4.7.1' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'QuickText Open Source' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $installDir -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value (Join-Path $installDir 'QuickText.ico') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value $uninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
}

Write-Host "QuickText 4.7.1 installed to $installDir"
Write-Host 'Your saved prompts are kept in %APPDATA%\QuickPrompt.'
