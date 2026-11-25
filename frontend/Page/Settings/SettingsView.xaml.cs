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
    private readonly Action<ProfileDto> _profileUpdated;
    private readonly Func<Task> _switchProfile;

    public SettingsView(
        ProfileDto profile,
        IProfileService profileService,
        IWatchlistService watchlistService,
        Action<ProfileDto> profileUpdated,
        Func<Task> switchProfile
    )
    {
        _profile = profile;
        _profileService = profileService;
        _watchlistService = watchlistService;
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

            CityCountText.Text =
                cities.Count == 1
                    ? "1 city on this profile"
                    : $"{cities.Count} cities on this profile";
        }
        catch
        {
            CityCountText.Text = "City count is unavailable.";
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
}
