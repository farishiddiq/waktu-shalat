param([string]$Out = "app.ico")
# Black rounded square with a white crescent, packed as a multi-size PNG .ico
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [single]($s * 0.23); $d = $r * 2; $w = [single]($s - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
    $path.AddArc(0, $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::Black, $path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 58, 58, 60)), ([Math]::Max(1, $s / 64))
    $g.DrawPath($pen, $path)

    # crescent: white disc, then a black disc offset up-right
    $cx = $s * 0.47; $cy = $s * 0.52; $R = $s * 0.30
    $g.FillEllipse([System.Drawing.Brushes]::White, [single]($cx - $R), [single]($cy - $R), [single]($R * 2), [single]($R * 2))
    $ix = $cx + $s * 0.12; $iy = $cy - $s * 0.07; $IR = $s * 0.25
    $g.FillEllipse([System.Drawing.Brushes]::Black, [single]($ix - $IR), [single]($iy - $IR), [single]($IR * 2), [single]($IR * 2))
    # small star
    if ($s -ge 32) {
        $sr = $s * 0.045; $sx = $s * 0.70; $sy = $s * 0.36
        $emerald = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 52, 211, 153))
        $g.FillEllipse($emerald, [single]($sx - $sr), [single]($sy - $sr), [single]($sr * 2), [single]($sr * 2))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
