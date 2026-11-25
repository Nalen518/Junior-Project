using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Page;
using ClimaPulse.Desktop.Page.Dashboard.CityDetail;

namespace ClimaPulse.Desktop;

public partial class MainWindow : Window
{
    private ProfileDto _profile;
    private readonly IProfileService _profileService;
    private readonly IWatchlistService _watchlistService;
    private readonly ICityService _cityService;
    private readonly IEventLedgerService _eventLedgerService;
    private readonly Func<Task> _switchProfile;

    public MainWindow(
        ProfileDto profile,
        IProfileService profileService,
        ICityService cityService,
        IWatchlistService watchlistService,
        IEventLedgerService eventLedgerService,
        Func<Task> switchProfile
    )
    {
        _profile = profile;
        _profileService = profileService;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _eventLedgerService = eventLedgerService;
        _switchProfile = switchProfile;

        InitializeComponent();

        ProfileNameText.Text = _profile.DisplayName;

        ShowDashboard();
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboard();
    }

    private void LedgerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowLedger();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void ShowDashboard()
    {
        PageTitleText.Text = "Dashboard";
        PageContent.Content = new DashboardView(
            _profile,
            _cityService,
            _watchlistService,
            ShowCityDetail
        );
        SetSelectedButton(DashboardButton);
    }

    private void ShowLedger()
    {
        PageTitleText.Text = "Anomaly Ledger";
        PageContent.Content = new LedgerView(_eventLedgerService, _cityService);

        SetSelectedButton(LedgerButton);
    }

    private void ShowSettings()
    {
        PageTitleText.Text = "Settings";

        PageContent.Content = new SettingsView(
            _profile,
            _profileService,
            _watchlistService,
            UpdateProfile,
            _switchProfile
        );

        SetSelectedButton(SettingsButton);
    }

    private void UpdateProfile(ProfileDto updatedProfile)
    {
        _profile = updatedProfile;
        ProfileNameText.Text = updatedProfile.DisplayName;
    }

    private void SetSelectedButton(Button selectedButton)
    {
        var navigationButtons = new[] { DashboardButton, LedgerButton, SettingsButton };

        foreach (var button in navigationButtons)
        {
            button.Background =
                button == selectedButton
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;

            button.Foreground =
                button == selectedButton
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.White;
        }
    }

    private void ShowCityDetail(WatchlistEntryDto entry)
    {
        PageTitleText.Text = entry.City.Name;

        PageContent.Content = new CityDetailView(
            _profile,
            entry,
            _watchlistService,
            _eventLedgerService,
            ShowDashboard
        );
    }

    private async void SidebarSwitchProfileButton_Click(object sender, RoutedEventArgs e)
    {
        await _switchProfile();
    }
}
