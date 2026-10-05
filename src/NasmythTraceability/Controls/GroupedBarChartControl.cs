using System.Windows;
using System.Windows.Media;
using NasmythTraceability.Models;

namespace NasmythTraceability.Controls;

/// <summary>
/// Three bars per station - valid, invalid and repeat scans - with a legend. Each station gets
/// at least <see cref="MinSlotWidth"/> pixels, so with many stations the chart grows wider
/// than its space and is meant to sit in a horizontally scrolling ScrollViewer.
/// Items are <see cref="StationScanBreakdown"/>.
/// </summary>
public sealed class GroupedBarChartControl : ChartBase
{
    private const double MinSlotWidth = 96;
    private const double LeftPad = 40, RightPad = 10, TopPad = 34, BottomPad = 28;

    private static readonly (string Label, string BrushKey)[] Series =
    {
        ("Valid", "AppOkBrush"),
        ("Invalid", "AppNgBrush"),
        ("Repeat", "AppWarnBrush"),
    };

    private List<StationScanBreakdown> Items()
        => ItemsSource?.OfType<StationScanBreakdown>().ToList() ?? new List<StationScanBreakdown>();

    protected override void OnDataChanged()
    {
        InvalidateMeasure();
        base.OnDataChanged();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = LeftPad + RightPad + Items().Count * MinSlotWidth;
        var height = double.IsInfinity(constraint.Height) ? 260 : constraint.Height;
        return new Size(double.IsInfinity(constraint.Width) ? width : Math.Min(width, constraint.Width), height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var items = Items();
        if (items.Count == 0 || items.All(i => i.Total == 0))
        {
            DrawEmpty(dc);
            return;
        }

        var brushes = Series.Select(s => Application.Current?.TryFindResource(s.BrushKey) as Brush ?? Brushes.Gray).ToArray();

        // legend
        var lx = LeftPad;
        for (var s = 0; s < Series.Length; s++)
        {
            dc.DrawRoundedRectangle(brushes[s], null, new Rect(lx, 8, 12, 12), 2, 2);
            var t = Text(Series[s].Label, 11, TextBrush);
            dc.DrawText(t, new Point(lx + 17, 14 - t.Height / 2));
            lx += 17 + t.Width + 18;
        }

        var plotW = Math.Max(1, w - LeftPad - RightPad);
        var plotH = Math.Max(1, h - TopPad - BottomPad);
        var max = Math.Max(1, items.Max(i => Math.Max(i.Valid, Math.Max(i.Invalid, i.Repeat))));

        var gridPen = new Pen(GridBrush, 0.6);
        foreach (var frac in new[] { 0.0, 0.5, 1.0 })
        {
            var y = TopPad + plotH * (1 - frac);
            dc.DrawLine(gridPen, new Point(LeftPad, y), new Point(LeftPad + plotW, y));
            var label = Text(Math.Round(max * frac).ToString("0"), 10, TextBrush);
            dc.DrawText(label, new Point(LeftPad - label.Width - 6, y - label.Height / 2));
        }

        var slot = plotW / items.Count;
        var barW = Math.Min(24, slot * 0.22);
        var groupW = barW * 3 + 4;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var values = new[] { item.Valid, item.Invalid, item.Repeat };
            var gx = LeftPad + slot * i + (slot - groupW) / 2;

            for (var s = 0; s < values.Length; s++)
            {
                var barH = plotH * (values[s] / (double)max);
                var x = gx + s * (barW + 2);
                var y = TopPad + plotH - barH;
                if (barH > 0)
                    dc.DrawRoundedRectangle(brushes[s], null, new Rect(x, y, barW, barH), 2, 2);

                var v = Text(values[s].ToString("0"), 9.5, TextBrush, FontWeights.SemiBold);
                dc.DrawText(v, new Point(x + (barW - v.Width) / 2, y - v.Height - 1));
            }

            var code = item.StationCode.Length > 12 ? item.StationCode[..11] + "…" : item.StationCode;
            var lbl = Text(code, 10.5, TextBrush, FontWeights.SemiBold);
            dc.DrawText(lbl, new Point(LeftPad + slot * i + (slot - lbl.Width) / 2, TopPad + plotH + 6));
        }
    }
}
