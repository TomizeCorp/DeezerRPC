[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\discord-deezer-monochrome.png'
}

$size = 1024
$bitmap = [System.Drawing.Bitmap]::new(
    $size,
    $size,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 230, 230, 230))

try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $barWidth = 62.0
    $gap = 22.0
    $left = 62.0
    $top = @(365, 280, 170, 245, 330, 395, 330, 245, 170, 280, 365)
    $bottom = @(505, 590, 700, 785, 875, 940, 875, 785, 700, 590, 505)

    for ($index = 0; $index -lt $top.Count; $index++) {
        $x = $left + $index * ($barWidth + $gap)
        $y = [double]$top[$index]
        $height = [double]$bottom[$index] - $y
        $radius = $barWidth / 2.0
        $graphics.FillRectangle($brush, [single]$x, [single]($y + $radius), [single]$barWidth, [single]($height - $barWidth))
        $graphics.FillEllipse($brush, [single]$x, [single]$y, [single]$barWidth, [single]$barWidth)
        $graphics.FillEllipse($brush, [single]$x, [single]($y + $height - $barWidth), [single]$barWidth, [single]$barWidth)
    }

    $directory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Logo Discord généré : $OutputPath"
