function CalcLuminance($r, $g, $b) {
    $rs = $r / 255; $gs = $g / 255; $bs = $b / 255
    $rs = if ($rs -le 0.03928) { $rs / 12.92 } else { [math]::Pow(($rs + 0.055) / 1.055, 2.4) }
    $gs = if ($gs -le 0.03928) { $gs / 12.92 } else { [math]::Pow(($gs + 0.055) / 1.055, 2.4) }
    $bs = if ($bs -le 0.03928) { $bs / 12.92 } else { [math]::Pow(($bs + 0.055) / 1.055, 2.4) }
    return 0.2126 * $rs + 0.7152 * $gs + 0.0722 * $bs
}
function ContrastRatio($c1, $c2) {
    $l1 = CalcLuminance $c1[0] $c1[1] $c1[2]
    $l2 = CalcLuminance $c2[0] $c2[1] $c2[2]
    $lighter = [math]::Max($l1, $l2)
    $darker = [math]::Min($l1, $l2)
    return [math]::Round(($lighter + 0.05) / ($darker + 0.05), 2)
}
$white = @(255, 255, 255)
Write-Host "=== WCAG Contrast Ratios on white (#ffffff) ==="
Write-Host ""
Write-Host "FgMutedBrush:"
Write-Host "  OLD #475569: $(ContrastRatio @(71,85,105) $white):1  [AAA]"
Write-Host "  NEW #334155: $(ContrastRatio @(51,65,85) $white):1  [AAA+]"
Write-Host ""
Write-Host "FgDimBrush (status bar, sidebar, detail text):"
Write-Host "  OLD #64748b: $(ContrastRatio @(100,116,139) $white):1  [AA borderline]"
Write-Host "  NEW #475569: $(ContrastRatio @(71,85,105) $white):1  [AAA]"
Write-Host ""
Write-Host "FgFaintBrush (last sync text, small labels):"
Write-Host "  OLD #94a3b8: $(ContrastRatio @(148,163,184) $white):1  [FAIL!]"
Write-Host "  NEW #64748b: $(ContrastRatio @(100,116,139) $white):1  [AA]"
Write-Host ""
Write-Host "=== WCAG Thresholds ==="
Write-Host "AA normal text:  4.5:1 (minimum)"
Write-Host "AAA normal text: 7.0:1 (enhanced)"
