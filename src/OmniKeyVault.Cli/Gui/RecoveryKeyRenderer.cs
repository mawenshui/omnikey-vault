using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmniKeyVault.Cli.Gui;

/// <summary>
/// Shared renderer for the Recovery Key 32-block secret. Used by both
/// CreateVaultWizard's step 3 and the standalone RecoveryKeyWindow so the
/// print / PDF / text-file outputs stay consistent.
///
/// PDF generation is hand-written (no external library) — minimal single-page
/// A4 layout with Courier monospace, 4×8 grid of 6-character groups, title +
/// warning + footer. Per UI_UX_SPEC §4.1 step 4 the recovery key is the
/// only thing that can recover the vault if the master password is lost, so
/// the output is intentionally text-based and self-contained.
/// </summary>
public static class RecoveryKeyRenderer
{
    public const int Groups = 32;
    public const int GroupSize = 6;
    public const int Columns = 8;
    public const int Rows = Groups / Columns; // 4

    /// <summary>Builds a plain-text rendering of the recovery key.</summary>
    public static string BuildTextContent(string key, string? vaultName = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OmniKey Vault — Recovery Key");
        sb.AppendLine(new string('=', 60));
        if (!string.IsNullOrEmpty(vaultName)) sb.AppendLine($"Vault: {vaultName}");
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine();
        var groups = Split(key);
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                sb.Append(groups[row * Columns + col]);
                sb.Append(' ');
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Store this document in a safe offline location.");
        sb.AppendLine("Anyone with this key can recover your vault.");
        return sb.ToString();
    }

    /// <summary>Splits the recovery key into 32 groups of 6 (padding if too short).</summary>
    public static List<string> Split(string key)
    {
        var groups = new List<string>(Groups);
        var src = key ?? string.Empty;
        if (src.Length < Groups * GroupSize)
        {
            var sb = new StringBuilder(src);
            int seed = 0;
            foreach (var ch in src) seed = (seed * 31 + ch) & 0x7fffffff;
            const string alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            while (sb.Length < Groups * GroupSize)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                sb.Append(alpha[seed % alpha.Length]);
            }
            src = sb.ToString();
        }
        for (int i = 0; i < Groups; i++) groups.Add(src.Substring(i * GroupSize, GroupSize));
        return groups;
    }

    /// <summary>Saves the recovery key as a one-page PDF file. No external deps.</summary>
    public static void SavePdf(string key, string path, string? vaultName = null)
    {
        var pdf = BuildPdf(key, vaultName);
        File.WriteAllBytes(path, pdf);
    }

    /// <summary>Builds a minimal one-page A4 PDF byte array (Courier monospace).</summary>
    public static byte[] BuildPdf(string key, string? vaultName = null)
    {
        // A4: 595 x 842 points. Margins 60pt → content 475 x 722.
        const double pageW = 595, pageH = 842;
        const double margin = 50;
        var groups = Split(key);
        var objs = new List<string>(); // PDF objects as strings (without trailing EOF)

        // ----- Build content stream -----
        var content = new StringBuilder();
        content.Append("BT\n/F1 16 Tf\n");
        content.Append($"{margin:F1} {pageH - margin - 16:F1} Td\n");
        content.Append("(OmniKey Vault \\2014 Recovery Key) Tj\n");
        content.Append("ET\n");

        // Warning + date (10pt)
        content.Append("BT\n/F1 9 Tf\n");
        content.Append($"{margin:F1} {pageH - margin - 38:F1} Td\n");
        var warn = "Store this document in a safe offline location.  " +
                   "Anyone with this key can recover your vault.";
        content.Append($"({Escape(warn)}) Tj\n");
        content.Append("ET\n");

        if (!string.IsNullOrEmpty(vaultName))
        {
            content.Append("BT\n/F1 9 Tf\n");
            content.Append($"{margin:F1} {pageH - margin - 52:F1} Td\n");
            content.Append($"(Vault: {Escape(vaultName)}) Tj\n");
            content.Append("ET\n");
        }

        // Date
        content.Append("BT\n/F1 8 Tf\n");
        content.Append($"{margin:F1} {pageH - margin - 65:F1} Td\n");
        content.Append($"(Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z) Tj\n");
        content.Append("ET\n");

        // 4x8 grid: each cell is a small bordered box. Compute layout.
        double gridTop = pageH - margin - 95;     // top of grid
        double gridLeft = margin;                  // left of grid
        double cellW = 60, cellH = 32;             // per-cell size (pts)
        double gapX = 4, gapY = 8;                 // spacing
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var idx = row * Columns + col;
                var text = groups[idx];
                double x = gridLeft + col * (cellW + gapX);
                double y = gridTop - row * (cellH + gapY) - cellH;
                // Cell rectangle (border)
                content.Append("0.85 g\n");
                content.Append($"{x:F1} {y:F1} {cellW:F1} {cellH:F1} re\n");
                content.Append("f\n");
                content.Append("0.3 g\n");
                content.Append($"1 w {x:F1} {y:F1} {cellW:F1} {cellH:F1} re S\n");
                content.Append("0 g\n");
                // Text centered horizontally: use leading spaces for visual centering.
                // Each Courier char ≈ cellW/7 ≈ 8.6pt wide at 14pt size.
                double textX = x + 6;
                double textY = y + 10;
                content.Append("BT\n/F1 14 Tf\n");
                content.Append($"{textX:F1} {textY:F1} Td\n");
                content.Append($"({Escape(text)}) Tj\n");
                content.Append("ET\n");
            }
        }

        // Footer (6pt)
        content.Append("BT\n/F1 6 Tf\n");
        content.Append($"{margin:F1} {margin - 12:F1} Td\n");
        var footer = "OmniKey Vault v0.2  ·  This document is the only way to recover the vault.";
        content.Append($"({Escape(footer)}) Tj\n");
        content.Append("ET\n");

        // Footer (6pt) - "footer" string declared at end of content block
        content.Append("BT\n/F1 6 Tf\n");
        content.Append($"{margin:F1} {margin - 12:F1} Td\n");
        var footerLine = "OmniKey Vault v0.2  ·  This document is the only way to recover the vault.";
        content.Append($"({Escape(footerLine)}) Tj\n");
        content.Append("ET\n");

        byte[] contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        // ----- Build PDF objects -----
        // 1: Catalog
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        // 2: Pages
        objs.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        // 3: Page
        objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW} {pageH}] " +
                  "/Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        // 4: Content stream
        objs.Add($"<< /Length {contentBytes.Length} >>\nstream\n");
        // 5: Font (Courier)
        objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

        // ----- Assemble PDF with xref table -----
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms, Encoding.ASCII) { NewLine = "\n" };
        writer.Write("%PDF-1.4\n");
        writer.Write("%\u00E2\u00E3\u00CF\u00D3\n"); // binary marker (4 bytes)

        var offsets = new List<long> { 0 }; // index 0 unused
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(ms.Position);
            var content_obj_4 = (i == 3);
            if (content_obj_4)
            {
                // object 4 is the content stream — write the dict + stream manually
                // because we need binary length of contentBytes (no encoding round-trip)
                writer.Flush();
                var header = Encoding.ASCII.GetBytes("4 0 obj\n" + objs[i]);
                ms.Write(header, 0, header.Length);
                ms.Write(contentBytes, 0, contentBytes.Length);
                var endstreamBytes = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");
                ms.Write(endstreamBytes, 0, endstreamBytes.Length);
                writer = new StreamWriter(ms, Encoding.ASCII) { NewLine = "\n" };
            }
            else
            {
                writer.Write($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
            }
        }
        long xrefStart = ms.Position;
        writer.Write($"xref\n0 {objs.Count + 1}\n");
        writer.Write("0000000000 65535 f \n");
        for (int i = 1; i <= objs.Count; i++) writer.Write($"{offsets[i]:D10} 00000 n \n");
        writer.Write($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>Escapes a string for inclusion in a PDF text object. Only handles ASCII safely
    /// (the recovery key is alphanumeric so this is sufficient); non-ASCII chars are dropped
    /// to avoid PDF encoding issues. The PDF /Encoding /WinAnsiEncoding supports most Latin chars.</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\\' || c == '(' || c == ')') sb.Append('\\').Append(c);
            else if (c >= 0x20 && c < 0x7F) sb.Append(c);
            // else: skip non-ASCII (recovery key is base32 alphanum anyway)
        }
        return sb.ToString();
    }
}
