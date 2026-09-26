using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NasmythTraceability.Controls;

namespace NasmythTraceability.Helpers;

/// <summary>Dev helper: renders the header band with <see cref="BrandLogo"/> to a PNG.</summary>
internal static class LogoPreview
{
    public static void Render(string path)
    {
        var band = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x23, 0x3F)),
            Padding = new Thickness(26, 16, 26, 16),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new BrandLogo { Height = 64, VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel
                    {
                        Margin = new Thickness(16, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Nasmyth Asia (IN) Pvt Ltd.",
                                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFC)),
                                FontSize = 22, FontWeight = FontWeights.SemiBold,
                            },
                            new TextBlock
                            {
                                Text = "Work Order Traceability System",
                                Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB4, 0xCE)),
                                FontSize = 13,
                            },
                        },
                    },
                },
            },
        };

        band.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        band.Arrange(new Rect(band.DesiredSize));
        band.UpdateLayout();

        const double scale = 2.0;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(band.ActualWidth * scale),
            (int)Math.Ceiling(band.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(band);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
