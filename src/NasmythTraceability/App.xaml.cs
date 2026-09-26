using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;
using NasmythTraceability.ViewModels;
using NasmythTraceability.Views;

namespace NasmythTraceability;

public partial class App : Application
{
    private AppServices? _services;
    private MainViewModel? _mainViewModel;
    private bool _smoke;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless smoke test: NasmythTraceability.exe --selftest
        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsoleIfPossible();
            var code = SelfTest.Run();
            Shutdown(code);
            return;
        }

        // Render the header logo to a PNG for review: --logopng <path>
        var logoArg = Array.IndexOf(e.Args, "--logopng");
        if (logoArg >= 0 && logoArg + 1 < e.Args.Length)
        {
            AttachConsoleIfPossible();
            try
            {
                LogoPreview.Render(e.Args[logoArg + 1]);
                Console.WriteLine("LOGO: wrote " + e.Args[logoArg + 1]);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("LOGO: failed " + ex);
                Shutdown(1);
            }
            return;
        }

        var calArg = Array.IndexOf(e.Args, "--calpreview");
        if (calArg >= 0 && calArg + 1 < e.Args.Length)
        {
            AttachConsoleIfPossible();
            try
            {
                CalendarPreview.Render(e.Args[calArg + 1]);
                Console.WriteLine("CAL: wrote " + e.Args[calArg + 1]);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("CAL: failed " + ex);
                Shutdown(1);
            }
            return;
        }

        // Reset scan data to a fresh-install state: --wipe-data
        if (e.Args.Any(a => string.Equals(a, "--wipe-data", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsoleIfPossible();
            try
            {
                using var svc = new AppServices(AppConfig.Load(), useSimulatedScanner: true);
                svc.Trace.PurgeAll();
                var devices = svc.Stations.DeleteAllDevices();
                Console.WriteLine($"WIPE: db = {svc.Database.DatabasePath}");
                Console.WriteLine($"WIPE: cleared scan history, current positions and logs; " +
                                  $"removed {devices} scanner mapping(s)");
                Console.WriteLine("WIPE: stations and settings kept");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("WIPE: failed " + ex);
                Shutdown(1);
            }
            return;
        }

        // Populate the database with a sample dataset: --seed-demo [units] [days] [--append]
        var seedArg = Array.IndexOf(e.Args, "--seed-demo");
        if (seedArg >= 0)
        {
            AttachConsoleIfPossible();
            var units = seedArg + 1 < e.Args.Length && int.TryParse(e.Args[seedArg + 1], out var u) ? u : 40;
            var days = seedArg + 2 < e.Args.Length && int.TryParse(e.Args[seedArg + 2], out var d) ? d : 7;
            var reset = !e.Args.Any(a => string.Equals(a, "--append", StringComparison.OrdinalIgnoreCase));
            try
            {
                using var svc = new AppServices(AppConfig.Load(), useSimulatedScanner: true);
                var s = DemoData.Generate(svc, units, days, reset);
                Console.WriteLine($"SEED: db = {svc.Database.DatabasePath}");
                Console.WriteLine($"SEED: {s.Units} units, {s.Events} trace events, {s.Logs} log rows");
                Console.WriteLine($"SEED: {s.Completed} completed, {s.InProgress} in progress, {s.Ng} NG reads");
                Console.WriteLine($"SEED: range {s.From:yyyy-MM-dd HH:mm} .. {s.To:yyyy-MM-dd HH:mm}  (reset={reset})");
                Console.WriteLine("SEED: done");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SEED: failed " + ex);
                Shutdown(1);
            }
            return;
        }

        // UI smoke test: build the shell, then close after a moment.
        _smoke = e.Args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var config = AppConfig.Load();
        _services = new AppServices(config, useSimulatedScanner: false);

        var fontSize = _services.Settings.GetDouble(SettingsService.BaseFontSize, 14);
        Resources["AppBaseFontSize"] = Math.Clamp(fontSize, 11, 20);

        _mainViewModel = new MainViewModel(_services);

        var window = new MainWindow
        {
            DataContext = _mainViewModel,
            AppServices = _services,
        };

        if (_services.Settings.GetBool(SettingsService.StartFullScreen, false))
        {
            window.WindowState = WindowState.Maximized;
            window.WindowStyle = WindowStyle.None;
        }

        MainWindow = window;
        window.Show();

        var shotArg = Array.IndexOf(e.Args, "--shot");
        var shotPath = shotArg >= 0 && shotArg + 1 < e.Args.Length ? e.Args[shotArg + 1] : null;

        var pageArg = Array.IndexOf(e.Args, "--page");
        if (pageArg >= 0 && pageArg + 1 < e.Args.Length
            && Enum.TryParse<AppPage>(e.Args[pageArg + 1], ignoreCase: true, out var page))
        {
            _mainViewModel.CurrentPage = page;
        }

        var tabArg = Array.IndexOf(e.Args, "--tab");
        if (tabArg >= 0 && tabArg + 1 < e.Args.Length && int.TryParse(e.Args[tabArg + 1], out var tab))
            _mainViewModel.Settings.SettingsTabIndex = tab;

        if (_smoke || shotPath is not null)
        {
            AttachConsoleIfPossible();
            Console.WriteLine("SMOKE: main window shown OK");
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(1.2), DispatcherPriority.Background,
                (_, _) =>
                {
                    if (shotPath is not null)
                    {
                        try
                        {
                            WindowShot.Capture(window, shotPath);
                            Console.WriteLine("SHOT: wrote " + shotPath);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("SHOT: failed " + ex.Message);
                        }
                    }

                    Shutdown(0);
                }, Dispatcher);
            timer.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_smoke)
        {
            AttachConsoleIfPossible();
            Console.WriteLine("SMOKE: unhandled exception");
            Console.WriteLine(e.Exception);
            e.Handled = true;
            Shutdown(1);
            return;
        }

        MessageBox.Show(e.Exception.Message, "Unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static void AttachConsoleIfPossible()
    {
        if (!AttachConsole(-1))
            AllocConsole();
    }
}
