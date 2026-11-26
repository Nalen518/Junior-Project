using System.IO;
using System.Net.Http;
using System.Windows;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Page.Auth;
using ClimaPulse.Desktop.State;
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
    private IEventLedgerService _eventLedgerService = null!;

    // New backend services used by the real dashboard and city-detail screens.
    private IDashboardService _dashboardService = null!;
    private IMonitoringService _monitoringService = null!;
    private IBaselineService _baselineService = null!;
    private IAdvisoryService _advisoryService = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            var connectionString = DatabaseConfig.ConnectionString;
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql");

            DatabaseInitializer.EnsureCreated(connectionString, schemaPath);

            // Repositories are created only in the composition root.
            var profiles = new UserProfileRepository(connectionString);
            var cities = new CityRepository(connectionString);
            var watchlist = new WatchlistRepository(connectionString);
            var baselines = new BaselineRepository(connectionString);
            var history = new HistoricalObservationRepository(connectionString);
            var events = new AnomalyEventRepository(connectionString);
            var advisories = new AdvisoryRepository(connectionString);

            // All services share one WeatherService and one in-memory refresh state.
            var weather = new WeatherService(
                new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
                new OpenMeteoOptions()
            );

            var refreshState = new CityRefreshState();

            var strategies = new Dictionary<MetricType, IAnomalyStrategy>
            {
                [MetricType.Temperature] = new TemperatureStrategy(),
                [MetricType.Precipitation] = new PrecipitationStrategy(),
                [MetricType.WindSpeed] = new WindStrategy(),
            };

            var anomalyDetection = new AnomalyDetectionService(
                strategies,
                weather,
                baselines,
                events
            );

            // Contract services received by WPF windows and UserControls.
            _profileService = new ProfileService(profiles);
            _cityService = new CityService(cities, weather);
            _watchlistService = new WatchlistService(watchlist, profiles, cities);
            _eventLedgerService = new EventLedgerService(events, cities);

            _advisoryService = new AdvisoryService(advisories);

            _baselineService = new BaselineService(
                cities,
                history,
                baselines,
                weather,
                refreshState
            );

            _monitoringService = new MonitoringService(
                cities,
                baselines,
                events,
                weather,
                anomalyDetection,
                (AdvisoryService)_advisoryService,
                refreshState
            );

            _dashboardService = new DashboardService(profiles, watchlist, cities, refreshState);

            if (!await OpenProfileSessionAsync())
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ClimaPulse could not start.\n\n{ex.Message}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown();
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
            _advisoryService,
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
