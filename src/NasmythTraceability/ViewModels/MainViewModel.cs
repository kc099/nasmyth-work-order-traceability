using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;

namespace NasmythTraceability.ViewModels;

public enum AppPage
{
    Dashboard,
    TagAssignment,
    ScanInformation,
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
        TagAssignment = new TagAssignmentViewModel(services);
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
        _clock.Tick += (_, _) =>
        {
            UpdateClock();

            // A password prompt, confirmation or file dialog is the user at work, not idling.
            if (ComponentDispatcher.IsThreadModal)
                NotifyActivity();
            else
                CheckIdle(DateTime.Now);
        };
        _clock.Start();
        UpdateClock();
    }

    public DashboardViewModel Dashboard { get; }
    public TagAssignmentViewModel TagAssignment { get; }
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
        AppPage.TagAssignment => TagAssignment,
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

        // Tags are assigned only on the Tag Assignment page, and it locks again when left.
        TagAssignment.SetActive(value == AppPage.TagAssignment);

        // Opening a page counts as activity: its idle timeout starts now.
        NotifyActivity();
    }

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    // ---- idle timeout ---------------------------------------------------
    // Every page except the Dashboard closes (back to the Dashboard, which also locks Settings
    // and Tag Assignment) after a period with no key press, click or touch. A warning with a
    // countdown is shown for the last seconds; any input dismisses it and restarts the period.
    private const int IdleWarningSeconds = 30;
    private DateTime _lastActivity = DateTime.Now;

    [ObservableProperty] private bool _isIdleWarningVisible;
    [ObservableProperty] private string _idleWarningText = "";

    public void NotifyActivity() => NotifyActivity(DateTime.Now);

    public void NotifyActivity(DateTime now)
    {
        _lastActivity = now;
        IsIdleWarningVisible = false;
    }

    /// <summary>Called once a second: shows the warning, or closes the page when its time is up.</summary>
    public void CheckIdle(DateTime now)
    {
        var timeout = _services.Settings.GetInt(SettingsService.PageTimeoutSeconds, 120);
        if (CurrentPage == AppPage.Dashboard || timeout <= 0)
        {
            IsIdleWarningVisible = false;
            return;
        }

        var left = timeout - (int)(now - _lastActivity).TotalSeconds;
        if (left <= 0)
        {
            IsIdleWarningVisible = false;
            CurrentPage = AppPage.Dashboard;
            return;
        }

        if (left <= IdleWarningSeconds)
        {
            IdleWarningText = $"This page will close in {left} second{(left == 1 ? "" : "s")} if no key is pressed.";
            IsIdleWarningVisible = true;
        }
    }

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
        TagAssignment.Dispose();
        Reports.Dispose();
        Settings.Dispose();
    }
}
