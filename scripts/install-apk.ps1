$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\env.ps1"
$apk = Join-Path $Root 'out\MyDeck.apk'
$adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
if (-not (Test-Path $apk)) { throw 'Run build.ps1 first' }

Invoke-Native $adb @('start-server') | Out-Null
$list = Invoke-Native $adb @('devices')
if (-not ($list | Where-Object { $_ -match "\tdevice$" })) {
    Write-Host 'No device found. Connect the tablet over USB, allow USB debugging on it and try again.' -ForegroundColor Yellow
    exit 1
}
Invoke-Native $adb @('install', '-r', $apk)
Invoke-Native $adb @('shell', 'monkey', '-p', 'app.mydeck', '-c', 'android.intent.category.LAUNCHER', '1') | Out-Null
Write-Host 'Installed.' -ForegroundColor Green
