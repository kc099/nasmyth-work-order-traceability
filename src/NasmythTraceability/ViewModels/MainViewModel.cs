using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;

namespace NasmythTraceability.ViewModels;

public enum AppPage
{
    Dashboard,
    CurrentInformation,
    Reports,
    Settings
}

/// <summary>Shell view model: header clock, navigation and the child screens.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _clock;

    public MainViewModel(AppServices services)
    {
        _services = services;

        Dashboard = new DashboardViewModel(services);
        Reports = new ReportsViewModel(services);
        Settings = new SettingsViewModel(services);

        ApplicationTitle = services.Settings.Get(SettingsService.ApplicationName, "Work Order Traceability System");
        CompanyTitle = services.Settings.Get(SettingsService.CompanyName, "Nasmyth Asia (IN) Pvt Ltd.");
        AppVersion = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        _dateFormat = SanitizeDateFormat(services.Settings.Get(SettingsService.DateTimeFormat));
        services.Settings.Changed += (_, _) =>
        {
            _dateFormat = SanitizeDateFormat(services.Settings.Get(SettingsService.DateTimeFormat));
            ApplicationTitle = services.Settings.Get(SettingsService.ApplicationName, ApplicationTitle);
            CompanyTitle = services.Settings.Get(SettingsService.CompanyName, CompanyTitle);
            UpdateClock();
        };

        _clock = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
    }

    public DashboardViewModel Dashboard { get; }
    public ReportsViewModel Reports { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _applicationTitle = "";
    [ObservableProperty] private string _companyTitle = "";
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private string _dateText = "";

    private string _dateFormat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveViewModel))]
    private AppPage _currentPage = AppPage.Dashboard;

    /// <summary>View model handed to the content region for the current page.</summary>
    public object ActiveViewModel => CurrentPage switch
    {
        AppPage.Reports => Reports,
        AppPage.Settings => Settings,
        _ => Dashboard,
    };

    partial void OnCurrentPageChanged(AppPage value)
    {
        // Reports data is computed on demand - refresh it whenever the page is opened
        // so edits/deletes made elsewhere are reflected.
        if (value == AppPage.Reports)
            Reports.Refresh();

        // Editing must be unlocked again each time the Settings screen is opened.
        if (value != AppPage.Settings)
            Settings.Lock();
    }

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    private void UpdateClock()
    {
        var now = DateTime.Now;

        // Big line = time only; small line above = the date (no duplication).
        ClockText = now.ToString("HH:mm:ss");
        try
        {
            DateText = now.ToString(_dateFormat);
        }
        catch (FormatException)
        {
            DateText = now.ToString(DefaultDateFormat);
        }
    }

    private const string DefaultDateFormat = "dddd, dd MMMM yyyy";

    /// <summary>Header date line is date-only - drop any time tokens a saved format may carry.</summary>
    private static string SanitizeDateFormat(string? format)
        => string.IsNullOrWhiteSpace(format) || format.IndexOfAny(new[] { 'H', 'h', 's' }) >= 0
            ? DefaultDateFormat
            : format.Trim();

    public void Dispose()
    {
        _clock.Stop();
        Dashboard.Dispose();
        Reports.Dispose();
        Settings.Dispose();
    }
}
