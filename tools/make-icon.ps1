# Gera app.ico (sino escuro sobre quadrado âmbar arredondado) em vários tamanhos.
param([string]$Out = (Join-Path $PSScriptRoot "..\app.ico"), [string]$PreviewPng = "")

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $s * 0.04)
    $x = $pad; $y = $pad; $w = $s - 2 * $pad; $h = $s - 2 * $pad
    $d = [Math]::Max(3, $s * 0.44)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 255, 204, 98)
    $bot = [System.Drawing.Color]::FromArgb(255, 238, 158, 38)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF 0, 0)), ((New-Object System.Drawing.PointF 0, $s)), $top, $bot
    $g.FillPath($grad, $path)

    # Sino desenhado (corpo + borda inferior + badalo)
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 35, 42))
    $cx = $s / 2.0
    $bw = $s * 0.50        # largura da base do sino
    $topY = $s * 0.22
    $baseY = $s * 0.66
    $bell = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bell.AddBezier(
        (New-Object System.Drawing.PointF ($cx - $bw / 2), $baseY),
        (New-Object System.Drawing.PointF ($cx - $bw * 0.36), ($baseY - $s * 0.08)),
        (New-Object System.Drawing.PointF ($cx - $bw * 0.40), $topY),
        (New-Object System.Drawing.PointF $cx, $topY))
    $bell.AddBezier(
        (New-Object System.Drawing.PointF $cx, $topY),
        (New-Object System.Drawing.PointF ($cx + $bw * 0.40), $topY),
        (New-Object System.Drawing.PointF ($cx + $bw * 0.36), ($baseY - $s * 0.08)),
        (New-Object System.Drawing.PointF ($cx + $bw / 2), $baseY))
    $bell.CloseFigure()
    $g.FillPath($ink, $bell)
    # aba inferior
    $lipH = [Math]::Max(1, $s * 0.075)
    $g.FillRectangle($ink, [single]($cx - $bw * 0.58), [single]($baseY - $lipH * 0.3), [single]($bw * 1.16), [single]$lipH)
    # badalo
    $r = $s * 0.085
    $g.FillEllipse($ink, [single]($cx - $r), [single]($baseY + $lipH * 0.45), [single]($r * 2), [single]($r * 2))
    # pino do topo
    $pr = $s * 0.045
    $g.FillEllipse($ink, [single]($cx - $pr), [single]($topY - $pr * 1.5), [single]($pr * 2), [single]($pr * 2))

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($row = $s - 1; $row -ge 0; $row--) { $bw.Write($raw, $row * $stride, $s * 4) }
    $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += , $ms.ToArray()
        if ($PreviewPng) { $bmp.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    } else {
        $images += , (Get-DibBytes $bmp)
    }
    $bmp.Dispose()
}

$icoStream = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $icoStream
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Out) | ForEach-Object { Join-Path $_ (Split-Path $Out -Leaf) }), $icoStream.ToArray())
Write-Output "Ícone gerado: $Out"
