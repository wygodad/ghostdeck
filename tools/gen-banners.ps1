# Generates the README banners (docs/images/banner*.png).
#
# Text is drawn with WPF DrawingContext.DrawText, i.e. ordinary text rendering to a bitmap.
# The banners are raster images of text - no font file and no glyph outline is redistributed.
# (An earlier version emitted SVG with FormattedText.BuildGeometry glyph paths; that exported
# Windows font outlines as vector data, which is not what the font terms allow.)
#
# Run on Windows (needs Segoe UI + Consolas and the WPF stack):
#   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools\gen-banners.ps1
#
# Layout is authored in a 1280x300 logical space and rendered at 2x (2560x600) so the images
# stay sharp on HiDPI screens; GitHub scales them down to the available width.
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

$inv    = [System.Globalization.CultureInfo]::InvariantCulture
$outDir = Join-Path $PSScriptRoot '..\docs\images'
$W = 1280.0; $H = 300.0; $SCALE = 2.0

function Br([string]$hex, [double]$opacity = 1.0) {
    $c = [Windows.Media.ColorConverter]::ConvertFromString($hex)
    $b = New-Object Windows.Media.SolidColorBrush($c)
    $b.Opacity = $opacity
    $b.Freeze()
    $b
}
function Pen([string]$hex, [double]$thickness = 1.0) {
    $p = New-Object Windows.Media.Pen((Br $hex), $thickness)
    $p.Freeze()
    $p
}
function Rct([double]$x, [double]$y, [double]$w, [double]$h) { New-Object Windows.Rect($x, $y, $w, $h) }
function Pt([double]$x, [double]$y) { New-Object Windows.Point($x, $y) }

function New-TF([string]$family, [string]$weight) {
    $w = switch ($weight) { 'bold' { [Windows.FontWeights]::Bold } 'semibold' { [Windows.FontWeights]::SemiBold } default { [Windows.FontWeights]::Normal } }
    New-Object Windows.Media.Typeface((New-Object Windows.Media.FontFamily($family)), [Windows.FontStyles]::Normal, $w, [Windows.FontStretches]::Normal)
}
function New-FT([string]$text, [string]$family, [double]$size, [string]$weight, [string]$fill) {
    $tf = New-TF $family $weight
    New-Object Windows.Media.FormattedText($text, $inv, [Windows.FlowDirection]::LeftToRight, $tf, $size, (Br $fill), 1.0)
}
function Measure-Run([string]$text, [string]$family, [double]$size, [string]$weight) {
    (New-FT $text $family $size $weight '#FFFFFF').WidthIncludingTrailingWhitespace
}

# left-anchored sequence of coloured runs sharing one baseline
function Draw-Line($dc, [double]$x, [double]$yBase, [object[]]$runs) {
    $cx = $x
    foreach ($r in $runs) {
        $ft = New-FT $r.t $r.f $r.s $r.w $r.c
        $dc.DrawText($ft, (Pt $cx ($yBase - $ft.Baseline)))
        $cx += $ft.WidthIncludingTrailingWhitespace
    }
}
# centre-anchored version: centres the WHOLE run sequence on cx
function Draw-CLine($dc, [double]$cx, [double]$yBase, [object[]]$runs) {
    $tot = 0.0
    foreach ($r in $runs) { $tot += Measure-Run $r.t $r.f $r.s $r.w }
    Draw-Line $dc ($cx - $tot / 2) $yBase $runs
}
function TxtR([string]$t, [double]$s, [string]$c, [string]$w = 'normal', [string]$f = 'Segoe UI') { @{t=$t; s=$s; c=$c; w=$w; f=$f} }
function M([string]$t, [double]$s, [string]$c, [string]$w = 'normal') { @{t=$t; s=$s; c=$c; w=$w; f='Consolas'} }

function New-Layer([scriptblock]$draw) {
    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    & $draw $dc
    $dc.Close()
    $v
}
function Save-Banner([string]$name, [Windows.Media.Visual[]]$layers) {
    $root = New-Object Windows.Media.ContainerVisual
    foreach ($l in $layers) { $root.Children.Add($l) | Out-Null }
    $dpi = 96.0 * $SCALE
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap(
        [int]($W * $SCALE), [int]($H * $SCALE), $dpi, $dpi, [Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($root)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb)) | Out-Null
    $path = Join-Path $outDir $name
    $fs = [IO.File]::Create($path)
    $enc.Save($fs)
    $fs.Close()
}

$hexDim = '#131C2C'
$hexRows = @(
    @(40,  'A0: 31 37 53 31 49 4D 53 31 2E 31 31 34 30 31 32 39'),
    @(70,  'C0: 00 00 06 26 00 00 00 0F 00 B5 00 B1 00 00 00 00'),
    @(130, 'E0: E2 00 00 64 11 00 00 40 00 00 00 00 00 C2 00 00'),
    @(160, 'F0: 00 00 70 00 39 00 00 00 00 00 00 00 00 01 00 00'),
    @(190, '60: 00 00 00 00 00 00 00 00 3A 00 3C 41 46 4E 55 5A'),
    @(220, '70: 64 2D 19 23 2D 37 41 4B 50 64 0A 03 03 03 03 03'),
    @(250, '80: 37 00 3C 41 46 4B 50 55 64 1E 14 1E 28 32 3C 46'),
    @(280, '90: 50 64 0A 03 03 03 02 03 06 00 00 06 00 00 00 00'))
function Draw-HexMatrix($dc, [double]$x) {
    foreach ($row in $hexRows) { Draw-Line $dc $x $row[0] @( (M $row[1] 15 $hexDim) ) }
    Draw-Line $dc $x 100 @( (M 'D0: 00 00 C1 83 ' 15 $hexDim), (M '1D' 15 '#0E2A3C'), (M ' 00 05 80 00 01 00 00 00 00 00 00' 15 $hexDim) )
}

# ================= banner.png (05B, header) =================
Save-Banner 'banner.png' @( (New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    Draw-HexMatrix $dc 30
    $dc.DrawRoundedRectangle($null, (Pen '#3DE3FF' 2), (Rct 332 82 40 26), 5, 5)
    Draw-CLine $dc 352 101 @( (M '1D' 15 '#3DE3FF' 'bold') )
    $dc.DrawRectangle((Br '#05070B' .92), $null, (Rct 380 84 286 24))
    Draw-Line $dc 388 101 @( (M ([char]0x2190 + ' one byte brings Silent back') 15 '#3DE3FF') )
    Draw-Line $dc 700 132 @( (TxtR 'Ghost' 60 '#F3F7FF' 'bold'), (TxtR 'Deck' 60 '#3DE3FF' 'bold') )
    Draw-Line $dc 702 178 @( (TxtR 'Restore Silent. Drive the fans. Read the machine.' 20 '#A4ADBD') )
    Draw-Line $dc 702 210 @( (TxtR ('MSI laptops ' + [char]0xB7 + ' no kernel driver ' + [char]0xB7 + ' anti-cheat safe') 16 '#566072') )
}) )

# ================= banner-profiles.png (07) =================
Save-Banner 'banner-profiles.png' @( (New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    Draw-CLine $dc 640 128 @( (TxtR 'Ghost' 62 '#F3F7FF' 'bold'), (TxtR 'Deck' 62 '#3DE3FF' 'bold') )
    $dc.DrawRoundedRectangle((Br '#0F1B33'), (Pen '#3C7DFF'), (Rct 356 164 130 42), 21, 21)
    Draw-CLine $dc 421 191 @( (TxtR 'Silent' 18 '#7FA8FF' 'semibold') )
    $dc.DrawRoundedRectangle((Br '#241B0C'), (Pen '#FFC15D'), (Rct 502 164 150 42), 21, 21)
    Draw-CLine $dc 577 191 @( (TxtR 'Balanced' 18 '#FFC15D' 'semibold') )
    $dc.DrawRoundedRectangle((Br '#2A0D1B'), (Pen '#FF2F7D'), (Rct 668 164 140 42), 21, 21)
    Draw-CLine $dc 738 191 @( (TxtR 'Extreme' 18 '#FF6FA5' 'semibold') )
    $dc.DrawRoundedRectangle((Br '#0C2418'), (Pen '#61E7A4'), (Rct 824 164 180 42), 21, 21)
    Draw-CLine $dc 914 191 @( (TxtR 'Super Battery' 18 '#61E7A4' 'semibold') )
    Draw-CLine $dc 640 248 @( (TxtR 'The profiles MSI Center dropped - one click away' 17 '#566072') )
}) )

# ================= banner-thermal.png (11) =================
$deg = [char]0xB0
Save-Banner 'banner-thermal.png' @( (New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = Pt 0 0; $g.EndPoint = Pt 1 0
    foreach ($s in @(@(0,'#3C7DFF'), @(.4,'#3DE3FF'), @(.7,'#FFC15D'), @(1,'#FF2F7D'))) {
        $g.GradientStops.Add((New-Object Windows.Media.GradientStop([Windows.Media.ColorConverter]::ConvertFromString($s[1]), [double]$s[0])))
    }
    $g.Freeze()
    $dc.PushOpacity(.85)
    $dc.DrawRoundedRectangle($g, $null, (Rct 0 236 $W 10), 5, 5)
    $dc.PushOpacity(.5);  $dc.DrawRoundedRectangle($g, $null, (Rct 0 254 $W 4), 2, 2);  $dc.Pop()
    $dc.PushOpacity(.25); $dc.DrawRoundedRectangle($g, $null, (Rct 0 264 $W 2), 1, 1);  $dc.Pop()
    $dc.Pop()
    Draw-Line $dc 34   228 @( (M ('30' + $deg) 14 '#566072') )
    Draw-Line $dc 560  228 @( (M ('62' + $deg) 14 '#566072') )
    Draw-Line $dc 1200 228 @( (M ('95' + $deg) 14 '#566072') )
    $dc.DrawEllipse($null, (Pen '#F3F7FF' 2.5), (Pt 500 243), 9, 9)
    Draw-CLine $dc 640 126 @( (TxtR 'Ghost' 62 '#F3F7FF' 'bold'), (TxtR 'Deck' 62 '#3DE3FF' 'bold') )
    Draw-CLine $dc 640 172 @( (TxtR 'Keep it cool. Keep it quiet. Keep control.' 19 '#A4ADBD') )
}) )

# ================= banner-hologram.png (03) =================
# Three stacked layers: background, blurred+sharp text (the SVG feGaussianBlur/feMerge glow),
# then the scanline overlay and the horizontal scan bar.
$holoBg = New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    $r = New-Object Windows.Media.RadialGradientBrush
    $r.Center = Pt .5 .5; $r.GradientOrigin = Pt .5 .5; $r.RadiusX = .7; $r.RadiusY = .7
    $r.GradientStops.Add((New-Object Windows.Media.GradientStop([Windows.Media.ColorConverter]::ConvertFromString('#0E2A3C'), 0.0)))
    $r.GradientStops.Add((New-Object Windows.Media.GradientStop([Windows.Media.ColorConverter]::ConvertFromString('#05070B'), 1.0)))
    $r.Freeze()
    $dc.DrawRectangle($r, $null, (Rct 0 0 $W $H))
}
$holoGlow = New-Layer {
    param($dc)
    Draw-CLine $dc 640 150 @( (TxtR 'Ghost' 72 '#F3F7FF' 'bold'), (TxtR 'Deck' 72 '#3DE3FF' 'bold') )
}
$holoGlow.Effect = New-Object Windows.Media.Effects.BlurEffect -Property @{ Radius = 12 }
$holoText = New-Layer {
    param($dc)
    Draw-CLine $dc 640 150 @( (TxtR 'Ghost' 72 '#F3F7FF' 'bold'), (TxtR 'Deck' 72 '#3DE3FF' 'bold') )
    $dc.PushOpacity(.85)
    Draw-CLine $dc 640 205 @( (TxtR 'INDEPENDENT POWER & FAN CONTROL FOR MSI LAPTOPS' 19 '#7BE9FF') )
    $dc.Pop()
}
$holoOver = New-Layer {
    param($dc)
    # 4x4 tile with a 2px bar = the SVG scanline pattern
    $tile = New-Object Windows.Media.DrawingGroup
    $gd = New-Object Windows.Media.GeometryDrawing((Br '#0A121C'), $null, (New-Object Windows.Media.RectangleGeometry((Rct 0 0 4 2))))
    $tile.Children.Add($gd)
    $tile.Freeze()
    $db = New-Object Windows.Media.DrawingBrush($tile)
    $db.TileMode = [Windows.Media.TileMode]::Tile
    $db.ViewportUnits = [Windows.Media.BrushMappingMode]::Absolute
    $db.Viewport = Rct 0 0 4 4
    $db.Stretch = [Windows.Media.Stretch]::None
    $db.Opacity = .55
    $db.Freeze()
    $dc.DrawRectangle($db, $null, (Rct 0 0 $W $H))
    $dc.DrawRectangle((Br '#3DE3FF' .25), $null, (Rct 0 118 $W 2))
}
Save-Banner 'banner-hologram.png' @($holoBg, $holoGlow, $holoText, $holoOver)

# ================= banner-glitch.png (12) =================
Save-Banner 'banner-glitch.png' @( (New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    $dc.PushOpacity(.7); Draw-CLine $dc 637 158 @( (TxtR 'GhostDeck' 74 '#FF2F7D' 'bold') ); $dc.Pop()
    $dc.PushOpacity(.7); Draw-CLine $dc 643 162 @( (TxtR 'GhostDeck' 74 '#3DE3FF' 'bold') ); $dc.Pop()
    Draw-CLine $dc 640 160 @( (TxtR 'Ghost' 74 '#F3F7FF' 'bold'), (TxtR 'Deck' 74 '#3DE3FF' 'bold') )
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 380 98 520 7))
    $dc.DrawRectangle((Br '#3DE3FF' .6), $null, (Rct 420 101 440 2))
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 430 138 430 5))
    $dc.DrawRectangle((Br '#FF2F7D' .55), $null, (Rct 470 140 350 2))
    Draw-CLine $dc 640 216 @( (M '>> power & fan control // MSI laptops // no kernel driver <<' 17 '#A4ADBD') )
}) )

# ================= banner-terminal.png (04) =================
Save-Banner 'banner-terminal.png' @( (New-Layer {
    param($dc)
    $dc.DrawRectangle((Br '#05070B'), $null, (Rct 0 0 $W $H))
    $dc.DrawRoundedRectangle((Br '#0A0D14'), (Pen '#232C40'), (Rct 70 34 1140 232), 12, 12)
    $dc.DrawEllipse((Br '#FF2F7D'), $null, (Pt 102 62), 6, 6)
    $dc.DrawEllipse((Br '#FFC15D'), $null, (Pt 124 62), 6, 6)
    $dc.DrawEllipse((Br '#61E7A4'), $null, (Pt 146 62), 6, 6)
    Draw-Line $dc 170 68  @( (M 'ghostdeck.exe' 15 '#566072') )
    Draw-Line $dc 104 122 @( (M '>' 21 '#61E7A4'), (M ' ghostdeck ' 21 '#F3F7FF'), (M '--profile silent' 21 '#3DE3FF') )
    Draw-Line $dc 104 158 @( (M ('Silent restored ' + [char]0xB7 + ' 0xD4 = 0x1D ' + [char]0xB7 + ' ~30 W cap') 21 '#A4ADBD') )
    Draw-Line $dc 104 212 @( (M 'Ghost' 34 '#F3F7FF' 'bold'), (M 'Deck' 34 '#3DE3FF' 'bold'), (M '  - the Silent profile MSI removed' 21 '#566072') )
    $dc.DrawRectangle((Br '#3DE3FF'), $null, (Rct 360 240 13 24))
    Draw-Line $dc 1076 120 @( (M '(\_/)' 17 '#153444') )
    Draw-Line $dc 1076 140 @( (M '(o o)' 17 '#153444') )
    Draw-Line $dc 1070 160 @( (M '/| |\' 17 '#153444') )
}) )

Get-ChildItem (Join-Path $outDir 'banner*.png') | ForEach-Object { '{0}  {1:N0} B' -f $_.Name, $_.Length }
