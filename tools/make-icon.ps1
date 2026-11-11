# Draws the EuroWatcher icon (a gold star on a deep blue tile) and saves it as
# assets\EuroWatcher.ico with all the sizes Windows uses, plus assets\eurowatcher.png (512 px) for Linux and macOS.
# Run:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\EuroWatcher.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    # deep blue rounded tile
    $x = 2 * $s; $y = 2 * $s; $w = 60 * $s; $d = 26 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $w - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF($x, $y, $w, $w)
    $top = [System.Drawing.Color]::FromArgb(255, 52, 72, 214)
    $bottom = [System.Drawing.Color]::FromArgb(255, 22, 30, 102)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillPath($brush, $path)

    # five-pointed star, a little larger on the tiny sizes so it stays readable
    $cx = 32 * $s; $cy = 33.5 * $s
    $outer = if ($size -le 24) { 25 * $s } else { 22 * $s }
    $inner = $outer * 0.45
    $points = New-Object 'System.Drawing.PointF[]' 10
    for ($i = 0; $i -lt 10; $i++) {
        $r = if ($i % 2 -eq 0) { $outer } else { $inner }
        $a = [math]::PI * (-90 + 36 * $i) / 180
        $points[$i] = New-Object System.Drawing.PointF(($cx + $r * [math]::Cos($a)), ($cy + $r * [math]::Sin($a)))
    }
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 242, 186, 38))
    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 242, 186, 38), (2.5 * $s))
    $edge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.FillPolygon($gold, $points)
    $g.DrawPolygon($edge, $points)

    $g.Dispose()
    return $bmp
}

function Get-FrameBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$ms.ToArray()
    }
    # classic icon bitmap: header, 32-bit pixels bottom-up, then an (empty) 1-bit mask
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int]([math]::Floor(($size + 31) / 32)) * 4
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$size); $bw.Write([int]($size * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([int]0)
    $bw.Write([int]($size * $size * 4 + $maskRow * $size))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $bw.Write($pixels, $row * $size * 4, $size * 4) }
    $bw.Write((New-Object byte[] ($maskRow * $size)))
    $bw.Flush()
    return ,$ms.ToArray()
}

$frames = @()
foreach ($size in $sizes) {
    $bmp = New-Frame $size
    $frames += ,(Get-FrameBytes $bmp)
    $bmp.Dispose()
}

$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
$png = New-Frame 512
$png.Save((Join-Path $root 'assets\eurowatcher.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Output ("wrote " + $out + " (" + $file.Length + " bytes, " + $sizes.Count + " sizes)")
