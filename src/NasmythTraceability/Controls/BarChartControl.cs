using System.Windows;
using System.Windows.Media;

namespace NasmythTraceability.Controls;

/// <summary>Vertical bar chart - used for "Scans by Station".</summary>
public sealed class BarChartControl : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var points = Points();
        if (points.Count == 0)
        {
            DrawEmpty(dc);
            return;
        }

        const double leftPad = 34, bottomPad = 26, topPad = 14, rightPad = 8;
        var plotW = Math.Max(1, w - leftPad - rightPad);
        var plotH = Math.Max(1, h - topPad - bottomPad);
        var max = Math.Max(1, points.Max(p => p.Value));

        // horizontal grid + y labels (0, half, max)
        var gridPen = new Pen(GridBrush, 0.6);
        foreach (var frac in new[] { 0.0, 0.5, 1.0 })
        {
            var y = topPad + plotH * (1 - frac);
            dc.DrawLine(gridPen, new Point(leftPad, y), new Point(leftPad + plotW, y));
            var label = Text(Math.Round(max * frac).ToString("0"), 10, TextBrush);
            dc.DrawText(label, new Point(leftPad - label.Width - 6, y - label.Height / 2));
        }

        var slot = plotW / points.Count;
        var barW = Math.Min(48, slot * 0.55);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var barH = plotH * (p.Value / max);
            var x = leftPad + slot * i + (slot - barW) / 2;
            var y = topPad + plotH - barH;

            var brush = SeriesBrush(i);
            dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barW, barH), 3, 3);

            var valText = Text(p.Value.ToString("0"), 10, TextBrush, FontWeights.SemiBold);
            dc.DrawText(valText, new Point(x + (barW - valText.Width) / 2, y - valText.Height - 2));

            var lblText = Text(p.Label, 10, TextBrush);
            var lblX = leftPad + slot * i + (slot - lblText.Width) / 2;
            dc.DrawText(lblText, new Point(Math.Max(0, lblX), topPad + plotH + 6));
        }
    }
}
