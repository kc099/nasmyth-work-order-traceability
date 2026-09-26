using System.IO;
using NasmythTraceability.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NasmythTraceability.Export;

/// <summary>Renders a <see cref="ReportBundle"/> to a paginated A4 PDF using PDFsharp.</summary>
public static class PdfExporter
{
    private const double Margin = 40;
    private static readonly XColor HeaderBlue = XColor.FromArgb(255, 12, 35, 63);
    private static readonly XColor BandBlue = XColor.FromArgb(255, 220, 230, 244);
    private static readonly XColor NgRed = XColor.FromArgb(255, 200, 60, 55);

    public static void Export(ReportBundle bundle, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var doc = new PdfDocument();
        doc.Info.Title = bundle.Title;
        doc.Info.Author = bundle.CompanyName;

        var fonts = new Fonts();
        var page = NewPage(doc, out var gfx);
        double y = Margin;

        y = DrawTitleBlock(gfx, page, fonts, bundle, y);
        y = DrawKpis(gfx, fonts, bundle, y);
        y = DrawStationTable(gfx, fonts, bundle, y);

        // Scan detail table (paginated)
        y += 16;
        y = EnsureSpace(doc, ref page, ref gfx, y, 60);
        gfx.DrawString("Scan Detail", fonts.H2, XBrushes.Black, Margin, y);
        y += 20;

        var columns = new (string Header, double Width)[]
        {
            ("Time", 110), ("Station", 55), ("Barcode", 170), ("Result", 50), ("Message", 130),
        };
        y = DrawTableHeader(gfx, fonts, columns, y);

        var maxRows = Math.Min(bundle.Scans.Count, 2000);
        for (var i = 0; i < maxRows; i++)
        {
            y = EnsureSpace(doc, ref page, ref gfx, y, 18);
            if (Math.Abs(y - Margin) < 0.01)
                y = DrawTableHeader(gfx, fonts, columns, y);

            var s = bundle.Scans[i];
            var cells = new[]
            {
                s.ScannedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                s.StationCode,
                Trim(s.Barcode, 28),
                s.Result.ToString(),
                Trim(s.Message, 22),
            };
            DrawRow(gfx, fonts, columns, cells, y, s.Result == ScanResult.NG);
            y += 16;
        }

        gfx.Dispose(); // release the last page before the footer pass reopens each page
        DrawFooter(doc, fonts, bundle);
        doc.Save(path);
    }

    // ---------------------------------------------------------------- sections

    private static double DrawTitleBlock(XGraphics gfx, PdfPage page, Fonts f, ReportBundle b, double y)
    {
        gfx.DrawRectangle(new XSolidBrush(HeaderBlue), Margin, y, page.Width.Point - 2 * Margin, 54);
        gfx.DrawString(b.CompanyName, f.H1, XBrushes.White, Margin + 12, y + 22);
        gfx.DrawString(b.Title, f.Normal, XBrushes.White, Margin + 12, y + 42);
        y += 66;

        gfx.DrawString(
            $"Period: {b.Summary.From:dd/MM/yyyy} to {b.Summary.To:dd/MM/yyyy}     " +
            $"Generated: {b.GeneratedAt:dd/MM/yyyy HH:mm:ss}",
            f.Small, XBrushes.Black, Margin, y);
        return y + 18;
    }

    private static double DrawKpis(XGraphics gfx, Fonts f, ReportBundle b, double y)
    {
        y += 6;
        var kpis = new (string Label, string Value)[]
        {
            ("Total Scans", b.Summary.TotalScans.ToString("N0")),
            ("OK Scans", b.Summary.OkScans.ToString("N0")),
            ("NG Scans", b.Summary.NgScans.ToString("N0")),
            ("Unique Barcodes", b.Summary.UniqueBarcodes.ToString("N0")),
            ("OK Rate", b.Summary.OkRate.ToString("P1")),
        };

        const double cardW = 98, cardH = 46, gap = 6;
        double x = Margin;
        foreach (var k in kpis)
        {
            gfx.DrawRectangle(new XSolidBrush(BandBlue), x, y, cardW, cardH);
            gfx.DrawString(k.Value, f.H2, XBrushes.Black, new XRect(x, y + 6, cardW, 20), XStringFormats.TopCenter);
            gfx.DrawString(k.Label, f.Small, XBrushes.Black, new XRect(x, y + 27, cardW, 16), XStringFormats.TopCenter);
            x += cardW + gap;
        }

        return y + cardH + 18;
    }

    private static double DrawStationTable(XGraphics gfx, Fonts f, ReportBundle b, double y)
    {
        gfx.DrawString("Scans by Station", f.H2, XBrushes.Black, Margin, y);
        y += 18;

        var columns = new (string Header, double Width)[]
        {
            ("Station", 70), ("Name", 200), ("OK", 60), ("NG", 60), ("Total", 60),
        };
        y = DrawTableHeader(gfx, f, columns, y);

        foreach (var s in b.ByStation)
        {
            DrawRow(gfx, f, columns,
                new[] { s.StationCode, s.StationName, s.Ok.ToString(), s.Ng.ToString(), s.Total.ToString() },
                y, highlight: false);
            y += 16;
        }

        return y + 6;
    }

    // ---------------------------------------------------------------- table helpers

    private static double DrawTableHeader(XGraphics gfx, Fonts f,
        (string Header, double Width)[] columns, double y)
    {
        double x = Margin;
        var totalWidth = columns.Sum(c => c.Width);
        gfx.DrawRectangle(new XSolidBrush(HeaderBlue), Margin, y, totalWidth, 16);
        foreach (var c in columns)
        {
            gfx.DrawString(c.Header, f.SmallBold, XBrushes.White, new XRect(x + 3, y + 2, c.Width - 6, 14),
                XStringFormats.TopLeft);
            x += c.Width;
        }

        return y + 16;
    }

    private static void DrawRow(XGraphics gfx, Fonts f, (string Header, double Width)[] columns,
        string[] cells, double y, bool highlight)
    {
        double x = Margin;
        var brush = highlight ? new XSolidBrush(NgRed) : XBrushes.Black;
        for (var i = 0; i < columns.Length && i < cells.Length; i++)
        {
            gfx.DrawString(cells[i], f.Small, brush, new XRect(x + 3, y, columns[i].Width - 6, 14),
                XStringFormats.TopLeft);
            x += columns[i].Width;
        }

        gfx.DrawLine(new XPen(XColor.FromArgb(255, 210, 210, 210), 0.5),
            Margin, y + 14, Margin + columns.Sum(c => c.Width), y + 14);
    }

    // ---------------------------------------------------------------- page plumbing

    private static PdfPage NewPage(PdfDocument doc, out XGraphics gfx)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        gfx = XGraphics.FromPdfPage(page);
        return page;
    }

    private static double EnsureSpace(PdfDocument doc, ref PdfPage page, ref XGraphics gfx,
        double y, double needed)
    {
        if (y + needed <= page.Height.Point - Margin)
            return y;

        gfx.Dispose();
        page = NewPage(doc, out gfx);
        return Margin;
    }

    private static void DrawFooter(PdfDocument doc, Fonts f, ReportBundle b)
    {
        for (var i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"{b.CompanyName}  -  page {i + 1} of {doc.PageCount}",
                f.Small, XBrushes.Gray,
                new XRect(Margin, page.Height.Point - 28, page.Width.Point - 2 * Margin, 14),
                XStringFormats.TopCenter);
        }
    }

    private static string Trim(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed class Fonts
    {
        public readonly XFont H1 = new("Arial", 15, XFontStyleEx.Bold);
        public readonly XFont H2 = new("Arial", 12, XFontStyleEx.Bold);
        public readonly XFont Normal = new("Arial", 10, XFontStyleEx.Regular);
        public readonly XFont Small = new("Arial", 8, XFontStyleEx.Regular);
        public readonly XFont SmallBold = new("Arial", 8, XFontStyleEx.Bold);
    }
}
