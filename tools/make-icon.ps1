# Draws the VM GPU Share logo (a monitor with a GPU chip on a blue tile) at every icon
# size and packs them into Assets\app.ico. Also writes docs\logo.png (256 px).
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Min($r, [Math]::Min($w, $h) / 2)
    if ($r -le 0) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the logo at $size px. Coordinates are in a 256-unit design grid.
function Draw-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $small = $size -le 32   # the chip's pins only read from 40 px up

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $screenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 63, 135))
    $chipBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 139, 220, 255))

    # Tile: rounded square with a blue diagonal gradient
    $inset = if ($small) { 0 } else { 12 }
    $tile = RoundRect ($inset * $s) ($inset * $s) ((256 - 2 * $inset) * $s) ((256 - 2 * $inset) * $s) (52 * $s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), ([System.Drawing.Color]::FromArgb(255, 47, 155, 255)), ([System.Drawing.Color]::FromArgb(255, 10, 88, 194))
    $g.FillPath($grad, $tile)

    if ($small) {
        # Bolder, simpler monitor for 16-32 px
        $g.FillPath($white, (RoundRect (36 * $s) (52 * $s) (184 * $s) (128 * $s) (20 * $s)))
        $g.FillPath($screenBrush, (RoundRect (56 * $s) (72 * $s) (144 * $s) (88 * $s) (6 * $s)))
        $g.FillRectangle($white, (108 * $s), (180 * $s), (40 * $s), (22 * $s))
        $g.FillPath($white, (RoundRect (72 * $s) (196 * $s) (112 * $s) (24 * $s) (10 * $s)))
        $g.FillPath($chipBrush, (RoundRect (100 * $s) (88 * $s) (56 * $s) (56 * $s) (8 * $s)))
    }
    else {
        # Monitor: white bezel, dark screen, neck and base
        $g.FillPath($white, (RoundRect (44 * $s) (60 * $s) (168 * $s) (114 * $s) (14 * $s)))
        $g.FillPath($screenBrush, (RoundRect (55 * $s) (71 * $s) (146 * $s) (92 * $s) (6 * $s)))
        $g.FillRectangle($white, (112 * $s), (172 * $s), (32 * $s), (24 * $s))
        $g.FillPath($white, (RoundRect (82 * $s) (192 * $s) (92 * $s) (14 * $s) (7 * $s)))

        # GPU chip in the middle of the screen: body, die and three pins per side
        $cx = 128; $cy = 117
        $g.FillPath($chipBrush, (RoundRect (($cx - 25) * $s) (($cy - 25) * $s) (50 * $s) (50 * $s) (6 * $s)))
        $g.FillPath($screenBrush, (RoundRect (($cx - 13) * $s) (($cy - 13) * $s) (26 * $s) (26 * $s) (3 * $s)))
        foreach ($o in -13, 0, 13) {
            $g.FillRectangle($chipBrush, (($cx + $o - 3) * $s), (($cy - 34) * $s), (6 * $s), (9 * $s))   # top
            $g.FillRectangle($chipBrush, (($cx + $o - 3) * $s), (($cy + 25) * $s), (6 * $s), (9 * $s))   # bottom
            $g.FillRectangle($chipBrush, (($cx - 34) * $s), (($cy + $o - 3) * $s), (9 * $s), (6 * $s))   # left
            $g.FillRectangle($chipBrush, (($cx + 25) * $s), (($cy + $o - 3) * $s), (9 * $s), (6 * $s))   # right
        }
    }
    $g.Dispose()
    return $bmp
}

# Classic icon image: 32-bit BGRA bitmap (bottom-up, double height) plus a 1-bit AND mask.
# Every icon reader understands this; PNG is only used for 256 px, as Windows itself does.
function To-Dib([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $pixels = New-Object byte[] ($w * $h * 4)
    for ($y = 0; $y -lt $h; $y++) {
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $pixels, $y * $w * 4, $w * 4)
    }
    $bmp.UnlockBits($data)
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]0); $bw.Write([uint32]($w * $h * 4 + $maskStride * $h)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $w * 4, $w * 4) }
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $w; $x++) {
            if ($pixels[($y * $w + $x) * 4 + 3] -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $bw.Write($row)
    }
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = Draw-Logo $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save((Join-Path $root 'docs\logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
    }
    else { $bytes = To-Dib $bmp }
    $bmp.Dispose()
    , $bytes
}

# ICO file: header, one directory entry per size, then the PNG images
New-Item -ItemType Directory -Force (Join-Path $root 'Assets') | Out-Null
$ico = Join-Path $root 'Assets\app.ico'
$fs = [System.IO.File]::Create($ico)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Dispose()
"Wrote $ico ($((Get-Item $ico).Length) bytes, sizes $($sizes -join ', '))"
