using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Page.Auth;
using ClimaPulse.Desktop.State;
using ClimaPulse.Desktop.Ui;
using ClimaPulse.Enums;
using ClimaPulse.Repositories;
using ClimaPulse.Services;
using ClimaPulse.Strategies;

namespace ClimaPulse.Desktop;

public partial class App : Application
{
    private IProfileService _profileService = null!;
    private ICityService _cityService = null!;
    private IWatchlistService _watchlistService = null!;
    private IBaselineService _baselineService = null!;
    private IMonitoringService _monitoringService = null!;
    private IDashboardService _dashboardService = null!;
    private IEventLedgerService _eventLedgerService = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject);
        base.OnStartup(e);

        try
        {
            CreateServices();

            if (!await OpenProfileSessionAsync())
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            LogError(ex);

            MessageBox.Show(
                "ClimaPulse could not start. Check that:\n\n"
                    + "• PostgreSQL is running\n"
                    + "• the database exists (run: createdb climapulse)\n"
                    + "• CLIMAPULSE_DB_CONNECTION points to that database\n\n"
                    + @"Details are in %LOCALAPPDATA%\ClimaPulse\logs\error.log.",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown();
        }
    }

    /// <summary>The only place that creates backend classes (composition root).</summary>
    private void CreateServices()
    {
        var cs = DatabaseConfig.ConnectionString;
        DatabaseInitializer.EnsureCreated(
            cs,
            Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql")
        );

        var profilesRepo = new UserProfileRepository(cs);
        var citiesRepo = new CityRepository(cs);
        var watchlistRepo = new WatchlistRepository(cs);
        var historyRepo = new HistoricalObservationRepository(cs);
        var baselinesRepo = new BaselineRepository(cs);
        var advisoriesRepo = new AdvisoryRepository(cs);
        var eventsRepo = new AnomalyEventRepository(cs);

        var weather = new WeatherService(
            new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
            new OpenMeteoOptions()
        );
        var state = new CityRefreshState(); // one shared instance for baseline, monitoring and dashboard
        var advisories = new AdvisoryService(advisoriesRepo);
        var detection = new AnomalyDetectionService(
            new Dictionary<MetricType, IAnomalyStrategy>
            {
                [MetricType.Temperature] = new TemperatureStrategy(),
                [MetricType.Precipitation] = new PrecipitationStrategy(),
                [MetricType.WindSpeed] = new WindStrategy(),
            },
            weather,
            baselinesRepo,
            eventsRepo
        );

        _profileService = new ProfileService(profilesRepo);
        _cityService = new CityService(citiesRepo, weather);
        _watchlistService = new WatchlistService(watchlistRepo, profilesRepo, citiesRepo);
        _baselineService = new BaselineService(citiesRepo, historyRepo, baselinesRepo, weather, state);
        _monitoringService = new MonitoringService(
            citiesRepo,
            baselinesRepo,
            eventsRepo,
            weather,
            detection,
            advisories,
            state
        );
        _dashboardService = new DashboardService(profilesRepo, watchlistRepo, citiesRepo, state);
        _eventLedgerService = new EventLedgerService(eventsRepo, citiesRepo);
        // IAdvisoryService (advisories) reaches the UI through MetricReadingDto.Advisories, so no screen needs it.
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e
    )
    {
        LogError(e.Exception);
        ServiceErrorPresenter.ShowUnexpected("ClimaPulse");
        e.Handled = true;
    }

    /// <summary>Appends to %LOCALAPPDATA%\ClimaPulse\logs\error.log. Never throws.</summary>
    private static void LogError(object? exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClimaPulse",
                "logs"
            );

            Directory.CreateDirectory(folder);

            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {exception}{Environment.NewLine}{Environment.NewLine}"
            );
        }
        catch (Exception)
        {
            // Logging must never crash the app.
        }
    }

    private async Task<bool> OpenProfileSessionAsync(Window? owner = null)
    {
        var lastProfileId = await LocalSettingsStore.LoadLastProfileIdAsync();

        var profilePicker = new ProfilePickerWindow(
            _profileService,
            _cityService,
            _watchlistService,
            _baselineService,
            lastProfileId
        )
        {
            Owner = owner,
        };

        var profile = profilePicker.ShowDialog() == true ? profilePicker.SelectedProfile : null;

        if (profile is null)
        {
            return false;
        }

        await LocalSettingsStore.SaveLastProfileIdAsync(profile.Id);

        var watchedCities = await _watchlistService.ListAsync(profile.Id);

        if (watchedCities.Count == 0)
        {
            var firstCityWindow = new FirstCityWindow(
                profile,
                _cityService,
                _watchlistService,
                _baselineService
            )
            {
                Owner = owner,
            };

            if (firstCityWindow.ShowDialog() != true)
            {
                return false;
            }
        }

        var mainWindow = new MainWindow(
            profile,
            _profileService,
            _cityService,
            _watchlistService,
            _eventLedgerService,
            _dashboardService,
            _monitoringService,
            _baselineService,
            SwitchProfileAsync
        );

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        mainWindow.Show();

        return true;
    }

    private async Task SwitchProfileAsync()
    {
        var currentWindow = MainWindow;

        if (currentWindow is null)
        {
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        currentWindow.Hide();

        var switched = await OpenProfileSessionAsync(currentWindow);

        if (switched)
        {
            currentWindow.Close();
            return;
        }

        MainWindow = currentWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        currentWindow.Show();
    }
}
