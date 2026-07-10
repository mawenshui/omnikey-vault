using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

class AnalyzeImages
{
    static void Main(string[] args)
    {
        var dir = @"D:\__qoder_project\OmniKey_Vault\issues";
        foreach (var file in System.IO.Directory.GetFiles(dir, "*.png"))
        {
            var name = System.IO.Path.GetFileName(file);
            Console.WriteLine($"\n=== {name} ===");
            using var bmp = new Bitmap(file);
            Console.WriteLine($"Size: {bmp.Width}x{bmp.Height}");

            // Sample pixels from different regions to detect contrast issues
            // Check top-left corner (title bar area)
            var tlColor = GetAverageColor(bmp, 10, 10, 100, 30);
            Console.WriteLine($"Title bar avg color: R={tlColor.R} G={tlColor.G} B={tlColor.B}");

            // Check center area
            var centerColor = GetAverageColor(bmp, bmp.Width/2 - 50, bmp.Height/2 - 50, 100, 100);
            Console.WriteLine($"Center avg color: R={centerColor.R} G={centerColor.G} B={centerColor.B}");

            // Check left sidebar area
            var leftColor = GetAverageColor(bmp, 10, bmp.Height/2, 200, 100);
            Console.WriteLine($"Left sidebar avg color: R={leftColor.R} G={leftColor.G} B={leftColor.B}");

            // Detect very dark or very light regions (potential contrast issues)
            int darkPixels = 0, lightPixels = 0, totalPixels = 0;
            int veryDarkOnLight = 0; // dark text on light bg
            int veryLightOnDark = 0; // light text on dark bg
            int lowContrastPixels = 0; // text same color as background

            // Sample every 10th pixel for performance
            for (int y = 0; y < bmp.Height; y += 10)
            {
                for (int x = 0; x < bmp.Width; x += 10)
                {
                    var px = bmp.GetPixel(x, y);
                    totalPixels++;
                    int brightness = (px.R + px.G + px.B) / 3;
                    if (brightness < 50) darkPixels++;
                    if (brightness > 200) lightPixels++;
                    
                    // Check neighbors for contrast
                    if (x > 10 && y > 10)
                    {
                        var pxLeft = bmp.GetPixel(x - 10, y);
                        var pxUp = bmp.GetPixel(x, y - 10);
                        int brightnessLeft = (pxLeft.R + pxLeft.G + pxLeft.B) / 3;
                        int brightnessUp = (pxUp.R + pxUp.G + pxUp.B) / 3;
                        
                        // If pixel is very different from neighbors (potential text edge)
                        if (Math.Abs(brightness - brightnessLeft) > 100 || Math.Abs(brightness - brightnessUp) > 100)
                        {
                            // This is likely a text edge - check if text is visible
                            // Low contrast: text color very close to background
                            if (Math.Abs(brightness - brightnessLeft) < 30 && Math.Abs(brightness - brightnessUp) < 30)
                            {
                                lowContrastPixels++;
                            }
                        }
                    }
                }
            }
            
            Console.WriteLine($"Dark pixels: {darkPixels}/{totalPixels} ({100*darkPixels/totalPixels}%)");
            Console.WriteLine($"Light pixels: {lightPixels}/{totalPixels} ({100*lightPixels/totalPixels}%)");
            
            // Check for potential invisible text: pixels that are very close in brightness to their surroundings
            // but are in text-like patterns
            
            // Check bottom status bar area
            if (bmp.Height > 100)
            {
                var statusColor = GetAverageColor(bmp, 10, bmp.Height - 30, bmp.Width - 20, 20);
                Console.WriteLine($"Status bar avg color: R={statusColor.R} G={statusColor.G} B={statusColor.B}");
                
                // Check if status bar text might be invisible
                var statusTextColor = GetDominantNonBgColor(bmp, 10, bmp.Height - 30, bmp.Width - 20, 20);
                if (statusTextColor.HasValue)
                {
                    var tc = statusTextColor.Value;
                    int textBrightness = (tc.R + tc.G + tc.B) / 3;
                    int bgBrightness = (statusColor.R + statusColor.G + statusColor.B) / 3;
                    int contrast = Math.Abs(textBrightness - bgBrightness);
                    Console.WriteLine($"Status bar text color: R={tc.R} G={tc.G} B={tc.B} (contrast: {contrast})");
                    if (contrast < 50)
                    {
                        Console.WriteLine($"WARNING: Low contrast in status bar! Text may be invisible.");
                    }
                }
            }

            // Check right panel area
            if (bmp.Width > 600)
            {
                var rightColor = GetAverageColor(bmp, bmp.Width - 200, bmp.Height/2, 150, 200);
                Console.WriteLine($"Right panel avg color: R={rightColor.R} G={rightColor.G} B={rightColor.B}");
            }

            // Check for any pure white areas (might indicate light theme)
            int whiteCount = 0;
            for (int y = 0; y < bmp.Height; y += 5)
            {
                for (int x = 0; x < bmp.Width; x += 5)
                {
                    var px = bmp.GetPixel(x, y);
                    if (px.R > 240 && px.G > 240 && px.B > 240) whiteCount++;
                }
            }
            Console.WriteLine($"White-ish pixels: {whiteCount} (sampled)");

            // Check for any pure dark areas (might indicate dark theme)
            int darkCount = 0;
            for (int y = 0; y < bmp.Height; y += 5)
            {
                for (int x = 0; x < bmp.Width; x += 5)
                {
                    var px = bmp.GetPixel(x, y);
                    if (px.R < 30 && px.G < 30 && px.B < 30) darkCount++;
                }
            }
            Console.WriteLine($"Dark-ish pixels: {darkCount} (sampled)");
        }
    }

    static Color GetAverageColor(Bitmap bmp, int x, int y, int w, int h)
    {
        long r = 0, g = 0, b = 0, count = 0;
        for (int dy = 0; dy < h && y + dy < bmp.Height; dy++)
        {
            for (int dx = 0; dx < w && x + dx < bmp.Width; dx++)
            {
                var px = bmp.GetPixel(x + dx, y + dy);
                r += px.R; g += px.G; b += px.B; count++;
            }
        }
        if (count == 0) return Color.Black;
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }

    static Color? GetDominantNonBgColor(Bitmap bmp, int x, int y, int w, int h)
    {
        var bg = GetAverageColor(bmp, x, y, w, h);
        long r = 0, g = 0, b = 0, count = 0;
        for (int dy = 0; dy < h && y + dy < bmp.Height; dy++)
        {
            for (int dx = 0; dx < w && x + dx < bmp.Width; dx++)
            {
                var px = bmp.GetPixel(x + dx, y + dy);
                int diff = Math.Abs(px.R - bg.R) + Math.Abs(px.G - bg.G) + Math.Abs(px.B - bg.B);
                if (diff > 60) // Non-background pixel (likely text)
                {
                    r += px.R; g += px.G; b += px.B; count++;
                }
            }
        }
        if (count == 0) return null;
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }
}
