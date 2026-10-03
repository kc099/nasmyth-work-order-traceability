using System.Windows;
using NasmythTraceability.Data;

namespace NasmythTraceability.Views;

/// <summary>Asks for the administrator password before a record is deleted.</summary>
public partial class PasswordDialog : Window
{
    private readonly SettingsService _settings;

    private PasswordDialog(string prompt, SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        PromptText.Text = prompt;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>Shows the prompt; true only when the correct password was entered and confirmed.</summary>
    public static bool Confirm(string prompt, SettingsService settings)
    {
        var dialog = new PasswordDialog(prompt, settings) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!_settings.CheckPassword(PasswordInput.Password))
        {
            ErrorText.Visibility = Visibility.Visible;
            PasswordInput.Clear();
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
