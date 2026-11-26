using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Controls;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Page;

public partial class SettingsView : UserControl
{
    private ProfileDto _profile;

    private readonly IProfileService _profileService;
    private readonly IWatchlistService _watchlistService;
    private readonly IBaselineService _baselineService;
    private readonly Action<ProfileDto> _profileUpdated;
    private readonly Func<Task> _switchProfile;

    private readonly ObservableCollection<WatchlistEntryDto> _cities = [];

    private bool _isRefreshingBaseline;

    public SettingsView(
        ProfileDto profile,
        IProfileService profileService,
        IWatchlistService watchlistService,
        IBaselineService baselineService,
        Action<ProfileDto> profileUpdated,
        Func<Task> switchProfile
    )
    {
        _profile = profile;
        _profileService = profileService;
        _watchlistService = watchlistService;
        _baselineService = baselineService;
        _profileUpdated = profileUpdated;
        _switchProfile = switchProfile;

        InitializeComponent();

        BaselineCityComboBox.ItemsSource = _cities;

        LoadProfile();

        Loaded += SettingsView_Loaded;
    }

    private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCitiesAsync();
    }

    private void LoadProfile()
    {
        DisplayNameTextBox.Text = _profile.DisplayName;

        CelsiusRadio.IsChecked = _profile.PreferredTemperatureUnit == TemperatureUnit.Celsius;

        FahrenheitRadio.IsChecked = _profile.PreferredTemperatureUnit == TemperatureUnit.Fahrenheit;

        NameErrorText.Visibility = Visibility.Collapsed;
        SavedBanner.Visibility = Visibility.Collapsed;
    }

    private async Task LoadCitiesAsync()
    {
        try
        {
            var cities = await _watchlistService.ListAsync(_profile.Id);

            _cities.Clear();

            foreach (var city in cities)
            {
                _cities.Add(city);
            }

            CityCountText.Text = _cities.Count switch
            {
                0 => "No cities on this profile",
                1 => "1 city on this profile",
                _ => $"{_cities.Count} cities on this profile",
            };

            NoCitiesText.Visibility =
                _cities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            BaselineCityComboBox.SelectedItem = _cities.FirstOrDefault();

            UpdateBaselineRefreshButton();
        }
        catch
        {
            CityCountText.Text = "City count is unavailable.";
            NoCitiesText.Visibility = Visibility.Collapsed;
        }
    }

    private void DisplayNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NameErrorText.Visibility = Visibility.Collapsed;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = DisplayNameTextBox.Text.Trim();

        if (name.Length is < 1 or > 50)
        {
            NameErrorText.Text = "Enter a display name from 1 to 50 characters.";

            NameErrorText.Visibility = Visibility.Visible;

            return;
        }

        var unit =
            FahrenheitRadio.IsChecked == true
                ? TemperatureUnit.Fahrenheit
                : TemperatureUnit.Celsius;

        SaveButton.IsEnabled = false;

        try
        {
            _profile = await _profileService.UpdateAsync(
                _profile.Id,
                new UpdateProfileRequest(name, unit, _profile.PreferredLocale)
            );

            _profileUpdated(_profile);

            SavedBanner.Message =
                "Settings saved. Your display name and temperature unit are updated.";

            SavedBanner.Kind = "Success";
            SavedBanner.Visibility = Visibility.Visible;
        }
        catch (ValidationException ex)
        {
            NameErrorText.Text = ex.Errors.Values.SelectMany(messages => messages).FirstOrDefault()
                ?? "The settings could not be saved.";

            NameErrorText.Visibility = Visibility.Visible;
        }
        catch (ClimaPulseException ex)
        {
            NameErrorText.Text = ex.Message;
            NameErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        LoadProfile();
    }

    private async void SwitchProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await _switchProfile();
    }

    private void BaselineCityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBaselineRefreshButton();
    }

    private async void RefreshBaselinesButton_Click(object sender, RoutedEventArgs e)
    {
        if (
            _isRefreshingBaseline || BaselineCityComboBox.SelectedItem is not WatchlistEntryDto city
        )
        {
            return;
        }

        SetBaselineRefreshState(true);

        try
        {
            var progress = new Progress<int>(percentage =>
            {
                BaselineProgressBar.Value = percentage;

                BaselineProgressText.Text = percentage switch
                {
                    < 60 => $"Downloading historical weather for {city.City.Name}…",

                    < 100 => $"Calculating monthly baselines for {city.City.Name}…",

                    _ => $"Baseline for {city.City.Name} is ready.",
                };
            });

            await _baselineService.RefreshAsync(city.City.Id, progress);

            SavedBanner.Message =
                $"Baseline refreshed for {city.City.Name}. "
                + "Use Dashboard or City Detail to refresh its current weather.";

            SavedBanner.Kind = "Success";
            SavedBanner.Visibility = Visibility.Visible;
        }
        catch (ConflictException ex)
        {
            ShowBaselineFailure(ex.Message);
        }
        catch (ClimaPulseException ex)
        {
            ShowBaselineFailure(ex.Message);
        }
        catch (Exception)
        {
            ShowBaselineFailure(
                "Baseline refresh failed. Check your internet connection and try again."
            );
        }
        finally
        {
            SetBaselineRefreshState(false);
        }
    }

    private void SetBaselineRefreshState(bool isRefreshing)
    {
        _isRefreshingBaseline = isRefreshing;

        BaselineCityComboBox.IsEnabled = !isRefreshing;

        RefreshBaselinesButton.Content = isRefreshing
            ? "Refreshing baseline…"
            : "Refresh selected baseline";

        BaselineProgressPanel.Visibility = isRefreshing ? Visibility.Visible : Visibility.Collapsed;

        if (isRefreshing)
        {
            BaselineProgressBar.Value = 0;
            BaselineProgressText.Text = "Preparing baseline refresh…";
        }

        UpdateBaselineRefreshButton();
    }

    private void UpdateBaselineRefreshButton()
    {
        RefreshBaselinesButton.IsEnabled =
            !_isRefreshingBaseline && BaselineCityComboBox.SelectedItem is WatchlistEntryDto;
    }

    private void ShowBaselineFailure(string message)
    {
        SavedBanner.Message = message;
        SavedBanner.Kind = "Failure";
        SavedBanner.Visibility = Visibility.Visible;
    }
}
