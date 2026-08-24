# Generates the Neyra Optimizer application icon (multi-size PNG-in-ICO).
# Usage: powershell -File scripts\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'src\NeyraOptimizer.App\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-NeyraPng([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  $r = [Math]::Max(2, [int]($s * 0.22))
  $rect = New-Object System.Drawing.Rectangle(0, 0, ($s - 1), ($s - 1))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
  $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
  $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
  $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
  $path.CloseFigure()

  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
    [System.Drawing.Color]::FromArgb(255,11,30,58),
    [System.Drawing.Color]::FromArgb(255,20,80,163), 55.0)
  $g.FillPath($grad, $path)

  $hl = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60,255,255,255), [Math]::Max(1.0, $s / 48.0))
  $g.DrawPath($hl, $path)

  # Lightning bolt polygon in normalized coordinates.
  $pts = @(
    @(0.560,0.100), @(0.250,0.545), @(0.455,0.545),
    @(0.400,0.900), @(0.740,0.430), @(0.520,0.430), @(0.640,0.100)
  )
  $poly = New-Object 'System.Drawing.PointF[]' ($pts.Count)
  for ($i = 0; $i -lt $pts.Count; $i++) {
    $poly[$i] = New-Object System.Drawing.PointF([float]($pts[$i][0] * $s), [float]($pts[$i][1] * $s))
  }
  if ($s -ge 32) {
    $glowW = [Math]::Max(2.0, $s / 12.0)
    $glow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70,120,220,255), $glowW)
    $glow.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($glow, $poly)
  }
  $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
    [System.Drawing.Color]::White,
    [System.Drawing.Color]::FromArgb(255,190,235,255), 70.0)
  $g.FillPolygon($b, $poly)

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bytes = $ms.ToArray()
  $g.Dispose(); $bmp.Dispose(); $path.Dispose(); $grad.Dispose(); $b.Dispose(); $ms.Dispose()
  return ,$bytes
}

$sizes = @(16,24,32,48,64,128,256)
$images = @{}
foreach ($s in $sizes) { $images[$s] = New-NeyraPng $s }

$icoPath = Join-Path $outDir 'neyra.ico'
$fs = [System.IO.File]::Create($icoPath)
try {
  $bw = New-Object System.IO.BinaryWriter($fs)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  foreach ($s in $sizes) {
    $data = [byte[]]$images[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
  }
  foreach ($s in $sizes) { $bw.Write([byte[]]$images[$s]) }
  $bw.Flush(); $bw.Dispose()
} finally { $fs.Dispose() }

[System.IO.File]::WriteAllBytes((Join-Path $outDir 'neyra-icon-256.png'), [byte[]]$images[256])
Write-Output ("ICO: {0} bytes; PNG256: {1} bytes" -f (Get-Item $icoPath).Length, (Get-Item (Join-Path $outDir 'neyra-icon-256.png')).Length)
