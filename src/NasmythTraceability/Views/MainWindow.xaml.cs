using System.Windows;
using System.Windows.Input;
using NasmythTraceability.Helpers;
using NasmythTraceability.ViewModels;

namespace NasmythTraceability.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Set by <see cref="App"/> before the window is shown.</summary>
    public AppServices? AppServices { get; set; }

    // Every key press, click, wheel turn or touch restarts the page's idle timeout.
    private void NotifyActivity() => (DataContext as MainViewModel)?.NotifyActivity();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        NotifyActivity();
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        NotifyActivity();
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        NotifyActivity();
        base.OnPreviewMouseWheel(e);
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        NotifyActivity();
        base.OnPreviewTouchDown(e);
    }
}
