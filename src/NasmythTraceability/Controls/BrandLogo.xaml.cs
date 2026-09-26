using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace NasmythTraceability.Controls;

/// <summary>
/// Nasmyth brand mark. Uses <c>Assets/nasmyth-logo.png</c> (or .jpg) when that
/// resource is compiled into the assembly; otherwise renders a vector fallback.
/// </summary>
public partial class BrandLogo : UserControl
{
    private static readonly string[] CandidateResources =
    {
        "pack://application:,,,/NasmythTraceability;component/Assets/nasmyth-logo.png",
        "pack://application:,,,/NasmythTraceability;component/Assets/nasmyth-logo.jpg",
    };

    public BrandLogo()
    {
        InitializeComponent();
        TryLoadRasterLogo();
    }

    private void TryLoadRasterLogo()
    {
        foreach (var resource in CandidateResources)
        {
            try
            {
                var uri = new Uri(resource, UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info is null)
                    continue;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = info.Stream;
                bitmap.EndInit();
                bitmap.Freeze();

                RasterLogo.Source = bitmap;
                RasterLogo.Visibility = Visibility.Visible;
                VectorLogo.Visibility = Visibility.Collapsed;
                return;
            }
            catch
            {
                // resource not present / not an image - keep the vector fallback
            }
        }
    }
}
