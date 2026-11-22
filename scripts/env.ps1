$Root  = Split-Path -Parent $PSScriptRoot
$Tools = Join-Path $Root 'tools'
if (Test-Path (Join-Path $Tools 'jdk')) {
    $env:JAVA_HOME         = Join-Path $Tools 'jdk'
    $env:ANDROID_HOME      = Join-Path $Tools 'android-sdk'
    $env:ANDROID_SDK_ROOT  = $env:ANDROID_HOME
    $env:ANDROID_USER_HOME = Join-Path $Tools 'android-user'
    $env:GRADLE_USER_HOME  = Join-Path $Tools 'gradle-home'
    $env:Path = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\cmdline-tools\latest\bin;$env:Path"
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$File, [string[]]$Arguments = @(), [string[]]$InputLines)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($InputLines) { $InputLines | & $File @Arguments 2>&1 | ForEach-Object { "$_" } }
        else { & $File @Arguments 2>&1 | ForEach-Object { "$_" } }
        return $LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
}

function Get-AppVersion {
    $xml = [xml](Get-Content (Join-Path $Root 'windows\MyDeck.Server\MyDeck.Server.csproj') -Raw)
    $v = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $v) { throw 'No <Version> in MyDeck.Server.csproj' }
    return $v
}

function Import-Signing([string]$Path) {
    if (-not $Path -or -not (Test-Path $Path)) { return }
    foreach ($line in Get-Content $Path) {
        if ($line -cmatch '^\s*([A-Z_]+)\s*=\s*(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2].Trim() }
    }
}
if ($env:MYDECK_SIGNING_FILE) { Import-Signing $env:MYDECK_SIGNING_FILE } else { Import-Signing (Join-Path $Root 'signing.env') }
