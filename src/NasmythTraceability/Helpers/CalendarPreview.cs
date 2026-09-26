using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NasmythTraceability.Helpers;

/// <summary>Dev helper: renders a bare Calendar with the app theme applied (used by --calpreview).</summary>
internal static class CalendarPreview
{
    public static void Render(string path)
    {
        var cal = new Calendar
        {
            DisplayDate = DateTime.Today,
            SelectedDate = DateTime.Today,
        };

        var host = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x1B, 0x33)),
            Padding = new Thickness(24),
            Child = cal,
        };

        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();

        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(host.ActualWidth * 2), (int)Math.Ceiling(host.ActualHeight * 2),
            192, 192, PixelFormats.Pbgra32);
        rtb.Render(host);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
