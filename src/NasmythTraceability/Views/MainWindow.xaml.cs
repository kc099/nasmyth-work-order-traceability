using System.Windows;
using System.Windows.Interop;
using NasmythTraceability.Helpers;

namespace NasmythTraceability.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Set by <see cref="App"/> before the window is shown.</summary>
    public AppServices? AppServices { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (AppServices?.RawInput is not { } scanner)
            return;

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            scanner.Attach(hwnd);
        }
        catch (Exception ex)
        {
            if (Environment.GetCommandLineArgs().Any(a =>
                    string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("SMOKE: scanner attach failed (non-fatal): " + ex.Message);
                return;
            }

            MessageBox.Show(
                "USB scanner input could not be initialised. You can still use manual entry.\n\n" + ex.Message,
                "Scanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
