param([switch]$RunTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw '.NET Framework C# compiler was not found; install the framework development tools.' }
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $out | Out-Null
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/win32icon:$root\assets\QuickPrompt.ico" "/out:$out\QuickPrompt.exe" "$root\src\QuickPrompt.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item "$root\assets\QuickPrompt.ico" "$out\QuickPrompt.ico" -Force
if ($RunTests) {
  & "$out\QuickPrompt.exe" --test-pins
  if ($LASTEXITCODE -ne 0) { throw 'Unable to run test executable.' }
  $result = Get-Content "$out\pin-test-result.txt" -Raw
  if ($result -notmatch '^PASS') { throw "Pin tests failed: $result" }
  Remove-Item "$out\pin-test-result.txt" -Force
}
Write-Host "Built: $out\QuickPrompt.exe"
