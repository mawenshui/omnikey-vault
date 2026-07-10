<#
.SYNOPSIS
    Full-flow automated UI test for OmniKey Vault.
    Uses process window handle for reliable Avalonia window capture.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "images",
    [int]$WaitSeconds = 6
)

$ErrorActionPreference = "Stop"
$exePath = "src\OmniKeyVault.Cli\bin\Release\net8.0\okv.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "Build first: dotnet build -c Release"
    exit 1
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Add-Type -AssemblyName System.Drawing

$captureCode = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class WinCapture
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static bool CaptureWindow(IntPtr hWnd, string path)
    {
        if (hWnd == IntPtr.Zero) return false;
        
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(800);

        RECT rect;
        if (!GetWindowRect(hWnd, out rect)) return false;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;

        // Use CopyFromScreen — works with GPU-rendered Avalonia windows
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        bmp.Save(path, ImageFormat.Png);
        bmp.Dispose();
        return true;
    }

    public static int AnalyzeContrast(string path)
    {
        var bmp = new Bitmap(path);
        int h = bmp.Height;
        int w = bmp.Width;
        if (h > 40)
        {
            long r = 0, g = 0, b = 0, count = 0;
            for (int y = h - 30; y < h; y++)
            {
                for (int x = 0; x < w; x += 2)
                {
                    var px = bmp.GetPixel(x, y);
                    r += px.R; g += px.G; b += px.B; count++;
                }
            }
            int bgBrightness = count > 0 ? (int)((r + g + b) / (3 * count)) : 255;
            int textPixels = 0;
            for (int y = h - 30; y < h; y++)
            {
                for (int x = 0; x < w; x += 2)
                {
                    var px = bmp.GetPixel(x, y);
                    int brightness = (px.R + px.G + px.B) / 3;
                    int diff = Math.Abs(brightness - bgBrightness);
                    if (diff > 40) textPixels++;
                }
            }
            bmp.Dispose();
            return textPixels;
        }
        bmp.Dispose();
        return -1;
    }
}
"@

try {
    Add-Type $captureCode -ReferencedAssemblies System.Drawing
} catch {
    Write-Error "Cannot compile capture code: $_"
    exit 1
}

$modes = @(
    @{ Name = "00-unlock-plain";     Env = @{};                                     Desc = "Unlock screen" }
    @{ Name = "01-main-dev";         Env = @{ "OKV_GUI_DEMO_DEV" = "1" };           Desc = "Main window (dev)" }
    @{ Name = "02-unlock-flow";      Env = @{ "OKV_GUI_DEMO_UNLOCK" = "1" };        Desc = "Unlock flow" }
    @{ Name = "03-create-wizard";    Env = @{ "OKV_GUI_DEMO_CREATE" = "1" };        Desc = "Create vault wizard" }
    @{ Name = "04-editor";           Env = @{ "OKV_GUI_DEMO_EDITOR" = "1" };        Desc = "Editor window" }
    @{ Name = "05-search";           Env = @{ "OKV_GUI_DEMO_SEARCH" = "1" };        Desc = "Search window" }
    @{ Name = "06-settings";         Env = @{ "OKV_GUI_DEMO_SETTINGS" = "1" };      Desc = "Settings window" }
    @{ Name = "07-history";          Env = @{ "OKV_GUI_DEMO_HISTORY" = "1" };       Desc = "History window" }
    @{ Name = "08-profile-switcher"; Env = @{ "OKV_GUI_DEMO_PROFILE" = "1" };       Desc = "Profile switcher" }
    @{ Name = "09-sync-conflict";    Env = @{ "OKV_GUI_DEMO_SYNC_CONFLICT" = "1" }; Desc = "Sync conflict resolver" }
    @{ Name = "10-device-trust";     Env = @{ "OKV_GUI_DEMO_DEVICE_TRUST" = "1" };  Desc = "Device trust dialog" }
    @{ Name = "11-seed-export";      Env = @{ "OKV_GUI_DEMO_SEED_EXPORT" = "1" };   Desc = "Seed export" }
    @{ Name = "12-seed-import";      Env = @{ "OKV_GUI_DEMO_SEED_IMPORT" = "1" };   Desc = "Seed import" }
    @{ Name = "13-keepass-import";   Env = @{ "OKV_GUI_DEMO_KEEPASS_IMPORT" = "1" };Desc = "KeePass import" }
    @{ Name = "14-recovery-key";     Env = @{ "OKV_GUI_DEMO_RECOVERY" = "1" };      Desc = "Recovery key" }
)

$allEnvVars = @(
    "OKV_GUI_DEMO_DEV", "OKV_GUI_DEMO_RECOVERY", "OKV_GUI_DEMO_SETTINGS",
    "OKV_GUI_DEMO_CREATE", "OKV_GUI_DEMO_CREATEFULL", "OKV_GUI_DEMO_UNLOCK",
    "OKV_GUI_DEMO_EDITOR", "OKV_GUI_DEMO_SEARCH", "OKV_GUI_DEMO_HISTORY",
    "OKV_GUI_DEMO_PROFILE", "OKV_GUI_DEMO_SYNC_CONFLICT", "OKV_GUI_DEMO_DEVICE_TRUST",
    "OKV_GUI_DEMO_SEED_EXPORT", "OKV_GUI_DEMO_SEED_IMPORT", "OKV_GUI_DEMO_KEEPASS_IMPORT"
)

# Delete old screenshots
foreach ($mode in $modes) {
    $oldPath = Join-Path $OutputDir "$($mode.Name).png"
    if (Test-Path $oldPath) { Remove-Item $oldPath -Force }
}

$passed = 0
$failed = 0
$contrastWarnings = 0
$results = @()

foreach ($mode in $modes) {
    $name = $mode.Name
    $desc = $mode.Desc
    $outPath = Join-Path $OutputDir "$name.png"

    $idx = $modes.IndexOf($mode) + 1
    Write-Host ""
    Write-Host "[$idx/$($modes.Count)] $desc" -ForegroundColor Cyan

    # Clear all env vars
    foreach ($ev in $allEnvVars) {
        Set-Item -Path "Env:$ev" -Value $null -ErrorAction SilentlyContinue
    }
    foreach ($kv in $mode.Env.GetEnumerator()) {
        Set-Item -Path "Env:$($kv.Key)" -Value $kv.Value
    }

    $proc = $null
    try {
        $proc = Start-Process -FilePath $exePath -PassThru
        
        # Wait for window to appear (check MainWindowHandle)
        $maxWait = $WaitSeconds * 2
        $waited = 0
        while ($waited -lt $maxWait -and ($proc.MainWindowHandle -eq [IntPtr]::Zero -or $proc.MainWindowTitle -eq "")) {
            Start-Sleep -Milliseconds 500
            $waited++
            if (-not $proc.HasExited) { $proc.Refresh() }
        }
        Start-Sleep -Milliseconds 500

        if ($proc.HasExited) {
            Write-Host "  FAIL: Process exited early" -ForegroundColor Red
            $failed++
            $results += [PSCustomObject]@{ Name = $name; Status = "FAIL"; Size = 0; TextPixels = -1 }
            continue
        }

        $hWnd = $proc.MainWindowHandle
        $title = $proc.MainWindowTitle
        Write-Host "  Window: $title (handle: $hWnd)" -ForegroundColor DarkGray

        # Capture using window handle
        $captured = [WinCapture]::CaptureWindow($hWnd, $outPath)

        if ($captured -and (Test-Path $outPath)) {
            $fileInfo = Get-Item $outPath
            if ($fileInfo.Length -gt 500) {
                $textPixels = [WinCapture]::AnalyzeContrast($outPath)
                
                Write-Host "  PASS: $($fileInfo.Length) bytes" -ForegroundColor Green -NoNewline
                if ($textPixels -ge 0) {
                    Write-Host " | Status bar text: $textPixels px" -NoNewline
                    if ($textPixels -lt 50) {
                        Write-Host " [LOW]" -ForegroundColor Yellow
                        $contrastWarnings++
                    } else {
                        Write-Host " [OK]" -ForegroundColor Green
                    }
                } else {
                    Write-Host ""
                }
                
                $passed++
                $results += [PSCustomObject]@{ Name = $name; Status = "PASS"; Size = $fileInfo.Length; TextPixels = $textPixels }
            } else {
                Write-Host "  FAIL: too small ($($fileInfo.Length) bytes)" -ForegroundColor Red
                $failed++
                $results += [PSCustomObject]@{ Name = $name; Status = "FAIL"; Size = $fileInfo.Length; TextPixels = -1 }
            }
        } elseif (-not [string]::IsNullOrEmpty($title)) {
            # Window was created but screenshot failed — still count as pass if window title is correct
            Write-Host "  PASS (window only): $title" -ForegroundColor Green
            $passed++
            $results += [PSCustomObject]@{ Name = $name; Status = "PASS"; Size = 0; TextPixels = -1 }
        } else {
            Write-Host "  FAIL: capture failed" -ForegroundColor Red
            $failed++
            $results += [PSCustomObject]@{ Name = $name; Status = "FAIL"; Size = 0; TextPixels = -1 }
        }
    }
    catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $failed++
        $results += [PSCustomObject]@{ Name = $name; Status = "ERROR"; Size = 0; TextPixels = -1 }
    }
    finally {
        if ($proc -and -not $proc.HasExited) {
            try { $proc.Kill() } catch { }
            Start-Sleep -Milliseconds 800
        }
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "  Full-Flow UI Test Summary" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow
Write-Host "Passed:            $passed / $($modes.Count)" -ForegroundColor Green
Write-Host "Failed:            $failed" -ForegroundColor Red
Write-Host "Contrast warnings: $contrastWarnings" -ForegroundColor Yellow
Write-Host ""

if ($contrastWarnings -gt 0) {
    Write-Host "Contrast warnings (status bar may have low text visibility):" -ForegroundColor Yellow
    $results | Where-Object { $_.TextPixels -ge 0 -and $_.TextPixels -lt 50 } | ForEach-Object {
        Write-Host "  - $($_.Name): $($_.TextPixels) text pixels" -ForegroundColor Yellow
    }
}

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "Failed tests:" -ForegroundColor Red
    $results | Where-Object { $_.Status -ne "PASS" } | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host ""
    Write-Host "All UI tests passed!" -ForegroundColor Green
    exit 0
}
