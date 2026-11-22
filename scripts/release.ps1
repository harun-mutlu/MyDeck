$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\env.ps1"
$version = Get-AppVersion
$dist = Join-Path $Root 'dist'
$stage = Join-Path $Root 'out\release'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

& (Join-Path $PSScriptRoot 'build.ps1') -NoStart

Write-Host "[release] MyDeck $version" -ForegroundColor Cyan
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Native 'dotnet' @('publish', (Join-Path $Root 'windows\MyDeck.Server\MyDeck.Server.csproj'), '-c', 'Release',
    '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', '-o', $stage, '--nologo')
if (-not (Test-Path (Join-Path $stage 'MyDeck.exe'))) { throw 'Publish failed' }
Copy-Item (Join-Path $Root 'out\MyDeck.apk') (Join-Path $stage 'MyDeck.apk')
Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $stage 'LICENSE.txt')

$zip = Join-Path $dist 'MyDeck-win-x64.zip'
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -Path "$zip.sha256" -Value "$hash  MyDeck-win-x64.zip" -Encoding ascii

Get-ChildItem (Join-Path $Root 'samples\plugins') -Directory | ForEach-Object {
    $pkg = Join-Path $dist "$($_.Name).mydeckplugin"
    $tmp = "$pkg.zip"
    Remove-Item $pkg, $tmp -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $_.FullName '*') -DestinationPath $tmp
    Move-Item $tmp $pkg
}

Write-Host ''
Write-Host "Built MyDeck $version into $dist" -ForegroundColor Green
Write-Host "Official releases are built by GitHub Actions when tag v$version is pushed."
