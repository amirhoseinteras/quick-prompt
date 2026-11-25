$ErrorActionPreference = 'SilentlyContinue'
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetExe = Join-Path $installDir 'QuickText.exe'
Get-Process -Name 'QuickText' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe } | Stop-Process -Force

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$programs = [Environment]::GetFolderPath('Programs')
Remove-Item (Join-Path $desktop 'QuickText.lnk') -Force
Remove-Item (Join-Path (Join-Path $programs 'QuickText') 'QuickText.lnk') -Force
Remove-Item (Join-Path $programs 'QuickText') -Recurse -Force
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickText' -Recurse -Force

# Keep personal prompts in %APPDATA%\QuickPrompt.
Start-Process -FilePath 'cmd.exe' -WindowStyle Hidden -ArgumentList @('/c', 'timeout /t 2 >nul & rmdir /s /q "' + $installDir + '"')
Write-Host 'QuickText was removed. Your saved prompts were kept.'
