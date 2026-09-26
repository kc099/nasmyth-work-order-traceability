using System.Windows;
using System.Windows.Media;

namespace NasmythTraceability.Controls;

/// <summary>Area/line chart - used for the "Station-wise Scan Trend".</summary>
public sealed class LineChartControl : ChartBase
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

        const double leftPad = 34, bottomPad = 26, topPad = 14, rightPad = 10;
        var plotW = Math.Max(1, w - leftPad - rightPad);
        var plotH = Math.Max(1, h - topPad - bottomPad);
        var max = Math.Max(1, points.Max(p => p.Value));

        var gridPen = new Pen(GridBrush, 0.6);
        foreach (var frac in new[] { 0.0, 0.5, 1.0 })
        {
            var y = topPad + plotH * (1 - frac);
            dc.DrawLine(gridPen, new Point(leftPad, y), new Point(leftPad + plotW, y));
            var label = Text(Math.Round(max * frac).ToString("0"), 10, TextBrush);
            dc.DrawText(label, new Point(leftPad - label.Width - 6, y - label.Height / 2));
        }

        double X(int i) => points.Count == 1
            ? leftPad + plotW / 2
            : leftPad + plotW * i / (points.Count - 1);
        double Y(double v) => topPad + plotH * (1 - v / max);

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            lc.BeginFigure(new Point(X(0), Y(points[0].Value)), false, false);
            ac.BeginFigure(new Point(X(0), topPad + plotH), true, true);
            ac.LineTo(new Point(X(0), Y(points[0].Value)), true, false);

            for (var i = 1; i < points.Count; i++)
            {
                var pt = new Point(X(i), Y(points[i].Value));
                lc.LineTo(pt, true, true);
                ac.LineTo(pt, true, false);
            }

            ac.LineTo(new Point(X(points.Count - 1), topPad + plotH), true, false);
        }

        line.Freeze();
        area.Freeze();

        var accent = Application.Current?.TryFindResource("AppAccentBrush") as Brush ?? Brushes.DodgerBlue;
        var fill = accent.Clone();
        fill.Opacity = 0.18;
        fill.Freeze();

        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(accent, 2), line);

        // markers + thinned x labels
        var step = Math.Max(1, (int)Math.Ceiling(points.Count / 8.0));
        for (var i = 0; i < points.Count; i++)
        {
            var cx = X(i);
            var cy = Y(points[i].Value);
            dc.DrawEllipse(accent, null, new Point(cx, cy), 2.5, 2.5);

            if (i % step == 0 || i == points.Count - 1)
            {
                var t = Text(points[i].Label, 9, TextBrush);
                dc.DrawText(t, new Point(Math.Min(w - t.Width, Math.Max(0, cx - t.Width / 2)), topPad + plotH + 6));
            }
        }
    }
}
