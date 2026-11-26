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

        LoadProfile();
        Loaded += SettingsView_Loaded;
    }

    private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCityCountAsync();
    }

    private void LoadProfile()
    {
        DisplayNameTextBox.Text = _profile.DisplayName;

        CelsiusRadio.IsChecked = _profile.PreferredTemperatureUnit == TemperatureUnit.Celsius;

        FahrenheitRadio.IsChecked = _profile.PreferredTemperatureUnit == TemperatureUnit.Fahrenheit;

        NameErrorText.Visibility = Visibility.Collapsed;
        SavedBanner.Visibility = Visibility.Collapsed;
    }

    private async Task LoadCityCountAsync()
    {
        try
        {
            var cities = await _watchlistService.ListAsync(_profile.Id);

            CityCountText.Text = cities.Count switch
            {
                0 => "No cities on this profile",
                1 => "1 city on this profile",
                _ => $"{cities.Count} cities on this profile",
            };

            // First choice refreshes every city (Tag null); the rest pick one city.
            BaselineCityComboBox.Items.Clear();
            BaselineCityComboBox.Items.Add(new ComboBoxItem { Content = "All cities" });

            foreach (var entry in cities)
            {
                BaselineCityComboBox.Items.Add(new ComboBoxItem { Content = entry.City.Name, Tag = entry.City });
            }

            BaselineCityComboBox.SelectedIndex = 0;
            NoCitiesText.Visibility = cities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshBaselinesButton.IsEnabled = cities.Count > 0;
        }
        catch
        {
            CityCountText.Text = "City count is unavailable.";
        }
    }

    /// <summary>One city at a time (FR-07 to FR-09). A refused or failed city is skipped.</summary>
    private async void RefreshBaselinesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshBaselinesButton.IsEnabled = false;
        BaselineCityComboBox.IsEnabled = false;
        BaselineErrorText.Text = string.Empty;

        try
        {
            var entries = BaselineCityComboBox.SelectedItem is ComboBoxItem { Tag: CityDto chosen }
                ? [chosen]
                : (await _watchlistService.ListAsync(_profile.Id)).Select(entry => entry.City).ToList();
            var errors = new List<string>();

            BaselineProgress.Value = 0;
            BaselineProgress.Visibility = Visibility.Visible;

            for (var index = 0; index < entries.Count; index++)
            {
                var city = entries[index];
                var done = index;
                BaselineStatusText.Text = $"Refreshing {index + 1} of {entries.Count} cities · {city.Name}";

                // Created on the UI thread; overall progress across all cities.
                var progress = new Progress<int>(percent =>
                    BaselineProgress.Value = (done * 100d + percent) / entries.Count
                );

                try
                {
                    await _baselineService.RefreshAsync(city.Id, progress);
                }
                catch (ClimaPulseException ex)
                {
                    errors.Add($"{city.Name}: {ex.Message}");
                }
            }

            BaselineProgress.Value = 100;
            BaselineStatusText.Text =
                $"Baselines refreshed for {entries.Count - errors.Count} of {entries.Count} cities.";
            BaselineErrorText.Text = string.Join(Environment.NewLine, errors);
        }
        catch (ClimaPulseException ex)
        {
            BaselineErrorText.Text = ex.Message;
        }
        finally
        {
            RefreshBaselinesButton.IsEnabled = true;
            BaselineCityComboBox.IsEnabled = true;
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
            NameErrorText.Text = $"Enter a display name from 1 to 50 characters. This one has {name.Length}.";

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
}
