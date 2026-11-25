$ErrorActionPreference = 'SilentlyContinue'
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetExe = Join-Path $installDir 'QuickText.exe'
Get-Process -Name 'QuickText' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe } | Stop-Process -Force

$desktop = $env:QUICKTEXT_DESKTOP_DIR
if ([string]::IsNullOrWhiteSpace($desktop)) {
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
}
$programs = $env:QUICKTEXT_STARTMENU_DIR
if ([string]::IsNullOrWhiteSpace($programs)) {
    $programs = [Environment]::GetFolderPath('Programs')
}
Remove-Item (Join-Path $desktop 'QuickText.lnk') -Force
Remove-Item (Join-Path (Join-Path $programs 'QuickText') 'QuickText.lnk') -Force
Remove-Item (Join-Path $programs 'QuickText') -Recurse -Force

if ($env:QUICKTEXT_SKIP_REGISTRY -ne '1') {
    Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickText' -Recurse -Force
}

# Remove the executable and runtime images. Keep personal prompts in %APPDATA%\QuickPrompt.
foreach ($name in @('QuickText.exe','QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    Remove-Item -LiteralPath (Join-Path $installDir $name) -Force
}

# The running uninstaller cleans up its own folder after this PowerShell process exits.
$escapedPath = $installDir.Replace("'", "''")
$cleanupCommand = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$escapedPath' -Recurse -Force -ErrorAction SilentlyContinue"
$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanupCommand))
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @('-NoProfile','-EncodedCommand',$encodedCommand)
Write-Host 'QuickText was removed. Your saved prompts were kept.'
