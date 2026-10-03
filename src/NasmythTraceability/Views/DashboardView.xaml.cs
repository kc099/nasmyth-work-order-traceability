using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NasmythTraceability.Models;
using NasmythTraceability.ViewModels;

namespace NasmythTraceability.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    // The cursor stays in the search box while the list of matches is open, so the keys that
    // drive the list are handled here: arrows move the highlight, Enter picks, Escape closes.
    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSuggestionCommand.Execute(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSuggestionCommand.Execute(-1);
                e.Handled = true;
                break;
            case Key.Escape when vm.IsSuggestionsOpen:
                vm.CloseSuggestionsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.SearchCommand.Execute(null);
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private void OnSearchBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ViewModel?.CloseSuggestionsCommand.Execute(null);

    private void OnSuggestionClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: WorkOrderSuggestion suggestion })
            return;

        ViewModel?.PickSuggestionCommand.Execute(suggestion);
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }
}
