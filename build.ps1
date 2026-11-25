param([switch]$RunTests, [switch]$NoPackage)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'src\QuickText.cs'
$assets = Join-Path $root 'assets'
$packaging = Join-Path $root 'packaging'
$out = Join-Path $root 'dist'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) { throw '.NET Framework C# compiler was not found.' }

foreach ($file in @('QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    if (-not (Test-Path (Join-Path $assets $file))) { throw "Missing build asset: $file" }
}
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$exe = Join-Path $out 'QuickText.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/win32icon:$assets\QuickText.ico" "/out:$exe" $source
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }

foreach ($file in @('QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    Copy-Item (Join-Path $assets $file) (Join-Path $out $file) -Force
}
$version = (Get-Item $exe).VersionInfo.ProductVersion
if ($version -ne '4.7.1') { throw "Unexpected executable version: $version" }

if ($RunTests) {
    $tests = @(
        @{Arg='--test-pins'; Result='pin-test-result.txt'},
        @{Arg='--test-numbering'; Result='number-test-result.txt'},
        @{Arg='--test-links'; Result='link-test-result.txt'},
        @{Arg='--self-test'; Result='selftest.txt'}
    )
    foreach ($test in $tests) {
        Start-Process -FilePath $exe -ArgumentList $test.Arg -Wait -NoNewWindow
        $resultPath = Join-Path $out $test.Result
        if (-not (Test-Path $resultPath)) { throw "$($test.Arg) did not create a result file." }
        $result = (Get-Content $resultPath -Raw).Trim()
        if ($result -notmatch '^PASS') { throw "$($test.Arg) failed: $result" }
        Write-Host "$($test.Arg): $result"
        Remove-Item $resultPath -Force
    }
}

if ($NoPackage) {
    Write-Host "Built and tested QuickText $version (packaging skipped)."
    return
}

$portableDir = Join-Path $out 'portable'
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
foreach ($file in @('QuickText.exe','QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    Copy-Item (Join-Path $out $file) (Join-Path $portableDir $file) -Force
}
$portableZip = Join-Path $out 'QuickText-Portable.zip'
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force

$installerDir = Join-Path $out 'installer'
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
foreach ($file in @('QuickText.exe','QuickText.ico','QuickText-pulse.ico','QuickText-4.7-logo.png')) {
    Copy-Item (Join-Path $out $file) (Join-Path $installerDir $file) -Force
}
foreach ($file in @('install.ps1','uninstall.ps1','setup.cmd')) {
    Copy-Item (Join-Path $packaging $file) (Join-Path $installerDir $file) -Force
}

$sed = Join-Path $out 'QuickText-Setup.sed'
$setupExe = Join-Path $out 'QuickText-Setup.exe'
$sedText = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=QuickText 4.7.1 has been installed.
TargetName=$setupExe
FriendlyName=QuickText 4.7.1 Setup
AppLaunched=cmd.exe /c setup.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=cmd.exe /c setup.cmd
UserQuietInstCmd=cmd.exe /c setup.cmd
SourceFiles=SourceFiles
[Strings]
FILE0="QuickText.exe"
FILE1="QuickText.ico"
FILE2="QuickText-pulse.ico"
FILE3="QuickText-4.7-logo.png"
FILE4="install.ps1"
FILE5="uninstall.ps1"
FILE6="setup.cmd"
[SourceFiles]
SourceFiles0=$installerDir\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=
"@
Set-Content -LiteralPath $sed -Value $sedText -Encoding ASCII

$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
$process = Start-Process -FilePath $iexpress -ArgumentList @('/N','/Q','/M',$sed) -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path $setupExe)) {
    throw "IExpress packaging failed. ExitCode=$($process.ExitCode)"
}

Write-Host "Built QuickText $version"
Get-ChildItem $out -File | Where-Object Name -in @('QuickText-Portable.zip','QuickText-Setup.exe') | Select-Object Name,Length
