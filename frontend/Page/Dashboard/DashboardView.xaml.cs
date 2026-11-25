using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Controls;
using ClimaPulse.Desktop.Page.Dashboard.AddCity;

namespace ClimaPulse.Desktop.Page;

public partial class DashboardView : UserControl
{
    private readonly ProfileDto _profile;
    private readonly IWatchlistService _watchlistService;
    private readonly ICityService _cityService;
    private readonly Action<WatchlistEntryDto> _openCityDetail;

    private ObservableCollection<DashboardCityCard> Cards { get; } = [];

    public DashboardView(
        ProfileDto profile,
        ICityService cityService,
        IWatchlistService watchlistService,
        Action<WatchlistEntryDto> openCityDetail
    )
    {
        _profile = profile;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _openCityDetail = openCityDetail;

        InitializeComponent();

        CardsList.ItemsSource = Cards;

        Loaded += DashboardView_Loaded;
    }

    private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCitiesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadCitiesAsync();
    }

    private async void AddCityButton_Click(object sender, RoutedEventArgs e)
    {
        var addCityWindow = new AddCityWindow(_profile, _cityService, _watchlistService)
        {
            Owner = Window.GetWindow(this),
        };

        if (addCityWindow.ShowDialog() == true)
        {
            await LoadCitiesAsync();
        }
    }

    private async Task LoadCitiesAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        CardsScrollViewer.Visibility = Visibility.Collapsed;
        ErrorBanner.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            var watchlist = await _watchlistService.ListAsync(_profile.Id);

            Cards.Clear();

            foreach (var entry in watchlist)
            {
                Cards.Add(
                    new DashboardCityCard(entry, entry.City.Name, FormatLocation(entry.City))
                );
            }

            CitySummaryText.Text =
                Cards.Count == 1
                    ? "1 city. Baseline data is still being prepared."
                    : $"{Cards.Count} cities. Baseline data is still being prepared.";

            UpdatedText.Text = $"Updated {DateTime.Now:HH:mm}";

            if (Cards.Count == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
            }
            else
            {
                CardsScrollViewer.Visibility = Visibility.Visible;
            }
        }
        catch (ClimaPulseException ex)
        {
            ErrorText.Text = ex.Message;
            ErrorBanner.Visibility = Visibility.Visible;

            CitySummaryText.Text = "Your saved cities could not be loaded.";
        }
        catch (Exception)
        {
            ErrorText.Text =
                "Check that PostgreSQL is running and the connection setting is correct.";

            ErrorBanner.Visibility = Visibility.Visible;

            CitySummaryText.Text = "Your saved cities could not be loaded.";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
        }
    }

    private static string FormatLocation(CityDto city)
    {
        return string.IsNullOrWhiteSpace(city.AdminRegion)
            ? city.CountryCode
            : $"{city.AdminRegion}, {city.CountryCode}";
    }

    private void CityCard_Click(object? sender, EventArgs e)
    {
        if (sender is CityCard { Entry: WatchlistEntryDto entry })
        {
            _openCityDetail(entry);
        }
    }

    private sealed record DashboardCityCard(
        WatchlistEntryDto Entry,
        string CityName,
        string Location
    );
}
