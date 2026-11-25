using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Page.Auth;

public partial class ProfilePickerWindow : Window
{
    private readonly IProfileService _profileService;
    private readonly ICityService _cityService;
    private readonly IWatchlistService _watchlistService;
    private readonly int? _lastProfileId;

    public ObservableCollection<ProfileDto> Profiles { get; } = [];

    public ProfileDto? SelectedProfile { get; private set; }

    public ProfilePickerWindow(
        IProfileService profileService,
        ICityService cityService,
        IWatchlistService watchlistService,
        int? lastProfileId
    )
    {
        _profileService = profileService;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _lastProfileId = lastProfileId;

        InitializeComponent();

        ProfilesList.ItemsSource = Profiles;
        Loaded += ProfilePickerWindow_Loaded;
    }

    private async void ProfilePickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var profiles = await _profileService.ListAsync();

            Profiles.Clear();

            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            EmptyStateText.Visibility =
                Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var lastProfile = Profiles.FirstOrDefault(profile => profile.Id == _lastProfileId);

            ProfilesList.SelectedItem = lastProfile ?? Profiles.FirstOrDefault();
        }
        catch (ClimaPulseException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not load profiles",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
        catch (Exception)
        {
            MessageBox.Show(
                "Profiles could not be loaded. Check that PostgreSQL is running and the connection setting is correct.",
                "Could not load profiles",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedProfile = ProfilesList.SelectedItem as ProfileDto;

        ContinueButton.IsEnabled = SelectedProfile is not null;

        ContinueButton.Content = SelectedProfile is null
            ? "Continue"
            : $"Continue as {SelectedProfile.DisplayName}";
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var newProfileWindow = new NewProfileWindow(_profileService) { Owner = this };

        if (newProfileWindow.ShowDialog() != true || newProfileWindow.CreatedProfile is null)
        {
            return;
        }

        var createdProfile = newProfileWindow.CreatedProfile;

        Profiles.Add(createdProfile);
        EmptyStateText.Visibility = Visibility.Collapsed;
        ProfilesList.SelectedItem = createdProfile;

        var firstCityWindow = new FirstCityWindow(createdProfile, _cityService, _watchlistService)
        {
            Owner = this,
        };

        if (firstCityWindow.ShowDialog() != true)
        {
            return;
        }

        SelectedProfile = createdProfile;
        DialogResult = true;
    }
}
