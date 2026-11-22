$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. "$PSScriptRoot\env.ps1"

$dl = Join-Path $Tools 'downloads'
New-Item -ItemType Directory -Force -Path $Tools, $dl | Out-Null

function Get-File([string]$Url, [string]$Out) {
    if (Test-Path $Out) { return }
    Write-Host "  Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Out -UseBasicParsing
}

Write-Host '[1/3] OpenJDK 17' -ForegroundColor Cyan
if (-not (Test-Path "$env:JAVA_HOME\bin\java.exe")) {
    $zip = Join-Path $dl 'jdk17.zip'
    Get-File 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' $zip
    $tmp = Join-Path $dl 'jdk-tmp'
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $zip $tmp
    Move-Item (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName $env:JAVA_HOME
    Remove-Item $tmp -Recurse -Force
}

Write-Host '[2/3] Android command-line tools' -ForegroundColor Cyan
$latest = Join-Path $env:ANDROID_HOME 'cmdline-tools\latest'
if (-not (Test-Path "$latest\bin\sdkmanager.bat")) {
    $zip = Join-Path $dl 'cmdline-tools.zip'
    Get-File 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' $zip
    $tmp = Join-Path $dl 'cmdline-tmp'
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $zip $tmp
    New-Item -ItemType Directory -Force -Path (Split-Path $latest) | Out-Null
    Move-Item (Join-Path $tmp 'cmdline-tools') $latest
    Remove-Item $tmp -Recurse -Force
}

Write-Host '[3/3] Android SDK packages' -ForegroundColor Cyan
$sdk = Join-Path $latest 'bin\sdkmanager.bat'
& $sdk "--sdk_root=$env:ANDROID_HOME" --licenses
Invoke-Native $sdk @("--sdk_root=$env:ANDROID_HOME", 'platform-tools', 'platforms;android-35', 'build-tools;35.0.0') | Where-Object { $_ -notmatch '^\s*\[=*\s*\]' }
if (-not (Test-Path (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'))) { throw 'platform-tools did not install' }

Write-Host 'Setup finished.' -ForegroundColor Green
