param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\env.ps1"
$out = Join-Path $Root 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host '[1/2] Tablet app' -ForegroundColor Cyan
Push-Location (Join-Path $Root 'android')
try { Invoke-Native (Join-Path (Get-Location) 'gradlew.bat') @(':deck:assembleRelease', '--no-daemon', '--console=plain') }
finally { Pop-Location }
$apk = Join-Path $Root 'android\deck\build\outputs\apk\release\deck-release.apk'
if (-not (Test-Path $apk)) { throw 'The APK was not built' }
Copy-Item $apk (Join-Path $out 'MyDeck.apk') -Force

Write-Host '[2/2] PC app' -ForegroundColor Cyan
Get-Process -Name 'MyDeck' -ErrorAction SilentlyContinue | ForEach-Object { $_ | Stop-Process -Force; $_.WaitForExit(5000) | Out-Null }
$server = Join-Path $out 'server'
Remove-Item $server -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Native 'dotnet' @('publish', (Join-Path $Root 'windows\MyDeck.Server\MyDeck.Server.csproj'), '-c', 'Release', '-o', $server, '--nologo')
if (-not (Test-Path (Join-Path $server 'MyDeck.exe'))) { throw 'The PC app was not built' }

if (-not $NoStart) { Start-Process -FilePath (Join-Path $server 'MyDeck.exe') -ArgumentList "`"$Root`"" }
Write-Host "Done: $out" -ForegroundColor Green
