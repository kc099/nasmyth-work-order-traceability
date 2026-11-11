using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TraceabilitySystem.Data;

namespace TraceabilitySystem.Controls;

/// <summary>
/// Company logo for the header: <c>Assets/logo.png</c>, compiled into the app. If the image is
/// missing the company name is shown instead, so the header never breaks.
/// </summary>
public partial class BrandLogo : UserControl
{
    // Relative to the application assembly, so it does not depend on the assembly name.
    private const string LogoResource = "pack://application:,,,/Assets/logo.png";

    public BrandLogo()
    {
        InitializeComponent();
        LoadLogo();
    }

    private void LoadLogo()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri(LogoResource, UriKind.Absolute));
            if (info is not null)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = info.Stream;
                bitmap.EndInit();
                bitmap.Freeze();
                RasterLogo.Source = bitmap;
                return;
            }
        }
        catch
        {
            // not present / not an image - fall through to the text
        }

        RasterLogo.Visibility = Visibility.Collapsed;
        TextLogo.Text = SettingsService.DefaultCompanyName;
        TextLogo.Visibility = Visibility.Visible;
    }
}
