Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\windows\MyDeck.Server\MyDeck.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$coral = [System.Drawing.ColorTranslator]::FromHtml("#FF6A5C")
$cyan = [System.Drawing.ColorTranslator]::FromHtml("#5EEAD4")
$slate = [System.Drawing.ColorTranslator]::FromHtml("#5A6680")

function Square($g, $color, $x, $y, $s, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $s - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $b = New-Object System.Drawing.SolidBrush $color
    $g.FillPath($b, $p); $b.Dispose(); $p.Dispose()
}

$pngs = @()
foreach ($n in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.Clear([System.Drawing.Color]::Transparent)
    $m = $n * 3 / 32; $s = $n * 12 / 32; $gap = $n * 2 / 32; $r = [Math]::Max(1, $n * 2.5 / 32)
    Square $g $coral $m $m $s $r
    Square $g $cyan ($m + $s + $gap) $m $s $r
    Square $g $cyan $m ($m + $s + $gap) $s $r
    Square $g $slate ($m + $s + $gap) ($m + $s + $gap) $s $r
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $pngs += , @($n, $ms.ToArray())
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
    $n = $e[0]; $data = $e[1]
    $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n }))); $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($e in $pngs) { $w.Write($e[1]) }
$w.Close()
"Icon written: $out"
