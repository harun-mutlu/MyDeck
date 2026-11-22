param([string]$address = "1.1.1.1")
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = New-Object System.Net.NetworkInformation.Ping
try { $r = $p.Send($address, 2000) } catch { $r = $null }
if ($r -and $r.Status -eq 'Success') {
    $ms = [int]$r.RoundtripTime
    $color = if ($ms -lt 40) { '#4ADE80' } elseif ($ms -lt 100) { '#FFB000' } else { '#FF3D5A' }
    @{ value = "$ms ms"; sub = $address; color = $color; percent = [math]::Min(1, $ms / 200) } | ConvertTo-Json -Compress
} else {
    @{ value = '-'; sub = 'no answer'; color = '#FF3D5A'; percent = 1 } | ConvertTo-Json -Compress
}
