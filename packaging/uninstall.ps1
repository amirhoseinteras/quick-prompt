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
if ($env:QUICKTEXT_SKIP_REGISTRY -ne '1') {`r`n    Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickText' -Recurse -Force`r`n}

# Keep personal prompts in %APPDATA%\QuickPrompt.
Start-Process -FilePath 'cmd.exe' -WindowStyle Hidden -ArgumentList @('/c', 'timeout /t 2 >nul & rmdir /s /q "' + $installDir + '"')
Write-Host 'QuickText was removed. Your saved prompts were kept.'
