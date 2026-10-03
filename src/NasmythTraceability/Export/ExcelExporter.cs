using System.IO;
using ClosedXML.Excel;
using NasmythTraceability.Models;

namespace NasmythTraceability.Export;

/// <summary>Writes a <see cref="ReportBundle"/> to a multi-sheet .xlsx workbook.</summary>
public static class ExcelExporter
{
    public static void Export(ReportBundle bundle, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var wb = new XLWorkbook();

        BuildSummarySheet(wb, bundle);
        BuildByStationSheet(wb, bundle);
        BuildScansSheet(wb, bundle);

        wb.SaveAs(path);
    }

    private static void BuildSummarySheet(XLWorkbook wb, ReportBundle b)
    {
        var ws = wb.AddWorksheet("Summary");
        ws.Cell(1, 1).Value = b.CompanyName;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = b.Title;
        ws.Cell(3, 1).Value = $"Period: {b.Summary.From:dd/MM/yyyy} to {b.Summary.To:dd/MM/yyyy}";
        ws.Cell(4, 1).Value = $"Generated: {b.GeneratedAt:dd/MM/yyyy HH:mm:ss}";

        var row = 6;
        ws.Cell(row, 1).Value = "KPI";
        ws.Cell(row, 2).Value = "Value";
        ws.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        row++;

        (string, object)[] kpis =
        {
            ("Total Scans", b.Summary.TotalScans),
            ("OK Scans", b.Summary.OkScans),
            ("NG Scans", b.Summary.NgScans),
            ("Unique Work Orders", b.Summary.UniqueBarcodes),
            ("OK Rate", $"{b.Summary.OkRate:P1}"),
        };
        foreach (var (k, v) in kpis)
        {
            ws.Cell(row, 1).Value = k;
            ws.Cell(row, 2).Value = XLCellValue.FromObject(v);
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildByStationSheet(XLWorkbook wb, ReportBundle b)
    {
        var ws = wb.AddWorksheet("Scans by Station");
        string[] headers = { "Station", "Name", "OK", "NG", "Total" };
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        var row = 2;
        foreach (var s in b.ByStation)
        {
            ws.Cell(row, 1).Value = s.StationCode;
            ws.Cell(row, 2).Value = s.StationName;
            ws.Cell(row, 3).Value = s.Ok;
            ws.Cell(row, 4).Value = s.Ng;
            ws.Cell(row, 5).Value = s.Total;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildScansSheet(XLWorkbook wb, ReportBundle b)
    {
        var ws = wb.AddWorksheet("Scans");
        string[] headers = { "Time In", "Station", "Work Order", "Result", "Message", "Time Out" };
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        var row = 2;
        foreach (var s in b.Scans)
        {
            ws.Cell(row, 1).Value = s.ScannedAt;
            ws.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cell(row, 2).Value = s.StationCode;
            ws.Cell(row, 3).Value = s.Barcode;
            ws.Cell(row, 4).Value = s.Result.ToString();
            ws.Cell(row, 5).Value = s.Message;
            if (s.ExitedAt is { } exit)
            {
                ws.Cell(row, 6).Value = exit;
                ws.Cell(row, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            }
            row++;
        }

        ws.Columns().AdjustToContents();
        if (row > 2)
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
    }
}
