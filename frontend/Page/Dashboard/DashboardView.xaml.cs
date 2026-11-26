using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ClimaPulse.Contracts;
using ClimaPulse.Desktop.Controls;
using ClimaPulse.Desktop.Formatting;
using ClimaPulse.Desktop.Page.Dashboard.AddCity;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Page;

public partial class DashboardView : UserControl
{
    private readonly ProfileDto _profile;
    private readonly IWatchlistService _watchlistService;
    private readonly ICityService _cityService;
    private readonly IDashboardService _dashboardService;
    private readonly IMonitoringService _monitoringService;
    private readonly IBaselineService _baselineService;
    private readonly Action<DashboardCityDto> _openCityDetail;

    private DashboardDto? _dashboard;
    private MetricType _metric = MetricType.Temperature;
    private readonly HashSet<int> _failedCityIds = [];

    private ObservableCollection<DashboardCityCard> Cards { get; } = [];

    public DashboardView(
        ProfileDto profile,
        ICityService cityService,
        IWatchlistService watchlistService,
        IDashboardService dashboardService,
        IMonitoringService monitoringService,
        IBaselineService baselineService,
        Action<DashboardCityDto> openCityDetail
    )
    {
        _profile = profile;
        _cityService = cityService;
        _watchlistService = watchlistService;
        _dashboardService = dashboardService;
        _monitoringService = monitoringService;
        _baselineService = baselineService;
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
        if (_dashboard is null)
        {
            await LoadCitiesAsync();
        }

        if (_dashboard is not null)
        {
            await RefreshCitiesAsync(_dashboard.Cities.Select(card => card.City).ToList());
        }
    }

    private async void AddCityButton_Click(object sender, RoutedEventArgs e)
    {
        var addCityWindow = new AddCityWindow(
            _profile,
            _cityService,
            _watchlistService,
            _baselineService
        )
        {
            Owner = Window.GetWindow(this),
        };

        if (addCityWindow.ShowDialog() == true)
        {
            await LoadCitiesAsync();
        }
    }

    private void MetricFilter_Click(object sender, RoutedEventArgs e)
    {
        _metric = sender == RainFilterButton ? MetricType.Precipitation
            : sender == WindFilterButton ? MetricType.WindSpeed
            : MetricType.Temperature; // All shows Temperature as the main metric

        foreach (var button in new[] { AllFilterButton, TemperatureFilterButton, RainFilterButton, WindFilterButton })
        {
            var active = button == sender;
            button.Background = active ? ActiveFilterBackground : System.Windows.Media.Brushes.White;
            button.BorderBrush = active ? ActiveFilterForeground : InactiveFilterBorder;
            button.Foreground = active ? ActiveFilterForeground : System.Windows.Media.Brushes.Black;
        }

        ShowCards();
    }

    private async void CityCard_ActionClicked(object? sender, EventArgs e)
    {
        if (sender is not CityCard { Entry: DashboardCityDto card })
        {
            return;
        }

        if (await BaselineImport.RunAsync(_baselineService, card.City))
        {
            BaselineImport.Visibility = Visibility.Collapsed;

            // A new baseline does not re-score the cached reading, so refresh the city again.
            await RefreshCitiesAsync([card.City]);
        }
    }

    /// <summary>One call per city, one at a time; a failing city does not stop the others (contract 7.3).</summary>
    private async Task RefreshCitiesAsync(IReadOnlyList<CityDto> cities)
    {
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Refreshing…";

        var errors = new List<string>();
        DateTime? refreshedAt = null;

        foreach (var city in cities)
        {
            try
            {
                refreshedAt = (await _monitoringService.RefreshCityAsync(city.Id)).RefreshedAt;
                _failedCityIds.Remove(city.Id);
            }
            catch (UpstreamException ex)
            {
                // Only an Open-Meteo failure marks the card as Error.
                _failedCityIds.Add(city.Id);
                errors.Add($"{city.Name}: {ex.Message}");
            }
            catch (ClimaPulseException ex)
            {
                // For example a baseline import still running (Conflict): keep the card as it is.
                errors.Add($"{city.Name}: {ex.Message}");
            }
        }

        await LoadCitiesAsync();
        RefreshButton.Content = "Refresh all";

        if (errors.Count > 0)
        {
            ErrorTitleText.Text = "Some cities could not be refreshed";
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorBanner.Visibility = Visibility.Visible;
        }

        if (refreshedAt is { } time)
        {
            UpdatedText.Text = $"Updated {time.ToLocalTime():HH:mm}";
        }
    }

    private async Task LoadCitiesAsync()
    {
        LoadingPanel.Visibility = Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ErrorBanner.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            _dashboard = await _dashboardService.GetAsync(_profile.Id);

            var count = _dashboard.Cities.Count;
            CitySummaryText.Text =
                $"{count} {(count == 1 ? "city" : "cities")}, worst first. Today compared with the 1995–2024 normal.";

            var lastRefresh = _dashboard.Cities.Max(card => card.LastRefreshedAt);
            UpdatedText.Text = lastRefresh is { } time
                ? $"Updated {time.ToLocalTime():HH:mm}"
                : "Not refreshed yet";

            ShowCards();
        }
        catch (ClimaPulseException ex)
        {
            ErrorTitleText.Text = "Dashboard data is unavailable";
            ErrorText.Text = ex.Message;
            ErrorBanner.Visibility = Visibility.Visible;

            CitySummaryText.Text = "Your saved cities could not be loaded.";
        }
        catch (Exception)
        {
            ErrorTitleText.Text = "Dashboard data is unavailable";
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

    private void ShowCards()
    {
        if (_dashboard is null)
        {
            return;
        }

        Cards.Clear();

        // Already sorted worst tier first by the backend.
        foreach (var city in _dashboard.Cities)
        {
            Cards.Add(ToCard(city));
        }

        EmptyPanel.Visibility = Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsScrollViewer.Visibility = Cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private DashboardCityCard ToCard(DashboardCityDto city)
    {
        var refreshed = city.LastRefreshedAt is not null;

        var footer = string.Join(
            " · ",
            MetricDisplay.All
                .Where(metric => metric != _metric)
                .Select(metric =>
                    $"{MetricDisplay.Name(metric)}: {(refreshed ? MetricDisplay.ShortStatus(Find(city, metric)) : "—")}"
                )
        );

        DashboardCityCard Card(string primary, string secondary, string tier = "", double? z = null, string action = "") =>
            new(city, city.City.Name, FormatLocation(city.City), tier, primary, secondary, footer, z, action);

        if (_failedCityIds.Contains(city.City.Id))
        {
            return Card("Error", "This city could not be refreshed. Showing no reading. Try Refresh all again.");
        }

        if (!refreshed)
        {
            return Card("Not refreshed yet", "Press Refresh all to compare today with the 30-year normal.");
        }

        var reading = Find(city, _metric);

        switch (reading?.DataStatus)
        {
            case DataStatus.Ok:
                var unit = _profile.PreferredTemperatureUnit;
                var month = MetricDisplay.MonthName(
                    MetricDisplay.CityMonth(reading.ObservedAt ?? city.LastRefreshedAt!.Value, city.City)
                );
                var normal = reading.BaselineMean is { } mean ? MetricDisplay.Value(_metric, mean, unit) : "—";

                return Card(
                    reading.ObservedValue is { } value ? MetricDisplay.Value(_metric, value, unit) : "—",
                    $"z {MetricDisplay.ZScore(reading.ZScore ?? 0)} · Normal for {month}: {normal}",
                    reading.Tier?.ToString() ?? "",
                    reading.ZScore
                );

            case DataStatus.NoBaseline:
                return Card(
                    "Baseline not ready",
                    "Build the 30-year baseline to score this city.",
                    action: "Build baseline"
                );

            case DataStatus.ZeroVariance:
                return Card("Not enough variation to score", "The 30-year normal for this month never changes.");

            default:
                return Card("No data for today", "Open-Meteo returned no value for this metric.");
        }
    }

    private static MetricReadingDto? Find(DashboardCityDto city, MetricType metric) =>
        city.Metrics.FirstOrDefault(reading => reading.Metric == metric);

    private static string FormatLocation(CityDto city)
    {
        return string.IsNullOrWhiteSpace(city.AdminRegion)
            ? city.CountryCode
            : $"{city.AdminRegion}, {city.CountryCode}";
    }

    private void CityCard_Click(object? sender, EventArgs e)
    {
        if (sender is CityCard { Entry: DashboardCityDto city })
        {
            _openCityDetail(city);
        }
    }

    private static readonly System.Windows.Media.Brush ActiveFilterBackground =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE4, 0xF3, 0xF7));

    private static readonly System.Windows.Media.Brush ActiveFilterForeground =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x07, 0x56, 0x6B));

    private static readonly System.Windows.Media.Brush InactiveFilterBorder =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB5, 0xC5, 0xCC));

    private sealed record DashboardCityCard(
        DashboardCityDto Entry,
        string CityName,
        string Location,
        string Tier,
        string PrimaryText,
        string SecondaryText,
        string FooterText,
        double? ZScore,
        string ActionText
    );
}
