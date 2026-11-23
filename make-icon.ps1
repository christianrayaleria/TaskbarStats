# Regenerates app.ico: purple rounded square with three rising bars, drawn per size so 16px stays crisp.
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = [Math]::Max(3, $s * 0.22); $d = $r * 2; $e = $s - 1
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($e - $d, 0, $d, $d, 270, 90)
    $path.AddArc($e - $d, $e - $d, $d, $d, 0, 90); $path.AddArc(0, $e - $d, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(0, 0)), ([Drawing.Point]::new($s, $s)), ([Drawing.Color]::FromArgb(167, 112, 255)), ([Drawing.Color]::FromArgb(91, 33, 182))
    $g.FillPath($bg, $path)
    # three bars, snapped to whole pixels
    $pad = [Math]::Round($s * 0.22); $gap = [Math]::Max(1, [Math]::Round($s * 0.07))
    $bw = [Math]::Floor(($s - 2 * $pad - 2 * $gap) / 3); $base = $s - $pad
    $white = [Drawing.Brushes]::White
    $i = 0
    foreach ($h in 0.38, 0.62, 0.86) {
        $bh = [Math]::Round(($s - 2 * $pad) * $h)
        $x = $pad + $i * ($bw + $gap)
        $g.FillRectangle($white, [int]$x, [int]($base - $bh), [int]$bw, [int]$bh)
        $i++
    }
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    ,$ms.ToArray()
}
# ICO container with PNG-encoded entries
$out = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $s = $sizes[$k]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$k].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$k].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
# preview
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon-preview.png'), $pngs[-1])
