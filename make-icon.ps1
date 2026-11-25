# Draws the app icon (a target ring on a dark tile): app.ico for Windows and mac/icon-1024.png for
# the Mac build. Run once; the results are committed and the build scripts just embed them.
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size, [double]$margin) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded dark tile
    $inset = [single]($size * $margin)
    $w = [single]($size - 1 - 2 * $inset)
    $r = [single]($w * 0.22)
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($inset, $inset, $r * 2, $r * 2, 180, 90)
    $tile.AddArc($inset + $w - $r * 2, $inset, $r * 2, $r * 2, 270, 90)
    $tile.AddArc($inset + $w - $r * 2, $inset + $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $tile.AddArc($inset, $inset + $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $tile.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 22, 27, 38))
    $g.FillPath($bg, $tile)

    # ring + centre dot
    $accent = [System.Drawing.Color]::FromArgb(255, 52, 211, 153)
    $c = [single]($size / 2.0)
    $ringR = [single]($w * 0.30)
    $pen = New-Object System.Drawing.Pen $accent, ([single][Math]::Max(1.6, $w * 0.085))
    $g.DrawEllipse($pen, $c - $ringR, $c - $ringR, $ringR * 2, $ringR * 2)
    $dotR = [single][Math]::Max(1.3, $w * 0.095)
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillEllipse($dot, $c - $dotR, $c - $dotR, $dotR * 2, $dotR * 2)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# --- Windows: ICO container (6-byte header, one 16-byte entry per image, then the PNG payloads)
$sizes = 16, 24, 32, 48, 64, 256
$images = foreach ($size in $sizes) { , (New-IconPng $size 0) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
"Wrote app.ico ($($out.Length) bytes)"

# --- Mac: one large PNG with the margin macOS icons carry; mac/build.sh scales it into an .icns
$macPng = New-IconPng 1024 0.098
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'mac\icon-1024.png'), $macPng)
"Wrote mac\icon-1024.png ($($macPng.Length) bytes)"
