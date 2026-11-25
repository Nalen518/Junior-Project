using System.IO;
using System.Net.Http;
using System.Windows;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Page.Auth;
using ClimaPulse.Desktop.State;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Desktop;

public partial class App : Application
{
    private IProfileService _profileService = null!;
    private ICityService _cityService = null!;
    private IWatchlistService _watchlistService = null!;
    private IEventLedgerService _eventLedgerService = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        try
        {
            var connectionString = DatabaseConfig.ConnectionString;
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql");

            DatabaseInitializer.EnsureCreated(connectionString, schemaPath);

            var profiles = new UserProfileRepository(connectionString);
            var cities = new CityRepository(connectionString);
            var watchlist = new WatchlistRepository(connectionString);

            _profileService = new ProfileService(profiles);

            _cityService = new CityService(
                cities,
                new WeatherService(
                    new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
                    new OpenMeteoOptions()
                )
            );

            _watchlistService = new WatchlistService(watchlist, profiles, cities);

            _eventLedgerService = new EventLedgerService(
                new AnomalyEventRepository(connectionString),
                cities
            );

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
            var firstCityWindow = new FirstCityWindow(profile, _cityService, _watchlistService)
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
