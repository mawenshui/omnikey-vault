<#
.SYNOPSIS
    Automated UI test for OmniKey Vault — launches demo windows and captures screenshots.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "images",
    [int]$WaitSeconds = 5
)

$ErrorActionPreference = "Stop"
$exePath = "src\OmniKeyVault.Cli\bin\Release\net8.0\okv.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at $exePath. Build first: dotnet build -c Release"
    exit 1
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Load assemblies for screenshot capture
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Win32 API helpers for window capture
$screenshotCode = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class WindowCapture
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private const int SW_RESTORE = 9;
    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static void SaveScreenshot(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            System.Threading.Thread.Sleep(500);
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) continue;

            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
            System.Threading.Thread.Sleep(300);

            RECT rect;
            if (!GetWindowRect(hWnd, out rect)) continue;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) continue;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdcDest = g.GetHdc();
                var hdcSrc = GetWindowDC(hWnd);
                BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);
                ReleaseDC(hWnd, hdcSrc);
                g.ReleaseHdc(hdcDest);
            }
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
            return;
        }
        System.Console.WriteLine("WARNING: Failed to capture window: " + path);
    }
}
"@

# Try to compile the C# code; if it fails, fall back to screen capture
try {
    Add-Type $screenshotCode -ReferencedAssemblies System.Drawing, System.Windows.Forms
} catch {
    Write-Warning "C# window capture failed, using screen capture fallback"
    # Fallback: capture the entire primary screen
    $fallbackCode = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

public static class WindowCapture
{
    public static void SaveScreenshot(string path)
    {
        System.Threading.Thread.Sleep(500);
        var bounds = Screen.PrimaryScreen.Bounds;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        bmp.Save(path, ImageFormat.Png);
        bmp.Dispose();
    }
}
"@
    try {
        Add-Type $fallbackCode -ReferencedAssemblies System.Drawing, System.Windows.Forms
    } catch {
        Write-Error "Cannot compile screenshot code. Error: $_"
        exit 1
    }
}

# Demo modes to test
$modes = @(
    @{ Name = "00-unlock-plain"; Env = @{}; Desc = "Unlock screen" }
    @{ Name = "01-main-dev"; Env = @{ "OKV_GUI_DEMO_DEV" = "1" }; Desc = "Main window (dev profile)" }
    @{ Name = "03-create-wizard"; Env = @{ "OKV_GUI_DEMO_CREATE" = "1" }; Desc = "Create vault wizard" }
    @{ Name = "04-editor"; Env = @{ "OKV_GUI_DEMO_EDITOR" = "1" }; Desc = "Editor window" }
    @{ Name = "05-search"; Env = @{ "OKV_GUI_DEMO_SEARCH" = "1" }; Desc = "Search window" }
    @{ Name = "06-settings"; Env = @{ "OKV_GUI_DEMO_SETTINGS" = "1" }; Desc = "Settings window" }
    @{ Name = "07-history"; Env = @{ "OKV_GUI_DEMO_HISTORY" = "1" }; Desc = "History window" }
    @{ Name = "08-profile-switcher"; Env = @{ "OKV_GUI_DEMO_PROFILE" = "1" }; Desc = "Profile switcher" }
    @{ Name = "09-sync-conflict"; Env = @{ "OKV_GUI_DEMO_SYNC_CONFLICT" = "1" }; Desc = "Sync conflict resolver" }
    @{ Name = "14-recovery-key"; Env = @{ "OKV_GUI_DEMO_RECOVERY" = "1" }; Desc = "Recovery key window" }
)

$allEnvVars = @("OKV_GUI_DEMO_DEV", "OKV_GUI_DEMO_RECOVERY", "OKV_GUI_DEMO_SETTINGS",
                "OKV_GUI_DEMO_CREATE", "OKV_GUI_DEMO_CREATEFULL", "OKV_GUI_DEMO_UNLOCK",
                "OKV_GUI_DEMO_EDITOR", "OKV_GUI_DEMO_SEARCH", "OKV_GUI_DEMO_HISTORY",
                "OKV_GUI_DEMO_PROFILE", "OKV_GUI_DEMO_SYNC_CONFLICT", "OKV_GUI_DEMO_DEVICE_TRUST",
                "OKV_GUI_DEMO_SEED_EXPORT", "OKV_GUI_DEMO_SEED_IMPORT", "OKV_GUI_DEMO_KEEPASS_IMPORT")

$passed = 0
$failed = 0
$results = @()

foreach ($mode in $modes) {
    $name = $mode.Name
    $desc = $mode.Desc
    $outPath = Join-Path $OutputDir "$name.png"

    Write-Host ""
    Write-Host "[$($modes.IndexOf($mode) + 1)/$($modes.Count)] Testing: $desc" -ForegroundColor Cyan

    # Clear all demo env vars first
    foreach ($ev in $allEnvVars) {
        Set-Item -Path "Env:$ev" -Value $null -ErrorAction SilentlyContinue
    }

    # Set environment variables for this mode
    foreach ($kv in $mode.Env.GetEnumerator()) {
        Set-Item -Path "Env:$($kv.Key)" -Value $kv.Value
    }

    $proc = $null
    try {
        # Launch the app
        $proc = Start-Process -FilePath $exePath -PassThru

        # Wait for window to appear and stabilize
        Start-Sleep -Seconds $WaitSeconds

        # Capture screenshot
        [WindowCapture]::SaveScreenshot($outPath)

        if (Test-Path $outPath) {
            $fileInfo = Get-Item $outPath
            if ($fileInfo.Length -gt 1000) {
                Write-Host "  PASS: Screenshot saved ($($fileInfo.Length) bytes)" -ForegroundColor Green
                $passed++
                $results += [PSCustomObject]@{ Name = $name; Status = "PASS"; Size = $fileInfo.Length }
            } else {
                Write-Host "  FAIL: Screenshot too small ($($fileInfo.Length) bytes)" -ForegroundColor Red
                $failed++
                $results += [PSCustomObject]@{ Name = $name; Status = "FAIL"; Size = $fileInfo.Length }
            }
        } else {
            Write-Host "  FAIL: Screenshot not created" -ForegroundColor Red
            $failed++
            $results += [PSCustomObject]@{ Name = $name; Status = "FAIL"; Size = 0 }
        }
    }
    catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $failed++
        $results += [PSCustomObject]@{ Name = $name; Status = "ERROR"; Size = 0 }
    }
    finally {
        # Kill the process
        if ($proc -and -not $proc.HasExited) {
            try { $proc.Kill() } catch { }
            Start-Sleep -Milliseconds 500
        }
    }
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "UI Test Summary" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "Passed: $passed" -ForegroundColor Green
Write-Host "Failed: $failed" -ForegroundColor Red
Write-Host "Total:  $($modes.Count)" -ForegroundColor White
Write-Host ""

if ($failed -gt 0) {
    Write-Host "Failed tests:" -ForegroundColor Red
    $results | Where-Object { $_.Status -ne "PASS" } | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "All UI tests passed!" -ForegroundColor Green
    exit 0
}
