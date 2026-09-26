using System.Windows;
using System.Windows.Media;

namespace NasmythTraceability.Controls;

/// <summary>Donut chart with legend - used for "Scan Results (OK vs NG)".</summary>
public sealed class DonutChartControl : ChartBase
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var points = Points().Where(p => p.Value > 0).ToList();
        var total = points.Sum(p => p.Value);
        if (total <= 0)
        {
            DrawEmpty(dc);
            return;
        }

        var legendW = 120.0;
        var size = Math.Max(20, Math.Min(w - legendW, h) - 16);
        var cx = 8 + size / 2;
        var cy = h / 2;
        var outer = size / 2;
        var inner = outer * 0.58;

        var start = -90.0;
        var allPoints = Points();
        for (var i = 0; i < allPoints.Count; i++)
        {
            var p = allPoints[i];
            if (p.Value <= 0)
                continue;

            var sweep = 360.0 * (p.Value / total);
            var brush = i == 0 ? OkBrush : i == 1 ? NgBrush : SeriesBrush(i);
            dc.DrawGeometry(brush, null, RingSegment(new Point(cx, cy), outer, inner, start, sweep));
            start += sweep;
        }

        // centre label
        var pct = Text($"{(allPoints.Count > 0 ? allPoints[0].Value / total : 0):P0}", 16, TextBrush, FontWeights.Bold);
        dc.DrawText(pct, new Point(cx - pct.Width / 2, cy - pct.Height / 2 - 6));
        var cap = Text("OK", 10, TextBrush);
        dc.DrawText(cap, new Point(cx - cap.Width / 2, cy + 8));

        // legend
        var lx = cx + outer + 18;
        var ly = cy - allPoints.Count * 12;
        for (var i = 0; i < allPoints.Count; i++)
        {
            var p = allPoints[i];
            var brush = i == 0 ? OkBrush : i == 1 ? NgBrush : SeriesBrush(i);
            dc.DrawRoundedRectangle(brush, null, new Rect(lx, ly, 12, 12), 2, 2);
            var share = total > 0 ? p.Value / total : 0;
            var t = Text($"{p.Label}  {p.Value:0} ({share:P0})", 11, TextBrush);
            dc.DrawText(t, new Point(lx + 18, ly - 1));
            ly += 24;
        }
    }

    private Brush OkBrush => Application.Current?.TryFindResource("AppOkBrush") as Brush ?? Brushes.SeaGreen;
    private Brush NgBrush => Application.Current?.TryFindResource("AppNgBrush") as Brush ?? Brushes.IndianRed;

    private static Geometry RingSegment(Point center, double outer, double inner, double startDeg, double sweepDeg)
    {
        if (sweepDeg >= 359.999)
            sweepDeg = 359.999;

        var startRad = startDeg * Math.PI / 180.0;
        var endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

        Point OnCircle(double r, double a) => new(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));

        var p1 = OnCircle(outer, startRad);
        var p2 = OnCircle(outer, endRad);
        var p3 = OnCircle(inner, endRad);
        var p4 = OnCircle(inner, startRad);
        var large = sweepDeg > 180;

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p1, isFilled: true, isClosed: true);
            ctx.ArcTo(p2, new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(p3, true, false);
            ctx.ArcTo(p4, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }

        g.Freeze();
        return g;
    }
}
