using System.Windows;
using System.Windows.Input;
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
                "USB reader input could not be initialised. You can still use manual entry.\n\n" + ex.Message,
                "Reader", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // A keyboard-type reader also types its card number (and the closing Enter) into whatever
    // has focus. The read is already handled through Raw Input, so drop those keystrokes here;
    // otherwise a tap could submit the manual scan box and be recorded twice.
    private bool IsReaderKeystroke => AppServices?.RawInput?.IsReaderTyping == true;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (IsReaderKeystroke)
            e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (IsReaderKeystroke)
            e.Handled = true;
        base.OnPreviewKeyUp(e);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (IsReaderKeystroke)
            e.Handled = true;
        base.OnPreviewTextInput(e);
    }
}
