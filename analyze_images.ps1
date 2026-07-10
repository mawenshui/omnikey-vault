Add-Type -AssemblyName System.Drawing

$dir = "D:\__qoder_project\OmniKey_Vault\issues"
$files = Get-ChildItem "$dir\*.png" | Sort-Object Name

foreach ($file in $files) {
    Write-Host ""
    Write-Host "=== $($file.Name) ==="
    $bmp = New-Object System.Drawing.Bitmap($file.FullName)
    Write-Host "Size: $($bmp.Width)x$($bmp.Height)"

    # Sample regions for average color
    function GetAvgColor($bitmap, $x, $y, $w, $h) {
        $r = 0; $g = 0; $b = 0; $count = 0
        for ($dy = 0; $dy -lt $h -and ($y + $dy) -lt $bitmap.Height; $dy++) {
            for ($dx = 0; $dx -lt $w -and ($x + $dx) -lt $bitmap.Width; $dx++) {
                $px = $bitmap.GetPixel($x + $dx, $y + $dy)
                $r += $px.R; $g += $px.G; $b += $px.B; $count++
            }
        }
        if ($count -eq 0) { return $null }
        return [PSCustomObject]@{ R = [int]($r / $count); G = [int]($g / $count); B = [int]($b / $count) }
    }

    # Title bar
    $tl = GetAvgColor $bmp 10 10 200 30
    Write-Host "Title bar: R=$($tl.R) G=$($tl.G) B=$($tl.B) (brightness: $(($tl.R + $tl.G + $tl.B) / 3))"

    # Left sidebar
    $left = GetAvgColor $bmp 10 ($bmp.Height / 2) 200 100
    Write-Host "Left sidebar: R=$($left.R) G=$($left.G) B=$($left.B) (brightness: $(($left.R + $left.G + $left.B) / 3))"

    # Center
    $center = GetAvgColor $bmp ($bmp.Width / 2 - 50) ($bmp.Height / 2 - 50) 100 100
    Write-Host "Center: R=$($center.R) G=$($center.G) B=$($center.B) (brightness: $(($center.R + $center.G + $center.B) / 3))"

    # Status bar (bottom)
    $status = GetAvgColor $bmp 10 ($bmp.Height - 30) ($bmp.Width - 20) 20
    Write-Host "Status bar: R=$($status.R) G=$($status.G) B=$($status.B) (brightness: $(($status.R + $status.G + $status.B) / 3))"

    # Right panel
    if ($bmp.Width -gt 600) {
        $right = GetAvgColor $bmp ($bmp.Width - 200) ($bmp.Height / 2) 150 200
        Write-Host "Right panel: R=$($right.R) G=$($right.G) B=$($right.B) (brightness: $(($right.R + $right.G + $right.B) / 3))"
    }

    # Count white and dark pixels
    $whiteCount = 0; $darkCount = 0; $totalSampled = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 5) {
        for ($x = 0; $x -lt $bmp.Width; $x += 5) {
            $px = $bmp.GetPixel($x, $y)
            $totalSampled++
            $brightness = ($px.R + $px.G + $px.B) / 3
            if ($brightness -gt 240) { $whiteCount++ }
            if ($brightness -lt 30) { $darkCount++ }
        }
    }
    Write-Host "White pixels: $whiteCount / $totalSampled ($([math]::Round(100 * $whiteCount / $totalSampled, 1))%)"
    Write-Host "Dark pixels: $darkCount / $totalSampled ($([math]::Round(100 * $darkCount / $totalSampled, 1))%)"

    # Detect low contrast text regions in status bar
    $bgBrightness = ($status.R + $status.G + $status.B) / 3
    $textLikePixels = 0; $lowContrastPixels = 0
    for ($y = [math]::Max(0, $bmp.Height - 40); $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x += 2) {
            $px = $bmp.GetPixel($x, $y)
            $brightness = ($px.R + $px.G + $px.B) / 3
            $diff = [math]::Abs($brightness - $bgBrightness)
            if ($diff -gt 30) { $textLikePixels++ }
            if ($diff -gt 10 -and $diff -lt 40) { $lowContrastPixels++ }
        }
    }
    Write-Host "Status bar text-like pixels: $textLikePixels, low-contrast: $lowContrastPixels"
    if ($textLikePixels -lt 5 -and $bgBrightness -gt 200) {
        Write-Host "  WARNING: Status bar has light background but almost no visible text!"
    }
    if ($textLikePixels -lt 5 -and $bgBrightness -lt 50) {
        Write-Host "  WARNING: Status bar has dark background but almost no visible text!"
    }

    # Check sidebar text visibility
    $sidebarBgBrightness = ($left.R + $left.G + $left.B) / 3
    $sidebarTextPixels = 0
    for ($y = 50; $y -lt ($bmp.Height - 50); $y += 2) {
        for ($x = 10; $x -lt 250; $x += 2) {
            $px = $bmp.GetPixel($x, $y)
            $brightness = ($px.R + $px.G + $px.B) / 3
            $diff = [math]::Abs($brightness - $sidebarBgBrightness)
            if ($diff -gt 50) { $sidebarTextPixels++ }
        }
    }
    Write-Host "Sidebar text-like pixels: $sidebarTextPixels"
    if ($sidebarTextPixels -lt 10) {
        Write-Host "  WARNING: Sidebar may have invisible text!"
    }

    $bmp.Dispose()
}
