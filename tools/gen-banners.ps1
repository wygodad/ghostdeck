# Generates the README banners (docs/images/banner*.svg) as SVG with every glyph
# converted to Bezier paths, so they render identically everywhere with no font
# dependency. Text shaping (incl. kerning) comes from WPF FormattedText.BuildGeometry.
#
# Run on Windows (needs Segoe UI + Consolas and the WPF stack):
#   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools\gen-banners.ps1
#
# Pitfalls encoded below: GeometryGroup.ToString() prints the type name, so the group
# is converted through PathGeometry.CreateFromGeometry first (keeps the curves); and
# a helper named "R" would collide with PowerShell's built-in Invoke-History alias.
Add-Type -AssemblyName PresentationCore
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$outDir = Join-Path $PSScriptRoot '..\docs\images'

function New-TF([string]$family, [string]$weight) {
    $w = switch ($weight) { 'bold' { [Windows.FontWeights]::Bold } 'semibold' { [Windows.FontWeights]::SemiBold } default { [Windows.FontWeights]::Normal } }
    New-Object Windows.Media.Typeface((New-Object Windows.Media.FontFamily($family)), [Windows.FontStyles]::Normal, $w, [Windows.FontStretches]::Normal)
}
function Measure-Run([string]$text, [string]$family, [double]$size, [string]$weight) {
    $tf = New-TF $family $weight
    $ft = New-Object Windows.Media.FormattedText($text, $inv, [Windows.FlowDirection]::LeftToRight, $tf, $size, [Windows.Media.Brushes]::White, 1.0)
    ,@($ft.WidthIncludingTrailingWhitespace, $ft.Baseline)
}
function Get-RunPath([string]$text, [string]$family, [double]$size, [string]$weight, [double]$x, [double]$yBase, [string]$fill) {
    $tf = New-TF $family $weight
    $ft = New-Object Windows.Media.FormattedText($text, $inv, [Windows.FlowDirection]::LeftToRight, $tf, $size, [Windows.Media.Brushes]::White, 1.0)
    $geo = $ft.BuildGeometry([Windows.Point]::new($x, $yBase - $ft.Baseline))
    # BuildGeometry returns a GeometryGroup, whose ToString() is just the type name -
    # PathGeometry.CreateFromGeometry flattens the group into figures but KEEPS the Beziers
    $d = [Windows.Media.PathGeometry]::CreateFromGeometry($geo).ToString($inv)
    if ($d.StartsWith('F1')) { $d = $d.Substring(2) }
    if ([string]::IsNullOrWhiteSpace($d)) { return '' }
    $d = [regex]::Replace($d, '-?\d+\.\d+', { param($m) ([math]::Round([double]$m.Value, 1)).ToString($inv) })
    "<path d=""$d"" fill=""$fill""/>"
}
# left-anchored sequence of coloured runs sharing one baseline; returns SVG paths
function Get-Line([double]$x, [double]$yBase, [object[]]$runs) {
    $svg = ''; $cx = $x
    foreach ($r in $runs) {
        $svg += Get-RunPath $r.t $r.f $r.s $r.w $cx $yBase $r.c
        $cx += (Measure-Run $r.t $r.f $r.s $r.w)[0]
    }
    $svg
}
# centre-anchored version: centres the WHOLE run sequence on cx
function Get-CLine([double]$cx, [double]$yBase, [object[]]$runs) {
    $tot = 0.0
    foreach ($r in $runs) { $tot += (Measure-Run $r.t $r.f $r.s $r.w)[0] }
    Get-Line ($cx - $tot / 2) $yBase $runs
}
function TxtR([string]$t, [double]$s, [string]$c, [string]$w = 'normal', [string]$f = 'Segoe UI') { @{t=$t; s=$s; c=$c; w=$w; f=$f} }
function M([string]$t, [double]$s, [string]$c, [string]$w = 'normal') { @{t=$t; s=$s; c=$c; w=$w; f='Consolas'} }

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
function Get-HexMatrix([double]$x) {
    $svg = ''
    foreach ($row in $hexRows) { $svg += Get-Line $x $row[0] @( (M $row[1] 15 $hexDim) ) }
    $svg += Get-Line $x 100 @( (M 'D0: 00 00 C1 83 ' 15 $hexDim), (M '1D' 15 '#0E2A3C'), (M ' 00 05 80 00 01 00 00 00 00 00 00' 15 $hexDim) )
    $svg
}

# ================= banner.svg (05B, header) =================
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<rect width="1280" height="300" fill="#05070B"/>'
$b += Get-HexMatrix 30
$b += '<rect x="332" y="82" width="40" height="26" rx="5" fill="none" stroke="#3DE3FF" stroke-width="2"/>'
$b += Get-CLine 352 101 @( (M '1D' 15 '#3DE3FF' 'bold') )
$b += '<rect x="380" y="84" width="286" height="24" fill="#05070B" opacity=".92"/>'
$b += Get-Line 388 101 @( (M ([char]0x2190 + ' one byte brings Silent back') 15 '#3DE3FF') )
$b += Get-Line 700 132 @( (TxtR 'Ghost' 60 '#F3F7FF' 'bold'), (TxtR 'Deck' 60 '#3DE3FF' 'bold') )
$b += Get-Line 702 178 @( (TxtR 'Restore Silent. Drive the fans. Read the machine.' 20 '#A4ADBD') )
$b += Get-Line 702 210 @( (TxtR ('MSI laptops ' + [char]0xB7 + ' no kernel driver ' + [char]0xB7 + ' anti-cheat safe') 16 '#566072') )
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner.svg') -Value $b -Encoding UTF8

# ================= banner-profiles.svg (07) =================
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<rect width="1280" height="300" fill="#05070B"/>'
$b += Get-CLine 640 128 @( (TxtR 'Ghost' 62 '#F3F7FF' 'bold'), (TxtR 'Deck' 62 '#3DE3FF' 'bold') )
$b += '<rect x="356" y="164" width="130" height="42" rx="21" fill="#0F1B33" stroke="#3C7DFF"/>'
$b += Get-CLine 421 191 @( (TxtR 'Silent' 18 '#7FA8FF' 'semibold') )
$b += '<rect x="502" y="164" width="150" height="42" rx="21" fill="#241B0C" stroke="#FFC15D"/>'
$b += Get-CLine 577 191 @( (TxtR 'Balanced' 18 '#FFC15D' 'semibold') )
$b += '<rect x="668" y="164" width="140" height="42" rx="21" fill="#2A0D1B" stroke="#FF2F7D"/>'
$b += Get-CLine 738 191 @( (TxtR 'Extreme' 18 '#FF6FA5' 'semibold') )
$b += '<rect x="824" y="164" width="180" height="42" rx="21" fill="#0C2418" stroke="#61E7A4"/>'
$b += Get-CLine 914 191 @( (TxtR 'Super Battery' 18 '#61E7A4' 'semibold') )
$b += Get-CLine 640 248 @( (TxtR 'The profiles MSI Center dropped - one click away, on 146 models' 17 '#566072') )
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner-profiles.svg') -Value $b -Encoding UTF8

# ================= banner-thermal.svg (11) =================
$deg = [char]0xB0
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<rect width="1280" height="300" fill="#05070B"/>'
$b += '<defs><linearGradient id="tg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#3C7DFF"/><stop offset=".4" stop-color="#3DE3FF"/><stop offset=".7" stop-color="#FFC15D"/><stop offset="1" stop-color="#FF2F7D"/></linearGradient></defs>'
$b += '<g opacity=".85"><rect x="0" y="236" width="1280" height="10" rx="5" fill="url(#tg)"/><rect x="0" y="254" width="1280" height="4" rx="2" fill="url(#tg)" opacity=".5"/><rect x="0" y="264" width="1280" height="2" rx="1" fill="url(#tg)" opacity=".25"/></g>'
$b += Get-Line 34 228 @( (M ('30' + $deg) 14 '#566072') )
$b += Get-Line 560 228 @( (M ('62' + $deg) 14 '#566072') )
$b += Get-Line 1200 228 @( (M ('95' + $deg) 14 '#566072') )
$b += '<circle cx="500" cy="243" r="9" fill="none" stroke="#F3F7FF" stroke-width="2.5"/>'
$b += Get-CLine 640 126 @( (TxtR 'Ghost' 62 '#F3F7FF' 'bold'), (TxtR 'Deck' 62 '#3DE3FF' 'bold') )
$b += Get-CLine 640 172 @( (TxtR 'Keep it cool. Keep it quiet. Keep control.' 19 '#A4ADBD') )
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner-thermal.svg') -Value $b -Encoding UTF8

# ================= banner-hologram.svg (03) =================
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<defs><pattern id="hs" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="4" height="2" fill="#0A121C"/></pattern>'
$b += '<filter id="hf"><feGaussianBlur stdDeviation="6" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>'
$b += '<radialGradient id="hr" cx=".5" cy=".5" r=".7"><stop offset="0" stop-color="#0E2A3C"/><stop offset="1" stop-color="#05070B"/></radialGradient></defs>'
$b += '<rect width="1280" height="300" fill="#05070B"/><rect width="1280" height="300" fill="url(#hr)"/>'
$b += '<g filter="url(#hf)">' + (Get-CLine 640 150 @( (TxtR 'Ghost' 72 '#F3F7FF' 'bold'), (TxtR 'Deck' 72 '#3DE3FF' 'bold') )) + '</g>'
$b += '<g opacity=".85">' + (Get-CLine 640 205 @( (TxtR 'INDEPENDENT POWER & FAN CONTROL FOR MSI LAPTOPS' 19 '#7BE9FF') )) + '</g>'
$b += '<rect width="1280" height="300" fill="url(#hs)" opacity=".55"/>'
$b += '<rect x="0" y="118" width="1280" height="2" fill="#3DE3FF" opacity=".25"/>'
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner-hologram.svg') -Value $b -Encoding UTF8

# ================= banner-glitch.svg (12) =================
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<rect width="1280" height="300" fill="#05070B"/>'
$b += '<g opacity=".7">' + (Get-CLine 637 158 @( (TxtR 'GhostDeck' 74 '#FF2F7D' 'bold') )) + '</g>'
$b += '<g opacity=".7">' + (Get-CLine 643 162 @( (TxtR 'GhostDeck' 74 '#3DE3FF' 'bold') )) + '</g>'
$b += Get-CLine 640 160 @( (TxtR 'Ghost' 74 '#F3F7FF' 'bold'), (TxtR 'Deck' 74 '#3DE3FF' 'bold') )
$b += '<rect x="380" y="98" width="520" height="7" fill="#05070B"/><rect x="420" y="101" width="440" height="2" fill="#3DE3FF" opacity=".6"/>'
$b += '<rect x="430" y="138" width="430" height="5" fill="#05070B"/><rect x="470" y="140" width="350" height="2" fill="#FF2F7D" opacity=".55"/>'
$b += Get-CLine 640 216 @( (M '>> power & fan control // MSI laptops // no kernel driver <<' 17 '#A4ADBD') )
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner-glitch.svg') -Value $b -Encoding UTF8

# ================= banner-terminal.svg (04) =================
$b = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 300">'
$b += '<rect width="1280" height="300" fill="#05070B"/>'
$b += '<rect x="70" y="34" width="1140" height="232" rx="12" fill="#0A0D14" stroke="#232C40"/>'
$b += '<circle cx="102" cy="62" r="6" fill="#FF2F7D"/><circle cx="124" cy="62" r="6" fill="#FFC15D"/><circle cx="146" cy="62" r="6" fill="#61E7A4"/>'
$b += Get-Line 170 68 @( (M 'ghostdeck.exe' 15 '#566072') )
$b += Get-Line 104 122 @( (M '>' 21 '#61E7A4'), (M ' ghostdeck ' 21 '#F3F7FF'), (M '--profile silent' 21 '#3DE3FF') )
$b += Get-Line 104 158 @( (M ('Silent restored ' + [char]0xB7 + ' 0xD4 = 0x1D ' + [char]0xB7 + ' ~30 W cap') 21 '#A4ADBD') )
$b += Get-Line 104 212 @( (M 'Ghost' 34 '#F3F7FF' 'bold'), (M 'Deck' 34 '#3DE3FF' 'bold'), (M '  - the Silent profile MSI removed' 21 '#566072') )
$b += '<rect x="360" y="240" width="13" height="24" fill="#3DE3FF"/>'
$b += Get-Line 1076 120 @( (M '(\_/)' 17 '#153444') )
$b += Get-Line 1076 140 @( (M '(o o)' 17 '#153444') )
$b += Get-Line 1070 160 @( (M '/| |\' 17 '#153444') )
$b += '</svg>'
Set-Content -Path (Join-Path $outDir 'banner-terminal.svg') -Value $b -Encoding UTF8

Get-ChildItem $outDir\banner*.svg | ForEach-Object { '{0}  {1:N0} B' -f $_.Name, $_.Length }
