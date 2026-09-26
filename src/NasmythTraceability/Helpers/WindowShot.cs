using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NasmythTraceability.Helpers;

/// <summary>Dev helper: saves a PNG of a live window's visual tree (used by --shot).</summary>
internal static class WindowShot
{
    public static void Capture(Window window, string path)
    {
        window.UpdateLayout();

        var w = (int)Math.Ceiling(window.ActualWidth);
        var h = (int)Math.Ceiling(window.ActualHeight);
        if (w <= 0 || h <= 0)
        {
            w = 1340;
            h = 840;
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
