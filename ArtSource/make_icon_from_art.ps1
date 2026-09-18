param([string]$Source, [string]$OutDir)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

# Where the artwork sits inside the supplied frame, measured, plus the sky
# colours at its top and bottom edges.
$CROP = New-Object System.Drawing.Rectangle 36, 24, 590, 578
$SKY_TOP = [System.Drawing.Color]::FromArgb(255, 8, 114, 233)
$SKY_BOTTOM = [System.Drawing.Color]::FromArgb(255, 158, 201, 246)
$SIZE = 1024

$art = New-Object System.Drawing.Bitmap $Source

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap $SIZE, $SIZE, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Fill-Sky($g, $height) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.PointF 0, -1), (New-Object System.Drawing.PointF 0, ($height + 1)),
        $SKY_TOP, $SKY_BOTTOM)
    $g.FillRectangle($brush, (New-Object System.Drawing.RectangleF 0, 0, $SIZE, $SIZE))
}

# The artwork's own rounded corners are white in the file. Drawing it inside a
# rounded path clips them away, so the corners show sky instead of paper.
function Rounded-Path($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Art($g, $x, $y, $size) {
    $path = Rounded-Path $x $y $size $size ($size * 0.16)
    $state = $g.Save()
    $g.SetClip($path)
    $g.DrawImage($art, (New-Object System.Drawing.Rectangle $x, $y, $size, $size),
                 $CROP.X, $CROP.Y, $CROP.Width, $CROP.Height,
                 [System.Drawing.GraphicsUnit]::Pixel)
    $g.Restore($state)
    $path.Dispose()
}

function Save-Canvas($pair, $name) {
    $pair[1].Dispose()
    $path = Join-Path $OutDir $name
    $pair[0].Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $pair[0].Dispose()
    Write-Output ("wrote " + $path)
}

# 1. the whole icon: sky, then the artwork over it, with a little room at the
#    sides so a round launcher mask does not slice the ends off the name
$c = New-Canvas
Fill-Sky $c[1] $SIZE
$margin = [int]($SIZE * 0.05)
Draw-Art $c[1] $margin $margin ($SIZE - $margin * 2)
Save-Canvas $c "ScrabbyIcon.png"

# 1b. what a round launcher shows, for looking at only
$c = New-Canvas
Fill-Sky $c[1] $SIZE
Draw-Art $c[1] $margin $margin ($SIZE - $margin * 2)
$mask = New-Object System.Drawing.Drawing2D.GraphicsPath
$mask.AddEllipse(0, 0, $SIZE, $SIZE)
$outside = New-Object System.Drawing.Drawing2D.GraphicsPath
$outside.AddRectangle((New-Object System.Drawing.Rectangle 0, 0, $SIZE, $SIZE))
$region = New-Object System.Drawing.Region $outside
$region.Exclude($mask)
$c[1].FillRegion((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 24, 28, 34))), $region)
Save-Canvas $c "Preview_Round.png"

# 2. adaptive background: just the sky, which is what shows around the edges
#    when a launcher crops
$c = New-Canvas
Fill-Sky $c[1] $SIZE
Save-Canvas $c "ScrabbyIcon_Background.png"

# 3. adaptive foreground: the artwork at two thirds, which is the part a
#    launcher always shows, on transparency
$c = New-Canvas
$inset = [int]($SIZE * 0.1667)
Draw-Art $c[1] $inset $inset ($SIZE - $inset * 2)
Save-Canvas $c "ScrabbyIcon_Foreground.png"

$art.Dispose()
